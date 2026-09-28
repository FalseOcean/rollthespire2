using RolltheSpire2.Search.Semantics;
using System.Numerics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Exact current-runtime Ancient identity probability for the Search UI's Act2/Act3
/// Any branches. Shared Ancients are modeled as the source-audited shuffle + uniform
/// prefix assignment chain, not as independent Act marginals. Runtime unlock filtering
/// is inherited from the immutable World snapshot.
/// </summary>
internal static class AncientIdentityProbabilityEstimator
{
    public static SearchSelectivityEstimate Estimate(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        int[] acts = ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientBranchConditions.Where(branch => branch.IsValid).Select(branch => branch.Act)
            .Concat(ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientIdentityFilters.Where(item => !item.IsEmpty).Select(item => item.Act))
            .Distinct().OrderBy(act => act).ToArray();
        if (acts.Length == 0)
            return SearchSelectivityEstimate.Unpriced("Probability.AncientIdentity.NoPredicate", "No Ancient identity predicate to price.");
        if (acts.Any(act => act is not (2 or 3)))
            return SearchSelectivityEstimate.Unpriced("Probability.AncientIdentity.UnsupportedAct", "Current Ancient identity authority covers Act2/Act3.");

        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null ||
            !generation.ActSelectionAuthorityExact ||
            !generation.SharedAncientCatalogExact ||
            !generation.AllSharedAncientCatalogExact ||
            !generation.UnlockFactsExact ||
            !generation.DirectSourceAudited ||
            (!generation.NoUnknownHooksOrModifiers && !plan.Authority.UsesBestEffortModel))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientIdentity.AuthorityMissing",
                "Exact Ancient identity pricing requires runtime unlock-filtered local/shared catalogs and exact Act selection authority.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.StructuralAssignment,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        var localByAct = new Dictionary<int, Beta109ActGenerationSnapshot>();
        foreach (int act in acts)
        {
            if (!SearchSelectivityEstimator.TryResolveActAuthorityForSelectivity(generation, act, out Beta109ActGenerationSnapshot? snapshot, out string resolution) || snapshot is null)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.AncientIdentity.ActVariantUnresolved:" + act,
                    "Ancient identity pricing currently requires one exact Runtime Eligible Act Variant for each constrained Ancient Act; " + resolution,
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.StructuralAssignment,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence);
            }
            localByAct[act] = snapshot;
        }

        ModelKey[] shared = generation.SharedAncients.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
        if (shared.Length > 12)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientIdentity.SharedPoolTooLargeForExactEnumeration",
                $"Shared Ancient pool has {shared.Length} entries; current exact subset enumerator is intentionally capped at 12.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.StructuralAssignment,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        Beta109ActSelectionGroupSnapshot[] orderedGroups = generation.ActSelectionGroups
            .OrderBy(group => group.Act)
            .ToArray();
        if (orderedGroups.Length < 2 || orderedGroups.Any(group => !group.EligibilityAndOrderExact || group.EligibleActsInSourceOrder.Count == 0))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientIdentity.AssignmentActGroupsMissing",
                "Shared Ancient prefix assignment requires exact ordered Act groups.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.StructuralAssignment,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        var branchKeysByAct = acts.ToDictionary(
            act => act,
            act => ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientBranchConditions
                .Where(branch => branch.IsValid && branch.Act == act)
                .Select(branch => branch.AncientKey)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray());
        var legacyByAct = acts.ToDictionary(
            act => act,
            act => ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientIdentityFilters.Where(item => !item.IsEmpty && item.Act == act).ToArray());

        ulong initialMask = shared.Length == 64 ? ulong.MaxValue : (shared.Length == 0 ? 0UL : (1UL << shared.Length) - 1UL);
        var assignedByAct = new Dictionary<int, ulong>();
        double probability = EnumerateAssignments(stepIndex: 1, remainingMask: initialMask, weight: 1d);
        probability = Math.Clamp(probability, 0d, 1d);

        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.StructuralAssignment,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Authority.AncientSharedAssignmentJoint",
            $"Exact Ancient identity joint over runtime local pools plus {shared.Length} unlocked shared Ancient(s); shared assignment is enumerated as uniform-prefix subsets before each Act identity draw.",
            new[]
            {
                "Ancient branch identities use Any semantics within each Act.",
                "Shared Ancient assignment is one latent state, so Act2/Act3 marginals are not blindly multiplied.",
                "Unlock state comes from the immutable runtime World authority."
            });

        double EnumerateAssignments(int stepIndex, ulong remainingMask, double weight)
        {
            if (stepIndex >= orderedGroups.Length)
                return weight * ConditionalIdentityProbability();

            int act = orderedGroups[stepIndex].Act;
            int remainingCount = BitOperations.PopCount(remainingMask);
            double subtotal = 0d;
            for (int take = 0; take <= remainingCount; take++)
            {
                double countWeight = 1d / (remainingCount + 1d);
                long combinations = Binomial(remainingCount, take);
                if (combinations <= 0) continue;
                foreach (ulong subset in EnumerateSubsetsOfSize(remainingMask, take))
                {
                    assignedByAct[act] = subset;
                    subtotal += EnumerateAssignments(
                        stepIndex + 1,
                        remainingMask & ~subset,
                        weight * countWeight / combinations);
                }
            }
            assignedByAct.Remove(act);
            return subtotal;
        }

        double ConditionalIdentityProbability()
        {
            double p = 1d;
            foreach (int act in acts)
            {
                Beta109ActGenerationSnapshot local = localByAct[act];
                var pool = local.OrderedAncients.Where(key => key.IsValid).ToList();
                if (assignedByAct.TryGetValue(act, out ulong subset))
                {
                    for (int index = 0; index < shared.Length; index++)
                    {
                        if ((subset & (1UL << index)) != 0UL) pool.Add(shared[index]);
                    }
                }
                if (pool.Count == 0) return 0d;

                int accepted = 0;
                foreach (ModelKey candidate in pool)
                {
                    bool matches = true;
                    ModelKey[] branchKeys = branchKeysByAct[act];
                    if (branchKeys.Length != 0 && !branchKeys.Contains(candidate, ModelKeyComparer.Instance))
                        matches = false;
                    if (matches)
                    {
                        foreach (ActModelKeySetFilter legacy in legacyByAct[act])
                        {
                            if (!QueryKeySetPredicate.MatchesKeySet(new[] { candidate }, legacy.Keys))
                            {
                                matches = false;
                                break;
                            }
                        }
                    }
                    if (matches) accepted++;
                }
                p *= accepted / (double)pool.Count;
                if (p == 0d) return 0d;
            }
            return p;
        }
    }

    private static IEnumerable<ulong> EnumerateSubsetsOfSize(ulong mask, int size)
    {
        if (size == 0)
        {
            yield return 0UL;
            yield break;
        }
        int[] bits = Enumerable.Range(0, 64).Where(index => (mask & (1UL << index)) != 0UL).ToArray();
        if (size > bits.Length) yield break;
        foreach (ulong subset in Choose(0, size, 0UL)) yield return subset;

        IEnumerable<ulong> Choose(int start, int remaining, ulong current)
        {
            if (remaining == 0)
            {
                yield return current;
                yield break;
            }
            for (int index = start; index <= bits.Length - remaining; index++)
            {
                foreach (ulong subset in Choose(index + 1, remaining - 1, current | (1UL << bits[index])))
                    yield return subset;
            }
        }
    }

    private static long Binomial(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        long value = 1;
        for (int i = 1; i <= k; i++) value = checked(value * (n - k + i) / i);
        return value;
    }
}
