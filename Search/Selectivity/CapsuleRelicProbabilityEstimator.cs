using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Probability authority for a direct Small/Large Capsule Neow route and the
/// Search UI's initial Relic-sequence predicates. Production source shows that
/// both projections replay the same player RelicGrabBag initialized from the
/// up_front stream. They are separate Search execution domains, but they are not
/// separate probability facts: Capsule pulls constrain positions in the same
/// rarity lanes exposed by the Relic-sequence Search domain.
///
/// The Capsule rarity rolls themselves live on Rewards. This model enumerates the
/// one/two rarity-roll outcomes, projects each pull onto a rarity-lane ordinal,
/// merges those implicit exact-position facts with explicit Relic constraints,
/// and solves the resulting finite permutations. No post-Capsule mutation of the
/// displayed Relic sequence is invented; the shared fact is the common initial bag.
/// </summary>
internal static class CapsuleRelicProbabilityEstimator
{
    private readonly record struct DrawPosition(int Lane, int Ordinal);
    private readonly record struct RarityRoll(int PreferredLane, double Mass, string Label);

    public static bool HasCapsuleRelicSharedConstraint(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ProbabilitySemanticView semantic = ProbabilitySemanticProjection.From(plan);
        return HasOverlappingRelicSequenceConstraint(plan) &&
               semantic.HasRelation(
                   SemanticRelationKind.SameFact,
                   SemanticFactKind.CapsuleNestedRelic,
                   SemanticFactKind.RelicGrabBag) &&
               semantic.HasRelation(
                   SemanticRelationKind.SequenceMembership,
                   SemanticFactKind.CapsuleNestedRelic,
                   SemanticFactKind.RelicGrabBag);
    }

    internal static bool HasOverlappingRelicSequenceConstraint(SearchSelectivityInput plan) =>
        ProbabilitySemanticProjection.From(plan).NumericalFilter.RelicSequenceConditions.Any(condition =>
            !condition.IsEmpty && IsCapsuleConsumedLane(condition.Lane));

    internal static RelicSequenceSearchCondition[] OverlappingRelicSequenceConditions(SearchSelectivityInput plan) =>
        ProbabilitySemanticProjection.From(plan).NumericalFilter.RelicSequenceConditions
            .Where(condition => !condition.IsEmpty && IsCapsuleConsumedLane(condition.Lane))
            .ToArray();

    internal static RelicShopSequenceSearchCondition[] IndependentShopRelicSequenceConditions(SearchSelectivityInput plan) =>
        ProbabilitySemanticProjection.From(plan).NumericalFilter.RelicShopSequenceConditions
            .Where(condition => !condition.IsEmpty)
            .ToArray();

    /// <summary>
    /// S3 migration parity oracle only. This is the pre-S3 legacy-filter shape
    /// detector and must not become a parallel Product semantic authority.
    /// </summary>
    public static bool IsDirectCapsuleStructuredQuery(SearchSelectivityInput plan)
    {
        if (ProbabilitySemanticProjection.From(plan).NumericalFilter.NeowRoute is not { IsValid: true } route) return false;
        if (route.RouteRelicKey != BaseGameModelKeys.Relics.SmallCapsule &&
            route.RouteRelicKey != BaseGameModelKeys.Relics.LargeCapsule) return false;
        return ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects.Any(condition =>
            !condition.IsEmpty &&
            condition.SourceRelicKey == route.RouteRelicKey &&
            condition.Scope == NeowStructuredEffectScope.NestedRelics &&
            condition.OutputKind == NeowStructuredOutputKind.Relic);
    }

