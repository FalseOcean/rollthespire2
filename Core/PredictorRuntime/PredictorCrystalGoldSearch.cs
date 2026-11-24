using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

// One frozen board and ordered selection. The finite legal geometry frontier
// survives slices; neither an elapsed budget nor cancellation exhausts it.
// Bounds/memoization own absence only. Every improved endpoint is replayed
// through node inputs, rerolls and actual card insertion before publication.
internal sealed class PredictorCrystalGoldSearch
{
    private sealed record Move(PredictorCrystalStep Step,int[] Cells,UInt128 Mask,UInt128 Center);
    private sealed record Position(UInt128 Fog,ImmutableArray<PredictorCrystalStep> Path,string Word,int Gold);
    private readonly PredictorCrystalSnapshot _source;
    private readonly ImmutableArray<CrystalRewardOption> _selected;
    private readonly bool _avoidCurse,_includeRerolls,_canPad;
    private readonly UInt128[] _items;
    private readonly int[] _gold;
    private readonly UInt128 _initial,_occupied,_relevant;
    private readonly UInt128[] _residues=new UInt128[9];
    private readonly Move[] _moves;
    private readonly (UInt128 Cells,UInt128 Items,int Gold)[]? _goldSubsets;
    private CrystalReachabilityTable? _table;
    private PredictorCrystalOffsetBound? _languageBound;
    private Task<CrystalGoalSearchResult>? _languageTask;
    private CancellationTokenSource? _languageCancel;
    private readonly HashSet<int> _incompleteThresholds=[];
    private int _languageThreshold;
    private readonly int[] _levels;
    private int _upper;
    private readonly Action<Exception,string>? _recordFailure;
    private readonly HashSet<(UInt128 Items,UInt128 Centers,string Word,int Left)> _seen=[];
    private readonly HashSet<string> _endpoints=[];
    private readonly HashSet<string> _imported=[];
    private readonly IEnumerator<bool> _cursor;
    private PredictorCrystalSolution? _best;
    private int _bestGold=-1;
    private readonly int _rootUpper;
    private UInt128 _coverageFog,_coverage;
    private bool _hasCoverage;
    private long _nodes,_pruned,_merged;
    private double _seconds;
    private Exception? _failure;
    private bool _proofComplete; // Geometry/threshold proof closed; a worker end receipt may still be pending.
    internal bool Complete { get; private set; }
    internal CrystalDiscoveryProgress Progress=>new(_nodes,_pruned,_merged,_endpoints.Count,_seconds);

