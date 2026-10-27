using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

// Prices the same fresh Act 1 room-entry premise as ForAct1Filter. Shop remains
// a possible Unknown outcome; only route-dependent adjacent-Shop blocking is absent.
internal static class FirstActUnknownEventProbability
{
    internal const string ComponentId = "E.FirstActUnknownEventPrefix";
    private static readonly ModelKey Juzu = new("RELIC", "JUZU_BRACELET");

    internal static double ConditionalPrefix(int count, bool juzuAcquired)
    {
        if (count is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(count));
        float monster = .1f, treasure = .02f, shop = .03f;
        double probability = 1;
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            // Every prior outcome in this branch is Event. Unrolled allowed room
            // types gain their base odds, matching the game's float accumulation.
            float total = juzuAcquired ? 0f : monster;
            total += treasure;
            total += shop;
            probability *= Math.Clamp(1d - total, 0d, 1d);
            if (!juzuAcquired) monster += .1f;
            treasure += .02f;
            shop += .03f;
        }
        return probability;
    }

    internal static JointSelectivityComponent EstimateComponent(SearchSelectivityInput input)
    {
        var query = input.CompiledSearch.NormalizedQuery;
        int count = Math.Max(query.FirstActUnknownEventPrefixCount,
            query.Players.Select(player => player.Conditions.FirstActUnknownEventPrefixCount).DefaultIfEmpty(0).Max());
        bool juzu = ExplicitOpeningRelicFacts.EnumerateGuaranteed(query).Contains(Juzu) || query.Players.Any(player =>
            ExplicitOpeningRelicFacts.EnumerateGuaranteed(player.Conditions).Contains(Juzu));
        bool supported = input.ProfileId == RuntimeProfileId.Beta111 && count is >= 1 and <= 3;
        string issue = input.ProfileId != RuntimeProfileId.Beta111 ? "ProfileUnsupported" :
            count is < 1 or > 3 ? "CountUnsupported" :
            "ConditionalEventPrefix";
        string[] assumptions =
        [
            "UnknownRoomPremise=Act1;NonTutorial;FreshOdds;EnteredUnknownOrdinal;NoEventSideEffects",
            "UnknownRoomShopPremise=NoAdjacentShopBlacklist;ShopOutcomeAndGrowingOddsRetained",
            "UnknownRoomJuzu=" + juzu,
            "UnspecifiedOpeningOutcomesHaveNoInfluence=OwnerApproved",
            "PartySharedPrefixPaidOnceAtMaximumRequestedCount=true",
            "UnknownRoomProbability=ConditionalStateRecurrence;ContinuousUniformDrawModel;NotFiniteSeedCount",
            "UnknownRoomCrossDomainPolicy=AssumedIndependentAfterConditioningOnAuthoredOpening",
            "UnknownRoomEstimateDoesNotChangeExactOrAddRoutePredicates=true"
        ];
        return new(ComponentId, "FirstActUnknownEventPrefix", "AuthoredOpening",
            supported ? ConditionalPrefix(count, juzu) : null,
            "Probability.FirstActUnknownEventPrefix." + issue,
            JointSelectivityCombinationMethod.ConditionalChain, SearchSelectivityDependencyClass.AssumedIndependent,
            supported, supported, issue, assumptions);
    }

    internal static JointSelectivityResult Compose(SearchSelectivityInput input,
        Func<SearchSelectivityInput, JointSelectivityResult> estimate)
    {
        var compiled = input.CompiledSearch;
        var query = compiled.NormalizedQuery with { FirstActUnknownEventPrefixCount = 0,
            Players = compiled.NormalizedQuery.Players.Select(player => player with {
                Conditions = player.Conditions with { FirstActUnknownEventPrefixCount = 0 } }).ToArray() };
        var remainder = compiled.Context.Party is null ? SearchCompiler.Compile(query, compiled.Context) :
            PartyInitialQuery.Compile(query, compiled.Context);
        var baseline = estimate(SearchSelectivityInput.From(remainder));
        return Combine(baseline, EstimateComponent(input), "FirstActUnknownEventPrefix");
    }

    internal static SearchSelectivityEstimate Estimate(SearchSelectivityInput input)
    {
        var component = EstimateComponent(input);
        return component.Probability is { } p ? SearchSelectivityEstimate.Exact(p,
            SearchSelectivityMethod.ConditionalChain, SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.AssumedIndependent, component.AuthoritySource, component.BranchReason, component.Assumptions) :
            SearchSelectivityEstimate.Unpriced(component.AuthoritySource, component.BranchReason);
    }

    internal static JointSelectivityResult Combine(JointSelectivityResult baseline, JointSelectivityComponent component, string name)
    {
        // A trustworthy independent contradiction remains decisive.
        if (baseline.ExactlyImpossible) return baseline;
        var components = baseline.KnownComponents.Append(component).ToArray();
        var assumptions = baseline.Assumptions.Concat(component.Assumptions).Distinct(StringComparer.Ordinal).ToArray();
        if (component.Probability is not { } prefix || baseline.Probability is not { } other)
            return JointSelectivityResult.Partial("Probability." + name + ".Partial",
                "A captured pool or another authored predicate remains unpriced.",
                "P(Query)=P(remainder) × P(component), under the approved independence model.", components,
                baseline.UnknownComponents.Concat(component.Probability.HasValue ? [] : new[] { component.AuthoritySource }).ToArray(),
                baseline.DependencyGraph, baseline.Branches, assumptions);
        return JointSelectivityResult.Exact(other * prefix, JointSelectivityCombinationMethod.ConditionalChain,
            "Probability." + name + ".ModeledJoint",
            "The component and remaining authored predicates use the approved independence model.",
            "P(remainder) × P(component)",
            baseline.DependencyGraph, components, baseline.Branches, assumptions, "ModeledConditionalPrefixAndExistingRemainder")
            with { Confidence = SearchSelectivityConfidence.Medium };
    }

}
