using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// Projects compiled player semantics into the topology-independent criteria used
/// by Production Exact. It contains no backend, Family, or candidate representation.
/// </summary>
public static class CompiledSearchEvaluationProjector
{
    public static ExactSearchEvaluationProjectionResult Project(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        return Project(compiled.NormalizedQuery, compiled.Context);
    }

    internal static ExactSearchEvaluationProjectionResult Project(
        SearchQuery query,
        SearchContext context)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        RuntimeContextAuthoritySnapshot authority = context.Authority;

        var diagnostics = new List<string>();
        ProjectionFidelity fidelity = ProjectionFidelity.Exact;

        IReadOnlyList<ActOrdinalModelKeySetFilter> branchBossProjection = Array.Empty<ActOrdinalModelKeySetFilter>();
        if (query.VariantBossBranches.Count > 0)
        {
            if (!TryProjectVariantBossBranches(query.VariantBossBranches, authority, out branchBossProjection, out ProjectionFidelity branchFidelity, out string issue))
            {
                return new ExactSearchEvaluationProjectionResult(
                    ExactSearchEvaluationProjection.Empty,
                    ProjectionFidelity.Unsupported,
                    new[] { issue });
            }
            fidelity = MaxFidelity(fidelity, branchFidelity);
            diagnostics.Add("VariantBossProjection=" + branchFidelity);
        }

        LegacyNeowSemanticConstraints oldNeow = query.LegacyNeow;
        LegacyWorldSemanticConstraints oldWorld = query.LegacyWorld;

        IReadOnlyList<ModelKey> projectedBonesCombination = oldNeow.RequiredBonesCombination;
        IReadOnlyList<ModelKey> projectedBonesOrder = oldNeow.RequiredBonesAcquisitionOrder;
        if (query.OpeningRouteRelicRequirement is { IsEmpty: false } routeRelics)
        {
            if (query.OpeningRoute is not { IsValid: true } opening ||
                opening.RouteRelicKey != routeRelics.ParentRouteRelicKey ||
                opening.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones)
            {
                return new ExactSearchEvaluationProjectionResult(
                    ExactSearchEvaluationProjection.Empty,
                    ProjectionFidelity.Unsupported,
                    new[] { "OpeningRouteRelicRequirementProjectionUnsupported" });
            }

            ModelKey[] canonicalRequired = routeRelics.RequiredRelicKeys
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            ModelKey[] legacyRequired = oldNeow.RequiredBonesCombination
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (legacyRequired.Length > 0 &&
                !new HashSet<ModelKey>(legacyRequired, ModelKeyComparer.Instance).SetEquals(canonicalRequired))
            {
                return new ExactSearchEvaluationProjectionResult(
                    ExactSearchEvaluationProjection.Empty,
                    ProjectionFidelity.Unsupported,
                    new[] { "OpeningRouteRelicRequirementConflictsWithLegacyBonesCombination" });
            }
            projectedBonesCombination = canonicalRequired;
            if (routeRelics.OrderMode == BonesRouteOrderMode.ExactOrder)
            {
                ModelKey[] canonicalOrder = routeRelics.RequiredRelicKeys.Where(key => key.IsValid).ToArray();
                if (canonicalOrder.Length != 2)
                {
                    return new ExactSearchEvaluationProjectionResult(
                        ExactSearchEvaluationProjection.Empty,
                        ProjectionFidelity.Unsupported,
                        new[] { "BonesExactOrderProjectionRequiresTwoRelics" });
                }
                if (oldNeow.RequiredBonesAcquisitionOrder.Count > 0 &&
                    !oldNeow.RequiredBonesAcquisitionOrder.SequenceEqual(canonicalOrder))
                {
                    return new ExactSearchEvaluationProjectionResult(
                        ExactSearchEvaluationProjection.Empty,
                        ProjectionFidelity.Unsupported,
                        new[] { "BonesExactOrderConflictsWithLegacyAcquisitionOrder" });
                }
                projectedBonesOrder = canonicalOrder;
            }
            diagnostics.Add("OpeningRouteRelicRequirementProjection=Exact:" + routeRelics.OrderMode);
        }

        NormalCombatRewardSearchCondition[] modernRewardFastProjection = BuildConservativeCombatRewardProjection(
            query.CombatCardRewards,
            query.CombatPotionRewards);