    internal PredictorCrystalGoldSearch(PredictorCrystalSnapshot source,ImmutableArray<CrystalRewardOption> selected,
        bool avoidCurse,bool includeRerolls=false,Action<Exception,string>? recordFailure=null)
    {
        _source=source;_selected=selected.IsDefault?[]:selected;_avoidCurse=avoidCurse;
        _includeRerolls=includeRerolls && PredictorSettlementEffects.Has(source.State,"DRIFTWOOD");
        _recordFailure=recordFailure;
        _items=source.Items.Select(item=>{
            UInt128 mask=0;
            if(item.X>=0) for(int x=item.X;x<item.X+item.Width;x++) for(int y=item.Y;y<item.Y+item.Height;y++)
                mask|=(UInt128)1<<(x*11+y);
            return mask;
        }).ToArray();
        // Crystal is an event reward: combat-only gold additions never apply.
        // Generate's MIDAS late modifier doubles every original Gold reward.
        int multiplier=1;
        foreach(var modifier in source.Context.Modifiers.Where(m=>m.Key.Entry=="MIDAS")) multiplier=checked(multiplier*2);
        _gold=source.Items.Select(item=>checked(multiplier*item.Subscriptions*(item.Kind switch {
            "GOLD_SMALL"=>10,"GOLD_BIG"=>30,_=>0 }))).ToArray();
        UInt128 initial=0;
        for(int i=0;i<121;i++)
        {
            if(source.Hidden[i]) initial|=(UInt128)1<<i;
            _residues[(i/11%3)*3+i%11%3]|=(UInt128)1<<i;
        }
        _initial=initial;_occupied=_items.Aggregate((UInt128)0,(a,m)=>a|m);
        _canPad=Bits(initial&~_occupied)>=source.Remaining;
        _relevant=Enumerable.Range(0,_items.Length).Where(i=>!_selected.IsEmpty || _gold[i]>0 ||
            avoidCurse && source.Items[i].Kind=="CURSE").Aggregate((UInt128)0,(a,i)=>a|_items[i]);
        _moves=(from cell in Enumerable.Range(0,121) where source.Hidden[cell]
            from tool in new[]{PredictorCrystalTool.Big,PredictorCrystalTool.Small}
            let cells=PredictorCrystalSearch.Cells(cell/11,cell%11,tool).Select(c=>c.X*11+c.Y).ToArray()
            select new Move(new(cell/11,cell%11,tool),cells,
                cells.Aggregate((UInt128)0,(a,c)=>a|((UInt128)1<<c)),(UInt128)1<<cell)).ToArray();
        int gold=source.Revealed.Sum(i=>checked(multiplier*(source.Items[i].Kind switch {
            "GOLD_SMALL"=>10,"GOLD_BIG"=>30,_=>0 })));
        var root=new Position(initial,[],"",gold);
        var goldItems=Enumerable.Range(0,_items.Length).Where(i=>_gold[i]>0 && (initial&_items[i])!=0).ToArray();
        if(goldItems.Length<=12 && _items.Length<=128)
        {
            var subsets=new (UInt128 Cells,UInt128 Items,int Gold)[1<<goldItems.Length];
            for(int mask=1;mask<subsets.Length;mask++)
            {
                int bit=BitOperations.TrailingZeroCount((uint)mask),item=goldItems[bit];
                var prior=subsets[mask&(mask-1)];
                subsets[mask]=(prior.Cells|_items[item],prior.Items|((UInt128)1<<item),checked(prior.Gold+_gold[item]));
            }
            _goldSubsets=subsets.OrderByDescending(s=>s.Gold).ToArray();
        }
        _rootUpper=Upper(root);
        _upper=Math.Max(0,_rootUpper);
        _levels=(_goldSubsets?.Select(s=>checked(gold+s.Gold))??[]).Where(v=>v<=_upper).Distinct().OrderDescending().ToArray();
        _cursor=Visit(root).GetEnumerator();
    }

    // Candidate compilation may supply its frozen optimistic offset table
    // later. Startup and every empty-target proof remain geometry-only.
    internal void UseRewardTable(CrystalReachabilityTable table)
    {
        _table=table;
        // A threshold compiler must not hold the candidate compiler's lock.
        // Only immutable offset rows are shared between the two proof questions.
        if(!_selected.IsEmpty && _levels.Length>0) _languageBound??=new(_source,table,_avoidCurse);
    }

    // Removing all take predicates enlarges the legal geometry domain, even
    // when obtain hooks make arbitrary target-subset projections unsound. An
    // empty query's acknowledged optimum is therefore an upper bound for every
    // ordered selection on this exact frozen source and action domain.
    internal void UseUnconditionalUpperBound(PredictorCrystalGoldSearch donor)
    {
        if(ReferenceEquals(this,donor) || !donor.Complete || !donor._selected.IsEmpty ||
            donor._best==null || Volatile.Read(ref donor._failure)!=null ||
            !ReferenceEquals(_source,donor._source) || _avoidCurse!=donor._avoidCurse ||
            _includeRerolls!=donor._includeRerolls) return;
        int upper=donor._bestGold;
        if(_best!=null && _bestGold>upper) throw new InvalidOperationException("CrystalGoldConflictingUpperBound");
        _upper=Math.Min(_upper,upper);
    }

