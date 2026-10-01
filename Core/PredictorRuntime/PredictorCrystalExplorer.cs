using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalRewardOption(PredictorRewardKind Kind, ModelKey Key, int? UpgradeLevel = null)
{
    // Null remains identity-only for legacy/internal callers. Published card
    // candidates always carry an exact level, including zero (not upgraded).
    internal static CrystalRewardOption AnyRelic { get; } = new(PredictorRewardKind.Relic,default);
    internal bool MatchesCard(PredictorCard card) => Kind==PredictorRewardKind.Card && card.Key==Key &&
        (UpgradeLevel==null || card.UpgradeLevel==UpgradeLevel);
    internal bool Accepts(CrystalRewardOption candidate) => Kind==candidate.Kind && Key==candidate.Key &&
        (Kind!=PredictorRewardKind.Card || UpgradeLevel==null || UpgradeLevel==candidate.UpgradeLevel);
    internal bool Matches(PredictorReward reward) => reward.Kind==Kind &&
        (Key.IsValid ? reward.Key==Key : Kind==PredictorRewardKind.Relic && reward.Key.IsValid);
}
internal sealed record CrystalRewardRoute(ImmutableArray<PredictorCrystalStep> Steps, ImmutableArray<PredictorReward> Rewards);
internal sealed record CrystalOptionProjection(ImmutableDictionary<CrystalRewardOption, PredictorCrystalSolution> Available,
    PredictorCrystalSolution? SelectedPlan, int Gold);

// Resumable legal-board traversal. Production uses a selection-scoped query;
// the existing audit can still enumerate the full reward index. Partial results
// prove their positive entries only, never missing entries.
internal enum CrystalDiscoveryPhase { FindingPlans, Exhaustive, Complete }
internal readonly record struct CrystalDiscoveryProgress(long Examined, long Pruned, long Merged, int Orders, double Seconds);

