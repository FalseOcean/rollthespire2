using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class CombatRewardProbabilityEstimator
{
    private static SearchSelectivityEstimate EstimateParty(SearchSelectivityInput plan, ProbabilitySemanticView semantic,
        MatcherContext matcher, CardPoolModel cards, PotionPoolModel potions)
    {
        SearchSelectivityEstimate Unknown(string issue) => SearchSelectivityEstimate.Unpriced(
            "Probability.CombatReward.Party." + issue, "Conditional reward probability lacks authority; the query remains executable.");
        var filter = semantic.NumericalFilter;
        var route = semantic.NormalizedQuery.OpeningRoute?.RouteRelicKey ?? filter.NeowRoute?.RouteRelicKey;
        var openings = new List<ModelKey[]>();
        if (route is null) openings.Add([]); // Owner's unselected-N neutral premise.
        else if (route != BaseGameModelKeys.Relics.NeowsBones) openings.Add([route.Value]);
        else
        {
            var authority = plan.Authority.EffectAuthority;
            if (authority?.HasExactBonesPools != true || authority.BonesEligibleRelics is null) return Unknown("BonesPoolAuthorityMissing");
            var pool = authority.BonesEligibleRelics.Where(k => k != BaseGameModelKeys.Relics.NeowsBones).Distinct().ToArray();
            var required = ExplicitBonesRelics(filter).Concat(filter.StructuredNeowEffects
                .Where(c => !c.IsEmpty && c.SourceRelicKey != BaseGameModelKeys.Relics.NeowsBones).Select(c => c.SourceRelicKey)).Distinct().ToList();
            if (filter.RequireSmallCapsule || filter.StructuredNeowEffects.Any(FamilyExecution.NeowReplayPlan.IsGroupedCapsule)) required.Add(BaseGameModelKeys.Relics.SmallCapsule);
            if (filter.RequireLargeCapsule || filter.StructuredNeowEffects.Any(FamilyExecution.NeowReplayPlan.IsGroupedCapsule)) required.Add(BaseGameModelKeys.Relics.LargeCapsule);
            for (int i = 0; i < pool.Length; i++) for (int j = i + 1; j < pool.Length; j++)
            {
                ModelKey[] pair = [pool[i], pool[j]];
                if (required.All(pair.Contains) && PartyInitialQuery.Matches(filter.BonesRelics, pair)) openings.Add(pair);
            }
        }
        // RNG-only opening consumption uses the approved marginal approximation.
        // Group equivalent held states; never run the reward DP once per Bones pair.
        var groups = new Dictionary<(int Capsules, RewardInfluenceProfile Held), int>();
        foreach (var opening in openings)
        {
            int capsules = 0; RewardInfluenceProfile held = default;
            foreach (var key in opening)
            {
                if (key == BaseGameModelKeys.Relics.SmallCapsule) capsules |= 1;
                else if (key == BaseGameModelKeys.Relics.LargeCapsule) capsules |= 2;
                else if (!TryPartyHeldProfile(plan, key, matcher, ref held)) return Unknown("HeldEffect:" + key.Entry);
            }
            groups[(capsules, held)] = groups.GetValueOrDefault((capsules, held)) + 1;
        }
        var profileMass = new Dictionary<RewardInfluenceProfile, double>();
        double accepted = 0;
        foreach (var (group, count) in groups)
        {
            var sources = new List<ModelKey>();
            if ((group.Capsules & 1) != 0) sources.Add(BaseGameModelKeys.Relics.SmallCapsule);
            if ((group.Capsules & 2) != 0) sources.Add(BaseGameModelKeys.Relics.LargeCapsule);
            bool hasBagCondition = route is not null && (filter.RelicSequenceConditions.Any(c => !c.IsEmpty) || !filter.CapsuleContainedRelics.IsEmpty ||
                filter.RequireWhetstone || filter.RequireWarPaint || filter.StructuredNeowEffects.Any(FamilyExecution.NeowReplayPlan.IsCapsule));
            if (sources.Count == 0 && !hasBagCondition) { accepted += count; AddMass(profileMass, group.Held, count); continue; }
            if (!TryBuildCapsuleInfluenceDistribution(plan, matcher, group.Held, sources,
                out var profiles, out _, out string issue, out double conditionalMass)) return Unknown(issue);
            accepted += count * conditionalMass;
            foreach (var profile in profiles) AddMass(profileMass, profile.Profile, count * conditionalMass * profile.Mass);
        }
        double probability = accepted == 0 ? 0 : profileMass.Where(p => p.Value > 0)
            .Sum(p => p.Value / accepted * EstimateForProfile(plan.Ascension, cards, potions, matcher, p.Key));
        var parents = new List<SearchSelectivityDomain>();
        if (route is not null) parents.Add(SearchSelectivityDomain.Neow);
        if (filter.RelicSequenceConditions.Any(c => !c.IsEmpty)) parents.Add(SearchSelectivityDomain.Relic);
        return SearchSelectivityEstimate.Exact(probability, SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            parents.Count == 0 ? SearchSelectivityDependencyClass.AssumedIndependent : SearchSelectivityDependencyClass.RouteDependent,
            "Probability.CombatReward.PartyHeldMixture",
            $"Runtime multiplayer pools; openingPairs={openings.Count};heldProfiles={profileMass.Count};conditionalMass={accepted:G17}.",
            ["CrossPlayerIndependence=OwnerApprovedApproximation", "OpeningRngDrawOffsetsDoNotChangeMarginalModel",
                "ActualCapsuleHeldRewardEffectsIntegrated=true", "UnspecifiedObtainHooksSkipped;AuthoredPoolChangesRequireAuthority",
                "ConditionalOnAuthoredOpeningAndInitialBag;NoRepeatedParentProbability", "CardAndPotionChronologicalPityRetained"],
            conditionedOnDomains: parents) with { Confidence = SearchSelectivityConfidence.Medium };
    }

    private static bool TryPartyHeldProfile(SearchSelectivityInput plan, ModelKey key, MatcherContext matcher, ref RewardInfluenceProfile profile)
    {
        if (IsRelevantImpactRelic(key, matcher)) { profile = profile.Add(key); return true; }
        if (!VanillaRelicRewardEffects.TryGet(plan.ProfileId, key, out var effect)) return false;
        if (effect.IsHeldNeutralForNormalCombatReward) return true;
        // Deterministic upgrades/enchantments preserve raw card identity.
        var relevant = HeldNormalCombatRewardEffects.None;
        if (matcher.UsesCards) relevant |= HeldNormalCombatRewardEffects.ChangesCardRewardPool |
            HeldNormalCombatRewardEffects.ChangesCardRewardCount | HeldNormalCombatRewardEffects.ChangesCardRewardOptions |
            HeldNormalCombatRewardEffects.AddsRewardEntries | HeldNormalCombatRewardEffects.RemovesRewardEntries |
            HeldNormalCombatRewardEffects.ReplacesRewardEntries | HeldNormalCombatRewardEffects.AddsCardRewardAlternative;
        if (matcher.UsesPotions) relevant |= HeldNormalCombatRewardEffects.ForcesPotionReward |
            HeldNormalCombatRewardEffects.ChangesPotionDropState | HeldNormalCombatRewardEffects.ChangesPotionRewardPool;
        if (matcher.UsesGold) relevant |= HeldNormalCombatRewardEffects.ChangesGoldReward;
        return (effect.HeldCapabilities & (relevant | HeldNormalCombatRewardEffects.UnknownModHook)) == 0;
    }
}
