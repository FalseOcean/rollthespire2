namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Planner-prefix probability composition policy. The 2026-08-11 Probability Design
/// defaults unrelated domains to AssumedIndependent; only registered Conditional
/// requirements block/reorder a local factor here. SharedConstraint normalization is
/// owned by the query/domain authorities before Planner consumption.
/// </summary>
internal static class SearchSelectivityCombinationPolicy
{
    public static SearchSelectivityCombinationResult ResolveForPrefix(
        SearchSelectivityEstimate estimate,
        IReadOnlyCollection<SearchSelectivityDomain> prefixDomains)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(prefixDomains);

        if (!estimate.IsPriced)
            return SearchSelectivityCombinationResult.Unpriced("EstimateUnpriced:PlanningUpperBoundOnly");

        HashSet<SearchSelectivityDomain> prefix = prefixDomains.ToHashSet();
        HashSet<SearchSelectivityDomain> conditioned = estimate.ConditionedOnDomains.ToHashSet();

        if (conditioned.Count != 0 && !conditioned.IsSubsetOf(prefix))
            return SearchSelectivityCombinationResult.Unpriced(
                "RequiredConditioningPrefixMissing:" + string.Join(',', conditioned.Except(prefix)));

        if (prefix.Count == 0 && conditioned.Count != 0)
            return SearchSelectivityCombinationResult.Unpriced("RequiredConditioningPrefixMissing");

        return new SearchSelectivityCombinationResult(
            estimate.PlanningUpperBoundRate,
            true,
            conditioned.Count != 0
                ? "ExplicitConditionalEstimate"
                : estimate.DependencyClass == SearchSelectivityDependencyClass.ProvenIndependent
                    ? "AuditedPrefixIndependence"
                    : "AssumedPrefixIndependenceUnlessSharedConstraintRegistered");
    }
}
