using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;

const int PoolSize = 8;
ModelKey[][] pools = Enumerable.Range(0, 4)
    .Select(lane => Enumerable.Range(0, PoolSize)
        .Select(index => new ModelKey("RELIC", $"L{lane}_{index}"))
        .ToArray())
    .ToArray();
var authority = new RelicLaneProbabilityAuthority(new Dictionary<RelicSequenceKind, ModelKey[]>
{
    [RelicSequenceKind.Common] = pools[0],
    [RelicSequenceKind.Uncommon] = pools[1],
    [RelicSequenceKind.Rare] = pools[2],
    [RelicSequenceKind.Shop] = pools[3]
});

Fixture alwaysReject = Project([], [], alwaysReject: true);
RequireClose(alwaysReject.Expected.FullFilteringWorkUnitsPerInput, 1d, "AlwaysReject full guard cost");
RequireClose(alwaysReject.Expected.ExpectedFilteringWorkUnitsPerInput!.Value, 1d, "AlwaysReject expected guard cost");
RequireClose(alwaysReject.Expected.FinalSurvival!.Value, 0d, "AlwaysReject survival");
RequireClose(alwaysReject.Expected.CompactInputWorkUnitsPerInput, 0d, "AlwaysReject compact ordinal must not execute");
Require(alwaysReject.Expected.Segments.Count == 1 && alwaysReject.Expected.Segments[0].Name == "R.AlwaysRejectGuard",
    "AlwaysReject must expose only its real guard segment.");

RelicSequenceSearchCondition earlyStrict = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, pools[0][0]);
RelicSequenceSearchCondition commonAlways = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, PoolSize, pools[0][0]);
RelicSequenceSearchCondition uncommonAlways = AllAt(RelicSequenceKind.Uncommon, SearchSequenceRangeMode.FirstN, PoolSize, pools[1][0]);
RelicSequenceSearchCondition lateAlways = AllAt(RelicSequenceKind.Rare, SearchSequenceRangeMode.FirstN, PoolSize, pools[2][0]);
RelicSequenceSearchCondition lateStrict = AllAt(RelicSequenceKind.Rare, SearchSequenceRangeMode.ExactSlot, 1, pools[2][0]);
Fixture early = Project([earlyStrict, uncommonAlways, lateAlways], []);
Fixture late = Project([commonAlways, uncommonAlways, lateStrict], []);
RequireClose(early.Expected.FullFilteringWorkUnitsPerInput, late.Expected.FullFilteringWorkUnitsPerInput,
    "early/late fixtures preserve the same full structural work");
RequireClose(early.Expected.FinalSurvival!.Value, 1d / PoolSize, "early final survival");
RequireClose(late.Expected.FinalSurvival!.Value, 1d / PoolSize, "late final survival");
Require(early.Expected.ExpectedFilteringWorkUnitsPerInput!.Value < late.Expected.ExpectedFilteringWorkUnitsPerInput!.Value,
    "An early strict condition must avoid enough later work to cost less than an equally selective late condition.");
Require(early.Expected.ExpectedFilteringWorkUnitsPerInput!.Value < early.Expected.FullFilteringWorkUnitsPerInput * 0.75d,
    "Early rejection must materially reduce expected work below full work.");

RelicSequenceSearchCondition commonA = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, pools[0][0]);
RelicSequenceSearchCondition overlapA = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, pools[0][0]);
RelicSequenceSearchCondition distinctB = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, pools[0][1]);
RelicSequenceSearchCondition commonAlwaysA = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, PoolSize, pools[0][0]);
RelicSequenceSearchCondition commonAlwaysB = AllAt(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, PoolSize, pools[0][1]);
Fixture overlap = Project([commonA, overlapA, commonAlwaysB, lateAlways], []);
Fixture distinct = Project([commonA, distinctB, commonAlwaysA, lateAlways], []);
RequireClose(overlap.Expected.FullFilteringWorkUnitsPerInput, distinct.Expected.FullFilteringWorkUnitsPerInput,
    "same structural shape full cost");
