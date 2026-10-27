using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;

internal static class EtaCostClosureChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string reason)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("EtaCostClosure:" + reason);
        }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var shop = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/cpu-main-v1/S-three-slots.json").Query;
        ExactSearchExecutionRequest Request(SearchQuery query, long count, bool skip = false, int workers = 1,
            SearchContext? source = null) => ExactSearchExecutionRequestFactory.Compile(
                SearchCompiler.Compile(query, source ?? context),
                new("000000000000", count, int.MaxValue, workers, SkipExactValidation: skip)).Plan
            ?? throw new InvalidOperationException("EtaCostClosure.Compile");

        const int window = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
        var request = Request(shop, 2L * window + 7);
        var prefix = new Unit("Eta.Prefix", .5, 10);
        var suffix = new Unit("Eta.Suffix", .25, 20);
        var units = FamilyPlanner.InOrder([prefix, suffix]) with { AllowSmallCandidateExact = true };
        var wrapper = new AutomaticPrivateSerialInvocation(request, units.OrderedFamilies,
            enableSmallCandidateExact: true, estimationStages: units.Stages);
        var transport = FamilyPlanner.InOrder([wrapper]) with { AllowSmallCandidateExact = true };
        var direct = FamilySearchEtaProjectorV1.Project(request, units, liveExactMsPerAttempt: .01);
        var wrapped = FamilySearchEtaProjectorV1.Project(request, transport, liveExactMsPerAttempt: .01);
        Check(wrapped.Status == FamilySearchEtaStatus.Ready && wrapped.TargetStages.Count == 2,
            "private metadata exposes the selected units without needing a private chain price");
        Check(wrapped.TargetFamilyPipelineMs == direct.TargetFamilyPipelineMs &&
            wrapped.TargetExactTailMs == direct.TargetExactTailMs &&
            wrapped.TargetStages.Select(s => s.InputPopulation).SequenceEqual(direct.TargetStages.Select(s => s.InputPopulation)),
            "private ETA follows the same selected public-unit reference model");
        Check(wrapped.TargetStages.All(s => s.CostEvidence.Contains("PrivateTransportOverheadNotMeasured", StringComparison.Ordinal)),
            "public-unit approximation does not claim measured private transport");
        Check(wrapper.PhysicalFamilies.SequenceEqual(units.OrderedFamilies) &&
            wrapper.EstimationStages.SequenceEqual(units.Stages), "metadata cannot select a different execution order");
        Check(prefix.Calls == 0 && suffix.Calls == 0, "ETA does not execute a physical family");

        var selective = new Unit("Eta.Selective", 4d / window, 10);
        var unpriced = new Unit("Eta.Unpriced", .5, null);
        var early = FamilyPlanner.InOrder([selective, unpriced]) with { AllowSmallCandidateExact = true };
        var earlyEta = FamilySearchEtaProjectorV1.Project(request, early, liveExactMsPerAttempt: .01);
        Check(earlyEta.Status == FamilySearchEtaStatus.Ready && earlyEta.TargetStages[1].SteadyStateMs == 0 &&
            earlyEta.TargetStages[1].InputPopulation == 0,
            "per-window early Exact does not ask an unexecuted sparse suffix for a price");
        Check(earlyEta.TargetStages[1].CostEvidence.Contains("ExpectedPopulationEarlyExactApproximation", StringComparison.Ordinal) &&
            Math.Abs(earlyEta.TargetExactTailMs!.Value - .15) < 1e-12,
            "two expected four-candidate handoffs and the seven-root tail reach Exact, with explicit approximation evidence");
        var skipped = FamilySearchEtaProjectorV1.Project(Request(shop, request.ScanCount, skip: true), early,
            liveExactMsPerAttempt: .01);
        Check(skipped.Status == FamilySearchEtaStatus.Unavailable && skipped.TargetExactTailMs == 0 &&
            skipped.TargetStages[1].InputPopulation > 0 && skipped.TargetStages[1].SteadyStateMs is null,
            "SkipExact still requires every selected filter and retains the real missing unit cost");
        var explicitTrial = FamilySearchEtaProjectorV1.Project(request, early with { AllowSmallCandidateExact = false },
            liveExactMsPerAttempt: .01);
        Check(explicitTrial.Status == FamilySearchEtaStatus.Unavailable && explicitTrial.TargetStages[1].InputPopulation > 0,
            "a physical trial without the policy never bypasses its suffix in ETA");
        var empty = FamilySearchEtaProjectorV1.Project(request,
            FamilyPlanner.InOrder([new Unit("Eta.Zero", 0, 10), new Unit("Eta.UnknownTail", null, null)]),
            liveExactMsPerAttempt: .01);
        Check(empty.Status == FamilySearchEtaStatus.Ready && empty.TargetExactTailMs == 0 &&
            empty.TargetStages[1].SteadyStateMs == 0 && empty.TargetStages[1].CostEvidence.Contains("EmptyStageNotInvoked", StringComparison.Ordinal),
            "empty batches do not require downstream survival or timing evidence");
        var unknown = FamilySearchEtaProjectorV1.Project(request, FamilyPlanner.InOrder([unpriced]), liveExactMsPerAttempt: .01);
        Check(unknown.Status == FamilySearchEtaStatus.Unavailable && unknown.TargetFamilyPipelineMs is null &&
            unknown.MissingEvidence.Any(s => s.Contains("NoBodyWorkerOrDeviceSubstitution", StringComparison.Ordinal)),
            "a selected unit with no reference remains explicitly unpriced");
        Check(unknown.TargetExactTailMs is > 0, "missing Family time does not erase independently known Exact time");
        var live = FamilySearchEtaProjectorV1.Project(request, units,
            new Dictionary<string, double> { [units.Stages[0].Performance.PhysicalImplementationRevision] = 1e6 }, .01);
        Check(live.AcceptedResultProbability == direct.AcceptedResultProbability &&
            live.TargetStages.Select(s => s.OutputPopulation).SequenceEqual(direct.TargetStages.Select(s => s.OutputPopulation)) &&
            live.TargetFamilyPipelineMs != direct.TargetFamilyPipelineMs,
            "observed speed changes time only, never the frozen probability or modeled populations");

        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "Intel64 Family 6 Model 183 Stepping 1, GenuineIntel");
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("RTX 4060 Laptop GPU", "d3d12");
            foreach (int acts in new[] { 1, 3 })
            {
                var mapQuery = SearchQuery.Empty with { StandardMaps = Enumerable.Range(1, acts).Select(act =>
                    new StandardMapSearchCondition(act, StandardMapMetric.GuaranteedMonster,
                        StandardMapComparison.AtLeast, 0)).ToArray() };
                var mapRequest = Request(mapQuery, 8, workers: 8);
                var mapPlan = FamilyExecutionCoordinator.Plan(mapRequest, gpuAvailable: false);
                var mapFamily = mapPlan.OrderedFamilies.Single();
                Check(mapFamily is StandardMapFamily && mapFamily.Survival.SurvivalProbability == 1 &&
                    mapPlan.Stages.Single().PerInputTimeMs is null &&
                    mapFamily.QuotePhysicalWork(new(false, false, 65536)) is null &&
                    mapFamily.QuotePhysicalWork(new(true, false, 65536)) is null,
                    "deferred map pricing keeps the filter and empirical probability, including on its former reference CPU");
                var preview = WorkbenchProbabilityPreview.Compute(mapRequest.CompiledSearch, int.MaxValue, 8, cpuOnly: true);
                Check(preview.Costs.TryGetValue("M.StandardMap", out var mapCost) &&
                    mapCost.Cpu is null && mapCost.Gpu is null && preview.Map?.Probability == 1,
                    "map preview preserves the condition/probability row without reviving a retired quote");
                var exactOnly = FamilyPlanner.InOrder([]);
                var coldMap = FamilySearchEtaProjectorV1.Project(mapRequest, exactOnly);
                Check(coldMap.TargetExactTailMs is null && coldMap.MissingEvidence.Any(reason =>
                    reason.Contains("ProductionExactHardwareEvidenceUnavailable", StringComparison.Ordinal)),
                    "map-only ETA cannot fall back to the retired preset Exact time");
                var observedMap = FamilySearchEtaProjectorV1.Project(mapRequest, exactOnly, liveExactMsPerAttempt: .01);
                Check(observedMap.TargetExactTailMs is > 0 &&
                    observedMap.AcceptedResultProbability == coldMap.AcceptedResultProbability,
                    "actual Exact time remains usable without changing map probability or restoring reference pricing");
                var execution = new FamilyExecutionContext(requiresGpu: false);
                try
                {
                    var batch = new SearchBatch(0, 1);
                    var result = mapFamily.InvokeAsync(execution, new(batch, mapRequest),
                        FamilyCandidateSet.Dense(batch), default).AsTask().GetAwaiter().GetResult();
                    Check(result.Count == 1, "unpriced map filtering remains executable");
                }
                finally
                {
                    mapFamily.DisposeAsync(execution).AsTask().GetAwaiter().GetResult();
                    execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            var relic = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/cpu-main-v1/R-shop-three.json").Query;
            var tiny = Request(relic, 128, workers: 16);
            var tinyPlan = FamilyExecutionCoordinator.Plan(tiny, gpuAvailable: false);
            var tinyEta = FamilySearchEtaProjectorV1.Project(tiny, tinyPlan, liveExactMsPerAttempt: .01);
            var tinyFamily = tinyPlan.OrderedFamilies.Single();
            var fullReference = tinyFamily.QuotePhysicalWork(new(false, false, 65536));
            Check(tinyPlan.Stages.Single().PerInputTimeMs is > 0 && FamilyCanonicalMeasuredCost.IsCanonicalQuote(fullReference),
                "the actual configured CPU body has an existing canonical full-unit reference");
            Check(tinyFamily.QuotePhysicalWork(new(false, false, 128)) is null,
                "tiny CPU input remains absent from canonical pricing while its actual configured body has a full-unit reference");
            Check(tinyEta.Status == FamilySearchEtaStatus.Ready && tinyEta.TargetStages.Single().CostEvidence.Contains(
                "ReferenceGeometryApproximation", StringComparison.Ordinal),
                "ETA may explicitly scale that same body and worker reference without inventing a new measured knot");

            var ns = shop with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with {
                NeowRelics = new([new("RELIC", "GOLDEN_PEARL")], [], []) } };
            var nsRequest = Request(ns, window);
            var nsPlan = FamilyExecutionCoordinator.Plan(nsRequest, gpuAvailable: true, gpuFloat64Available: true);
            Check(nsPlan.OrderedFamilies.Single() is AutomaticPrivateSerialInvocation { EstimationStages.Count: 2 } &&
                FamilySearchEtaProjectorV1.EstimationStages(nsPlan).SelectMany(s => s.Coverage).ToHashSet().SetEquals(
                    ["N.Neow", "S.MerchantShopColorless"]),
                "ordinary real N+S planning attaches the selected child references to its private transport");
            var nsEta = FamilySearchEtaProjectorV1.Project(nsRequest, nsPlan, liveExactMsPerAttempt: .01);
            Check(nsEta.Status == FamilySearchEtaStatus.Ready && nsEta.TargetStages.Count == 2,
                "ordinary real private plan reaches ETA through its existing child-unit references");

            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            foreach (int players in new[] { 2, 4 })
            {
                var party = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
                    .Invoke(null, [context, players, null, null])!;
                var pool = Beta111MerchantColorlessAuthority.From(context.Authority).UncommonPool;
                var query = SearchQuery.Empty with { Players = party.Party!.Players.Select(owner =>
                    new PlayerOfferQuery(owner.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = SearchQuery.Empty with {
                        MerchantColorlessConditions = [new(3, MerchantColorlessSlot.Uncommon, pool[0])] } }).ToArray() };
                var compiled = SearchCompiler.Compile(query, party);
                var partyRequest = Request(query, window, workers: 16, source: party);
                var partyPlan = FamilyExecutionCoordinator.Plan(partyRequest, gpuAvailable: true, gpuFloat64Available: true);
                Check(partyPlan.OrderedFamilies.Single() is PartyFamily && partyPlan.Stages.Single().PerInputTimeMs is > 0,
                    "real multiplayer shop wrapper retains its existing measured single-unit reference");
                var partyEta = FamilySearchEtaProjectorV1.Project(partyRequest, partyPlan, liveExactMsPerAttempt: .01);
                Check(partyEta.Status == FamilySearchEtaStatus.Ready && partyEta.TargetStages.Single().CostEvidence.Contains(
                    "LocalMeasuredGPU", StringComparison.Ordinal), "ETA consumes the existing whole-table unit quote");
                var preview = WorkbenchProbabilityPreview.Compute(compiled, int.MaxValue, 16, cpuOnly: true);
                Check(preview.Eta is not null && preview.Costs.ContainsKey("S.MerchantShopColorless") && preview.Issues.Count == 0,
                    "multiplayer preview reaches ETA and cost consumers even on an unpriced CPU configuration");

                var card = context.Authority.EffectAuthority!.CharacterRewardPool!.First(c => c.IsUnlockedInCapturedPool).CardKey;
                var unchangedCombat = SearchQuery.Empty with { CombatCardRewards = new(6,
                    CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]) };
                var changedCombat = unchangedCombat with { LegacyCombatRewardConstraints = [new(0,
                    new([card], [], []), NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, null)] };
                foreach (bool changed in new[] { false, true })
                {
                    var personal = changed ? changedCombat : unchangedCombat;
                    var combatQuery = SearchQuery.Empty with { Players = party.Party.Players.Select(owner =>
                        new PlayerOfferQuery(owner.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = personal }).ToArray() };
                    var combatRequest = Request(combatQuery, window, source: party);
                    var gpuCombat = FamilyExecutionCoordinator.CreateRegisteredFamilies(combatRequest, true, true)
                        .OfType<PartyFamily>().Single(f => f.FamilyId == "C.CombatReward");
                    var cpuCombat = ((IFamilyInvocation)gpuCombat).CpuRealizations.OfType<PartyFamily>().Single();
                    foreach (var wrapperCombat in new[] { gpuCombat, cpuCombat })
                    {
                        Check(wrapperCombat.PersonalStages.All(stage =>
                            (stage.Family is FamilyCpuExecution cpu ? cpu.RealizationOwner : stage.Family)
                                is CombatRewardFamily c && c.Replay.Plan.NeedsCompleteMatchingReference == changed),
                            "party C keeps the actual changed body identity through its CPU realization");
                        Check(wrapperCombat.ConditionPerformance.PhysicalImplementationRevision.Contains(
                            ".CompletePositiveMatching.20261026.v1", StringComparison.Ordinal) == changed,
                            "only the changed party C wrapper carries the new matching revision");
                        Check(!wrapperCombat.ConditionPerformance.PhysicalImplementationRevision.Contains(
                            ".PartyQueryLiteralOpening.20261027.v1", StringComparison.Ordinal),
                            "unchanged opening bodies do not acquire the query-literal party revision");
                        Check((wrapperCombat.QuoteUnit(window, false, new HashSet<string>()) is null) == changed &&
                            (wrapperCombat.QuotePhysicalWork(new(false, false, window)) is null) == changed,
                            "changed party C bodies cannot borrow old wrapper prices, while unchanged bodies retain them");
                    }
                }
                foreach (var opening in new[] {
                    unchangedCombat with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) },
                    unchangedCombat with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule) } })
                {
                    // Real table compilation and CPU realizations, without GPU
                    // dispatch or manufactured timing evidence for the new body.
                    var combatQuery = SearchQuery.Empty with { Players = party.Party.Players.Select(owner =>
                        new PlayerOfferQuery(owner.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = opening }).ToArray() };
                    var combatRequest = Request(combatQuery, window, source: party);
                    var gpuCombat = FamilyExecutionCoordinator.CreateRegisteredFamilies(combatRequest, true, true)
                        .OfType<PartyFamily>().Single(f => f.FamilyId == "C.CombatReward");
                    var cpuCombat = ((IFamilyInvocation)gpuCombat).CpuRealizations.OfType<PartyFamily>().Single();
                    foreach (var wrapperCombat in new[] { gpuCombat, cpuCombat })
                    foreach (bool compact in new[] { false, true })
                    {
                        Check(wrapperCombat.PersonalStages.All(stage =>
                            (stage.Family is FamilyCpuExecution cpu ? cpu.RealizationOwner : stage.Family)
                                is CombatRewardFamily { Replay.UsesQueryLiteralPartyOpening: true }),
                            "party opening body identity survives the configured CPU realization");
                        Check(wrapperCombat.ResolveConditionPerformance(compact).PhysicalImplementationRevision.Contains(
                            ".PartyQueryLiteralOpening.20261027.v1", StringComparison.Ordinal) &&
                            wrapperCombat.ResolveConditionPerformance(compact).Evidence.Contains(
                                "NoHistoricalOpeningQuote", StringComparison.Ordinal),
                            "changed party opening wrapper records the new body and missing reference scope");
                        Check(wrapperCombat.QuoteUnit(window, compact, new HashSet<string>()) is null &&
                            wrapperCombat.QuotePhysicalWork(new(compact, false, window)) is null,
                            "new partial-Bones/Capsule CPU and GPU wrappers cannot borrow old opening measurements");
                        Check(wrapperCombat.PersonalStages.All(stage =>
                            stage.Family.ResolveConditionPerformance(compact).PhysicalImplementationRevision.Contains(
                                ".PartyQueryLiteralOpening.20261027.v1", StringComparison.Ordinal)),
                            "each underlying C body carries the same opening revision as its wrapper");
                    }
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", oldCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity, oldDevice.RenderingBackend);
        }
        Console.WriteLine($"EtaCostClosure checks={checks};PASS");
        return 0;
    }

    // Timing/population fixture only: it throws if the estimate attempts to run
    // or bind a numerical body. Real compiler/CPU/Party entry checks follow it.
    private sealed class Unit(string id, double? probability, double? ns) : IFamilyInvocation
    {
        internal int Calls;
        public string FamilyId => id;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(id,
            FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, [], "EtaFixture");
        public FamilySurvivalProjection Survival => probability is double p
            ? FamilySurvivalProjection.Resolved(id, p, "EtaFixture")
            : FamilySurvivalProjection.Unresolved(id, "EtaFixtureUnknown");
        public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
        public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) =>
            new(id, id + (compact ? ".Compact" : ".Dense"), "EtaFixture", 1, "EtaFixture");
        bool IFamilyInvocation.CanBindPrivateSerial => true;
        public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry) => ns is double cost
            ? new(id, "EtaFixture", cost, FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, 0, "EtaFixtureReference",
                PublicTransportClass: "CompleteCanonicalAbi1") : null;
        public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance,
            "", 0, 0, 0, 0, 0, 0, 0, 0, false, "EtaFixture");
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
            FamilyCandidateSet input, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("EtaCostClosure.EstimationMustNotExecute");
        }
    }
}