        var evaluation = new ExactSearchEvaluationProjection(
            oldNeow.NeowRelics,
            oldNeow.RequireNeowsBones,
            oldNeow.BonesRelics,
            projectedBonesCombination,
            oldNeow.RequireSmallCapsule,
            oldNeow.RequireLargeCapsule,
            oldNeow.CapsuleContainedRelics,
            oldNeow.RequireWhetstone,
            oldNeow.RequireWarPaint,
            oldNeow.RequiredFinalCurse,
            oldNeow.BannedFinalCurses,
            oldNeow.Preset)
        {
            NeowRoute = query.OpeningRoute,
            StructuredNeowEffects = query.StructuredOpeningEffects,
            RequiredBonesAcquisitionOrder = projectedBonesOrder,
            EffectOutputConditions = oldNeow.EffectOutputConditions,
            BossFilters = oldWorld.BossFilters,
            BossOrdinalFilters = oldWorld.BossOrdinalFilters.Concat(branchBossProjection).ToArray(),
            AncientBranchConditions = query.AncientBranches,
            AncientIdentityFilters = oldWorld.AncientIdentityFilters,
            AncientOptionFilters = oldWorld.AncientOptionFilters,
            AncientSeaGlassTargetFilters = oldWorld.AncientSeaGlassTargetFilters,
            AncientOptionConditions = context.EvaluationAssumptions.AncientEligibilityAssumptions,
            RelicSequenceConditions = query.RelicSequenceConstraints,
            RelicShopSequenceConditions = query.RelicShopSequenceConditions,
            EventSequenceConditions = query.EventSequenceConstraints,
            EventResultConditions = query.EventResultConditions,
            TransformationAggregate = query.TransformationAggregate,
            MerchantColorlessConditions = query.MerchantColorlessConditions,
            MerchantColorlessSequenceConditions = query.MerchantColorlessSequenceConditions,
            NormalCombatRewardConditions = query.LegacyCombatRewardConstraints
                .Concat(modernRewardFastProjection)
                .ToArray(),
            CombatCardRewardSequence = query.CombatCardRewards,
            CombatPotionRewardSequence = query.CombatPotionRewards
        };