RequireClose(overlap.Expected.FinalSurvival!.Value, 1d / PoolSize, "overlap target survival");
RequireClose(distinct.Expected.FinalSurvival!.Value, 1d / PoolSize / (PoolSize - 1), "distinct target survival");
Require(overlap.Expected.ExpectedFilteringWorkUnitsPerInput!.Value > distinct.Expected.ExpectedFilteringWorkUnitsPerInput!.Value,
    "Target relations may change downstream reach while preserving structural full cost.");

var anyAllBan = new RelicSequenceSearchCondition(
    RelicSequenceKind.Common,
    SearchSequenceRangeMode.FirstN,
    5,
    new ModelKeySetFilter([pools[0][0], pools[0][1]], [pools[0][2], pools[0][3]], [pools[0][4], pools[0][5]]));
Fixture prefix = Project([anyAllBan], []);
double[] anyReach = Reaches(prefix, ".Any[");
double[] allReach = Reaches(prefix, ".All[");
double[] banReach = Reaches(prefix, ".Ban[");
Require(anyReach.Length == 2 && allReach.Length == 2 && banReach.Length == 2, "Any/All/Ban segment coverage");
RequireClose(anyReach[0], anyReach[1], "Any probes do not receive first-hit discount");
Require(allReach[1] < allReach[0], "All later target is reached only after earlier All target passes");
Require(banReach[1] < banReach[0], "Ban later target is reached only after earlier Ban target is absent");

RelicSequenceSearchCondition shopOrdinary = AllAt(
    RelicSequenceKind.Shop, SearchSequenceRangeMode.FirstN, 2, pools[3][0]);
Fixture ordinaryShop = Project([shopOrdinary], []);
Require(ordinaryShop.Expected.IsResolved && ordinaryShop.Expected.Segments.Any(segment => segment.Name == "R.Shop.PartialPermutation.LocalState"),
    "ordinary Shop must include partial permutation and resolve expected cost");
RequireClose(ordinaryShop.Expected.FinalSurvival!.Value, 2d / PoolSize, "ordinary Shop FirstN survival");
Require(ordinaryShop.Expected.ExpectedFilteringWorkUnitsPerInput!.Value < ordinaryShop.Expected.FullFilteringWorkUnitsPerInput,
    "ordinary Shop rejection must discount output and later conditional work");

var typedOnlyCondition = new RelicShopSequenceSearchCondition(
    2, CombatRewardSequenceOrderMode.Ordered, [null, pools[3][1]]);
Fixture typedOnly = Project([], [typedOnlyCondition]);
RequireClose(typedOnly.Expected.FinalSurvival!.Value, 1d / PoolSize, "typed Shop positional survival");
Require(typedOnly.Expected.Segments.Any(segment => segment.Name.Contains("TypedSequence", StringComparison.Ordinal)),
    "typed Shop prefix segments missing");
Require(typedOnly.Expected.ExpectedFilteringWorkUnitsPerInput!.Value < typedOnly.Expected.FullFilteringWorkUnitsPerInput,
    "typed Shop rejection must reduce expected work");

var mixedTyped = new RelicShopSequenceSearchCondition(
    2, CombatRewardSequenceOrderMode.Ordered, [null, pools[3][1]]);
Fixture mixed = Project([shopOrdinary], [mixedTyped]);
Require(mixed.Expected.IsResolved, "mixed ordinary+typed Shop must resolve");
RequireClose(mixed.Expected.FinalSurvival!.Value, 1d / PoolSize / (PoolSize - 1), "mixed Shop joint survival");
Require(Math.Abs(mixed.Expected.FinalSurvival.Value - (2d / PoolSize) * (1d / PoolSize)) > 1e-6,
    "mixed Shop must not use marginal independence multiplication");
