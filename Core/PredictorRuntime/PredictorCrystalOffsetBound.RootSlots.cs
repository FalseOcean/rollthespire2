using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    private sealed record RootCardSlotProof(ImmutableArray<string> Kinds,ImmutableArray<int> Slots,
        ImmutableDictionary<CrystalRewardOption,int> Supports);
    private readonly object _rootCardSlotSync=new();
    private RootCardSlotProof? _readableRootCardSlots;

    // UI reads only an already prepared immutable view. It never builds a
    // table, materializes lazy enchantment rows or waits for the compiler.
    internal bool PreparedRootCardSlotsConflict(ImmutableArray<CrystalRewardOption> requested)
    {
        var proof=Volatile.Read(ref _readableRootCardSlots);
        return proof!=null && MatchRootCardSlots(proof,requested,lookupMissing:false);
    }

    // Pure necessary condition with a separate preparation lock: an old
    // compilation must not delay cheap exclusions for a new selection.
    // Modeled Doubt obtains add no reward slots or RNG draws; Curse stays
    // outside this card-capacity domain even when its reveal is allowed.
    internal bool RootCardSlotsConflict(ImmutableArray<CrystalRewardOption> requested)
    {
        if(!_rootCardSlotsEnabled || !_admitted || requested.Length>16) return false;
        if(!requested.Any(goal=>goal.Kind==PredictorRewardKind.Card)) return false;
        var proof=Volatile.Read(ref _readableRootCardSlots);
        if(proof==null)
            lock(_rootCardSlotSync)
            {
                proof=_readableRootCardSlots;
                if(proof==null)
                {
                    var capacities=new Dictionary<string,int>(StringComparer.Ordinal);
                    void Add(string kind,int count)
                    {
                        if(kind.StartsWith("CARD_",StringComparison.Ordinal))
                            capacities[kind]=Math.Min(16,capacities.GetValueOrDefault(kind)+Math.Min(16,count));
                    }
                    // Revealed entries already count individual callbacks.
                    foreach(int item in _source.Revealed) Add(_source.Items[item].Kind,1);
                    foreach(var block in _core) Add(block.Kind,block.Calls);
                    var kinds=capacities.Keys.Order(StringComparer.Ordinal).ToImmutableArray();
                    var slots=kinds.SelectMany((kind,index)=>Enumerable.Repeat(index,capacities[kind])).ToImmutableArray();
                    int Support(CrystalRewardOption card)
                    {
                        int bits=0;
                        for(int k=0;k<kinds.Length;k++) if(!_table.CardOffsets(kinds[k],card).IsZero) bits|=1<<k;
                        return bits;
                    }
                    var supports=_table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card)
                        .ToImmutableDictionary(c=>c,Support);
                    proof=new(kinds,slots,supports);
                    Volatile.Write(ref _readableRootCardSlots,proof);
                }
            }
        return MatchRootCardSlots(proof,requested,lookupMissing:true);
    }

    private bool MatchRootCardSlots(RootCardSlotProof proof,ImmutableArray<CrystalRewardOption> requested,bool lookupMissing)
    {
        if(!_rootCardSlotsEnabled || !_admitted || requested.Length>16) return false;
        var cards=requested.Where(g=>g.Kind==PredictorRewardKind.Card).Distinct().ToImmutableArray();
        if(cards.IsEmpty) return false;
        var supports=new int[cards.Length];
        for(int g=0;g<cards.Length;g++)
        {
            if(proof.Supports.TryGetValue(cards[g],out supports[g])) continue;
            // Unprepared UI targets remain unknown. Worker-side authored
            // queries retain the original exact offset lookup.
            if(!lookupMissing) return false;
            for(int k=0;k<proof.Kinds.Length;k++)
                if(!_table.CardOffsets(proof.Kinds[k],cards[g]).IsZero) supports[g]|=1<<k;
        }
        var owners=Enumerable.Repeat(-1,proof.Slots.Length).ToArray();
        bool Assign(int goal,bool[] seen)
        {
            for(int s=0;s<proof.Slots.Length;s++)
                if(!seen[s] && (supports[goal]&(1<<proof.Slots[s]))!=0)
                {
                    seen[s]=true;
                    if(owners[s]<0 || Assign(owners[s],seen)) {owners[s]=goal;return true;}
                }
            return false;
        }
        for(int g=0;g<cards.Length;g++) if(!Assign(g,new bool[proof.Slots.Length])) return true;
        return false;
    }

    // Root proof shared by production queries and the language experiment.
    // All card callbacks may complete at any supported
    // initial/reroll offset: timing and geometry are deliberately relaxed.
    // Only failure of a distinct-slot assignment supplies a negative fact.
    internal bool RejectRootCardSlots(ImmutableArray<CrystalRewardOption> requested)
    {
        if(!RootCardSlotsConflict(requested)) return false;
        var cards=requested.Where(g=>g.Kind==PredictorRewardKind.Card).Distinct()
            .OrderBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
        RememberLanguageNegative(cards,CrystalLanguageNegativeKind.RewardDomain);
        return true;
    }
}
