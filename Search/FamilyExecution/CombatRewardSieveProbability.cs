using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// The estimate uses the Owner's fixed independent/empty-slot-neutral premises.
// C checks every positive typed predicate; Exact-only exclusion is not its filter.
// Party query-literal opening replay uses these same premises. Removing latent
// obtains does not add a mixture, recall correction or observed-survival fit.
// FamilyModels overrides this with s=1 when an unresolved explicit opening
// makes the actual implementation conservatively retain every input.
internal static class CombatRewardSieveProbability
{
    internal static SearchSelectivityEstimate Estimate(ExactSearchExecutionRequest request, CombatRewardReplay replay) =>
        Estimate(request, replay.Plan);

    internal static SearchSelectivityEstimate Estimate(ExactSearchExecutionRequest request, Beta110CombatRewardFastPlan fast) =>
        CombatRewardProbabilityEstimator.EstimateOptimizedConditions(SearchSelectivityInput.From(request));

    // Exact physical quote scope: these modifiers are modeled after a proved
    // fixed prefix. Product approximations do not widen this list or its bounds.
    internal static bool HasModeledRewardImpact(Beta110CombatRewardExplicitContext context) =>
        context.AdditionalCardRewardCount <= 1 && context.FixedGoldAmount == 0 &&
        (context.InfluenceFlags & ~(Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward |
            Beta110CombatRewardInfluenceFlags.ForcePotionReward |
            Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard |
            Beta110CombatRewardInfluenceFlags.DeterministicUpgradeOnly |
            Beta110CombatRewardInfluenceFlags.DeterministicEnchantmentOnly)) == 0;

    internal static int? FixedAuthoredPrefixDrawCount(ExactSearchExecutionRequest request, Beta110CombatRewardFastPlan fast)
    {
        // The solo query-literal donor replays three rarity rolls for Small+Large,
        // without obtaining latent bag outputs. This existing solo price scope
        // does not certify the changed Party query-literal opening body.
        if (request.ProfileId == RuntimeProfileId.Beta111 && request.Authority.CanUseCurrentModel &&
            request.Authority.PlayersCount == 1 && request.Authority.NoRunModifiers == true &&
            fast.OpeningConsumption.ReplayBonesOffer && fast.OpeningConsumption.OrderedRelicIds.Length == 2 &&
            fast.OpeningConsumption.OrderedRelicIds.ToHashSet().SetEquals([Beta110FastRelicCatalog.SmallCapsule, Beta110FastRelicCatalog.LargeCapsule]) &&
            HasModeledRewardImpact(fast.ExplicitContext) &&
            request.Evaluation.StructuredNeowEffects.All(c => c.Scope == NeowStructuredEffectScope.FinalCurse || NeowReplayPlan.IsCapsule(c)))
            return 3;
        if (!(request.ProfileId == RuntimeProfileId.Beta111 && request.Authority.CanUseCurrentModel && request.Authority.NoRunModifiers == true &&
        fast.OpeningConsumption.HasReplay && fast.OpeningConsumption.OrderedRelicIds.Length is 1 or 2 &&
        fast.OpeningConsumption.OrderedRelicIds.All(id => id <= Beta110FastRelicCatalog.Pomander && id is not (
            Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule or
            Beta110FastRelicCatalog.HeftyTablet or
            Beta110FastRelicCatalog.LeadPaperweight or Beta110FastRelicCatalog.LostCoffer or
            Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.ScrollBoxes)) &&
        request.Evaluation.StructuredNeowEffects.All(c => c.Scope == NeowStructuredEffectScope.FinalCurse))) return null;
        int draws = fast.OpeningConsumption.OrderedRelicIds.Count(id => id == Beta110FastRelicCatalog.ArcaneScroll);
        if (draws > 1 || draws == 1 && (request.Authority.EffectAuthority?.HasExactCharacterRewardPool != true ||
            !request.Authority.EffectAuthority.CharacterRewardPool!.Any(c => c.Rarity == EffectCardRarity.Rare))) return null;
        return draws;
    }
}
