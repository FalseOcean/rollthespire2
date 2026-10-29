using System.Text.Json;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;

internal static class FamilyComputationBoundaryChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidDataException("FamilyComputationBoundary:" + message);
        }
        var old = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? actual = null,
            SearchRunOptions? options = null) => ExactSearchExecutionRequestFactory.Compile(
                SearchCompiler.Compile(query, actual ?? context),
                options ?? new("000000000000", 1 << 24, 30, 16)).Plan
                ?? throw new InvalidDataException("BoundaryFixture.Compile");
        SearchQuery Read(string path) => ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/" + path).Query;
        var snapshot = new GpuCostSnapshot([]);
        try
        {
            var syntheticGeometry = new FamilyPhysicalQuoteRequest(false, false, 2048) { ComputationOnly = true };
            var numerical = new FamilyPhysicalQuote("Fixture", "NumericalDevice", 1, 1024, null, "fixture") {
                FixedWindowMilliseconds = 100 };
            Check(FamilyCoarsePricing.ComputationMilliseconds(numerical, syntheticGeometry) == .002048,
                "GPU call-window allowance is outside numerical/device c");
            Check(FamilyCoarsePricing.HostTransferMilliseconds(numerical, syntheticGeometry, .5) ==
                FamilyCoarsePricing.HostTransferMilliseconds(numerical with { GpuCostRatio = 4 }, syntheticGeometry, .5),
                "GPU device ratio cannot scale host readback/control time");
            Check(FamilyCoarsePricing.HostTransferMilliseconds(numerical, syntheticGeometry with { PrivateOutput = true }, .5) <
                FamilyCoarsePricing.HostTransferMilliseconds(numerical, syntheticGeometry, .5),
                "private intermediate output retains control but has no public payload charge");
            Check(FamilyCoarsePricing.ComputationMilliseconds(numerical with {
                PublicTransportClass = "CompleteCanonicalAbi1" }, syntheticGeometry) is null,
                "complete GPU wall time has no fabricated numerical decomposition");
            var coupledAnchor = GpuReferenceCostAtlas.Measurements.Single(m => m.Key.StartsWith("DirectCapsule|", StringComparison.Ordinal));
            var mixedCalibration = new GpuCostSnapshot([new("fixture", "GPU", coupledAnchor.Key,
                coupledAnchor.PeakThroughput / 2, 1000, 1000000, DateTimeOffset.UtcNow)]);
            Check(mixedCalibration.ConditionCount == 0 && mixedCalibration.GlobalRatio == 1,
                "whole coupled-call evidence cannot calibrate numerical device rates");
            // Real immutable complete-call evidence remains usable at its own
            // public boundary. Only its use as computation is excluded.
            using var stream = typeof(FamilyCanonicalMeasuredCost).Assembly.GetManifestResourceStream(
                FamilyCanonicalMeasuredCost.ResourceName)!;
            var asset = JsonSerializer.Deserialize<FamilyCanonicalCostReference>(stream)!;
            var curves = asset.CompleteCallCurves!.Value.Deserialize<FamilyCompleteCallCurve[]>()!;
            var curve = curves.Single(c => c.Key.ExecutionClass == "GPU" && !c.Key.CompactInput &&
                c.TrainingCases.Contains("r43t-c-six-terminal"));
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", curve.Key.CpuIdentity);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(curve.Key.GpuIdentity, curve.Key.RenderingBackend);
            var card = context.Authority.EffectAuthority!.CharacterRewardPool!
                .First(c => c.IsUnlockedInCapturedPool && c.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common).CardKey;
            var measuredRequest = Request(SearchQuery.Empty with { CombatCardRewards = new(6,
                CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]) },
                options: new("000000000000", 1 << 24, 30, curve.Key.ConfiguredWorkers));
            var measuredFamily = FamilyExecutionCoordinator.CreateRegisteredFamilies(measuredRequest)
                .OfType<CombatRewardFamily>().Single();
            var publicGeometry = new FamilyPhysicalQuoteRequest(false, false, 1 << 24);
            var publicQuote = measuredFamily.QuotePhysicalWork(publicGeometry);
            var original = curve.Points.Single(p => p.N == 1 << 24);
            var historical = new FamilyCanonicalCostSnapshot(asset.Rows, asset.CompleteCallCurves)
                .Quote(curve.Key with { RootWindow = 1 << 24 }, publicGeometry);
            Check(FamilyCanonicalMeasuredCost.IsCanonicalQuote(historical) &&
                Math.Abs(historical!.NanosecondsPerInput * (1 << 24) / 1e6 - original.CallMilliseconds) < 1e-9 &&
                !FamilyCanonicalMeasuredCost.IsCanonicalQuote(publicQuote) && FamilyCoarsePricing.IsEstimate(publicQuote) &&
                measuredFamily.ConditionPerformance.PhysicalImplementationRevision == curve.Key.PhysicalRevision + GpuRngRotation.Revision,
                "original complete-call knot retains its old identity while the rotated body uses an explicit estimate");
            var bodyGeometry = publicGeometry with { ComputationOnly = true };
            Check(FamilyCanonicalMeasuredCost.TryQuote(measuredFamily, bodyGeometry, 1 << 24) is null,
                "GPU complete-call measurement cannot answer a computation request");
            var body = measuredFamily.QuotePhysicalWork(bodyGeometry);
            Check(body is { NanosecondsPerInput: > 0 } && !FamilyCanonicalMeasuredCost.IsCanonicalQuote(body) &&
                body.PublicTransportClass != "CompleteCanonicalAbi1",
                "real family retains its numerical/device estimate after complete-call exclusion");
            var ranked = GpuCostSnapshot.ComputationReference(snapshot, measuredFamily, measuredFamily.ConditionPerformance, false);
            Check(body is not null && Math.Abs(1e9 / ranked.CurrentQueryReference - body.NanosecondsPerInput) < 1e-9,
                "actual ranking reference contains computation without public submit/readback/sort");
            var preview = WorkbenchProbabilityPreview.Compute(measuredRequest.CompiledSearch, 1,
                measuredRequest.WorkerCount, cpuOnly: false);
            var previewBody = measuredFamily.QuotePhysicalWork(new(false, false, 1 << 20) { ComputationOnly = true });
            Check(preview.Issues.Count == 0 && previewBody is not null &&
                preview.Costs[measuredFamily.FamilyId].Gpu == previewBody.NanosecondsPerInput,
                "actual preview family check rate uses computation rather than the public complete-call curve");

            var cases = new (string Family, SearchQuery Query)[] {
                ("N.Neow", Read("n-cost-v1/Leafy.json")),
                ("R.Relic", Read("pricing-hole-v1/R-seven-tracked.json")),
                ("R.Relic", Read("v1/cases/NR-small-capsule.json")),
                ("S.MerchantShopColorless", Read("pricing-hole-v1/S-mixed.json")),
                ("C.CombatReward", Read("pricing-hole-v1/C-two-targets.json")),
                ("E.EventResult", Read("pricing-hole-v1/E-three-operators.json")),
                ("A.AncientOption", Read("pricing-hole-v1/A-two-generators.json")),
                ("W.World", Read("pricing-hole-v1/W-event-mix.json")),
                ("T.TransformationAggregate", SearchQuery.Empty with {
                    OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
                    TransformationAggregate = new(TransformationOpening.LeafyPoultice, TransformationPickupOrder.Any,
                        false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, []) })
            };
            foreach (var item in cases)
            {
                var family = FamilyExecutionCoordinator.CreateRegisteredFamilies(Request(item.Query))
                    .Single(f => f.FamilyId == item.Family);
                foreach (bool compact in new[] { false, true })
                {
                    var geometry = new FamilyPhysicalQuoteRequest(compact, false, 1 << 24) { ComputationOnly = true };
                    var quote = family.QuotePhysicalWork(geometry);
                    Check(quote is { NanosecondsPerInput: > 0 } && quote.PublicTransportClass != "CompleteCanonicalAbi1" &&
                        !FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote),
                        item.Family + " ordinary GPU computation retains numerical/device work " + compact);
                    var performance = GpuCostSnapshot.ComputationReference(snapshot, family, family.ResolveConditionPerformance(compact), compact);
                    Check(quote is not null && Math.Abs(1e9 / performance.CurrentQueryReference - quote.NanosecondsPerInput) < 1e-9,
                        item.Family + " ranking excludes host output envelope " + compact);
                    var privateQuote = family.QuotePhysicalWork(geometry with { PrivateOutput = true });
                    Check(privateQuote is { NanosecondsPerInput: > 0 } &&
                        privateQuote.PublicTransportClass != "CompleteCanonicalAbi1",
                        item.Family + " device-output computation does not borrow public complete-call price " + compact);
                }
                var cpu = FamilyCpuExecution.ConfiguredRealizations(family).OfType<FamilyCpuExecution>().FirstOrDefault();
                if (cpu is not null)
                {
                    var full = cpu.Quote(publicGeometry, new HashSet<string>());
                    var computation = cpu.Quote(bodyGeometry, new HashSet<string>());
                    Check(full is not null && computation is not null &&
                        full.NanosecondsPerInput == computation.NanosecondsPerInput &&
                        computation.PublicTransportClass == "CpuOrderedAbi1",
                        item.Family + " CPU owns its canonical output work in both ranking and public quote");
                }
            }

            // Freeze the Owner's exact business condition. Optional captured
            // input repeats the real 09:37:09 query/authority; the portable
            // version uses the same premises and existing corpus authority.
            string? inputs = Environment.GetEnvironmentVariable("RT2_BOUNDARY_REGRESSION_INPUT_DIR");
            var realContext = inputs is null ? context : ProductionSearchReplay.Read<ProductionSearchReplayContext>(
                Path.Combine(inputs, "context.json")).Context;
            var realQuery = inputs is null ? LeafyNineQuery(context) : ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                Path.Combine(inputs, "workload-0b5fee80.json")).Query;
            Check(realQuery.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.LeafyPoultice &&
                realQuery.TransformationAggregate is { OpportunityCount: 9, MinimumRareCount: 9,
                    Predicate: TransformationAggregatePredicate.RareCountAtLeast } && realQuery.EventSequenceConstraints.Count == 5,
                "regression retains Leafy identity, nine Rare opportunities, and all five W requirements");
            var realRequest = Request(realQuery, realContext);
            string[] required = ["N.Neow", "T.TransformationAggregate",
                ..(realRequest.VariantOpening is { HasVariantRestrictions: true } ? new[] { VariantOpeningFamily.Id } : []), "W.World"];
            var automatic = FamilyExecutionCoordinator.Plan(realRequest, true);
            Check(automatic.ExecutionOrderUnits.SelectMany(f => f.Coverage).Order().SequenceEqual(
                required.Order()), "real automatic plan preserves N/T/W and the prepared necessary V gate");
            Console.WriteLine("FamilyComputationBoundary Leafy9 automatic=" +
                string.Join('>', automatic.ExecutionOrderUnits.Select(f => f.FamilyId)) + ";context=" +
                (inputs is null ? "CorpusV1AuthoredPremises" : "LoggedEventScenarioAuthority"));
            var independent = FamilyExecutionCoordinator.CreateRegisteredFamilies(realRequest);
            var natural = FamilyPlanner.PlanSimple(independent);
            foreach (var stage in natural.Stages)
            {
                Check(stage.PerInputTimeMs is > 0 && !stage.Performance.Evidence.Contains("TrainingRowsSha256=", StringComparison.Ordinal),
                    "real natural stage consumes honest computation evidence " + stage.Family.FamilyId);
                Console.WriteLine("FamilyComputationBoundary quote=" + stage.Family.FamilyId + ";ns=" +
                    stage.PerInputTimeMs * 1e6 + ";evidence=" + stage.Performance.Evidence);
            }
            var manualRequest = Request(realQuery, realContext, new("000000000000", 1 << 24, 30, 16) {
                ManualFamilyOrder = ["N.Neow", "T.TransformationAggregate", "W.World"] });
            var manual = FamilyExecutionCoordinator.Plan(manualRequest, true);
            Check(manual.ExecutionOrderUnits.Select(f => f.FamilyId).SequenceEqual(required),
                "manual NTW remains executable with V inheriting its former W duty position");
            Check(GreedyUnitOrder.Select(new[] { "missing", "priced" }, x => x == "priced" ? .1 : null,
                _ => .1, _ => null) == "missing", "missing price retains its obligation instead of becoming infinite cost");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", oldCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(old.GpuIdentity, old.RenderingBackend);
        }
        Console.WriteLine($"FamilyComputationBoundary: {checks} checks passed.");
        return checks;
    }

    private static SearchQuery LeafyNineQuery(SearchContext context)
    {
        using var source = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var targets = source.RootElement.GetProperty("Evidence")[0].GetProperty("Targets")
            .Deserialize<MorphicGroveTarget[]>(ProductionSearchReplay.JsonOptions)!
            .Select((target, i) => target with { InstanceId = "basic:" + i }).ToArray();
        var scenario = new MorphicGroveScenario(context.Authority,
            new("Authored initial-Basic event entry; occurrence independently required by W", true, false, null), targets);
        EventSequenceSearchCondition Event(int act, string id) => new(act, null, SearchSequenceRangeMode.FirstN, 3,
            new([new ModelKey("EVENT", id)], [], []));
        return SearchQuery.Empty with {
            OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
            TransformationAggregate = new(TransformationOpening.LeafyPoultice, TransformationPickupOrder.Any,
                true, true, true, TransformationAggregatePredicate.RareCountAtLeast, 9, []) {
                EventScenario = scenario, Symbiote = true, TrialNondescript = true },
            EventSequenceConstraints = [Event(1, "AROMA_OF_CHAOS"), Event(1, "MORPHIC_GROVE"),
                Event(1, "WHISPERING_HOLLOW"), Event(3, "SYMBIOTE"), Event(3, "TRIAL")]
        };
    }
}