var mixedAuthorityConstraints = new List<FiniteSequenceProbabilitySolver.Constraint>
{
    new(shopOrdinary.RangeMode, shopOrdinary.RangeValue, shopOrdinary.Keys)
};
Require(SearchSelectivityEstimator.TryAppendTypedShopConstraints(
        mixedTyped, mixedAuthorityConstraints, out string mixedAuthorityIssue),
    "mixed Shop authority conversion failed: " + mixedAuthorityIssue);
Require(FiniteSequenceProbabilitySolver.TrySolve(
        pools[3].Select(key => new FiniteSequenceProbabilitySolver.Item(key)).ToArray(),
        mixedAuthorityConstraints,
        out FiniteSequenceProbabilitySolver.Result mixedAuthorityResult,
        out string mixedSolveIssue),
    "mixed Shop authority solve failed: " + mixedSolveIssue);
RequireClose(mixedAuthorityResult.Probability, 1d / PoolSize / (PoolSize - 1),
    "production mixed Shop probability helper must use one joint permutation");

Print("AlwaysReject", alwaysReject.Expected);
Print("EarlyStrict", early.Expected);
Print("LateStrict", late.Expected);
Print("SameShapeDistinctTargets", distinct.Expected);
Print("MixedShop", mixed.Expected);
Console.WriteLine("rFamilyExpectedFilteringCostTests=PASS");
return;

Fixture Project(
    IReadOnlyList<RelicSequenceSearchCondition> ordinary,
    IReadOnlyList<RelicShopSequenceSearchCondition> typed,
    bool alwaysReject = false)
{
    ExactSearchEvaluationProjection evaluation = ExactSearchEvaluationProjection.Empty with
    {
        RelicSequenceConditions = ordinary,
        RelicShopSequenceConditions = typed
    };
    RelicFamilyPlan plan = BuildPlan(ordinary, typed, alwaysReject);
    FamilyAnalyticalCostProjection full = RelicFamilyAnalyticalCost.Project(plan);
    FamilyExpectedFilteringCostProjection expected =
        RelicExpectedFilteringCost.ProjectForTesting(evaluation, plan, full, authority);
    Require(expected.IsResolved, "projection unexpectedly unresolved: " + expected.Evidence);
    return new Fixture(full, expected);
}

