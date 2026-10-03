using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    private ImmutableArray<string> _rootCardSlots;

    // Root proof shared by production queries and the language experiment.
    // All card callbacks may complete at any supported
    // initial/reroll offset: timing and geometry are deliberately relaxed.
    // Only failure of a distinct-slot assignment supplies a negative fact.
    internal bool RejectRootCardSlots(ImmutableArray<CrystalRewardOption> requested)
    {
        if(!_rootCardSlotsEnabled || !_admitted || requested.Length>16) return false;
        var cards=requested.Where(g=>g.Kind==PredictorRewardKind.Card).Distinct()
            .OrderBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
        if(cards.IsEmpty) return false;
        lock(_sync)
        {
            if(_rootCardSlots.IsDefault)
            {
                var capacities=new Dictionary<string,int>(StringComparer.Ordinal);
                void Add(string kind,int count)
                {
                    if(kind.StartsWith("CARD_",StringComparison.Ordinal))
                        capacities[kind]=Math.Min(16,capacities.GetValueOrDefault(kind)+Math.Min(16,count));
                }
                // Revealed is already a callback log, including repeated
                // subscriptions. Each entry is one slot, not Subscriptions slots.
                foreach(int item in _source.Revealed) Add(_source.Items[item].Kind,1);
                foreach(var block in _core) Add(block.Kind,block.Calls);
                // At most sixteen distinct goals are considered here. Capping
                // each kind's capacity at sixteen cannot remove an assignment.
                _rootCardSlots=capacities.OrderBy(p=>p.Key,StringComparer.Ordinal)
                    .SelectMany(p=>Enumerable.Repeat(p.Key,p.Value)).ToImmutableArray();
            }
            var supports=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string kind in _rootCardSlots.Distinct(StringComparer.Ordinal))
            {
                int bits=0;
                for(int g=0;g<cards.Length;g++)
                    if(!_table.CardOffsets(kind,cards[g]).IsZero) bits|=1<<g;
                supports.Add(kind,bits);
            }
            var owners=Enumerable.Repeat(-1,_rootCardSlots.Length).ToArray();
            bool Assign(int goal,bool[] seen)
            {
                for(int s=0;s<_rootCardSlots.Length;s++)
                    if(!seen[s] && (supports[_rootCardSlots[s]]&(1<<goal))!=0)
                    {
                        seen[s]=true;
                        if(owners[s]<0 || Assign(owners[s],seen)) {owners[s]=goal;return true;}
                    }
                return false;
            }
            for(int g=0;g<cards.Length;g++)
                if(!Assign(g,new bool[_rootCardSlots.Length]))
                {
                    // This proof does not depend on non-card targets. Duplicate
                    // goal normalization above matches the relaxed compiler;
                    // a two-copy demand must not become a singleton conflict.
                    RememberLanguageNegative(cards,CrystalLanguageNegativeKind.RewardDomain);
                    return true;
                }
            return false;
        }
    }
}
