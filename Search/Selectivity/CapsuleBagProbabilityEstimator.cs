using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

// Conditional extraction facts for the shared initial player RelicGrabBag.
// Callers own the Neow/Bones parent probability. This solver owns no C reward
// profile, held-state mixture or Product/physical reward probability policy.
internal static class CapsuleBagProbabilityEstimator
{
    private readonly record struct CapsuleDrawPosition(int Lane, int Ordinal);
    private readonly record struct CapsuleRarityChoice(int PreferredLane, double Probability, string Label);
    private sealed record CapsuleGroup(ModelKey Source, int DrawCount, IReadOnlyList<NeowStructuredEffectSearchCondition> Constraints);

    internal static bool TryEstimateConjunction(
        SearchSelectivityInput plan,
        IReadOnlyList<ModelKey> capsuleSources,
        bool includeExplicitRelicSequence,
        out double probability,
        out string detail,
        out string issue)
    {
        probability = 0;
        detail = string.Empty;
        issue = string.Empty;

        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out string poolIssue))
        {
            issue = "RuntimeRelicBagMissing:" + poolIssue;
            detail = "Capsule bag probability requires exact runtime player RelicGrabBag authority.";
            return false;
        }
        if (!TryBuildPlayerLanes(pool, denseByKey, out ModelKey[][] lanes, out string laneIssue))
        {
            issue = "RuntimeRelicLaneMissing:" + laneIssue;
            detail = "Capsule bag probability could not reconstruct the runtime Common/Uncommon/Rare lanes.";
            return false;
        }

        NeowStructuredEffectSearchCondition[] capsuleConstraints = ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects
            .Where(condition => !condition.IsEmpty && NeowReplayPlan.IsCapsule(condition) &&
                                condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                                condition.OutputKind == NeowStructuredOutputKind.Relic)
            .ToArray();
        var groupsBySource = new Dictionary<ModelKey, CapsuleGroup>(ModelKeyComparer.Instance);
        foreach (ModelKey source in capsuleSources)
        {
            int drawCount = source == BaseGameModelKeys.Relics.LargeCapsule ? 2 : 1;
            groupsBySource[source] = new CapsuleGroup(source, drawCount, capsuleConstraints.Where(item => item.SourceRelicKey == source).ToArray());
        }
        if (capsuleConstraints.Any(condition => !NeowReplayPlan.IsGroupedCapsule(condition) && !groupsBySource.ContainsKey(condition.SourceRelicKey)))
        {
            detail = "A nested Capsule predicate references a Capsule identity that is not explicitly present on the selected opening route.";
            return true;
        }
        var sourceFilter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        if (sourceFilter.RequireSmallCapsule && !groupsBySource.ContainsKey(BaseGameModelKeys.Relics.SmallCapsule) ||
            sourceFilter.RequireLargeCapsule && !groupsBySource.ContainsKey(BaseGameModelKeys.Relics.LargeCapsule) ||
            capsuleConstraints.Any(NeowReplayPlan.IsGroupedCapsule) && groupsBySource.Count != 2)
        { detail = "RequiredCapsuleSourceAbsent"; return true; }

        CapsuleGroup[][] routeGroupings;
        if (groupsBySource.Count <= 1)
        {
            routeGroupings = [groupsBySource.Values.ToArray()];
        }
        else
        {
            ModelKey[] pinnedOrder = ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder
                .Where(groupsBySource.ContainsKey)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            routeGroupings = pinnedOrder.Length == 2
                ? new[] { pinnedOrder.Select(key => groupsBySource[key]).ToArray() }
                : new[]
                {
                    new[] { groupsBySource[BaseGameModelKeys.Relics.LargeCapsule], groupsBySource[BaseGameModelKeys.Relics.SmallCapsule] },
                    new[] { groupsBySource[BaseGameModelKeys.Relics.SmallCapsule], groupsBySource[BaseGameModelKeys.Relics.LargeCapsule] }
                };
        }

        int totalDraws = groupsBySource.Values.Sum(group => group.DrawCount);
        if (totalDraws is < 0 or > 3)
        {
            issue = "CapsuleDrawCountUnsupported:" + totalDraws;
            detail = "Current opening Capsule topology supports one direct Capsule or the Bones Large+Small three-draw combination.";
            return false;
        }

        var distinguished = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (NeowStructuredEffectSearchCondition constraint in capsuleConstraints)
            foreach (ModelKey key in constraint.OutputKeys.Where(key => key.IsValid)) distinguished.Add(key);
        var capsuleFilter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        foreach (var key in capsuleFilter.CapsuleContainedRelics.All) distinguished.Add(key);
        if (capsuleFilter.RequireWhetstone) distinguished.Add(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        if (capsuleFilter.RequireWarPaint) distinguished.Add(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        var unmodeled = new HashSet<ModelKey>();
        if (capsuleSources.Count > 0 && plan.Authority.PlayersCount > 1)
        {
            var authored = PartyInitialQuery.CapsuleEffectPremise(plan.CompiledSearch.NormalizedQuery).Values.SelectMany(k => k).ToHashSet();
            foreach (var key in authored)
            {
                // Held reward modifiers do not affect a pure N/R extraction
                // probability. An explicitly applied obtain that changes pools,
                // nested extraction or reward continuation still needs authority.
                if (!VanillaRelicRewardEffects.TryGet(plan.ProfileId, key, out var effect) ||
                    (effect.OnObtainCapabilities & VanillaRelicRewardEffects.NestedRewardBlockingCapabilities) != 0)
                    unmodeled.Add(key);
            }
        }
        // All and structured identities need individual categories. Any/Ban and
        // unsupported-hook sets stay aggregate sequence constraints; their size
        // must not turn a legal three-draw observation into an arbitrary budget failure.
        var requiredIdentities = capsuleConstraints.SelectMany(c => c.OutputKeys)
            .Concat(capsuleFilter.CapsuleContainedRelics.All).ToHashSet();
        if (capsuleFilter.RequireWhetstone) requiredIdentities.Add(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        if (capsuleFilter.RequireWarPaint) requiredIdentities.Add(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        if (requiredIdentities.Count > totalDraws)
        { detail = "CapsuleRequiredIdentitiesExceedDrawCount"; return true; }
        distinguished.IntersectWith(lanes.SelectMany(l => l));

        var laneByKey = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        for (int lane = 0; lane < 3; lane++)
            foreach (ModelKey key in lanes[lane]) laneByKey[key] = lane;

        int acceptedScenarioCount = 0;
        foreach ((CapsuleRarityChoice[] rolls, double rollMass, string _) in EnumerateCapsuleRarityPatterns(totalDraws))
        {
            CapsuleDrawPosition?[] positions = ProjectCapsulePositions(rolls, lanes.Take(3).Select(items => items.Length).ToArray());
            foreach (ModelKey?[] assignment in EnumerateCompressedAssignments(positions, lanes, distinguished, laneByKey))
            {
                if (!RouteGroupingAccepts(routeGroupings, assignment) ||
                    capsuleConstraints.Where(NeowReplayPlan.IsGroupedCapsule).Any(c => !MatchesCapsuleConstraint(assignment, c))) continue;
                var actualKeys = assignment.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
                if (!capsuleFilter.CapsuleContainedRelics.All.All(actualKeys.Contains) ||
                    capsuleFilter.RequireWhetstone && !actualKeys.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone) ||
                    capsuleFilter.RequireWarPaint && !actualKeys.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint)) continue;
                double BagMass(IReadOnlyCollection<ModelKey> extraBan, out string solveIssue)
                {
                    ModelKey[] bans = capsuleFilter.CapsuleContainedRelics.Ban.Concat(extraBan).Distinct().ToArray();
                    var explicitRelics = includeExplicitRelicSequence ? capsuleFilter.RelicSequenceConditions : [];
                    double all = SolveCompressedAssignmentBagMass(lanes, explicitRelics, positions, assignment,
                        distinguished, out solveIssue, bans);
                    if (double.IsNaN(all) || all <= 0 || capsuleFilter.CapsuleContainedRelics.Any.Count == 0) return all;
                    double none = SolveCompressedAssignmentBagMass(lanes, explicitRelics, positions, assignment,
                        distinguished, out solveIssue, bans.Concat(capsuleFilter.CapsuleContainedRelics.Any).Distinct().ToArray());
                    return double.IsNaN(none) ? none : Math.Max(0, all - none);
                }
                double bagMass = BagMass([], out string solveIssue);
                if (double.IsNaN(bagMass))
                {
                    issue = "CapsuleBagSolver:" + solveIssue;
                    detail = "Capsule bag probability could not normalize the shared initial RelicGrabBag constraint.";
                    return false;
                }
                if (bagMass <= 0d) continue;
                if (unmodeled.Count > 0)
                {
                    double knownMass = BagMass(unmodeled, out solveIssue);
                    if (double.IsNaN(knownMass)) { issue = "CapsuleBagSolver:" + solveIssue; return false; }
                    if (bagMass - knownMass > 1e-14)
                    { issue = "AuthoredObtainChangesBagOrRewardContinuationPositiveMass"; return false; }
                }
                probability += rollMass * bagMass;
                acceptedScenarioCount++;
            }
        }

        probability = Math.Clamp(probability, 0d, 1d);
        detail = $"OpeningCapsuleBag;draws={totalDraws};acceptedScenarios={acceptedScenarioCount};conditionalMass={probability:G17};NoLatentRewardProfiles";
        return true;
    }

    private static IEnumerable<ModelKey?[]> EnumerateCompressedAssignments(
        IReadOnlyList<CapsuleDrawPosition?> positions,
        IReadOnlyList<ModelKey[]> lanes,
        IReadOnlySet<ModelKey> distinguished,
        IReadOnlyDictionary<ModelKey, int> laneByKey)
    {
        var buffer = new ModelKey?[positions.Count];
        var used = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (ModelKey?[] result in Walk(0)) yield return result;

        IEnumerable<ModelKey?[]> Walk(int index)
        {
            if (index == positions.Count)
            {
                yield return (ModelKey?[])buffer.Clone();
                yield break;
            }
            if (positions[index] is not { } position)
            {
                buffer[index] = BaseGameModelKeys.OrdinaryRelics.Circlet;
                foreach (ModelKey?[] result in Walk(index + 1)) yield return result;
                buffer[index] = null;
                yield break;
            }
            int lane = position.Lane;
            ModelKey[] candidates = distinguished.Where(key => laneByKey.TryGetValue(key, out int keyLane) && keyLane == lane && !used.Contains(key)).ToArray();
            foreach (ModelKey key in candidates)
            {
                buffer[index] = key;
                used.Add(key);
                foreach (ModelKey?[] result in Walk(index + 1)) yield return result;
                used.Remove(key);
            }

            int distinguishedInLane = distinguished.Count(key => laneByKey.TryGetValue(key, out int keyLane) && keyLane == lane);
            int alreadyUsedOtherSlots = 0;
            for (int prior = 0; prior < index; prior++)
                if (positions[prior] is { } previous && previous.Lane == lane && !buffer[prior].HasValue) alreadyUsedOtherSlots++;
            int otherAvailable = lanes[lane].Length - distinguishedInLane - alreadyUsedOtherSlots;
            if (otherAvailable > 0)
            {
                buffer[index] = null;
                foreach (ModelKey?[] result in Walk(index + 1)) yield return result;
            }
            buffer[index] = null;
        }
    }

    private static double SolveCompressedAssignmentBagMass(
        IReadOnlyList<ModelKey[]> lanes,
        IReadOnlyList<RelicSequenceSearchCondition> explicitRelicConditions,
        IReadOnlyList<CapsuleDrawPosition?> positions,
        IReadOnlyList<ModelKey?> assignment,
        IReadOnlySet<ModelKey> distinguished,
        out string issue,
        IReadOnlyCollection<ModelKey>? globalBans = null)
    {
        issue = string.Empty;
        globalBans ??= [];
        for (int draw = 0; draw < positions.Count; draw++)
            if (positions[draw] is null && globalBans.Contains(BaseGameModelKeys.OrdinaryRelics.Circlet)) return 0;
        double probability = 1d;
        for (int lane = 0; lane < 3; lane++)
        {
            var constraints = new List<FiniteSequenceProbabilitySolver.Constraint>();
            constraints.AddRange(explicitRelicConditions
                .Where(condition => LaneIndex(condition.Lane) == lane)
                .Select(condition => new FiniteSequenceProbabilitySolver.Constraint(condition.RangeMode, condition.RangeValue, condition.Keys)));

            ModelKey[] distinguishedLane = distinguished.Where(key => lanes[lane].Contains(key, ModelKeyComparer.Instance)).ToArray();
            for (int draw = 0; draw < positions.Count; draw++)
            {
                if (positions[draw] is not { } position || position.Lane != lane) continue;
                int ordinal = position.Ordinal;
                ModelKeySetFilter filter = assignment[draw].HasValue
                    ? new ModelKeySetFilter(new[] { assignment[draw]!.Value }, [], globalBans.ToArray())
                    : new ModelKeySetFilter([], [], distinguishedLane.Concat(globalBans).Distinct().ToArray());
                constraints.Add(new FiniteSequenceProbabilitySolver.Constraint(SearchSequenceRangeMode.ExactSlot, ordinal, filter));
            }

            if (constraints.Count == 0) continue;
            FiniteSequenceProbabilitySolver.Item[] items = lanes[lane].Select(key => new FiniteSequenceProbabilitySolver.Item(key)).ToArray();
            if (!FiniteSequenceProbabilitySolver.TrySolve(items, constraints, out FiniteSequenceProbabilitySolver.Result result, out issue))
                return double.NaN;
            probability *= result.Probability;
            if (probability == 0d) return 0d;
        }
        return Math.Clamp(probability, 0d, 1d);
    }

    private static bool RouteGroupingAccepts(IReadOnlyList<CapsuleGroup[]> groupings, IReadOnlyList<ModelKey?> assignment)
    {
        foreach (CapsuleGroup[] grouping in groupings)
        {
            int offset = 0;
            bool accepted = true;
            foreach (CapsuleGroup group in grouping)
            {
                ModelKey?[] outputs = assignment.Skip(offset).Take(group.DrawCount).ToArray();
                offset += group.DrawCount;
                if (group.Constraints.Any(c => !MatchesCapsuleConstraint(outputs, c)))
                {
                    accepted = false;
                    break;
                }
            }
            if (accepted) return true;
        }
        return false;
    }

    private static bool MatchesCapsuleConstraint(IReadOnlyList<ModelKey?> outputs, NeowStructuredEffectSearchCondition constraint)
    {
        ModelKey[] actual = outputs.Where(key => key.HasValue).Select(key => key.GetValueOrDefault()).ToArray();
        if (NeowReplayPlan.IsGroupedCapsule(constraint))
            return outputs.Count == 3 && constraint.OutputKeys.GroupBy(k=>k).All(g=>actual.Count(k=>k==g.Key)>=g.Count());
        if (constraint.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule)
        {
            return constraint.Kind == NeowStructuredConditionKind.ExactSingle &&
                   constraint.OutputKeys.Count == 1 &&
                   actual.Length == 1 && actual[0] == constraint.OutputKeys[0];
        }
        if (constraint.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule)
        {
            return constraint.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
                   outputs.Count == 2 &&
                   constraint.OutputKeys.GroupBy(k => k).All(g => actual.Count(k => k == g.Key) >= g.Count());
        }
        return false;
    }

    private static IEnumerable<(CapsuleRarityChoice[] Rolls, double Mass, string Label)> EnumerateCapsuleRarityPatterns(int drawCount)
    {
        CapsuleRarityChoice[] choices =
        {
            new(0, 0.50d, "C"),
            new(1, 0.33d, "U"),
            new(2, 0.17d, "R")
        };
        var buffer = new CapsuleRarityChoice[drawCount];
        foreach ((CapsuleRarityChoice[] Rolls, double Mass, string Label) result in Walk(0, 1d, string.Empty)) yield return result;

        IEnumerable<(CapsuleRarityChoice[] Rolls, double Mass, string Label)> Walk(int index, double mass, string label)
        {
            if (index == drawCount)
            {
                yield return ((CapsuleRarityChoice[])buffer.Clone(), mass, label);
                yield break;
            }
            foreach (CapsuleRarityChoice choice in choices)
            {
                buffer[index] = choice;
                foreach ((CapsuleRarityChoice[] Rolls, double Mass, string Label) result in Walk(index + 1, mass * choice.Probability, label + choice.Label))
                    yield return result;
            }
        }
    }

    private static CapsuleDrawPosition?[] ProjectCapsulePositions(IReadOnlyList<CapsuleRarityChoice> rolls, int[] initialCounts)
    {
        int[] remaining = (int[])initialCounts.Clone();
        int[] consumed = new int[remaining.Length];
        var output = new CapsuleDrawPosition?[rolls.Count];
        for (int draw = 0; draw < rolls.Count; draw++)
        {
            int selectedLane = -1;
            for (int lane = rolls[draw].PreferredLane; lane < 3; lane++)
            {
                if (remaining[lane] <= 0) continue;
                selectedLane = lane;
                break;
            }
            if (selectedLane < 0) continue;
            consumed[selectedLane]++;
            remaining[selectedLane]--;
            output[draw] = new CapsuleDrawPosition(selectedLane, consumed[selectedLane]);
        }
        return output;
    }

    private static bool TryBuildPlayerLanes(
        CompiledRelicPoolSnapshot pool,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        out ModelKey[][] lanes,
        out string issue)
    {
        lanes = new ModelKey[3][];
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
        for (int lane = 0; lane < 3; lane++)
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
            if (offset < 0 || length < 0 || offset + length > pool.DenseRelicIds.Length)
            {
                issue = "PlayerLaneBoundsInvalid:" + lane;
                return false;
            }
            var keys = new List<ModelKey>(length);
            for (int index = 0; index < length; index++)
            {
                ushort dense = pool.DenseRelicIds[offset + index];
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

    private static int LaneIndex(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => 0,
        RelicSequenceKind.Uncommon => 1,
        RelicSequenceKind.Rare => 2,
        _ => -1
    };
}
