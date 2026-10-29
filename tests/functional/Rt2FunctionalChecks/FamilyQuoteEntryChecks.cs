using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;

// A few fixed real consumers, captured before H15 changes. This is an entry in
// the existing functional harness, not a new price matrix or performance run.
internal static class FamilyQuoteEntryChecks
{
    private const string BaselineRevision = "04ff2beb69742e19cc0f5e9b9c6ca600aece989b";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static int Run(string? baselinePath = null)
    {
        string path = Path.GetFullPath(baselinePath ?? Path.Combine("tests", "functional", "fixtures",
            "FAMILY_QUOTE_ENTRY_04FF2BEB.json"));
        if (!File.Exists(path)) throw new FileNotFoundException(
            "Quote entry parity requires the preserved main@04ff2beb fixture or --quote-baseline <path>.", path);
        var before = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string? beforeCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        var values = new SortedDictionary<string, object?>();
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidDataException("FamilyQuoteEntry: " + name); }
        try
        {
            using var asset = typeof(FamilyCanonicalMeasuredCost).Assembly.GetManifestResourceStream(FamilyCanonicalMeasuredCost.ResourceName)
                ?? throw new InvalidDataException("FamilyQuoteEntry: embedded canonical reference missing");
            var source = JsonSerializer.Deserialize<FamilyCanonicalCostReference>(asset)!;
            var curve = source.CompleteCallCurves!.Value.Deserialize<FamilyCompleteCallCurve[]>()!
                .Single(c => c.Key.ExecutionClass == "GPU" && !c.Key.CompactInput && c.TrainingCases.Contains("r43t-c-six-terminal"));
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", curve.Key.CpuIdentity);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(curve.Key.GpuIdentity, curve.Key.RenderingBackend);
            values["identity"] = new { curve.Key.CpuIdentity, curve.Key.GpuIdentity, curve.Key.RenderingBackend };
            var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
            SearchQuery Read(string file) => ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + file).Query;
            ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? actual = null, int workers = 4) =>
                ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, actual ?? context),
                    new("000000000000", 1 << 24, 30, workers)).Plan ?? throw new InvalidDataException("FamilyQuoteEntry: compile failed");
            var costs = new GpuCostSnapshot([]);
            void Quote(string name, IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry)
            {
                var computationGeometry = geometry with { ComputationOnly = true };
                var local = FamilyPhysicalQuote.Computation(family, geometry, costs);
                var full = family.QuotePhysicalWork(geometry);
                double survival = family.Survival.SurvivalProbability ?? 1;
                values[name] = new {
                    family.FamilyId, Coverage = family.Coverage.ToArray(),
                    Revision = family.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision,
                    geometry.CompactInput, geometry.PrivateInput, geometry.PrivateOutput, geometry.MeanInputPopulation,
                    Computation = Describe(local), Complete = Describe(full),
                    ComputationMs = local is null ? null : FamilyCoarsePricing.ComputationMilliseconds(local, computationGeometry),
                    CompleteMs = full is null ? null : FamilyCoarsePricing.PublicMilliseconds(full, geometry, survival)
                };
                if (local is not null) Check(local.PublicTransportClass != "CompleteCanonicalAbi1",
                    name + " complete GPU call cannot become computation");
            }
            void Plan(string name, ExactSearchExecutionRequest request, bool gpu)
            {
                var plan = FamilyExecutionCoordinator.Plan(request, gpu);
                var eta = FamilySearchEtaProjectorV1.Project(request, plan, liveExactMsPerAttempt: .001);
                values[name] = new {
                    Order = plan.ExecutionOrderUnits.Select(FamilyExecutionCoordinator.FamilyOrderKey).ToArray(),
                    Stages = plan.Stages.Select(s => new { s.Family.FamilyId, s.Coverage, s.CompactAbi1Input,
                        s.ExpectedInputFraction, s.Survival, s.PerInputTimeMs, s.Performance.EvidenceSource,
                        s.Performance.PhysicalImplementationRevision }).ToArray(),
                    plan.ExpectedFamilyPipelineMsPerRoot, plan.FilterRemainders,
                    Eta = new { eta.Status, eta.AcceptedResultProbability, eta.FixedScanRangeMs,
                        eta.TargetMeanMs, eta.TargetFamilyPipelineMs, eta.TargetExactTailMs,
                        eta.ExactTimingImplementationRevision, Stages = eta.TargetStages.Select(s => new {
                            s.FamilyId, s.CompactAbi1Input, s.InputPopulation, s.OutputPopulation,
                            s.SteadyStateMs, s.EvidenceSource, Source = Provenance(s.CostEvidence) }).ToArray() }
                };
            }

            var card = context.Authority.EffectAuthority!.CharacterRewardPool!.First(c => c.IsUnlockedInCapturedPool &&
                c.Rarity == EffectCardRarity.Common).CardKey;
            var measuredRequest = Request(SearchQuery.Empty with { CombatCardRewards = new(6,
                CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]) }, workers: curve.Key.ConfiguredWorkers);
            var combat = FamilyExecutionCoordinator.CreateRegisteredFamilies(measuredRequest, true).OfType<CombatRewardFamily>().Single();
            Quote("canonical-C", combat, new(false, false, 1 << 24));
            var historical = new FamilyCanonicalCostSnapshot(source.Rows, source.CompleteCallCurves);
            var currentKey = FamilyCanonicalCostShape.Describe(measuredRequest, combat, false, 1 << 24)!;
            Check(currentKey.PhysicalRevision == curve.Key.PhysicalRevision + GpuRngRotation.Revision &&
                FamilyCanonicalMeasuredCost.IsCanonicalQuote(historical.Quote(curve.Key with { RootWindow = 1 << 24 }, new(false, false, 1 << 24))) &&
                historical.Quote(currentKey, new(false, false, 1 << 24)) is null,
                "C historical finite reference remains available only for its actual measured body");
            var canonicalComplete = FamilyPhysicalQuote.CompleteCall(combat, new(false, false, 1 << 24), costs,
                combat.Survival.SurvivalProbability ?? 1, rootPopulation: 1 << 24);
            var canonicalRaw = combat.QuotePhysicalWork(new(false, false, 1 << 24))!;
            var combatGeometry = new FamilyPhysicalQuoteRequest(false, false, 1 << 24);
            var combatComputation = FamilyPhysicalQuote.Computation(combat, combatGeometry, costs)!;
            double combatComputeMs = FamilyCoarsePricing.ComputationMilliseconds(combatComputation, combatGeometry)!.Value;
            double combatTransferMs = FamilyCoarsePricing.HostTransferMilliseconds(combatComputation, combatGeometry,
                combat.Survival.SurvivalProbability ?? 1)!.Value;
            Check(!FamilyCanonicalMeasuredCost.IsCanonicalQuote(canonicalRaw) && FamilyCoarsePricing.IsEstimate(canonicalRaw) &&
                canonicalRaw.NanosecondsPerInput > 0 &&
                canonicalRaw.Evidence.Contains("NotCurrentBodyMeasurement", StringComparison.Ordinal) &&
                canonicalComplete is { Source: FamilyPerformanceEvidenceSource.Reference,
                    Origin: FamilyUnitCostOrigin.ComputationAndTransferEstimate } &&
                Math.Abs(canonicalComplete.Milliseconds - combatComputeMs - combatTransferMs) < 1e-9 &&
                Math.Abs(canonicalComplete.Milliseconds - FamilyCoarsePricing.PublicMilliseconds(canonicalRaw,
                    combatGeometry, combat.Survival.SurvivalProbability ?? 1)!.Value) < 1e-9,
                "rotated C retains finite coarse coefficients with computation and host transfer charged once");
            Plan("canonical-C-plan", measuredRequest, true);

            var shopRequest = Request(Read("pricing-hole-v1/S-mixed.json"));
            var shop = FamilyExecutionCoordinator.CreateRegisteredFamilies(shopRequest, true)
                .Single(f => f.FamilyId == "S.MerchantShopColorless");
            Quote("ordinary-S-GPU", shop, new(false, false, 1 << 24));
            var cpu = FamilyCpuExecution.ConfiguredRealizations(shop).OfType<FamilyCpuExecution>().Single();
            Quote("configured-S-CPU-Compact", cpu, new(true, false, cpu.WindowCapacity));
            Plan("ordinary-S-plan", shopRequest, true);
            Plan("configured-S-CPU-plan", shopRequest, false);
            var preview = WorkbenchProbabilityPreview.Compute(shopRequest.CompiledSearch, 30, 4, false);
            var previewGeometry = new FamilyPhysicalQuoteRequest(false, false, 1 << 20) { ComputationOnly = true };
            var previewLocal = FamilyPhysicalQuote.Computation(shop, previewGeometry, GpuCostCalibration.Capture())!;
            Check(preview.Issues.Count == 0 && preview.Costs[shop.FamilyId].Gpu == previewLocal.NanosecondsPerInput,
                "preview uses the same family computation source at its declared 1Mi population");
            values["preview-S"] = new { preview.Costs[shop.FamilyId].Cpu, preview.Costs[shop.FamilyId].Gpu,
                Source = Provenance(previewLocal.Evidence) };

            var synthetic = new GpuCostSnapshot([new("fixture", "GPU", "fixture", 1, 1000, 1, DateTimeOffset.UnixEpoch)], _ => 2);
            var baseLocal = FamilyPhysicalQuote.Computation(shop, new(false, false, 1 << 24), costs)!;
            var scaled = FamilyPhysicalQuote.Computation(shop, new(false, false, 1 << 24), synthetic)!;
            Check(Math.Abs(scaled.NanosecondsPerInput / baseLocal.NanosecondsPerInput - 2) < 1e-12,
                "in-memory frozen device ratio applies once to actual GPU body");
            Check(FamilyCoarsePricing.HostTransferMilliseconds(baseLocal, new(false, false, 1 << 24), .5) ==
                FamilyCoarsePricing.HostTransferMilliseconds(scaled, new(false, false, 1 << 24), .5),
                "device ratio cannot scale host transfer");
            Check(FamilyPhysicalQuote.CompleteCall(shop, new(false, true, 65536), costs, .5) is
                { Origin: FamilyUnitCostOrigin.ComputationAndTransferEstimate },
                "composed timing has typed provenance so reference-only tools can retain missing prices");
            values["calibration-S"] = Describe(scaled);

            const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
            var partyContext = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", statics)!
                .Invoke(null, [context, 2, null, null])!;
            var table = SearchQuery.Empty with { Players = partyContext.Party!.Players.Select(owner =>
                new PlayerOfferQuery(owner.PlayerSlotIndex, new([], ModernNeowIdentityPredictor.PredictModernCore(
                    "000000000000", owner, RolltheSpire2.Compatibility.Beta111Profile.Instance, true).RelicKeys, []))
                { Conditions = Read("pricing-hole-v1/S-mixed.json") }).ToArray() };
            var tableRequest = Request(table, partyContext);
            var tableUnits = FamilyExecutionCoordinator.CreateRegisteredFamilies(tableRequest, true);
            var personal = tableUnits.OfType<PartyFamily>().Single(f => f.FamilyId == "S.MerchantShopColorless");
            var neow = tableUnits.OfType<NeowFamily.PartyInitial>().Single();
            Quote("party-S", personal, new(false, false, 65536));
            Quote("party-N-Compact", neow, new(true, false, 65536));
            Check(personal.QuotePhysicalWork(new(false, false, 65536))!.Evidence.Contains("HistoricalWrapperCoefficients", StringComparison.Ordinal),
                "Party complete ETA genuinely exercises retained historical wrapper priority");
            Check(FamilyPhysicalQuote.Computation(neow, new(true, false, 65536), costs)!.Evidence.Contains(
                "SharedArrivalWitnessUnchanged", StringComparison.Ordinal), "N shared-arrival witness remains explicit");
            foreach (IFamilyInvocation unit in new IFamilyInvocation[] { personal, neow })
            {
                var geometry = new FamilyPhysicalQuoteRequest(unit == neow, false, 65536);
                double survival = unit.Survival.SurvivalProbability ?? 1;
                var retained = unit.QuotePhysicalWork(geometry)!;
                var complete = FamilyPhysicalQuote.CompleteCall(unit, geometry, costs, survival,
                    rootPopulation: 65536);
                Check(complete is { Source: FamilyPerformanceEvidenceSource.Reference,
                        Origin: FamilyUnitCostOrigin.FamilyCompleteReference } &&
                    Math.Abs(complete.Milliseconds - FamilyCoarsePricing.PublicMilliseconds(retained, geometry, survival)!.Value) < 1e-9 &&
                    Provenance(complete.Evidence).SequenceEqual(Provenance(retained.Evidence)),
                    unit.FamilyId + " complete entry preserves historical wrapper priority, provenance and milliseconds");
            }
            Plan("party-plan", tableRequest, true);

            var initial = Request(Read("family-pricing-matrix-v1/R-three-common.json") with { TreasureRelics = new(1, 2,
                TreasureRelicOrderMode.Ordered, [null, new ModelKey("RELIC", "JUZU_BRACELET")]) });
            var initialUnit = FamilyExecutionCoordinator.CreateRegisteredFamilies(initial, true)
                .OfType<InitialRoomsInvocation>().Single(f => f.FamilyId == "R.Relic");
            Check(initialUnit.Ordinary is not null, "InitialRooms fixture actually adds work to ordinary R");
            Quote("initial-R-Compact", initialUnit, new(true, false, 65536));
            Plan("initial-R-plan", initial, true);
            var variant = Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 },
                VariantOpeningPreparationChecks.CreateContext());
            var variantUnit = FamilyExecutionCoordinator.CreateRegisteredFamilies(variant, false).Single();
            Check(variantUnit.FamilyId == "V.VariantOpening", "V owns initial Unknown prefix");
            Quote("V-CPU", variantUnit, new(false, false, 65536));
            Plan("V-plan", variant, false);

            var map = Request(SearchQuery.Empty with { StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster,
                StandardMapComparison.AtLeast, 1)] });
            var mapUnit = FamilyExecutionCoordinator.CreateRegisteredFamilies(map, false).Single();
            Check(mapUnit.QuotePhysicalWork(new(false, false, 65536)) is null, "M remains deliberately unpriced");
            Quote("M-unpriced", mapUnit, new(false, false, 65536));
            Plan("M-plan", map, false);
            var manual = FamilyPlanner.InManualOrder([mapUnit, variantUnit], [mapUnit, variantUnit],
                ["V.VariantOpening", "M.StandardMap"]);
            Check(manual.ExecutionOrderUnits.Select(f => f.FamilyId).SequenceEqual(["V.VariantOpening", "M.StandardMap"]),
                "missing cost cannot block a real manual order");

            // Reuse the existing auxiliary-failure fixture, with no execution or
            // cache writes. Broader result publication remains in allocation checks.
            var fixture = (IFamilyInvocation)Activator.CreateInstance(typeof(FamilyAllocationPricingChecks)
                .GetNestedType("LifecycleFixture", BindingFlags.NonPublic)!, ["quote", "QuoteEntryAuxiliaryFailure"])!;
            var failedCost = FamilyPlanner.PlanSimple([fixture]);
            Check(failedCost.OrderedFamilies.Single() == fixture && failedCost.Stages.Single().PerInputTimeMs is null,
                "auxiliary quote failure retains executable unit with unknown price");

            var snapshot = JsonSerializer.SerializeToElement(new { BaselineRevision, Values = values }, Json);
            using var baseline = JsonDocument.Parse(File.ReadAllText(path));
            CheckRotationBaseline(baseline.RootElement, snapshot, Check);
            Compare(baseline.RootElement, snapshot, "$", Check);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", beforeCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(before.GpuIdentity, before.RenderingBackend);
        }
        Console.WriteLine("FamilyQuoteEntry: retained consumer parity, explicit rotation measurement separation and computation/complete-call boundaries passed.");
        return 0;
    }

    private static object? Describe(FamilyPhysicalQuote? q) => q is null ? null : new {
        q.Shape, q.NanosecondsPerInput, q.WindowCapacity, q.SetupMilliseconds, q.FixedWindowMilliseconds,
        q.PublicTransportClass, q.OutputElementBytes, q.OutputAlreadyOrdered, q.LocalCostSource,
        q.GpuCostRatio, q.LocalExecutorCostRatio, Source = Provenance(q.Evidence),
        CanonicalMeasurement = FamilyCanonicalMeasuredCost.IsCanonicalQuote(q), Estimate = FamilyCoarsePricing.IsEstimate(q)
    };

    // Capture provenance and quality rather than incidental consumer routing text.
    private static string[] Provenance(string evidence) => evidence.Split(';').Where(part =>
        part.StartsWith("Basis=", StringComparison.Ordinal) || part.StartsWith("CanonicalAsset", StringComparison.Ordinal) ||
        part.StartsWith("TrainingRowsSha256=", StringComparison.Ordinal) || part.StartsWith("PriceQuality=", StringComparison.Ordinal) ||
        part.StartsWith("ReferenceQuality=", StringComparison.Ordinal) || part.StartsWith("Provenance=", StringComparison.Ordinal) ||
        part.Contains("HistoricalWrapperCoefficients", StringComparison.Ordinal) ||
        part.Contains("SharedArrivalWitnessUnchanged", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal).ToArray();

    private static void CheckRotationBaseline(JsonElement expected, JsonElement actual, Action<bool, string> check)
    {
        var before = expected.GetProperty("Values");
        var after = actual.GetProperty("Values");
        var oldC = before.GetProperty("canonical-C");
        var currentC = after.GetProperty("canonical-C");
        var retained = oldC.GetProperty("Computation");
        var replacement = currentC.GetProperty("Complete");
        // Pin the replacement to independent pre-rotation computation fields;
        // keep the measured complete-call fixture and its actual body intact.
        foreach (var property in retained.EnumerateObject().Where(p => p.Name != "Source"))
            Compare(property.Value, replacement.GetProperty(property.Name), "C retained coarse coefficient." + property.Name, check);
        var oldEta = before.GetProperty("canonical-C-plan").GetProperty("Eta");
        var eta = after.GetProperty("canonical-C-plan").GetProperty("Eta");
        var stage = eta.GetProperty("Stages")[0];
        void Near(double left, double right, string name) => check(double.IsFinite(left) && double.IsFinite(right) &&
            Math.Abs(left - right) <= 1e-10 * Math.Max(1, Math.Abs(right)), name);
        double targetCall = eta.GetProperty("TargetFamilyPipelineMs").GetDouble();
        check(targetCall > 0, "C coarse target ETA stays finite and positive");
        Near(stage.GetProperty("SteadyStateMs").GetDouble(), targetCall, "C single-stage target timing remains consistent");
        Near(eta.GetProperty("TargetMeanMs").GetDouble(), targetCall + eta.GetProperty("TargetExactTailMs").GetDouble(), "C target retains its Exact tail");
        Near(eta.GetProperty("FixedScanRangeMs").GetDouble() - currentC.GetProperty("CompleteMs").GetDouble(),
            oldEta.GetProperty("FixedScanRangeMs").GetDouble() - oldC.GetProperty("CompleteMs").GetDouble(),
            "C full-scan ETA changes only its body reference and preserves the original Exact contribution");
        string[] source = stage.GetProperty("Source").EnumerateArray().Concat(replacement.GetProperty("Source").EnumerateArray())
            .Select(v => v.GetString()!).ToArray();
        check(stage.GetProperty("EvidenceSource").GetInt32() == (int)FamilyPerformanceEvidenceSource.Reference &&
            source.Contains("PriceQuality=Estimate", StringComparer.Ordinal) &&
            source.All(value => !value.StartsWith("CanonicalAsset", StringComparison.Ordinal) &&
                !value.StartsWith("TrainingRowsSha256=", StringComparison.Ordinal)),
            "C rotated ETA identifies a reference estimate instead of a newly measured body");
    }

    private static void Compare(JsonElement expected, JsonElement actual, string path, Action<bool, string> check)
    {
        check(expected.ValueKind == actual.ValueKind, path + " kind changed");
        // Only the C complete call lost a formerly applicable exact-body curve.
        // CheckRotationBaseline separately proves its retained coefficients,
        // composed timing and unchanged Exact contribution against the old file.
        if (path is "$.Values.canonical-C.Complete" or "$.Values.canonical-C.CompleteMs" or
            "$.Values.canonical-C-plan.Eta.FixedScanRangeMs" or "$.Values.canonical-C-plan.Eta.TargetMeanMs" or
            "$.Values.canonical-C-plan.Eta.TargetFamilyPipelineMs" or
            "$.Values.canonical-C-plan.Eta.Stages[0].SteadyStateMs" or
            "$.Values.canonical-C-plan.Eta.Stages[0].EvidenceSource" or
            "$.Values.canonical-C-plan.Eta.Stages[0].Source") return;
        if (path is "$.Values.M-unpriced.Revision" or "$.Values.M-plan.Stages[0].PhysicalImplementationRevision" &&
            expected.GetString() == "M.StandardMap.Cpu.Final.EndpointReach.20261028.v2.RootMode2.P4.B8192.Dense.CanonicalAbi1Ready" &&
            actual.GetString() == "M.StandardMap.Cpu.Final.EndpointReach.RouteSequence.20261028.v3.RootMode2.P4.B8192.Dense.CanonicalAbi1Ready") return;
        if (expected.ValueKind == JsonValueKind.String &&
            (path.EndsWith(".Revision", StringComparison.Ordinal) || path.EndsWith(".PhysicalImplementationRevision", StringComparison.Ordinal)) &&
            actual.GetString() == expected.GetString() + GpuRngRotation.Revision) return;
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var names = expected.EnumerateObject().Select(p => p.Name).ToArray();
            check(names.SequenceEqual(actual.EnumerateObject().Select(p => p.Name)), path + " fields changed");
            foreach (string name in names) Compare(expected.GetProperty(name), actual.GetProperty(name), path + "." + name, check);
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            check(expected.GetArrayLength() == actual.GetArrayLength(), path + " length changed");
            for (int i = 0; i < expected.GetArrayLength(); i++) Compare(expected[i], actual[i], path + "[" + i + "]", check);
        }
        else if (expected.ValueKind == JsonValueKind.Number)
        {
            double a = expected.GetDouble(), b = actual.GetDouble();
            check(Math.Abs(a - b) <= 1e-12 * Math.Max(1, Math.Abs(a)), path + $" value changed: {a:R} -> {b:R}");
        }
        else check(expected.GetRawText() == actual.GetRawText(), path + " value/source changed: " + expected + " -> " + actual);
    }
}
