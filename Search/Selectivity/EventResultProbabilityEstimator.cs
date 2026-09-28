using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Conditional Event Result v1 marginals only. This estimator deliberately owns no
/// Event occurrence probability and registers no independence relation to World/Event.
/// </summary>
internal static class EventResultProbabilityEstimator
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
        EventResultSearchCondition[] grabs = conditions
            .Where(item => item.Kind == EventResultConditionKind.TrashHeapGrabCard)
            .ToArray();
        EventResultSearchCondition[] dives = conditions
            .Where(item => item.Kind == EventResultConditionKind.TrashHeapDiveRelic)
            .ToArray();
        EventResultSearchCondition[] fake = conditions
            .Where(item => item.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic)
            .ToArray();
        EventResultSearchCondition[] colorful = conditions
            .Where(item => item.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor)
            .ToArray();

        if (grabs.Length > 1 || dives.Length > 1 || fake.Length > 1 || colorful.Length > 1)
        {
            issue = "RepeatedEventResultLocalGroup";
            return null;
        }

        var factors = new List<SearchSelectivityEstimate>(3);
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
