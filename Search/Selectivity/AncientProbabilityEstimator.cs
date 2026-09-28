using System.Numerics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Query-level Ancient probability authority. It evaluates the Search UI's real
/// parent-scoped semantics:
///   OR over selected Ancient branches in each Act,
///   where each branch is Identity=i AND OptionPredicate_i.
/// Shared Ancient assignment is one latent state and is enumerated before the
/// Act-local identity draw. Option probabilities are conditional on that identity.
/// </summary>
internal static class AncientProbabilityEstimator
{
    private sealed record BranchPolicy(
        ModelKey AncientKey,
        double ConditionalAcceptance,
        SearchSelectivityEstimate? OptionEstimate,
        bool Unrestricted);

    public static JointSelectivityResult? Estimate(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        AncientSearchBranchCondition[] rows = filter.AncientBranchConditions
            .Where(branch => branch.IsValid)
            .ToArray();
        bool hasLegacy = ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientIdentityFilters.Any(item => !item.IsEmpty) ||
                         ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientOptionFilters.Any(item => !item.IsEmpty) ||
                         ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientSeaGlassTargetFilters.Any(item => !item.IsEmpty);
        if (rows.Length == 0)
        {
            if (!hasLegacy) return null;
            if (!filter.AncientOptionFilters.Any(f => !f.IsEmpty) && !filter.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty))
                return FromLegacyIdentity(AncientIdentityProbabilityEstimator.Estimate(plan));
        }
        // Legacy unscoped sets apply to whichever identity appears in that Act.
        // Enumerate the same parent alternatives, then conjoin their visible
        // offer predicates; never price a global option marginal independently.
        var legacyActs = filter.AncientIdentityFilters.Concat(filter.AncientOptionFilters).Concat(filter.AncientSeaGlassTargetFilters)
            .Where(f => !f.IsEmpty).Select(f => f.Act).Distinct().Where(act => !rows.Any(r => r.Act == act)).ToArray();
        if (legacyActs.Length > 0)
        {
            var g = plan.Authority.WorldAuthority?.Beta109Generation;
            if (g is null) return JointSelectivityResult.Unpriced("Probability.Ancient.AuthorityMissing",
                "Legacy option sets require the captured parent Ancient catalog.", "No guessed parent pool.");
            rows = rows.Concat(legacyActs.SelectMany(act => g.OrderedActCatalog.Where(a => a.Act == act)
                .SelectMany(a => a.OrderedAncients).Concat(g.SharedAncients).Distinct()
                .Select(key => new AncientSearchBranchCondition(act, key, [], [])))).ToArray();
        }

        ProbabilitySemanticView semantic = ProbabilitySemanticProjection.From(plan);
        foreach (AncientSearchBranchCondition row in rows.Where(row => row.OptionAny.Count != 0 || row.SeaGlassTargetAny.Count != 0))
        {
            bool parentScoped = semantic.Relations.Any(relation =>
                relation.Kind == SemanticRelationKind.ParentScoped &&
                relation.Source.Kind == SemanticFactKind.AncientIdentity &&
                relation.Source.Act == row.Act &&
                relation.Source.Key == row.AncientKey &&
                relation.Target.Kind == SemanticFactKind.AncientOption);
            if (!parentScoped)
            {
                return JointSelectivityResult.Unpriced(
                    "Probability.Ancient.CanonicalParentScopedRelationMissing",
                    $"Act {row.Act} Ancient option pricing requires the Canonical ParentScoped relation for {row.AncientKey.Serialized}.",
                    "Probability does not recreate parent ownership from the legacy execution filter.",
                    new[] { $"Act{row.Act}:{row.AncientKey.Serialized}:ParentScoped" },
                    assumptions: new[] { "SemanticRelationAuthority=Canonical" });
            }
        }

        bool act2Darv = filter.AncientBranchConditions.Any(row => row.Act == 2 && string.Equals(row.AncientKey.Entry, "DARV", StringComparison.Ordinal));
        bool act3Darv = filter.AncientBranchConditions.Any(row => row.Act == 3 && string.Equals(row.AncientKey.Entry, "DARV", StringComparison.Ordinal));
        if (act2Darv && act3Darv && !semantic.HasRelation(SemanticRelationKind.SharedState, SemanticFactKind.AncientIdentity, SemanticFactKind.AncientIdentity))
        {
            return JointSelectivityResult.Unpriced(
                "Probability.Ancient.CanonicalSharedStateRelationMissing",
                "Act2/Act3 Darv pricing requires the Canonical SharedState relation.",
                "The shared assignment is not independently multiplied or reconstructed by Probability.",
                new[] { "DarvSharedState" },
                assumptions: new[] { "SemanticRelationAuthority=Canonical" });
        }

