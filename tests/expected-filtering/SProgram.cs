using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

ModelKey[] uncommon = Enumerable.Range(0, 32)
    .Select(index => new ModelKey("CARD", $"U_{index:D2}"))
    .ToArray();
ModelKey[] rare = [new ModelKey("CARD", "R_ONLY")];
var authority = new Beta111MerchantColorlessAuthority(
    uncommon,
    rare,
    OrderedPoolExact: true,
    PoolIdentityHooksExact: true,
    RarityIdentityHooksExact: true,
    SuccessfulInitialInventoryShapeExact: true,
    RuntimeProfileId.Beta111,
    PlayerSlotIndex: 0,
    PlayersCount: 1,
    AuthorityFingerprint: "s-expected-cost-fixture");

var earlyStrict = new MerchantColorlessSlotCondition(1, MerchantColorlessSlot.Uncommon, uncommon[0]);
var lateStrict = new MerchantColorlessSlotCondition(5, MerchantColorlessSlot.Uncommon, uncommon[0]);
var earlyAlways = new MerchantColorlessSlotCondition(1, MerchantColorlessSlot.Rare, rare[0]);
var lateAlways = new MerchantColorlessSlotCondition(5, MerchantColorlessSlot.Rare, rare[0]);

Fixture early = Project([earlyStrict, lateAlways], []);
Fixture late = Project([earlyAlways, lateStrict], []);
RequireClose(early.Expected.FullFilteringWorkUnitsPerInput, late.Expected.FullFilteringWorkUnitsPerInput,
    "early/late fixtures must have the same full structural work");
RequireClose(early.Expected.FinalSurvival!.Value, 1d / uncommon.Length, "early final survival");
RequireClose(late.Expected.FinalSurvival!.Value, 1d / uncommon.Length, "late final survival");
Require(early.Expected.ExpectedFilteringWorkUnitsPerInput!.Value <
        late.Expected.ExpectedFilteringWorkUnitsPerInput!.Value * 0.5d,
    "Merchant 1 rejection must save later Merchant replay work");
Require(late.Expected.ExpectedToFullRatio!.Value > 0.95d,
    "late rejection must retain almost all full filtering work");

RequireClose(Reach(early, "S.MerchantReplay[1].RngAdvance"), 1d,
    "Merchant 1 replay is fully paid before its slot can reject");
RequireClose(Reach(early, "S.MerchantReplay[2].RngAdvance"), 1d / uncommon.Length,
    "Merchant 2 is reached only after the Merchant 1 slot passes");
RequireClose(Reach(early, "S.Merchant[1].SlotIdentityProbe[0]"), 1d,
    "slot mismatch is tested only after Merchant 1 replay");
double earlyPlanRowScans = early.Expected.Segments
    .Where(segment => segment.Name.Contains("MaximumOrdinal", StringComparison.Ordinal) ||
                      segment.Name.Contains("PlanRowScan", StringComparison.Ordinal))
    .Sum(segment => segment.FullWorkUnits);
RequireClose(earlyPlanRowScans, 2d * (5d + 1d),
    "full cost must include one maximum-ordinal scan and repeated row scans at every reached Merchant");
Require(early.Full.Evidence.Contains("PlanRowScans=12", StringComparison.Ordinal),
    "flat S ledger must expose the repeated plan-row scan count");

var ordered = new MerchantColorlessSequenceSearchCondition(
    5,
    CombatRewardSequenceOrderMode.Ordered,
    MerchantColorlessSlot.Uncommon,
    [uncommon[0], null, uncommon[1], null, null]);
Fixture orderedFixture = Project([], [ordered]);
RequireClose(orderedFixture.Expected.FinalSurvival!.Value, 1d / uncommon.Length / uncommon.Length,
    "ordered concrete slots use replacement-draw positional probability");
for (int merchant = 1; merchant <= 5; merchant++)
    RequireClose(Reach(orderedFixture, $"S.MerchantReplay[{merchant}].RngAdvance"), 1d,
        "ordered sequence cannot reject before its declared horizon");
RequireClose(Reach(orderedFixture, "S.Merchant[5].OrderedTargetProbe[0,0]"), 1d,
    "first ordered target reach");
RequireClose(Reach(orderedFixture, "S.Merchant[5].OrderedTargetProbe[0,2]"), 1d / uncommon.Length,
    "later ordered target uses concrete-prefix reach");
Require(!orderedFixture.Expected.Segments.Any(segment => segment.Name.Contains("OrderedTargetProbe[0,1]", StringComparison.Ordinal)),
    "ordered wildcard must not add an identity-comparison segment");

var unordered = new MerchantColorlessSequenceSearchCondition(
    3,
    CombatRewardSequenceOrderMode.Unordered,
    MerchantColorlessSlot.Uncommon,
    [null, uncommon[0], uncommon[1]]);