    internal CrystalOptionProjection Advance(TimeSpan budget,CrystalOptionProjection? known=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(Volatile.Read(ref _failure) is {} failure) throw new InvalidOperationException("CrystalGoldSearchFailed",failure);
        var watch=Stopwatch.StartNew();
        try
        {
            if(known?.SelectedPlan is {} witness && _imported.Add(PathKey(witness.Steps)))
                Consider(witness.Steps);
            if(_best!=null && _bestGold==_upper) Finish();
            AdvanceLanguage(budget,watch,token);
            // A completed reward picker is outside this board-click query.
            // Do not claim a new proof from a reconstructed empty event picker.
            while(!_proofComplete && _source.Remaining>0 && watch.Elapsed<budget)
            {
                token.ThrowIfCancellationRequested();
                // The threshold compiler owns a separate bound and reads the
                // same frozen table. Its pending task must not suspend this
                // finite geometry frontier or consume its slice by waiting.
                if(_languageTask is {IsCompleted:true}) AdvanceLanguage(budget,watch,token);
                if(_proofComplete) break;
                if(!_cursor.MoveNext()) {Finish();break;}
                if(_best!=null && _bestGold==_upper) Finish();
            }
            if(_proofComplete) ConsumeRetiredThreshold(budget,watch,token);
            else AdvanceLanguage(budget,watch,token);
            if(Volatile.Read(ref _failure) is {} concurrentFailure)
                throw new InvalidOperationException("CrystalGoldSearchFailed",concurrentFailure);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) {throw;}
        catch(Exception ex) {_failure=ex;throw;}
        finally {_seconds+=watch.Elapsed.TotalSeconds;}
        return new(known?.Available??ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,
            _best,Math.Max(0,_bestGold)) {GoldComplete=Complete,GoldUpperBound=Complete && _best!=null?_bestGold:_upper,
                SacrificeRelics=(known?.SacrificeRelics??ImmutableHashSet<RolltheSpire2.Core.Identity.ModelKey>.Empty)
                    .Union(_best?.SacrificeRelics??ImmutableHashSet<RolltheSpire2.Core.Identity.ModelKey>.Empty)};
    }

    private void Finish()
    {
        if(_proofComplete) return;
        _proofComplete=true;_cursor.Dispose();_languageCancel?.Cancel();
        Complete=_languageTask==null;
    }

    private void ConsumeRetiredThreshold(TimeSpan budget,Stopwatch watch,CancellationToken token)
    {
        if(_languageTask==null) {Complete=true;return;}
        var task=_languageTask;
        if(!task.IsCompleted)
        {
            // Only the end acknowledgement remains: spend this slice's unused
            // budget waiting instead of hot-looping identical UI publications.
            // Active geometry never waits here; zero-budget polls stay free.
            var remaining=budget-watch.Elapsed;
            if(remaining<=TimeSpan.Zero) return;
            Task.WaitAny([task],Math.Max(1,(int)Math.Min(int.MaxValue,remaining.TotalMilliseconds)),token);
            if(!task.IsCompleted) return;
        }
        var cancel=_languageCancel!;int threshold=_languageThreshold;
        _languageTask=null;_languageCancel=null;
        try
        {
            var result=task.GetAwaiter().GetResult();
            if(result.Plan is {} plan)
            {
                int provedGold=_bestGold;
                int? actual=Consider(plan.Steps,verifyExisting:true);
                if(actual==null || actual<threshold) throw new InvalidOperationException("CrystalGoldThresholdWitnessMismatch");
                if(actual>provedGold) throw new InvalidOperationException("CrystalGoldConflictingThresholdProof");
            }
            if(result.Verdict==CrystalOffsetVerdict.Impossible && _best!=null && _bestGold>=threshold)
                throw new InvalidOperationException("CrystalGoldConflictingThresholdProof");
        }
        catch(OperationCanceledException) when(cancel.IsCancellationRequested) {token.ThrowIfCancellationRequested();}
        finally {cancel.Dispose();}
        // Keep certainty pending until the canceled worker has acknowledged its
        // end. A real late proof/failure is consumed on this same owning thread,
        // before callers can stop polling after observing GoldComplete.
        Complete=true;
    }

