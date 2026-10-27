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

        // Preserve parent-scoped alternatives. Flattening them into independent
        // Boss ordinal sets loses the same-row witness and requires facts that
        // may themselves be unknown. Final evaluation observes the typed rows.
        if (query.VariantBossBranches.Count > 0) diagnostics.Add("VariantBossProjection=ExactTypedBranches");

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
            BossOrdinalFilters = oldWorld.BossOrdinalFilters,
            VariantBossBranches = query.VariantBossBranches,
            AncientBranchConditions = query.AncientBranches,
            AncientIdentityFilters = oldWorld.AncientIdentityFilters,
            AncientOptionFilters = oldWorld.AncientOptionFilters,
            AncientSeaGlassTargetFilters = oldWorld.AncientSeaGlassTargetFilters,
            AncientOptionConditions = context.EvaluationAssumptions.AncientEligibilityAssumptions,
            TreasureRelics = query.TreasureRelics,
            FirstActUnknownEventPrefixCount = query.FirstActUnknownEventPrefixCount,
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
            CombatCardExclusion = query.CombatCardExclusion,
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

}