RelicFamilyPlan BuildPlan(
    IReadOnlyList<RelicSequenceSearchCondition> ordinary,
    IReadOnlyList<RelicShopSequenceSearchCondition> typed,
    bool alwaysReject)
{
    int[] lengths = [5, PoolSize, PoolSize, PoolSize, PoolSize];
    int[] offsets = new int[lengths.Length];
    for (int index = 1; index < offsets.Length; index++) offsets[index] = offsets[index - 1] + lengths[index - 1];
    int total = lengths.Sum();
    ushort[] denseIds = Enumerable.Range(0, total).Select(index => checked((ushort)index)).ToArray();
    byte[] flags = new byte[total];
    for (int index = offsets[4]; index < offsets[4] + lengths[4]; index++) flags[index] = 1;
    var pool = new RelicFamilyPool(
        denseIds,
        flags,
        offsets,
        lengths,
        [0, 1, 1, 1, 1],
        [0, 1, 2, 3, 4],
        [0, 1, 1, 1, 2],
        PoolSize);

    var predicateIds = new List<ushort>();
    var predicates = new List<RelicFamilyPredicate>();
    byte[] positive = new byte[4];
    byte[] exclusion = new byte[4];
    foreach (RelicSequenceSearchCondition condition in ordinary)
    {
        int lane = (int)condition.Lane;
        int anyOffset = predicateIds.Count;
        predicateIds.AddRange(condition.Keys.Any.Select((_, index) => checked((ushort)index)));
        int allOffset = predicateIds.Count;
        predicateIds.AddRange(condition.Keys.All.Select((_, index) => checked((ushort)index)));
        int banOffset = predicateIds.Count;
        predicateIds.AddRange(condition.Keys.Ban.Select((_, index) => checked((ushort)index)));
        if (condition.Keys.Any.Count + condition.Keys.All.Count > 0)
            positive[lane] = Math.Max(positive[lane], checked((byte)condition.RangeValue));
        if (condition.Keys.Ban.Count > 0)
            exclusion[lane] = Math.Max(exclusion[lane], checked((byte)condition.RangeValue));
        predicates.Add(new RelicFamilyPredicate(
            checked((byte)lane),
            condition.RangeMode == SearchSequenceRangeMode.FirstN ? (byte)0 : (byte)1,
            checked((byte)condition.RangeValue),
            anyOffset, checked((ushort)condition.Keys.Any.Count),
            allOffset, checked((ushort)condition.Keys.All.Count),
            banOffset, checked((ushort)condition.Keys.Ban.Count)));
    }

    var shopPredicates = new List<RelicFamilyShopPredicate>();
    foreach (RelicShopSequenceSearchCondition condition in typed)
    {
        positive[3] = Math.Max(positive[3], checked((byte)condition.Count));
        shopPredicates.Add(new RelicFamilyShopPredicate(
            checked((byte)condition.Count),
            condition.OrderMode == CombatRewardSequenceOrderMode.Ordered ? (byte)0 : (byte)1,
            condition.Slots.Take(condition.Count)
                .Select((key, index) => key.HasValue ? checked((ushort)index) : ushort.MaxValue)
                .ToArray()));
    }

    int[] trackedCounts = new int[4];
    for (int lane = 0; lane < 3; lane++)
        trackedCounts[lane] = ordinary.Where(condition => (int)condition.Lane == lane)
            .SelectMany(condition => condition.Keys.Any.Concat(condition.Keys.All).Concat(condition.Keys.Ban))
            .Distinct(ModelKeyComparer.Instance).Count();
    byte[] tracked = trackedCounts.Select(value => checked((byte)value)).ToArray();
    int[] trackedOffsets = new int[4];
    for (int lane = 1; lane < 4; lane++) trackedOffsets[lane] = trackedOffsets[lane - 1] + tracked[lane - 1];
    ushort[] initialPositions = new ushort[tracked.Sum(value => value)];
    int lastLane = Enumerable.Range(0, 4).Where(lane => positive[lane] > 0 || exclusion[lane] > 0)
        .DefaultIfEmpty(-1).Max();

    return new RelicFamilyPlan(
        pool,
        predicates.ToArray(),
        predicateIds.ToArray(),
        shopPredicates.ToArray(),
        positive,
        exclusion,
        initialPositions,
        trackedOffsets,
        tracked,
        lastLane < 0 ? 0 : lastLane + 1,
        alwaysReject);
}

static RelicSequenceSearchCondition AllAt(
    RelicSequenceKind lane,
    SearchSequenceRangeMode range,
    int value,
    ModelKey target) => new(lane, range, value, new ModelKeySetFilter([], [target], []));

static double[] Reaches(Fixture fixture, string marker) => fixture.Expected.Segments
    .Where(segment => segment.Name.Contains(marker, StringComparison.Ordinal))
    .Select(segment => segment.ReachProbability)
    .ToArray();

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireClose(double actual, double expected, string message)
{
    if (Math.Abs(actual - expected) > 1e-10 * Math.Max(1d, Math.Abs(expected)))
        throw new InvalidOperationException($"{message}: expected={expected:G17};actual={actual:G17}");
}

static void Print(string name, FamilyExpectedFilteringCostProjection projection) => Console.WriteLine(
    $"fixture={name};full={projection.FullFilteringWorkUnitsPerInput:G17};" +
    $"expected={projection.ExpectedFilteringWorkUnitsPerInput!.Value:G17};" +
    $"survival={projection.FinalSurvival:G17};ratio={projection.ExpectedToFullRatio:G17}");

internal sealed record Fixture(
    FamilyAnalyticalCostProjection Full,
    FamilyExpectedFilteringCostProjection Expected);
