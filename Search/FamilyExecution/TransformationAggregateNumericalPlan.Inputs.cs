using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class TransformationAggregateNumericalPlan
{
    internal int LocalId(RolltheSpire2.Core.Identity.ModelKey key)
    {
        if (_neow is null) return -2;
        int dense = Array.IndexOf(_neow.Authority.EffectCatalog.DenseKeys,key);
        return dense >= 0 ? _neowIds[dense] : -2;
    }
    internal bool OwnsGroup(ulong hash, RolltheSpire2.Core.Identity.ModelKey source) =>
        hash == TransformationsHash && source == RolltheSpire2.Core.Identity.BaseGameModelKeys.Relics.LeafyPoultice ||
        hash == NicheHash && source == RolltheSpire2.Core.Identity.BaseGameModelKeys.Relics.NewLeaf;
    internal double EventGateProbability => _condition.TrialNondescript ? 1d/3 : 1d;
    internal TransformationAggregateCondition Condition => _condition;
    internal NeowReplayPlan? Neow => _neow;
    internal bool IsRare(int id) => _rare[id];
    internal uint TargetBits(int id)
    {
        uint bits=0; for(int i=0;i<_targets.Length;i++) if(_targets[i]==id) bits |= 1u<<i; return bits;
    }
    internal bool Closed => (_neow is null || _closedNeow) && _events.All(g=>g.Pools.All(p=>p.Length>0));
    internal (ulong Hash, int Prefix, int[][] Pools)[] DrawGroups
    {
        get
        {
            var groups = new List<(ulong,int,int[][])>();
            if (_neow is { } n)
            {
                int[] Pool(ushort[] ids) => ids.Select(i=>_neowIds[i]).ToArray();
                var catalog=n.Authority.EffectCatalog;
                if (_condition.Opening != TransformationOpening.NewLeaf)
                    groups.Add((TransformationsHash,0,[Pool(catalog.LeafyStrikeTransformPool),Pool(catalog.LeafyDefendTransformPool)]));
                if (_condition.Opening is TransformationOpening.NewLeaf or TransformationOpening.BonesLeafyNewLeaf)
                    groups.Add((NicheHash,0,[Pool(catalog.NewLeafTransformPool)]));
            }
            groups.AddRange(_events); return groups.ToArray();
        }
    }
}
