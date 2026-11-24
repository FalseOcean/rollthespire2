using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class TransformationAggregateNumericalPlan
{
    internal int LocalId(ModelKey key)
    {
        if (_neow is null) return -2;
        int dense = Array.IndexOf(_neow.Authority.EffectCatalog.DenseKeys,key);
        return dense >= 0 ? _neowIds[dense] : -2;
    }
    internal bool OwnsGroup((ulong Hash, int Prefix, int[][] Pools) group, ModelKey source) =>
        group.Hash == TransformationsHash && group.Pools.Length == 2 && source == BaseGameModelKeys.Relics.LeafyPoultice ||
        group.Hash == (UsesModern113 ? TransformationsHash : NicheHash) && group.Pools.Length == 1 &&
            source == BaseGameModelKeys.Relics.NewLeaf;
    internal double EventGateProbability => _condition.TrialNondescript ? 1d/3 : 1d;
    internal TransformationAggregateCondition Condition => _condition;
    internal NeowReplayPlan? Neow => _neow;
    internal bool IsRare(int id) => _rare[id];
    internal uint TargetBits(int id)
    {
        uint bits=0; for(int i=0;i<_targets.Length;i++) if(_targets[i]==id) bits |= 1u<<i; return bits;
    }
    internal bool Closed => (_neow is null || _closedNeow) && _events.All(g=>g.Pools.All(p=>p.Length>0));
    internal (ulong Hash, int Prefix, int[][] Pools)[] DrawGroups =>
        Groups(_condition.PickupOrder == TransformationPickupOrder.NewLeafThenLeafy);
    internal (ulong Hash, int Prefix, int[][] Pools)[] Groups(bool newLeafFirst)
    {
        var groups = new List<(ulong, int, int[][])>();
        if (_neow is { } n)
        {
            int[] Pool(ushort[] ids) => ids.Select(i => _neowIds[i]).ToArray();
            var catalog = n.Authority.EffectCatalog;
            if (_condition.Opening != TransformationOpening.NewLeaf)
                groups.Add((TransformationsHash, UsesModern113 && newLeafFirst ? 3 : 0,
                    [Pool(catalog.LeafyStrikeTransformPool), Pool(catalog.LeafyDefendTransformPool)]));
            if (_condition.Opening is TransformationOpening.NewLeaf or TransformationOpening.BonesLeafyNewLeaf)
                groups.Add((UsesModern113 ? TransformationsHash : NicheHash,
                    UsesModern113 && _condition.IsBones && !newLeafFirst ? 4 : 0, [Pool(catalog.NewLeafTransformPool)]));
        }
        groups.AddRange(_events);
        return groups.ToArray();
    }
}