    private void AdvanceLanguage(TimeSpan budget,Stopwatch watch,CancellationToken token)
    {
        if(_proofComplete || _languageBound==null || _table==null || _table.IncludesRerolls!=_includeRerolls ||
            _selected.IsEmpty || !_source.PlacedAllItems || _source.Remaining<=0) return;
        while(!Complete)
        {
            if(_languageTask!=null && _languageTask.IsCompleted)
            {
                var task=_languageTask;var cancel=_languageCancel!;int threshold=_languageThreshold;
                _languageTask=null;_languageCancel=null;
                CrystalGoalSearchResult? result=null;
                try {result=task.GetAwaiter().GetResult();}
                catch(OperationCanceledException) when(cancel.IsCancellationRequested) {token.ThrowIfCancellationRequested();}
                finally {cancel.Dispose();}
                if(result!=null)
                {
                    _nodes+=result.RewardStates+result.GeometryNodes;_pruned+=result.LanguagePruned;
                    if(result.Plan is {} plan)
                    {
                        Consider(plan.Steps);
                        if(_bestGold<threshold) throw new InvalidOperationException("CrystalGoldThresholdWitnessMismatch");
                    }
                    if(result.Verdict==CrystalOffsetVerdict.Impossible)
                    {
                        if(_best!=null && _bestGold>=threshold) throw new InvalidOperationException("CrystalGoldConflictingThresholdProof");
                        if(threshold==0) {Finish();return;}
                        // An empty-query certificate may have tightened the
                        // bound while this older threshold was still running.
                        _upper=Math.Min(_upper,_levels.Where(v=>v<threshold).DefaultIfEmpty(0).Max());
                    }
                    else if(result.Verdict==CrystalOffsetVerdict.Incomplete) _incompleteThresholds.Add(threshold);
                    if(_best!=null && _bestGold==_upper) {Finish();return;}
                }
            }
            if(_languageTask==null)
            {
                int? threshold=_levels.Where(v=>v<=_upper && v>_bestGold && !_incompleteThresholds.Contains(v)).Select(v=>(int?)v).FirstOrDefault();
                if(threshold==null || watch.Elapsed>=budget) return;
                _languageThreshold=threshold.Value;
                _languageCancel=CancellationTokenSource.CreateLinkedTokenSource(token);var workerToken=_languageCancel.Token;
                var table=_table;var bound=_languageBound;int minimum=threshold.Value;
                _languageTask=Task.Run(()=>
                {
                    try
                    {
                        // One bounded threshold task survives all UI slices.
                        // A work limit stays unknown and is not restarted on the
                        // next slice; the original finite geometry cursor remains.
                        return PredictorCrystalGoalLanguage.Solve(_source,table,_selected,_avoidCurse,TimeSpan.FromSeconds(30),
                            stateLimit:500_000,token:workerToken,sharedBound:bound,minimumGold:minimum);
                    }
                    catch(OperationCanceledException) when(workerToken.IsCancellationRequested) {throw;}
                    catch(Exception ex)
                    {
                        Volatile.Write(ref _failure,ex);
                        // The observer records copied evidence only. A logging
                        // fault never replaces the original execution failure.
                        try {_recordFailure?.Invoke(ex,$"minimumGold={minimum};selection={string.Join('|',_selected.Select(s=>s.CacheKey))}");} catch { }
                        throw;
                    }
                },workerToken);
            }
            // Poll only. The caller advances geometry within this same budget
            // and consumes a completed threshold before its next cursor tick.
            if(!_languageTask.IsCompleted) return;
        }
    }

    private IEnumerable<bool> Visit(Position node)
    {
        _nodes++;
        // Cooperate before all per-node work. This also lets a zero-work slice
        // retain precisely the same frontier without an incomplete proof claim.
        yield return false;
        int upper=Math.Min(Upper(node),_upper);
        if(upper<0 || _best!=null && upper<=_bestGold) {_pruned++;yield break;}
        int left=_source.Remaining-node.Path.Length;
        if(left==0)
        {
            Consider(node.Path);yield break;
        }
        // Pad prepends only initially blank Small clicks, so its endpoint gold
        // is exactly node.Gold. Avoid constructing/replaying a candidate which
        // cannot improve the already verified incumbent.
        if(_canPad && (_best==null || node.Gold>_bestGold) && Pad(node) is {} padded) Consider(padded);
        if(_best!=null && upper<=_bestGold) {_pruned++;yield break;}
        UInt128 live=node.Fog&_relevant;
        UInt128 centers=node.Fog;
        if(_canPad)
        {
            UInt128 capable=0;
            foreach(var move in _moves) if((move.Mask&live)!=0) capable|=move.Center;
            centers &= capable;
        }
        // No padding guarantee: full fog and exact remaining clicks retain
        // every no-progress action and its effect on subsequent legal centers.
        if(!_seen.Add((_canPad?live:node.Fog,centers,node.Word,left))) {_merged++;yield break;}
        var next=new List<Position>();
        foreach(var move in _moves)
        {
            if((node.Fog&move.Center)==0 || _canPad && (move.Mask&live)==0) continue;
            if(Apply(node,move) is {} child) next.Add(child);
        }
        // Ordering changes latency only; all surviving siblings remain in the
        // retained frontier. No beam width or state limit discards a branch.
        foreach(var child in next.OrderByDescending(n=>n.Gold).ThenByDescending(n=>Bits(live&~n.Fog)))
            foreach(bool tick in Visit(child)) yield return tick;
    }