        return new ExactSearchEvaluationProjectionResult(evaluation, fidelity, diagnostics);
    }

    private static NormalCombatRewardSearchCondition[] BuildConservativeCombatRewardProjection(
        CombatCardRewardSequenceSearchCondition? cards,
        CombatPotionRewardSequenceSearchCondition? potions)
    {
        var output = new List<NormalCombatRewardSearchCondition>();
        if (cards is { IsEmpty: false })
        {
            for (int index = 0; index < cards.Slots.Count; index++)
            {
                ModelKey? target = cards.Slots[index];
                if (!target.HasValue) continue;
                int battleOrdinal = cards.OrderMode == CombatRewardSequenceOrderMode.Ordered || cards.Count == 1
                    ? index + 1
                    : 0;
                output.Add(new NormalCombatRewardSearchCondition(
                    battleOrdinal,
                    new ModelKeySetFilter(new[] { target.Value }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>()),
                    NormalCombatPotionRequirement.Any,
                    ModelKeySetFilter.Empty,
                    null,
                    null));
            }
        }
        if (potions is { IsEmpty: false })
        {
            for (int index = 0; index < potions.Slots.Count; index++)
            {
                CombatPotionRewardSlotSearchCondition slot = potions.Slots[index];
                if (slot.IsNeutral) continue;
                int battleOrdinal = potions.OrderMode == CombatRewardSequenceOrderMode.Ordered || potions.Count == 1
                    ? index + 1
                    : 0;
                NormalCombatPotionRequirement requirement = slot.Requirement == CombatPotionSlotRequirement.NoDrop
                    ? NormalCombatPotionRequirement.MustNotDrop
                    : NormalCombatPotionRequirement.MustDrop;
                ModelKeySetFilter potionKeys = slot.Requirement == CombatPotionSlotRequirement.DropSpecific && slot.PotionKey.HasValue
                    ? new ModelKeySetFilter(new[] { slot.PotionKey.Value }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : ModelKeySetFilter.Empty;
                output.Add(new NormalCombatRewardSearchCondition(
                    battleOrdinal,
                    ModelKeySetFilter.Empty,
                    requirement,
                    potionKeys,
                    null,
                    null));
            }
        }
        return output.ToArray();
    }

    private static bool TryProjectVariantBossBranches(
        IReadOnlyList<VariantScopedBossBranch> branches,
        RuntimeContextAuthoritySnapshot authority,
        out IReadOnlyList<ActOrdinalModelKeySetFilter> filters,
        out ProjectionFidelity fidelity,
        out string issue)
    {
        var output = new List<ActOrdinalModelKeySetFilter>();
        fidelity = ProjectionFidelity.Exact;
        issue = string.Empty;

        foreach (IGrouping<int, VariantScopedBossBranch> actGroup in branches.GroupBy(branch => branch.Act).OrderBy(group => group.Key))
        {
            VariantScopedBossBranch[] selected = actGroup.ToArray();
            if (!TryGetRuntimeVariants(authority, actGroup.Key, out IReadOnlyDictionary<ModelKey, IReadOnlyList<ModelKey>> runtimeVariants))
            {
                filters = Array.Empty<ActOrdinalModelKeySetFilter>();
                fidelity = ProjectionFidelity.Unsupported;
                issue = $"VariantProjectionRuntimeAuthorityMissing:Act{actGroup.Key}";
                return false;
            }

            foreach (VariantScopedBossBranch branch in selected)
            {
                if (!runtimeVariants.ContainsKey(branch.VariantKey))
                {
                    filters = Array.Empty<ActOrdinalModelKeySetFilter>();
                    fidelity = ProjectionFidelity.Unsupported;
                    issue = $"VariantProjectionUnknownVariant:Act{actGroup.Key}:{branch.VariantKey.Serialized}";
                    return false;
                }
            }

            if (runtimeVariants.Count > 1)
            {
                if (!BossPoolsArePairwiseDisjoint(runtimeVariants.Values))
                {
                    filters = Array.Empty<ActOrdinalModelKeySetFilter>();
                    fidelity = ProjectionFidelity.Unsupported;
                    issue = $"VariantProjectionOverlappingBossPools:Act{actGroup.Key}";
                    return false;
                }
                fidelity = MaxFidelity(fidelity, ProjectionFidelity.EquivalentUnderCurrentRuntime);
            }

            var first = new List<ModelKey>();
            foreach (VariantScopedBossBranch branch in selected)
            {
                bool parentMustBeEncoded = runtimeVariants.Count > 1;
                if (!TryResolveAllowedBosses(
                        branch.FirstBoss,
                        runtimeVariants[branch.VariantKey],
                        parentMustBeEncoded,
                        out IReadOnlyList<ModelKey> allowed,
                        out string predicateIssue))
                {
                    filters = Array.Empty<ActOrdinalModelKeySetFilter>();
                    fidelity = ProjectionFidelity.Unsupported;
                    issue = $"VariantProjectionBossPredicateUnsupported:Act{actGroup.Key}:Boss1:{branch.VariantKey.Serialized}:{predicateIssue}";
                    return false;
                }
                first.AddRange(allowed);
            }
            ModelKey[] firstKeys = first
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (firstKeys.Length > 0)
            {
                output.Add(new ActOrdinalModelKeySetFilter(
                    actGroup.Key,
                    1,
                    new ModelKeySetFilter(firstKeys, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())));
            }

            if (selected.Any(branch => branch.IncludesSecondBoss))
            {
                VariantScopedBossBranch[] secondBranches = selected.Where(branch => branch.IncludesSecondBoss).ToArray();
                bool hasConstrainedSecond = secondBranches.Any(branch => !branch.SecondBoss.IsEmpty);
                bool hasUnconstrainedSecond = secondBranches.Any(branch => branch.SecondBoss.IsEmpty);
                if (runtimeVariants.Count > 1 && hasConstrainedSecond && hasUnconstrainedSecond)
                {
                    filters = Array.Empty<ActOrdinalModelKeySetFilter>();
                    fidelity = ProjectionFidelity.Unsupported;
                    issue = $"VariantProjectionPartialSecondBossBranchUnsupported:Act{actGroup.Key}";
                    return false;
                }

                var second = new List<ModelKey>();
                foreach (VariantScopedBossBranch branch in secondBranches)
                {
                    // Empty Boss2 is semantically true within that Variant. The
                    // current legacy UI flatten can preserve that only when every
                    // active branch is likewise unconstrained; mixed constrained /
                    // unconstrained Boss2 branches fail closed above.
                    if (branch.SecondBoss.IsEmpty) continue;
                    if (!TryResolveAllowedBosses(
                            branch.SecondBoss,
                            runtimeVariants[branch.VariantKey],
                            true,
                            out IReadOnlyList<ModelKey> allowed,
                            out string predicateIssue))
                    {
                        filters = Array.Empty<ActOrdinalModelKeySetFilter>();
                        fidelity = ProjectionFidelity.Unsupported;
                        issue = $"VariantProjectionBossPredicateUnsupported:Act{actGroup.Key}:Boss2:{branch.VariantKey.Serialized}:{predicateIssue}";
                        return false;
                    }
                    second.AddRange(allowed);
                }
                ModelKey[] secondKeys = second
                    .Where(key => key.IsValid)
                    .Distinct(ModelKeyComparer.Instance)
                    .ToArray();
                if (secondKeys.Length > 0)
                {
                    output.Add(new ActOrdinalModelKeySetFilter(
                        actGroup.Key,
                        2,
                        new ModelKeySetFilter(secondKeys, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())));
                }
            }
        }

        filters = output;
        return true;
    }

    private static bool TryResolveAllowedBosses(
        ModelKeySetFilter predicate,
        IReadOnlyList<ModelKey> runtimePool,
        bool parentMustBeEncoded,
        out IReadOnlyList<ModelKey> allowed,
        out string issue)
    {
        issue = string.Empty;
        IEnumerable<ModelKey> candidates = runtimePool.Where(key => key.IsValid);

        if (predicate.All.Count > 1)
        {
            allowed = Array.Empty<ModelKey>();
            issue = "SingleValueAllCardinalityConflict";
            return false;
        }
        if (predicate.All.Count == 1)
            candidates = candidates.Where(key => key == predicate.All[0]);
        if (predicate.Any.Count > 0)
            candidates = candidates.Where(key => predicate.Any.Contains(key, ModelKeyComparer.Instance));
        if (predicate.Ban.Count > 0)
            candidates = candidates.Where(key => !predicate.Ban.Contains(key, ModelKeyComparer.Instance));

        ModelKey[] resolved = candidates.Distinct(ModelKeyComparer.Instance).ToArray();
        if (!predicate.IsEmpty && resolved.Length == 0)
        {
            allowed = Array.Empty<ModelKey>();
            issue = "PredicateHasNoRuntimeBoss";
            return false;
        }

        if (predicate.IsEmpty && !parentMustBeEncoded)
        {
            allowed = Array.Empty<ModelKey>();
            return true;
        }

        allowed = resolved;
        return true;
    }

    private static bool TryGetRuntimeVariants(
        RuntimeContextAuthoritySnapshot authority,
        int act,
        out IReadOnlyDictionary<ModelKey, IReadOnlyList<ModelKey>> variants)
    {
        var output = new Dictionary<ModelKey, IReadOnlyList<ModelKey>>(ModelKeyComparer.Instance);
        Beta109WorldGenerationSnapshot? modern = authority.WorldAuthority?.Beta109Generation;
        if (modern is not null)
        {
            Beta109ActSelectionGroupSnapshot? group = modern.ActSelectionGroups.FirstOrDefault(item => item.Act == act);
            IEnumerable<ModelKey> eligible = group?.EligibleActsInSourceOrder.Count > 0
                ? group.EligibleActsInSourceOrder
                : modern.OrderedActCatalog.Where(item => item.Act == act).Select(item => item.ActKey);
            foreach (ModelKey key in eligible.Distinct(ModelKeyComparer.Instance))
            {
                Beta109ActGenerationSnapshot? row = modern.OrderedActCatalog.FirstOrDefault(item => item.Act == act && item.ActKey == key);
                if (row is null || row.Bosses.Count == 0) continue;
                output[key] = row.Bosses.Distinct(ModelKeyComparer.Instance).ToArray();
            }
        }
        else if (authority.WorldAuthority?.ActGroups is { } legacy)
        {
            WorldActGroupSnapshot? group = legacy.FirstOrDefault(item => item.Act == act);
            if (group is not null)
            {
                foreach (WorldActSnapshot row in group.Acts)
                    output[row.ActKey] = row.Bosses.Distinct(ModelKeyComparer.Instance).ToArray();
            }
        }

        variants = output;
        return output.Count > 0;
    }

    private static bool BossPoolsArePairwiseDisjoint(IEnumerable<IReadOnlyList<ModelKey>> pools)
    {
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (IReadOnlyList<ModelKey> pool in pools)
        {
            foreach (ModelKey key in pool)
            {
                if (!seen.Add(key)) return false;
            }
        }
        return true;
    }

    private static ProjectionFidelity MaxFidelity(ProjectionFidelity left, ProjectionFidelity right) =>
        (ProjectionFidelity)Math.Max((int)left, (int)right);
}
