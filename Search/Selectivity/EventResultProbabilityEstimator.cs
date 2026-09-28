using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Conditional Event Result v1 marginals only. This estimator deliberately owns no
/// Event occurrence probability and registers no independence relation to World/Event.
/// </summary>
internal static partial class EventResultProbabilityEstimator
{
    public static SearchSelectivityEstimate? EstimateTrashHeapJoint(
        SearchSelectivityInput plan,
        EventResultSearchCondition? grab,
        EventResultSearchCondition? dive)
    {
        if (grab is null || dive is null ||
            grab.Kind != EventResultConditionKind.TrashHeapGrabCard ||
            dive.Kind != EventResultConditionKind.TrashHeapDiveRelic ||
            !grab.IsValid || !dive.IsValid)
            return null;

        int grabIndex = IndexOf(Beta111EventResultCatalog.TrashHeapGrabCards, grab.TargetKey);
        int diveIndex = IndexOf(Beta111EventResultCatalog.TrashHeapDiveRelics, dive.TargetKey);
        double probability = grabIndex >= 0 && diveIndex == grabIndex / 2 ? 1d / 10d : 0d;
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.AuthorityPoolMembership,
            SearchSelectivityCoverage.ExactRequestedPredicate,
            SearchSelectivityDependencyClass.SharedContinuation,
            "Probability.EventResult.TrashHeapGrabDiveJoint.Conditional",
            probability > 0d
                ? "Conditional on Trash Heap result generation: Grab and Dive share one local root; DiveIndex=floor(GrabIndex/2)."
                : "Conditional on Trash Heap result generation: the selected Grab/Dive pair is not a corresponding shared-root pair.",
            new[]
            {
                "ConditionalEventResultOnly=true",
                "TrashHeapSharedLocalRoot=true",
                "TrashHeapDiveIndex=floor(GrabIndex/2)",
                "NaiveMarginalMultiplicationForbidden=true"
            });
    }

    private static int IndexOf(IReadOnlyList<ModelKey> values, ModelKey target)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i] == target) return i;
        return -1;
    }

    internal static SearchSelectivityEstimate? EstimateBlock(
        SearchSelectivityInput plan,
        IReadOnlyList<EventResultSearchCondition> conditions,
        out string issue)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(conditions);
        issue = string.Empty;
        // Deduplicate scalar facts before coupling Grab/Dive or Trial case/results.
        var normalized = new List<EventResultSearchCondition>();
        foreach (var group in conditions.GroupBy(c => c.Kind))
        {
            if (group.Key is EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.TrialCase)
            {
                var first = group.First();
                if (group.Any(c => c.TargetKey != first.TargetKey || c.TrialCase != first.TrialCase))
                    return SearchSelectivityEstimate.Exact(0, SearchSelectivityMethod.AuthorityPoolMembership,
                        SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                        "Probability.EventResult.ScalarConflict", "A single result cannot have two different identities.");
                normalized.Add(first);
            }
            else normalized.AddRange(group);
        }
        conditions = normalized;
        var grouped = conditions.GroupBy(c => c.Kind).Where(g => g.Count() > 1).ToArray();
        var groupFactors = new List<SearchSelectivityEstimate>();
        foreach (var group in grouped)
        {
            var joint = EstimateSameResult(plan, group.ToArray());
            if (joint is null) { issue = "RepeatedEventResultLocalGroup:" + group.Key; return null; }
            groupFactors.Add(joint);
        }
        var singles = conditions.Where(c => !grouped.Any(g => g.Key == c.Kind)).ToArray();
        EventResultSearchCondition[] grabs = singles
            .Where(item => item.Kind == EventResultConditionKind.TrashHeapGrabCard)
            .ToArray();
        EventResultSearchCondition[] dives = singles
            .Where(item => item.Kind == EventResultConditionKind.TrashHeapDiveRelic)
            .ToArray();
        EventResultSearchCondition[] fake = singles
            .Where(item => item.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic)
            .ToArray();
        EventResultSearchCondition[] colorful = singles
            .Where(item => item.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor)
            .ToArray();
        var morphic = singles.Where(item => item.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains).ToArray();
        var added = singles.Where(c => c.Kind > EventResultConditionKind.MorphicGroveGroupInitialBasicsContains).ToArray();
        var factors = new List<SearchSelectivityEstimate>(groupFactors);
        if (grabs.Length == 1 && dives.Length == 1)
        {
            SearchSelectivityEstimate? trash = EstimateTrashHeapJoint(plan, grabs[0], dives[0]);
            if (trash is null)
            {
                issue = "TrashHeapSharedLocalJointUnavailable";
                return null;
            }
            factors.Add(trash);
        }
        else if (grabs.Length == 1)
        {
            factors.Add(EstimateSingle(plan, grabs[0]));
        }
        else if (dives.Length == 1)
        {
            factors.Add(EstimateSingle(plan, dives[0]));
        }

        if (fake.Length == 1) factors.Add(EstimateSingle(plan, fake[0]));
        if (colorful.Length == 1) factors.Add(EstimateSingle(plan, colorful[0]));
        if (morphic.Length == 1) factors.Add(EstimateSingle(plan, morphic[0]));
        foreach (var c in added)
        {
            // Trial's transform already includes the very same case draw.
            if (c.Kind == EventResultConditionKind.TrialCase && conditions.Any(x => x.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains))
            {
                if (c.TrialCase == TrialCaseTarget.Nondescript) continue;
                factors.Add(SearchSelectivityEstimate.Exact(0, SearchSelectivityMethod.AuthorityPoolMembership,
                    SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.SharedContinuation,
                    "Probability.EventResult.TrialCaseConflict", "One Trial cannot show two different cases.", []));
            }
            else factors.Add(EstimateSingle(plan, c));
        }
        if (factors.Count == 0)
        {
            issue = "NoSupportedEventResultFactor";
            return null;
        }
        if (factors.Any(item => !item.IsPriced || !item.Probability.HasValue))
        {
            issue = factors.First(item => !item.IsPriced || !item.Probability.HasValue).EvidenceCode;
            return null;
        }
        if (factors.Count == 1) return factors[0];

        double probability = factors.Aggregate(1d, (current, item) => current * item.Probability!.Value);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.NamedStreamIndependenceModel,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.AssumedIndependent,
            "Probability.EventResult.CrossGroupAssumedIndependent",
            "Distinct Event Result local groups are combined by the current Probability assumed-independent policy; Trash Heap Grab+Dive is normalized as one shared-root block first.",
            factors.SelectMany(item => item.Assumptions)
                .Concat(new[]
                {
                    "EventResultDistinctGroupsAssumedIndependent=true",
                    "EventResultProbabilityOnly=true",
                    "SearchExactWitnessAuthorityUnaffected=true"
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static SearchSelectivityEstimate? EstimateSameResult(SearchSelectivityInput plan, EventResultSearchCondition[] group)
    {
        var first = group[0]; double p;
        bool transform = EventResultTransformSemantics.IsTransform(first.Kind);
        if (transform)
        {
            return EstimateTransformJoint(group);
        }
        else if (first.Kind is EventResultConditionKind.FakeMerchantOfferedFakeRelic or EventResultConditionKind.ColorfulPhilosophersOfferedColor)
        {
            var authority = Beta111EventResultAuthority.From(plan.Authority);
            ModelKey[] pool; int take;
            if (first.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic)
            { pool = Beta111EventResultCatalog.FakeMerchantRelics.ToArray(); take = Math.Min(6,pool.Length); }
            else
            {
                if (!authority.ColorfulPoolAuthorityExact) return null;
                pool = Beta111EventResultCatalog.ColorfulCharacterOrder.Where(k => k != authority.OwnerCharacterKey && authority.UnlockedCharacterCardPoolKeys.Contains(k,ModelKeyComparer.Instance)).ToArray();
                take = Math.Min(3,pool.Length);
            }
            var required = group.Select(c => c.TargetKey).Distinct().ToArray();
            p = required.Length > take || required.Any(k => !pool.Contains(k)) ? 0 : 1;
            for (int i=0; p>0 && i<required.Length; i++) p *= (double)(take-i)/(pool.Length-i);
        }
        else if (first.Kind is EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.TrialCase)
            p = group.All(c => c.TargetKey == first.TargetKey && c.TrialCase == first.TrialCase)
                ? EstimateSingle(plan,first).Probability ?? double.NaN : 0;
        else if (first.Kind == EventResultConditionKind.TinkerTimeTypeAndRider)
        {
            int types = group.Select(c => c.TinkerCardType).Distinct().Count();
            // Every rider query replays the same post-type shuffle; different
            // card types relabel its three indices, not independent rider draws.
            int riders = group.Where(c => c.TinkerRider.HasValue).Select(c => (int)c.TinkerRider!.Value % 3).Distinct().Count();
            static double Contains(int required) => required switch { 0 => 1, 1 => 2d / 3, 2 => 1d / 3, _ => 0 };
            p = Contains(types) * Contains(riders);
        }
        else return null;
        if (double.IsNaN(p)) return null;
        return SearchSelectivityEstimate.Exact(p, transform ? SearchSelectivityMethod.ConditionalChain : SearchSelectivityMethod.WithoutReplacement,
            SearchSelectivityCoverage.ExactRequestedConjunction,SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.EventResult.SameResultJoint", "All predicates observe the same generated result; repeated facts paid once.",
            ["EventOccurrenceNotProven=true","SameEventJoint=true"]);
    }

    public static SearchSelectivityEstimate EstimateSingle(
        SearchSelectivityInput plan,
        EventResultSearchCondition condition)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!condition.IsValid)
            return SearchSelectivityEstimate.Unpriced("Probability.EventResult.Invalid", "Event Result condition is invalid.");

        double probability;
        string evidence;
        string notes;
        switch (condition.Kind)
        {
            case EventResultConditionKind.SymbioteInitialBasicTransform:
            case EventResultConditionKind.AromaOfChaosInitialBasicTransform:
            case EventResultConditionKind.WhisperingHollowInitialBasicTransform:
            case EventResultConditionKind.TrialNondescriptInitialBasicsContains:
            case EventResultConditionKind.MorphicGroveGroupInitialBasicsContains:
            {
                return EstimateTransformJoint([condition]);
            }
            case EventResultConditionKind.TrialCase:
                probability = 1d / 3;
                evidence = "Probability.EventResult.TrialCase.Conditional";
                notes = "Accept draws one of three cases; cosmetic Chaotic RNG is excluded.";
                break;
            case EventResultConditionKind.TinkerTimeTypeAndRider:
                probability = condition.TinkerRider is null ? 2d / 3 : 4d / 9;
                evidence = "Probability.EventResult.TinkerTypeRider.Conditional";
                notes = "Two of three types, then two of three riders for the chosen type on the same local continuation.";
                break;
            case EventResultConditionKind.TrashHeapGrabCard:
                probability = Beta111EventResultCatalog.TrashHeapGrabCards.Contains(condition.TargetKey) ? 1d / 10d : 0d;
                evidence = "Probability.EventResult.TrashHeapGrab.Conditional";
                notes = "Conditional on Trash Heap Grab being the observed option result: one uniform pick from the audited ten-card list.";
                break;
            case EventResultConditionKind.TrashHeapDiveRelic:
                probability = Beta111EventResultCatalog.TrashHeapDiveRelics.Contains(condition.TargetKey) ? 1d / 5d : 0d;
                evidence = "Probability.EventResult.TrashHeapDive.Conditional";
                notes = "Conditional on Trash Heap Dive being the observed option result: one uniform pick from the audited five-relic list.";
                break;
            case EventResultConditionKind.FakeMerchantOfferedFakeRelic:
                probability = Beta111EventResultCatalog.FakeMerchantRelics.Contains(condition.TargetKey) ? 6d / 9d : 0d;
                evidence = "Probability.EventResult.FakeMerchantContains.Conditional";
                notes = "Conditional on Fake Merchant inventory generation: six of nine distinct audited fake relics are retained after a uniform full shuffle.";
                break;
            case EventResultConditionKind.ColorfulPhilosophersOfferedColor:
            {
                Beta111EventResultAuthority authority = Beta111EventResultAuthority.From(plan.Authority);
                if (!authority.ColorfulPoolAuthorityExact)
                {
                    return SearchSelectivityEstimate.Unpriced(
                        "Probability.EventResult.Colorful.AuthorityMissing",
                        "Exact owner/unlock pool authority is required for the conditional Colorful Philosophers marginal.");
                }
                ModelKey[] eligible = Beta111EventResultCatalog.ColorfulCharacterOrder
                    .Where(key => key != authority.OwnerCharacterKey &&
                                  authority.UnlockedCharacterCardPoolKeys.Contains(key, ModelKeyComparer.Instance))
                    .ToArray();
                if (!eligible.Contains(condition.TargetKey, ModelKeyComparer.Instance))
                    probability = 0d;
                else if (eligible.Length <= 3)
                    probability = 1d;
                else
                    probability = 3d / eligible.Length;
                evidence = "Probability.EventResult.ColorfulContains.Conditional";
                notes = $"Conditional on Colorful Philosophers offer generation: target membership in a uniform min(3,n)-subset of n={eligible.Length} eligible non-owner colors.";
                break;
            }
            default:
                return SearchSelectivityEstimate.Unpriced("Probability.EventResult.UnsupportedKind", "Unsupported Event Result condition kind.");
        }

        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.AuthorityPoolMembership,
            SearchSelectivityCoverage.ExactRequestedPredicate,
            SearchSelectivityDependencyClass.UnknownDependence,
            evidence,
            notes,
            new[]
            {
                "ConditionalEventResultOnly=true",
                "EventOccurrenceNotProven=true",
                "NoOccurrenceResultIndependenceTheorem=true"
            });
    }
}
