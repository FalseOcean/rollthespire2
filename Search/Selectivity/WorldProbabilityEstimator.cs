using System.Globalization;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Exact/partial World-domain probability authority for the Search UI's current
/// Boss + Event semantics. Act Variant is an explicit latent parent. Event and Boss
/// are evaluated inside each Variant branch, then combined under the product policy's
/// AssumedIndependent conditional relation. The Variant prior is therefore paid once.
/// </summary>
internal static class WorldProbabilityEstimator
{
    public static JointSelectivityResult? Estimate(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;

        int[] acts = filter.BossFilters.Where(item => !item.IsEmpty).Select(item => item.Act)
            .Concat(filter.BossOrdinalFilters.Where(item => !item.IsEmpty).Select(item => item.Act))
            .Concat(filter.EventSequenceConditions.Where(item => !item.IsEmpty).Select(item => item.Act))
            .Distinct()
            .OrderBy(act => act)
            .ToArray();
        if (acts.Length == 0) return null;

        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null || !generation.ActSelectionAuthorityExact)
        {
            return JointSelectivityResult.Unpriced(
                "Probability.World.ActSelectionAuthorityMissing",
                "Boss/Event probability requires exact runtime Act Variant selection authority.",
                "Act Variant priors are unavailable; no fixed Beta110 denominator is substituted.",
                acts.Select(act => "Act" + act + ":VariantAuthority").ToArray());
        }

        var actResults = new List<JointSelectivityResult>(acts.Length);
        foreach (int act in acts)
        {
            JointSelectivityResult actResult = EstimateAct(plan, generation, act);
            actResults.Add(actResult);
            if (actResult.ExactlyImpossible)
            {
                return JointSelectivityResult.Exact(
                    0d,
                    JointSelectivityCombinationMethod.ExactImpossible,
                    "Probability.World.ActConstraintImpossible",
                    $"Act {act} has no legal Variant/Boss/Event outcome satisfying the normalized query.",
                    string.Join(" | ", actResults.Select(item => item.Derivation)),
                    MergeGraphs(actResults),
                    actResults.SelectMany(item => item.KnownComponents).ToArray(),
                    actResults.SelectMany(item => item.Branches).ToArray(),
                    new[] { "SharedConstraint normalization eliminated every legal branch." },
                    "CompleteSharedConstraint");
            }
        }

        if (actResults.All(item => item.JointlyPriced && item.Probability.HasValue))
        {
            double probability = actResults.Aggregate(1d, (value, item) => value * item.Probability!.Value);
            JointSelectivityCombinationMethod method = actResults.Count == 1
                ? actResults[0].Method
                : JointSelectivityCombinationMethod.IndependentProduct;
            return JointSelectivityResult.Exact(
                probability,
                method,
                acts.Length == 1 ? actResults[0].EvidenceCode : "Probability.World.CrossActAssumedIndependent",
                acts.Length == 1
                    ? actResults[0].Notes
                    : "Each Act-local World block is exactly priced; distinct Act blocks are combined by the current AssumedIndependent policy unless a registered structural dependency says otherwise.",
                acts.Length == 1
                    ? actResults[0].Derivation
                    : "P(World)=" + string.Join(" × ", actResults.Select(item => F(item.Probability))) + "=" + F(probability),
                MergeGraphs(actResults, crossActAssumedIndependent: acts.Length > 1),
                actResults.SelectMany(item => item.KnownComponents).ToArray(),
                actResults.SelectMany(item => item.Branches).ToArray(),
                acts.Length == 1
                    ? actResults[0].Assumptions
                    : new[]
                    {
                        "Act-local SharedConstraint/Variant normalization is completed before multiplication.",
                        "CrossActRelation=AssumedIndependentUnlessStructuralDependencyRegistered",
                        "Player route/visited-event state is not modeled."
                    },
                acts.Length == 1 ? actResults[0].DependencyCoverage : "CompleteWithAssumedIndependence");
        }

