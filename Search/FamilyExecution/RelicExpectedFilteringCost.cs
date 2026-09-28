using System.Globalization;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

public sealed class FamilyExpectedFilteringCostProjection
{
    private readonly IReadOnlyList<FamilyExpectedFilteringCostSegment> _segments;

    internal FamilyExpectedFilteringCostProjection(
        string familyId,
        double fullFilteringWorkUnitsPerInput,
        double? expectedFilteringWorkUnitsPerInput,
        double? finalSurvival,
        double compactInputWorkUnitsPerInput,
        IReadOnlyList<FamilyExpectedFilteringCostSegment> segments,
        string evidence)
    {
        FamilyId = familyId;
        FullFilteringWorkUnitsPerInput = fullFilteringWorkUnitsPerInput;
        ExpectedFilteringWorkUnitsPerInput = expectedFilteringWorkUnitsPerInput;
        FinalSurvival = finalSurvival;
        CompactInputWorkUnitsPerInput = compactInputWorkUnitsPerInput;
        _segments = segments;
        Evidence = evidence;
    }

    public string FamilyId { get; }
    public double FullFilteringWorkUnitsPerInput { get; }
    public double? ExpectedFilteringWorkUnitsPerInput { get; }
    public double? FinalSurvival { get; }
    public double CompactInputWorkUnitsPerInput { get; }
    public bool IsResolved => ExpectedFilteringWorkUnitsPerInput.HasValue && FinalSurvival.HasValue;
    public double? ExpectedToFullRatio =>
        IsResolved && FullFilteringWorkUnitsPerInput > 0d
            ? ExpectedFilteringWorkUnitsPerInput!.Value / FullFilteringWorkUnitsPerInput
            : null;
    public string Evidence { get; }

    internal IReadOnlyList<FamilyExpectedFilteringCostSegment> Segments => _segments;

    internal string FormatSummary()
    {
        string top = IsResolved
            ? string.Join('|', _segments.OrderByDescending(segment => segment.ExpectedContribution)
                .Take(5)
                .Select(segment =>
                    $"{San(segment.Name)}:{F(segment.FullWorkUnits)}@{F(segment.ReachProbability)}={F(segment.ExpectedContribution)}"))
            : "unavailable";
        return $"family={FamilyId};fullFilteringWorkUnitsPerInput={F(FullFilteringWorkUnitsPerInput)};" +
               $"expectedFilteringWorkUnitsPerInput={Maybe(ExpectedFilteringWorkUnitsPerInput)};" +
               $"expectedToFullRatio={Maybe(ExpectedToFullRatio)};finalSurvival={Maybe(FinalSurvival)};" +
               $"compactInputWorkUnitsPerInput={F(CompactInputWorkUnitsPerInput)};" +
               $"segmentCount={_segments.Count};topExpectedContributors={top};evidence={San(Evidence)}";
    }

    private static string Maybe(double? value) => value.HasValue ? F(value.Value) : "unresolved";
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string San(string value) => string.IsNullOrWhiteSpace(value)
        ? "none"
        : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>
/// Internal diagnostic line item shared only by the two Family-local projectors.
/// It is not an execution node, graph, scheduler contract or Planner surface.
/// </summary>
internal readonly record struct FamilyExpectedFilteringCostSegment(
    string Name,
    FamilyAnalyticalOperation Operation,
    double FullWorkUnits,
    double ReachProbability)
{
    internal double ExpectedContribution => FullWorkUnits * ReachProbability;
}

/// <summary>
/// R-local analytical projection of the actual shader early-return path. It consumes
/// immutable Query/pool probability only; runtime survivors and Hardware never enter it.
/// </summary>
internal static class RelicExpectedFilteringCost
{
    internal static FamilyExpectedFilteringCostProjection Project(
        ExactSearchExecutionRequest request,
        RelicFamilyPlan plan,
        FamilyAnalyticalCostProjection fullCost,
        FamilySurvivalProjection survival)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fullCost);
        ArgumentNullException.ThrowIfNull(survival);

        if (!survival.IsResolved)
            return Unresolved(fullCost, survival.SurvivalProbability, "FinalSurvivalUnresolved:" + survival.Evidence);
        if (!SearchSelectivityEstimator.TryGetRelicLaneProbabilityAuthority(
                SearchSelectivityInput.From(request), out RelicLaneProbabilityAuthority? authority, out string issue) ||
            authority is null)
            return Unresolved(fullCost, survival.SurvivalProbability, "RelicLaneAuthorityUnavailable:" + issue);

