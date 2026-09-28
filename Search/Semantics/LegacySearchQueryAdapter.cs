using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// Compatibility ingress for historical/programmatic callers that only own the
/// already-flattened legacy execution DTO. It never attempts to reconstruct UI-only
/// parent/branch intent that the legacy DTO no longer contains.
/// </summary>
public static class LegacySearchQueryAdapter
{
    public static SearchQuery FromFilter(NeowSearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        OpeningRouteRelicRequirement? bonesRequirement = null;
        if (filter.NeowRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
            filter.RequiredBonesCombination.Count > 0)
        {
            bool exactOrder = filter.RequiredBonesAcquisitionOrder.Count == 2;
            IReadOnlyList<ModelKey> relics = exactOrder
                ? filter.RequiredBonesAcquisitionOrder
                : filter.RequiredBonesCombination;
            bonesRequirement = new OpeningRouteRelicRequirement(
                BaseGameModelKeys.Relics.NeowsBones,
                relics,
                exactOrder ? BonesRouteOrderMode.ExactOrder : BonesRouteOrderMode.AnyOrder);
        }

        return new SearchQuery(
            filter.NeowRoute,
            bonesRequirement,
            filter.StructuredNeowEffects,
            Array.Empty<VariantScopedBossBranch>(),
            filter.AncientBranchConditions,
            filter.RelicSequenceConditions,
            filter.EventSequenceConditions,
            filter.NormalCombatRewardConditions,
            new LegacyNeowSemanticConstraints(
                filter.NeowRelics,
                filter.RequireNeowsBones,
                filter.BonesRelics,
                filter.RequiredBonesCombination,
                filter.RequireSmallCapsule,
                filter.RequireLargeCapsule,
                filter.CapsuleContainedRelics,
                filter.RequireWhetstone,
                filter.RequireWarPaint,
                filter.RequiredFinalCurse,
                filter.BannedFinalCurses,
                filter.Preset,
                filter.RequiredBonesAcquisitionOrder,
                filter.EffectOutputConditions),
            new LegacyWorldSemanticConstraints(
                filter.BossFilters,
                filter.BossOrdinalFilters,
                filter.AncientIdentityFilters,
                filter.AncientOptionFilters,
                filter.AncientSeaGlassTargetFilters))
        {
            CombatCardRewards = filter.CombatCardRewardSequence,
            CombatPotionRewards = filter.CombatPotionRewardSequence,
            EventResultConditions = filter.EventResultConditions,
            MerchantColorlessConditions = filter.MerchantColorlessConditions,
            MerchantColorlessSequenceConditions = filter.MerchantColorlessSequenceConditions,
            RelicShopSequenceConditions = filter.RelicShopSequenceConditions
        };
    }
}