    private Position? Apply(Position node,Move move)
    {
        UInt128 fog=node.Fog;int gold=node.Gold;string word=node.Word;
        foreach(int cell in move.Cells)
        {
            UInt128 bit=(UInt128)1<<cell;if((fog&bit)==0) continue;
            fog &= ~bit;int item=_source.Occupancy[cell];
            if(item<0 || (fog&_items[item])!=0) continue;
            if(_avoidCurse && _source.Items[item].Kind=="CURSE") {_pruned++;return null;}
            gold=checked(gold+_gold[item]);
            // Full ordered physical completions preserve callback words,
            // subscription multiplicity and curse insertion, including a curse
            // with zero subscriptions. No selected target fixes one word.
            if(!_selected.IsEmpty) word+=$"{item},";
        }
        return new(fog,node.Path.Add(move.Step),word,gold);
    }

    private int Upper(Position node)
    {
        int left=_source.Remaining-node.Path.Length,total=node.Gold;
        if(!TargetsPossible(node,out UInt128 required)) return -1;
        if(_goldSubsets!=null)
        {
            UInt128 liveItems=0;
            for(int i=0;i<_gold.Length;i++) if(_gold[i]>0 && (node.Fog&_items[i])!=0) liveItems|=(UInt128)1<<i;
            foreach(var subset in _goldSubsets)
                if((subset.Items&liveItems)==subset.Items && MayCover((subset.Cells|required)&node.Fog,node.Fog,left))
                    return checked(node.Gold+subset.Gold);
            return node.Gold;
        }
        for(int i=0;i<_gold.Length;i++)
        {
            UInt128 need=node.Fog&_items[i];
            if(_gold[i]>0 && need!=0 && MayCover(need,node.Fog,left)) total=checked(total+_gold[i]);
        }
        return total;
    }
    private bool MayCover(UInt128 need,UInt128 fog,int left)
    {
        if(need==0) return true;
        if(left<=0 || Bits(need)>9*left) return false;
        foreach(var residue in _residues) if(Bits(need&residue)>left) return false;
        if(left==1) return _moves.Any(m=>(fog&m.Center)!=0 && (need&~m.Mask)==0);
        // Each still-hidden cell needs some currently legal center. Legal
        // centers only disappear, so their optimistic union bounds the future.
        // One Upper call asks about many item/subset masks at the same fog.
        // Keep only the last fog's union: it depends on no target or click count,
        // and avoids a new unbounded per-position cache.
        if(!_hasCoverage || _coverageFog!=fog)
        {
            UInt128 coverage=0;
            foreach(var move in _moves) if((fog&move.Center)!=0) coverage|=move.Mask;
            _coverageFog=fog;_coverage=coverage;_hasCoverage=true;
        }
        return (need&~_coverage)==0;
    }

    private ImmutableArray<PredictorCrystalStep>? Pad(Position node)
    {
        if(!_canPad) return null;
        // Enough initially blank cells for the original budget guarantees at
        // least Remaining-productiveCount blank cells after excluding centers.
        // Small clicks inserted first cannot complete an item or consume a
        // productive center, even when a later Big footprint covers the pad.
        UInt128 blank=_initial&~_occupied;
        foreach(var step in node.Path) blank &= ~((UInt128)1<<(step.X*11+step.Y));
        var prefix=ImmutableArray.CreateBuilder<PredictorCrystalStep>();
        for(int i=0;i<121 && prefix.Count+node.Path.Length<_source.Remaining;i++)
            if((blank&((UInt128)1<<i))!=0) prefix.Add(new(i/11,i%11,PredictorCrystalTool.Small));
        if(prefix.Count+node.Path.Length!=_source.Remaining) throw new InvalidOperationException("CrystalGoldPaddingContractChanged");
        return prefix.ToImmutable().AddRange(node.Path);
    }

