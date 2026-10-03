using System.Collections.Immutable;
using System.Numerics;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Snapshot-local optimistic lookup, not reward truth. Geometry still has to
// realize an offset, and every published option is checked by typed replay.
internal sealed partial class CrystalReachabilityTable
{
    private readonly Dictionary<(string Kind,ModelKey Card),int[]> _cardOffsets=[];
    private readonly Dictionary<(string Kind,ModelKey Potion),int[]> _potionOffsets=[];
    private readonly Dictionary<(string Kind,ModelKey Card),BigInteger> _cardBits=[];
    private readonly Dictionary<(string Kind,CrystalRewardOption Card),BigInteger> _cardVariantBits=[];
    private readonly Dictionary<(string Kind,ModelKey Potion),BigInteger> _potionBits=[];
    private readonly Lazy<ImmutableDictionary<ModelKey,BigInteger>>? _singleRelicBits;
    internal ImmutableArray<CrystalRewardOption> Candidates { get; }
    internal int MaximumDraws { get; }
    internal int MaximumInitialDraws { get; }
    internal bool IncludesRerolls { get; }
    internal bool IncludesEnchantments { get; }
    internal bool HasSingleRelicOffsets=>_singleRelicBits!=null;
    internal static int Draws(string kind)=>kind switch {
        "CARD_COMMON" or "CARD_UNCOMMON" or "CARD_RARE"=>6,
        "CURSE"=>0,"GOLD_SMALL" or "GOLD_BIG" or "RELIC" or "POTION_COMMON" or "POTION_RARE"=>1,
        _=>throw new InvalidOperationException("CrystalUnknownRewardKind:"+kind)
    };
    internal CrystalReachabilityTable(PredictorCrystalSnapshot source,CancellationToken token=default,bool includeRerolls=false,bool relicOffsetBound=false,bool includeEnchantments=true)
    {
        var placed=source.Items.Where(i=>i.X>=0).ToArray();
        var future=source.Items.Select((item,index)=>(item,index))
            .Where(p=>p.item.X>=0 && !source.Revealed.Contains(p.index)).Select(p=>p.item).ToArray();
        int relicCallbacks=source.Revealed.Count(i=>source.Items[i].Kind=="RELIC")+
            future.Where(i=>i.Kind=="RELIC").Sum(i=>i.Subscriptions);
        if(relicOffsetBound && relicCallbacks==1)
        {
            // Historical entries are callbacks, not physical items. Each offset
            // starts from the original bag; no real run or shared mutable state.
            int draws=source.Revealed.Sum(i=>Draws(source.Items[i].Kind))+future.Sum(i=>checked(i.Subscriptions*Draws(i.Kind)));
            _singleRelicBits=new(()=>
            {
                var bits=ImmutableDictionary.CreateBuilder<ModelKey,BigInteger>();var cursor=source.EventRng.Restore();
                for(int offset=0;offset<draws;offset++)
                {
                    var rng=cursor.Clone();var state=source.State;
                    var key=PredictorRewardGeneration.PullRelic(source.Context,ref state,rng);
                    if(rng.CallCount-cursor.CallCount!=1) throw new InvalidOperationException("CrystalRelicOffsetContractChanged");
                    bits[key]=bits.GetValueOrDefault(key)|(BigInteger.One<<offset);cursor.Advance(1);
                }
                return bits.ToImmutable();
            },LazyThreadSafetyMode.ExecutionAndPublication);
        }
        MaximumInitialDraws=source.Revealed.Sum(i=>Draws(source.Items[i].Kind))+future.Sum(i=>checked(i.Subscriptions*Draws(i.Kind)));
        IncludesRerolls=includeRerolls && PredictorSettlementEffects.Has(source.State,"DRIFTWOOD");
        int cardInstances=source.Revealed.Count(i=>source.Items[i].Kind.StartsWith("CARD_",StringComparison.Ordinal))+
            future.Where(i=>i.Kind.StartsWith("CARD_",StringComparison.Ordinal)).Sum(i=>i.Subscriptions);
        MaximumDraws=checked(MaximumInitialDraws+(IncludesRerolls?6*cardInstances:0));
        IncludesEnchantments=includeEnchantments && CrystalRewardOption.UsesEnchantments(source.State);
        ConfigureEnchantmentRows(source,token);
        var candidates=new HashSet<CrystalRewardOption>();
        foreach(string kind in placed.Where(i=>i.Kind.StartsWith("CARD_",StringComparison.Ordinal)).Select(i=>i.Kind).Distinct())
        {
            var rarity=Enum.Parse<EffectCardRarity>(kind[5..],ignoreCase:true);
            var plan=new PredictorRewardPlan(PredictorRewardKind.Card,CardCount:3,Odds:PredictorCardOdds.Uniform,Rarity:rarity,UseEventRng:true);
            var positions=new Dictionary<ModelKey,List<int>>();
            // Silver Crucible may expire after earlier card groups. Enumerate
            // its possible prior-group counts rather than freezing every group
            // at the entry counter. Offset/ordinal independence is optimistic;
            // actual generation and take replay remain the final authority.
            int priorGroups=source.State.Relics.Any(r=>!r.Melted && r.Key.Entry=="SILVER_CRUCIBLE" && r.RewardUpgradeUses<3)
                ?Math.Min(3,Math.Max(0,cardInstances*(IncludesRerolls?2:1)-1)):0;
            var cursor=source.EventRng.Restore();
            for(int offset=0;offset+6<=MaximumDraws;offset++)
            {
                token.ThrowIfCancellationRequested();
                for(int prior=0;prior<=priorGroups;prior++)
                {
                    var rng=cursor.Clone();var state=source.State with { Relics=source.State.Relics.Select(r=>
                        r.Key.Entry=="SILVER_CRUCIBLE"?r with { RewardUpgradeUses=Math.Min(3,r.RewardUpgradeUses+prior) }:r).ToImmutableArray() };
                    var cards=PredictorRewardGeneration.GenerateCards(source.Context,ref state,plan,true,rng);
                    // Current Crystal creation is three fixed-rarity draws plus
                    // three upgrade draws. All subsequent inventory hooks preserve
                    // card identity; Niche/counter changes remain in final replay.
                    if(rng.CallCount-cursor.CallCount!=6) throw new InvalidOperationException("CrystalCardOffsetContractChanged");
                    foreach(var card in cards)
                    {
                        if(!positions.TryGetValue(card.Key,out var values)) positions.Add(card.Key,values=[]);
                        if(prior==0) values.Add(offset);
                        var option=new CrystalRewardOption(PredictorRewardKind.Card,card.Key,card.UpgradeLevel);
                        candidates.Add(option);
                        var variant=(kind,option);
                        _cardVariantBits[variant]=_cardVariantBits.GetValueOrDefault(variant)|(BigInteger.One<<offset);
                    }
                }
                cursor.Advance(1);
            }
            foreach(var (card,values) in positions)
            {
                _cardOffsets.Add((kind,card),values.ToArray());
                _cardBits.Add((kind,card),values.Aggregate(BigInteger.Zero,(bits,n)=>bits | (BigInteger.One<<n)));
            }
        }
        if(!PredictorSettlementEffects.Has(source.State,"SOZU"))
        {
            int count=placed.Where(i=>i.Kind.StartsWith("POTION_",StringComparison.Ordinal)).Sum(i=>i.Subscriptions);
            foreach(string kind in placed.Where(i=>i.Kind.StartsWith("POTION_",StringComparison.Ordinal)).Select(i=>i.Kind).Distinct())
            {
                var rarity=kind=="POTION_RARE"?PredictorPotionRarity.Rare:PredictorPotionRarity.Common;
                var pool=source.Context.PotionPool.Where(p=>p.Rarity==rarity).ToArray();
                if(pool.Length==0) throw new InvalidOperationException("PredictorCrystalPotionPoolEmpty:"+rarity);
                var positions=new Dictionary<ModelKey,List<int>>();var rng=source.EventRng.Restore();
                for(int ordinal=0;ordinal<count;ordinal++)
                {
                    token.ThrowIfCancellationRequested();var key=pool[rng.NextInt(pool.Length)].Key;
                    if(!positions.TryGetValue(key,out var values)) positions.Add(key,values=[]);
                    values.Add(ordinal);candidates.Add(new(PredictorRewardKind.Potion,key));
                }
                foreach(var (potion,values) in positions)
                {
                    _potionOffsets.Add((kind,potion),values.ToArray());
                    _potionBits.Add((kind,potion),values.Aggregate(BigInteger.Zero,(bits,n)=>bits | (BigInteger.One<<n)));
                }
            }
        }
        int relicCount=placed.Where(i=>i.Kind=="RELIC").Sum(i=>i.Subscriptions);
        if(relicCount>0)
        {
            bool Allowed(ModelKey key)=>PredictorRewardGeneration.RelicAllowed(source.Context,source.State,key);
            var bag=source.State.PersonalBag;
            foreach(var key in bag.Buckets.SelectMany(b=>b.Entries.Where(Allowed).Take(relicCount))
                .Concat(bag.MultiplayerFallback.Where(Allowed).Take(relicCount))
                .Concat(bag.RefreshAllowed?bag.Original.Select(e=>e.Key).Where(Allowed):[]).Distinct())
                candidates.Add(new(PredictorRewardKind.Relic,key));
            if(new[]{"Common","Uncommon","Rare"}.Any(r=>bag.Buckets.Where(b=>b.Rarity==r).Sum(b=>b.Entries.Count(Allowed))<relicCount))
                candidates.Add(new(PredictorRewardKind.Relic,new("RELIC","CIRCLET")));
            candidates.Add(CrystalRewardOption.AnyRelic);
        }
        if(IncludesEnchantments)
        {
            candidates.RemoveWhere(c=>c.Kind==PredictorRewardKind.Card);
            candidates.UnionWith(_enchantmentRows.Value.Candidates);
        }
        Candidates=candidates.OrderBy(c=>c.Kind).ThenBy(c=>c.Key.Category,StringComparer.Ordinal)
            .ThenBy(c=>c.Key.Entry,StringComparer.Ordinal).ThenBy(c=>c.UpgradeLevel)
            .ThenBy(c=>c.CacheKey,StringComparer.Ordinal).ToImmutableArray();
    }
    internal BigInteger CardOffsets(string kind,ModelKey key)=>_cardBits.GetValueOrDefault((kind,key));
    internal BigInteger CardOffsets(string kind,CrystalRewardOption option)
    {
        if(option.Enchantment!=null) return EnchantmentOffsets(kind,option);
        return option.UpgradeLevel==null?CardOffsets(kind,option.Key):_cardVariantBits.GetValueOrDefault((kind,option));
    }
    internal bool CardAt(string kind,CrystalRewardOption option,int minimum,int maximum)
    {
        if(maximum<minimum || maximum<0) return false;
        minimum=Math.Max(0,minimum);
        return ((CardOffsets(kind,option)>>minimum)&((BigInteger.One<<(maximum-minimum+1))-1))!=0;
    }
    internal BigInteger PotionOffsets(string kind,ModelKey key)=>_potionBits.GetValueOrDefault((kind,key));
    internal bool TrySingleRelicOffsets(ModelKey key,out BigInteger offsets)
    {
        offsets=_singleRelicBits?.Value.GetValueOrDefault(key)??BigInteger.Zero;
        return _singleRelicBits!=null;
    }
    private static bool InRange(int[] offsets,int minimum,int maximum)
    {
        if(maximum<minimum) return false;
        int found=Array.BinarySearch(offsets,minimum);if(found<0) found=~found;
        return found<offsets.Length && offsets[found]<=maximum;
    }
    internal bool CardAt(string kind,ModelKey card,int minimum,int maximum)=>
        _cardOffsets.TryGetValue((kind,card),out var offsets) && InRange(offsets,minimum,maximum);
    internal bool PotionAt(string kind,ModelKey potion,int minimum,int maximum)=>
        _potionOffsets.TryGetValue((kind,potion),out var offsets) && InRange(offsets,minimum,maximum);
}