        JointSelectivityComponent[] known = actResults.SelectMany(item => item.KnownComponents).ToArray();
        string[] unknown = actResults.SelectMany(item => item.UnknownComponents)
            .Concat(actResults.Where(item => !item.JointlyPriced).Select((item, index) => "ActBlockUnpriced:" + acts[index] + ":" + item.EvidenceCode))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return JointSelectivityResult.Partial(
            "Probability.World.Partial",
            "At least one Act-local World block is not fully priceable from current runtime authority.",
            string.Join(" | ", actResults.Select(item => item.Derivation)),
            known,
            unknown,
            MergeGraphs(actResults),
            actResults.SelectMany(item => item.Branches).ToArray(),
            new[] { "Known Act-local prices are retained; missing authority is not replaced by a fixed constant." },
            SearchSelectivityConfidence.High,
            "PartialWorldCoverage");
    }

    private static JointSelectivityResult EstimateAct(
        SearchSelectivityInput plan,
        Beta109WorldGenerationSnapshot generation,
        int act)
    {
        Beta109ActSelectionGroupSnapshot? group = generation.ActSelectionGroups.SingleOrDefault(item => item.Act == act);
        if (group is null || !group.EligibilityAndOrderExact || group.EligibleActsInSourceOrder.Count == 0)
        {
            return JointSelectivityResult.Unpriced(
                "Probability.World.ActVariantGroupMissing",
                $"Act {act} has no exact Runtime Eligible Variant group.",
                "Finite mixture cannot be constructed.",
                new[] { $"Act{act}:VariantGroup" });
        }

        IReadOnlyList<(ModelKey Key, double Prior)> priors = ResolveVariantPriors(generation, group);
        if (priors.Count == 0 || Math.Abs(priors.Sum(item => item.Prior) - 1d) > 1e-12)
        {
            return JointSelectivityResult.Unpriced(
                "Probability.World.ActVariantPriorUnavailable",
                $"Act {act} Variant priors are unavailable or not normalized.",
                "No automatic renormalization is performed.",
                new[] { $"Act{act}:VariantPrior" });
        }

        ActModelKeySetFilter[] bossFilters = ProbabilitySemanticProjection.From(plan).NumericalFilter.BossFilters
            .Where(item => !item.IsEmpty && item.Act == act).ToArray();
        ActOrdinalModelKeySetFilter[] bossOrdinals = ProbabilitySemanticProjection.From(plan).NumericalFilter.BossOrdinalFilters
            .Where(item => !item.IsEmpty && item.Act == act).ToArray();
        EventSequenceSearchCondition[] events = ProbabilitySemanticProjection.From(plan).NumericalFilter.EventSequenceConditions
            .Where(item => !item.IsEmpty && item.Act == act).ToArray();

        bool hasBoss = bossFilters.Length != 0 || bossOrdinals.Length != 0;
        bool hasEvent = events.Length != 0;
        if (hasBoss && hasEvent)
        {
            ProbabilitySemanticView semantic = ProbabilitySemanticProjection.From(plan);
            bool sharedParentRegistered = semantic.Relations.Any(relation =>
                relation.Kind == SemanticRelationKind.SharedParent &&
                relation.Source.Act == act &&
                relation.Target.Act == act &&
                relation.Target.Kind == SemanticFactKind.EventSequence);
            if (!sharedParentRegistered)
            {
                return JointSelectivityResult.Unpriced(
                    "Probability.World.CanonicalSharedParentMissing",
                    $"Act {act} Boss + Event pricing requires the Canonical SharedParent relation.",
                    "Probability does not recreate Variant parent ownership from the legacy execution filter.",
                    new[] { $"Act{act}:SharedParentRelation" },
                    assumptions: new[] { "SemanticRelationAuthority=Canonical" });
            }
        }

        var nodes = new List<SelectivityDependencyNode>
        {
            new($"act{act}:query", SelectivityDependencyNodeKind.QueryRoot, $"Act{act} World Query", null,
                "CompiledQuery", SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>()),
            new($"act{act}:variant", SelectivityDependencyNodeKind.LatentParent, $"Act{act} Variant", null,
                "RuntimeAuthority.ActSelection", SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>())
        };
        var edges = new List<SelectivityDependencyEdge>
        {
            new($"act{act}:variant", $"act{act}:query", SelectivityDependencyEdgeKind.SharedParent,
                "CanonicalSemanticRelation.SharedParent", "Boss and Event constraints share the Canonical Act Variant parent; runtime authority supplies the branch priors.")
        };
        if (hasBoss)
        {
            nodes.Add(new($"act{act}:boss", SelectivityDependencyNodeKind.ObservableCondition, $"Act{act} Boss", null,
                "RuntimeAuthority.ActBossPool", SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>()));
            edges.Add(new($"act{act}:variant", $"act{act}:boss", SelectivityDependencyEdgeKind.ConditionalOn,
                "RuntimeAuthority.ActBossPool", "Boss pool is Variant-conditional."));
        }
        if (hasEvent)
        {
            nodes.Add(new($"act{act}:event", SelectivityDependencyNodeKind.ObservableCondition, $"Act{act} Event Queue", null,
                "EventPoolSequenceProjector", SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>()));
            edges.Add(new($"act{act}:variant", $"act{act}:event", SelectivityDependencyEdgeKind.ConditionalOn,
                "EventPoolSequenceProjector", "Cleaned Event pool is Variant-conditional."));
        }
        if (hasBoss && hasEvent)
        {
            edges.Add(new($"act{act}:boss", $"act{act}:event", SelectivityDependencyEdgeKind.AssumedIndependent,
                "ProbabilityDesign20260811", "Conditional on the same Variant, no additional structural dependency is registered; multiply once under AssumedIndependent policy."));
        }

        var branches = new List<JointSelectivityBranchTrace>();
        var known = new List<JointSelectivityComponent>();
        var unknown = new List<string>();
        double total = 0d;
        bool complete = true;

        foreach ((ModelKey actKey, double prior) in priors)
        {
            Beta109ActGenerationSnapshot? variant = generation.OrderedActCatalog
                .FirstOrDefault(item => item.Act == act && item.ActKey == actKey);
            if (variant is null || !variant.HasExactGenerationInputs)
            {
                complete = false;
                unknown.Add("VariantAuthorityIncomplete:" + actKey.Serialized);
                branches.Add(new(
                    $"act{act}:variant:{actKey.Serialized}", actKey.Serialized, prior, true, null, false,
                    "VariantAuthorityIncomplete", Array.Empty<JointSelectivityComponent>(), Array.Empty<string>()));
                continue;
            }

            var components = new List<JointSelectivityComponent>();
            double bossP = 1d;
            if (bossFilters.Length != 0 || bossOrdinals.Length != 0)
            {
                if (!TryEstimateBoss(plan, variant, bossFilters, bossOrdinals, out bossP, out string bossReason))
                {
                    complete = false;
                    unknown.Add("Boss:" + actKey.Serialized + ":" + bossReason);
                    components.Add(new(
                        $"act{act}:boss:{actKey.Serialized}", "Boss constraints", actKey.Serialized, null,
                        "RuntimeAuthority.ActBossPool", JointSelectivityCombinationMethod.Unknown,
                        SearchSelectivityDependencyClass.StructuralDependence, false, false, bossReason, Array.Empty<string>()));
                }
                else
                {
                    components.Add(new(
                        $"act{act}:boss:{actKey.Serialized}", "Boss constraints", actKey.Serialized, bossP,
                        "RuntimeAuthority.ActBossPool", JointSelectivityCombinationMethod.WithoutReplacementJoint,
                        SearchSelectivityDependencyClass.StructuralDependence, true, bossP > 0d, bossReason,
                        new[] { "Boss Any/All/Ban and ordinal constraints are evaluated on legal single/double-Boss outcomes." }));
                }
            }

            double eventP = 1d;
            bool eventKnown = true;
            if (events.Length != 0)
            {
                eventKnown = TryEstimateEvents(generation, variant, events, out eventP, out string eventReason);
                if (!eventKnown)
                {
                    complete = false;
                    unknown.Add("Event:" + actKey.Serialized + ":" + eventReason);
                    components.Add(new(
                        $"act{act}:event:{actKey.Serialized}", "Event queue constraints", actKey.Serialized, null,
                        "EventPoolSequenceProjector+FiniteSequenceProbabilitySolver", JointSelectivityCombinationMethod.Unknown,
                        SearchSelectivityDependencyClass.StructuralDependence, false, false, eventReason, Array.Empty<string>()));
                }
                else
                {
                    components.Add(new(
                        $"act{act}:event:{actKey.Serialized}", "Event queue constraints", actKey.Serialized, eventP,
                        "EventPoolSequenceProjector+FiniteSequenceProbabilitySolver", JointSelectivityCombinationMethod.WithoutReplacementJoint,
                        SearchSelectivityDependencyClass.StructuralDependence, true, eventP > 0d, eventReason,
                        new[] { "Probability is over the production cleaned/static-effective Event queue; RawEntries is diagnostic only." }));
                }
            }

            bool branchKnown = (bossFilters.Length == 0 && bossOrdinals.Length == 0 || components.Any(item => item.Id.Contains(":boss:", StringComparison.Ordinal) && item.Probability.HasValue)) &&
                               eventKnown;
            double? conditional = branchKnown ? bossP * eventP : null;
            bool pruned = conditional == 0d;
            string branchReason = !branchKnown
                ? "BranchComponentUnpriced"
                : pruned
                    ? "SharedConstraintImpossibleInVariant"
                    : (bossFilters.Length != 0 || bossOrdinals.Length != 0) && events.Length != 0
                        ? "Boss×Event|Variant;AssumedIndependent;VariantPriorPaidOnce"
                        : "SingleWorldSubdomain";
            if (conditional.HasValue) total += prior * conditional.Value;
            known.AddRange(components.Where(item => item.Probability.HasValue));
            branches.Add(new(
                $"act{act}:variant:{actKey.Serialized}", actKey.Serialized, prior, true, conditional,
                pruned, branchReason, components,
                new[] { "Variant prior is applied once to the complete branch conjunction." }));
        }

        var graph = new SelectivityDependencyGraph(nodes, edges);
        string derivation = $"Act{act} Variant mixture: " + string.Join(" + ", branches.Select(branch =>
            F(branch.PriorProbability) + "×" + F(branch.ConditionalProbability))) + "=" + (complete ? F(total) : "Unknown");
        if (complete)
        {
            return JointSelectivityResult.Exact(
                total,
                JointSelectivityCombinationMethod.FiniteMixture,
                "Probability.World.ActVariantSharedConstraint",
                "Exact finite mixture over Runtime Eligible Act Variants with Boss/Event constraints normalized inside each branch.",
                derivation,
                graph,
                known,
                branches,
                new[]
                {
                    "Boss/Event Variant constraints are not priced as independent marginals.",
                    "Conditional Boss×Event relation inside one Variant=AssumedIndependent.",
                    "Player route/visited-event state is outside Event probability semantics."
                },
                "CompleteSharedConstraint");
        }

        return JointSelectivityResult.Partial(
            "Probability.World.ActVariantPartial",
            "Variant priors are known, but at least one branch lacks exact Boss/Event authority.",
            derivation,
            known,
            unknown,
            graph,
            branches,
            new[] { "Known finite-mixture branches are preserved; missing branch probability is not fabricated." },
            SearchSelectivityConfidence.High,
            "PartialVariantMixture");
    }

    private static bool TryEstimateBoss(
        SearchSelectivityInput plan,
        Beta109ActGenerationSnapshot variant,
        IReadOnlyList<ActModelKeySetFilter> actFilters,
        IReadOnlyList<ActOrdinalModelKeySetFilter> ordinalFilters,
        out double probability,
        out string reason)
    {
        probability = 0d;
        reason = string.Empty;
        ModelKey[] pool = variant.Bosses.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
        if (pool.Length == 0)
        {
            reason = "BossPoolEmpty";
            return false;
        }

        int bossCount = variant.Act == 3 && plan.Ascension >= 10 ? 2 : 1;
        if (ordinalFilters.Any(item => item.Ordinal <= 0 || item.Ordinal > bossCount))
        {
            reason = $"BossOrdinalOutsideRuntimeShape:bossCount={bossCount}";
            return false;
        }
        if (bossCount == 2 && pool.Length < 2)
        {
            probability = 0d;
            reason = "DoubleBossPoolTooSmall";
            return true;
        }

        long total = 0;
        long accepted = 0;
        if (bossCount == 1)
        {
            foreach (ModelKey first in pool)
            {
                total++;
                ModelKey[] outcome = { first };
                if (MatchesBossOutcome(outcome, actFilters, ordinalFilters)) accepted++;
            }
        }
        else
        {
            foreach (ModelKey first in pool)
            foreach (ModelKey second in pool)
            {
                if (first == second) continue;
                total++;
                ModelKey[] outcome = { first, second };
                if (MatchesBossOutcome(outcome, actFilters, ordinalFilters)) accepted++;
            }
        }

        probability = total == 0 ? 0d : accepted / (double)total;
        reason = $"BossOutcomeShape={(bossCount == 1 ? "Single" : "DoubleOrderedWithoutReplacement")};accepted={accepted};total={total};pool={pool.Length}";
        return true;
    }

    private static bool MatchesBossOutcome(
        IReadOnlyList<ModelKey> outcome,
        IReadOnlyList<ActModelKeySetFilter> actFilters,
        IReadOnlyList<ActOrdinalModelKeySetFilter> ordinalFilters)
    {
        foreach (ActModelKeySetFilter condition in actFilters)
        {
            if (!QueryKeySetPredicate.MatchesKeySet(outcome, condition.Keys)) return false;
        }
        foreach (ActOrdinalModelKeySetFilter condition in ordinalFilters)
        {
            if (condition.Ordinal <= 0 || condition.Ordinal > outcome.Count) return false;
            if (!QueryKeySetPredicate.MatchesKeySet(new[] { outcome[condition.Ordinal - 1] }, condition.Keys)) return false;
        }
        return true;
    }

    private static bool TryEstimateEvents(
        Beta109WorldGenerationSnapshot generation,
        Beta109ActGenerationSnapshot variant,
        IReadOnlyList<EventSequenceSearchCondition> conditions,
        out double probability,
        out string reason)
    {
        probability = 0d;
        reason = string.Empty;
        if (!EventPoolSequenceProjector.TryPrepare(
                variant.Act,
                variant.ActKey,
                variant.EventRngConsumptionExact,
                variant.OrderedRawEvents,
                generation.EventAuthority.OrderedSharedEventsRaw,
                generation.EventAuthority,
                variant.OrderedEligibleEvents,
                out EventPoolSequenceProjector.PreparedAct? prepared,
                out string issue) || prepared is null)
        {
            reason = "EventAuthority:" + issue;
            return false;
        }

        // Static eligibility is query-context deterministic. Runtime/deck/route-dependent
        // predicates deliberately remain in the cleaned candidate pool, matching Searcher semantics.
        EventPoolSequenceProjector.Candidate[] raw = prepared.Candidates.ToArray();
        if (raw.Length == 0)
        {
            return TrySolveEventPool(Array.Empty<EventPoolSequenceProjector.Candidate>(), conditions, out probability, out reason);
        }

        EventPoolSequenceProjector.Candidate[] staticallyEligible = raw
            .Where(candidate => !EventStaticEligibilityCatalog.Evaluate(
                candidate.EventKey, variant.Act, generation.Profile, candidate.Source).ShouldReject)
            .ToArray();
        if (staticallyEligible.GroupBy(item => item.EventKey, ModelKeyComparer.Instance).Any(group => group.Count() > 1))
        {
            reason = "StaticEligibleDuplicateIdentityRequiresLocalVisitedFirstOccurrenceModel";
            return false;
        }

        // The opening Ancient consumes raw shuffled position 1. Condition on the identity
        // occupying that position. The remainder is a uniform permutation; deleting static
        // impossibilities preserves uniform relative order among the effective candidates.
        double total = 0d;
        var branchNotes = new List<string>();
        for (int skippedIndex = 0; skippedIndex < raw.Length; skippedIndex++)
        {
            EventPoolSequenceProjector.Candidate skipped = raw[skippedIndex];
            EventPoolSequenceProjector.Candidate[] effectivePool = raw
                .Where((candidate, index) => index != skippedIndex)
                .Where(candidate => !EventStaticEligibilityCatalog.Evaluate(
                    candidate.EventKey, variant.Act, generation.Profile, candidate.Source).ShouldReject)
                .ToArray();
            if (!TrySolveEventPool(effectivePool, conditions, out double branchP, out string branchReason))
            {
                reason = "OpeningSkipBranchUnpriced:" + skipped.EventKey.Serialized + ":" + branchReason;
                return false;
            }
            total += branchP / raw.Length;
            if (branchNotes.Count < 4)
                branchNotes.Add(skipped.EventKey.Entry + "→" + branchP.ToString("G6", CultureInfo.InvariantCulture));
        }

        probability = Math.Clamp(total, 0d, 1d);
        reason = $"CleanedEventQueue;raw={raw.Length};staticEligible={staticallyEligible.Length};openingSkipMixture={raw.Length};sampleBranches={string.Join(',', branchNotes)}";
        return true;
    }

    private static bool TrySolveEventPool(
        IReadOnlyList<EventPoolSequenceProjector.Candidate> effectivePool,
        IReadOnlyList<EventSequenceSearchCondition> conditions,
        out double probability,
        out string reason)
    {
        var items = effectivePool.Select(candidate => new FiniteSequenceProbabilitySolver.Item(
            candidate.EventKey,
            candidate.Source == EventPoolSourceKind.ActLocal ? 0 : 1)).ToArray();
        var constraints = conditions.Select(condition => new FiniteSequenceProbabilitySolver.Constraint(
            condition.RangeMode,
            condition.RangeValue,
            condition.Keys,
            condition.Source.HasValue
                ? condition.Source.Value == EventPoolSourceKind.ActLocal ? 0 : 1
                : null,
            ExactSlotRequiresPartitionMatch: condition.Source.HasValue && condition.RangeMode == SearchSequenceRangeMode.ExactSlot)).ToArray();
        if (!FiniteSequenceProbabilitySolver.TrySolve(items, constraints, out FiniteSequenceProbabilitySolver.Result result, out string issue))
        {
            probability = 0d;
            reason = issue;
            return false;
        }
        probability = result.Probability;
        reason = result.Normalization;
        return true;
    }

    internal static IReadOnlyList<(ModelKey Key, double Prior)> ResolveVariantPriors(
        Beta109WorldGenerationSnapshot generation,
        Beta109ActSelectionGroupSnapshot group)
    {
        if (group.Act == 1)
        {
            if (!generation.Act1OverrideExact)
                return Array.Empty<(ModelKey, double)>();
            if (generation.Act1OverrideResolvedKey is ModelKey overrideKey && overrideKey.IsValid)
                return new[] { (overrideKey, 1d) };
        }

        if (group.SelectionMode == Beta109ActSelectionMode.DeterministicFirst)
            return new[] { (group.EligibleActsInSourceOrder[0], 1d) };
        if (group.SelectionMode != Beta109ActSelectionMode.RandomNextItem)
            return Array.Empty<(ModelKey, double)>();

        int count = group.EligibleActsInSourceOrder.Count;
        if (count <= 0) return Array.Empty<(ModelKey, double)>();
        return group.EligibleActsInSourceOrder
            .GroupBy(key => key, ModelKeyComparer.Instance)
            .Select(items => (items.Key, items.Count() / (double)count))
            .ToArray();
    }

    private static SelectivityDependencyGraph MergeGraphs(
        IReadOnlyList<JointSelectivityResult> results,
        bool crossActAssumedIndependent = false)
    {
        var nodes = new List<SelectivityDependencyNode>();
        var edges = new List<SelectivityDependencyEdge>();
        foreach (JointSelectivityResult result in results)
        {
            foreach (SelectivityDependencyNode node in result.DependencyGraph.Nodes)
                if (!nodes.Any(item => item.Id == node.Id)) nodes.Add(node);
            edges.AddRange(result.DependencyGraph.Edges);
        }
        if (crossActAssumedIndependent)
        {
            string[] roots = results.SelectMany(result => result.DependencyGraph.Nodes)
                .Where(node => node.Kind == SelectivityDependencyNodeKind.QueryRoot)
                .Select(node => node.Id).Distinct(StringComparer.Ordinal).ToArray();
            for (int index = 1; index < roots.Length; index++)
                edges.Add(new(roots[0], roots[index], SelectivityDependencyEdgeKind.AssumedIndependent,
                    "ProbabilityDesign20260811", "Distinct Act-local World blocks use the current default independence policy."));
        }
        return new SelectivityDependencyGraph(nodes, edges);
    }

    private static string F(double? value) => value.HasValue
        ? value.Value.ToString("G17", CultureInfo.InvariantCulture)
        : "Unknown";
}