    public static SearchSelectivityEstimate Estimate(
        SearchSelectivityInput plan,
        bool includeExplicitRelicSequence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ProbabilitySemanticProjection.From(plan).NumericalFilter.NeowRoute is not { IsValid: true } route ||
            (route.RouteRelicKey != BaseGameModelKeys.Relics.SmallCapsule &&
             route.RouteRelicKey != BaseGameModelKeys.Relics.LargeCapsule))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Capsule.DirectRouteMissing",
                "Capsule probability requires a direct selected Small/Large Capsule Neow route.");
        }

        NeowStructuredEffectSearchCondition[] structured = ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects
            .Where(condition => !condition.IsEmpty && condition.SourceRelicKey == route.RouteRelicKey)
            .ToArray();
        if (structured.Length != 1 ||
            structured[0].Scope != NeowStructuredEffectScope.NestedRelics ||
            structured[0].OutputKind != NeowStructuredOutputKind.Relic)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Capsule.StructuredShapeUnsupported",
                "Direct Capsule pricing currently requires exactly one NestedRelics structured condition for the selected Capsule source.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        ModelKey[] targets = structured[0].OutputKeys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        int drawCount = route.RouteRelicKey == BaseGameModelKeys.Relics.SmallCapsule ? 1 : 2;
        if (targets.Length == 0 || targets.Length > drawCount ||
            (drawCount == 1 && structured[0].Kind != NeowStructuredConditionKind.ExactSingle) ||
            (drawCount == 2 && structured[0].Kind != NeowStructuredConditionKind.ExactUnorderedPair))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Capsule.TargetShapeUnsupported",
                "Capsule target subset does not match the current Search structured-output semantics.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        RelicSequenceSearchCondition[] explicitRelicConditions = includeExplicitRelicSequence
            ? OverlappingRelicSequenceConditions(plan)
            : Array.Empty<RelicSequenceSearchCondition>();
        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out string poolIssue))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CapsuleRelic.RuntimePoolMissing:" + poolIssue,
                "Capsule/Relic shared-constraint pricing requires the immutable exact runtime player RelicGrabBag authority.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        if (!TryBuildLanePools(pool, denseByKey, out ModelKey[][] lanes, out string laneIssue))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CapsuleRelic.LaneAuthority:" + laneIssue,
                "The runtime Relic lane identities could not be reconstructed exactly.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        var laneByKey = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        for (int lane = 0; lane < lanes.Length; lane++)
        {
            foreach (ModelKey key in lanes[lane])
            {
                if (laneByKey.TryGetValue(key, out int existing) && existing != lane)
                {
                    return SearchSelectivityEstimate.Unpriced(
                        "Probability.CapsuleRelic.IdentityInMultipleRarityLanes",
                        "A runtime Relic identity appears in multiple player rarity lanes; no unique Capsule queue constraint can be assigned.");
                }
                laneByKey[key] = lane;
            }
        }

        foreach (ModelKey target in targets)
        {
            if (!laneByKey.ContainsKey(target))
            {
                return SearchSelectivityEstimate.Exact(
                    0d,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.ExactRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    "Probability.CapsuleRelic.TargetAbsentFromRuntimeBag",
                    "A requested Capsule nested Relic is absent from the runtime Common/Uncommon/Rare player grab bag.",
                    new[] { "Circlet/fallback-only special identities are not silently treated as ordinary bag entries." });
            }
        }

        // Match the Product evaluator boundary: an explicit exact Relic slot beyond
        // the captured lane is Unknown/unavailable rather than an authority-proven NoMatch.
        foreach (RelicSequenceSearchCondition condition in explicitRelicConditions
                     .Where(condition => condition.RangeMode == SearchSequenceRangeMode.ExactSlot))
        {
            int lane = LaneIndex(condition.Lane);
            if (lane < 0 || condition.RangeValue > lanes[lane].Length)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.CapsuleRelic.ExactSlotOutsideCapturedLane",
                    $"Explicit Relic slot {condition.RangeValue} is unavailable in {condition.Lane}; Product evaluator keeps this Unknown.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.WithoutReplacement,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence);
            }
        }

        if (!NeowStructuredEffectProbabilityEstimator.TryTopLevelRouteProbability(
                plan, route.RouteRelicKey, out double parentProbability, out string parentEvidence))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CapsuleRelic.ParentIdentityUnpriced",
                "The selected Capsule parent offer cannot be priced from current runtime Neow authority.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }
        if (parentProbability == 0d)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.CapsuleRelic.ParentImpossible",
                "The selected Capsule parent is absent from the runtime Neow offer space.");
        }

        double conditional = 0d;
        var branchDetails = new List<string>();
        foreach ((RarityRoll[] rolls, double rollMass, string label) in EnumerateRollPatterns(drawCount))
        {
            DrawPosition?[] positions = ProjectDrawPositions(rolls, lanes.Select(lane => lane.Length).ToArray());
            IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> scenarios =
                BuildTargetScenarios(positions, targets, laneByKey);
            if (scenarios.Count == 0)
            {
                branchDetails.Add(label + "=0(TargetLaneMismatch)");
                continue;
            }

            double bagMass = 0d;
            foreach (IReadOnlyList<(DrawPosition Position, ModelKey Target)> scenario in scenarios)
            {
                double scenarioMass = SolveSharedBagScenario(lanes, explicitRelicConditions, scenario, out string solveIssue);
                if (double.IsNaN(scenarioMass))
                {
                    return SearchSelectivityEstimate.Unpriced(
                        "Probability.CapsuleRelic.SequenceSolver:" + solveIssue,
                        "A Capsule/Relic shared sequence relation is outside the current exact finite-permutation solver.",
                        SearchSelectivityConfidence.High,
                        SearchSelectivityMethod.WithoutReplacement,
                        SearchSelectivityCoverage.PartialRequestedConjunction,
                        SearchSelectivityDependencyClass.StructuralDependence,
                        new[] { "SharedConstraint=Capsule×RelicQueue" });
                }
                bagMass += scenarioMass;
            }
            bagMass = Math.Clamp(bagMass, 0d, 1d);
            conditional += rollMass * bagMass;
            branchDetails.Add(label + "=" + (rollMass * bagMass).ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
        }

        conditional = Math.Clamp(conditional, 0d, 1d);
        double final = Math.Clamp(parentProbability * conditional, 0d, 1d);
        string scope = includeExplicitRelicSequence ? "Capsule×RelicQueue" : "CapsuleNestedRelic";
        return SearchSelectivityEstimate.Exact(
            final,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            includeExplicitRelicSequence
                ? "Probability.Authority.CapsuleRelicSharedConstraint"
                : "Probability.Authority.CapsuleNestedRelic",
            $"{scope}: parent Neow offer is paid once; Capsule Rewards-rarity branches are enumerated and each branch is merged with the same runtime up_front player RelicGrabBag lanes. Parent={parentProbability:G17};conditional={conditional:G17}; {parentEvidence}.",
            new[]
            {
                "SharedConstraint=Capsule×InitialRelicGrabBag",
                "Production source evidence: SearchAuthorityProjector.BuildOrderedRelicBag and RelicSequencePredictor.Replay consume the same shared+player up_front bag initialization.",
                "Separate Search execution systems do not imply separate probability facts.",
                "Capsule rarity-roll branches use the source 0.50/0.33/0.17 masses and are combined with lane permutation constraints.",
                "Only Common/Uncommon/Rare Relic Queue constraints participate in the Capsule shared runtime-consumption block.",
                "Shop runtime lane constraints are intentionally excluded and priced as an independent Relic remainder by query-wide normalization.",
                "Branches=" + string.Join(";", branchDetails)
            });
    }

    internal static bool TryEstimateBonesNestedCapsules(
        SearchSelectivityInput plan,
        IReadOnlyList<NeowStructuredEffectSearchCondition> capsuleConditions,
        bool includeExplicitRelicSequence,
        IReadOnlyList<ModelKey>? actualBonesPair,
        out double conditionalProbability,
        out string detail,
        out string issue)
    {
        ArgumentNullException.ThrowIfNull(plan);
        conditionalProbability = 0d;
        detail = string.Empty;
        issue = string.Empty;

        NeowStructuredEffectSearchCondition[] active = capsuleConditions
            .Where(condition => !condition.IsEmpty && NeowReplayPlan.IsCapsule(condition))
            .ToArray();
        if (active.Length == 0)
        {
            conditionalProbability = 1d;
            detail = "No nested Capsule structured predicate.";
            return true;
        }
        if (active.GroupBy(condition => condition.SourceRelicKey, ModelKeyComparer.Instance).Any(group => group.Count() != 1) ||
            active.Length > 2)
        {
            issue = "BonesCapsuleSourceMultiplicityUnsupported";
            detail = "Bones can contain at most one Small Capsule and one Large Capsule identity; each source must contribute one structured NestedRelics predicate.";
            return false;
        }

        bool grouped = active.Any(NeowReplayPlan.IsGroupedCapsule);
        if (grouped && (active.Length != 1 || active[0].OutputKeys.Count is < 1 or > 3))
        { issue = "GroupedCapsuleMixedOrInvalidShape"; return false; }
        if (grouped && actualBonesPair is not null &&
            !actualBonesPair.ToHashSet().SetEquals([BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule]))
        { detail = "Grouped requires both Capsules"; return true; }
        // Grouped is one three-draw observation, not the union of two source routes.
        var requests = new List<(ModelKey Source, ModelKey[] Targets, int DrawCount)>();
        foreach (NeowStructuredEffectSearchCondition condition in active)
        {
            int drawCount = grouped ? 3 : condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule ? 1 : 2;
            ModelKey[] targets = condition.OutputKeys
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (grouped) targets = condition.OutputKeys.ToArray(); // preserve multiset multiplicity
            bool shapeOk = grouped || condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                           condition.OutputKind == NeowStructuredOutputKind.Relic &&
                           targets.Length is >= 1 && targets.Length <= 2 &&
                           targets.Length <= drawCount &&
                           (drawCount == 1
                               ? condition.Kind == NeowStructuredConditionKind.ExactSingle
                               : condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair);
            if (!shapeOk)
            {
                issue = "BonesCapsuleStructuredShapeUnsupported:" + condition.SourceRelicKey.Entry;
                detail = "Nested Capsule target shape does not match current Search structured-output semantics.";
                return false;
            }
            requests.Add((condition.SourceRelicKey, targets, drawCount));
        }

        // The actual unordered Bones pair is the probability fact being conditioned on.
        // If that pair contains the sibling Capsule even when the sibling has no
        // explicit nested-output predicate, it still consumes the same RelicGrabBag.
        // When no explicit pair is supplied (defensive/direct reuse), fall back to the
        // Capsule identities explicitly required by the normalized Search query.
        ModelKey[] requiredCapsuleSources = (actualBonesPair ??
                ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesCombination
                    .Concat(ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder)
                    .ToArray())
            .Where(IsCapsule)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

        if (!grouped && actualBonesPair is not null &&
            active.Any(condition => !actualBonesPair.Contains(condition.SourceRelicKey, ModelKeyComparer.Instance)))
        {
            conditionalProbability = 0d;
            detail = "A structured Capsule source is absent from the conditioned Bones pair.";
            return true;
        }
        foreach (ModelKey source in grouped ? Array.Empty<ModelKey>() : requiredCapsuleSources)
        {
            if (requests.Any(request => request.Source == source)) continue;
            requests.Add((
                source,
                Array.Empty<ModelKey>(),
                source == BaseGameModelKeys.Relics.SmallCapsule ? 1 : 2));
        }

        RelicSequenceSearchCondition[] explicitRelicConditions = includeExplicitRelicSequence
            ? OverlappingRelicSequenceConditions(plan)
            : Array.Empty<RelicSequenceSearchCondition>();

        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out string poolIssue))
        {
            issue = "RuntimePoolMissing:" + poolIssue;
            detail = "Bones nested Capsule probability requires the immutable exact runtime player RelicGrabBag authority.";
            return false;
        }
        if (!TryBuildLanePools(pool, denseByKey, out ModelKey[][] lanes, out string laneIssue))
        {
            issue = "LaneAuthority:" + laneIssue;
            detail = "Runtime Relic lane identities could not be reconstructed exactly.";
            return false;
        }

        var laneByKey = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        for (int lane = 0; lane < 3; lane++)
        {
            foreach (ModelKey key in lanes[lane])
            {
                if (laneByKey.TryGetValue(key, out int existing) && existing != lane)
                {
                    issue = "IdentityInMultipleRarityLanes:" + key.Serialized;
                    detail = "A runtime Relic identity appears in multiple player rarity lanes.";
                    return false;
                }
                laneByKey[key] = lane;
            }
        }
        foreach (ModelKey target in requests.SelectMany(request => request.Targets))
        {
            if (!laneByKey.ContainsKey(target))
            {
                conditionalProbability = 0d;
                detail = "At least one requested Capsule nested Relic is absent from the runtime Common/Uncommon/Rare player grab bag.";
                return true;
            }
        }

        foreach (RelicSequenceSearchCondition condition in explicitRelicConditions
                     .Where(condition => condition.RangeMode == SearchSequenceRangeMode.ExactSlot))
        {
            int lane = LaneIndex(condition.Lane);
            if (lane < 0 || lane >= lanes.Length || condition.RangeValue > lanes[lane].Length)
            {
                issue = "ExactSlotOutsideCapturedLane:" + condition.Lane + ":" + condition.RangeValue;
                detail = "Explicit Relic exact-slot constraint is unavailable in the captured runtime lane; Product evaluator keeps this Unknown.";
                return false;
            }
        }

        int[][] routeOrders;
        if (requests.Count == 1)
        {
            routeOrders = new[] { new[] { 0 } };
        }
        else
        {
            ModelKey[] pinned = ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder
                .Where(key => key.IsValid && requests.Any(request => request.Source == key))
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (pinned.Length == 2 &&
                pinned.All(key => requests.Any(request => request.Source == key)))
            {
                routeOrders = new[]
                {
                    pinned.Select(key => requests.FindIndex(request => request.Source == key)).ToArray()
                };
            }
            else
            {
                routeOrders = new[] { new[] { 0, 1 }, new[] { 1, 0 } };
            }
        }

        int totalDraws = requests.Sum(request => request.DrawCount);
        int[] rarityLaneCounts = lanes.Take(3).Select(lane => lane.Length).ToArray();
        double total = 0d;
        var branchNotes = new List<string>();
        foreach ((RarityRoll[] rolls, double rollMass, string label) in EnumerateRollPatterns(totalDraws))
        {
            DrawPosition?[] positions = ProjectDrawPositions(rolls, rarityLaneCounts);
            var allScenarios = new List<IReadOnlyList<(DrawPosition Position, ModelKey Target)>>();

            foreach (int[] order in routeOrders)
            {
                IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> routeScenarios =
                    BuildRouteScenarios(order, requests, positions, laneByKey);
                allScenarios.AddRange(routeScenarios);
            }

            IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> uniqueScenarios =
                DeduplicateScenarios(allScenarios);
            double bagMass;
            if (uniqueScenarios.Count == 0)
            {
                bagMass = 0d;
            }
            else if (!TrySolveScenarioUnion(lanes, explicitRelicConditions, uniqueScenarios, out bagMass, out string unionIssue))
            {
                issue = "SequenceUnion:" + unionIssue;
                detail = "Bones nested Capsule route alternatives require a shared RelicGrabBag relation outside the current finite-sequence union solver.";
                return false;
            }

            total += rollMass * bagMass;
            if (branchNotes.Count < 12)
                branchNotes.Add(label + "→" + (rollMass * bagMass).ToString("G8", System.Globalization.CultureInfo.InvariantCulture));
        }

        conditionalProbability = Math.Clamp(total, 0d, 1d);
        string pairLabel = actualBonesPair is null
            ? "QueryRequired"
            : string.Join("+", actualBonesPair.Select(key => key.Entry));
        detail =
            $"Bones nested Capsule SharedConstraint;pair={pairLabel};capsules={requests.Count};filteredCapsules={active.Length};draws={totalDraws};routeAlternatives={routeOrders.Length};conditional={conditionalProbability:G17};branches={string.Join(',', branchNotes)}";
        return true;
    }

    private static IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> BuildRouteScenarios(
        IReadOnlyList<int> order,
        IReadOnlyList<(ModelKey Source, ModelKey[] Targets, int DrawCount)> requests,
        IReadOnlyList<DrawPosition?> positions,
        IReadOnlyDictionary<ModelKey, int> laneByKey)
    {
        var combined = new List<IReadOnlyList<(DrawPosition Position, ModelKey Target)>>
        {
            Array.Empty<(DrawPosition Position, ModelKey Target)>()
        };
        int offset = 0;
        foreach (int requestIndex in order)
        {
            (ModelKey Source, ModelKey[] Targets, int DrawCount) request = requests[requestIndex];
            DrawPosition?[] slice = positions.Skip(offset).Take(request.DrawCount).ToArray();
            offset += request.DrawCount;
            IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> local =
                BuildTargetScenarios(slice, request.Targets, laneByKey);
            if (local.Count == 0) return Array.Empty<IReadOnlyList<(DrawPosition Position, ModelKey Target)>>();

            var next = new List<IReadOnlyList<(DrawPosition Position, ModelKey Target)>>();
            foreach (IReadOnlyList<(DrawPosition Position, ModelKey Target)> prefix in combined)
            foreach (IReadOnlyList<(DrawPosition Position, ModelKey Target)> suffix in local)
                next.Add(prefix.Concat(suffix).ToArray());
            combined = next;
        }
        return combined;
    }

    private static IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> DeduplicateScenarios(
        IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> scenarios)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var output = new List<IReadOnlyList<(DrawPosition Position, ModelKey Target)>>();
        foreach (IReadOnlyList<(DrawPosition Position, ModelKey Target)> scenario in scenarios)
        {
            string key = string.Join("|", scenario
                .OrderBy(fact => fact.Position.Lane)
                .ThenBy(fact => fact.Position.Ordinal)
                .ThenBy(fact => fact.Target.Serialized, StringComparer.Ordinal)
                .Select(fact => $"{fact.Position.Lane}:{fact.Position.Ordinal}:{fact.Target.Serialized}"));
            if (seen.Add(key)) output.Add(scenario);
        }
        return output;
    }

    private static bool TrySolveScenarioUnion(
        IReadOnlyList<ModelKey[]> lanes,
        IReadOnlyList<RelicSequenceSearchCondition> explicitConditions,
        IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> scenarios,
        out double probability,
        out string issue)
    {
        probability = 0d;
        issue = string.Empty;
        int n = scenarios.Count;
        if (n == 0) return true;
        if (n > 12)
        {
            issue = "TooManyScenarioAlternatives:" + n;
            return false;
        }

        double total = 0d;
        int subsetCount = 1 << n;
        for (int mask = 1; mask < subsetCount; mask++)
        {
            var facts = new List<(DrawPosition Position, ModelKey Target)>();
            int bits = 0;
            for (int index = 0; index < n; index++)
            {
                if ((mask & (1 << index)) == 0) continue;
                bits++;
                facts.AddRange(scenarios[index]);
            }
            double intersection = SolveSharedBagScenario(lanes, explicitConditions, facts, out string solveIssue);
            if (double.IsNaN(intersection))
            {
                issue = solveIssue;
                return false;
            }
            total += (bits & 1) == 1 ? intersection : -intersection;
        }
        probability = Math.Clamp(total, 0d, 1d);
        return true;
    }

    private static IEnumerable<(RarityRoll[] Rolls, double Mass, string Label)> EnumerateRollPatterns(int drawCount)
    {
        RarityRoll[] choices =
        {
            new(0, 0.50d, "C"),
            new(1, 0.33d, "U"),
            new(2, 0.17d, "R")
        };
        if (drawCount <= 0)
        {
            yield return (Array.Empty<RarityRoll>(), 1d, "-");
            yield break;
        }

        var buffer = new RarityRoll[drawCount];
        foreach ((RarityRoll[] Rolls, double Mass, string Label) value in Enumerate(0, 1d, string.Empty))
            yield return value;

        IEnumerable<(RarityRoll[] Rolls, double Mass, string Label)> Enumerate(int index, double mass, string label)
        {
            if (index == buffer.Length)
            {
                yield return ((RarityRoll[])buffer.Clone(), mass, label);
                yield break;
            }
            foreach (RarityRoll choice in choices)
            {
                buffer[index] = choice;
                foreach ((RarityRoll[] Rolls, double Mass, string Label) value in
                         Enumerate(index + 1, mass * choice.Mass, label + choice.Label))
                    yield return value;
            }
        }
    }

    private static DrawPosition?[] ProjectDrawPositions(IReadOnlyList<RarityRoll> rolls, int[] initialCounts)
    {
        int[] remaining = (int[])initialCounts.Clone();
        int[] consumed = new int[remaining.Length];
        var output = new DrawPosition?[rolls.Count];
        for (int draw = 0; draw < rolls.Count; draw++)
        {
            int selectedLane = -1;
            for (int lane = rolls[draw].PreferredLane; lane < 3; lane++)
            {
                if (remaining[lane] <= 0) continue;
                selectedLane = lane;
                break;
            }
            if (selectedLane < 0)
            {
                // RelicFactory yields Circlet after the permitted fallback chain is
                // exhausted. It is not a player-bag position.
                output[draw] = null;
                continue;
            }
            consumed[selectedLane]++;
            remaining[selectedLane]--;
            output[draw] = new DrawPosition(selectedLane, consumed[selectedLane]);
        }
        return output;
    }

    private static IReadOnlyList<IReadOnlyList<(DrawPosition Position, ModelKey Target)>> BuildTargetScenarios(
        IReadOnlyList<DrawPosition?> positions,
        IReadOnlyList<ModelKey> targets,
        IReadOnlyDictionary<ModelKey, int> laneByKey)
    {
        var scenarios = new List<IReadOnlyList<(DrawPosition Position, ModelKey Target)>>();
        if (targets.Count == 0)
        {
            // Unfiltered Capsule: consume its draw positions but impose no identity
            // fact. This is necessary when the sibling Capsule is filtered and Bones
            // acquisition order can move that filtered source to later bag positions.
            scenarios.Add(Array.Empty<(DrawPosition Position, ModelKey Target)>());
            return scenarios;
        }
        if (targets.Count == 1)
        {
            for (int draw = 0; draw < positions.Count; draw++)
            {
                if (!positions[draw].HasValue) continue;
                DrawPosition position = positions[draw]!.Value;
                if (!laneByKey.TryGetValue(targets[0], out int lane) || lane != position.Lane) continue;
                scenarios.Add(new[] { (position, targets[0]) });
            }
            return scenarios;
        }

        // Bounded distinct draw assignment (at most 3!); the shared finite-bag
        // union below handles overlaps and repeated target multiplicity exactly.
        var used = new bool[positions.Count];
        var facts = new List<(DrawPosition Position, ModelKey Target)>();
        void Assign(int target) {
            if (target == targets.Count) { scenarios.Add(facts.ToArray()); return; }
            for (int draw = 0; draw < positions.Count; draw++) {
                if (used[draw] || positions[draw] is not { } position ||
                    !laneByKey.TryGetValue(targets[target],out int lane) || lane != position.Lane) continue;
                used[draw]=true; facts.Add((position, targets[target])); Assign(target+1);
                facts.RemoveAt(facts.Count-1); used[draw]=false;
            }
        }
        if (targets.Count <= positions.Count && targets.Count <= 3) Assign(0);
        return scenarios;
    }

    private static double SolveSharedBagScenario(
        IReadOnlyList<ModelKey[]> lanes,
        IReadOnlyList<RelicSequenceSearchCondition> explicitConditions,
        IReadOnlyList<(DrawPosition Position, ModelKey Target)> implicitFacts,
        out string issue)
    {
        issue = string.Empty;
        double probability = 1d;
        for (int lane = 0; lane < lanes.Count; lane++)
        {
            RelicSequenceKind kind = LaneKind(lane);
            RelicSequenceSearchCondition[] explicitLane = explicitConditions.Where(condition => condition.Lane == kind).ToArray();
            (DrawPosition Position, ModelKey Target)[] implicitLane = implicitFacts.Where(fact => fact.Position.Lane == lane).ToArray();
            if (explicitLane.Length == 0 && implicitLane.Length == 0) continue;

            var constraints = new List<FiniteSequenceProbabilitySolver.Constraint>();
            constraints.AddRange(explicitLane.Select(condition => new FiniteSequenceProbabilitySolver.Constraint(
                condition.RangeMode,
                condition.RangeValue,
                condition.Keys)));
            foreach ((DrawPosition position, ModelKey target) in implicitLane)
            {
                constraints.Add(new FiniteSequenceProbabilitySolver.Constraint(
                    SearchSequenceRangeMode.ExactSlot,
                    position.Ordinal,
                    new ModelKeySetFilter(
                        new[] { target },
                        Array.Empty<ModelKey>(),
                        Array.Empty<ModelKey>())));
            }

            FiniteSequenceProbabilitySolver.Item[] items = lanes[lane]
                .Select(key => new FiniteSequenceProbabilitySolver.Item(key))
                .ToArray();
            if (!FiniteSequenceProbabilitySolver.TrySolve(items, constraints, out FiniteSequenceProbabilitySolver.Result solved, out issue))
                return double.NaN;
            probability *= solved.Probability;
            if (probability == 0d) return 0d;
        }
        return Math.Clamp(probability, 0d, 1d);
    }

    private static bool TryBuildLanePools(
        CompiledRelicPoolSnapshot pool,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        out ModelKey[][] lanes,
        out string issue)
    {
        lanes = new ModelKey[4][];
        issue = string.Empty;
        var keyByDense = new Dictionary<ushort, ModelKey>();
        foreach ((ModelKey key, ushort dense) in denseByKey)
        {
            if (!keyByDense.TryAdd(dense, key) && keyByDense[dense] != key)
            {
                issue = "DenseIdentityAmbiguous:" + dense;
                return false;
            }
        }

        for (int lane = 0; lane < 4; lane++)
        {
            if (lane >= pool.PlayerLaneBucketIndexes.Length)
            {
                issue = "PlayerLaneIndexMissing:" + lane;
                return false;
            }
            short bucket = pool.PlayerLaneBucketIndexes[lane];
            if (bucket < 0 || bucket >= pool.BucketCount)
            {
                issue = "PlayerLaneMissing:" + lane;
                return false;
            }
            int offset = pool.BucketOffsets[bucket];
            int length = pool.BucketLengths[bucket];
            if (offset < 0 || length < 0 || offset + length > pool.DenseRelicIds.Length ||
                offset + length > pool.EntryFlags.Length)
            {
                issue = "PlayerLaneBoundsInvalid:" + lane;
                return false;
            }

            var keys = new List<ModelKey>(length);
            for (int index = 0; index < length; index++)
            {
                int entryIndex = offset + index;
                if (lane == 3 &&
                    (pool.EntryFlags[entryIndex] & (byte)Beta110RelicEntryFlags.AllowedInShops) == 0)
                    continue;
                ushort dense = pool.DenseRelicIds[entryIndex];
                if (!keyByDense.TryGetValue(dense, out ModelKey key))
                {
                    issue = "DenseIdentityMissing:" + dense;
                    return false;
                }
                keys.Add(key);
            }
            if (keys.Distinct(ModelKeyComparer.Instance).Count() != keys.Count)
            {
                issue = "DuplicateIdentityInPlayerLane:" + lane;
                return false;
            }
            lanes[lane] = keys.ToArray();
        }
        return true;
    }

    private static bool IsCapsule(ModelKey key) =>
        key == BaseGameModelKeys.Relics.SmallCapsule ||
        key == BaseGameModelKeys.Relics.LargeCapsule;

    private static bool IsCapsuleConsumedLane(RelicSequenceKind kind) =>
        kind is RelicSequenceKind.Common or RelicSequenceKind.Uncommon or RelicSequenceKind.Rare;

    private static int LaneIndex(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => 0,
        RelicSequenceKind.Uncommon => 1,
        RelicSequenceKind.Rare => 2,
        RelicSequenceKind.Shop => 3,
        _ => -1
    };

    private static RelicSequenceKind LaneKind(int lane) => lane switch
    {
        0 => RelicSequenceKind.Common,
        1 => RelicSequenceKind.Uncommon,
        2 => RelicSequenceKind.Rare,
        _ => RelicSequenceKind.Shop
    };
}