internal sealed record CrystalReachabilityUpdate(CrystalOptionProjection Projection,
    CrystalDiscoveryProgress Progress,CrystalDiscoveryPhase Phase,bool Complete,
    long BoundChecks=0,long BoundRejected=0,double BoundSeconds=0,long BoundCacheHits=0,int BoundRootExcluded=0,ImmutableArray<CrystalRewardOption> UnwitnessedUpper=default,long Scenarios=0,long ScenarioCoverRejected=0,long ScenarioFallbacks=0,int Workers=1,long DirectedRecipes=0,long DirectedNodes=0,long DirectedWitnesses=0,long IslandBuilds=0,long IslandHits=0,long ProbeSlices=0,long ProbePlans=0,ImmutableArray<CrystalRewardOption> ExcludedCandidates=default);

// One frozen board. Each selection has its own frontier. Tables, positive
// witnesses and completed contradictions retain their explicit query scope.
internal sealed partial class PredictorCrystalReachability(PredictorCrystalSnapshot source,bool avoidCurse,bool discreteRewardBound=false,bool potionScenarioBound=false,int potionScenarioLimit=6,int workers=1,bool targetDirected=false,bool islandDiscovery=false,bool proactiveDiscovery=true,bool candidateFirst=true,bool jointOffsetBound=true,bool includeRerolls=false,bool rootCardSlots=true,bool packingCover=true,bool relicOffsetBound=false,bool goalLanguageProof=false,bool includeEnchantments=true)
{
    private sealed class Lane(PredictorCrystalExplorer explorer)
    {
        internal readonly PredictorCrystalExplorer Explorer=explorer;
        internal int Seen;
    }
    private sealed class Query(Lane[] lanes)
    {
        internal readonly Lane[] Lanes=lanes;
        internal double Seconds;
        internal Exception? Failure;
        internal long Used;
        internal CrystalRewardOption[] ProbeOrder=[];
        internal int ProbeCursor;
        internal Probe? ActiveProbe;
        internal readonly List<Probe> ActiveCandidates=[];
        internal readonly Dictionary<CrystalRewardOption,Probe> Probes=[];
        internal readonly HashSet<CrystalRewardOption> ExhaustedProbes=[];
        internal long ProbeSlices,ProbePlans,ProbeExamined,ProbePruned,ProbeMerged;
    }
    private readonly object _sync=new();
    private CrystalReachabilityTable? _table;
    private PredictorCrystalOffsetBound? _offsetBound;
    private PredictorCrystalIslands? _islands;
    private readonly Dictionary<string,Query> _queries=[];
    private readonly List<CrystalRewardRoute> _witnesses=[];
    // Readers need only an immutable positive snapshot, not the traversal lock.
    // An older snapshot costs discoveries until the next slice, never correctness.
    private CrystalRewardRoute[] _readableWitnesses=[];
    private void PublishWitnesses()
    {
        if(_readableWitnesses.Length!=_witnesses.Count) Volatile.Write(ref _readableWitnesses,_witnesses.ToArray());
    }
    private readonly HashSet<string> _paths=[];
    internal void AddWitnesses(IEnumerable<CrystalRewardRoute> rows)
    {
        lock(_sync)
        {
            foreach(var row in rows)
                if(_paths.Add(string.Join(';',row.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}"))))
                    _witnesses.Add(includeRerolls && row.RerollVariants.IsDefault && PredictorSettlementEffects.Has(source.State,"DRIFTWOOD")
                        ?PredictorCrystalExplorer.WithRerolls(row,PredictorCrystalExplorer.Replay(source,row.Steps)):row);
            PublishWitnesses();
        }
    }
    // Reproject known successful routes before creating a new search frontier.
    // This returns positive evidence only, never a completeness verdict.
    internal CrystalOptionProjection ProjectKnown(ImmutableArray<CrystalRewardOption> selected,CancellationToken token=default,
        Action<CrystalOptionProjection>? publish=null)
    {
        lock(_reuseSync)
        {
            string key=SelectionKey(selected);
            var projection=PeekKnown(selected);
            if(projection.SelectedPlan!=null) publish?.Invoke(projection);
            token.ThrowIfCancellationRequested();
            var rows=Volatile.Read(ref _readableWitnesses);
            int seen=_projectedRows.GetValueOrDefault(key);
            while(seen<rows.Length)
            {
                token.ThrowIfCancellationRequested();
                int count=Math.Min(8,rows.Length-seen);
                projection=PredictorCrystalExplorer.Project(source,ImmutableArray.Create(rows,seen,count),selected,token,projection,VerifyCached,includeEnchantments);
                seen+=count;projection=PublishKnown(key,projection,selected);_projectedRows[key]=seen;
                publish?.Invoke(projection);
            }
            return projection;
        }
    }
    internal CrystalReachabilityUpdate Advance(ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,CancellationToken token=default,
        Action<ImmutableArray<CrystalRewardOption>>? publishExcluded=null)
    {
        if(goalLanguageProof && !selected.IsEmpty) return AdvanceLanguage(selected,budget,token,publishExcluded);
        return AdvanceLegacy(selected,budget,token,publishExcluded);
    }
    private CrystalReachabilityUpdate AdvanceLegacy(ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,CancellationToken token,
        Action<ImmutableArray<CrystalRewardOption>>? publishExcluded=null)
    {
        lock(_sync)
        {
            StopLanguageWorker();
            token.ThrowIfCancellationRequested();
            if(workers is <1 or >4) throw new ArgumentOutOfRangeException(nameof(workers));
            // Include table/root proof work in visible query time. A zero
            // traversal slice may still perform a substantial negative proof.
            var watch=System.Diagnostics.Stopwatch.StartNew();
            _table??=new(source,token,includeRerolls:includeRerolls,relicOffsetBound:relicOffsetBound,includeEnchantments:includeEnchantments);
            if(jointOffsetBound) _offsetBound??=new(source,_table,avoidCurse,rootCardSlots:rootCardSlots,packingCover:packingCover);
            if(islandDiscovery) _islands??=new(source,avoidCurse);
            string key=SelectionKey(selected);
            if(_finished.TryGetValue(key,out var finished)) return finished;
            if(!_queries.TryGetValue(key,out var query))
            {
                // Dormant proof frontiers are expendable; all their published
                // witnesses survive. Eviction can cost time, never lose a result.
                if(_queries.Count>=Math.Max(1,8/workers)) _queries.Remove(_queries.MinBy(q=>q.Value.Used).Key);
                // Keep initial discovery lightweight; the physical potion bound
                // is useful once the player constrains compatible additions.
                // Publish cheap all-orders card contradictions before any lane
                // enters potion/offset search. UI can read these immutable facts
                // while the remaining query is still being prepared.
                if(rootCardSlots && _offsetBound!=null)
                    foreach(var candidate in _table.Candidates.Where(c=>!selected.Contains(c)))
                    {
                        token.ThrowIfCancellationRequested();
                        var goals=selected.Add(candidate);
                        if(!ConflictsWith(goals) && _offsetBound.RejectRootCardSlots(goals))
                            RememberConflict(goals.Where(g=>g.Kind==PredictorRewardKind.Card).ToImmutableArray(),"root-card-slots");
                    }
                var inherited=ConflictingCandidates(selected).ToHashSet();
                // Immutable same-selection publication; the caller enqueues it,
                // never updates Godot controls from this worker callback.
                if(inherited.Count>0) publishExcluded?.Invoke(inherited.ToImmutableArray());
                var candidates=_table.Candidates.Where(c=>!selected.Contains(c) && !inherited.Contains(c)).ToImmutableArray();
                int count=Math.Min(workers,Math.Max(1,candidates.Length));
                query=new(Enumerable.Range(0,count).Select(i=>new Lane(new(source,avoidCurse,reachability:_table,selected:selected,
                    discreteRewardBound:discreteRewardBound,potionScenarioBound:potionScenarioBound && !selected.IsEmpty,potionScenarioLimit:potionScenarioLimit,
                    queryCandidates:candidates.Where((_,index)=>index%count==i).ToImmutableArray(),targetDirected:targetDirected,islands:_islands,
                    verifySelection:VerifyCached,offsetBound:selected.IsEmpty?null:_offsetBound))).ToArray());
                _queries.Add(key,query);
                query.ExhaustedProbes.UnionWith(inherited);
                if(proactiveDiscovery && targetDirected && source.Remaining>3) query.ProbeOrder=ProbeOrder(candidates);
            }
            query.Used=++_useClock;
            query.ExhaustedProbes.UnionWith(ConflictingCandidates(selected));
            if(_focusedExcluded.TryGetValue(key,out var focusedResolved)) query.ExhaustedProbes.UnionWith(focusedResolved);
            if(query.Failure!=null) throw new InvalidOperationException("CrystalParallelQueryFailed",query.Failure);
            bool individual=candidateFirst && !selected.IsEmpty && query.ProbeOrder.Length>0;
            var known=PeekKnown(selected).Available;
            var excludedBeforeSlice=query.Lanes.SelectMany(l=>l.Explorer.ExcludedCandidates).ToHashSet();
            bool probing=budget>TimeSpan.Zero && query.ProbeOrder.Any(c=>!known.ContainsKey(c) &&
                !excludedBeforeSlice.Contains(c) && !query.ExhaustedProbes.Contains(c));
            var broadBudget=probing?TimeSpan.FromTicks(individual?budget.Ticks/4:budget.Ticks*3/4):budget;
            var rows=new ImmutableArray<CrystalRewardRoute>[query.Lanes.Length];
            double accountedSeconds=0;
            try
            {
                // The table/snapshot and witness list are read-only during a slice.
                // Each lane owns its iterator, RNG replays and mutable caches. Only
                // the calling worker merges results, after every lane has joined.
                void AdvanceLane(int index)
                {
                    var lane=query.Lanes[index];
                    lane.Explorer.SeedProjection(PeekKnown(selected));
                    lane.Explorer.SeedQuery(_witnesses.Skip(lane.Seen));lane.Seen=_witnesses.Count;
                    rows[index]=lane.Explorer.Advance(broadBudget,token);
                }
                if(query.Lanes.Length==1) AdvanceLane(0);
                else Parallel.For(0,query.Lanes.Length,new ParallelOptions { MaxDegreeOfParallelism=query.Lanes.Length },AdvanceLane);
                foreach(var batch in rows)
                    foreach(var row in batch)
                        if(_paths.Add(string.Join(';',row.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}")))) _witnesses.Add(row);
                if(probing && !query.Lanes.All(l=>l.Explorer.Complete))
                {
                    var probeBudget=TimeSpan.FromTicks(budget.Ticks-broadBudget.Ticks);
                    if(individual) AdvanceCandidates(query,selected,probeBudget,token);
                    else AdvanceProbe(query,selected,probeBudget,token);
                }
            }
            catch(Exception ex) { query.Failure=ex;throw; }
            finally { query.Seconds+=accountedSeconds=watch.Elapsed.TotalSeconds; }
            var explorers=query.Lanes.Select(l=>l.Explorer).ToArray();
            var projections=explorers.Select(e=>e.QueryProjection).ToArray();
            var best=projections.OrderByDescending(p=>p.Gold).FirstOrDefault(p=>p.SelectedPlan!=null);
            var available=projections.SelectMany(p=>p.Available).ToImmutableDictionary();
            PublishWitnesses();
            var projection=PublishKnown(key,new(available,best?.SelectedPlan,projections.Max(p=>p.Gold)),selected,reshape:true);
            // Gold refinement is worker computation too; include publication
            // work in elapsed time instead of reporting only click traversal.
            query.Seconds+=watch.Elapsed.TotalSeconds-accountedSeconds;
            var progress=new CrystalDiscoveryProgress(explorers.Sum(e=>e.Progress.Examined)+query.ProbeExamined,explorers.Sum(e=>e.Progress.Pruned)+query.ProbePruned,
                explorers.Sum(e=>e.Progress.Merged)+query.ProbeMerged,explorers.Sum(e=>e.Progress.Orders),query.Seconds);
            var excluded=explorers.SelectMany(e=>e.ExcludedCandidates).Concat(query.ExhaustedProbes).Distinct().ToImmutableArray();
            if(excluded.Any(projection.Available.ContainsKey)) throw new InvalidOperationException("CrystalConflictingCandidateProofs");
            // Explorer exclusions can include ordered take failures. Their
            // query frontier/finished entry retains them; do not generalize them.
            // This closes the candidate question, not every route or gold optimum.
            // Each extra reward needs its own positive witness or negative proof.
            bool complete=explorers.All(e=>e.Complete) || individual && projection.SelectedPlan!=null &&
                _table.Candidates.Where(c=>!selected.Contains(c)).All(c=>projection.Available.ContainsKey(c) || excluded.Contains(c));
            var phase=complete?CrystalDiscoveryPhase.Complete:explorers.Any(e=>!e.Complete && e.Phase==CrystalDiscoveryPhase.FindingPlans)
                ?CrystalDiscoveryPhase.FindingPlans:CrystalDiscoveryPhase.Exhaustive;
            var result=new CrystalReachabilityUpdate(projection,progress,phase,complete,
                explorers.Sum(e=>e.BoundChecks),explorers.Sum(e=>e.BoundRejected),explorers.Sum(e=>e.BoundSeconds),
                explorers.Sum(e=>e.BoundCacheHits),explorers.Sum(e=>e.BoundRootExcluded),explorers.SelectMany(e=>e.UnwitnessedUpper).ToImmutableArray(),
                explorers.Sum(e=>e.ScenarioCount),explorers.Sum(e=>e.ScenarioCoverRejected),explorers.Sum(e=>e.ScenarioFallbacks),explorers.Length,
                explorers.Sum(e=>e.DirectedRecipes),explorers.Sum(e=>e.DirectedNodes),explorers.Sum(e=>e.DirectedWitnesses),_islands?.Builds??0,_islands?.Hits??0,query.ProbeSlices,query.ProbePlans,excluded);
            if(complete) _finished[key]=result;
            return result;
        }
    }
}