        int[] acts = rows.Select(row => row.Act).Distinct().OrderBy(act => act).ToArray();
        if (acts.Any(act => act is not (2 or 3)))
        {
            return JointSelectivityResult.Unpriced(
                "Probability.Ancient.UnsupportedAct",
                "Current Ancient Search branches are defined for Act2/Act3.",
                "No probability is guessed for an unsupported Act.",
                acts.Select(act => "Act" + act).ToArray());
        }

        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null ||
            !generation.ActSelectionAuthorityExact ||
            !generation.SharedAncientCatalogExact ||
            !generation.AllSharedAncientCatalogExact ||
            !generation.UnlockFactsExact ||
            !generation.DirectSourceAudited ||
            (!generation.NoUnknownHooksOrModifiers && !plan.Authority.UsesBestEffortModel))
        {
            return JointSelectivityResult.Unpriced(
                "Probability.Ancient.AuthorityMissing",
                "Exact Ancient probability requires the immutable runtime unlock-filtered local/shared catalogs and exact Act-selection authority.",
                "No fully-unlocked Beta110 constant is substituted.",
                new[] { "RuntimeEligibleAncientCatalog", "SharedAncientAssignment" });
        }

        var localByAct = new Dictionary<int, (Beta109ActGenerationSnapshot Snapshot, double Weight)[]>();
        foreach (int act in acts)
        {
            if (SearchSelectivityEstimator.TryResolveActAuthorityForSelectivity(generation, act, out var single, out _) && single is not null)
            {
                localByAct[act] = [(single, 1d)];
                continue;
            }
            var groups = generation.ActSelectionGroups.Where(g => g.Act == act).ToArray();
            if (groups.Length != 1 || !groups[0].EligibilityAndOrderExact ||
                groups[0].EligibleActsInSourceOrder.Count == 0 || groups[0].SelectionMode == Beta109ActSelectionMode.Unsupported)
                return JointSelectivityResult.Unpriced("Probability.Ancient.ActVariantAuthorityMissing", "Act variant priors unavailable.", "No guessed variant weights.");
            var keys = groups[0].SelectionMode == Beta109ActSelectionMode.DeterministicFirst
                ? groups[0].EligibleActsInSourceOrder.Take(1).ToArray() : groups[0].EligibleActsInSourceOrder.ToArray();
            var variants = keys.Select(key => generation.OrderedActCatalog.FirstOrDefault(a => a.ActKey == key)).ToArray();
            if (variants.Any(v => v is null || !v.HasExactGenerationInputs))
                return JointSelectivityResult.Unpriced("Probability.Ancient.ActVariantCatalogMissing", "An eligible variant lacks its pool.", "Missing branches are not renormalized away.");
            localByAct[act] = variants.Select(v => (v!, 1d / variants.Length)).ToArray();
        }

        var policiesByAct = new Dictionary<int, Dictionary<ModelKey, BranchPolicy>>();
        var optionComponents = new List<JointSelectivityComponent>();
        var unknown = new List<string>();
        foreach (int act in acts)
        {
            var policies = new Dictionary<ModelKey, BranchPolicy>(ModelKeyComparer.Instance);
            foreach (IGrouping<ModelKey, AncientSearchBranchCondition> identityRows in rows
                         .Where(row => row.Act == act)
                         .GroupBy(row => row.AncientKey, ModelKeyComparer.Instance))
            {
                AncientSearchBranchCondition[] sameParent = identityRows.ToArray();
                var options = filter.AncientOptionFilters.Where(f => f.Act == act && !f.IsEmpty).Select(f => f.Keys).ToArray();
                var seaTargets = filter.AncientSeaGlassTargetFilters.Where(f => f.Act == act && !f.IsEmpty).Select(f => f.Keys).ToArray();
                if (!filter.AncientIdentityFilters.Where(f => f.Act == act).All(f => PartyInitialQuery.Matches(f.Keys, [identityRows.Key])))
                {
                    policies[identityRows.Key] = new BranchPolicy(identityRows.Key, 0d, null, false);
                    continue;
                }
                bool unrestricted = options.Length == 0 && seaTargets.Length == 0 && sameParent.Any(row => row.OptionAny.Count == 0 && row.SeaGlassTargetAny.Count == 0);
                if (unrestricted)
                {
                    policies[identityRows.Key] = new BranchPolicy(identityRows.Key, 1d, null, true);
                    continue;
                }

                SearchSelectivityEstimate conditional = AncientOptionProbabilityEstimator.EstimateConditionalConjunction(
                    plan, act, identityRows.Key, sameParent, options, seaTargets);
                if (!conditional.IsPriced || !conditional.Probability.HasValue)
                {
                    unknown.Add($"Act{act}:{identityRows.Key.Serialized}:{conditional.EvidenceCode}");
                    continue;
                }

                double q = conditional.Probability.Value;
                policies[identityRows.Key] = new BranchPolicy(identityRows.Key, q, conditional, false);
                optionComponents.Add(new JointSelectivityComponent(
                    $"ancient-option:{act}:{identityRows.Key.Serialized}",
                    $"Act{act} {identityRows.Key.Serialized} OptionAny",
                    $"AncientIdentity:{identityRows.Key.Serialized}",
                    q,
                    conditional.EvidenceCode,
                    JointSelectivityCombinationMethod.ConditionalChain,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    true,
                    q > 0d,
                    "Parent-scoped child probability; parent identity is paid once by the Ancient mixture.",
                    conditional.Assumptions));
            }
            policiesByAct[act] = policies;
        }

        if (unknown.Count != 0)
        {
            return JointSelectivityResult.Partial(
                "Probability.Ancient.ParentScopedOptionPartial",
                "At least one selected Ancient branch has an unpriced parent-conditioned option predicate.",
                "Identity/shared-assignment authority remains known, but a global identity marginal is not multiplied by one child's option probability.",
                optionComponents,
                unknown,
                BuildGraph(acts, policiesByAct, optionComponents),
                assumptions: new[]
                {
                    "Ancient Query semantics are OR over parent-scoped branches within each Act.",
                    "Unknown child probability keeps the integrated Ancient block Partial instead of substituting a global Option marginal."
                },
                confidence: SearchSelectivityConfidence.High,
                dependencyCoverage: "PartialParentScopedAncient");
        }

        ModelKey[] shared = generation.SharedAncients
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (shared.Length > 12)
        {
            return JointSelectivityResult.Unpriced(
                "Probability.Ancient.SharedPoolTooLargeForExactEnumeration",
                $"Shared Ancient pool has {shared.Length} entries; the exact subset enumerator is intentionally capped at 12.",
                "The cap is computational only; no approximate shared-assignment factor is substituted.",
                new[] { "SharedAncientSubsetEnumeration" });
        }

        Beta109ActSelectionGroupSnapshot[] orderedGroups = generation.ActSelectionGroups
            .OrderBy(group => group.Act)
            .ToArray();
        if (orderedGroups.Length < 2 ||
            orderedGroups.Any(group => !group.EligibilityAndOrderExact || group.EligibleActsInSourceOrder.Count == 0))
        {
            return JointSelectivityResult.Unpriced(
                "Probability.Ancient.AssignmentActGroupsMissing",
                "Shared Ancient prefix assignment requires exact ordered Act-selection groups.",
                "The shared latent state cannot be constructed.",
                new[] { "SharedAncientAssignmentActGroups" });
        }

        ulong initialMask = shared.Length == 0 ? 0UL : (1UL << shared.Length) - 1UL;
        var assignedByAct = new Dictionary<int, ulong>();
        double probability = EnumerateAssignments(stepIndex: 1, remainingMask: initialMask, weight: 1d);
        probability = Math.Clamp(probability, 0d, 1d);

        var components = new List<JointSelectivityComponent>(optionComponents)
        {
            new(
                "ancient:identity-mixture",
                "Ancient parent-scoped identity branches",
                "SharedAncientAssignment",
                probability,
                "Probability.Authority.AncientParentScopedSharedAssignment",
                JointSelectivityCombinationMethod.FiniteMixture,
                SearchSelectivityDependencyClass.StructuralDependence,
                true,
                probability > 0d,
                "Shared assignment + runtime local pool + parent-conditioned branch acceptance are solved as one logical block.",
                new[] { "Unlock state comes from immutable Runtime Authority." })
        };

        return JointSelectivityResult.Exact(
            probability,
            JointSelectivityCombinationMethod.FiniteMixture,
            "Probability.Authority.AncientParentScopedSharedAssignment",
            "Exact Ancient query probability over runtime local/shared pools with parent-scoped OptionAny semantics.",
            $"P(AncientQuery)={probability:G17}; sharedAncients={shared.Length}; acts={string.Join(',', acts)}",
            BuildGraph(acts, policiesByAct, optionComponents),
            components,
            assumptions: new[]
            {
                "OR over selected Ancient branches within each Act.",
                "Within a branch: Identity=Ancient AND OptionPredicate(Ancient).",
                "A branch with no option selection has conditional acceptance 1.",
                "Shared Ancient assignment is one latent state; Darv cannot be assigned to both Act2 and Act3.",
                "Act-local identity draws use the current AssumedIndependentUnlessStructuralDependencyRegistered policy after conditioning on shared assignment."
            },
            dependencyCoverage: "CompleteParentScopedAncient");

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
                double actAcceptance = 0;
                foreach (var (snapshot, weight) in localByAct[act])
                {
                    var pool = snapshot.OrderedAncients.Where(key => key.IsValid).ToList();
                    if (assignedByAct.TryGetValue(act, out ulong subset))
                        for (int index = 0; index < shared.Length; index++)
                            if ((subset & (1UL << index)) != 0UL) pool.Add(shared[index]);
                    if (pool.Count == 0) continue;
                    var policies = policiesByAct[act];
                    actAcceptance += weight * pool.Sum(candidate => policies.TryGetValue(candidate, out var policy)
                        ? policy.ConditionalAcceptance : 0d) / pool.Count;
                }
                p *= actAcceptance;
                if (p == 0d) return 0d;
            }
            return p;
        }
    }

    private static SelectivityDependencyGraph BuildGraph(
        IReadOnlyList<int> acts,
        IReadOnlyDictionary<int, Dictionary<ModelKey, BranchPolicy>> policiesByAct,
        IReadOnlyList<JointSelectivityComponent> optionComponents)
    {
        var nodes = new List<SelectivityDependencyNode>
        {
            new(
                "ancient:query",
                SelectivityDependencyNodeKind.QueryRoot,
                "Ancient Query",
                null,
                "CompiledQuery.ParentScopedBranches",
                SearchSelectivityDependencyClass.StructuralDependence,
                Array.Empty<string>()),
            new(
                "ancient:shared-assignment",
                SelectivityDependencyNodeKind.LatentParent,
                "Shared Ancient Assignment",
                null,
                "RuntimeAuthority.SharedAncients",
                SearchSelectivityDependencyClass.StructuralDependence,
                Array.Empty<string>())
        };
        var edges = new List<SelectivityDependencyEdge>
        {
            new(
                "ancient:shared-assignment",
                "ancient:query",
                SelectivityDependencyEdgeKind.SharedParent,
                "RuntimeAuthority.SharedAncients",
                "Shared Ancient assignment is paid once across Act2/Act3 branches.")
        };

        foreach (int act in acts)
        {
            string identityId = "ancient:identity:" + act;
            nodes.Add(new(
                identityId,
                SelectivityDependencyNodeKind.ObservableCondition,
                "Act" + act + " Ancient Identity",
                null,
                "RuntimeAuthority.AncientPool",
                SearchSelectivityDependencyClass.StructuralDependence,
                Array.Empty<string>()));
            edges.Add(new(
                "ancient:shared-assignment",
                identityId,
                SelectivityDependencyEdgeKind.ConditionalOn,
                "RuntimeAuthority.AncientPool",
                "Act identity pool includes the shared subset assigned to this Act."));
            edges.Add(new(
                identityId,
                "ancient:query",
                SelectivityDependencyEdgeKind.ConditionalOn,
                "CompiledQuery.ParentScopedBranches",
                "Selected Ancient identities use Any semantics within this Act."));

            foreach (BranchPolicy policy in policiesByAct[act].Values.Where(policy => !policy.Unrestricted))
            {
                string optionId = $"ancient:option:{act}:{policy.AncientKey.Serialized}";
                nodes.Add(new(
                    optionId,
                    SelectivityDependencyNodeKind.ObservableCondition,
                    "OptionAny | " + policy.AncientKey.Serialized,
                    policy.OptionEstimate?.Probability,
                    policy.OptionEstimate?.EvidenceCode ?? "AncientOptionConditional",
                    SearchSelectivityDependencyClass.StructuralDependence,
                    policy.OptionEstimate?.Assumptions ?? Array.Empty<string>()));
                edges.Add(new(
                    identityId,
                    optionId,
                    SelectivityDependencyEdgeKind.ConditionalOn,
                    policy.OptionEstimate?.EvidenceCode ?? "AncientOptionConditional",
                    "OptionAny is evaluated only when this parent Ancient identity is the actual Ancient."));
            }
        }
        return new SelectivityDependencyGraph(nodes, edges);
    }

    private static JointSelectivityResult FromLegacyIdentity(SearchSelectivityEstimate estimate)
    {
        if (estimate.IsPriced && estimate.Probability.HasValue)
        {
            return JointSelectivityResult.Exact(
                estimate.Probability.Value,
                JointSelectivityCombinationMethod.FiniteMixture,
                estimate.EvidenceCode,
                estimate.Notes,
                "Legacy Ancient identity-only probability=" + estimate.Probability.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
                assumptions: estimate.Assumptions,
                dependencyCoverage: "LegacyIdentityOnly");
        }
        return JointSelectivityResult.Unpriced(
            estimate.EvidenceCode,
            estimate.Notes,
            "Legacy Ancient identity contract remains unpriced by the consolidated branch authority.",
            new[] { "AncientLegacyIdentity" },
            assumptions: estimate.Assumptions);
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
