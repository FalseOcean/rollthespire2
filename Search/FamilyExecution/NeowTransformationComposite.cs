using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// One N physical invocation covers the pure opening aggregate as well as N.
// Query ownership and Exact's source-free multiset validation remain unchanged.
internal sealed class NeowTransformationComposite
{
    internal const string Revision = "N.Neow.JointLeafyNewLeaf.TransformFirst.Carry8.CanonicalAbi1Ready.20261001.v1";
    internal TransformationAggregateNumericalPlan Numerical { get; }
    internal TransformationAggregateProbability Probability { get; }
    internal NeowFamilyGpuPlan Gpu { get; }
    private NeowTransformationComposite(TransformationAggregateNumericalPlan numerical, NeowFamilyGpuPlan gpu)
    { Numerical = numerical; Probability = TransformationAggregateProbability.Build(numerical); Gpu = gpu; }

    internal static bool TryCreate(ExactSearchExecutionRequest request, NeowReplayPlan neow,
        out NeowTransformationComposite? composite)
    {
        composite = null;
        var e = request.Evaluation;
        if (request.Authority.PlayersCount != 1 || request.Authority.PlayerSlotIndex != 0 ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || neow.ExactOnly.Length != 0 ||
            e.NeowRoute?.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones ||
            e.RequiredBonesCombination.Count != 2 ||
            !e.RequiredBonesCombination.Contains(BaseGameModelKeys.Relics.LeafyPoultice) ||
            !e.RequiredBonesCombination.Contains(BaseGameModelKeys.Relics.NewLeaf) ||
            e.TransformationAggregate is not { Opening: TransformationOpening.BonesLeafyNewLeaf, UsesEvents: false } c ||
            c.OpportunityCount != 3 || c.PickupOrder != TransformationPickupOrder.Any ||
            e.StructuredNeowEffects.Any(x => !x.IsEmpty && x.Scope != NeowStructuredEffectScope.FinalCurse) ||
            !NeowFamilyGpuPlan.TryCreate(neow, out var gpu, out _) || gpu is null)
            return false;
        var numerical = new TransformationAggregateNumericalPlan(request);
        if (!numerical.Closed || numerical.DrawGroups.Length != 2) return false;
        // The same immutable pools/target-instance masks used by the aggregate
        // donor are appended to N's buffers. No RNG or observation payload leaves N.
        var meta = gpu.Meta.ToList(); var data = gpu.Cards.ToList();
        int offset = meta.Count;
        meta.AddRange([(uint)c.Predicate, (uint)c.TargetMultiset.Count, (uint)c.MinimumRareCount, 3u]);
        foreach (var group in numerical.DrawGroups)
        {
            if (group.Prefix != 0 || group.Pools.Length is < 1 or > 2) return false;
            meta.AddRange([(uint)group.Hash, (uint)(group.Hash >> 32), (uint)group.Pools.Length]);
            foreach (var pool in group.Pools)
            {
                meta.Add((uint)data.Count); meta.Add((uint)pool.Length);
                data.AddRange(pool.Select(id => c.Predicate == TransformationAggregatePredicate.RareCountAtLeast
                    ? (numerical.IsRare(id) ? 1u : 0u)
                    : numerical.TargetBits(id) | (c.RequiresRareRemainder && numerical.IsRare(id) ? 0x80000000u : 0u)));
            }
            for (int i = group.Pools.Length; i < 2; i++) meta.AddRange([0u, 0u]);
        }
        composite = new(numerical, gpu with { Meta = meta.ToArray(), Cards = data.ToArray(), JointTransformMetaOffset = offset });
        return true;
    }

}
