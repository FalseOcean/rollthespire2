using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class EventResultProbabilityEstimator
{
    // Alternative branches of the same event share a stream. Until their joint
    // distribution is modeled, retain an unknown estimate instead of multiplying
    // marginals. This never changes execution or the authored predicates.
    private static bool HasUnpricedOpportunityDependence(IReadOnlyList<EventResultSearchCondition> conditions)
    {
        if (conditions.Any(c => c.Kind == EventResultConditionKind.DollRoomRandomRelic) &&
            conditions.Any(c => c.Kind == EventResultConditionKind.DollRoomTakeSomeTimeRelic)) return true;
        var rarity = conditions.Where(c => c.Kind == EventResultConditionKind.EventRelicRarityRoll).ToArray();
        if (rarity.GroupBy(c => c.TargetKey).Any(g => g.Distinct().Count() > 1)) return true;
        return rarity.Any(c => c.TargetKey.Entry == "TRIAL") && conditions.Any(c =>
            c.Kind is EventResultConditionKind.TrialCase or EventResultConditionKind.TrialNondescriptInitialBasicsContains);
    }

    private static SearchSelectivityEstimate? EstimateOpportunity(SearchSelectivityInput plan, EventResultSearchCondition condition)
    {
        if ((EventOpportunitySemantics.IsEventLocalOpportunity(condition.Kind) ||
             condition.Kind == EventResultConditionKind.CrystalSphereRewards) &&
            !EventOpportunitySemantics.SupportsProfile(condition.Kind, plan.Authority.ProfileId,
                plan.Authority.VersionResolution.SemanticProfileId))
            return SearchSelectivityEstimate.Unpriced("Probability.EventResult.OpportunityRulesUnavailable",
                "The event-local opportunity rule is unavailable for this game version.");
        double probability;
        switch (condition.Kind)
        {
            case EventResultConditionKind.DollRoomRandomRelic:
            case EventResultConditionKind.DollRoomTakeSomeTimeRelic:
            case EventResultConditionKind.MuseumOfTortureOfferedRelic:
            case EventResultConditionKind.ThermaeNonupeipeDrinkRelic:
                var pool = EventOpportunitySemantics.RelicTargets(condition.Kind);
                probability = !pool.Contains(condition.TargetKey) ? 0 :
                    (condition.Kind == EventResultConditionKind.DollRoomTakeSomeTimeRelic ? 2d : 1d) / pool.Count;
                break;
            case EventResultConditionKind.EventRelicRarityRoll:
                double belowCommon = FloatBelowProbability(0.5f), belowRare = FloatBelowProbability(0.83f);
                double single = condition.RelicRarity switch
                {
                    EffectRelicRarity.Common => belowCommon,
                    EffectRelicRarity.Uncommon => belowRare - belowCommon,
                    EffectRelicRarity.Rare => 1 - belowRare,
                    _ => throw new InvalidOperationException("EventResult.InvalidRelicRarity")
                };
                int count = EventOpportunityCatalog.RarityBranches.Single(opportunity =>
                    opportunity.Branch == condition.RelicRarityBranch).RollCount;
                probability = condition.RelicRaritySelection switch
                {
                    EventRelicRaritySelection.Any => 1 - Math.Pow(1 - single, count),
                    EventRelicRaritySelection.All => Math.Pow(single, count),
                    _ => single
                };
                if (condition.RelicRarityBranch == EventRelicRarityBranch.MerchantGuilty) probability /= 3;
                break;
            case EventResultConditionKind.CrystalSphereRewards:
                if (condition.CrystalDivinations < CrystalSphereSeedSearch.MinimumRelicDivinations && condition.RelicRarity.HasValue)
                    return SearchSelectivityEstimate.Exact(0, SearchSelectivityMethod.StructuralAssignment,
                        SearchSelectivityCoverage.ExactRequestedPredicate, SearchSelectivityDependencyClass.StructuralDependence,
                        "Probability.EventResult.CrystalRelicRequiresFourMoves",
                        "A 4x4 relic has four corners; each native reveal reaches at most one, so three moves cannot complete it.");
                return SearchSelectivityEstimate.Unpriced("Probability.EventResult.CrystalGeometryUnpriced",
                    "Same-board attainable reward combinations are solved per candidate; no measured seed probability is available.");
            default:
                return null;
        }
        return SearchSelectivityEstimate.Exact(probability, SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedPredicate, SearchSelectivityDependencyClass.UnknownDependence,
            "Probability.EventResult.OpportunityConditional",
            "Conditional on the declared event entry and branch. Relic rarity denotes its roll, before bag fallback.",
            ["EventOccurrenceNotProven=true", "NormalEventEntryPremises=true", "RelicBagFallbackExcluded=true"]);
    }

    private static double FloatBelowProbability(float threshold)
    {
        // NextDouble is a uniform 53-bit grid before the native float rounding.
        const double grid = 9007199254740992d;
        double boundary = ((double)threshold + MathF.BitDecrement(threshold)) * 0.5d;
        double count = Math.Ceiling(boundary * grid);
        // At the midpoint, ties round to the even float mantissa. An odd upper
        // threshold rounds downward and therefore includes this one grid point.
        if ((BitConverter.SingleToInt32Bits(threshold) & 1) != 0 && boundary * grid == Math.Floor(boundary * grid)) count++;
        return count / grid;
    }
}