    private int? Consider(ImmutableArray<PredictorCrystalStep> path,bool verifyExisting=false)
    {
        if(path.Length!=_source.Remaining) return null;
        var node=new Position(_initial,[],"",InitialGold());
        foreach(var step in path)
        {
            var move=_moves.FirstOrDefault(m=>m.Step==step);
            if(move==null || (node.Fog&move.Center)==0 || Apply(node,move) is not {} next) return null;
            node=next;
        }
        if(!verifyExisting && _best!=null && node.Gold<=_bestGold) return null;
        // For empty targets all words with the same geometric gold amount are
        // interchangeable. Selected targets retain the entire completion word.
        string endpoint=_selected.IsEmpty?node.Gold.ToString():node.Word;
        if(!_endpoints.Add(endpoint) && !verifyExisting) return null;
        var run=PredictorCrystalExplorer.Replay(_source,path);
        int actual=run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
        if(actual!=node.Gold) throw new InvalidOperationException("CrystalGoldRewardAmountContractChanged");
        PredictorCrystalSolution? proof;
        if(_selected.IsEmpty && run.Phase==PredictorPhase.Complete && run.Rewards.IsEmpty)
            proof=new("Found",_nodes,path,[]);
        else
        {
            var row=new CrystalRewardRoute(path,run.Rewards) {EnchantmentTargets=CrystalRewardOption.UsesEnchantments(_source.State)};
            if(_includeRerolls && !_selected.IsEmpty) row=PredictorCrystalExplorer.WithRerolls(row,run);
            proof=PredictorCrystalExplorer.VerifySelection(_source,row,_selected);
        }
        if(proof==null) return null;
        if(actual>_upper) throw new InvalidOperationException("CrystalGoldConflictingUpperBound");
        if(_best==null || actual>_bestGold) {_best=proof;_bestGold=actual;}
        return actual;
    }
    private int InitialGold()=>_source.Revealed.Sum(i=>_source.Items[i].Subscriptions==0?0:_gold[i]/_source.Items[i].Subscriptions);

