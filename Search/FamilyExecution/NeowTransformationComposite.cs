using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// One N physical invocation covers the pure opening aggregate as well as N.
// Query ownership and Exact's source-free multiset validation remain unchanged.
internal sealed class NeowTransformationComposite
{
    internal const string Revision = "N.Neow.JointLeafyNewLeaf.TransformFirst.Carry8.CanonicalAbi1Ready.20261001.v1";
    internal string PhysicalRevision => Numerical.UsesModern113 ? Revision + ".SharedTransformations.20261114.v1" : Revision;
    internal TransformationAggregateNumericalPlan Numerical { get; }
    internal TransformationAggregateProbability Probability { get; }
    internal NeowFamilyGpuPlan Gpu { get; }
    internal bool MatchesModernReferenceShape { get; }
    private NeowTransformationComposite(TransformationAggregateNumericalPlan numerical, NeowFamilyGpuPlan gpu,
        bool matchesModernReferenceShape = false)
    {
        Numerical = numerical;
        Probability = TransformationAggregateProbability.Build(numerical);
        Gpu = gpu;
        MatchesModernReferenceShape = matchesModernReferenceShape;
    }

    internal static bool TryCreate(SearchModel request, NeowReplayPlan neow,
        out NeowTransformationComposite? composite, NeowGpuNumericalVariant numericalVariant = NeowGpuNumericalVariant.NativeFloat64)
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
            c.OpportunityCount != 3 ||
            e.StructuredNeowEffects.Any(x => !x.IsEmpty && x.Scope != NeowStructuredEffectScope.FinalCurse) ||
            !NeowFamilyGpuPlan.TryCreate(neow, out var gpu, out _) || gpu is null)
            return false;
        var numerical = new TransformationAggregateNumericalPlan(request);
        if (!numerical.Closed || numerical.DrawGroups.Length != 2) return false;
        if (numerical.UsesModern113)
        {
            // Explicitly bind the same order set used by Exact. In this pair,
            // pickup order affects Transformations only; the other permitted
            // N predicate (Final Curse) sees the same NonCombat arrival.
            byte leafy = Beta110FastRelicCatalog.LeafyPoultice, leaf = Beta110FastRelicCatalog.NewLeaf;
            uint orders = c.PickupOrder switch
            {
                TransformationPickupOrder.Any when neow.First == 255 && neow.Second == 255 => 3u,
                TransformationPickupOrder.LeafyThenNewLeaf when neow.First == leafy && neow.Second == leaf => 1u,
                TransformationPickupOrder.NewLeafThenLeafy when neow.First == leaf && neow.Second == leafy => 2u,
                _ => 0u
            };
            if (orders == 0 || !NeowModernTransformGate.HasSharedPools(neow, request.Authority)) return false;
            var groups = numerical.Groups(false);
            var reverse = numerical.Groups(true);
            if (!numerical.OwnsGroup(groups[0], BaseGameModelKeys.Relics.LeafyPoultice) ||
                !numerical.OwnsGroup(groups[1], BaseGameModelKeys.Relics.NewLeaf) || groups[0].Hash != groups[1].Hash ||
                groups[0].Prefix != 0 || groups[1].Prefix != 4 || reverse[0].Prefix != 3 || reverse[1].Prefix != 0)
                return false;
            var sharedMeta = gpu.Meta.ToList();
            var sharedData = gpu.Cards.ToList();
            int sharedOffset = sharedMeta.Count;
            sharedMeta.AddRange([(uint)c.Predicate, (uint)c.TargetMultiset.Count, (uint)c.MinimumRareCount, orders]);
            var pools = groups.SelectMany(group => group.Pools).ToArray();
            foreach (int[] pool in pools)
            {
                sharedMeta.AddRange([(uint)sharedData.Count, (uint)pool.Length]);
                sharedData.AddRange(pool.Select(id => numerical.TargetBits(id) | (numerical.IsRare(id) ? 0x80000000u : 0u)));
            }
            bool measuredShape = request.Authority.GameVersion == "0.113.0" && request.Authority.Character.CharacterKey == BaseGameModelKeys.Characters.Fool &&
                neow.Authority.Ascension == 10 && neow.Authority.AllCharacterCardPoolsUnlocked &&
                neow.Authority.BonesEligibleRelicIds.Length == 28 && neow.Authority.EligibleCurseRelicIds.Length == 10 &&
                neow.Authority.EffectCatalog.GeneratedCurseIds.Length == 10 && neow.RequiredFinalCurseIds.Length == 1 && neow.BannedFinalCurseIds.Length == 0 &&
                neow.TopAny == 0 && neow.TopAll == 0 && neow.TopBan == 0 && neow.BonesAny == 0 && neow.BonesBan == 0 &&
                orders == 3u && c.Predicate == TransformationAggregatePredicate.ContainsMultiset &&
                c.TargetMultiset.Count == 3 && MatchesMeasuredRankShape(pools, numerical);
            composite = new(numerical, gpu with {
                Meta = sharedMeta.ToArray(), Cards = sharedData.ToArray(), JointTransformMetaOffset = sharedOffset,
                NumericalVariant = numericalVariant
            }, measuredShape);
            return true;
        }
        if (c.PickupOrder != TransformationPickupOrder.Any || numericalVariant != NeowGpuNumericalVariant.NativeFloat64) return false;
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

    // The measured full-window case presents identical rank maps for all
    // three draws, with one distinct target at each of these positions.
    private static bool MatchesMeasuredRankShape(int[][] pools, TransformationAggregateNumericalPlan numerical)
    {
        uint[]? reference = null;
        foreach (int[] pool in pools)
        {
            if (pool.Length != 79) return false;
            uint[] masks = pool.Select(numerical.TargetBits).ToArray();
            if (reference is not null && !masks.AsSpan().SequenceEqual(reference)) return false;
            reference = masks;
        }
        return reference is not null && reference.Where(mask => mask != 0).Count() == 3 &&
            new[] { reference[11], reference[23], reference[56] }.Order().SequenceEqual(new uint[] { 1, 2, 4 });
    }

}
