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
    private static FamilySearchEtaProjectionV1 Project(PreparedSearch request, FamilyExecutionPlan plan,
        IReadOnlyDictionary<string, double>? livePerformanceByPhysicalRevision = null, double? liveExactMsPerAttempt = null) =>
        FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(request, plan), plan,
            livePerformanceByPhysicalRevision, liveExactMsPerAttempt);

    internal static int RunGpuPrefixTransport()
    {
        int checks = 0;
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidDataException("GpuPrefixTransport:" + reason); }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var request = SearchPreparationCompiler.Compile(SearchCompiler.Compile(SearchQuery.Empty, context),
            new("000000000000", 65536, 100, 1, SkipExactValidation: true)).Plan!;
        var method = typeof(FamilyExecutionCoordinator).GetMethod("PlanSimpleSingleplayer",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        FamilyExecutionPlan Select(FilterBodyDescription[] bodies, string? mode = null)
        {
            Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", mode);
            var ordered = request with { RunOptions = request.RunOptions with {
                ManualFamilyOrder = bodies.Select(f => f.FamilyId).ToArray() } };
            return (FamilyExecutionPlan)method.Invoke(null, [ordered, bodies, true, false])!;
        }
        string? old = Environment.GetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT");
        try
        {
            var n = new Unit("N.Neow", .5, 10);
            var s = new Unit("S.MerchantShopColorless", .25, 20);
            var m = new Unit("M.StandardMap", .5, null, bindable: false);
            FilterBodyDescription[] bodies = [n, s, m];
            var ordinary = Select(bodies, "off");
            var selected = Select(bodies);
            Check(ordinary.OrderedFamilies.SequenceEqual(bodies) && selected.OrderedFamilies.Count == 2 &&
                selected.OrderedFamilies[0].IsPrivateTransport &&
                ReferenceEquals(selected.OrderedFamilies[1], m.Body), "unpriced CPU tail leaves a runnable private GPU prefix");
            var serial = selected.OrderedFamilies[0];
            Check(serial.PhysicalBodies.SequenceEqual([n, s]) &&
                selected.OrderedFamilies.Skip(1).SelectMany(body => body.Coverage).SequenceEqual(["M.StandardMap"]) &&
                selected.ExecutionOrderUnits.SequenceEqual(bodies) &&
                selected.Stages.SelectMany(stage => stage.Coverage).SequenceEqual(bodies.Select(f => f.FamilyId)),
                "transport preserves every selected unit, authored order and coverage exactly once");
            var afterCpu = new Unit("W.World", .5, 20);
            var segmented = Select([n, s, m, afterCpu]);
            Check(segmented.OrderedFamilies.Count == 3 && segmented.OrderedFamilies[0] is { IsPrivateTransport: true } head &&
                head.PhysicalBodies.SequenceEqual([n, s]) && segmented.OrderedFamilies.Skip(1).SelectMany(body => body.Coverage).SequenceEqual(["M.StandardMap", "W.World"]) &&
                segmented.OrderedFamilies.Skip(1).SequenceEqual([m, afterCpu]),
                "first CPU boundary closes the Dense prefix and retains later GPU units in the public tail");
            static object StageIdentity(FamilyExecutionPlanStage stage) =>
                (stage.Family, stage.CompactAbi1Input, stage.ExpectedInputFraction, stage.Survival,
                    stage.PerInputTimeMs, stage.ExpectedStageTimeMsPerRoot, stage.RegistrationIndex);
            Check(FamilySearchEtaProjectorV1.EstimationStages(selected).Select(StageIdentity)
                    .SequenceEqual(ordinary.Stages.Select(StageIdentity)) &&
                selected.ExpectedFamilyPipelineMsPerRoot == ordinary.ExpectedFamilyPipelineMsPerRoot &&
                selected.EstimatedTerminalSurvival == ordinary.EstimatedTerminalSurvival &&
                selected.SelectionPolicyId == ordinary.SelectionPolicyId && selected.RankingAuthority == ordinary.RankingAuthority,
                "transport keeps the original computational costs, population projections and selection authority");
            foreach (var unit in new[] { n, s, m }) unit.Geometry.Clear();
            var eta = Project(request, selected, liveExactMsPerAttempt: .01);
            Check(eta.Status == FamilySearchEtaStatus.Unavailable && eta.TargetStages.Count == 3 &&
                eta.TargetStages[2].InputPopulation > 0 && eta.TargetStages[2].SteadyStateMs is null && eta.TargetExactTailMs == 0,
                "SkipExact retains the missing CPU-tail price without skipping its filter or invoking Exact");
            Check(n.Geometry.Any(g => !g.PrivateInput && g.PrivateOutput) &&
                s.Geometry.Any(g => g.PrivateInput && !g.PrivateOutput) &&
                m.Geometry.Count > 0 && m.Geometry.All(g => g.CompactInput && !g.PrivateInput && !g.PrivateOutput),
                "ETA materializes the prefix before the public CPU tail rather than extending private roles through it");
            var pricedM = new Unit("M.StandardMap", .5, 30, bindable: false);
            var priced = Select([n, s, pricedM]);
            var pricedEta = Project(request, priced, liveExactMsPerAttempt: .01);
            Check(pricedEta.Status == FamilySearchEtaStatus.Ready && pricedEta.TargetStages[2].CostEvidence.Contains(
                "PublicTransferEnvelope", StringComparison.Ordinal) && !pricedEta.TargetStages[2].CostEvidence.Contains(
                "ActualPrivateInputOutputRoles", StringComparison.Ordinal), "priced CPU suffix uses its public complete invocation envelope");
            foreach (FilterBodyDescription[] shape in new FilterBodyDescription[][] { [m, n, s], [n, m, s], [n], [m] })
            {
                var untouched = Select(shape);
                Check(untouched.OrderedFamilies.SequenceEqual(shape), "CPU-first and prefixes shorter than two retain ordinary transport");
            }
            foreach (string? mode in new[] { null, "off" })
                Check(Select([n, s], mode).OrderedFamilies.Single() is { IsPrivateTransport: true } full &&
                    full.PhysicalBodies.SequenceEqual([n, s]), "all-GPU complete-chain transport retains its original behavior including off");
            var shop = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/cpu-main-v1/S-three-slots.json").Query;
            var authored = shop with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with {
                NeowRelics = new([new("RELIC", "GOLDEN_PEARL")], [], []) },
                StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 0)] };
            var real = SearchPreparationCompiler.Compile(SearchCompiler.Compile(authored, context),
                request.RunOptions with { ManualFamilyOrder = ["N.Neow", "S.MerchantShopColorless", "M.StandardMap"] }).Plan!;
            Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", null);
            var realPlan = FamilyExecutionCoordinator.Plan(real, true, gpuFloat64Available: true);
            Check(realPlan.OrderedFamilies.Count == 2 && realPlan.OrderedFamilies[0] is { IsPrivateTransport: true, PhysicalBodies.Count: 2 } && realPlan.OrderedFamilies[1].FamilyId == StandardMapFamily.Id &&
                realPlan.ExecutionOrderUnits.Select(f => f.FamilyId).SequenceEqual(["N.Neow", "S.MerchantShopColorless", "M.StandardMap"]),
                "actual compiler and coordinator select native N/S prefix and preserve CPU M tail");
            Check(n.Calls == 0 && s.Calls == 0 && m.Calls == 0, "planning and ETA never execute numerical bodies");
        }
        finally { Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", old); }
        Console.WriteLine($"GpuPrefixTransport checks={checks};PASS");
        return 0;
    }

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
        PreparedSearch Request(SearchQuery query, long count, bool skip = false, int workers = 1,
            SearchContext? source = null) => SearchPreparationCompiler.Compile(
                SearchCompiler.Compile(query, source ?? context),
                new("000000000000", count, int.MaxValue, workers, SkipExactValidation: skip)).Plan
            ?? throw new InvalidOperationException("EtaCostClosure.Compile");

        const int window = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
        var request = Request(shop, 2L * window + 7);
        var prefix = new Unit("Eta.Prefix", .5, 10);
        var suffix = new Unit("Eta.Suffix", .25, 20);
        var units = FamilyPlanner.InOrder([prefix, suffix]) with { AllowSmallCandidateExact = true };
        var wrapper = AutomaticPrivateSerialInvocation.Prepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), units.OrderedFamilies,
            enableSmallCandidateExact: true, estimationStages: units.Stages);
        var transport = FamilyPlanner.InOrder([wrapper]) with { AllowSmallCandidateExact = true };
        var direct = Project(request, units, liveExactMsPerAttempt: .01);
        var wrapped = Project(request, transport, liveExactMsPerAttempt: .01);
        Check(wrapped.Status == FamilySearchEtaStatus.Ready && wrapped.TargetStages.Count == 2,
            "private metadata exposes the selected units without needing a private chain price");
        Check(wrapped.TargetFamilyPipelineMs < direct.TargetFamilyPipelineMs &&
            wrapped.TargetExactTailMs == direct.TargetExactTailMs &&
            wrapped.TargetStages.Select(s => s.InputPopulation).SequenceEqual(direct.TargetStages.Select(s => s.InputPopulation)),
            "private ETA preserves populations and Exact while excluding intermediate public payload transport");
        Check(wrapped.TargetStages.All(s => s.CostEvidence.Contains("PrivateTransportOverheadNotMeasured", StringComparison.Ordinal) &&
            s.CostEvidence.Contains("FamilyComputationMs=", StringComparison.Ordinal) &&
            s.CostEvidence.Contains("HostTransferEstimateMs=", StringComparison.Ordinal) &&
            s.CostEvidence.Contains("PublicCompleteCallNotReused", StringComparison.Ordinal)),
            "private ETA composes computation and a separate disclosed transport estimate");
        Check(wrapper.PhysicalBodies.SequenceEqual(units.OrderedFamilies) &&
            wrapper.EstimationStages.SequenceEqual(units.Stages), "metadata cannot select a different execution order");
        Check(prefix.Calls == 0 && suffix.Calls == 0, "ETA does not execute a physical family");

        var selective = new Unit("Eta.Selective", 4d / window, 10);
        var unpriced = new Unit("Eta.Unpriced", .5, null);
        var early = FamilyPlanner.InOrder([selective, unpriced]) with { AllowSmallCandidateExact = true };
        var earlyEta = Project(request, early, liveExactMsPerAttempt: .01);
        Check(earlyEta.Status == FamilySearchEtaStatus.Ready && earlyEta.TargetStages[1].SteadyStateMs == 0 &&
            earlyEta.TargetStages[1].InputPopulation == 0,
            "per-window early Exact does not ask an unexecuted sparse suffix for a price");
        Check(earlyEta.TargetStages[1].CostEvidence.Contains("ExpectedPopulationEarlyExactApproximation", StringComparison.Ordinal) &&
            Math.Abs(earlyEta.TargetExactTailMs!.Value - .15) < 1e-12,
            "two expected four-candidate handoffs and the seven-root tail reach Exact, with explicit approximation evidence");
        var skipped = Project(Request(shop, request.ScanCount, skip: true), early,
            liveExactMsPerAttempt: .01);
        Check(skipped.Status == FamilySearchEtaStatus.Unavailable && skipped.TargetExactTailMs == 0 &&
            skipped.TargetStages[1].InputPopulation > 0 && skipped.TargetStages[1].SteadyStateMs is null,
            "SkipExact still requires every selected filter and retains the real missing unit cost");
        var explicitTrial = Project(request, early with { AllowSmallCandidateExact = false },
            liveExactMsPerAttempt: .01);
        Check(explicitTrial.Status == FamilySearchEtaStatus.Unavailable && explicitTrial.TargetStages[1].InputPopulation > 0,
            "a physical trial without the policy never bypasses its suffix in ETA");
        var empty = Project(request,
            FamilyPlanner.InOrder([new Unit("Eta.Zero", 0, 10), new Unit("Eta.UnknownTail", null, null)]),
            liveExactMsPerAttempt: .01);
        Check(empty.Status == FamilySearchEtaStatus.Ready && empty.TargetExactTailMs == 0 &&
            empty.TargetStages[1].SteadyStateMs == 0 && empty.TargetStages[1].CostEvidence.Contains("EmptyStageNotInvoked", StringComparison.Ordinal),
            "empty batches do not require downstream survival or timing evidence");
        var unknown = Project(request, FamilyPlanner.InOrder([unpriced]), liveExactMsPerAttempt: .01);
        Check(unknown.Status == FamilySearchEtaStatus.Unavailable && unknown.TargetFamilyPipelineMs is null &&
            unknown.MissingEvidence.Any(s => s.Contains("NoBodyWorkerOrDeviceSubstitution", StringComparison.Ordinal)),
            "a selected unit with no reference remains explicitly unpriced");
        Check(unknown.TargetExactTailMs is > 0, "missing Family time does not erase independently known Exact time");
        var live = Project(request, units,
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
                Check(mapFamily.FamilyId == StandardMapFamily.Id && mapFamily.Survival.SurvivalProbability == 1 &&
                    mapPlan.Stages.Single().PerInputTimeMs is null &&
                    mapFamily.QuotePhysicalWork(new(false, false, 65536)) is null &&
                    mapFamily.QuotePhysicalWork(new(true, false, 65536)) is null,
                    "deferred map pricing keeps the filter and empirical probability, including on its former reference CPU");
                var preview = WorkbenchProbabilityPreview.Compute(mapRequest.Model.CompiledSearch, int.MaxValue, 8, cpuOnly: true);
                Check(preview.Costs.TryGetValue("M.StandardMap", out var mapCost) &&
                    mapCost.Cpu is null && mapCost.Gpu is null && preview.Map?.Probability == 1,
                    "map preview preserves the condition/probability row without reviving a retired quote");
                var exactOnly = FamilyPlanner.InOrder([]);
                var coldMap = Project(mapRequest, exactOnly);
                Check(coldMap.TargetExactTailMs is null && coldMap.MissingEvidence.Any(reason =>
                    reason.Contains("ProductionExactHardwareEvidenceUnavailable", StringComparison.Ordinal)),
                    "map-only ETA cannot fall back to the retired preset Exact time");
                var observedMap = Project(mapRequest, exactOnly, liveExactMsPerAttempt: .01);
                Check(observedMap.TargetExactTailMs is > 0 &&
                    observedMap.AcceptedResultProbability == coldMap.AcceptedResultProbability,
                    "actual Exact time remains usable without changing map probability or restoring reference pricing");
                var execution = new FilterRuntimeServices(requiresGpu: false);
                var runningMap = mapFamily.CreateExecution!(mapFamily, execution);
                try
                {
                    var batch = new SearchBatch(0, 1);
                    var result = runningMap.FilterAsync(execution,
                        FamilyCandidateSet.Dense(batch), default).AsTask().GetAwaiter().GetResult();
                    Check(result.Count == 1, "unpriced map filtering remains executable");
                }
                finally
                {
                    runningMap.DisposeAsync(execution).AsTask().GetAwaiter().GetResult();
                    execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            var relic = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/cpu-main-v1/R-shop-three.json").Query;
            var tiny = Request(relic, 128, workers: 16);
            var tinyPlan = FamilyExecutionCoordinator.Plan(tiny, gpuAvailable: false);
            var tinyEta = Project(tiny, tinyPlan, liveExactMsPerAttempt: .01);
            var tinyFamily = tinyPlan.OrderedFamilies.Single();
            var fullReference = tinyFamily.QuotePhysicalWork(new(false, false, 65536));
            Check(tinyPlan.Stages.Single().PerInputTimeMs is > 0 && FamilyCanonicalMeasuredCost.IsCanonicalQuote(fullReference),
                "the actual configured CPU body has an existing canonical full-unit reference");
            Check(FamilyCoarsePricing.IsEstimate(tinyFamily.QuotePhysicalWork(new(false, false, 128))) &&
                FamilyCanonicalMeasuredCost.TryQuote(tinyFamily, new(false, false, 128), 128) is null,
                "tiny CPU input has an honest work estimate without manufacturing a new canonical knot");
            Check(tinyEta.Status == FamilySearchEtaStatus.Ready && tinyEta.TargetStages.Single().CostEvidence.Contains(
                "FamilyComputationMs=", StringComparison.Ordinal) && tinyEta.TargetStages.Single().CostEvidence.Contains(
                "PriceQuality=Estimate", StringComparison.Ordinal),
                "tiny CPU ETA uses that same body's explicit work estimate without inventing a measured knot");

            var ns = shop with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with {
                NeowRelics = new([new("RELIC", "GOLDEN_PEARL")], [], []) } };
            var nsRequest = Request(ns, window);
            var nsPlan = FamilyExecutionCoordinator.Plan(nsRequest, gpuAvailable: true, gpuFloat64Available: true);
            Check(nsPlan.OrderedFamilies.Single() is { IsPrivateTransport: true, EstimationStages.Count: 2 } &&
                FamilySearchEtaProjectorV1.EstimationStages(nsPlan).SelectMany(s => s.Coverage).ToHashSet().SetEquals(
                    ["N.Neow", "S.MerchantShopColorless"]),
                "ordinary real N+S planning attaches the selected child references to its private transport");
            var nsEta = Project(nsRequest, nsPlan, liveExactMsPerAttempt: .01);
            Check(nsEta.Status == FamilySearchEtaStatus.Ready && nsEta.TargetStages.Count == 2 &&
                nsEta.TargetStages.All(s => s.CostEvidence.Contains("HostTransferOutsideFamilyCost", StringComparison.Ordinal) &&
                    s.CostEvidence.Contains("ActualPrivateInputOutputRoles", StringComparison.Ordinal)),
                "real N+S private ETA uses each child's computation and separately names its actual transport roles");

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
                Check(partyPlan.OrderedFamilies.Single().Preparation.Obligations.Count == players && partyPlan.Stages.Single().PerInputTimeMs is > 0,
                    "real multiplayer shop wrapper has a whole-unit historical reference estimate");
                var partyEta = Project(partyRequest, partyPlan, liveExactMsPerAttempt: .01);
                Check(partyEta.Status == FamilySearchEtaStatus.Ready && partyEta.TargetStages.Single().CostEvidence.Contains(
                    "PriceQuality=Estimate", StringComparison.Ordinal), "ETA consumes the whole-table estimate without claiming a wrapper measurement");
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
                        .Single(f => f.FamilyId == "C.CombatReward");
                    var cpuCombat = ((FilterBodyDescription)gpuCombat).CpuAlternatives.Single();
                    foreach (var wrapperCombat in new[] { gpuCombat, cpuCombat })
                    {
                        Check(wrapperCombat.PhysicalBodies.All(child => child.Cost.PartyChild.CompleteCombatMatching == changed),
                            "party C keeps the actual changed body identity through its CPU realization");
                        Check(wrapperCombat.ConditionPerformance.PhysicalImplementationRevision.Contains(
                            ".CompletePositiveMatching.20261026.v1", StringComparison.Ordinal) == changed,
                            "only the changed party C wrapper carries the new matching revision");
                        Check(!wrapperCombat.ConditionPerformance.PhysicalImplementationRevision.Contains(
                            ".PartyQueryLiteralOpening.20261027.v1", StringComparison.Ordinal),
                            "unchanged opening bodies do not acquire the query-literal party revision");
                        var unitQuote = wrapperCombat.Cost.CompleteCallQuote?.Invoke(new(false, false, window), new HashSet<string>());
                        Check(unitQuote is not null && FamilyCoarsePricing.IsEstimate(wrapperCombat.QuotePhysicalWork(new(false, false, window))) &&
                            (!changed || unitQuote.Evidence.Contains("WeightedOwnChildren", StringComparison.Ordinal)),
                            "changed party C bodies use their own child work estimate; unchanged wrappers retain historical estimates");
                    }
                }
                foreach (var opening in new[] {
                    unchangedCombat with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) },
                    unchangedCombat with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule) } })
                {
                    // FT03 replaced partial-Bones/Capsule's query-literal
                    // placeholder with actual observed held-effect replay. This
                    // checks both realizations without dispatching a GPU body.
                    var combatQuery = SearchQuery.Empty with { Players = party.Party.Players.Select(owner =>
                        new PlayerOfferQuery(owner.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = opening }).ToArray() };
                    var combatRequest = Request(combatQuery, window, source: party);
                    var gpuCombat = FamilyExecutionCoordinator.CreateRegisteredFamilies(combatRequest, true, true)
                        .Single(f => f.FamilyId == "C.CombatReward");
                    var cpuCombat = ((FilterBodyDescription)gpuCombat).CpuAlternatives.Single();
                    foreach (var wrapperCombat in new[] { gpuCombat, cpuCombat })
                    foreach (bool compact in new[] { false, true })
                    {
                        var openingQuote = wrapperCombat.Cost.CompleteCallQuote?.Invoke(new(compact, false, window), new HashSet<string>());
                        Console.WriteLine($"EtaObservedOpening players={players};opening={opening.OpeningRoute!.RouteRelicKey};gpu={wrapperCombat.UsesGpu};compact={compact};" +
                            $"children={string.Join('|', wrapperCombat.PhysicalBodies.Select(child => child.ResolveConditionPerformance(compact).PhysicalImplementationRevision))};" +
                            $"historicalAllowed={string.Join(',', wrapperCombat.PhysicalBodies.Select(child => child.Cost.PartyChild.AllowsHistoricalEnvelope))};" +
                            $"queryLiteral={string.Join(',', wrapperCombat.PhysicalBodies.Select(child => child.Cost.PartyChild.QueryLiteralCombatOpening))};quoteEvidence={openingQuote?.Evidence}");
                        Check(wrapperCombat.PhysicalBodies.Count == players && wrapperCombat.PhysicalBodies.All(child => !child.Cost.PartyChild.QueryLiteralCombatOpening &&
                            !child.Cost.PartyChild.AllowsHistoricalEnvelope),
                            "actual observed party opening bodies do not borrow query-literal or historical wrapper identities");
                        Check(!wrapperCombat.ResolveConditionPerformance(compact).PhysicalImplementationRevision.Contains(
                            ".PartyQueryLiteralOpening.20261027.v1", StringComparison.Ordinal),
                            "observed party wrapper does not claim the superseded query-literal opening body");
                        Check(openingQuote is not null &&
                            openingQuote.Evidence.Contains("WeightedOwnChildren", StringComparison.Ordinal) &&
                            openingQuote.Evidence.Contains("ObservedOpeningShuffleDraws=", StringComparison.Ordinal) &&
                            openingQuote.Evidence.Contains("ObservedOpeningNotMeasured", StringComparison.Ordinal) &&
                            !openingQuote.Evidence.Contains("HistoricalWrapperCoefficients", StringComparison.Ordinal) &&
                            FamilyCoarsePricing.IsEstimate(wrapperCombat.QuotePhysicalWork(new(compact, false, window))),
                            "observed partial-Bones/Capsule CPU and GPU wrappers compose their own unmeasured child work");
                        Check(wrapperCombat.PhysicalBodies.All(child =>
                            child.ResolveConditionPerformance(compact).PhysicalImplementationRevision.Contains(
                                CombatRewardObservedOpening.Revision, StringComparison.Ordinal)),
                            "each configured CPU/GPU child retains its actual observed-opening numerical revision");
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
    private sealed class Unit(string id, double? probability, double? ns, bool bindable = true)
    {
        internal int Calls;
        internal readonly List<FamilyPhysicalQuoteRequest> Geometry = [];
        private FilterBodyDescription? _body;
        public FilterBodyDescription Body => _body ??= new(new PreparedFilter(id)
        { AnalyticalCost = new(id, FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, [], "EtaFixture"),
            Survival = probability is double p ? FamilySurvivalProjection.Resolved(id, p, "EtaFixture") :
                FamilySurvivalProjection.Unresolved(id, "EtaFixtureUnknown") }, "EtaFixture", RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
        {
            UsesGpu = true, PerformanceProjection = compact => new(id, id + (compact ? ".Compact" : ".Dense"), "EtaFixture", 1, "EtaFixture"),
            BindPrivate = bindable ? (_, _, _) => throw new InvalidOperationException("EtaCostClosure.EstimationMustNotBind") : null,
            Cost = new() { Quote = (_, geometry, _) => QuotePhysicalWork(geometry) },
            CreateExecution = (_, _) => { Calls++; throw new InvalidOperationException("EtaCostClosure.EstimationMustNotExecute"); }
        };
        public static implicit operator FilterBodyDescription(Unit unit) => unit.Body;
        public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry)
        {
            Geometry.Add(geometry);
            return ns is double cost ? new(id, "EtaFixture", cost, FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, 0,
                "EtaFixtureReference", PublicTransportClass: bindable ? "Counted32" : "CpuOrderedAbi1") : null;
        }
    }
}