    private bool TargetsPossible(Position node,out UInt128 required)
    {
        required=0;if(_selected.IsEmpty) return true;
        int left=_source.Remaining-node.Path.Length;
        var completed=_source.Revealed.ToBuilder();
        foreach(var text in node.Word.Split(',',StringSplitOptions.RemoveEmptyEntries))
        {
            int item=int.Parse(text);
            for(int n=0;n<_source.Items[item].Subscriptions;n++) completed.Add(item);
        }
        var slots=new List<(string Kind,int Minimum,int Maximum,UInt128 Need)>();
        int potions=0,core=0;
        foreach(int item in completed)
        {
            string kind=_source.Items[item].Kind;
            if(kind.StartsWith("POTION_",StringComparison.Ordinal)) {slots.Add((kind,potions,potions,0));potions++;}
            else
            {
                if(kind.StartsWith("CARD_",StringComparison.Ordinal) || kind=="RELIC") slots.Add((kind,core,core,0));
                core=checked(core+CrystalReachabilityTable.Draws(kind));
            }
        }
        var future=new List<(PredictorCrystalItem Item,UInt128 Need)>();int maxPotions=potions,maxCore=core;
        for(int i=0;i<_items.Length;i++)
        {
            var item=_source.Items[i];UInt128 need=node.Fog&_items[i];
            if(need==0 || item.Kind=="CURSE" || !MayCover(need,node.Fog,left)) continue;
            future.Add((item,need));
            if(item.Kind.StartsWith("POTION_",StringComparison.Ordinal)) maxPotions=checked(maxPotions+item.Subscriptions);
            else maxCore=checked(maxCore+item.Subscriptions*CrystalReachabilityTable.Draws(item.Kind));
        }
        foreach(var (item,need) in future)
        {
            bool potion=item.Kind.StartsWith("POTION_",StringComparison.Ordinal);
            if(!potion && !item.Kind.StartsWith("CARD_",StringComparison.Ordinal) && item.Kind!="RELIC") continue;
            for(int n=0;n<item.Subscriptions;n++) slots.Add((item.Kind,potion?potions:core,
                potion?maxPotions-1:maxCore-(item.Kind=="RELIC"?1:6),need));
        }
        var goals=_selected.Where(g=>g.Kind==PredictorRewardKind.Card)
            .Concat(_selected.Where(g=>g.Kind==PredictorRewardKind.Potion).Distinct()).ToArray();
        var edges=new List<int>[goals.Length];
        // Newly obtained curses may affect reward-generation state outside the
        // frozen offset table's premise. In that scope keep geometry only.
        bool offsets=_table!=null && (_avoidCurse || !_source.Items.Any(i=>i.Kind=="CURSE" && i.X>=0 && !i.Revealed));
        for(int n=0;n<goals.Length;n++)
        {
            var goal=goals[n];UInt128 intersection=UInt128.MaxValue;edges[n]=[];
            for(int s=0;s<slots.Count;s++)
            {
                var slot=slots[s];bool card=goal.Kind==PredictorRewardKind.Card;
                if(card?!slot.Kind.StartsWith("CARD_",StringComparison.Ordinal):!slot.Kind.StartsWith("POTION_",StringComparison.Ordinal)) continue;
                bool possible=true;
                if(offsets)
                {
                    var table=_table!;
                    if(card)
                    {
                        // The table filters source identity only. Offer upgrade,
                        // enchantment and obtain-hook outcomes stay with the
                        // typed take replay, rather than excluding a legal path
                        // from an acquired-card predicate here.
                        var predicate=goal with {UpgradeLevel=null,Enchantment=null};
                        possible=_includeRerolls && !table.IncludesRerolls ||
                            table.CardAt(slot.Kind,predicate,potions+slot.Minimum,maxPotions+slot.Maximum) ||
                            _includeRerolls && table.CardAt(slot.Kind,predicate,0,table.MaximumDraws-6);
                    }
                    else possible=table.PotionAt(slot.Kind,goal.Key,slot.Minimum,slot.Maximum);
                }
                if(possible) {edges[n].Add(s);intersection &= slot.Need;}
            }
            if(edges[n].Count==0) return false;
            required |= intersection;
        }
        var used=new bool[slots.Count];
        bool Match(int n)
        {
            if(n==edges.Length) return true;
            foreach(int slot in edges[n]) if(!used[slot]) {used[slot]=true;if(Match(n+1)) return true;used[slot]=false;}
            return false;
        }
        if(!Match(0)) return false;
        if(_selected.Any(g=>g.Kind==PredictorRewardKind.Relic))
        {
            var relicSlots=slots.Where(s=>s.Kind=="RELIC").ToArray();
            int count=Math.Max(1,_selected.Where(g=>g.Kind==PredictorRewardKind.Relic && g.Key.IsValid).Select(g=>g.Key).Distinct().Count());
            int gifts=PredictorCrystalWing.Active(_source.State)?
                (slots.Count(s=>s.Kind.StartsWith("CARD_",StringComparison.Ordinal))-goals.Count(g=>g.Kind==PredictorRewardKind.Card)+
                    PredictorCrystalWing.Parity(_source.State))/2:0;
            if(relicSlots.Length+gifts<count) return false;
            if(count>gifts)
            {
                UInt128 intersection=UInt128.MaxValue;foreach(var slot in relicSlots) intersection &= slot.Need;
                required |= intersection;
            }
        }
        return MayCover(required&node.Fog,node.Fog,left);
    }
    private static string PathKey(ImmutableArray<PredictorCrystalStep> steps)=>string.Join(';',steps.Select(s=>$"{s.X},{s.Y},{s.Tool}"));
    private static int Bits(UInt128 value)=>BitOperations.PopCount((ulong)value)+BitOperations.PopCount((ulong)(value>>64));
}
