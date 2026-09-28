using System.Globalization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using static RolltheSpire2.Search.FamilyExecution.FamilyAnalyticalWorkTerms;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Closed Operation Ledger classifications that can be projected before execution.
/// Runtime observations and hardware timing deliberately do not enter Family Cost.
/// </summary>
public enum FamilyAnalyticalWorkVariability
{
    Constant,
    Query,
    Path
}

/// <summary>
/// Stable, source-level operations whose counts distinguish current Family algorithms.
/// Every operation has equal normalized weight; this is not a GPU instruction model.
/// </summary>
public enum FamilyAnalyticalOperation
{
    InvocationBoundary,
    CandidateReconstruction,
    CompactInputOrdinal,
    RngInitialization,
    RngAdvance,
    HistoricalNextInt,
    ObservationWrite,
    BucketProgression,
    TrackedPositionLoad,
    TrackedPositionUpdateAttempt,
    LocalPermutationStep,
    PredicateProbe,
    OutputCompaction
}

public readonly record struct FamilyAnalyticalWorkTerm
{
    public FamilyAnalyticalWorkTerm(
        FamilyAnalyticalOperation operation,
        FamilyAnalyticalWorkVariability variability,
        double fixedPerInvocation,
        double perInput,
        double perCompactInput,
        double perOutput,
        string evidence)
    {
        Validate(fixedPerInvocation, nameof(fixedPerInvocation));
        Validate(perInput, nameof(perInput));
        Validate(perCompactInput, nameof(perCompactInput));
        Validate(perOutput, nameof(perOutput));
        Operation = operation;
        Variability = variability;
        FixedPerInvocation = fixedPerInvocation;
        PerInput = perInput;
        PerCompactInput = perCompactInput;
        PerOutput = perOutput;
        Evidence = string.IsNullOrWhiteSpace(evidence) ? operation.ToString() : evidence.Trim();
    }

    public FamilyAnalyticalOperation Operation { get; }
    public FamilyAnalyticalWorkVariability Variability { get; }
    public double FixedPerInvocation { get; }
    public double PerInput { get; }
    public double PerCompactInput { get; }
    public double PerOutput { get; }
    public string Evidence { get; }

    private static void Validate(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0d) throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct FamilyAnalyticalWorkEstimate(
    string FamilyId,
    double InputPopulation,
    double OutputPopulation,
    double ExpectedPhysicalInvocationCount,
    bool CompactInput,
    double FixedWorkUnits,
    double InputWorkUnits,
    double OutputWorkUnits)
{
    public double TotalWorkUnits => FixedWorkUnits + InputWorkUnits + OutputWorkUnits;
}

/// <summary>
/// Query-resolved analytical work for one Family. WorkUnits are source-counted,
/// equal-weight operation units and never milliseconds or observed device service.
/// </summary>
public sealed class FamilyAnalyticalCostProjection
{
    public FamilyAnalyticalCostProjection(
        string familyId,
        int inputCapacityPerInvocation,
        IEnumerable<FamilyAnalyticalWorkTerm> terms,
        string evidence)
    {
        if (string.IsNullOrWhiteSpace(familyId)) throw new ArgumentException("Family id is required.", nameof(familyId));
        if (inputCapacityPerInvocation <= 0) throw new ArgumentOutOfRangeException(nameof(inputCapacityPerInvocation));
        ArgumentNullException.ThrowIfNull(terms);
        FamilyId = familyId.Trim();
        InputCapacityPerInvocation = inputCapacityPerInvocation;
        Terms = terms.ToArray();
        Evidence = evidence?.Trim() ?? string.Empty;
    }

    public string FamilyId { get; }
    public int InputCapacityPerInvocation { get; }
    public IReadOnlyList<FamilyAnalyticalWorkTerm> Terms { get; }
    public string Evidence { get; }
    public double FixedWorkUnitsPerInvocation => Terms.Sum(term => term.FixedPerInvocation);
    public double WorkUnitsPerInput => Terms.Sum(term => term.PerInput);
    public double CompactInputWorkUnitsPerInput => Terms.Sum(term => term.PerCompactInput);
    public double WorkUnitsPerOutput => Terms.Sum(term => term.PerOutput);

    public FamilyAnalyticalWorkEstimate Estimate(
        double inputPopulation,
        double survivalRate,
        bool compactInput,
        double? expectedPhysicalInvocationCount = null)
    {
        if (!double.IsFinite(inputPopulation) || inputPopulation < 0d)
            throw new ArgumentOutOfRangeException(nameof(inputPopulation));
        if (!double.IsFinite(survivalRate) || survivalRate is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(survivalRate));
        if (expectedPhysicalInvocationCount is double suppliedInvocationCount &&
            (!double.IsFinite(suppliedInvocationCount) || suppliedInvocationCount < 0d))
            throw new ArgumentOutOfRangeException(nameof(expectedPhysicalInvocationCount));

        double deterministicInvocationCount = inputPopulation <= 0d
            ? 0d
            : Math.Ceiling(inputPopulation / InputCapacityPerInvocation);
        double physicalInvocationCount = expectedPhysicalInvocationCount ?? deterministicInvocationCount;
        if (inputPopulation <= 0d && physicalInvocationCount > 0d)
            throw new ArgumentException(
                "An empty expected input population cannot produce a physical invocation.",
                nameof(expectedPhysicalInvocationCount));
        double outputPopulation = inputPopulation * survivalRate;
        double fixedWork = physicalInvocationCount * FixedWorkUnitsPerInvocation;
        double inputRate = WorkUnitsPerInput + (compactInput ? CompactInputWorkUnitsPerInput : 0d);
        return new FamilyAnalyticalWorkEstimate(
            FamilyId,
            inputPopulation,
            outputPopulation,
            physicalInvocationCount,
            compactInput,
            fixedWork,
            inputPopulation * inputRate,
            outputPopulation * WorkUnitsPerOutput);
    }

    internal string FormatSummary() => string.Format(
        CultureInfo.InvariantCulture,
        "family={0};fixedPerInvocation={1:G17};workUnitsPerInput={2:G17};" +
        "compactInputIncrement={3:G17};workUnitsPerOutput={4:G17};batchCapacity={5};evidence={6}",
        FamilyId,
        FixedWorkUnitsPerInvocation,
        WorkUnitsPerInput,
        CompactInputWorkUnitsPerInput,
        WorkUnitsPerOutput,
        InputCapacityPerInvocation,
        Evidence.Replace(';', ','));
}

public readonly record struct FamilyExpectedWorkStage(
    string FamilyId,
    double SurvivalRate,
    FamilyAnalyticalWorkEstimate Work);

public sealed record FamilyExpectedPlanWork(
    double StartingPopulation,
    double FinalPopulation,
    double TotalWorkUnits,
    IReadOnlyList<FamilyExpectedWorkStage> Stages);

/// <summary>
/// Folds one explicit Family order using externally supplied Survival. It compares
/// no alternatives and makes no execution or Planner decision. An empty order is legal.
/// </summary>
public static class FamilyExpectedWorkFold
{
    public static FamilyExpectedPlanWork Fold(
        double startingPopulation,
        IReadOnlyList<FamilyAnalyticalCostProjection> orderedFamilies,
        IReadOnlyList<FamilySurvivalProjection> survivalProjections)
    {
        ArgumentNullException.ThrowIfNull(orderedFamilies);
        ArgumentNullException.ThrowIfNull(survivalProjections);
        if (orderedFamilies.Count != survivalProjections.Count)
            throw new ArgumentException("Every Family requires one Survival projection.");

        var survivalRates = new double[survivalProjections.Count];
        for (int index = 0; index < survivalProjections.Count; index++)
        {
            FamilyAnalyticalCostProjection cost = orderedFamilies[index] ??
                throw new ArgumentException("Family Cost projection cannot be null.", nameof(orderedFamilies));
            FamilySurvivalProjection survival = survivalProjections[index] ??
                throw new ArgumentException("Family Survival projection cannot be null.", nameof(survivalProjections));
            if (!string.Equals(cost.FamilyId, survival.FamilyId, StringComparison.Ordinal))
                throw new ArgumentException("Family Cost and Survival projection order does not match.");
            survivalRates[index] = survival.SurvivalProbability ??
                throw new InvalidOperationException($"Family Survival is unresolved: {survival.FamilyId}:{survival.Evidence}");
        }
        return Fold(startingPopulation, orderedFamilies, survivalRates);
    }

    public static FamilyExpectedPlanWork Fold(
        double startingPopulation,
        IReadOnlyList<FamilyAnalyticalCostProjection> orderedFamilies,
        IReadOnlyList<double> survivalRates)
    {
        if (!double.IsFinite(startingPopulation) || startingPopulation < 0d)
            throw new ArgumentOutOfRangeException(nameof(startingPopulation));
        ArgumentNullException.ThrowIfNull(orderedFamilies);
        ArgumentNullException.ThrowIfNull(survivalRates);
        if (orderedFamilies.Count != survivalRates.Count)
            throw new ArgumentException("Every Family requires one externally owned Survival rate.");
        if (orderedFamilies.Count == 0)
            return new FamilyExpectedPlanWork(startingPopulation, startingPopulation, 0d, []);

        int fragmentCapacity = orderedFamilies.Min(family => family?.InputCapacityPerInvocation ??
            throw new ArgumentException("Family projection cannot be null.", nameof(orderedFamilies)));
        double population = startingPopulation;
        double totalWork = 0d;
        double cumulativeInputSurvival = 1d;
        var stages = new FamilyExpectedWorkStage[orderedFamilies.Count];
        for (int index = 0; index < orderedFamilies.Count; index++)
        {
            FamilyAnalyticalCostProjection family = orderedFamilies[index] ??
                throw new ArgumentException("Family projection cannot be null.", nameof(orderedFamilies));
            double survival = survivalRates[index];
            double expectedPhysicalInvocationCount = ExpectedNonEmptyFragmentCount(
                startingPopulation, fragmentCapacity, cumulativeInputSurvival);
            FamilyAnalyticalWorkEstimate work = family.Estimate(
                population,
                survival,
                compactInput: index > 0,
                expectedPhysicalInvocationCount: expectedPhysicalInvocationCount);
            stages[index] = new FamilyExpectedWorkStage(family.FamilyId, survival, work);
            totalWork += work.TotalWorkUnits;
            population = work.OutputPopulation;
            cumulativeInputSurvival *= survival;
        }

        return new FamilyExpectedPlanWork(startingPopulation, population, totalWork, stages);
    }

    private static double ExpectedNonEmptyFragmentCount(
        double rootPopulation,
        int fragmentCapacity,
        double cumulativeInputSurvival)
    {
        if (rootPopulation <= 0d || cumulativeInputSurvival <= 0d) return 0d;

        double fullFragmentCount = Math.Floor(rootPopulation / fragmentCapacity);
        double remainderPopulation = rootPopulation - fullFragmentCount * fragmentCapacity;
        double expected = fullFragmentCount * SearchPredictabilityMath.ProbabilityAtLeastOne(
            cumulativeInputSurvival, fragmentCapacity);
        if (remainderPopulation > 0d)
            expected += SearchPredictabilityMath.ProbabilityAtLeastOne(
                cumulativeInputSurvival, remainderPopulation);

        double fragmentCount = fullFragmentCount + (remainderPopulation > 0d ? 1d : 0d);
        return Math.Clamp(expected, 0d, fragmentCount);
    }
}

internal static class MerchantShopColorlessAnalyticalCost
{
    internal static FamilyAnalyticalCostProjection Project(ExactSearchEvaluationProjection evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        MerchantColorlessSlotCondition[] slots = evaluation.MerchantColorlessConditions.ToArray();
        MerchantColorlessSequenceSearchCondition[] sequences =
            evaluation.MerchantColorlessSequenceConditions.ToArray();
        int maximumOrdinal = slots.Select(condition => condition.MerchantOrdinal)
            .Concat(sequences.Select(condition => condition.Count))
            .DefaultIfEmpty(0).Max();

        double planRowScans = (slots.Length + sequences.Length) * (maximumOrdinal + 1d);
        double predicateBudget = planRowScans + slots.Length;
        foreach (MerchantColorlessSequenceSearchCondition condition in sequences)
        {
            int targetCount = condition.Slots.Count;
            int concreteTargets = condition.Slots.Count(target => target.HasValue);
            predicateBudget += condition.OrderMode == CombatRewardSequenceOrderMode.Ordered
                ? concreteTargets
                : concreteTargets * (targetCount + condition.Count);
        }

        FamilyAnalyticalWorkTerm[] terms =
        [
            Fixed(FamilyAnalyticalOperation.InvocationBoundary, 4d,
                "Dispatch+Submit+Sync+HeaderReadback"),
            Input(FamilyAnalyticalOperation.CandidateReconstruction, 1d,
                FamilyAnalyticalWorkVariability.Constant, "VisibleSeed+RootHash"),
            Compact(FamilyAnalyticalOperation.CompactInputOrdinal, 2d,
                "ABI1 ordinal uses two uint words in S"),
            Input(FamilyAnalyticalOperation.RngInitialization, maximumOrdinal > 0 ? 1d : 0d,
                FamilyAnalyticalWorkVariability.Query, "Named shops stream"),
            Input(FamilyAnalyticalOperation.RngAdvance, 26d * maximumOrdinal,
                FamilyAnalyticalWorkVariability.Query, "26 consumed NextUInt64 draws per Merchant"),
            Input(FamilyAnalyticalOperation.HistoricalNextInt, 2d * maximumOrdinal,
                FamilyAnalyticalWorkVariability.Query, "Two approved integer-limb range reductions per Merchant"),
            Input(FamilyAnalyticalOperation.ObservationWrite, 2d * maximumOrdinal,
                FamilyAnalyticalWorkVariability.Query, "Uncommon+Rare observations per Merchant"),
            Input(FamilyAnalyticalOperation.PredicateProbe, predicateBudget,
                FamilyAnalyticalWorkVariability.Query, "Historical Merchant predicate probe budget"),
            Output(FamilyAnalyticalOperation.OutputCompaction, 3d,
                "Atomic append+two uint ABI1 writes")
        ];
        return new FamilyAnalyticalCostProjection(
            "S.MerchantShopColorless",
            MerchantShopColorlessGpuExecutor.Capacity,
            terms,
            $"FamilyOperationLedgerV1;Algorithm=MerchantReplay;MaximumMerchantOrdinal={maximumOrdinal};" +
            $"SlotPredicates={slots.Length};SequencePredicates={sequences.Length};" +
            $"PlanRowScans={F(planRowScans)};NextInt=HistoricalShortcut");
    }

    private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
}

internal static class RelicFamilyAnalyticalCost
{
    internal static FamilyAnalyticalCostProjection Project(RelicFamilyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var terms = new List<FamilyAnalyticalWorkTerm>
        {
            Fixed(FamilyAnalyticalOperation.InvocationBoundary, 4d,
                "Dispatch+Submit+Sync+HeaderReadback")
        };
        if (plan.AlwaysReject)
        {
            terms.Add(Input(FamilyAnalyticalOperation.PredicateProbe, 1d,
                FamilyAnalyticalWorkVariability.Query, "AlwaysReject guard"));
            return new FamilyAnalyticalCostProjection(
                "R.Relic", RelicFamilyGpuExecutor.Capacity, terms,
                "FamilyOperationLedgerV1;Algorithm=TrackedPositionsStreaming;AlwaysReject=true;NextInt=HistoricalShortcut");
        }

        terms.Add(Compact(FamilyAnalyticalOperation.CompactInputOrdinal, 1d,
            "R physical compact input is one batch-local uint ordinal"));
        terms.Add(Input(FamilyAnalyticalOperation.CandidateReconstruction, 1d,
            FamilyAnalyticalWorkVariability.Constant, "VisibleSeed+RootHash"));
        terms.Add(Input(FamilyAnalyticalOperation.RngInitialization, 1d,
            FamilyAnalyticalWorkVariability.Constant, "up_front stream"));

        double bucketProgression = 0d;
        double nextInt = 0d;
        double trackedLoads = 0d;
        double trackedUpdates = 0d;
        double localPermutation = 0d;
        double predicateBudget = 0d;
        for (int bucket = 0; bucket < plan.Pool.BucketCount; bucket++)
        {
            bool shared = plan.Pool.BucketScopes[bucket] == 0;
            if (!shared && bucket > plan.LastRequiredBucket) break;
            bucketProgression++;
            int length = Math.Max(0, plan.Pool.BucketLengths[bucket]);
            int fullDraws = Math.Max(0, length - 1);
            if (shared)
            {
                nextInt += fullDraws;
                continue;
            }

            int lane = LaneFromKind(plan.Pool.BucketKinds[bucket]);
            int positiveDepth = lane is >= 0 and < 4 ? plan.PositiveDepthByLane[lane] : 0;
            int exclusionDepth = lane is >= 0 and < 4 ? plan.ExclusionDepthByLane[lane] : 0;
            int targetDepth = Math.Max(positiveDepth, exclusionDepth);
            if (lane < 0 || targetDepth == 0)
            {
                nextInt += fullDraws;
                continue;
            }

            if (lane == 3)
            {
                int eligible = Enumerable.Range(plan.Pool.BucketOffsets[bucket], length)
                    .Count(index => (plan.Pool.EntryFlags[index] & 1) != 0);
                double expectedObservedDraws = ExpectedShopDraws(length, eligible, targetDepth);
                double paidDraws = bucket < plan.LastRequiredBucket ? fullDraws : expectedObservedDraws;
                nextInt += paidDraws;
                localPermutation += length + expectedObservedDraws;
                predicateBudget += ShopPredicateBudget(plan, targetDepth);
                continue;
            }

            int trackedCount = plan.TrackedCountsByLane[lane];
            nextInt += fullDraws;
            trackedLoads += trackedCount;
            trackedUpdates += (double)fullDraws * trackedCount;
            predicateBudget += plan.Predicates.Length;
            predicateBudget += plan.Predicates
                .Where(predicate => predicate.Lane == lane)
                .Sum(predicate => predicate.AnyCount + predicate.AllCount + predicate.BanCount);
        }

        terms.Add(Input(FamilyAnalyticalOperation.BucketProgression, bucketProgression,
            FamilyAnalyticalWorkVariability.Path, "Full query-resolved path through last required bucket"));
        terms.Add(Input(FamilyAnalyticalOperation.HistoricalNextInt, nextInt,
            FamilyAnalyticalWorkVariability.Path, "Approved integer-limb range reduction; no FP64/RNE"));
        terms.Add(Input(FamilyAnalyticalOperation.TrackedPositionLoad, trackedLoads,
            FamilyAnalyticalWorkVariability.Query, "Tracked initial positions only; no bag materialization"));
        terms.Add(Input(FamilyAnalyticalOperation.TrackedPositionUpdateAttempt, trackedUpdates,
            FamilyAnalyticalWorkVariability.Path, "One update attempt per tracked position per ordinary shuffle draw"));
        terms.Add(Input(FamilyAnalyticalOperation.LocalPermutationStep, localPermutation,
            FamilyAnalyticalWorkVariability.Path, "Shop-only bounded local state initialization+partial permutation"));
        terms.Add(Input(FamilyAnalyticalOperation.PredicateProbe, predicateBudget,
            FamilyAnalyticalWorkVariability.Path, "Current tracked-position/shop predicate probe budget"));
        terms.Add(Output(FamilyAnalyticalOperation.OutputCompaction, 2d,
            "Atomic append+one uint batch-local ordinal write"));

        return new FamilyAnalyticalCostProjection(
            "R.Relic",
            RelicFamilyGpuExecutor.Capacity,
            terms,
            "FamilyOperationLedgerV1;Algorithm=TrackedPositionsStreaming;" +
            $"LastRequiredBucket={plan.LastRequiredBucket};TrackedShape={string.Join("|", plan.TrackedCountsByLane)};" +
            $"OrdinaryPredicates={plan.Predicates.Length};ShopPredicates={plan.ShopPredicates.Length};" +
            $"NextInt=HistoricalShortcut;BucketSteps={F(bucketProgression)};ShuffleDraws={F(nextInt)};" +
            $"TrackedUpdateAttempts={F(trackedUpdates)};ShopLocalPermutation={F(localPermutation)}");
    }

    private static double ShopPredicateBudget(RelicFamilyPlan plan, int targetDepth)
    {
        double budget = plan.Predicates.Length;
        foreach (RelicFamilyPredicate predicate in plan.Predicates.Where(predicate => predicate.Lane == 3))
        {
            int targets = predicate.AnyCount + predicate.AllCount + predicate.BanCount;
            budget += targets * (predicate.RangeMode == 0 ? Math.Max(1, (int)predicate.RangeValue) : 1d);
        }
        foreach (RelicFamilyShopPredicate predicate in plan.ShopPredicates)
        {
            int targets = predicate.TargetIds.Length;
            int concrete = predicate.TargetIds.Count(target => target != ushort.MaxValue);
            budget += targets;
            budget += predicate.OrderMode == 0
                ? concrete
                : concrete * (targets + Math.Max(targetDepth, (int)predicate.Count));
        }
        return budget;
    }

    internal static double ExpectedShopDraws(int length, int eligible, int targetDepth)
    {
        if (length <= 1 || eligible <= 0 || targetDepth <= 0) return 0d;
        int required = Math.Min(targetDepth, eligible);
        return Math.Min(length - 1d, required * (length + 1d) / (eligible + 1d));
    }

    private static int LaneFromKind(byte kind) => kind is >= 1 and <= 4 ? kind - 1 : -1;
    private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
}

internal static class FamilyAnalyticalWorkTerms
{
    internal static FamilyAnalyticalWorkTerm Fixed(
        FamilyAnalyticalOperation operation,
        double units,
        string evidence) => new(operation, FamilyAnalyticalWorkVariability.Constant, units, 0d, 0d, 0d, evidence);

    internal static FamilyAnalyticalWorkTerm Input(
        FamilyAnalyticalOperation operation,
        double units,
        FamilyAnalyticalWorkVariability variability,
        string evidence) => new(operation, variability, 0d, units, 0d, 0d, evidence);

    internal static FamilyAnalyticalWorkTerm Compact(
        FamilyAnalyticalOperation operation,
        double units,
        string evidence) => new(operation, FamilyAnalyticalWorkVariability.Path, 0d, 0d, units, 0d, evidence);

    internal static FamilyAnalyticalWorkTerm Output(
        FamilyAnalyticalOperation operation,
        double units,
        string evidence) => new(operation, FamilyAnalyticalWorkVariability.Path, 0d, 0d, 0d, units, evidence);
}
