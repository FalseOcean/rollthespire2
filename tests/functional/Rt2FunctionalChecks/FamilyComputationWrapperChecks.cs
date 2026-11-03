using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static class FamilyComputationWrapperChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException("FamilyComputationWrapper: " + message);
        }
        bool Near(double first, double second) => Math.Abs(first - second) <= 1e-10 * Math.Max(1, Math.Abs(second));
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var priorDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string? priorCpuIdentity = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var party = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", statics)!
            .Invoke(null, [captured, 2, null, null])!;
        PreparedSearch Request(SearchQuery query, SearchContext source)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, source),
                new("000000000000", 65536, 16, 4));
            return result.Plan ?? throw new InvalidOperationException("FamilyComputationWrapper.Compile:" + result.Issue);
        }
        SearchQuery Read(string path) => ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/" + path).Query;
        FamilyPhysicalQuote Need(FilterBodyDescription family, FamilyPhysicalQuoteRequest geometry) =>
            family.QuotePhysicalWork(geometry) ?? throw new InvalidOperationException("FamilyComputationWrapper.Missing:" + family.FamilyId);
        double Computation(FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry) =>
            FamilyCoarsePricing.ComputationMilliseconds(quote, geometry) ??
                throw new InvalidOperationException("FamilyComputationWrapper.InadmissibleComputation:" + quote.Evidence);
        double Component(FilterBodyDescription family, FamilyPhysicalQuoteRequest geometry) =>
            Computation(FamilyCoarsePricing.LocalComputation(family, Need(family, geometry), geometry), geometry);
        double Public(FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry, double pass) =>
            FamilyCoarsePricing.PublicMilliseconds(quote, geometry, pass) ??
                throw new InvalidOperationException("FamilyComputationWrapper.InadmissiblePublic:" + quote.Evidence);
        void Boundary(FilterBodyDescription family, FamilyPhysicalQuote full, FamilyPhysicalQuote compute)
        {
            Check(full.PublicTransportClass == "CompleteCanonicalAbi1", family.FamilyId + " default retains complete public boundary");
            Check(compute.PublicTransportClass == (family.ConditionPerformance.UsesGpu ? "DeviceFamilyWork" : "CpuOrderedAbi1"),
                family.FamilyId + " computation labels its owned execution boundary");
            Check(compute.Evidence.Contains("ComputationOnly", StringComparison.Ordinal) &&
                !compute.Evidence.Contains("HistoricalWrapperCoefficients", StringComparison.Ordinal),
                family.FamilyId + " computation cannot borrow the legacy complete Party wrapper");
            Check(compute.LocalCostSource == "ComponentComposition", family.FamilyId + " composite protects already scaled GPU and unscaled CPU components");
            var scaledDevice = new GpuCostSnapshot([new("SyntheticDevice", "GPU", "SyntheticDeviceRate", 1, 1000, 1,
                DateTimeOffset.UnixEpoch)], _ => 2);
            var localized = scaledDevice.Local(family, compute, new(false, false, 65536) { ComputationOnly = true });
            Check(scaledDevice.GlobalRatio == 2 && localized.NanosecondsPerInput == compute.NanosecondsPerInput &&
                localized.FixedWindowMilliseconds == compute.FixedWindowMilliseconds,
                family.FamilyId + " a different GPU ratio cannot rescale the composed cost or CPU floor");
        }
        try
        {
            // Exercise estimates on an unmeasured device without constructing a GPU
            // or changing the numerical bodies. Canonical finite lookup is covered
            // independently; it cannot supply GPU computation for these wrappers.
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "WrapperBoundaryUnmeasuredCPU");
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("WrapperBoundaryUnmeasuredGPU", "vulkan");
            var shop = Read("pricing-hole-v1/S-mixed.json");
            var table = SearchQuery.Empty with { Players = party.Party!.Players.Select(owner =>
                new PlayerOfferQuery(owner.PlayerSlotIndex, new([], ModernNeowIdentityPredictor.PredictModernCore(
                    "000000000000", owner, RolltheSpire2.Compatibility.Beta111Profile.Instance, true).RelicKeys, []))
                { Conditions = shop }).ToArray() };
            var tableRequest = Request(table, party);
            foreach (bool gpu in new[] { false, true })
            {
                var units = FamilyExecutionCoordinator.CreateRegisteredFamilies(tableRequest, gpu);
                var personal = units.Single(f => f.FamilyId == "S.MerchantShopColorless");
                var neow = units.Single(f => f.FamilyId == "N.Neow");
                foreach (bool compact in new[] { false, true })
                {
                    var geometry = new FamilyPhysicalQuoteRequest(compact, false, 65536);
                    var computeGeometry = geometry with { ComputationOnly = true };
                    foreach (FilterBodyDescription unit in new FilterBodyDescription[] { personal, neow })
                    {
                        var full = Need(unit, geometry);
                        var compute = Need(unit, computeGeometry);
                        Boundary(unit, full, compute);
                        Check(Computation(compute, computeGeometry) > 0, unit.FamilyId + " computation retains positive owned work");
                        if (gpu)
                            Check(Public(full, geometry, unit.Survival.SurvivalProbability ?? 1) > Computation(compute, computeGeometry),
                                unit.FamilyId + " public transport remains outside family computation");
                        else
                            Check(Near(Public(full, geometry, unit.Survival.SurvivalProbability ?? 1), Computation(compute, computeGeometry)),
                                unit.FamilyId + " complete CPU canonical work remains owned");
                    }
                    if (!gpu)
                    {
                        Check(Computation(Need(neow, computeGeometry), computeGeometry) > .35,
                            "CPU N retains its owned wrapper window floor");
                        continue;
                    }
                    // Real child numerical quotes include device filtering/emission;
                    // sum those rather than reconstructing them from public-call deltas.
                    var ordered = ((PartyFilterStage[])
                        typeof(PartyFamily).GetMethod("OrderPersonalStages", members)!.Invoke(personal.PerformanceProjection!.Target,
                            [compact, new HashSet<string>(StringComparer.Ordinal)])!).Select(stage => stage.Body).ToArray();
                    bool privateEdges = (bool)typeof(PartyFamily).GetProperty("UsesPrivate", members)!.GetValue(personal.PerformanceProjection!.Target)!;
                    double expected = 0, reach = 1;
                    for (int index = 0; index < ordered.Length; index++)
                    {
                        var childGeometry = new FamilyPhysicalQuoteRequest(compact || index > 0,
                            privateEdges && index < ordered.Length - 1, 65536 * reach,
                            PrivateInput: privateEdges && (compact || index > 0)) { ComputationOnly = true };
                        var child = ordered[index];
                        expected += Component(child, childGeometry);
                        reach *= child.ResolveSurvival(new HashSet<string>()).SurvivalProbability ?? 1;
                    }
                    Check(Near(Computation(Need(personal, computeGeometry), computeGeometry), expected),
                        "Party S pays each actual child numerical/device output once at its own reached population");
                }
            }

            // N has a shared-arrival witness, so every stage keeps the existing
            // full population envelope. Check public, private-chain and fused modes.
            foreach (var mode in new[] { (false, (bool?)false), (true, (bool?)false), (true, (bool?)true) })
            {
                var neow = new NeowFamily.PartyInitial(tableRequest.Model, FilterExecutionPreferences.From(tableRequest.RunOptions), preferGpu: true, optimize: mode.Item1, fuse: mode.Item2);
                var stages = (List<NeowPartyStage>)
                    typeof(NeowFamily.PartyInitial).GetField("_gpuStages", members)!.GetValue(neow)!;
                Check(stages.Count > 1 && neow.ResolveConditionPerformance(false).UsesGpu, "N test has real multiple GPU stages");
                bool fused = (bool)typeof(NeowFamily.PartyInitial).GetProperty("UsesFused", members)!.GetValue(neow)!;
                bool privateEdges = fused || (bool)typeof(NeowFamily.PartyInitial).GetProperty("UsesPrivate", members)!.GetValue(neow)!;
                foreach (bool compact in new[] { false, true })
                {
                    var geometry = new FamilyPhysicalQuoteRequest(compact, false, 65536) { ComputationOnly = true };
                    double expected = 0;
                    for (int index = 0; index < stages.Count; index++)
                    {
                        bool stageCompact = compact || !fused && index > 0;
                        var childGeometry = new FamilyPhysicalQuoteRequest(stageCompact,
                            privateEdges && index < stages.Count - 1, 65536,
                            PrivateInput: privateEdges && stageCompact) { ComputationOnly = true };
                        expected += Component(stages[index].Family, childGeometry);
                    }
                    var compute = Need(neow.Describe(), geometry);
                    Check(Near(Computation(compute, geometry), expected),
                        "N preserves full-stage shared-witness envelope and device output without a second host charge");
                    Check(compute.Evidence.Contains("SharedArrivalWitnessUnchanged", StringComparison.Ordinal),
                        "N computation preserves shared-arrival scope");
                }
            }

            foreach (string familyId in new[] { "R.Relic" })
            {
                var ordinary = Read(familyId == "R.Relic" ? "family-pricing-matrix-v1/R-three-common.json" : "pricing-hole-v1/W-event-mix.json");
                var query = familyId == "R.Relic" ? ordinary with { TreasureRelics = new(1, 2,
                    TreasureRelicOrderMode.Ordered, [null, new ModelKey("RELIC", "JUZU_BRACELET")]) }
                    : ordinary with { FirstActUnknownEventPrefixCount = 2 };
                var request = Request(query, captured);
                foreach (bool gpu in new[] { false, true })
                {
                    var wrapper = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpu)
                        .Single(f => f.FamilyId == familyId && f.Coverage.Contains(InitialRoomsInvocation.TreasureCoverage));
                    Check(wrapper.PhysicalBodies.Count > 1, familyId + " InitialRooms has actual ordinary and additional work");
                    foreach (bool compact in new[] { false, true })
                    {
                        var geometry = new FamilyPhysicalQuoteRequest(compact, false, 65536);
                        var computeGeometry = geometry with { ComputationOnly = true };
                        var full = Need(wrapper, geometry);
                        var compute = Need(wrapper, computeGeometry);
                        Boundary(wrapper, full, compute);
                        double reach = wrapper.PhysicalBodies[0].Survival.SurvivalProbability ?? 1;
                        var localGeometry = computeGeometry with { CompactInput = true, MeanInputPopulation = 65536 * reach };
                        var local = InitialRoomsWorkEstimate.Local(familyId, InitialRoomsPlan.TryCompileTreasure(request.Model)!, localGeometry, gpu,
                            Math.Clamp(request.WorkerCount, 1, 32))!;
                        double expected = Component(wrapper.PhysicalBodies[0], computeGeometry) +
                            Computation(FamilyCoarsePricing.LocalComputation(wrapper, local, localGeometry), localGeometry);
                        Check(Near(Computation(compute, computeGeometry), expected),
                            familyId + " InitialRooms owns ordinary once and local once on ordinary survivors");
                        if (gpu)
                            Check(Public(full, geometry, wrapper.Survival.SurvivalProbability ?? 1) > expected,
                                familyId + " InitialRooms excludes public submits and output from computation");
                        else
                            Check(Near(Public(full, geometry, wrapper.Survival.SurvivalProbability ?? 1), expected),
                                familyId + " InitialRooms retains complete CPU canonical work without duplicate output");
                    }
                }
            }
            // Unknown-room work moved from the W wrapper into its own V owner.
            // Both calls keep their computation/output boundary independently.
            var unknownRequest = Request(Read("pricing-hole-v1/W-event-mix.json") with {
                FirstActUnknownEventPrefixCount = 2 }, captured);
            foreach (bool gpu in new[] { false, true })
            {
                var units = FamilyExecutionCoordinator.CreateRegisteredFamilies(unknownRequest, gpu);
                var world = units.Single(body => body.FamilyId == "W.World");
                var variant = units.Single(body => body.FamilyId == VariantOpeningFamily.Id);
                Check(variant.Preparation.Obligations.Any(o => o.Domain == InitialRoomsInvocation.UnknownCoverage && o.ThroughOrdinal == 2) && units.All(f => !f.Coverage.Contains(InitialRoomsInvocation.TreasureCoverage)),
                    "W progression and V Unknown prefix retain independent actual owners " + gpu);
                foreach (FilterBodyDescription owner in new FilterBodyDescription[] { world, variant })
                foreach (bool compact in new[] { false, true })
                {
                    var geometry = new FamilyPhysicalQuoteRequest(compact, false, 65536);
                    var computeGeometry = geometry with { ComputationOnly = true };
                    var full = Need(owner, geometry);
                    var compute = Need(owner, computeGeometry);
                    double work = Computation(compute, computeGeometry);
                    Check(work > 0 && Near(full.NanosecondsPerInput, compute.NanosecondsPerInput),
                        owner.FamilyId + " retains its computation coefficient across public and computation entry points");
                    Check(gpu ? Public(full, geometry, owner.Survival.SurvivalProbability ?? 1) > work :
                        full.PublicTransportClass == "CpuOrderedAbi1" && Near(Public(full, geometry, 1), work),
                        owner.FamilyId + " keeps GPU host transfer separate and CPU canonical output charged once");
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", priorCpuIdentity);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(priorDevice.GpuIdentity, priorDevice.RenderingBackend);
        }
        Console.WriteLine($"FamilyComputationWrapper checks={checks};PASS");
        return checks;
    }
}