internal sealed partial class PredictorCrystalExplorer
{
    // Native ToReward materializes ALL potions before other rewards Populate.
    // Preserve each phase's ordered word, but discard their interleaving. Curse
    // effects happen on physical completion, once regardless of subscriptions.
    private readonly record struct HistoryKey(int Potions, int Core, int NewCurses);
    private readonly Dictionary<(int Prefix,string Kind),int> _wordIds = [];
    private readonly List<(int Prefix,string Kind)> _words = [(0,"")];
    private readonly Dictionary<(int Prefix,int Suffix),int> _joins = [];
    private readonly PredictorCrystalSnapshot _source;
    private readonly Func<CrystalRewardRoute,ImmutableArray<CrystalRewardOption>,PredictorCrystalSolution?> _verifySelection;
    private readonly bool _avoidCurse;
    private readonly bool _useTwoStepCache;
    private readonly bool _discreteRewardBound;
    private readonly bool _potionScenarioBound;
    private readonly bool _targetDirected;
    private readonly bool _stopAfterPlan;
    private readonly PredictorCrystalOffsetBound? _offsetBound;
    private readonly PredictorCrystalIslands? _islands;
    private readonly int _potionScenarioLimit;
    private long _scenarioCount,_scenarioCoverRejected,_scenarioFallbacks;
    internal long ScenarioCount=>_scenarioCount;
    internal long ScenarioCoverRejected=>_scenarioCoverRejected;
    internal long ScenarioFallbacks=>_scenarioFallbacks;
    private long _boundChecks,_boundRejected,_boundTicks,_boundCacheHits;
    private readonly Dictionary<(UInt128 Need,HistoryKey History,int Left,CrystalRewardOption? Extra),bool> _discreteMemo=[];
    internal long BoundCacheHits=>_boundCacheHits;
    internal int BoundRootExcluded { get; }
    private readonly ImmutableArray<CrystalRewardOption> _rootExcluded=[];
    private bool _negativeComplete;
    // Negative evidence belongs to this exact selection and candidate lane.
    // A paused frontier cannot exclude its still-unwitnessed candidates.
    internal ImmutableArray<CrystalRewardOption> ExcludedCandidates=>
        _rootExcluded.AddRange(_negativeComplete?UnwitnessedUpper:[]);
    internal ImmutableArray<CrystalRewardOption> UnwitnessedUpper=>_queryCandidates.Where(c=>!_proven.ContainsKey(c)).ToImmutableArray();
    internal long BoundChecks=>_boundChecks;
    internal long BoundRejected=>_boundRejected;
    internal double BoundSeconds=>_boundTicks/(double)Stopwatch.Frequency;
    private readonly CrystalReachabilityTable? _reachability;
    private readonly ImmutableArray<CrystalRewardOption> _selected;
    private readonly ImmutableArray<CrystalRewardOption> _queryCandidates;
    private readonly ImmutableHashSet<CrystalRewardOption>? _candidateScope;
    private readonly Dictionary<CrystalRewardOption,PredictorCrystalSolution> _proven=[];
    private PredictorCrystalSolution? _queryPlan;
    private int _queryGold=-1;
    internal CrystalOptionProjection QueryProjection=>new(_proven.ToImmutableDictionary(),_queryPlan,Math.Max(0,_queryGold));
    private bool QuerySatisfied=>_reachability!=null && (_selected.IsEmpty || _queryPlan!=null) &&
        (_stopAfterPlan || _queryCandidates.All(_proven.ContainsKey));
    private readonly UInt128[] _items;
    private readonly (PredictorCrystalStep Step, int[] Cells, UInt128 Mask)[] _moves;
    private readonly (PredictorCrystalStep Step, int[] Cells, UInt128 Mask)[] _bigMoves = new (PredictorCrystalStep,int[],UInt128)[121],
        _smallMoves = new (PredictorCrystalStep,int[],UInt128)[121];
    private readonly Dictionary<UInt128,UInt128> _finishCenters = [];
    private sealed record FinalChoice((PredictorCrystalStep Step,int[] Cells,UInt128 Mask) Move, ImmutableArray<int> Items, HistoryKey Suffix);
    private readonly Dictionary<(UInt128 Need,UInt128 Centers),ImmutableArray<FinalChoice>> _finalChoices = [];
    private sealed record SuffixChoice(ImmutableArray<PredictorCrystalStep> Steps,ImmutableArray<int> Items,HistoryKey Key);
    private readonly Dictionary<(UInt128 Need,UInt128 Centers),ImmutableArray<SuffixChoice>> _twoStepChoices = [];
    private int _twoStepEntries;
    private readonly UInt128 _initialFog, _occupied;
    private readonly UInt128[] _covers;
    private readonly bool _alwaysCanPad;
    private readonly Dictionary<(UInt128 Need, int Left), bool> _coverMemo = [];
    private readonly HashSet<HistoryKey> _orders = [];
    private readonly HashSet<(UInt128, UInt128, HistoryKey, int, UInt128)> _visited = [];
    private readonly Dictionary<(UInt128 Need,HistoryKey Rewards,int Depth),List<UInt128>> _frontiers = [];
    private int _frontierEntries;
    private readonly IEnumerator<CrystalRewardRoute?> _cursor;
    private sealed record Position(UInt128 Fog, ImmutableArray<int> Order, ImmutableArray<PredictorCrystalStep> Path, int Score,
        UInt128 Centers = default, string OrderKey = "", HistoryKey RewardKey = default);
    private long _pruned, _merged;
    private double _seconds;
    private long _settlementTicks;
    internal CrystalDiscoveryProgress Progress => new(Examined, _pruned, _merged, _orders.Count, _seconds);
    private Exception? _failure;
    internal bool Complete { get; private set; }
    internal CrystalDiscoveryPhase Phase { get; private set; }
    internal long Examined { get; private set; }
    internal PredictorCrystalExplorer(PredictorCrystalSnapshot source, bool avoidCurse, bool exhaustiveOnly = false,
        bool useTwoStepCache = false,CrystalReachabilityTable? reachability=null,ImmutableArray<CrystalRewardOption> selected=default,bool discreteRewardBound=false,bool potionScenarioBound=false,int potionScenarioLimit=6,
        ImmutableArray<CrystalRewardOption> queryCandidates=default,bool targetDirected=false,PredictorCrystalIslands? islands=null,
        Func<CrystalRewardRoute,ImmutableArray<CrystalRewardOption>,PredictorCrystalSolution?>? verifySelection=null,bool stopAfterPlan=false,
        PredictorCrystalOffsetBound? offsetBound=null)
    {
        _source = source; _avoidCurse = avoidCurse;
        _verifySelection=verifySelection??((row,selection)=>VerifySelection(source,row,selection));
        _targetDirected=targetDirected;_islands=islands;
        _stopAfterPlan=stopAfterPlan;
        _offsetBound=offsetBound;
        // Kept for bounded audit comparisons. The native six-click workload
        // advances faster with one-step finishing than rebuilding uncached tails.
        _useTwoStepCache=useTwoStepCache;_discreteRewardBound=discreteRewardBound;
        _potionScenarioBound=potionScenarioBound;_potionScenarioLimit=potionScenarioLimit;
        if(potionScenarioLimit is <0 or >6 || discreteRewardBound && potionScenarioBound)
            throw new ArgumentException("CrystalBoundExperimentInvalid");
        _reachability=reachability;_selected=selected.IsDefault?[]:selected;
        _candidateScope=queryCandidates.IsDefault?null:queryCandidates.ToImmutableHashSet();
        _queryCandidates=reachability==null?[]:reachability.Candidates.Where(c=>!_selected.Contains(c) && (_candidateScope==null || _candidateScope.Contains(c))).ToImmutableArray();
        _items = source.Items.Select(item => {
            UInt128 mask = 0;
            if(item.X >= 0) for(int x=item.X;x<item.X+item.Width;x++) for(int y=item.Y;y<item.Y+item.Height;y++) mask |= (UInt128)1 << (x*11+y);
            return mask;
        }).ToArray();
        _moves = (from x in Enumerable.Range(0,11) from y in Enumerable.Range(0,11)
            where source.Hidden[x*11+y] from tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}
            let cells = PredictorCrystalSearch.Cells(x,y,tool).Select(c=>c.X*11+c.Y).ToArray()
            select (new PredictorCrystalStep(x,y,tool), cells, cells.Aggregate((UInt128)0,(mask,i)=>mask|((UInt128)1<<i)))).ToArray();
        UInt128 fog=0; for(int i=0;i<121;i++) if(source.Hidden[i]) fog |= (UInt128)1 << i;
        _initialFog=fog;
        foreach(var move in _moves) (move.Step.Tool==PredictorCrystalTool.Big?_bigMoves:_smallMoves)[move.Step.X*11+move.Step.Y]=move;
        _occupied=_items.Aggregate((UInt128)0,(mask,item)=>mask|item);
        _covers=_moves.Where(m=>m.Step.Tool==PredictorCrystalTool.Big).Select(m=>m.Mask).Distinct().ToArray();
        // If enough initial blank cells exist, prefix padding is always possible
        // after excluding every productive click center. Otherwise center history
        // is retained in the transposition key for exact padding feasibility.
        _alwaysCanPad=Bits(_initialFog & ~_occupied)>=source.Remaining;
        var initial=new Position(fog,source.Revealed,[],0,default,string.Join(',',source.Revealed),RewardKey(source.Revealed));
        // A first-plan probe asks only whether its selected goals coexist.
        // Filtering every possible third goal here wastes the probe's slice;
        // incidental positives are still harvested by actual row projection.
        if(!_stopAfterPlan && (_discreteRewardBound || _potionScenarioBound || _offsetBound!=null))
        {
            var before=_queryCandidates;
            _queryCandidates=_queryCandidates.Where(candidate=>QueryCanImprove(initial,candidate) &&
                OffsetMaySupport(_selected.Add(candidate))).ToImmutableArray();
            _rootExcluded=before.Except(_queryCandidates).ToImmutableArray();
            BoundRootExcluded=_rootExcluded.Length;
        }
        // The existing audit can exercise completeness without discovery heuristics
        // accidentally supplying a witness omitted by exhaustive traversal.
        Phase=exhaustiveOnly?CrystalDiscoveryPhase.Exhaustive:CrystalDiscoveryPhase.FindingPlans;
        _cursor=(exhaustiveOnly?Exhaustive(initial):Enumerate(initial)).GetEnumerator();
        if(!OffsetMaySupport(_selected))
        { Complete=true;_negativeComplete=true;Phase=CrystalDiscoveryPhase.Complete;_cursor.Dispose(); }
    }
    // Existing controlled source checks use this to compare optimistic root
    // decisions with independently enumerated legal suffixes.
    internal bool InitialMaySupport(CrystalRewardOption option)=>QueryCanImprove(
        new(_initialFog,_source.Revealed,[],0,default,string.Join(',',_source.Revealed),RewardKey(_source.Revealed)),option) &&
        OffsetMaySupport(_selected.Add(option));
    private bool OffsetMaySupport(ImmutableArray<CrystalRewardOption> goals)
    {
        if(_offsetBound==null) return true;
        long start=Stopwatch.GetTimestamp();_boundChecks++;
        try { bool possible=_offsetBound.Check(goals).MaySupport;if(!possible) _boundRejected++;return possible; }
        finally { _boundTicks+=Stopwatch.GetTimestamp()-start; }
    }
    internal ImmutableArray<CrystalRewardRoute> Advance(TimeSpan budget,CancellationToken token=default)
    {
        if(_failure!=null) throw new InvalidOperationException("CrystalDiscoveryFailed",_failure);
        var watch=Stopwatch.StartNew(); var rows=ImmutableArray.CreateBuilder<CrystalRewardRoute>();
        try
        {
            while(!Complete && !token.IsCancellationRequested && watch.Elapsed<budget)
            {
                if(QuerySatisfied) { Complete=true;Phase=CrystalDiscoveryPhase.Complete;_cursor.Dispose();break; }
                if(!_cursor.MoveNext()) { Complete=true;_negativeComplete=true; Phase=CrystalDiscoveryPhase.Complete; _cursor.Dispose(); break; }
                if(_cursor.Current is { } row) rows.Add(row);
            }
            return rows.ToImmutable();
        }
        catch(Exception ex) { _failure=ex; throw; } // a faulted iterator must never become an exhaustion proof
        finally { _seconds+=watch.Elapsed.TotalSeconds; }
    }

    // Optimistic cover only: initial Big centers may be reused, without future
    // center eligibility, curse or reward-order restrictions. Failure therefore
    // proves this item cannot complete; success still needs real legal replay.
    private bool CanCover(UInt128 need, int left)
    {
        if(need==0) return true;
        if(left<=0) return false;
        if(left==1) return FinishingBigCenters(need)!=0;
        if(Bits(need)<=left) return true;
        if(_coverMemo.TryGetValue((need,left),out bool cached)) return cached;
        var gains=_covers.Select(c=>c&need).Where(c=>c!=0).Distinct().OrderByDescending(Bits).ToArray();
        bool possible=false;
        if(gains.Length>0 && Bits(need)<=left*Bits(gains[0]))
        {
            if(gains[0]==need) possible=true;
            else
            {
                UInt128 cell=need & (~need+1);
                possible=gains.Where(g=>(g&cell)!=0).Any(g=>CanCover(need & ~g,left-1));
            }
        }
        // Keep existing memo entries at the cap instead of repeatedly forgetting
        // whole explored regions. Uncached states are still examined normally.
        if(_coverMemo.Count<100_000) _coverMemo[(need,left)]=possible;
        return possible;
    }
    private int AppendWord(int prefix,string kind)
    {
        var key=(prefix,kind);
        if(_wordIds.TryGetValue(key,out int id)) return id;
        id=_words.Count;_words.Add(key);_wordIds.Add(key,id);return id;
    }
    internal void SeedQuery(IEnumerable<CrystalRewardRoute> rows)
    {
        if(_reachability==null) throw new InvalidOperationException("CrystalQueryNotConfigured");
        foreach(var row in rows) ConsiderQueryRow(row);
    }
    internal void SeedProjection(CrystalOptionProjection projection)
    {
        if(projection.SelectedPlan!=null && (_queryPlan==null || projection.Gold>_queryGold))
        { _queryPlan=projection.SelectedPlan;_queryGold=projection.Gold; }
        foreach(var (option,proof) in projection.Available)
            if((_candidateScope==null || _candidateScope.Contains(option)) && _queryCandidates.Contains(option)) _proven.TryAdd(option,proof);
    }
    private bool ConsiderQueryRow(CrystalRewardRoute row)
    {
        var options=Options(row).ToArray();
        if(_selected.Any(o=>!options.Any(o.Accepts))) return false;
        var cards=_selected.Where(o=>o.Kind==PredictorRewardKind.Card).ToImmutableArray();
        if(MatchCardOptions(row.Rewards,cards)==null) return false;
        int gold=row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
        var additions=options.Where(o=>!_selected.Contains(o) && !_proven.ContainsKey(o) && (_candidateScope==null || _candidateScope.Contains(o))).ToArray();
        if(_queryPlan!=null && gold<=_queryGold && additions.Length==0) return false;
        var proof=_verifySelection(row,_selected);if(proof==null) return false;
        bool useful=false;
        if(_queryPlan==null || gold>_queryGold) { _queryPlan=proof;_queryGold=gold;useful=true; }
        if(_stopAfterPlan) return useful; // Probe success, not candidate completeness.
        foreach(var option in additions)
        {
            var extended=_verifySelection(row,_selected.Add(option));if(extended==null) continue;
            if(!_queryCandidates.Contains(option)) throw new InvalidOperationException("CrystalOptionOutsideReachabilityBound:"+option.Key);
            _proven.Add(option,extended);useful=true;
        }
        return useful;
    }

    private readonly record struct DiscreteSlot(string Kind,BigInteger Prefixes,UInt128 Missing,int Item,int Callback,int BlockDraws,int TrailingPotions=0);
    private readonly record struct PossibleSlot(string Kind,int Minimum,int Maximum,UInt128 Missing);
    // An optimistic offset/geometry bound only. Future potions shift ALL card
    // groups, including already completed ones, because their ToReward runs first.
    private bool QueryCanImprove(Position node,CrystalRewardOption? probe=null)
    {
        if(_reachability==null) return true;
        int left=_source.Remaining-node.Path.Length,potions=0,core=0;
        var slots=new List<PossibleSlot>();var relicMasks=new List<UInt128>();
        foreach(int item in node.Order)
        {
            string kind=_source.Items[item].Kind;
            if(kind.StartsWith("POTION_",StringComparison.Ordinal)) { slots.Add(new(kind,potions,potions,0));potions++; }
            else
            {
                if(kind.StartsWith("CARD_",StringComparison.Ordinal)) slots.Add(new(kind,core,core,0));
                if(kind=="RELIC") relicMasks.Add(0);
                core+=CrystalReachabilityTable.Draws(kind);
            }
        }
        int completedSlots=slots.Count;
        var future=new List<(PredictorCrystalItem Item,UInt128 Need)>();int maxPotions=potions,maxCore=core;
        for(int i=0;i<_items.Length;i++)
        {
            UInt128 need=node.Fog & _items[i];var item=_source.Items[i];
            if(need==0 || item.Kind=="CURSE" || !CanCover(need,left)) continue;
            future.Add((item,need));
            if(item.Kind.StartsWith("POTION_",StringComparison.Ordinal)) maxPotions+=item.Subscriptions;
            else maxCore+=item.Subscriptions*CrystalReachabilityTable.Draws(item.Kind);
        }
        foreach(var (item,need) in future)
        {
            if(item.Kind=="RELIC") { for(int n=0;n<item.Subscriptions;n++) relicMasks.Add(need);continue; }
            bool card=item.Kind.StartsWith("CARD_",StringComparison.Ordinal);
            if(!card && !item.Kind.StartsWith("POTION_",StringComparison.Ordinal)) continue;
            for(int n=0;n<item.Subscriptions;n++) slots.Add(new(item.Kind,card?core:potions,card?maxCore-6:maxPotions-1,need));
        }
        bool Possible(ImmutableArray<CrystalRewardOption> targets,int? scenarioCalls=null,UInt128 scenarioNeed=default,List<PossibleSlot>? scenarioSlots=null)
        {
            UInt128 required=scenarioNeed;var candidateSlots=scenarioSlots??slots;
            var requested=targets.Where(t=>t.Kind==PredictorRewardKind.Card)
                .Concat(targets.Where(t=>t.Kind==PredictorRewardKind.Potion).Distinct()).ToArray();
            if(requested.Length>candidateSlots.Count) return false;
            var edges=new int[requested.Length][];
            for(int t=0;t<requested.Length;t++)
            {
                var target=requested[t];var eligible=new List<int>();UInt128 intersection=UInt128.MaxValue;
                for(int s=0;s<candidateSlots.Count;s++)
                {
                    var slot=candidateSlots[s];bool matches=target.Kind==PredictorRewardKind.Card
                        ? _reachability.CardAt(slot.Kind,target,(scenarioCalls??potions)+slot.Minimum,(scenarioCalls??maxPotions)+slot.Maximum)
                        : _reachability.PotionAt(slot.Kind,target.Key,slot.Minimum,slot.Maximum);
                    if(matches) { eligible.Add(s);intersection &= slot.Missing; }
                }
                if(eligible.Count==0) return false;
                edges[t]=eligible.ToArray();required |= intersection;
            }
            if(targets.Any(t=>t.Kind==PredictorRewardKind.Relic))
            {
                int distinctRelics=targets.Where(t=>t.Kind==PredictorRewardKind.Relic && t.Key.IsValid).Select(t=>t.Key).Distinct().Count();
                if(relicMasks.Count<Math.Max(1,distinctRelics) || targets.Where(t=>t.Kind==PredictorRewardKind.Relic).Any(t=>!_reachability.Candidates.Contains(t))) return false;
                required |= relicMasks.Aggregate(UInt128.MaxValue,(a,b)=>a & b);
            }
            if(!CanCover(required,left)) return false;
            var owners=Enumerable.Repeat(-1,candidateSlots.Count).ToArray();
            bool Assign(int target,bool[] seen)
            {
                foreach(int slot in edges[target])
                    if(!seen[slot]) { seen[slot]=true;if(owners[slot]<0 || Assign(owners[slot],seen)) { owners[slot]=target;return true; } }
                return false;
            }
            return Enumerable.Range(0,requested.Length).All(t=>Assign(t,new bool[candidateSlots.Count]));
        }
        // Experimental, opt-in only. Entire physical callback blocks may be
        // placed before a future group; a block cannot contribute half of its
        // subscriptions. Completed phase prefixes remain fixed. Geometry/order
        // restrictions are relaxed, so rejection is a necessary-condition proof.
        List<DiscreteSlot>? discrete=null;
        BigInteger totals=0;
        BigInteger Sums(bool potionPhase,int except=-1)
        {
            BigInteger sums=1;
            for(int i=0;i<future.Count;i++)
            {
                var item=future[i].Item;
                if(i==except || item.Kind.StartsWith("POTION_",StringComparison.Ordinal)!=potionPhase) continue;
                sums |= sums << checked(item.Subscriptions*CrystalReachabilityTable.Draws(item.Kind));
            }
            return sums;
        }
        bool DiscretePossible(ImmutableArray<CrystalRewardOption> targets)
        {
            if(discrete==null)
            {
                discrete=[];int c=0,p=0;var callbacks=new Dictionary<int,int>();
                foreach(int index in node.Order)
                {
                    var item=_source.Items[index];string kind=item.Kind;
                    int callback=callbacks.GetValueOrDefault(index);callbacks[index]=callback+1;
                    int span=checked(item.Subscriptions*CrystalReachabilityTable.Draws(kind));
                    if(kind.StartsWith("POTION_",StringComparison.Ordinal)) { discrete.Add(new(kind,BigInteger.One<<p,0,index,callback,span));p++; }
                    else
                    {
                        if(kind.StartsWith("CARD_",StringComparison.Ordinal)) discrete.Add(new(kind,BigInteger.One<<c,0,index,callback,span));
                        c+=CrystalReachabilityTable.Draws(kind);
                    }
                }
                totals=Sums(true)<<potions;
                for(int i=0;i<future.Count;i++)
                {
                    var (item,need)=future[i];bool potion=item.Kind.StartsWith("POTION_",StringComparison.Ordinal);
                    if(!potion && !item.Kind.StartsWith("CARD_",StringComparison.Ordinal)) continue;
                    var prefixes=Sums(potion,i) << (potion?potions:core);
                    for(int n=0;n<item.Subscriptions;n++)
                        discrete.Add(new(item.Kind,prefixes << (n*CrystalReachabilityTable.Draws(item.Kind)),need,
                            _source.Items.IndexOf(item),n,checked(item.Subscriptions*CrystalReachabilityTable.Draws(item.Kind)),
                            potion?item.Subscriptions-n-1:0));
                }
            }
            var requested=targets.Where(t=>t.Kind==PredictorRewardKind.Card)
                .Concat(targets.Where(t=>t.Kind==PredictorRewardKind.Potion).Distinct()).ToArray();
            bool relic=targets.Any(t=>t.Kind==PredictorRewardKind.Relic);
            for(int finalPotions=potions;finalPotions<=maxPotions;finalPotions++)
            {
                if((totals & (BigInteger.One<<finalPotions)).IsZero) continue;
                var edges=new (int Slot,BigInteger Positions)[requested.Length][];bool failed=false;
                for(int t=0;t<requested.Length;t++)
                {
                    var target=requested[t];var candidates=new List<(int,BigInteger)>();
                    for(int slotIndex=0;slotIndex<discrete.Count;slotIndex++)
                    {
                        var slot=discrete[slotIndex];BigInteger matched;
                        if(target.Kind==PredictorRewardKind.Card)
                            matched=(slot.Prefixes<<finalPotions) & _reachability.CardOffsets(slot.Kind,target);
                        else
                        {
                            int limit=finalPotions-slot.TrailingPotions;
                            matched=limit<=0?BigInteger.Zero:slot.Prefixes & _reachability.PotionOffsets(slot.Kind,target.Key) & ((BigInteger.One<<limit)-1);
                        }
                        if(!matched.IsZero) candidates.Add((slotIndex,matched));
                    }
                    if(candidates.Count==0) { failed=true;break; }
                    edges[t]=candidates.ToArray();
                }
                if(failed) continue;
                var order=Enumerable.Range(0,requested.Length).OrderBy(t=>edges[t].Length).ToArray();
                var used=new bool[discrete.Count];
                // A physical item's repeated callbacks are contiguous within
                // their phase. Different blocks cannot overlap; two target groups
                // from one item must agree on that block's start. Optional prefix
                // subsets may still conflict, so this remains an optimistic bound.
                var assigned=new (int Slot,int Start)[requested.Length];
                bool Assign(int depth,UInt128 need)
                {
                    if(!CanCover(need,left)) return false;
                    if(depth==order.Length) return !relic || relicMasks.Any(r=>CanCover(need | r,left));
                    foreach(var edge in edges[order[depth]])
                    {
                        int slotIndex=edge.Slot;var slot=discrete[slotIndex];
                        if(used[slotIndex]) continue;
                        var positions=edge.Positions;
                        for(int offset=0;positions>0;offset++,positions>>=1)
                        {
                            if(positions.IsEven) continue;
                            int start=offset-slot.Callback*CrystalReachabilityTable.Draws(slot.Kind);
                            bool compatible=true;
                            for(int prior=0;prior<depth;prior++)
                            {
                                var previous=discrete[assigned[prior].Slot];int before=assigned[prior].Start;
                                if(slot.Item==previous.Item)
                                { if(start!=before) compatible=false; }
                                else if(slot.Kind.StartsWith("CARD_",StringComparison.Ordinal)==previous.Kind.StartsWith("CARD_",StringComparison.Ordinal)
                                    && start<before+previous.BlockDraws && before<start+slot.BlockDraws)
                                    compatible=false;
                                if(!compatible) break;
                            }
                            if(!compatible) continue;
                            used[slotIndex]=true;assigned[depth]=(slotIndex,start);
                            if(Assign(depth+1,need | slot.Missing)) return true;
                            used[slotIndex]=false;
                        }
                    }
                    return false;
                }
                if(Assign(0,0)) return true;
            }
            return false;
        }
        List<(int Calls,UInt128 Need,List<PossibleSlot> Slots)>? potionScenarios=null;
        bool ScenarioPossible(ImmutableArray<CrystalRewardOption> targets)
        {
            if(targets.All(t=>t.Kind==PredictorRewardKind.Relic)) return true;
            var potionIndices=Enumerable.Range(0,future.Count).Where(i=>future[i].Item.Kind.StartsWith("POTION_",StringComparison.Ordinal)).ToArray();
            // Cost fallback to the already-passed legacy necessary condition.
            // No subset is truncated and no path is rejected on this account.
            if(potionIndices.Length==0) return true;
            if(potionIndices.Length>_potionScenarioLimit) { _scenarioFallbacks++;return true; }
            if(potionScenarios==null)
            {
                potionScenarios=[];
                for(int subset=0;subset<(1<<potionIndices.Length);subset++)
                {
                    int calls=potions;UInt128 need=0;var chosen=new HashSet<int>();
                    for(int bit=0;bit<potionIndices.Length;bit++)
                        if((subset & (1<<bit))!=0)
                        {
                            int item=potionIndices[bit];chosen.Add(item);
                            calls=checked(calls+future[item].Item.Subscriptions);need |= future[item].Need;
                        }
                    _scenarioCount++;
                    if(!CanCover(need,left)) { _scenarioCoverRejected++;continue; }
                    var scoped=slots.Take(completedSlots).ToList();
                    for(int i=0;i<future.Count;i++)
                    {
                        var (item,missing)=future[i];bool card=item.Kind.StartsWith("CARD_",StringComparison.Ordinal);
                        if(!card && !chosen.Contains(i)) continue;
                        for(int n=0;n<item.Subscriptions;n++)
                            scoped.Add(new(item.Kind,card?core:potions,card?maxCore-6:calls-1,missing));
                    }
                    potionScenarios.Add((calls,need,scoped));
                }
            }
            return potionScenarios.Any(scenario=>Possible(targets,scenario.Calls,scenario.Need,scenario.Slots));
        }
        bool Bound(ImmutableArray<CrystalRewardOption> targets)
        {
            if(!Possible(targets)) return false;
            if(_potionScenarioBound)
            {
                long scenarioStarted=Stopwatch.GetTimestamp();_boundChecks++;
                try { bool result=ScenarioPossible(targets);if(!result) _boundRejected++;return result; }
                finally { _boundTicks+=Stopwatch.GetTimestamp()-scenarioStarted; }
            }
            if(!_discreteRewardBound || targets.All(t=>t.Kind==PredictorRewardKind.Relic)) return true;
            // This memo stores only the relaxed bound, never exact board
            // reachability: CanCover ignores center legality. Blank fog/actual
            // center history therefore cannot change this bound's answer.
            var key=(node.Fog & _occupied,node.RewardKey,left,targets.Length==_selected.Length?null:targets[^1]);
            _boundChecks++;
            if(_discreteMemo.TryGetValue(key,out bool cached))
            { _boundCacheHits++;if(!cached) _boundRejected++;return cached; }
            long started=Stopwatch.GetTimestamp();
            try
            {
                bool result=DiscretePossible(targets);if(!result) _boundRejected++;
                if(_discreteMemo.Count<100_000) _discreteMemo.Add(key,result);
                return result;
            }
            finally { _boundTicks+=Stopwatch.GetTimestamp()-started; }
        }
        if(!Bound(_selected)) return false;
        if(probe!=null) return Bound(_selected.Add(probe));
        if(_queryPlan==null && !_selected.IsEmpty) return true;
        return _queryCandidates.Any(c=>!_proven.ContainsKey(c) && Bound(_selected.Add(c)));
    }
    private int JoinWords(int prefix,int suffix)
    {
        if(prefix==0) return suffix;
        if(suffix==0) return prefix;
        var key=(prefix,suffix);
        if(_joins.TryGetValue(key,out int id)) return id;
        var word=_words[suffix];id=AppendWord(JoinWords(prefix,word.Prefix),word.Kind);
        if(_joins.Count<100_000) _joins.Add(key,id);
        return id;
    }
    private HistoryKey JoinHistory(HistoryKey prefix,HistoryKey suffix)=>new(
        JoinWords(prefix.Potions,suffix.Potions),JoinWords(prefix.Core,suffix.Core),prefix.NewCurses+suffix.NewCurses);
    private HistoryKey AppendCallback(HistoryKey key,string kind)=>kind.StartsWith("POTION_",StringComparison.Ordinal)
        ? key with { Potions=AppendWord(key.Potions,kind) }
        : key with { Core=AppendWord(key.Core,kind) };
    private HistoryKey AppendItem(HistoryKey key,int item)
    {
        var source=_source.Items[item];
        if(source.Kind=="CURSE") return key with { NewCurses=key.NewCurses+1 };
        for(int n=0;n<source.Subscriptions;n++) key=AppendCallback(key,source.Kind);
        return key;
    }
    private HistoryKey RewardKey(ImmutableArray<int> order)
    {
        HistoryKey key=default;HashSet<int>? curses=null;
        foreach(int item in order)
        {
            string kind=_source.Items[item].Kind;
            if(kind!="CURSE") key=AppendCallback(key,kind);
            // Captured curses are already applied in source.State. Physical
            // identity remains in Order for real replay, not in the phase key.
            else if(!_source.Revealed.Contains(item) && (curses??=[]).Add(item))
                key=key with { NewCurses=key.NewCurses+1 };
        }
        return key;
    }
    private static string AppendKey(string key,string value)=>key.Length==0?value:key+","+value;

    // Once an item cannot complete even under the optimistic cover bound, its
    // partial fog can no longer affect any future reward. Preserve only live item
    // fog, curse safety, and hidden centers capable of advancing a live item.
    // Enough initial blank cells guarantee padding irrespective of discarded fog;
    // scarce-padding boards keep their full geometric/center-history key.
    private (UInt128, UInt128, HistoryKey, int, UInt128) ContinuationKey(Position node)
    {
        if(!_alwaysCanPad) return (node.Fog,0,node.RewardKey,node.Path.Length,node.Centers);
        UInt128 needed=0,positive=0,centers=0; int left=_source.Remaining-node.Path.Length;
        for(int i=0;i<_items.Length;i++)
        {
            UInt128 need=node.Fog & _items[i];
            if(need==0 || !CanCover(need,left)) continue;
            needed|=need;
            if(!(_avoidCurse && _source.Items[i].Kind=="CURSE")) positive|=need;
        }
        foreach(var move in _moves)
            if(move.Step.Tool==PredictorCrystalTool.Big && (move.Mask & positive)!=0)
                centers |= (UInt128)1 << (move.Step.X*11+move.Step.Y);
        return (needed,node.Fog & centers,node.RewardKey,node.Path.Length,0);
    }
    private UInt128 RelevantCells(Position node)
    {
        UInt128 relevant=0; int left=_source.Remaining-node.Path.Length;
        for(int i=0;i<_items.Length;i++)
        {
            UInt128 need=node.Fog & _items[i];
            if(need!=0 && !(_avoidCurse && _source.Items[i].Kind=="CURSE") && CanCover(need,left)) relevant|=need;
        }
        return relevant;
    }
    private IEnumerable<Position> Moves(Position node,Func<Position,bool>? accept=null,bool checkQuery=true)
    {
        var seen=new HashSet<(UInt128,string,UInt128)>();
        var siblingFrontiers=new Dictionary<(UInt128,HistoryKey),List<UInt128>>();
        UInt128 relevant=RelevantCells(node);
        foreach(var move in _moves)
        {
            UInt128 center=(UInt128)1 << (move.Step.X*11+move.Step.Y);
            if((node.Fog & center)==0) continue;
            // A click must advance at least one item that could still complete.
            // Removed no-progress clicks are represented by verified Small padding,
            // including prefix padding when later Big reveals cover the blank cells.
            if((move.Mask & relevant)==0) { _pruned++; continue; }
            UInt128 fog=node.Fog; var order=node.Order; int score=node.Score;
            string orderKey=node.OrderKey;var rewardKey=node.RewardKey;
            foreach(int cell in move.Cells)
            {
                if((fog & (UInt128)1 << cell)==0) continue;
                fog &= ~((UInt128)1 << cell);
                int i=_source.Occupancy[cell]; if(i<0) continue;
                score += _source.Items[i].Kind=="RELIC" ? 4 : 1;
                if((fog & _items[i])==0 && !order.Contains(i))
                {
                    for(int n=0;n<_source.Items[i].Subscriptions;n++)
                    {
                        order=order.Add(i);orderKey=AppendKey(orderKey,i.ToString());
                    }
                    rewardKey=AppendItem(rewardKey,i);score+=20;
                }
            }
            if(_avoidCurse && order.Any(i=>_source.Items[i].Kind=="CURSE" && !_source.Revealed.Contains(i))) { _pruned++; continue; }
            if(_alwaysCanPad)
            {
                // For identical occupied-cell progress/reward history, extra
                // hidden blank centers can only provide more legal continuations.
                // Small-first traversal exposes these dominating siblings early.
                var siblingKey=(fog & _occupied,rewardKey);
                if(siblingFrontiers.TryGetValue(siblingKey,out var variants))
                {
                    if(variants.Any(previous=>(previous & fog)==fog)) { _merged++;continue; }
                    variants.RemoveAll(previous=>(previous & fog)==previous);
                }
                else siblingFrontiers[siblingKey]=variants=[];
                variants.Add(fog);
            }
            if(seen.Add((fog,orderKey,_alwaysCanPad?(UInt128)0:node.Centers|center)))
            {
                var next=new Position(fog,order,node.Path.Add(move.Step),score,node.Centers|center,orderKey,rewardKey);
                if((accept==null || accept(next)) && (!checkQuery || QueryCanImprove(next))) yield return next;else _pruned++;
            }
            else _merged++;
        }
    }
    private CrystalRewardRoute? Row(Position node)
    {
        Examined++;
        var signature=node.RewardKey;
        if(_orders.Contains(signature)) return null;
        var completed=CompleteSteps(node);
        if(completed is not { } path) return null;
        // Every published row has a complete legal click witness, not just an
        // assumed RNG offset or an unachievable counterfactual reward order.
        ValidateOrder(path,node.Order);
        long started=Stopwatch.GetTimestamp();
        PredictorRun run;
        try { run=PredictorRun.PreviewCrystalRewards(_source,node.Order,CancellationToken.None); }
        finally { _settlementTicks+=Stopwatch.GetTimestamp()-started; }
        if(run.Phase==PredictorPhase.Rewards && !run.ExportCrystal().Revealed.SequenceEqual(node.Order))
            throw new InvalidOperationException("CrystalPaddingOrderChanged");
        _orders.Add(signature);
        if(run.Phase!=PredictorPhase.Rewards) return null;
        var row=new CrystalRewardRoute(path,run.Rewards);
        return _reachability==null || ConsiderQueryRow(row)?row:null;
    }
    private void ValidateOrder(ImmutableArray<PredictorCrystalStep> path,ImmutableArray<int> expected)
    {
        if(path.Length!=_source.Remaining) throw new InvalidOperationException("CrystalIndexWitnessLength");
        UInt128 fog=_initialFog;var order=_source.Revealed;
        foreach(var step in path)
        {
            int center=step.X*11+step.Y;
            if((fog & ((UInt128)1<<center))==0) throw new InvalidOperationException("CrystalIndexWitnessCenter");
            var move=(step.Tool==PredictorCrystalTool.Big?_bigMoves:_smallMoves)[center];
            foreach(int cell in move.Cells)
            {
                if((fog & ((UInt128)1<<cell))==0) continue;
                fog &= ~((UInt128)1<<cell);int item=_source.Occupancy[cell];
                if(item>=0 && (fog & _items[item])==0 && !order.Contains(item))
                    for(int n=0;n<_source.Items[item].Subscriptions;n++) order=order.Add(item);
            }
        }
        if(!order.SequenceEqual(expected)) throw new InvalidOperationException("CrystalPaddingOrderChanged");
    }
    private ImmutableArray<PredictorCrystalStep>? CompleteSteps(Position node)
    {
        UInt128 fog=node.Fog; var path=node.Path;
        // Prefer suffix padding so existing productive instructions stay first.
        while(path.Length<_source.Remaining)
        {
            int next=-1;
            for(int i=0;i<121;i++)
            {
                if((fog & ((UInt128)1<<i))==0) continue;
                int item=_source.Occupancy[i]; UInt128 need=item<0?0:fog & _items[item];
                if(item<0 || (need & (need-1))!=0) { next=i; break; }
            }
            if(next<0) break;
            fog &= ~((UInt128)1<<next); path=path.Add(new(next/11,next%11,PredictorCrystalTool.Small));
        }
        if(path.Length==_source.Remaining) return path;

        // A discarded blank click can have preceded a Big click that later
        // covers it. Preserve this outcome by placing canonical Small padding
        // before the productive path. Never touch its centers or completed items;
        // reserve one final hidden cell per unfinished item so padding cannot
        // complete it or change the ordered rewards. Full Runtime replay follows.
        UInt128 allowed=_initialFog & ~_occupied;
        for(int i=0;i<_items.Length;i++)
        {
            UInt128 need=node.Fog & _items[i];
            if(need!=0) allowed |= (_initialFog & _items[i]) & ~(need & (~need+1));
        }
        allowed &= ~node.Centers;
        var prefix=ImmutableArray.CreateBuilder<PredictorCrystalStep>();
        for(int i=0;i<121 && prefix.Count+node.Path.Length<_source.Remaining;i++)
            if((allowed & ((UInt128)1<<i))!=0) prefix.Add(new(i/11,i%11,PredictorCrystalTool.Small));
        return prefix.Count+node.Path.Length==_source.Remaining ? prefix.ToImmutable().AddRange(node.Path) : null;
    }
    private IEnumerable<CrystalRewardRoute?> Enumerate(Position initial)
    {
        if(_targetDirected && _reachability!=null && !_selected.IsEmpty)
            foreach(var row in TargetDirected(initial)) yield return row;
        // Seed the index with directed single-object and joint-object covers,
        // so expensive relic shapes are not starved by many cheap gold rows.
        // These are witness heuristics only; exhaustive traversal still follows.
        bool Requested(PredictorCrystalItem item)=>_selected.Any(s=>s.Kind switch {
            PredictorRewardKind.Card=>_reachability!.CardAt(item.Kind,s,0,_reachability.MaximumDraws),
            PredictorRewardKind.Potion=>_reachability!.PotionAt(item.Kind,s.Key,0,_reachability.MaximumDraws),
            PredictorRewardKind.Relic=>item.Kind=="RELIC",_=>false });
        var main=_source.Items.Select((item,i)=>(item,i)).Where(x=>x.item.X>=0 &&
            (x.item.Kind=="RELIC" || x.item.Kind.StartsWith("CARD_") ||
                _reachability!=null && x.item.Kind.StartsWith("POTION_")))
            .OrderByDescending(x=>Requested(x.item)).Select(x=>_items[x.i]).ToArray();
        var goals=main.Concat(from a in Enumerable.Range(0,main.Length) from b in Enumerable.Range(a+1,main.Length-a-1)
            select main[a]|main[b]).Distinct();
        foreach(var goal in goals)
        {
            int Gain(Position before,Position after)=>Bits(goal & before.Fog & ~after.Fog);
            foreach(var seed in Moves(initial).OrderByDescending(n=>Gain(initial,n)).ThenByDescending(n=>n.Score).Take(3))
            {
                var current=seed;
                while(true)
                {
                    yield return Row(current);
                    if(current.Path.Length>=_source.Remaining) break;
                    var next=Moves(current).OrderByDescending(n=>Gain(current,n)).ThenByDescending(n=>n.Score).FirstOrDefault();
                    if(next==null) break;current=next;
                }
            }
        }
        // Broad discovery first. Retain different reward orders as well as
        // partial geometric progress; this pass cannot establish completeness.
        var beam=new List<Position>{initial};
        for(int depth=0;depth<=_source.Remaining && beam.Count>0;depth++)
        {
            var next=new List<Position>();
            foreach(var node in beam)
            {
                yield return Row(node);
                if(depth<_source.Remaining) next.AddRange(Moves(node));
            }
            beam=next.GroupBy(n=>(n.Fog,n.OrderKey)).Select(g=>g.First())
                .GroupBy(n=>n.OrderKey).SelectMany(g=>g.OrderByDescending(n=>n.Score).Take(3))
                .OrderByDescending(n=>n.Score).Take(384).ToList();
        }
        Phase=CrystalDiscoveryPhase.Exhaustive;
        foreach(var row in Exhaustive(initial)) yield return row;
    }
    private static int Bits(UInt128 value)=>System.Numerics.BitOperations.PopCount((ulong)value)+
        System.Numerics.BitOperations.PopCount((ulong)(value>>64));
    private static int FirstBit(UInt128 mask)=> (ulong)mask!=0 ? System.Numerics.BitOperations.TrailingZeroCount((ulong)mask) :
        64+System.Numerics.BitOperations.TrailingZeroCount((ulong)(mask>>64));
    // A Big reveal completes this item iff its center is within one cell of
    // every remaining hidden item cell. Intersect those coordinate rectangles;
    // there is no reason to scan the entire 11x11 board for a finishing click.
    private UInt128 FinishingBigCenters(UInt128 need)
    {
        if(_finishCenters.TryGetValue(need,out var cached)) return cached;
        int minX=11,maxX=-1,minY=11,maxY=-1;
        for(UInt128 bits=need;bits!=0;bits &= bits-1)
        {
            int i=FirstBit(bits),x=i/11,y=i%11;
            minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
        }
        UInt128 centers=0;
        for(int x=Math.Max(0,maxX-1);x<=Math.Min(10,minX+1);x++)
            for(int y=Math.Max(0,maxY-1);y<=Math.Min(10,minY+1);y++) centers |= (UInt128)1 << (x*11+y);
        centers &= _initialFog;
        if(_finishCenters.Count<100_000) _finishCenters[need]=centers;
        return centers;
    }
    private IEnumerable<(PredictorCrystalStep Step,int[] Cells,UInt128 Mask)> FinishingMoves(Position node)
    {
        UInt128 big=0,small=0,forbiddenBig=0,forbiddenSmall=0;
        for(int i=0;i<_items.Length;i++)
        {
            UInt128 need=node.Fog & _items[i];if(need==0) continue;
            UInt128 bigCenters=FinishingBigCenters(need),smallCenters=(need & (need-1))==0?need:0;
            if(_avoidCurse && _source.Items[i].Kind=="CURSE") { forbiddenBig|=bigCenters;forbiddenSmall|=smallCenters; }
            else { big|=bigCenters;small|=smallCenters; }
        }
        big &= node.Fog & ~forbiddenBig; small &= node.Fog & ~forbiddenSmall;
        _pruned+=2*Bits(node.Fog)-Bits(big)-Bits(small);
        for(UInt128 bits=big;bits!=0;bits &= bits-1) yield return _bigMoves[FirstBit(bits)];
        for(UInt128 bits=small;bits!=0;bits &= bits-1) yield return _smallMoves[FirstBit(bits)];
    }
    // With one click left, future geometry no longer matters. Derive only the
    // ordered completions of each legal footprint; do not allocate/cache a whole
    // terminal board for every equivalent click. Unknown rows still use full replay.
    private ImmutableArray<FinalChoice> FinalChoices(Position node)
    {
        UInt128 needed=0,centers=0;
        for(int i=0;i<_items.Length;i++)
        {
            UInt128 need=node.Fog & _items[i];if(need==0) continue;
            UInt128 finishing=FinishingBigCenters(need);if(finishing==0) continue;
            needed|=need;centers|=finishing;
        }
        var key=(needed,node.Fog & centers);
        if(_finalChoices.TryGetValue(key,out var cached)) { _merged++;return cached; }
        var completed=new int[Math.Min(_items.Length,9)];var choices=ImmutableArray.CreateBuilder<FinalChoice>();
        var suffixes=new HashSet<HistoryKey>();
        foreach(var move in FinishingMoves(node))
        {
            int count=0;
            // Last still-hidden cell determines the native callback order.
            for(int rank=move.Cells.Length-1;rank>=0;rank--)
            {
                int cell=move.Cells[rank],item=_source.Occupancy[cell];
                if(item<0 || (node.Fog & ((UInt128)1<<cell))==0) continue;
                UInt128 need=node.Fog & _items[item];
                if((need & ~move.Mask)!=0 || completed.AsSpan(0,count).Contains(item)) continue;
                completed[count++]=item;
            }
            if(count==0) continue;
            HistoryKey suffix=default;var order=ImmutableArray.CreateBuilder<int>();
            for(int j=count-1;j>=0;j--)
            {
                order.Add(completed[j]);
                suffix=AppendItem(suffix,completed[j]);
            }
            if(suffixes.Add(suffix)) choices.Add(new(move,order.ToImmutable(),suffix));else _merged++;
        }
        var result=choices.ToImmutable();
        if(_finalChoices.Count<100_000) _finalChoices[key]=result;
        return result;
    }
    private IEnumerable<CrystalRewardRoute?> FinalRows(Position node)
    {
        // Final geometry is independent of the preceding reward history. Share
        // its distinct ordered completion suffixes across all compatible prefixes.
        foreach(var choice in FinalChoices(node))
        {
            var signature=JoinHistory(node.RewardKey,choice.Suffix);
            if(_orders.Contains(signature)) { _merged++;continue; }
            var order=node.Order;string orderKey=node.OrderKey;
            foreach(int item in choice.Items)
                for(int n=0;n<_source.Items[item].Subscriptions;n++)
                { order=order.Add(item);orderKey=AppendKey(orderKey,item.ToString()); }
            var move=choice.Move;
            yield return Row(new(node.Fog & ~move.Mask,order,node.Path.Add(move.Step),node.Score,
                node.Centers|((UInt128)1 << (move.Step.X*11+move.Step.Y)),orderKey,signature));
        }
    }
    private ImmutableArray<SuffixChoice> TwoStepChoices(Position node)
    {
        var continuation=ContinuationKey(node);var key=(continuation.Item1,continuation.Item2);
        if(_alwaysCanPad && _twoStepChoices.TryGetValue(key,out var cached)) { _merged++;return cached; }
        var choices=ImmutableArray.CreateBuilder<SuffixChoice>();var known=new HashSet<HistoryKey>();
        void Add(ImmutableArray<PredictorCrystalStep> steps,ImmutableArray<int> items)
        {
            if(items.IsEmpty) return;
            var suffix=RewardKey(items);
            if(known.Add(suffix)) choices.Add(new(steps,items,suffix));else _merged++;
        }
        // Geometry depends on live fog/centers, not the preceding reward/RNG
        // history. Keep the real depth and centers but collect only new callbacks.
        var start=node with { Order=[],OrderKey="",RewardKey=default };
        foreach(var next in Moves(start))
        {
            var first=next.Path[^1];Add([first],next.Order);
            foreach(var last in FinalChoices(next))
            {
                var items=next.Order;
                foreach(int item in last.Items)
                    for(int n=0;n<_source.Items[item].Subscriptions;n++) items=items.Add(item);
                Add([first,last.Move.Step],items);
            }
        }
        var result=choices.ToImmutable();
        if(_alwaysCanPad && _twoStepChoices.Count<100_000 && _twoStepEntries+result.Length<=350_000)
        { _twoStepChoices[key]=result;_twoStepEntries+=result.Length; }
        return result;
    }
    private IEnumerable<CrystalRewardRoute?> TwoStepRows(Position node)
    {
        foreach(var suffix in TwoStepChoices(node))
        {
            var signature=JoinHistory(node.RewardKey,suffix.Key);
            if(_orders.Contains(signature)) { _merged++;yield return null;continue; }
            var path=node.Path.AddRange(suffix.Steps);UInt128 fog=node.Fog,centers=node.Centers;
            foreach(var step in suffix.Steps)
            {
                int i=step.X*11+step.Y;centers|=(UInt128)1<<i;
                fog &= ~(step.Tool==PredictorCrystalTool.Big?_bigMoves:_smallMoves)[i].Mask;
            }
            var order=node.Order.AddRange(suffix.Items);
            string orderKey=node.OrderKey;
            foreach(int item in suffix.Items) orderKey=AppendKey(orderKey,item.ToString());
            yield return Row(new(fog,order,path,node.Score,centers,orderKey,signature));
        }
    }
    private IEnumerable<CrystalRewardRoute?> Exhaustive(Position node)
    {
        if(_reachability!=null)
        {
            yield return null; // cooperative query pause, including pruned nodes
            if(!QueryCanImprove(node)) { _pruned++;yield break; }
        }
        // Terminal geometries have no continuation. Global reward-order replay
        // already deduplicates them, so do not spend the transposition budget on
        // leaves while forgetting the much more valuable interior states.
        if(node.Path.Length==_source.Remaining) { yield return Row(node); yield break; }
        if(node.Path.Length==_source.Remaining-1)
        {
            yield return Row(node);
            foreach(var row in FinalRows(node)) yield return row;
            yield break;
        }
        var key=ContinuationKey(node);
        if(_alwaysCanPad)
        {
            var frontierKey=(key.Item1,key.Item3,key.Item4);
            if(_frontiers.TryGetValue(frontierKey,out var variants))
            {
                // Same live item fog and ordered reward effects: a previously
                // explored superset of legal centers covers every continuation.
                // Actual tool footprints still determine every emitted witness.
                if(variants.Any(previous=>(previous & key.Item2)==key.Item2)) { _merged++;yield break; }
                int removed=variants.RemoveAll(previous=>(previous & key.Item2)==previous);
                _frontierEntries-=removed;
            }
            else if(_frontierEntries<500_000) _frontiers[frontierKey]=variants=[];
            if(variants!=null && _frontierEntries<500_000) { variants.Add(key.Item2);_frontierEntries++; }
        }
        else
        {
            if(_visited.Contains(key)) { _merged++;yield break; }
            if(_visited.Count<500_000) _visited.Add(key);
        }
        yield return Row(node);
        // A one-step tail representative may need another click. Without the
        // blank-padding guarantee, a different representative of the SAME reward
        // suffix can be the only one that completes the exact remaining budget.
        // The legacy suffix cache erases the reward prefix before calling Moves.
        // Query pruning requires that prefix; keep query mode on ordinary
        // traversal even when an audit explicitly requests the old tail cache.
        if(_useTwoStepCache && _reachability==null && _alwaysCanPad && node.Path.Length==_source.Remaining-2)
        {
            foreach(var row in TwoStepRows(node)) yield return row;
            yield break;
        }
        foreach(var next in Moves(node)) foreach(var row in Exhaustive(next)) yield return row;
    }
    internal static PredictorRun Replay(PredictorCrystalSnapshot source, ImmutableArray<PredictorCrystalStep> steps)
    {
        var run=PredictorRun.FromCrystal(source);
        foreach(var step in steps)
            if(run.Submit(run.Request!,new SelectCrystalTool(step.Tool))!=PredictorInputResult.Accepted ||
                run.Submit(run.Request!,new RevealCrystalCell(step.X,step.Y))!=PredictorInputResult.Accepted)
                throw new InvalidOperationException("CrystalIndexWitnessInvalid");
        return run;
    }
    internal static IEnumerable<CrystalRewardOption> Options(CrystalRewardRoute row) => row.Rewards.SelectMany(r=>r.Kind switch {
        PredictorRewardKind.Card => r.Cards.Select(c=>new CrystalRewardOption(PredictorRewardKind.Card,c.Key,c.UpgradeLevel)),
        PredictorRewardKind.Potion when r.Key.IsValid => [new CrystalRewardOption(r.Kind,r.Key)],
        PredictorRewardKind.Relic when r.Key.IsValid => [CrystalRewardOption.AnyRelic, new CrystalRewardOption(r.Kind,r.Key)],
        _ => Enumerable.Empty<CrystalRewardOption>() }).Distinct();
    internal static PredictorCrystalSolution? VerifySelection(PredictorCrystalSnapshot source, CrystalRewardRoute row,
        ImmutableArray<CrystalRewardOption> selected)
    {
        var cards=selected.Where(o=>o.Kind==PredictorRewardKind.Card).ToImmutableArray();
        // Materialize every UI-visible/joint witness through the actual node inputs,
        // including relic/potion-only plans. Table settlement is candidate discovery.
        var run=Replay(source,row.Steps);
        if(run.Phase!=PredictorPhase.Rewards) return null;
        if(selected.Any(o=>o.Kind==PredictorRewardKind.Potion) && PredictorSettlementEffects.Has(run.Working,"SOZU")) return null;
        var slots=MatchCardOptions(run.Rewards,cards);
        if(slots==null || selected.Any(o=>!Options(new(row.Steps,run.Rewards)).Any(o.Accepts))) return null;
        if(cards.IsEmpty) return new("Found",0,row.Steps,[]);
        for(int n=0;n<cards.Length;n++)
        {
            var reward=run.Rewards[slots[n]]; var card=reward.Cards.FirstOrDefault(cards[n].MatchesCard);
            if(card==null) return null;
            var priorDeck=run.Working.Deck.ToHashSet();
            if(run.Submit(run.Request!,new EnterCardSelection(reward.Id))!=PredictorInputResult.Accepted ||
                run.Submit(run.Request!,new TakeReward(reward.Id,card.Id))!=PredictorInputResult.Accepted) return null;
            if(!run.Working.Deck.Where(id=>!priorDeck.Contains(id)).Select(run.Working.Card).Any(cards[n].MatchesCard)) return null;
        }
        // Non-card entries promise an available reward, not an automatic pickup.
        // Full potion slots and nested relic choices remain native player choices.
        if(selected.Where(o=>o.Kind!=PredictorRewardKind.Card).Any(o=>!run.Rewards.Any(r=>o.Matches(r) && r.Status==PredictorRewardStatus.Open))) return null;
        return new("Found",0,row.Steps,cards.Select((option,n)=>new PredictorCrystalTake(slots[n],option.Key,option.UpgradeLevel)).ToImmutableArray());
    }
    internal static int[]? MatchCardOptions(ImmutableArray<PredictorReward> rewards, ImmutableArray<CrystalRewardOption> cards)
    {
        var slots=new int[cards.Length];
        bool Match(int n)
        {
            if(n==cards.Length) return true;
            for(int i=0;i<rewards.Length;i++)
                if(!slots.Take(n).Contains(i) && rewards[i].Kind==PredictorRewardKind.Card && rewards[i].Cards.Any(cards[n].MatchesCard))
                { slots[n]=i;if(Match(n+1)) return true; }
            return false;
        }
        return Match(0)?slots:null;
    }
    internal static CrystalOptionProjection Project(PredictorCrystalSnapshot source, ImmutableArray<CrystalRewardRoute> rows,
        ImmutableArray<CrystalRewardOption> selected, CancellationToken token=default,CrystalOptionProjection? known=null,
        Func<CrystalRewardRoute,ImmutableArray<CrystalRewardOption>,PredictorCrystalSolution?>? verifySelection=null)
    {
        var available=known?.Available.ToBuilder()??ImmutableDictionary.CreateBuilder<CrystalRewardOption,PredictorCrystalSolution>();
        PredictorCrystalSolution? best=known?.SelectedPlan; int gold=known?.Gold??-1;
        verifySelection??=(row,selection)=>VerifySelection(source,row,selection);
        foreach(var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var choices=Options(row).ToArray();
            if(selected.Any(o=>!choices.Any(o.Accepts))) continue;
            int value=row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
            var additions=choices.Where(o=>!selected.Contains(o) && !available.ContainsKey(o)).ToArray();
            if(best!=null && value<=gold && additions.Length==0) continue;
            var proof=verifySelection(row,selected); if(proof==null) continue;
            if(best==null || value>gold) { best=proof; gold=value; }
            foreach(var option in additions)
            {
                token.ThrowIfCancellationRequested();
                var extended=verifySelection(row,selected.Add(option));
                if(extended!=null) available.Add(option,extended);
            }
        }
        return new(available.ToImmutable(),best,Math.Max(0,gold));
    }
}