        try
        {
            return Build(request.Evaluation, plan, fullCost, authority, survival.SurvivalProbability);
        }
        catch (Exception ex)
        {
            return Unresolved(fullCost, survival.SurvivalProbability,
                "RelicExpectedFilteringProjectionFailed:" + ex.GetType().Name + ':' + ex.Message);
        }
    }

    internal static FamilyExpectedFilteringCostProjection ProjectForTesting(
        ExactSearchEvaluationProjection evaluation,
        RelicFamilyPlan plan,
        FamilyAnalyticalCostProjection fullCost,
        RelicLaneProbabilityAuthority authority) =>
        Build(evaluation, plan, fullCost, authority, authoritativeFinalSurvival: null);

    private static FamilyExpectedFilteringCostProjection Build(
        ExactSearchEvaluationProjection evaluation,
        RelicFamilyPlan plan,
        FamilyAnalyticalCostProjection fullCost,
        RelicLaneProbabilityAuthority authority,
        double? authoritativeFinalSurvival)
    {
        var segments = new List<FamilyExpectedFilteringCostSegment>();
        void Add(string name, FamilyAnalyticalOperation operation, double work, double reach)
        {
            if (!double.IsFinite(work) || work < 0d) throw new InvalidOperationException("RExpectedCostInvalidWork:" + name);
            if (!double.IsFinite(reach) || reach is < 0d or > 1d) throw new InvalidOperationException("RExpectedCostInvalidReach:" + name);
            if (work > 0d) segments.Add(new FamilyExpectedFilteringCostSegment(name, operation, work, reach));
        }

        if (plan.AlwaysReject)
        {
            Add("R.AlwaysRejectGuard", FamilyAnalyticalOperation.PredicateProbe, 1d, 1d);
            return Complete(fullCost, segments, 0d, authoritativeFinalSurvival, "AlwaysRejectGuardOnly");
        }

        RelicSequenceSearchCondition[] ordinary = evaluation.RelicSequenceConditions
            .Where(condition => !condition.IsEmpty).ToArray();
        RelicShopSequenceSearchCondition[] typedShop = evaluation.RelicShopSequenceConditions
            .Where(condition => !condition.IsEmpty).ToArray();
        if (ordinary.Length != plan.Predicates.Length || typedShop.Length != plan.ShopPredicates.Length)
            throw new InvalidOperationException("RExpectedCostConditionPlanCountMismatch");

        Add("R.InputPrefix.CandidateReconstruction", FamilyAnalyticalOperation.CandidateReconstruction, 1d, 1d);
        Add("R.InputPrefix.RngInitialization", FamilyAnalyticalOperation.RngInitialization, 1d, 1d);

        double cumulativeReach = 1d;
        bool playerBucketSeen = false;
        for (int bucket = 0; bucket < plan.Pool.BucketCount; bucket++)
        {
            bool shared = plan.Pool.BucketScopes[bucket] == 0;
            if (shared && playerBucketSeen)
                throw new InvalidOperationException("RExpectedCostSharedBucketAfterPlayerBucket");
            if (!shared && bucket > plan.LastRequiredBucket) break;

            int length = Math.Max(0, plan.Pool.BucketLengths[bucket]);
            int fullDraws = Math.Max(0, length - 1);
            if (shared)
            {
                Add($"R.SharedBucket[{bucket}].Progression", FamilyAnalyticalOperation.BucketProgression, 1d, cumulativeReach);
                Add($"R.SharedBucket[{bucket}].Shuffle", FamilyAnalyticalOperation.HistoricalNextInt, fullDraws, cumulativeReach);
                continue;
            }

            playerBucketSeen = true;
            RelicSequenceKind? lane = LaneFromKind(plan.Pool.BucketKinds[bucket]);
            int laneIndex = lane.HasValue ? (int)lane.Value : -1;
            int positiveDepth = laneIndex is >= 0 and < 4 ? plan.PositiveDepthByLane[laneIndex] : 0;
            int exclusionDepth = laneIndex is >= 0 and < 4 ? plan.ExclusionDepthByLane[laneIndex] : 0;
            int targetDepth = Math.Max(positiveDepth, exclusionDepth);

            Add($"R.PlayerBucket[{bucket}].Progression", FamilyAnalyticalOperation.BucketProgression, 1d, cumulativeReach);
            if (!lane.HasValue || targetDepth == 0)
            {
                Add($"R.PlayerBucket[{bucket}].InactiveShuffle", FamilyAnalyticalOperation.HistoricalNextInt, fullDraws, cumulativeReach);
                continue;
            }

            if (!authority.LanePools.TryGetValue(lane.Value, out ModelKey[]? lanePool))
                throw new InvalidOperationException("RExpectedCostLanePoolMissing:" + lane.Value);

            if (lane.Value != RelicSequenceKind.Shop)
            {
                Add($"R.PlayerBucket[{bucket}].ActiveShuffle", FamilyAnalyticalOperation.HistoricalNextInt, fullDraws, cumulativeReach);
                int tracked = plan.TrackedCountsByLane[laneIndex];
                Add($"R.PlayerBucket[{bucket}].TrackedLoad", FamilyAnalyticalOperation.TrackedPositionLoad, tracked, cumulativeReach);
                Add($"R.PlayerBucket[{bucket}].TrackedUpdates", FamilyAnalyticalOperation.TrackedPositionUpdateAttempt,
                    (double)fullDraws * tracked, cumulativeReach);
                double laneSurvival = AddOrdinaryPredicateSegments(
                    Add, bucket, lane.Value, lanePool, ordinary, plan, cumulativeReach,
                    shopLookupCost: false, out _);
                cumulativeReach *= laneSurvival;
                continue;
            }

            int eligible = Enumerable.Range(plan.Pool.BucketOffsets[bucket], length)
                .Count(index => (plan.Pool.EntryFlags[index] & 1) != 0);
            double partialDraws = RelicFamilyAnalyticalCost.ExpectedShopDraws(length, eligible, targetDepth);
            Add("R.Shop.PartialPermutation.NextInt", FamilyAnalyticalOperation.HistoricalNextInt, partialDraws, cumulativeReach);
            Add("R.Shop.PartialPermutation.LocalState", FamilyAnalyticalOperation.LocalPermutationStep,
                length + partialDraws, cumulativeReach);

            double ordinaryShopSurvival = AddOrdinaryPredicateSegments(
                Add, bucket, RelicSequenceKind.Shop, lanePool, ordinary, plan, cumulativeReach,
                shopLookupCost: true, out List<FiniteSequenceProbabilitySolver.Constraint> shopConstraints);
            double typedShopSurvival = AddTypedShopSegments(
                Add, lanePool, typedShop, targetDepth, cumulativeReach, ordinaryShopSurvival, shopConstraints);
            double shopSurvival = typedShopSurvival;
            if (bucket < plan.LastRequiredBucket)
                Add("R.Shop.TailShuffle", FamilyAnalyticalOperation.HistoricalNextInt,
                    Math.Max(0d, fullDraws - partialDraws), cumulativeReach * shopSurvival);
            cumulativeReach *= shopSurvival;
        }

        double finalSurvival = authoritativeFinalSurvival ?? cumulativeReach;
        if (authoritativeFinalSurvival.HasValue && !NearlyEqual(authoritativeFinalSurvival.Value, cumulativeReach))
            throw new InvalidOperationException(
                $"RExpectedCostFinalSurvivalMismatch:authority={F(authoritativeFinalSurvival.Value)};segments={F(cumulativeReach)}");
        Add("R.OutputCompaction", FamilyAnalyticalOperation.OutputCompaction, fullCost.WorkUnitsPerOutput, finalSurvival);
        return Complete(fullCost, segments, cumulativeReach, authoritativeFinalSurvival,
            "RelicFiniteSequencePrefixReach;DistinctLanePolicy=AcceptedModel;RuntimeSurvivalUsed=false");
    }

    private static double AddOrdinaryPredicateSegments(
        Action<string, FamilyAnalyticalOperation, double, double> add,
        int bucket,
        RelicSequenceKind lane,
        ModelKey[] pool,
        RelicSequenceSearchCondition[] ordinary,
        RelicFamilyPlan plan,
        double absoluteBucketReach,
        bool shopLookupCost,
        out List<FiniteSequenceProbabilitySolver.Constraint> accepted)
    {
        accepted = new List<FiniteSequenceProbabilitySolver.Constraint>();
        var poolSet = new HashSet<ModelKey>(pool, ModelKeyComparer.Instance);
        for (int row = 0; row < ordinary.Length; row++)
        {
            double rowReach = Solve(pool, accepted);
            add($"R.PlayerBucket[{bucket}].PredicateRow[{row}]", FamilyAnalyticalOperation.PredicateProbe,
                1d, absoluteBucketReach * rowReach);
            RelicSequenceSearchCondition condition = ordinary[row];
            if (condition.Lane != lane) continue;

            ModelKey[] any = condition.Keys.Any.Where(poolSet.Contains).ToArray();
            ModelKey[] all = condition.Keys.All.Where(poolSet.Contains).ToArray();
            ModelKey[] ban = condition.Keys.Ban.Where(poolSet.Contains).ToArray();
            RelicFamilyPredicate physical = plan.Predicates[row];
            if (physical.AnyCount != any.Length || physical.AllCount != all.Length || physical.BanCount != ban.Length)
                throw new InvalidOperationException("RExpectedCostPredicateTargetCountMismatch:" + row);

            double targetCost = shopLookupCost && condition.RangeMode == SearchSequenceRangeMode.FirstN
                ? Math.Max(1, condition.RangeValue)
                : 1d;
            foreach ((ModelKey _, int target) in any.Select((key, index) => (key, index)))
                add($"R.PlayerBucket[{bucket}].Predicate[{row}].Any[{target}]",
                    FamilyAnalyticalOperation.PredicateProbe, targetCost, absoluteBucketReach * rowReach);

            var allPrefix = new List<ModelKey>();
            for (int target = 0; target < all.Length; target++)
            {
                double reach = PrefixProbability(pool, accepted, condition, any, allPrefix, []);
                add($"R.PlayerBucket[{bucket}].Predicate[{row}].All[{target}]",
                    FamilyAnalyticalOperation.PredicateProbe, targetCost, absoluteBucketReach * reach);
                allPrefix.Add(all[target]);
            }

            var banPrefix = new List<ModelKey>();
            for (int target = 0; target < ban.Length; target++)
            {
                double reach = PrefixProbability(pool, accepted, condition, any, all, banPrefix);
                add($"R.PlayerBucket[{bucket}].Predicate[{row}].Ban[{target}]",
                    FamilyAnalyticalOperation.PredicateProbe, targetCost, absoluteBucketReach * reach);
                banPrefix.Add(ban[target]);
            }

            accepted.Add(Constraint(condition, any, all, ban));
        }
        return Solve(pool, accepted);
    }

    private static double AddTypedShopSegments(
        Action<string, FamilyAnalyticalOperation, double, double> add,
        ModelKey[] pool,
        RelicShopSequenceSearchCondition[] typedShop,
        int targetDepth,
        double absoluteBucketReach,
        double ordinaryShopSurvival,
        List<FiniteSequenceProbabilitySolver.Constraint> accepted)
    {
        bool impossible = false;
        for (int row = 0; row < typedShop.Length; row++)
        {
            RelicShopSequenceSearchCondition condition = typedShop[row];
            ModelKey?[] slots = condition.Slots.Take(condition.Count).ToArray();
            for (int target = 0; target < slots.Length; target++)
            {
                double prefixReach = impossible ? 0d : Solve(pool, accepted);
                add($"R.Shop.TypedSequence[{row}].TargetRow[{target}]",
                    FamilyAnalyticalOperation.PredicateProbe, 1d, absoluteBucketReach * prefixReach);
                if (slots[target] is not ModelKey key) continue;

                double targetWork = condition.OrderMode == CombatRewardSequenceOrderMode.Ordered
                    ? 1d
                    : slots.Length + Math.Max(targetDepth, condition.Count);
                add($"R.Shop.TypedSequence[{row}].TargetProbe[{target}]",
                    FamilyAnalyticalOperation.PredicateProbe, targetWork, absoluteBucketReach * prefixReach);

                if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered)
                {
                    accepted.Add(new FiniteSequenceProbabilitySolver.Constraint(
                        SearchSequenceRangeMode.ExactSlot,
                        target + 1,
                        new ModelKeySetFilter([], [key], [])));
                }
                else
                {
                    int requested = slots.Count(item => item is ModelKey value && value == key);
                    if (requested > 1)
                    {
                        impossible = true;
                    }
                    else
                    {
                        accepted.Add(new FiniteSequenceProbabilitySolver.Constraint(
                            SearchSequenceRangeMode.FirstN,
                            condition.Count,
                            new ModelKeySetFilter([], [key], [])));
                    }
                }
            }
        }
        double jointSurvival = impossible ? 0d : Solve(pool, accepted);
        if (ordinaryShopSurvival == 0d && jointSurvival != 0d)
            throw new InvalidOperationException("RExpectedCostShopPrefixSurvivalExpanded");
        return jointSurvival;
    }

    private static double PrefixProbability(
        ModelKey[] pool,
        IReadOnlyList<FiniteSequenceProbabilitySolver.Constraint> accepted,
        RelicSequenceSearchCondition condition,
        IReadOnlyList<ModelKey> any,
        IReadOnlyList<ModelKey> all,
        IReadOnlyList<ModelKey> ban)
    {
        var constraints = new List<FiniteSequenceProbabilitySolver.Constraint>(accepted)
        {
            Constraint(condition, any, all, ban)
        };
        return Solve(pool, constraints);
    }

    private static FiniteSequenceProbabilitySolver.Constraint Constraint(
        RelicSequenceSearchCondition condition,
        IReadOnlyList<ModelKey> any,
        IReadOnlyList<ModelKey> all,
        IReadOnlyList<ModelKey> ban) => new(
        condition.RangeMode,
        condition.RangeValue,
        new ModelKeySetFilter(any.ToArray(), all.ToArray(), ban.ToArray()));

    private static double Solve(
        ModelKey[] pool,
        IReadOnlyList<FiniteSequenceProbabilitySolver.Constraint> constraints)
    {
        if (!FiniteSequenceProbabilitySolver.TrySolve(
                pool.Select(key => new FiniteSequenceProbabilitySolver.Item(key)).ToArray(),
                constraints,
                out FiniteSequenceProbabilitySolver.Result result,
                out string issue))
            throw new InvalidOperationException("RExpectedCostPrefixProbabilityUnavailable:" + issue);
        return result.Probability;
    }

    private static FamilyExpectedFilteringCostProjection Complete(
        FamilyAnalyticalCostProjection fullCost,
        IReadOnlyList<FamilyExpectedFilteringCostSegment> segments,
        double calculatedFinalSurvival,
        double? authoritativeFinalSurvival,
        string evidence)
    {
        double segmentedFull = segments.Sum(segment => segment.FullWorkUnits);
        double flatFull = fullCost.WorkUnitsPerInput + fullCost.WorkUnitsPerOutput;
        if (!NearlyEqual(segmentedFull, flatFull))
            throw new InvalidOperationException(
                $"RExpectedCostFullSegmentationMismatch:flat={F(flatFull)};segments={F(segmentedFull)}");
        double expected = segments.Sum(segment => segment.ExpectedContribution);
        return new FamilyExpectedFilteringCostProjection(
            "R.Relic",
            flatFull,
            expected,
            authoritativeFinalSurvival ?? calculatedFinalSurvival,
            fullCost.CompactInputWorkUnitsPerInput,
            segments,
            evidence);
    }

    private static FamilyExpectedFilteringCostProjection Unresolved(
        FamilyAnalyticalCostProjection fullCost,
        double? finalSurvival,
        string evidence) => new(
        "R.Relic",
        fullCost.WorkUnitsPerInput + fullCost.WorkUnitsPerOutput,
        null,
        finalSurvival,
        fullCost.CompactInputWorkUnitsPerInput,
        [],
        evidence);

    private static RelicSequenceKind? LaneFromKind(byte kind) => kind switch
    {
        1 => RelicSequenceKind.Common,
        2 => RelicSequenceKind.Uncommon,
        3 => RelicSequenceKind.Rare,
        4 => RelicSequenceKind.Shop,
        _ => null
    };

    private static bool NearlyEqual(double left, double right)
    {
        double scale = Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= 1e-11 * scale;
    }

    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