Fixture unorderedFixture = Project([], [unordered]);
for (int merchant = 1; merchant <= 3; merchant++)
    RequireClose(Reach(unorderedFixture, $"S.MerchantReplay[{merchant}].RngAdvance"), 1d,
        "unordered sequence cannot reject before its declared horizon");
double firstTargetPass = 1d - Math.Pow((uncommon.Length - 1d) / uncommon.Length, 3d);
RequireClose(Reach(unorderedFixture, "S.Merchant[3].UnorderedMultiplicityProbe[0,2]"), firstTargetPass,
    "later unordered target uses prior multiplicity joint reach");
double marginalProduct = firstTargetPass * firstTargetPass;
Require(Math.Abs(unorderedFixture.Expected.FinalSurvival!.Value - marginalProduct) > 1e-6,
    "unordered final probability must come from the joint DP, not marginal multiplication");

Fixture reused = Project(
    [new MerchantColorlessSlotCondition(2, MerchantColorlessSlot.Uncommon, uncommon[0])],
    [new MerchantColorlessSequenceSearchCondition(
        3, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon,
        [null, null, uncommon[1]])]);
double replayAdvances = reused.Expected.Segments
    .Where(segment => segment.Operation == FamilyAnalyticalOperation.RngAdvance)
    .Sum(segment => segment.FullWorkUnits);
RequireClose(replayAdvances, 26d * 3d,
    "multiple conditions must reuse one Merchant observation replay");

Fixture empty = Project([], []);
RequireClose(empty.Expected.FullFilteringWorkUnitsPerInput, 4d, "empty full cost");
RequireClose(empty.Expected.ExpectedFilteringWorkUnitsPerInput!.Value, 4d, "empty expected cost");
RequireClose(empty.Expected.FinalSurvival!.Value, 1d, "empty final survival");
Require(empty.Expected.Segments.Count == 2, "empty shape needs only reconstruction and output segments");

FamilyExpectedFilteringCostSegment output = orderedFixture.Expected.Segments.Single(
    segment => segment.Name == "S.OutputCompaction");
RequireClose(output.ReachProbability, orderedFixture.Expected.FinalSurvival.Value,
    "output compaction reach must equal final survival");
RequireClose(output.ExpectedContribution, 3d * orderedFixture.Expected.FinalSurvival.Value,
    "output compaction contribution must be survival weighted");

Print("Merchant1EarlyReject", early.Expected);
Print("Merchant5LateReject", late.Expected);
Print("OrderedHorizon5", orderedFixture.Expected);
Print("UnorderedHorizon3", unorderedFixture.Expected);
Print("MultipleConditionsReuseReplay", reused.Expected);
Print("Empty", empty.Expected);
Console.WriteLine("sFamilyExpectedFilteringCostTests=PASS");
return;

Fixture Project(
    IReadOnlyList<MerchantColorlessSlotCondition> slots,
    IReadOnlyList<MerchantColorlessSequenceSearchCondition> sequences)
{
    ExactSearchEvaluationProjection evaluation = ExactSearchEvaluationProjection.Empty with
    {
        MerchantColorlessConditions = slots,
        MerchantColorlessSequenceConditions = sequences
    };
    FamilyAnalyticalCostProjection full = MerchantShopColorlessAnalyticalCost.Project(evaluation);
    FamilyExpectedFilteringCostProjection expected =
        MerchantShopColorlessExpectedFilteringCost.ProjectForTesting(evaluation, full, authority);
    Require(expected.IsResolved, "projection unexpectedly unresolved: " + expected.Evidence);
    return new Fixture(full, expected);
}

static double Reach(Fixture fixture, string segmentName) => fixture.Expected.Segments
    .Single(segment => segment.Name == segmentName)
    .ReachProbability;

static void Print(string name, FamilyExpectedFilteringCostProjection projection) => Console.WriteLine(
    $"fixture={name};full={projection.FullFilteringWorkUnitsPerInput:G17};" +
    $"expected={projection.ExpectedFilteringWorkUnitsPerInput!.Value:G17};" +
    $"survival={projection.FinalSurvival!.Value:G17};ratio={projection.ExpectedToFullRatio!.Value:G17}");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireClose(double actual, double expected, string message)
{
    double scale = Math.Max(1d, Math.Max(Math.Abs(actual), Math.Abs(expected)));
    if (Math.Abs(actual - expected) > 1e-11 * scale)
        throw new InvalidOperationException($"{message}: expected={expected:G17};actual={actual:G17}");
}

internal sealed record Fixture(
    FamilyAnalyticalCostProjection Full,
    FamilyExpectedFilteringCostProjection Expected);
