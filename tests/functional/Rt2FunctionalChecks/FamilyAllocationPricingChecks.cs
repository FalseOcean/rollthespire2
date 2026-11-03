using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Presentation.Localization;

internal static class FamilyAllocationPricingChecks
{
    internal static int RunManualOrder()
    {
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("ManualFamilyOrder: " + name); }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (InvalidOperationException error) when (error.Message.StartsWith(code, StringComparison.Ordinal))
            { checks++; return; }
            throw new InvalidOperationException("ManualFamilyOrder: expected " + code);
        }
        var calls = new List<(string Id, bool Compact)>();
        var n = new SimplePlannerFixture("N.Neow", null, null, _ => .5, onInvoke: (id, compact) => calls.Add((id, compact)));
        var w = new SimplePlannerFixture("W.World", null, null, _ => .25, onInvoke: (id, compact) => calls.Add((id, compact)));
        var r = new SimplePlannerFixture("R.Relic", null, null, _ => .2);
        var fused = new SimplePlannerFixture("N.Neow", null, null, _ => .1, coverage: ["N.Neow", "R.Relic"]);
        var manual = FamilyPlanner.InManualOrder([n, w], [n, w], ["W.World", "N.Neow"]);
        Check(manual.ExecutionOrderUnits.SequenceEqual([w, n]) && manual.ExpectedFamilyPipelineMsPerRoot is null,
            "unpriced units follow manual order with unknown estimate");
        Check(manual.Stages[0].CompactAbi1Input == false && manual.Stages[1].CompactAbi1Input &&
            manual.Stages[1].ExpectedInputFraction == .25 && n.PassedCoverage.Last().SetEquals(["W.World"]),
            "manual projections use the actual Dense/Compact role and passed coverage");
        Check(FamilyPlanner.PlanSimple([n, w]).ExecutionOrderUnits.SequenceEqual([n, w]),
            "automatic planning keeps its existing unknown-evidence registration order");
        Check(FamilyExecutionCoordinator.ReconcileFamilyOrder(["W.World", "N.Neow+R.Relic", "Gone"], [n, r, w])
            .SequenceEqual(["W.World", "N.Neow", "R.Relic"]), "changed groups split without losing authored relative order");
        Check(FamilyPlanner.InManualOrder([n, r, w], [n, r, w], ["W.World", "Gone", "N.Neow"])
            .ExecutionOrderUnits.SequenceEqual([w, n, r]), "removed units disappear and newly participating units append");
        var v = new SimplePlannerFixture("V.VariantOpening", null, null, _ => .4);
        Check(FamilyExecutionCoordinator.ReconcileFamilyOrder(["W.World", "N.Neow"], [n, v])
            .SequenceEqual(["V.VariantOpening", "N.Neow"]), "old variant-only W position transfers to V");
        Check(FamilyExecutionCoordinator.ReconcileFamilyOrder(["N.Neow", "W.World.InitialUnknown", "R.Relic"], [r, v, n])
            .SequenceEqual(["N.Neow", "V.VariantOpening", "R.Relic"]), "old unknown-only W position transfers to V");
        Check(FamilyExecutionCoordinator.ReconcileFamilyOrder(["W.World.InitialUnknown", "N.Neow", "W.World"], [w, n, v])
            .SequenceEqual(["V.VariantOpening", "N.Neow", "W.World"]), "legacy W components transfer their earliest rank to V");
        Check(FamilyExecutionCoordinator.ReconcileFamilyOrder(["W.World", "N.Neow", "V.VariantOpening"], [v, w, n])
            .SequenceEqual(["W.World", "N.Neow", "V.VariantOpening"]), "explicit V position is preserved instead of inheriting W");
        Check(FamilyPlanner.InManualOrder([v, w, n], [v, w, n], ["W.World", "N.Neow"])
            .ExecutionOrderUnits.SequenceEqual([v, w, n]), "legacy combined W expands to adjacent V/W without moving later authored units");
        Check(FamilyPlanner.InManualOrder([v, w, n], [v, w, n], ["W.World", "N.Neow", "V.VariantOpening"])
            .ExecutionOrderUnits.SequenceEqual([w, n, v]), "explicit manual W-before-V remains effective");
        var grouped = FamilyPlanner.InManualOrder([fused, n, r, w], [n, r, w], ["N.Neow+R.Relic", "W.World"], fused);
        Check(grouped.ExecutionOrderUnits.SequenceEqual([fused, w]), "coupled units remain indivisible");
        Reject(() => FamilyPlanner.InManualOrder([fused, n, r, w], [n, r, w], ["W.World", "N.Neow+R.Relic"], fused),
            "ManualFamilyOrder.DenseOnlyUnitMustBeFirst");
        Reject(() => FamilyPlanner.InManualOrder([n, r], [n, r], ["N.Neow+R.Relic", "N.Neow"]),
            "ManualFamilyOrder.OverlappingUnits");

        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/owner-nwae-v1/context.json");
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/owner-nwae-v1/NWAE_01.json");
        var compiled = SearchCompiler.Compile(workload.Query, context.Context);
        var options = workload.Options with { ScanCount = 3, TargetMatchCount = 1, WorkerCount = 1, ManualFamilyOrder = null };
        var automaticRequest = SearchPreparationCompiler.Compile(compiled, options).Plan!;
        var automatic = FamilyExecutionCoordinator.Plan(automaticRequest, false);
        var authored = automatic.ExecutionOrderUnits.Select(FamilyExecutionCoordinator.FamilyOrderKey).Reverse().ToArray();
        var request = SearchPreparationCompiler.Compile(compiled, options with { ManualFamilyOrder = authored }).Plan!;
        var requested = authored.ToArray();
        authored[0] = "ChangedAfterCompile";
        Check(request.RunOptions.ManualFamilyOrder!.SequenceEqual(requested) && request.SnapshotFingerprint != automaticRequest.SnapshotFingerprint,
            "compiled request freezes and identifies its authored order");
        var selected = FamilyExecutionCoordinator.Plan(request, false);
        Check(selected.ExecutionOrderUnits.Select(FamilyExecutionCoordinator.FamilyOrderKey).SequenceEqual(requested),
            "production planning consumes request order instead of rerunning greedy sorting");
        var executionContext = new FilterRuntimeServices(false);
        try
        {
            var batch = new SearchBatch(0, 3);
            var output = LinearFamilyPipeline.FilterAsync(executionContext, batch,
                manual.OrderedFamilies.Select(body => body.CreateExecution!(body, executionContext)).ToArray(), default)
                .GetAwaiter().GetResult();
            Check(calls.SequenceEqual([("W.World", false), ("N.Neow", true)]) && output.Count == 3,
                "runtime pipeline invokes the manual order with canonical compact transport");
        }
        finally { executionContext.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Console.WriteLine($"ManualFamilyOrder checks={checks};PASS");
        return 0;
    }

    internal static int RunScanningSpeed()
    {
        int checks = CheckScanningSpeed();
        Console.WriteLine($"SearchScanningSpeed checks={checks};PASS");
        return 0;
    }

    internal static int CheckScanningSpeed()
    {
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("SearchScanningSpeed: " + name); }
        var speed=new RolltheSpire2.Presentation.Ui1.SearchScanningSpeed();
        Check(speed.Observe(0,0) is null && speed.Observe(10,0) is null,"Startup has no scanning speed");
        Check(speed.Observe(11,100) is null && speed.Observe(11.5,600) is null,"Active scanning warms for one second");
        Check(speed.Observe(12,1100)==1000,"One-second scanning rate excludes cold setup");
        Check(Math.Abs(speed.Observe(12.5,2100)!.Value - 2000 / 1.5) < .001,
            "Recent completed intervals smooth uneven progress without splitting their work");
        Check(speed.Observe(13,2100)==1000,"Waiting contributes elapsed wall time without fictitious roots");
        Check(speed.Observe(13.6,2100) is null,"A stalled active window cannot retain stale speed indefinitely");
        Check(speed.Observe(0,0) is null && speed.Observe(1,200) is null,"New session resets speed");
        const long mapBatch = 16 * 1024 * 1024;
        speed.Reset();
        Check(speed.Observe(50,0) is null && speed.Observe(58.6,mapBatch) is null,
            "Cold GPU preparation and the first batch do not invent a measured interval");
        for (int second = 1; second <= 8; second++)
            Check(speed.Observe(58.6 + second,mapBatch) is null,
                "Zero-progress UI polls cannot fabricate a first batch duration");
        double batchRate = mapBatch / 8.6;
        Check(Math.Abs(speed.Observe(67.2,2 * mapBatch)!.Value / batchRate - 1) < 1e-9,
            "An eight-second GPU batch uses its complete wall interval rather than a UI tick");
        for (int second = 1; second <= 8; second++)
        {
            double rate = speed.Observe(67.2 + second,2 * mapBatch)!.Value;
            Check(rate > 0 && Math.Abs(rate - mapBatch / (8.6 + second)) < .001,
                "A normal GPU batch keeps a visible rate and includes its current waiting time");
        }
        speed.Observe(75.8,3 * mapBatch);
        speed.Observe(84.4,4 * mapBatch);
        Check(Math.Abs(speed.Observe(93,5 * mapBatch)!.Value / batchRate - 1) < 1e-9,
            "Recent GPU speed retains three full completed batches");
        Check(speed.Observe(101,5 * mapBatch) is > 0 &&
            Math.Abs(speed.Observe(101,5 * mapBatch)!.Value - 3 * mapBatch / 33.8) < .001,
            "Three batch smoothing limits normal inter-batch variation without inventing progress");
        Check(speed.Observe(111,5 * mapBatch) is null,
            "No GPU progress beyond twice the recent batch interval expires the observation");
        Check(Math.Abs(speed.Observe(115,6 * mapBatch)!.Value - 3 * mapBatch / 39.2) < .001,
            "Progress after a stall retains the actual stalled wall time");
        Check(speed.Observe(116,0) is null && speed.Observe(117,100) is null,
            "Root counter regression resets an otherwise increasing session clock");
        speed.Reset();
        Check(speed.Observe(200,5 * mapBatch) is null && speed.Observe(208.6,6 * mapBatch) is > 0,
            "Reopening a running page measures new progress instead of borrowing earlier roots");
        speed.Reset();
        speed.Observe(0,1);
        bool highFrequencyCorrect = true;
        for (int tick = 1; tick <= 2000; tick++)
        {
            double? rate = speed.Observe(tick / 1000d,tick + 1);
            if (tick >= 1000) highFrequencyCorrect &= rate is { } value && Math.Abs(value - 1000) < .001;
        }
        Check(highFrequencyCorrect,"Bounded observation history still warms and measures high frequency CPU progress");
        Check(speed.Observe(double.NaN,2002) is null && speed.Observe(2.01,2011) is > 0,
            "Invalid presentation input does not corrupt the valid observation history");
        speed.Reset();
        Check(speed.Observe(5,100) is null && speed.Observe(5,200) is null && speed.Observe(6,200) is null,
            "Same-timestamp progress alone cannot establish a measured interval");
        Check(speed.Observe(7,300)==100,"Same-timestamp observations preserve roots once a real interval completes");
        var rolling=new RolltheSpire2.Presentation.Ui1.SearchScanningSpeed();
        Check(rolling.Observe(0,0) is null && rolling.Observe(3,100) is null && rolling.Observe(3.5,200) is null,"cold/warm speed waits for actual window");
        Check(Math.Abs(rolling.Observe(4.1,320)!.Value-200)<.001,"rolling speed excludes startup");
        var progress=new SearchProgressSnapshot(SearchRunState.Completed,1000,1,1000,1,10,100,SearchDisposition.Match,"",false)
            {ObservedScanningRoots=500,ObservedScanningSeconds=.1};
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress,null)==5000,"short terminal speed excludes startup");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {State=SearchRunState.Running},null) is null,"running waits for rolling window");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {State=SearchRunState.Running},12345)==12345,"running uses its recent measured rate");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {ObservedScanningRoots=0},null) is null,"no invented speed without observation");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {ObservedScanningRoots=0},12345) is null,"terminal cannot borrow stale rolling speed");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {ObservedScanningSeconds=double.NaN},12345) is null,"terminal cannot use invalid completed-interval time");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {State=SearchRunState.Cancelled},null)==5000,"cancelled session may show completed scanning evidence");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {State=SearchRunState.Faulted},null)==5000,"faulted session may show completed scanning evidence");
        return checks;
    }

    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException(name); }
        // Physical uint upload must preserve batch-local identity, including a
        // high global base and large sparse ordinals. Returned ABI1 stays ulong.
        var transportBatch = new SearchBatch(ulong.MaxValue - int.MaxValue, int.MaxValue);
        ulong[] transportOrdinals = [0, 65536, (ulong)int.MaxValue - 1];
        var transportInput = FamilyCandidateSet.FromAbi1(transportBatch, transportOrdinals);
        byte[] transportBytes = FamilyGpuComputeUtility.PackAbi1UInt32(transportInput);
        Check(transportBytes.Length == transportOrdinals.Length * sizeof(uint) &&
            FamilyGpuComputeUtility.UInt32Span(transportBytes).SequenceEqual([0u, 65536u, int.MaxValue - 1u]) &&
            transportInput.ExportAbi1().Span.SequenceEqual(transportOrdinals),
            "Compact physical packing preserves sparse local identities and original ABI1");
        Check(FamilyGpuComputeUtility.PackAbi1UInt32(FamilyCandidateSet.FromAbi1(transportBatch, [])).Length == 0,
            "Empty Compact physical packing has no synthetic ordinal");
        Check(FamilyGpuComputeUtility.UInt32Span(FamilyGpuComputeUtility.PackAbi1UInt32(transportInput, 1, 2))
                .SequenceEqual([65536u, int.MaxValue - 1u]),
            "Compact window packing preserves original ordinals without full-input staging");
        bool denseTransportRejected = false;
        try { FamilyGpuComputeUtility.PackAbi1UInt32(FamilyCandidateSet.Dense(transportBatch)); }
        catch (InvalidOperationException) { denseTransportRejected = true; }
        Check(denseTransportRejected, "Dense physical input remains implicit");
        bool unalignedTransportRejected = false;
        try { FamilyGpuComputeUtility.UInt32Span(new byte[5]); }
        catch (InvalidDataException) { unalignedTransportRejected = true; }
        Check(unalignedTransportRejected, "Span decoding preserves unaligned payload rejection");
        var allocationInput = FamilyCandidateSet.FromAbi1(new SearchBatch(0, 65536),
            Enumerable.Range(0, 65536).Select(value => (ulong)value).ToArray());
        // Warm the helper before accounting, then require a single 4-byte/ordinal
        // upload allocation rather than a uint[] plus byte[] staging pair.
        FamilyGpuComputeUtility.PackAbi1UInt32(allocationInput);
        long transportAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] allocationBytes = FamilyGpuComputeUtility.PackAbi1UInt32(allocationInput);
        long transportAllocated = GC.GetAllocatedBytesForCurrentThread() - transportAllocatedBefore;
        Check(allocationBytes.Length == 65536 * sizeof(uint) && transportAllocated < 65536L * 5,
            "Compact physical upload allocates only its final byte payload");
        var emptyParty = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)] };
        var relicKey = RolltheSpire2.Core.Identity.BaseGameModelKeys.Relics.LeafyPoultice;
        var personalNeow = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = new([relicKey], [], []) } };
        Check(!FamilyExactTimingDomains.HasQueryWork(SearchQuery.Empty), "Empty solo query cannot seed Exact timing");
        Check(!FamilyExactTimingDomains.HasQueryWork(emptyParty), "Empty party seats cannot seed shared Exact timing");
        Check(FamilyExactTimingDomains.HasQueryWork(emptyParty with { Players = [emptyParty.Players[0], emptyParty.Players[1] with { Conditions = personalNeow }] }),
            "A personal predicate retains party Exact timing");
        Check(FamilyExactTimingDomains.HasQueryWork(emptyParty with { Players = [emptyParty.Players[0] with { Offers = new([relicKey], [], []) }, emptyParty.Players[1]] }),
            "Initial offers retain party Exact timing");
        Check(FamilyExactTimingDomains.HasQueryWork(SearchQuery.Empty with { StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 1)] }),
            "Map-only queries retain their dedicated Exact timing");
        Check(NeowPhysicalPricing.StagedSetupMilliseconds(new(false,false,1))==4200 &&
            NeowPhysicalPricing.StagedSetupMilliseconds(new(true,false,1))==4200,"Public N retains both module sets");
        Check(NeowPhysicalPricing.StagedSetupMilliseconds(new(false,true,1))==1800,"Private Dense N removes existing full-donor setup component");
        Check(NeowPhysicalPricing.StagedSetupMilliseconds(new(true,true,1,true))==2400 &&
            NeowPhysicalPricing.StagedSetupMilliseconds(new(true,false,1,true))==2400,"Private Compact N retains only full donor including terminal role");
        checks += CheckScanningSpeed();
        // Real compiled work-shape facts, not relic-name lookups or runtime ratios.
        string corpus = Path.Combine(Environment.CurrentDirectory, "tools", "ProductionSearchHost", "Corpus");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(corpus, "owner-nwae-v1", "context.json"));
        NeowFamily Family(string file, int? ascension = null)
        {
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus, file));
            var source = ascension.HasValue ? context.Context with { Ascension = ascension.Value } : context.Context;
            var compiled = SearchPreparationCompiler.Compile(SearchCompiler.Compile(workload.Query, source), workload.Options);
            if (!compiled.Success || compiled.Plan is null) throw new InvalidDataException(compiled.Issue);
            return new NeowFamily(compiled.Plan.Model, NeowReplayPlan.Compile(compiled.Plan.Model, false));
        }
        Check(NeowPhysicalPricing.TryResolve(Family("n-cost-v1/CurseIdentity.json"), out var curseWork, out _), "Curse work priced");
        Check(NeowPhysicalPricing.TryResolve(Family("n-cost-v1/PositiveIdentity.json"), out var positiveWork, out _), "Positive work priced");
        Check(positiveWork.DenseNs > curseWork.DenseNs * 3 && positiveWork.CompactNs > curseWork.CompactNs,
            "Positive replay cannot inherit first-draw quote in either addressing mode");
        Check(NeowPhysicalPricing.TryResolve(Family("n-rewards-v1/Arcane-first-rank.json"), out var firstRank, out _) &&
            NeowPhysicalPricing.TryResolve(Family("n-rewards-v1/Arcane-last-rank.json"), out var lastRank, out _) && firstRank == lastRank,
            "Same physical work has same quote across target identities");
        Check(NeowPhysicalPricing.TryResolve(Family("n-cost-v1/Leafy.json"), out var leafyWork, out _), "Bounded Leafy work priced");
        Check(!NeowPhysicalPricing.TryResolve(Family("n-direct-v1/K-one.json"), out _, out _), "Unmeasured generic operator stays unknown");
        var legacyContext = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(corpus, "v1", "context.json"));
        var legacyWorkload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus, "v1", "cases", "NC-silken-cards.json"));
        var legacyRequest = SearchPreparationCompiler.Compile(SearchCompiler.Compile(legacyWorkload.Query, legacyContext.Context), legacyWorkload.Options).Plan!;
        var legacyReplay = NeowReplayPlan.Compile(legacyRequest.Model, false);
        var unfilteredQuery = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with {
            NeowRelics = new([], [new("RELIC", "UNMAPPED_N_ALLOCATION_FIXTURE")], []) } };
        var unfilteredCompiled = SearchCompiler.Compile(unfilteredQuery, legacyContext.Context);
        var unfilteredRequest = new PreparedSearch(new SearchModel(unfilteredCompiled, CompiledSearchEvaluationProjector.Project(unfilteredCompiled).Evaluation, legacyRequest.Model.CombatRewardRoutePolicy, unfilteredCompiled.SemanticFingerprint), legacyRequest.RunOptions with { SkipExactValidation = true }, legacyRequest.CanonicalStartSeed, legacyRequest.ResolvedScanCount, unfilteredCompiled.SemanticFingerprint);
        var rawPlan = FamilyExecutionCoordinator.Plan(unfilteredRequest, false);
        Check(rawPlan.Stages.Count == 0 && rawPlan.AllConditionsUnfiltered && rawPlan.FinalValidationSkipped &&
            rawPlan.FormatSummary().Contains("UnfilteredSeedEnumeration", StringComparison.Ordinal),
            "Zero filters with SkipExact explicitly describe unfiltered seed enumeration");
        var exactPlan = FamilyExecutionCoordinator.Plan(unfilteredRequest with {
            RunOptions = unfilteredRequest.RunOptions with { SkipExactValidation = false } }, false);
        Check(exactPlan.AllConditionsUnfiltered && !exactPlan.FinalValidationSkipped &&
            exactPlan.FormatSummary().Contains("DirectProductionExact", StringComparison.Ordinal),
            "No Fast filters do not claim Exact coverage or disable ordinary final validation");
        Check(legacyReplay.Selected == 255 && NeowPhysicalPricing.TryResolve(new NeowFamily(legacyRequest.Model, legacyReplay), out _, out var legacyShape) &&
            legacyShape == "N.FirstDrawOrBoundedProjection", "Legacy singleton identity receives its actual entry-mask work quote");
        var singleWorkload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus,
            "composition-holdout-v2", "Arcane-singleA-k11.json"));
        var singleRequest = SearchPreparationCompiler.Compile(
            SearchCompiler.Compile(singleWorkload.Query, context.Context), singleWorkload.Options).Plan!;
        var singlePlan = new AncientOptionFamilyPlan(singleRequest.Model);
        Check(singlePlan.ScratchWords == 3,
            "Single-option quote follows compiled generator and scratch, not Query identity");
        // Exercise actual session completion with injected invocation cleanup/recovery
        // failures. This is a lifecycle fixture, not a new registered Product allocation.
        var fixtureWorkload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus, "owner-nwae-v1", "NWAE_01.json"));
        var fixtureRequest = SearchPreparationCompiler.Compile(
            SearchCompiler.Compile(fixtureWorkload.Query, context.Context),
            fixtureWorkload.Options with { ScanCount = 67, TargetMatchCount = 1 }).Plan!;
        double aConditional = fixtureRequest.Model.Evaluation.AncientBranchConditions.Aggregate(1d, (product, row) =>
            product * AncientOptionProbabilityEstimator.EstimateConditional(SearchSelectivityInput.From(fixtureRequest), row).Probability!.Value);
        double? wConditional = WorldPhysicalPricing.ProjectOwnedSurvival(fixtureRequest.Model, aConditional);
        double nConditional = new NeowFamily(fixtureRequest.Model, NeowReplayPlan.Compile(fixtureRequest.Model, false)).Survival.SurvivalProbability!.Value;
        var fullProbability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(fixtureRequest));
        Check(wConditional is > 0 and <= 1 && fullProbability.JointlyPriced &&
            Math.Abs(nConditional * .1 * aConditional * wConditional.Value / fullProbability.Probability!.Value - 1) < 1e-10,
            "W-owned modeled projection removes A options once and reconstructs the canonical full-query model");
        foreach (string auxiliaryFault in new[] { "survival", "quote" })
        {
            var auxiliaryFamily = new LifecycleFixture(auxiliaryFault);
            var auxiliaryPlan = FamilyPlanner.Plan([auxiliaryFamily]);
            Check(auxiliaryPlan.OrderedFamilies.Single() == auxiliaryFamily &&
                (auxiliaryFault != "survival" || auxiliaryPlan.Stages.Single().Survival is null),
                "Auxiliary planning failure keeps the registered filtering obligation: " + auxiliaryFault);
        }
        var failedQuotePlan = FamilyPlanner.Plan([new LifecycleFixture("quote")]);
        var failedQuoteEta = FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(fixtureRequest, failedQuotePlan), failedQuotePlan);
        Check(failedQuoteEta.Status == FamilySearchEtaStatus.Unavailable &&
            failedQuoteEta.AcceptedResultProbability == fullProbability.Probability && failedQuoteEta.TargetFamilyPipelineMs is null,
            "Failed auxiliary unit quote preserves the query probability without inventing time");
        var observationContext = new FilterRuntimeServices(false);
        try
        {
            var observed = new LifecycleFixture("observation");
            var observedBatch = new SearchBatch(0, 3);
            var observedOutput = LinearFamilyPipeline.FilterAsync(observationContext,
                observedBatch, [observed], default).GetAwaiter().GetResult();
            Check(observedOutput.Count == 0 && observed.Calls == 1,
                "Failed performance observation does not discard a valid filtering result");
        }
        finally { observationContext.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        foreach (bool legacyRecoveryFlag in new[] { false, true })
        foreach (string fault in new[] { "none", "cleanup", "recovery", "evidence", "calibration" })
        {
            var invocation = new LifecycleFixture(fault);
            var quotedPlan = FamilyPlanner.InOrder([invocation]);
            var eta = FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(fixtureRequest, quotedPlan), quotedPlan);
            Check(eta.TargetMeanMs is null && eta.TargetFamilyPipelineMs is null,
                "Unmeasured lifecycle fixture never invents an ETA");
            var session = new ProductionSearchSession(fixtureRequest, quotedPlan, eta, 64, 8, null, "", legacyRecoveryFlag);
            session.Completion.GetAwaiter().GetResult();
            Check(ReferenceEquals(eta, session.EtaProjection),
                "Session does not learn/reprice ETA from runtime observations");
            var progress = session.GetProgress();
            Check(progress.State == (fault == "recovery" ? SearchRunState.Faulted : SearchRunState.Completed),
                "Physical lifecycle faults remain distinct from optional evidence failures: " + fault);
            if (fault == "none") Check(progress.ScannedCount == 67 && invocation.Calls == 2, "Zero output and tail commit");
            if (fault == "cleanup") Check(progress.CleanupIssue.Contains("InjectedCleanupFailure", StringComparison.Ordinal) && progress.FailureCode.Length == 0,
                "Cleanup failure retains calculation completion and original cause separately");
            if (fault == "recovery") Check(progress.FailureCode.Contains("PhysicalRecoveryRejected", StringComparison.Ordinal), "Recovery observation rejected before Exact");
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        CheckCandidateValidationPublication(context.Context, Check);
        checks += CheckUnifiedPartyPlanning();
        checks += CheckPartyCpuNeowQuoteIdentity();
        checks += CheckPartyFilterRemainders();
        checks += CheckStartupFilterDisclosure();
        checks += CheckSimplePlanning();
        checks += CheckDenseAcceleratorReachability();
        checks += CheckBonesHiddenUpgradeSelections();
        checks += CheckCapsuleSparseSelections();
        Console.WriteLine($"FamilyAllocationPricing checks={checks};PASS");
        return 0;
    }
    private static int CheckSimplePlanning()
    {
        int checks = 0;
        void Check(bool pass, string name) { checks++; if (!pass) throw new InvalidOperationException("SimplePlanning:" + name); }
        var n = new SimplePlannerFixture("N", 1, 100, _ => 1e-10);
        var r = new SimplePlannerFixture("R", 3, null, _ => .5);
        var sparse = FamilyPlanner.PlanSimple([r, n]);
        Check(sparse.OrderedFamilies.SequenceEqual(new FilterBodyDescription[] { n, r }),
            "Known Dense N entry survives an unpriced Compact R suffix");
        Check(sparse.AllowSmallCandidateExact && sparse.SelectionPolicyId == FamilyPlanner.SelectionPolicyId &&
            sparse.ExpectedFamilyPipelineMsPerRoot is null,
            "Greedy planning keeps the complete pipeline price Unknown without enumerating orders");
        Check(sparse.Stages[0].PerInputTimeMs.HasValue && sparse.Stages[1].PerInputTimeMs is null &&
            sparse.MissingEvidence.Contains("FamilyHardwarePerformanceUnavailable:R:CompactAbi1"),
            "Missing suffix evidence stays scoped to the actual Compact stage");
        Check(n.QuotedPopulations.All(population => population == PrivateOrdinalBuffer.Capacity) &&
            r.QuotedPopulations.All(population => population == PrivateOrdinalBuffer.Capacity),
            "Simple reference use does not demand prices for a modeled tiny downstream population");

        var a = new SimplePlannerFixture("A", 1, 30, _ => .1);
        var b = new SimplePlannerFixture("B", 2, 20, _ => .1);
        var c = new SimplePlannerFixture("C", 3, 1, _ => .1);
        Check(FamilyPlanner.PlanSimple([b, c, a]).OrderedFamilies.SequenceEqual(new FilterBodyDescription[] { a, c, b }),
            "Dense first and Compact suffix use their own unit costs");
        var localFirst = new SimplePlannerFixture("LocalFirst", 2, 1, _ => .5);
        var wholeChainFirst = new SimplePlannerFixture("WholeChainFirst", 3, 100, _ => .5);
        Check(FamilyPlanner.PlanSimple([wholeChainFirst, localFirst]).OrderedFamilies.First() == localFirst,
            "Simple Dense score is not overridden by an all-chain time comparison");

        var prefix = new SimplePlannerFixture("Prefix", .1, .1, _ => .1);
        var conditional = new SimplePlannerFixture("Conditional", 1, 1, passed => passed.Contains("Prefix") ? .99 : .01);
        var independent = new SimplePlannerFixture("Independent", 2, 2, _ => .5);
        var conditionalPlan = FamilyPlanner.PlanSimple([conditional, independent, prefix]);
        Check(conditionalPlan.OrderedFamilies.SequenceEqual(new FilterBodyDescription[] { prefix, independent, conditional }) &&
            conditionalPlan.Stages[2].Survival == .99 &&
            conditional.PassedCoverage.Any(passed => passed.SetEquals(new[] { "Prefix", "Independent" })),
            "Greedy selection and final stage projections use the selected passed Coverage");

        var unknown = new SimplePlannerFixture("Unknown", null, null, _ => null);
        var alsoUnknown = new SimplePlannerFixture("AlsoUnknown", null, null, _ => null);
        var unknownPlan = FamilyPlanner.PlanSimple([unknown, alsoUnknown]);
        Check(unknownPlan.OrderedFamilies.SequenceEqual(new FilterBodyDescription[] { unknown, alsoUnknown }) &&
            unknownPlan.Stages.All(stage => stage.Survival is null && stage.PerInputTimeMs is null) &&
            unknownPlan.ExpectedFamilyPipelineMsPerRoot is null && unknownPlan.EstimatedTerminalSurvival is null,
            "Unresolved units retain every original filter and preserve Unknown");
        var analyticalSlow = new SimplePlannerFixture("AnalyticalSlow", null, null, _ => .5, expectedWork: 10);
        var pricedLater = new SimplePlannerFixture("PricedLater", .01, .01, _ => .1);
        Check(FamilyPlanner.PlanSimple([unknown, pricedLater]).OrderedFamilies.First() == unknown,
            "Missing evidence retains its stable boundary instead of becoming an infinite cost");
        var knownBefore = new SimplePlannerFixture("KnownBefore", 10, 10, _ => .5);
        Check(FamilyPlanner.PlanSimple([knownBefore, unknown, pricedLater]).OrderedFamilies.SequenceEqual(
            new FilterBodyDescription[] { knownBefore, unknown, pricedLater }),
            "Known units do not jump an unresolved original obligation; no time or survival is invented");
        var analyticalFast = new SimplePlannerFixture("AnalyticalFast", null, null, _ => .1, expectedWork: 1);
        Check(FamilyPlanner.PlanSimple([analyticalSlow, analyticalFast]).OrderedFamilies.First() == analyticalFast,
            "Unpriced hardware may use same-unit analytical work without inventing milliseconds");

        var transform = new SimplePlannerFixture("T", 2, 2, _ => .1);
        var neow = new SimplePlannerFixture("N", 2, 2, _ => .1);
        var fused = new SimplePlannerFixture("NT", .5, .5, _ => .01, coverage: ["N", "T"]);
        var fusedPlan = FamilyPlanner.PlanSimple([neow, transform], [fused]);
        Check(fusedPlan.OrderedFamilies.SequenceEqual(new FilterBodyDescription[] { fused }) &&
            fusedPlan.Stages.SelectMany(stage => stage.Coverage).Order().SequenceEqual(new[] { "N", "T" }),
            "An existing fused whole unit competes by the same score and replaces its entire equivalent Coverage");
        var expensiveFusion = new SimplePlannerFixture("NT", 100, 100, _ => .01, coverage: ["N", "T"]);
        Check(FamilyPlanner.PlanSimple([neow, transform], [expensiveFusion]).OrderedFamilies.Count == 2,
            "Selecting a constituent removes an overlapping fused alternative without omitting its other obligation");
        bool rejectedCoverage = false;
        try { _ = FamilyPlanner.PlanSimple([neow, transform], [new SimplePlannerFixture("NX", 1, 1, _ => .1, coverage: ["N", "X"])]); }
        catch (InvalidOperationException error) when (error.Message == "SimplePlanning.FusedCoverageMustReplaceRegisteredObligations")
        { rejectedCoverage = true; }
        Check(rejectedCoverage, "A fused alternative cannot add or substitute authored Coverage");

        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        PreparedSearch Request(SearchQuery query, bool skip = false) => SearchPreparationCompiler.Compile(
            SearchCompiler.Compile(query, context), new("000000000000", 1, 1, 1) { SkipExactValidation = skip }).Plan
            ?? throw new InvalidOperationException("SimplePlanning.RequestCompile");
        var request = Request(SearchQuery.Empty);
        FilterBodyDescription Cpu(SimplePlannerFixture owner, string revision,
            Func<FamilyPhysicalQuoteRequest, FamilyPhysicalQuote?> quote) => owner.Body with
            { UsesGpu = false, PhysicalRevision = revision, WindowCapacity = 65536,
                NumericRequirements = GpuNumericRequirements.None,
                PerformanceProjection = compact => CpuFilterExecution.Describe(owner.FamilyId, revision, 1, 65536, false, false, compact),
                Cost = new() { Quote = (_, geometry, _) => quote(geometry) } };
        FamilyPhysicalQuote CpuQuote(FilterBodyDescription owner, double ns) =>
            new(owner.FamilyId, "SimpleCpuAlternative", ns, 65536, null, "SimpleCpuAlternative",
                PublicTransportClass: "CpuOrderedAbi1");
        var gpuOwner = new SimplePlannerFixture("CpuPreferred", 1000, 1000, _ => .5, gpu: true);
        var fasterCpu = Cpu(gpuOwner, "SimpleCpuPreferred", _ => CpuQuote(gpuOwner, 1));
        gpuOwner.CpuAlternatives = [fasterCpu];
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var cpuSelected = FamilyPlanner.PlanSimple([gpuOwner], includeCpuAlternatives: true);
        Check(cpuSelected.OrderedFamilies.Single() == fasterCpu && cpuSelected.Stages[0].PerInputTimeMs.HasValue,
            "GPU availability retains a cheaper independent CPU realization in the same greedy step");
        var unpricedInteger = new SimplePlannerFixture("IntegerGpuUnpriced", null, null, _ => .5, gpu: true,
            numericRequirements: GpuNumericRequirements.Int64);
        unpricedInteger.CpuAlternatives = [Cpu(unpricedInteger, "KnownCpuForUnpricedInteger", _ => CpuQuote(unpricedInteger, 1))];
        var integerSelected = FamilyPlanner.PlanSimple([unpricedInteger], includeCpuAlternatives: true);
        Check(integerSelected.OrderedFamilies.Single() == unpricedInteger && integerSelected.Stages[0].PerInputTimeMs is null &&
            ((FilterBodyDescription)unpricedInteger).GpuNumericRequirements == GpuNumericRequirements.Int64,
            "A known CPU price cannot displace an executable unpriced integer GPU body");

        var conditionalOwner = new SimplePlannerFixture("ConditionalCpu", 1000, 1000, _ => .5, gpu: true);
        var conditionalCpu = Cpu(conditionalOwner, "SimpleConditionalCpu", _ => CpuQuote(conditionalOwner, 1));
        conditionalCpu = conditionalCpu with { RequiredPassedCoverage = new HashSet<string> { "CpuPrefix" } };
        conditionalOwner.CpuAlternatives = [conditionalCpu];
        Check(FamilyPlanner.PlanSimple([conditionalOwner], includeCpuAlternatives: true).OrderedFamilies.Single() == conditionalOwner,
            "A cheap conditional CPU quote cannot bypass its RequiredPassedCoverage");
        var cpuPrefix = new SimplePlannerFixture("CpuPrefix", .01, .01, _ => .1);
        var conditionalCpuPlan = FamilyPlanner.PlanSimple([conditionalOwner, cpuPrefix], includeCpuAlternatives: true);
        Check(conditionalCpuPlan.OrderedFamilies.SequenceEqual(new FilterBodyDescription[] { cpuPrefix, conditionalCpu }) &&
            conditionalCpuPlan.Stages[1].CompactAbi1Input && conditionalCpuPlan.Stages[1].PerInputTimeMs.HasValue,
            "A CPU alternative becomes eligible only after its exact passed Coverage in the Compact role");
        var denseOnlyOwner = new SimplePlannerFixture("DenseOnlyCpu", 1000, 1000, _ => .5, gpu: true);
        denseOnlyOwner.CpuAlternatives = [Cpu(denseOnlyOwner, "SimpleDenseOnlyCpu", geometry =>
            geometry.CompactInput ? null : CpuQuote(denseOnlyOwner, 1))];
        Check(FamilyPlanner.PlanSimple([denseOnlyOwner, cpuPrefix], includeCpuAlternatives: true).OrderedFamilies.Last() == denseOnlyOwner,
            "A known Dense CPU quote does not certify the actual Compact suffix role");
        var unpricedCompactGpu = new SimplePlannerFixture("CompactGpuUnpriced", 1000, null, _ => .5, gpu: true);
        unpricedCompactGpu.CpuAlternatives = [Cpu(unpricedCompactGpu, "KnownCompactCpu", _ => CpuQuote(unpricedCompactGpu, 1))];
        var compactUnknownPlan = FamilyPlanner.PlanSimple([unpricedCompactGpu, cpuPrefix], includeCpuAlternatives: true);
        Check(compactUnknownPlan.OrderedFamilies.Last() == unpricedCompactGpu && compactUnknownPlan.Stages[1].PerInputTimeMs is null,
            "A known GPU Dense price cannot justify CPU replacement when its actual Compact price is Unknown");
        var unpricedCpuOwner = new SimplePlannerFixture("UnpricedCpu", null, null, _ => null, gpu: true);
        unpricedCpuOwner.CpuAlternatives = Enumerable.Range(0, 3).Select(index =>
            (FilterBodyDescription)Cpu(unpricedCpuOwner, "SimpleUnpricedCpu" + index, _ => null)).ToArray();
        Check(FamilyPlanner.PlanSimple([unpricedCpuOwner], includeCpuAlternatives: true).OrderedFamilies.Single() == unpricedCpuOwner,
            "Adding unpriced CPU versions keeps the original Unknown-cost fallback stable");
        Check(FamilyExecutionCoordinator.Plan(request, false).AllowSmallCandidateExact &&
            FamilyExecutionCoordinator.Plan(request, true, gpuFloat64Available: false).AllowSmallCandidateExact,
            "The real default entry uses simple planning on CPU and a restricted numerical backend");
        Check(FamilyExecutionCoordinator.Plan(Request(SearchQuery.Empty, skip: true), false).AllowSmallCandidateExact,
            "SkipExact uses the same simple planner while the session controls Exact eligibility");
        Check(FamilyExecutionCoordinator.Plan(Request(SearchQuery.Empty with {
            StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 1)] }), false).AllowSmallCandidateExact,
            "Map queries use the same simple planner");
        var partyContext = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
            .Invoke(null, [context, 2, null, null])!;
        var partyRequest = SearchPreparationCompiler.Compile(SearchCompiler.Compile(SearchQuery.Empty with {
            Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)] }, partyContext),
            new("000000000000", 1, 1, 1)).Plan ?? throw new InvalidOperationException("SimplePlanning.PartyRequestCompile");
        Check(FamilyExecutionCoordinator.Plan(partyRequest, false).AllowSmallCandidateExact,
            "Party queries use the same simple planner");
        string? oldControl = Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT");
        try
        {
            Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", "baseline");
            bool unavailable = false;
            try { _ = FamilyExecutionCoordinator.Plan(request, false); }
            catch (InvalidOperationException error) when (error.Message == "ExplicitPhysicalSelectionGpuUnavailable:RequestedGpuNumericalBodyUnavailable")
            { unavailable = true; }
            Check(unavailable, "An explicit numerical experiment reports the unavailable GPU body at its actual scope");
            Check(!FamilyExecutionCoordinator.Plan(request, true).AllowSmallCandidateExact,
                "An available explicit numerical experiment selects its direct control path");
        }
        finally { Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", oldControl); }
        return checks;
    }

    private static int CheckDenseAcceleratorReachability()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new InvalidOperationException("DenseAccelerator:" + reason); }
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string[] controls = ["RT2_BONES_CAPSULE_BOUNDARY", "RT2_DIRECT_CAPSULE_EXPERIMENT", "RT2_N_DIRECT_EXPERIMENT", "RT2_CAPSULE_SPARSE_REFINE"];
        var previous = controls.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (string name in controls) Environment.SetEnvironmentVariable(name, null);
            SearchPerformanceProfileFoundation.ObserveRenderingBackend("d3d12");
            foreach (var fixture in new[] {
                (Context: "n-direct-v1/context-defect-a10.json", Workload: "bones-k-sameroute-v1/scoped-both-route.json", Bones: true),
                (Context: "v1/context.json", Workload: "direct-capsule-v1/Small-C-check.json", Bones: false) })
            {
                var source = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/" + fixture.Context).Context;
                var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + fixture.Workload);
                var request = SearchPreparationCompiler.Compile(SearchCompiler.Compile(workload.Query, source), workload.Options).Plan
                    ?? throw new InvalidOperationException("DenseAccelerator.Compile");
                string bonesRevision = BonesCapsuleAllocation.LocalRevision + (BonesHiddenUpgradeContinuation.CaptureMode() switch {
                    0 => "", 1 => BonesHiddenUpgradeContinuation.Revision + ".direct",
                    2 => BonesHiddenUpgradeContinuation.Revision + ".branches",
                    3 => BonesHiddenUpgradeContinuation.Revision + ".direct-first",
                    _ => throw new InvalidOperationException("DenseAccelerator.UnknownHiddenUpgradeMode") });
                if (NeowReplayPlan.Compile(request.Model, false).CapsuleNicheContinuation is not null)
                    bonesRevision += NeowCapsuleNicheContinuation.Revision;
                foreach (bool skip in new[] { false, true })
                {
                    var selected = FamilyExecutionCoordinator.Plan(request with { RunOptions = request.RunOptions with { SkipExactValidation = skip } }, true);
                    var first = selected.Stages.First();
                    Check(first.Family.UsesGpu && first.Family.InputModes == FilterInputModes.Dense &&
                        (fixture.Bones ? first.ConditionPerformance.PhysicalImplementationRevision == bonesRevision && first.Family.PhysicalRevision == bonesRevision :
                        new[] { "fused", "rarity-serial", "entry-rarity-serial" }.Any(mode =>
                            first.Family.PhysicalRevision == DirectCapsuleInvocation.PhysicalRevision(mode))),
                        "Ordinary greedy retains the existing coupled Dense accelerator:" + fixture.Workload);
                    Check(!first.CompactAbi1Input && first.Coverage.ToHashSet().SetEquals(["N.Neow", "R.Relic"]) &&
                        selected.AllowSmallCandidateExact && selected.FinalValidationSkipped == skip,
                        "Coupled entry retains all same-route obligations and requested Exact mode");
                }
                var restricted = FamilyExecutionCoordinator.Plan(request, true, gpuFloat64Available: false);
                Check(restricted.OrderedFamilies.All(family => family.InputModes != FilterInputModes.Dense),
                    "Restricted backend never reconstructs an unavailable native coupled body");
                string control = fixture.Bones ? "RT2_BONES_CAPSULE_BOUNDARY" : "RT2_DIRECT_CAPSULE_EXPERIMENT";
                Environment.SetEnvironmentVariable(control, fixture.Bones ? "local" : "fused");
                var explicitPlan = FamilyExecutionCoordinator.Plan(request, true);
                Check(!explicitPlan.AllowSmallCandidateExact && explicitPlan.RankingAuthority == FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
                    "Explicit numerical trial directly selects its body without an early Exact policy");
                Environment.SetEnvironmentVariable(control, null);
            }
        }
        finally
        {
            foreach (string name in controls) Environment.SetEnvironmentVariable(name, previous[name]);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity.Length == 0 ? "Unknown" : oldDevice.GpuIdentity,
                oldDevice.RenderingBackend.Length == 0 ? "unknown" : oldDevice.RenderingBackend);
        }
        return checks;
    }

    private static int CheckBonesHiddenUpgradeSelections()
    {
        int checks = 0;
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException("BonesHiddenSelection:" + reason); }
        string[] controls = ["RT2_BONES_HIDDEN_UPGRADES", "RT2_BONES_CAPSULE_BOUNDARY", "RT2_DIRECT_CAPSULE_EXPERIMENT", "RT2_N_DIRECT_EXPERIMENT", "RT2_CAPSULE_SPARSE_REFINE"];
        var previous = controls.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        var source = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/n-direct-v1/context-defect-a10.json").Context;
        var template = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/bones-k-sameroute-v1/counterexample.json");
        PreparedSearch Request(SearchQuery query) => SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, source),
            template.Options with { ScanCount = 67, WorkerCount = 1 }).Plan ?? throw new InvalidOperationException("BonesHiddenSelection.Compile");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var simple = typeof(FamilyExecutionCoordinator).GetMethod("PlanSimpleSingleplayer", flags)!;
        try
        {
            foreach (string control in controls) Environment.SetEnvironmentVariable(control, null);
            SearchPerformanceProfileFoundation.ObserveRenderingBackend("d3d12");
            var pinned = template.Query with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                [BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.Kaleidoscope], BonesRouteOrderMode.ExactOrder) };
            var full = pinned with { StructuredOpeningEffects = pinned.StructuredOpeningEffects.Select(c =>
                c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule ? c with {
                    OutputKeys = [new("RELIC", "ANCHOR"), BaseGameModelKeys.OrdinaryRelics.WarPaint] } : c).ToArray() };
            foreach (var mode in new[] { "direct", "branches", "direct-first" })
            foreach (var (shape, query) in new[] { ("any-partial", template.Query), ("pinned-partial", pinned), ("pinned-full", full) })
            {
                Environment.SetEnvironmentVariable("RT2_BONES_HIDDEN_UPGRADES", mode);
                Environment.SetEnvironmentVariable("RT2_BONES_CAPSULE_BOUNDARY", null);
                var request = Request(query);
                var replay = NeowReplayPlan.Compile(request.Model, false);
                Check(replay.CapsuleNicheContinuation is not null, mode + ":" + shape + ":shared continuation prepared");
                string expectedCpu = "N.Neow.Cpu.LocalReplay.20261015.v3" + NeowIdentityOfferFacts.Revision +
                    NeowCapsuleNicheContinuation.Revision;
                string expectedDefaultCpu = CpuFilterExecution.Describe("N.Neow", expectedCpu, 1,
                    CpuFilterExecution.Capacity, false, false, false).PhysicalImplementationRevision;
                var nativeOwner = new NeowFamily(request.Model, replay);
                var integerOwner = new NeowFamily(request.Model, replay, gpuPlanFactory: NeowFamilyGpuPlan.TryCreateIntegerUnitFloat);
                string expectedNative = nativeOwner.ConditionPerformance.PhysicalImplementationRevision;
                string expectedInteger = integerOwner.ConditionPerformance.PhysicalImplementationRevision;
                var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, true);
                var native = registered.Single(b => b.FamilyId == "N.Neow");
                Check(native.UsesGpu && native.PhysicalRevision == expectedNative &&
                    nativeOwner.GpuPlan is { CapsuleNicheMetaOffset: >= 0 } && integerOwner.GpuPlan is { CapsuleNicheMetaOffset: >= 0 } &&
                    native.CpuAlternatives.Any(b => !b.UsesGpu && b.PhysicalRevision == expectedCpu),
                    mode + ":" + shape + ":native integer and CPU share corrected continuation");
                void CorrectedStandalone(FamilyExecutionPlan plan, string path, bool integer = false)
                {
                    var n = plan.ExecutionOrderUnits.Where(b => b.Coverage.Contains("N.Neow")).ToArray();
                    Check(n is [var body] &&
                        body.Coverage.SequenceEqual(["N.Neow"]) &&
                        (body.UsesGpu ? body.PhysicalRevision == (integer ? expectedInteger : expectedNative) &&
                            body.ConditionPerformance.PhysicalImplementationRevision == body.PhysicalRevision :
                            (body.PhysicalRevision == expectedCpu && body.WindowCapacity == 65536 ||
                             body.PhysicalRevision == expectedDefaultCpu && body.WindowCapacity == CpuFilterExecution.Capacity)) &&
                        (!integer || (body.NumericRequirements & GpuNumericRequirements.Float64) == 0),
                        mode + ":" + shape + ":" + path + ":exact corrected standalone body");
                }
                var dense = FamilyExecutionCoordinator.Plan(request, true, true);
                Check(dense.OrderedFamilies.First().PhysicalRevision == BonesCapsuleAllocation.LocalRevision +
                    BonesHiddenUpgradeContinuation.Revision + "." + mode +
                    (shape == "pinned-full" ? BonesHiddenUpgradeContinuation.KnownFullGpuRevision : "") + NeowCapsuleNicheContinuation.Revision &&
                    dense.OrderedFamilies.First().Coverage.ToHashSet().SetEquals(["N.Neow", "R.Relic"]),
                    mode + ":" + shape + ":coupled Dense remains available");
                Environment.SetEnvironmentVariable("RT2_BONES_CAPSULE_BOUNDARY", "ordinary");
                CorrectedStandalone(FamilyExecutionCoordinator.Plan(request, true, true), "ordinary");
                Environment.SetEnvironmentVariable("RT2_BONES_CAPSULE_BOUNDARY", null);
                var manual = FamilyExecutionCoordinator.Plan(request with { RunOptions = request.RunOptions with {
                    ManualFamilyOrder = ["R.Relic", "N.Neow"] } }, true, true);
                CorrectedStandalone(manual, "manual R-before-N");
                Check(manual.ExecutionOrderUnits.Select(b => b.FamilyId).SequenceEqual(["R.Relic", "N.Neow"]),
                    mode + ":" + shape + ":manual order preserved");
                CorrectedStandalone(FamilyExecutionCoordinator.Plan(request, true, false), "noFP64", integer: true);
                CorrectedStandalone(FamilyExecutionCoordinator.Plan(request, false), "CPU-only");
                var blocked = registered.Select(b => b.FamilyId == "R.Relic" ? b with {
                    Preparation = b.Preparation with { Issues = [new("GpuPreparation", "InjectedFailure", "Dense entry unavailable fixture")] } } : b).ToArray();
                CorrectedStandalone((FamilyExecutionPlan)simple.Invoke(null, [request, blocked, true, true])!, "Dense preparation unavailable");
            }
            Environment.SetEnvironmentVariable("RT2_BONES_HIDDEN_UPGRADES", "legacy");
            Check(NeowReplayPlan.Compile(Request(template.Query).Model, false).CapsuleNicheContinuation is null,
                "Legacy control retains its original donor identity");
        }
        finally
        {
            foreach (string name in controls) Environment.SetEnvironmentVariable(name, previous[name]);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity.Length == 0 ? "Unknown" : oldDevice.GpuIdentity,
                oldDevice.RenderingBackend.Length == 0 ? "unknown" : oldDevice.RenderingBackend);
        }
        return checks;
    }

    private static int CheckCapsuleSparseSelections()
    {
        int checks = 0;
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException("CapsuleSparseSelection:" + reason); }
        string[] controls = ["RT2_CAPSULE_SPARSE_REFINE", "RT2_BONES_HIDDEN_UPGRADES", "RT2_BONES_CAPSULE_BOUNDARY", "RT2_N_DIRECT_EXPERIMENT", "RT2_DIRECT_CAPSULE_EXPERIMENT"];
        var previous = controls.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            foreach (string name in controls) Environment.SetEnvironmentVariable(name, null);
            SearchPerformanceProfileFoundation.ObserveRenderingBackend("d3d12");
            Check(NeowCapsuleSparseRefinement.CaptureSelection() == "auto" && !NeowCapsuleSparseRefinement.CaptureMode(),
                "default is auto and needs a matching plan");
            foreach (string mode in new[] { "", "auto", "off", "on" })
            {
                Environment.SetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE", mode);
                Check(NeowCapsuleSparseRefinement.CaptureSelection() == (mode.Length == 0 ? "auto" : mode) &&
                    NeowCapsuleSparseRefinement.CaptureMode() == (mode == "on"), "explicit selector:" + mode);
            }
            Environment.SetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE", "invalid");
            bool rejected = false;
            try { _ = NeowCapsuleSparseRefinement.CaptureMode(); }
            catch (InvalidOperationException error) when (error.Message == "N.UnknownCapsuleSparseRefineMode:invalid") { rejected = true; }
            Check(rejected, "invalid selector reports its cause");
            Environment.SetEnvironmentVariable("RT2_BONES_HIDDEN_UPGRADES", "branches");
            var source = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/n-direct-v1/context-defect-a10.json").Context;
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/bones-k-sameroute-v1/counterexample.json");
            var options = workload.Options with { ScanCount = 67, WorkerCount = 1 };
            var bones = BaseGameModelKeys.Relics.NeowsBones;
            var small = BaseGameModelKeys.Relics.SmallCapsule;
            var large = BaseGameModelKeys.Relics.LargeCapsule;
            var leaf = BaseGameModelKeys.Relics.NewLeaf;
            var leafRow = new NeowStructuredEffectSearchCondition(leaf, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card, [new("CARD", "CHILL")]);
            SearchQuery AutoQuery(IReadOnlyList<ModelKey> pair,
                IReadOnlyList<NeowStructuredEffectSearchCondition> rows, BonesRouteOrderMode order = BonesRouteOrderMode.AnyOrder,
                bool finalCurse = false) => SearchQuery.Empty with {
                    OpeningRoute = new(bones), OpeningRouteRelicRequirement = pair.Count == 0 ? null : new(bones, pair, order),
                    StructuredOpeningEffects = rows, LegacyNeow = SearchQuery.Empty.LegacyNeow with {
                        RequiredFinalCurse = finalCurse ? source.Authority.EffectAuthority!.GeneratedCursePool!.First() : null } };
            var autoCases = new List<(string Name, SearchQuery Query, bool Enabled)>();
            foreach (var (orderName, order, reverse) in new[] {
                ("any", BonesRouteOrderMode.AnyOrder, false), ("pinned", BonesRouteOrderMode.ExactOrder, false),
                ("reverse", BonesRouteOrderMode.ExactOrder, true) })
            {
                autoCases.Add(("small-leaf-" + orderName, AutoQuery(reverse ? [leaf, small] : [small, leaf], [leafRow], order), true));
                autoCases.Add(("dual-final-" + orderName, AutoQuery(reverse ? [large, small] : [small, large], [], order, finalCurse: true), true));
            }
            autoCases.Add(("small-k", AutoQuery([small, BaseGameModelKeys.Relics.Kaleidoscope],
                workload.Query.StructuredOpeningEffects.Where(c => c.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope).ToArray()), false));
            autoCases.Add(("wide-bones", AutoQuery([], [leafRow]), false));
            // A source-scoped NewLeaf row itself requires that Bones source;
            // together with Small it determines the complete unordered pair.
            autoCases.Add(("small-plus-implied-leaf-source", AutoQuery([small], [leafRow]), true));
            autoCases.Add(("one-required-bones-relic", AutoQuery([small], [], finalCurse: true), false));
            autoCases.Add(("small-leaf-no-observation", AutoQuery([small, leaf], []), false));
            autoCases.Add(("dual-no-final-curse", AutoQuery([small, large], []), false));
            autoCases.Add(("large-leaf", AutoQuery([large, leaf], [leafRow]), false));
            foreach (var fixture in autoCases)
            {
                var prepared = SearchPreparationCompiler.Compile(SearchCompiler.Compile(fixture.Query, source), options).Plan
                    ?? throw new InvalidOperationException("CapsuleSparseSelection.AutoCompile:" + fixture.Name);
                var n = NeowReplayPlan.Compile(prepared.Model, false);
                var pref = FilterExecutionPreferences.From(prepared.RunOptions);
                Environment.SetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE", "off");
                var off = new NeowFamily(prepared.Model, n);
                string dense = off.ConditionPerformance.PhysicalImplementationRevision;
                string compact = off.ResolveConditionPerformance(true).PhysicalImplementationRevision;
                string cpuOff = new NeowFamily(prepared.Model, n, preferGpu: false).ConditionPerformance.PhysicalImplementationRevision;
                foreach (string? mode in new string?[] { null, "", "auto" })
                {
                    Environment.SetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE", mode);
                    Check(NeowCapsuleSparseRefinement.CaptureSelection() == "auto" &&
                        NeowCapsuleSparseRefinement.ShouldApply("auto", n) == fixture.Enabled &&
                        NeowCapsuleSparseRefinement.CaptureMode(n) == fixture.Enabled,
                        fixture.Name + ":canonical auto selector and gate:" + (mode ?? "unset"));
                    var owner = new NeowFamily(prepared.Model, n); var body = owner.Describe(pref);
                    string suffix = fixture.Enabled ? NeowCapsuleSparseRefinement.Revision : "";
                    Check(body.UsesGpu && body.PhysicalRevision == dense + suffix &&
                        owner.ResolveConditionPerformance(true).PhysicalImplementationRevision == compact + suffix,
                        fixture.Name + ":exact auto dense/compact identity");
                    Check(fixture.Enabled ? body.Cost.PrivatePhysicalRevision?.Invoke(false) == dense + NeowCapsuleSparseRefinement.PrivateRevision &&
                        body.Cost.PrivatePhysicalRevision?.Invoke(true) == compact + NeowCapsuleSparseRefinement.PrivateRevision :
                        body.Cost.PrivatePhysicalRevision is null, fixture.Name + ":private union identity follows gate");
                    string cpuExpected = fixture.Enabled ? CpuFilterExecution.Describe("N.Neow",
                        "N.Neow.Cpu.LocalReplay.20261015.v3" + NeowIdentityOfferFacts.Revision +
                        NeowCapsuleNicheContinuation.Revision + NeowCapsuleSparseRefinement.Revision,
                        1, CpuFilterExecution.Capacity, false, false, false).PhysicalImplementationRevision : cpuOff;
                    Check(new NeowFamily(prepared.Model, n, preferGpu: false).ConditionPerformance.PhysicalImplementationRevision == cpuExpected,
                        fixture.Name + ":exact auto CPU identity");
                }
            }
            var query = workload.Query with { StructuredOpeningEffects = workload.Query.StructuredOpeningEffects
                .Where(c => c.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope).ToArray() };
            var request = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, source), options).Plan
                ?? throw new InvalidOperationException("CapsuleSparseSelection.Compile");
            var replay = NeowReplayPlan.Compile(request.Model, false);
            Check(replay.CapsuleNicheContinuation is not null, "shared union remains present");
            var preferences = FilterExecutionPreferences.From(request.RunOptions);
            foreach (bool integer in new[] { false, true })
            {
                NeowFamily Owner(bool gpu = true) => new(request.Model, replay, preferGpu: gpu,
                    gpuPlanFactory: integer ? NeowFamilyGpuPlan.TryCreateIntegerUnitFloat : NeowFamilyGpuPlan.TryCreate);
                Environment.SetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE", "off");
                var off = Owner(); var offBody = off.Describe(preferences);
                string dense = off.ConditionPerformance.PhysicalImplementationRevision;
                string compact = off.ResolveConditionPerformance(true).PhysicalImplementationRevision;
                Check(offBody.UsesGpu && offBody.Cost.PrivatePhysicalRevision is null, "off preserves ordinary body:" + integer);
                Environment.SetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE", "on");
                var on = Owner(); var onBody = on.Describe(preferences);
                Check(onBody.PhysicalRevision == dense + NeowCapsuleSparseRefinement.Revision &&
                    on.ResolveConditionPerformance(true).PhysicalImplementationRevision == compact + NeowCapsuleSparseRefinement.Revision,
                    "public dense/compact exact body identity:" + integer);
                Check(on.GpuPlan!.Meta.SequenceEqual(off.GpuPlan!.Meta) && on.GpuPlan.Conditions.SequenceEqual(off.GpuPlan.Conditions),
                    "GPU numerical payload unchanged:" + integer);
                Check(onBody.Cost.PrivatePhysicalRevision?.Invoke(false) == dense + NeowCapsuleSparseRefinement.PrivateRevision &&
                    onBody.Cost.PrivatePhysicalRevision?.Invoke(true) == compact + NeowCapsuleSparseRefinement.PrivateRevision,
                    "private retains exact union body identity:" + integer);
                Check(integer ? onBody.BindPrivate is null && onBody.BindPrivateShared is null :
                    onBody.BindPrivate is not null && onBody.BindPrivateShared is not null, "private availability unchanged:" + integer);
                string cpuBase = "N.Neow.Cpu.LocalReplay.20261015.v3" + NeowIdentityOfferFacts.Revision +
                    NeowCapsuleNicheContinuation.Revision + NeowCapsuleSparseRefinement.Revision;
                Check(onBody.CpuAlternatives.Any(b => !b.UsesGpu && b.PhysicalRevision == cpuBase), "optimized CPU uses same refinement:" + integer);
                var cpu = Owner(gpu: false).Describe(preferences);
                string expectedCpu = CpuFilterExecution.Describe("N.Neow", cpuBase, 1, CpuFilterExecution.Capacity, false, false, false).PhysicalImplementationRevision;
                Check(!cpu.UsesGpu && cpu.PhysicalRevision == expectedCpu, "default CPU exact body identity:" + integer);
            }
            var coupledRequest = SearchPreparationCompiler.Compile(SearchCompiler.Compile(workload.Query, source), options).Plan
                ?? throw new InvalidOperationException("CapsuleSparseSelection.CoupledCompile");
            var coupled = FamilyExecutionCoordinator.Plan(coupledRequest, true, true).OrderedFamilies.First();
            Check(coupled.Coverage.ToHashSet().SetEquals(["N.Neow", "R.Relic"]) &&
                coupled.PhysicalRevision == BonesCapsuleAllocation.LocalRevision + BonesHiddenUpgradeContinuation.Revision +
                    ".branches" + NeowCapsuleNicheContinuation.Revision, "coupled body retains its exact union identity");
            var unrelated = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query with { StructuredOpeningEffects = [] }, source), options).Plan
                ?? throw new InvalidOperationException("CapsuleSparseSelection.UnrelatedCompile");
            var unrelatedReplay = NeowReplayPlan.Compile(unrelated.Model, false);
            var unrelatedOwner = new NeowFamily(unrelated.Model, unrelatedReplay);
            Check(unrelatedReplay.CapsuleNicheContinuation is null &&
                unrelatedOwner.Describe(FilterExecutionPreferences.From(unrelated.RunOptions)).Cost.PrivatePhysicalRevision is null &&
                !unrelatedOwner.ConditionPerformance.PhysicalImplementationRevision.Contains(NeowCapsuleSparseRefinement.Revision, StringComparison.Ordinal),
                "unrelated N has no refinement identity");
        }
        finally
        {
            foreach (string name in controls) Environment.SetEnvironmentVariable(name, previous[name]);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity.Length == 0 ? "Unknown" : oldDevice.GpuIdentity,
                oldDevice.RenderingBackend.Length == 0 ? "unknown" : oldDevice.RenderingBackend);
        }
        return checks;
    }

    private sealed class SimplePlannerFixture(string id, double? denseNs, double? compactNs,
        Func<IReadOnlySet<string>, double?> survival, double? expectedWork = null, string[]? coverage = null, bool gpu = false,
        GpuNumericRequirements? numericRequirements = null, Action<string, bool>? onInvoke = null) : IFilterExecution
    {
        internal readonly List<double> QuotedPopulations = [];
        internal readonly List<HashSet<string>> PassedCoverage = [];
        private FilterBodyDescription? _body;
        private IReadOnlyList<FilterBodyDescription> _cpuAlternatives = [];
        internal IReadOnlyList<FilterBodyDescription> CpuAlternatives
        { get => _cpuAlternatives; set { _cpuAlternatives = value; if (_body is not null) _body = _body with { CpuAlternatives = value }; } }
        public FilterBodyDescription Body => _body ??= new(new PreparedFilter(id)
        {
            Coverage = coverage ?? [id], AnalyticalCost = new(id, PrivateOrdinalBuffer.Capacity, [], "SimplePlannerFixture"),
            Survival = FamilySurvivalProjection.Unresolved(id, "SimplePlannerFixture"),
            HasConditionalProjections = true, SurvivalResolver = ResolveSurvival,
            ExpectedCostResolver = _ => ExpectedFilteringCost
        }, "SimplePlannerFixture." + id, RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
        {
            UsesGpu = gpu, NumericRequirements = gpu ? numericRequirements ?? GpuNumericRequirements.Unspecified : GpuNumericRequirements.None,
            CpuAlternatives = _cpuAlternatives, PerformanceProjection = _ => ConditionPerformance,
            Cost = new() { Quote = (_, geometry, _) => QuotePhysicalWork(geometry) },
            CreateExecution = (_, _) => this
        };
        public static implicit operator FilterBodyDescription(SimplePlannerFixture fixture) => fixture.Body;
        public bool ExecutionFaulted => false;
        public string FamilyId => id;
        public IReadOnlyList<string> Coverage => coverage ?? [id];
        public FamilySurvivalProjection Survival => ResolveSurvival(new HashSet<string>());
        public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passed)
        {
            PassedCoverage.Add(new(passed, StringComparer.Ordinal));
            return survival(passed) is double probability
                ? FamilySurvivalProjection.Resolved(id, probability, "SimplePlannerFixture")
                : FamilySurvivalProjection.Unresolved(id, "SimplePlannerFixture");
        }
        public FamilyExpectedFilteringCostProjection? ExpectedFilteringCost => expectedWork is double work
            ? new(id, work, work, Survival.SurvivalProbability, 0, [], "SimplePlannerFixture") : null;
        public FamilyConditionPerformanceProjection ConditionPerformance => new(id,
            "SimplePlannerFixture." + id, "UnpricedFixture", 1, "SimplePlannerFixture", usesGpu: gpu);
        public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry)
        {
            QuotedPopulations.Add(geometry.MeanInputPopulation);
            return (geometry.CompactInput ? compactNs : denseNs) is double ns
                ? new(id, "SimplePlannerFixture", ns, PrivateOrdinalBuffer.Capacity, null, "SimplePlannerFixture")
                    { PublicTransportClass = "CpuOrderedAbi1" } : null;
        }
        public ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
        {
            if (onInvoke is null) throw new InvalidOperationException("PlanningFixtureMustNotExecute");
            onInvoke(id, !input.IsDense);
            return ValueTask.FromResult(FamilyCandidateSet.FromSortedAbi1(input.Batch, input.EnumerateLogicalOrdinals().ToArray()));
        }
        public ValueTask DisposeAsync(FilterRuntimeServices services) => ValueTask.CompletedTask;
    }

    private static int CheckPartyFilterRemainders()
    {
        int checks = 0;
        void Check(bool pass, string name) { checks++; if (!pass) throw new InvalidOperationException("PartyFilterRemainders:" + name); }
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var describe = typeof(FamilyExecutionCoordinator).GetMethod("DescribeFilterRemainders", flags)!;
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var context = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
            .Invoke(null, [captured.Context, 2, null, null])!;
        SearchContext WithPlayers(params RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot[] players) => context with {
            Authority = players[0], Party = new(players, context.Party!.UnlockProvenance, context.Party.World) };
        SearchQuery Table(SearchQuery first, SearchQuery second) => SearchQuery.Empty with {
            Players = [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty) { Conditions = second }] };
        PreparedSearch Request(SearchQuery query, SearchContext source)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, source),
                new("000000000000", 16, 1, 1) { SkipExactValidation = true });
            return result.Plan ?? throw new InvalidOperationException("PartyFilterRemainders.Compile:" + result.Issue);
        }
        string[] Describe(PreparedSearch request, FilterBodyDescription[] selected) =>
            ((IReadOnlyList<string>)describe.Invoke(null, [request, FamilyPlanner.InOrder(selected), selected])!).ToArray();
        var shop = SearchQuery.Empty with { MerchantColorlessConditions = [new(1, MerchantColorlessSlot.Uncommon,
            RolltheSpire2.Core.Merchant.Beta111MerchantColorlessAuthority.From(context.Party!.Players[0]).UncommonPool[0])] };
        var events = SearchQuery.Empty with { EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard,
            RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
        var ancient = SearchQuery.Empty with { AncientBranches = [new(2, new("EVENT", "TEZCATARA"),
            [new("RELIC", "TOASTY_MITTENS")], [])] };

        // Controlled missing-observation fixtures: these prove typed API and
        // allocation disclosure, not the frequency of this state in a live game.
        var partialShopAuthority = context.Party.Players[0].WithEffectAuthority(context.Party.Players[0].EffectAuthority! with {
            MerchantInitialInventoryShapeExact = false });
        var missingShop = WithPlayers(partialShopAuthority, context.Party.Players[1]);
        Check(!RolltheSpire2.Core.Merchant.Beta111MerchantColorlessAuthority.From(partialShopAuthority).HasExactV1Inputs &&
            RolltheSpire2.Ui.Pages.Search.Shop.ShopColorlessSearchUiCatalog.FromAuthority(context.ProfileId,
                partialShopAuthority.EffectAuthority).CatalogAvailable,
            "Partial S authority can retain picker identities without claiming a filter is available");
        var mixed = Request(Table(shop, events), missingShop);
        var mixedPlan = FamilyExecutionCoordinator.Plan(mixed, false);
        Check(mixedPlan.OrderedFamilies.Any(f => f.FamilyId == "E.EventResult") && !mixedPlan.AllConditionsUnfiltered &&
            mixedPlan.FilterRemainders.Contains("S.MerchantShopColorless:slot=0:NoSelectedFilter"),
            "Missing P1 S remains disclosed when only P2 E participates");
        Check(mixed.Model.CompiledSearch.NormalizedQuery.Players[0].Conditions.MerchantColorlessConditions.Count == 1 &&
            mixedPlan.FinalValidationSkipped, "Disclosure retains authored S and the requested validation mode");
        var sameShop = Request(Table(shop, shop), missingShop);
        var sameShopPlan = FamilyExecutionCoordinator.Plan(sameShop, false);
        Check(sameShopPlan.OrderedFamilies.Single().Preparation.Obligations.Single().PlayerSlot == 1 &&
            sameShopPlan.FilterRemainders.Contains("S.MerchantShopColorless:slot=0:NoSelectedFilter") &&
            !sameShopPlan.FilterRemainders.Contains("S.MerchantShopColorless:slot=1:NoSelectedFilter"),
            "P2 S cannot impersonate missing P1 S coverage");

        var missingAncient = WithPlayers(context.Party.Players[0], context.Party.Players[1].WithWorldAuthority(null));
        var mixedAncient = Request(Table(events, ancient), missingAncient);
        var mixedAncientPlan = FamilyExecutionCoordinator.Plan(mixedAncient, false);
        Check(mixedAncientPlan.OrderedFamilies.Any(f => f.FamilyId == "E.EventResult") &&
            mixedAncientPlan.FilterRemainders.Contains("A.AncientOption:slot=1:NoSelectedFilter"),
            "Missing P2 A OptionContext remains disclosed beside P1 E");
        var sameAncient = Request(Table(ancient, ancient), missingAncient);
        var sameAncientPlan = FamilyExecutionCoordinator.Plan(sameAncient, false);
        Check(sameAncientPlan.OrderedFamilies.Single(f => f.FamilyId == "A.AncientOption")
                .Preparation.Obligations.Single().PlayerSlot == 0 &&
            sameAncientPlan.FilterRemainders.Contains("A.AncientOption:slot=1:NoSelectedFilter") &&
            !sameAncientPlan.FilterRemainders.Contains("A.AncientOption:slot=0:NoSelectedFilter"),
            "P1 A cannot impersonate missing P2 A coverage");

        // E currently retains an existing conservative CPU body when observations
        // are missing. Exercise a selected-allocation omission directly instead
        // of claiming that absent event authority prevents E registration.
        var sameEvents = Request(Table(events, events), context);
        var eventFamily = FamilyExecutionCoordinator.CreateRegisteredFamilies(sameEvents, false).Single(body => body.FamilyId == "E.EventResult");
        var eventChild = SearchPreparationCompiler.ForPlayer(sameEvents.Model, sameEvents.Model.CompiledSearch.PlayerSearches[0]);
        var firstEvent = PartyFamily.Prepare(sameEvents.Model, FilterExecutionPreferences.From(sameEvents.RunOptions),
            [new PartyFilterStage(0, eventFamily.PhysicalBodies.Single(body => body.Preparation.Obligations.Any(o => o.PlayerSlot == 0)))]);
        string[] eventRemainders = Describe(sameEvents, [firstEvent]);
        Check(eventRemainders.Contains("E.EventResult:slot=1:NoSelectedFilter") &&
            !eventRemainders.Contains("E.EventResult:slot=0:NoSelectedFilter"), "P1 E cannot impersonate unselected P2 E coverage");
        var eventsAndShop = Request(Table(events, shop), context);
        var selectedShop = FamilyExecutionCoordinator.CreateRegisteredFamilies(eventsAndShop, false)
            .Where(f => f.FamilyId == "S.MerchantShopColorless").ToArray();
        Check(Describe(eventsAndShop, selectedShop).Contains("E.EventResult:slot=0:NoSelectedFilter"),
            "Unselected P1 E is disclosed beside P2 S");
        Check(!Describe(sameEvents, [eventFamily]).Any(r => r.Contains(":NoSelectedFilter", StringComparison.Ordinal)),
            "Complete E coverage has no missing-slot claim");

        var sharedMap = Request(Table(SearchQuery.Empty, SearchQuery.Empty) with {
            StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 1)] }, context);
        Check(FamilyExecutionCoordinator.Plan(sharedMap, false).FilterRemainders.Contains("M.StandardMap:Shared:NoSelectedFilter"),
            "Shared map obligation remains separate from personal slot zero");
        var solo = Request(shop, captured.Context with { Authority = captured.Context.Authority.WithEffectAuthority(null) });
        Check(FamilyExecutionCoordinator.Plan(solo, false).FilterRemainders.SequenceEqual(["S.MerchantShopColorless:NoSelectedFilter"]),
            "Singleplayer missing-filter disclosure keeps its existing shape");

        var chestTarget = new ModelKey("RELIC", "JUZU_BRACELET");
        var treasure = new TreasureRelicSearchCondition(1, 2, TreasureRelicOrderMode.Ordered, [null, chestTarget]);
        var authored = SearchQuery.Empty with { TreasureRelics = treasure, FirstActUnknownEventPrefixCount = 2 };
        var bounded = Request(authored, captured.Context);
        var boundedPlan = FamilyExecutionCoordinator.Plan(bounded, false);
        Check(bounded.Model.Evaluation.TreasureRelics is { StartOrdinal: 1, Count: 2, OrderMode: TreasureRelicOrderMode.Ordered } kept &&
            kept.Slots.SequenceEqual(treasure.Slots) && bounded.Model.Evaluation.FirstActUnknownEventPrefixCount == 2,
            "Exact projection preserves both new obligations and relative empty slots");
        Check(bounded.Model.Evaluation.RequiresRelicSequenceDomain && !bounded.Model.Evaluation.HasNeowConstraints &&
            boundedPlan.Stages.Count == 2 && !boundedPlan.AllConditionsUnfiltered &&
            boundedPlan.Stages.SelectMany(stage => stage.Coverage).ToHashSet(StringComparer.Ordinal)
                .SetEquals([InitialRoomsInvocation.TreasureCoverage, VariantOpeningFamily.Id]) &&
            boundedPlan.FilterRemainders.Count == 0,
            "Initial treasure and Unknown filters retain their separate R/V ownership without invented N authority");
        var prefix = Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 }, captured.Context);
        var prefixPlan = FamilyExecutionCoordinator.Plan(prefix, false);
        Check(prefix.Model.Evaluation.CheapPredictionDomains == RolltheSpire2.Core.Prediction.SeedPredictionDomainSelection.None &&
            prefix.Model.Evaluation.RequiresCanonicalRootLocalDomain && !prefix.Model.Evaluation.HasNeowConstraints &&
            !prefixPlan.AllConditionsUnfiltered && prefixPlan.Stages.Count == 1 &&
            prefixPlan.OrderedFamilies.Single().FamilyId == VariantOpeningFamily.Id &&
            prefixPlan.Stages.Single().Coverage.SequenceEqual([VariantOpeningFamily.Id]),
            "Unknown prefix alone keeps root-local Exact and gains only its V filter");
        var missingBag = Request(authored, captured.Context with { Authority = captured.Context.Authority.WithWorldAuthority(null) });
        var missingBagPlan = FamilyExecutionCoordinator.Plan(missingBag, false);
        Check(!missingBagPlan.AllConditionsUnfiltered &&
            missingBagPlan.Stages.SelectMany(stage => stage.Coverage).SequenceEqual([VariantOpeningFamily.Id]) &&
            missingBagPlan.FilterRemainders.SequenceEqual(["TreasureRelics:ProductionExactOnly"]) &&
            missingBag.Model.Evaluation.TreasureRelics!.Slots.SequenceEqual(treasure.Slots),
            "Missing shared-bag authority retains the treasure remainder while independent V filtering continues");
        var projected = bounded.Model.Evaluation.ToLegacyFilter();
        var roundtrip = SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(projected), captured.Context);
        Check(roundtrip.NormalizedQuery.TreasureRelics!.Slots.SequenceEqual(treasure.Slots) &&
            roundtrip.NormalizedQuery.FirstActUnknownEventPrefixCount == 2 &&
            ExactSearchEvaluationProjection.FromLegacyFilter(projected).TreasureRelics == projected.TreasureRelics,
            "Legacy query and Exact adapters retain new conditions");
        var dormant = treasure with { Count = 1 };
        var dormantQuery = SearchCompiler.Compile(SearchQuery.Empty with { TreasureRelics = dormant }, captured.Context);
        Check(dormant.IsEmpty && dormantQuery.NormalizedQuery.TreasureRelics!.Slots.SequenceEqual(treasure.Slots) &&
            !CompiledSearchEvaluationProjector.Project(dormantQuery).Evaluation.RequiresRelicSequenceDomain,
            "Inactive authored target survives normalization without becoming a predicate");
        Check(bounded.Model.CompiledSearch.SemanticFingerprint != Request(authored with { FirstActUnknownEventPrefixCount = 1 }, captured.Context).Model.CompiledSearch.SemanticFingerprint &&
            bounded.Model.CompiledSearch.SemanticFingerprint != Request(authored with { TreasureRelics = treasure with { OrderMode = TreasureRelicOrderMode.Unordered } }, captured.Context).Model.CompiledSearch.SemanticFingerprint,
            "New settings participate in semantic identity");
        var initialProbability = RolltheSpire2.Search.Selectivity.JointSelectivityEstimator.EstimateQuery(
            RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(bounded));
        Check(initialProbability.JointlyPriced && initialProbability.Probability is >= 0 and < 1,
            "Declared initial-room factors now participate in the complete query probability");
        foreach (var invalid in new[] {
                     authored with { FirstActUnknownEventPrefixCount = -1 }, authored with { FirstActUnknownEventPrefixCount = 4 },
                     authored with { TreasureRelics = treasure with { StartOrdinal = 2 } },
                     authored with { TreasureRelics = treasure with { Count = 3 } },
                     authored with { TreasureRelics = treasure with { OrderMode = (TreasureRelicOrderMode)99 } },
                     authored with { TreasureRelics = treasure with { Slots = [new("CARD", "STRIKE"), null] } } })
        {
            bool rejected = false;
            try { SearchCompiler.Compile(invalid, captured.Context); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Malformed new authored shape is rejected before normalization");
        }
        return checks;
    }
    private static int CheckStartupFilterDisclosure()
    {
        int checks = 0;
        void Check(bool pass, string name) { checks++; if (!pass) throw new InvalidOperationException("StartupFilterDisclosure:" + name); }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        PreparedSearch Request(SearchQuery query, SearchContext source, bool skip)
        {
            var compiled = SearchCompiler.Compile(query, source);
            var result = SearchPreparationCompiler.Compile(compiled,
                new("000000000000", 1, 1, 1) { SkipExactValidation = skip });
            return result.Plan ?? throw new InvalidOperationException("StartupFilterDisclosure.Compile:" + result.Issue +
                ";normalization=" + compiled.Status + ";diagnostics=" + string.Join('|', compiled.Normalization.Diagnostics));
        }
        (RuntimeIssue[] Issues, SearchProgressSnapshot Progress, SearchCandidate[] Candidates) Start(PreparedSearch request)
        {
            string correlation = "StartupFilterDisclosure." + Guid.NewGuid().ToString("N");
            using var scope = RuntimeLog.SearchScope(correlation);
            var session = FamilyExecutionCoordinator.Start(request, gpuAvailable: false);
            try
            {
                session.Completion.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
                var candidates = new List<SearchCandidate>();
                while (session.TryReadCandidate(out var candidate)) candidates.Add(candidate!);
                return (RuntimeIssueLedger.Current.Snapshot().Where(issue => issue.CorrelationId == correlation).ToArray(),
                    session.GetProgress(), candidates.ToArray());
            }
            finally { session.Cancel(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        var treasure = SearchQuery.Empty with { TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered,
            [new("RELIC", "JUZU_BRACELET")]) };
        var prefix = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 2 };
        var events = new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,
            RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards[0]);
        foreach (var (query, obligation) in new[] { (treasure, "TreasureRelics"), (prefix, "FirstActUnknownEventPrefix") })
        foreach (bool available in obligation == "TreasureRelics" ? new[] { false, true } : new[] { true })
        foreach (bool mixed in new[] { false, true })
        foreach (bool skip in new[] { false, true })
        {
            var authored = mixed ? query with { EventResultConditions = [events] } : query;
            var source = available ? context : context with { Authority = context.Authority.WithWorldAuthority(null) };
            var request = Request(authored, source, skip);
            var plan = FamilyExecutionCoordinator.Plan(request, false);
            Check(plan.FilterRemainders.Contains(obligation + ":ProductionExactOnly") == !available &&
                plan.AllConditionsUnfiltered == (!available && !mixed) && plan.FinalValidationSkipped == skip &&
                plan.Stages.SelectMany(stage => stage.Coverage).Contains(obligation == "TreasureRelics"
                    ? InitialRoomsInvocation.TreasureCoverage : VariantOpeningFamily.Id) == available,
                "Startup discloses the actual initial-room coverage beside independent filters:" + obligation);
            var result = Start(request);
            Check(result.Progress.State == SearchRunState.Completed && result.Progress.PendingCandidateCount == 0,
                "One-root startup completes without inventing a candidate fault:" + obligation);
            var notices = result.Issues.Where(issue => issue.Code == "SearchFilterIncomplete").ToArray();
            Check(skip && !available ? notices.Length == 1 && notices[0].MessageKey == "issues.conditions_unchecked" &&
                    notices[0].ExceptionDetail.Contains(obligation + ":ProductionExactOnly", StringComparison.Ordinal) &&
                    notices[0].ExceptionDetail.Contains("finalValidationSkipped=True", StringComparison.Ordinal)
                : notices.Length == 0,
                "Actual Coordinator startup warns only for an unfiltered obligation whose Exact duty is skipped:" + obligation);
            Check(result.Issues.Any(issue => issue.Code == "AllConditionsUnfiltered") == (skip && !available && !mixed),
                "Raw enumeration disclosure remains tied to skipped Exact with no filters");
            if (skip && !available && !mixed)
                Check(result.Candidates.Length == 1 && result.Candidates[0].IsUnfiltered && result.Candidates[0].IsUnverified &&
                    result.Progress.MatchCount == 0, "Unchecked raw seed never claims a confirmed match");
        }

        // Whole-table scope information is not itself a failure. Personal Exact
        // duties keep their slot identity beside unrelated selected filters.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var party = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
            .Invoke(null, [context, 2, null, null])!;
        SearchQuery Table(SearchQuery first, SearchQuery second) => SearchQuery.Empty with {
            Players = [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty) { Conditions = second }] };
        foreach (bool mixed in new[] { false, true })
        foreach (bool skip in new[] { false, true })
        {
            var request = Request(Table(mixed ? treasure with { EventResultConditions = [events] } : treasure, prefix), party, skip);
            var plan = FamilyExecutionCoordinator.Plan(request, false);
            Check(plan.FilterRemainders.Contains("TreasureRelics:slot=0:ProductionExactOnly") &&
                plan.Stages.SelectMany(stage => stage.Coverage).Contains(VariantOpeningFamily.Id) &&
                !plan.FilterRemainders.Any(item => item.StartsWith(VariantOpeningFamily.Id, StringComparison.Ordinal)),
                "Party retains the personal treasure remainder while V covers the shared Unknown stream");
            var notices = Start(request).Issues.Where(issue => issue.Code == "SearchFilterIncomplete").ToArray();
            Check(skip ? notices.Length == 1 && notices[0].MessageKey == "issues.conditions_unchecked" &&
                    notices[0].ExceptionDetail.Split(";finalValidationSkipped=", 2)[0].Contains("TreasureRelics:slot=0:ProductionExactOnly", StringComparison.Ordinal) &&
                    !notices[0].ExceptionDetail.Split(";finalValidationSkipped=", 2)[0].Contains("FirstActUnknownEventPrefix", StringComparison.Ordinal) &&
                    !notices[0].ExceptionDetail.Split(";finalValidationSkipped=", 2)[0].Contains("Party:", StringComparison.Ordinal)
                : notices.Length == 0, "Party startup classifies direct/mixed Exact duties without warning on scope information");
        }
        Check(!Start(Request(Table(SearchQuery.Empty with { EventResultConditions = [events] }, SearchQuery.Empty), party, false))
            .Issues.Any(issue => issue.Code == "SearchFilterIncomplete"), "Party scope marker alone does not warn");

        var shop = treasure with { MerchantColorlessConditions = [new(1, MerchantColorlessSlot.Uncommon,
            RolltheSpire2.Core.Merchant.Beta111MerchantColorlessAuthority.From(context.Authority).UncommonPool[0])] };
        var missing = context with { Authority = context.Authority.WithEffectAuthority(null) };
        var unavailable = Start(Request(shop, missing, false)).Issues.Single(issue => issue.Code == "SearchFilterIncomplete");
        Check(unavailable.MessageKey == "issues.filter_incomplete" && unavailable.ExceptionDetail.StartsWith(
            "remainders=S.MerchantShopColorless:NoSelectedFilter;finalValidationSkipped=False;", StringComparison.Ordinal),
            "Enabled Exact does not hide a real missing filter beside its normal treasure duty");
        var unsupported = treasure with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with {
            RequiredFinalCurse = new("CARD", "RT2_UNAVAILABLE_CURSE") } };
        var partial = Start(Request(unsupported, context, false)).Issues.Single(issue => issue.Code == "SearchFilterIncomplete");
        Check(partial.ExceptionDetail.StartsWith("remainders=N.Neow:FinalCurseAuthorityUnavailable;", StringComparison.Ordinal),
            "Enabled Exact does not hide the actual unsupported N remainder reason");
        // This is a legal Bones requirement with missing replay observations,
        // not an impossible offered identity rejected by the query compiler.
        var compileFault = treasure with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with { RequireNeowsBones = true } };
        var faultRequest = Request(compileFault, missing, false);
        bool replayRejected = false;
        try { _ = NeowReplayPlan.Compile(faultRequest.Model, false); }
        catch (InvalidOperationException error) when (error.Message == "NeowFamilyReplayAuthorityUnavailable") { replayRejected = true; }
        Check(replayRejected, "Legal request reaches the real replay compilation authority failure");
        var failed = Start(faultRequest).Issues.Single(issue => issue.Code == "SearchFilterIncomplete");
        Check(failed.ExceptionDetail.StartsWith("remainders=N.Neow:NoSelectedFilter;", StringComparison.Ordinal),
            "Enabled Exact does not hide a filter omitted after replay compilation fails");
        foreach (string language in new[] { "en", "zh" })
        {
            var text = JsonUiTextProvider.CreateUi13(language);
            Check(text.Get("issues.conditions_unchecked") != "issues.conditions_unchecked" &&
                text.Get("issues.filter_incomplete") != "issues.filter_incomplete", "Both warning classes are localized:" + language);
        }
        checks += CheckComposedFilterDisclosure(context);
        return checks;
    }
    private static int CheckComposedFilterDisclosure(SearchContext context)
    {
        int checks = 0;
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException("ComposedFilterDisclosure:" + reason); }
        PreparedSearch Request(SearchQuery query, SearchContext? source = null) => SearchPreparationCompiler.Compile(
            SearchCompiler.Compile(query, source ?? context), new("000000000000", 1, 1, 1)).Plan
            ?? throw new InvalidOperationException("ComposedFilterDisclosure.Compile");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var describe = typeof(FamilyExecutionCoordinator).GetMethod("DescribeFilterRemainders", flags)!;
        string[] Describe(PreparedSearch request, params FilterBodyDescription[] bodies) =>
            ((IReadOnlyList<string>)describe.Invoke(null, [request, FamilyPlanner.InOrder(bodies), bodies])!).ToArray();
        FilterBodyDescription Body(string id, FilterObligation[]? obligations = null) => new(
            new PreparedFilter(id) { Obligations = obligations ?? [] }, "DisclosureFixture", context.ProfileId)
            { BindPrivate = (_, _, _) => throw new InvalidOperationException("DisclosureFixture.MustNotExecute") };
        var events = new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,
            RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards[0]);
        var request = Request(SearchQuery.Empty with { EventResultConditions = [events], FirstActUnknownEventPrefixCount = 3 });
        var preferences = FilterExecutionPreferences.From(request.RunOptions);
        var n = Body("N.Neow");
        var w = Body("W.World");
        var e = Body("E.EventResult", [new("E.EventResult", 1)]) with {
            Preparation = new("E.EventResult") { Obligations = [new("E.EventResult", 1)], Remainders = ["RealEventRemainder"] } };
        var v = Body(VariantOpeningFamily.Id, [new(InitialRoomsInvocation.UnknownCoverage, ThroughOrdinal: 2)]) with {
            Preparation = new(VariantOpeningFamily.Id) {
                Obligations = [new(InitialRoomsInvocation.UnknownCoverage, ThroughOrdinal: 2)],
                Issues = [new("VariantPreparation", "RealVariantUnavailable")] } };
        var serial = AutomaticPrivateSerialInvocation.Prepare(request.Model, preferences, [n, v, w, e]);
        Check(serial.Preparation.Obligations.Contains(new("N.Neow")) && serial.Preparation.Obligations.Contains(new("W.World")) &&
            serial.Preparation.Obligations.Contains(new("E.EventResult", 1)), "Mixed declarations retain every child including explicit player ownership");
        Check(!serial.Preparation.Obligations.Any(o => o.Domain == VariantOpeningFamily.Id) &&
            serial.Preparation.Obligations.Contains(new(InitialRoomsInvocation.UnknownCoverage, ThroughOrdinal: 2)),
            "Partial V keeps its bounded Unknown duty without claiming variant coverage");
        Check(Describe(request, serial).ToHashSet().SetEquals(["FirstActUnknownEventPrefix:ProductionExactOnly",
            "V.VariantOpening:RealVariantUnavailable", "E.EventResult:RealEventRemainder"]),
            "Composition retains the real ordinal gap and child causes without attributing them to the transport parent");
        var noVariant = Body(VariantOpeningFamily.Id);
        var noClaim = AutomaticPrivateSerialInvocation.Prepare(request.Model, preferences, [n, noVariant]);
        Check(!noClaim.Preparation.Obligations.Any(o => o.Domain == VariantOpeningFamily.Id),
            "An undeclared V never acquires support from its broad physical Coverage");
        var unknownPlan = InitialRoomsPlan.TryCompileUnknown(request.Model)!;
        var decorated = InitialRoomsInvocation.Prepare(request.Model, preferences, "W.World", unknownPlan, w, false);
        Check(decorated.Preparation.Obligations.Contains(new("W.World")) &&
            decorated.Preparation.Obligations.Contains(new(InitialRoomsInvocation.UnknownCoverage, ThroughOrdinal: 3)),
            "Initial Unknown decoration retains ordinary W and the real prefix bound");
        var treasureRequest = Request(SearchQuery.Empty with { TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered,
            [new("RELIC", "JUZU_BRACELET")]) });
        var treasure = InitialRoomsInvocation.Prepare(treasureRequest.Model, preferences, "R.Relic",
            InitialRoomsPlan.TryCompileTreasure(treasureRequest.Model)!, Body("R.Relic"), false);
        Check(treasure.Preparation.Obligations.Contains(new("R.Relic")) &&
            treasure.Preparation.Obligations.Contains(new(InitialRoomsInvocation.TreasureCoverage)),
            "Initial treasure decoration retains a Coverage-only ordinary child");
        var party = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
            .Invoke(null, [context, 2, null, null])!;
        var tableRequest = Request(SearchQuery.Empty with { Players = [
            new(0, ModelKeySetFilter.Empty) { Conditions = SearchQuery.Empty with { EventResultConditions = [events] } },
            new(1, ModelKeySetFilter.Empty) { Conditions = SearchQuery.Empty with { EventResultConditions = [events] } }] }, party);
        var table = PartyFamily.Prepare(tableRequest.Model, preferences, [new(0, Body("E.EventResult")), new(1, e)]);
        Check(table.Preparation.Obligations.Select(o => o.PlayerSlot).SequenceEqual(new int?[] { 0, 1 }) &&
            !Describe(tableRequest, table).Any(r => r.Contains("NoSelectedFilter", StringComparison.Ordinal)),
            "Party mixes fallback and explicit personal declarations without losing a seat");
        Check(Describe(tableRequest, PartyFamily.Prepare(tableRequest.Model, preferences, [new(0, e)]))
            .Contains("E.EventResult:slot=0:NoSelectedFilter"), "Explicit ownership cannot be reassigned to conceal a missing seat");
        var ncRequest = Request(SearchQuery.Empty with {
            LegacyNeow = LegacyNeowSemanticConstraints.Empty with { RequiredFinalCurse = new("CARD", "RT2_UNAVAILABLE_CURSE") },
            CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [new("CARD", "BLUDGEON")]) });
        var ncBodies = FamilyExecutionCoordinator.CreateRegisteredFamilies(ncRequest, true);
        foreach (string mode in new[] { "private-nc", "private-ordinal" })
        {
            Check(NcPhysicalExperiment.TrySelect(ncRequest, ncBodies, FamilyPlanner.InOrder(ncBodies), out var nc, mode) &&
                Describe(ncRequest, nc!.OrderedFamilies.ToArray()).Contains("N.Neow:FinalCurseAuthorityUnavailable"),
                "Dedicated NC preserves its actual N remainder in either physical order:" + mode);
            var body = nc!.OrderedFamilies.Single();
            // Controlled C cause also verifies qualification when C is not the
            // transport parent's ID; colons inside a raw cause are not domains.
            var causes = body with { Preparation = body.Preparation with { Remainders =
                ["C.CombatReward:AuthoredNestedRewardEffectUnresolved:RELIC:CONTROLLED", "RawCause:Detail"] } };
            var remaining = Describe(ncRequest, causes);
            Check(remaining.Contains("C.CombatReward:AuthoredNestedRewardEffectUnresolved:RELIC:CONTROLLED") &&
                remaining.Contains(body.FamilyId + ":RawCause:Detail"), "Dedicated causes retain their actual domain:" + mode);
        }
        var nwaeRequest = Request(SearchQuery.Empty with {
            LegacyNeow = ncRequest.Model.CompiledSearch.Query.LegacyNeow, EventResultConditions = [events],
            AncientBranches = [new(2, new("EVENT", "TEZCATARA"), [new("RELIC", "TOASTY_MITTENS")], [])] });
        var nwaeBodies = FamilyExecutionCoordinator.CreateRegisteredFamilies(nwaeRequest, true);
        Check(NwaePhysicalExperiment.TrySelect(nwaeRequest, nwaeBodies, FamilyPlanner.InOrder(nwaeBodies), out var nwae, "private-ENAW") &&
            Describe(nwaeRequest, nwae!.OrderedFamilies.ToArray()).Contains("N.Neow:FinalCurseAuthorityUnavailable"),
            "E-first dedicated NWAE retains the actual N cause without an E prefix");
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            SearchPerformanceProfileFoundation.ObserveRenderingBackend("d3d12");
            var capsule = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/direct-capsule-v1/Small-C-check.json");
            var nrRequest = Request(capsule.Query with { TreasureRelics = treasureRequest.Model.CompiledSearch.Query.TreasureRelics });
            // Current opening/treasure admission keeps this combination Exact-only.
            // Widen the input descriptor deliberately to guard against a future
            // decorator accidentally granting NR a duty its kernel never runs.
            var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(nrRequest, true).Select(body =>
                body.FamilyId == "R.Relic" ? body with { Preparation = body.Preparation with {
                    Coverage = [..body.Coverage, InitialRoomsInvocation.TreasureCoverage],
                    Obligations = [..body.Preparation.EffectiveObligations, new(InitialRoomsInvocation.TreasureCoverage)] } } : body).ToArray();
            Check(registered.Single(body => body.FamilyId == "R.Relic").Coverage.Contains(InitialRoomsInvocation.TreasureCoverage),
                "Controlled wider R descriptor includes the additional treasure obligation");
            Check(DirectCapsuleAllocation.TryCreateDefaultUnit(nrRequest.Model, preferences, registered, out var nr) && nr is not null,
                "Actual dedicated NR admits its ordinary N/R numerical scope beside decorated R");
            Check(Describe(nrRequest, [nr!, ..registered.Where(body => body.FamilyId is not ("N.Neow" or "R.Relic"))])
                .Contains("TreasureRelics:ProductionExactOnly"),
                "Dedicated NR does not inherit unexecuted initial treasure from the wider registered R");
        }
        finally
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity.Length == 0 ? "Unknown" : oldDevice.GpuIdentity,
                oldDevice.RenderingBackend.Length == 0 ? "unknown" : oldDevice.RenderingBackend);
        }
        Console.WriteLine($"ComposedFilterDisclosure checks={checks};PASS");
        return checks;
    }
    private static int CheckUnifiedPartyPlanning()
    {
        int checks = 0;
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException("UnifiedParty:" + reason); }
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        foreach (int players in new[] { 2, 3, 4 })
        {
            var context = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
                .Invoke(null, [captured, players, null, null])!;
            const string seed = "000000000000";
            ulong root = RolltheSpire2.Compatibility.Beta111Profile.Instance.ComputeRootSeed(seed);
            var query = SearchQuery.Empty with { Players = context.Party!.Players.Select(owner =>
            {
                var offers = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(seed, owner,
                    RolltheSpire2.Compatibility.Beta111Profile.Instance, true).RelicKeys;
                var shop = RolltheSpire2.Core.Merchant.Beta111NormalMerchantColorlessSequenceProjector.Project(root,
                    RolltheSpire2.Core.Merchant.Beta111MerchantColorlessAuthority.From(owner), 3);
                return new PlayerOfferQuery(owner.PlayerSlotIndex, new([], offers, [])) { Conditions = SearchQuery.Empty with {
                    MerchantColorlessConditions = [new(3, MerchantColorlessSlot.Uncommon, shop.Merchants[2].UncommonColorless)] } };
            }).ToArray() };
            foreach (bool skip in new[] { false, true })
            foreach (bool map in new[] { false, true })
            {
                var authored = map ? query with { StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 1)] } : query;
                PreparedSearch Request(long count, int target) => SearchPreparationCompiler.Compile(
                    SearchCompiler.Compile(authored, context), new(seed, count, target, 4, SkipExactValidation: skip)).Plan
                    ?? throw new InvalidOperationException("UnifiedParty.Compile");
                var request = Request(64, 1);
                var plan = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false);
                var otherHorizon = FamilyExecutionCoordinator.Plan(Request(1L << 34, 1000), gpuAvailable: false);
                Check(plan.SelectionPolicyId == FamilyPlanner.SelectionPolicyId && plan.AllowSmallCandidateExact,
                    $"Shared greedy policy players={players};skip={skip};map={map}");
                Check(plan.FinalValidationSkipped == skip && request.Model.CompiledSearch.NormalizedQuery.Players.Count == players,
                    "Validation mode and authored table remain intact");
                Check(plan.Stages.SelectMany(stage => stage.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "S.MerchantShopColorless"]),
                    "Every table N/S obligation participates");
                Check(plan.OrderedFamilies.Select(f => f.FamilyId).SequenceEqual(otherHorizon.OrderedFamilies.Select(f => f.FamilyId)),
                    "Target count and scan horizon cannot choose a different whole chain");
                var shop = plan.OrderedFamilies.Single(f => f.FamilyId == "S.MerchantShopColorless");
                Check(shop.Preparation.Obligations.Select(obligation => obligation.PlayerSlot!.Value).Order().SequenceEqual(Enumerable.Range(0, players)),
                    "Whole-table shop keeps every personal predicate bound to its original seat");
            }
        }
        return checks;
    }

    private static int CheckPartyCpuNeowQuoteIdentity()
    {
        int checks = 0;
        void Check(bool pass, string name) { checks++; if (!pass) throw new InvalidOperationException("PartyCpuNeowQuote:" + name); }
        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "Intel64 Family 6 Model 183 TestFixture");
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("RTX 4060 Laptop GPU", "d3d12");
            (double Ms, double SetupMs, string Evidence)? Quote(string? revision, int capacity = 8192, int workers = 4, int inputs = 8192) =>
                PartyMeasuredCost.Price("N.Neow", inputs, 4, false, false, capacity, 4, .0001, workers,
                    cpuPhysicalRevision: revision);
            foreach (int workers in Enumerable.Range(1, 8))
            foreach (string mode in new[] { "Dense", "CompactAbi1" })
            foreach (int inputs in new[] { 64, 8192 })
            {
                string revision = $"N.Neow.PartyEffects.Cpu.v3.RootMode2.P{workers}.B8192.{mode}.CanonicalAbi1Ready";
                var quote = Quote(revision, workers: workers, inputs: inputs);
                double ns = workers == 1 ? 5800 : workers <= 4 ? 2000 : 8000;
                Check(quote.HasValue && Math.Abs(quote.Value.Ms - (.35 + inputs * ns / 1e6)) < 1e-12 &&
                    quote.Value.Evidence.Contains("CpuPhysicalRevision=" + revision, StringComparison.Ordinal),
                    $"Anchored P{workers}/{mode}/{inputs} retains its existing sustained quote and actual identity");
            }
            const string anchored = "N.Neow.PartyEffects.Cpu.v3.RootMode2.P4.B8192.Dense.CanonicalAbi1Ready";
            Check(Quote(null) is null && Quote("") is null, "Missing physical identity remains unpriced");
            Check(Quote(anchored.Replace(".v3.", ".v6.", StringComparison.Ordinal)) is null,
                "A new CPU body cannot inherit old anchors");
            Check(Quote(anchored, capacity: 65536) is null &&
                Quote(anchored.Replace(".B8192.", ".B65536.", StringComparison.Ordinal), capacity: 65536) is null,
                "A changed scheduler window remains unpriced even with an old or matching new identity");
            Check(Quote(anchored.Replace(".B8192.", ".B65536.", StringComparison.Ordinal)) is null &&
                Quote(anchored.Replace(".RootMode2.", ".RootMode0.", StringComparison.Ordinal)) is null &&
                Quote(anchored, workers: 8) is null && Quote(anchored + ".NewBody") is null,
                "Full identity must match root mode, requested workers, window and body exactly");
            Check(Quote(anchored.Replace(".P4.", ".P16.", StringComparison.Ordinal), workers: 16) is null,
                "Matching identity does not invent an unmeasured worker anchor");
            var gpu = PartyMeasuredCost.Price("N.Neow", 8192, 4, true, true, 8192, 4, .0001);
            var gpuNewIdentity = PartyMeasuredCost.Price("N.Neow", 8192, 4, true, true, 8192, 4, .0001,
                cpuPhysicalRevision: "UnanchoredCpuBody");
            Check(gpu.HasValue && gpu == gpuNewIdentity, "GPU N pricing does not depend on CPU identity");
            var shop = PartyMeasuredCost.Price("S.MerchantShopColorless", 64, 4, false, false, 8192, 35, .00001);
            var shopNewIdentity = PartyMeasuredCost.Price("S.MerchantShopColorless", 64, 4, false, false, 8192, 35, .00001,
                cpuPhysicalRevision: "UnanchoredCpuBody");
            Check(shop.HasValue && shop == shopNewIdentity, "Other CPU families keep their existing quote contract");
            // Retained measured envelope for explicit small configured-wrapper trials.
            // This does not restore the removed automatic shape-specific selector.
            (double Ms, double SetupMs, string Evidence)? SmallShopQuote(string id, double inputs, int workers, bool narrow) =>
                PartyMeasuredCost.Price(id, inputs, 2, false, false, 8192, id == "R.Relic" ? 206 : 35, .00001, workers, narrow);
            var p1 = SmallShopQuote("S.MerchantShopColorless", 1024, 1, false);
            var p8 = SmallShopQuote("S.MerchantShopColorless", 1024, 8, true);
            Check(p1.HasValue && p8.HasValue && p1.Value.Ms == p8.Value.Ms &&
                p8.Value.Evidence.Contains("RequestedWorkers=8;ModeledEffectiveWorkers=1", StringComparison.Ordinal),
                "Configured small P8 preserves the existing P1 envelope with explicit attribution");
            Check(SmallShopQuote("S.MerchantShopColorless", 1024, 8, false) is null, "Ordinary P8 quote remains unavailable");
            Check(SmallShopQuote("S.MerchantShopColorless", 1024.01, 8, true) is null && SmallShopQuote("S.MerchantShopColorless", 0, 8, true) is null,
                "Narrow measured P1 attribution keeps its original input boundaries");
            Check(SmallShopQuote("R.Relic", 1024, 8, true) is null, "Small S envelope does not price other families");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", oldCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity.Length == 0 ? "Unknown" : oldDevice.GpuIdentity,
                oldDevice.RenderingBackend.Length == 0 ? "unknown" : oldDevice.RenderingBackend);
        }
        return checks;
    }

    private static void CheckCandidateValidationPublication(SearchContext context, Action<bool, string> check)
    {
        var compiled = SearchCompiler.Compile(SearchQuery.Empty, context);
        var plan = SearchPreparationCompiler.Compile(compiled, new("000000000000", 4, 4, 1)).Plan!;
        var execution = FamilyPlanner.InOrder([]);
        (SearchProgressSnapshot Progress, SearchCandidate[] Candidates, ulong Cursor, IReadOnlyList<SearchPendingCandidate> Pending) Run(
            SearchDisposition[] dispositions, int target, string fault = "")
        {
            var requestPlan = plan with { RunOptions = plan.RunOptions with { TargetMatchCount = target } };
            int attempt = 0;
            ProductionExactSearchResult Evaluate(SearchModel request, TrustedRootHashInput input)
            {
                if (fault == "throw") throw new InvalidOperationException("InjectedValidationFault");
                if (fault == "unexpectedCancel") throw new OperationCanceledException("InjectedUnrequestedCancellation");
                // A one-disposition fixture applies to every attempted seed,
                // including later seeds retained after a real provider failure.
                var disposition = dispositions[Math.Min(attempt++, dispositions.Length - 1)];
                if (fault == "missingMatch") return new(null, null, request.Authority, SearchQueryEvaluation.Match(), "");
                if (!SeedPredictionRequest.TryCreateFromRootHash(input, CharacterIdentity.FromKey(request.CharacterKey), request.Ascension,
                    request.Authority.PlayersCount, request.Authority.PlayerSlotIndex, request.Authority,
                    request.Evaluation.AncientOptionConditions, SeedPredictionDomainSelection.None,
                    SeedPredictionInputLimits.DefaultRelicSequencePreviewCount, true, out var predictionRequest, out var issue))
                    throw new InvalidOperationException("PublicationFixtureRequest:" + issue);
                var facts = new SeedPredictionFacts {
                    Context = PredictionContext.FromRequest(predictionRequest!, request.Detection.DisplayVersion, request.ProfileId),
                    PredictorId = "ValidationPublicationFixture", OriginalSeed = input.CanonicalSeed, CanonicalSeed = input.CanonicalSeed,
                    InputStatus = SeedPredictionInputStatus.Ready,
                    Warnings = fault == "predictionFailure" ? [new(PredictionWarningCode.AnalysisFailed)] : [],
                    Diagnostics = fault is "predictionFailure" or "diagnostic"
                        ? [new(PredictionDiagnosticCodes.Exception, fault == "predictionFailure" ? "InjectedPredictionFault" : "InjectedDiagnosticOnly")] : []
                };
                var evaluation = disposition switch {
                    SearchDisposition.Match => SearchQueryEvaluation.Match([new("FixtureEvidence")], ["FixtureRoute"]),
                    SearchDisposition.NoMatch => SearchQueryEvaluation.NoMatch("ExplicitMismatch"),
                    SearchDisposition.Unknown => SearchQueryEvaluation.Unknown("UnresolvedFixture") with {
                        RouteRejections = [new("FixtureRoute", [], SearchDisposition.Unsupported, "RouteUnsupportedRootCause")] },
                    _ => SearchQueryEvaluation.Unsupported("UnsupportedFixture")
                };
                // Scoped unresolved evaluations may retain bounded input facts;
                // those facts never add confirmation evidence or route witnesses.
                return new(predictionRequest, facts, request.Authority, evaluation, evaluation.FailureCode);
            }
            var session = new ProductionSearchSession(requestPlan, execution,
                FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(requestPlan, execution), execution), 4, 8, null, "", exactEvaluator: Evaluate);
            try {
                check(session.Completion.Wait(15000), "Validation publication session completes: " + fault);
                var candidates = new List<SearchCandidate>();
                while (session.TryReadCandidate(out var candidate)) candidates.Add(candidate!);
                return (session.GetProgress(), candidates.ToArray(), session.GetSafeNextOrdinal(),
                    session.CaptureContinuation()!.PendingCandidates);
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        var mixed = Run([SearchDisposition.Match, SearchDisposition.Unknown, SearchDisposition.Unsupported, SearchDisposition.NoMatch], 4);
        check(mixed.Progress.State == SearchRunState.Completed && mixed.Progress.ScannedCount == 4 && mixed.Cursor == 4 &&
            mixed.Progress.MatchCount == 1 && mixed.Progress.UnverifiedCandidateCount == 2 && mixed.Progress.DeliveredCandidateCount == 3,
            "Normal validation counts confirmed and unresolved deliveries independently and rejects only Mismatch");
        check(mixed.Candidates.Select(c => c.ValidationDisposition).SequenceEqual([SearchDisposition.Match, SearchDisposition.Unknown, SearchDisposition.Unsupported]),
            "Delivered candidates retain distinct validation dispositions");
        var unresolved = mixed.Candidates.Where(c => c.IsUnverified).ToArray();
        check(unresolved.All(c => c.ValidationIssue.Length > 0 && c.Facts is { Neow: null, World: null, Relics: null, CombatRewards: null }),
            "Unresolved deliveries retain a reason and no invented domain observation");
        check(unresolved.All(c => c.MatchEvidence.Count == 0 && c.MatchedRouteIds.Count == 0 && c.Witnesses.Count == 0),
            "Unresolved deliveries contain no confirmation evidence, route or witness");
        // TryCreateFromRootHash normalizes None to Neow request intent. Domains
        // do not assert observations; canonical identity and the empty result
        // payload above establish that this is an input-only candidate.
        check(unresolved.All(c => c.Facts?.OriginalSeed == c.Seed && c.Facts.CanonicalSeed == c.Seed &&
            ReferenceEquals(c.Authority, plan.Model.Authority) && c.Facts.Party is null),
            "Unresolved deliveries bind canonical input and frozen authority without an opening observation");
        check(unresolved.All(c => !c.IsUnfiltered), "Attempted validation is not wholly unfiltered seed enumeration");
        var unknown = mixed.Candidates[1];
        check(unknown.ValidationIssue.StartsWith("UnresolvedFixture\n", StringComparison.Ordinal) &&
            unknown.ValidationIssue.Contains("route=FixtureRoute;disposition=Unsupported;reason=RouteUnsupportedRootCause", StringComparison.Ordinal),
            "Unresolved candidate retains the stable code first and each route's actual missing-support cause");
        check(unknown.IsUnverified &&
            !(unknown with { Evaluation = SearchQueryEvaluation.Match() }).IsUnverified,
            "Explicit validation disposition owns the compatibility flag");
        var legacy = new SearchCandidate(unknown.Seed, unknown.ProfileId, unknown.CharacterKey, unknown.Ascension,
            unknown.SnapshotFingerprint, unknown.Authority, SearchQueryEvaluation.NotEvaluatedByPolicy("FixtureSkipped"));
        check(legacy.IsUnverified && legacy.ValidationDisposition == SearchDisposition.NotEvaluatedByPolicy &&
            (legacy with { Evaluation = SearchQueryEvaluation.Match() }).ValidationDisposition == SearchDisposition.Match,
            "Explicit skipped and confirmed evaluations retain distinct candidate interpretation");
        var limited = Run([SearchDisposition.Unknown, SearchDisposition.Unsupported, SearchDisposition.Match], 2);
        check(limited.Progress.State == SearchRunState.Completed && limited.Candidates.Length == 2 &&
            limited.Progress.MatchCount == 0 && limited.Progress.UnverifiedCandidateCount == 2 && limited.Progress.DeliveredCandidateCount == 2,
            "Target stops at actual unresolved deliveries without waiting for confirmed matches");
        var withoutPayload = Run([SearchDisposition.Match], 1, "missingMatch");
        check(withoutPayload.Progress.MatchCount == 1 && withoutPayload.Candidates.Length == 1 &&
            withoutPayload.Candidates[0].Facts is null && !withoutPayload.Candidates[0].IsUnverified &&
            withoutPayload.Progress.PendingCandidateCount == 0,
            "A completed model Match remains confirmed without optional prediction payload");
        var diagnosticOnly = Run([SearchDisposition.Match], 1, "diagnostic");
        check(diagnosticOnly.Progress.State == SearchRunState.Completed && diagnosticOnly.Candidates.Length == 1 &&
            diagnosticOnly.Progress.MatchCount == 1 && diagnosticOnly.Progress.PendingCandidateCount == 0 &&
            !diagnosticOnly.Candidates[0].IsUnverified && diagnosticOnly.Candidates[0].Facts!.Diagnostics.Any(d =>
                d.Code == PredictionDiagnosticCodes.Exception && d.Value == "InjectedDiagnosticOnly"),
            "Auxiliary diagnostic text remains available without reversing a completed model Match");
        foreach (string fault in new[] { "throw", "predictionFailure", "unexpectedCancel" }) {
            var failed = Run([SearchDisposition.Unknown], 1, fault);
            check(failed.Progress.State == SearchRunState.Completed && failed.Candidates.Length == 0 && failed.Cursor == 4 &&
                failed.Progress.PendingCandidateCount == 4 && failed.Progress.DeliveredCandidateCount == 0 &&
                failed.Pending.Count == 4 && failed.Pending.All(pending => pending.ExceptionText.Contains(fault switch {
                    "throw" => "InjectedValidationFault", "predictionFailure" => "InjectedPredictionFault",
                    _ => "InjectedUnrequestedCancellation" }, StringComparison.Ordinal)),
                "Failed attempts retain their original cause and remain pending without target credit: " + fault);
        }
    }
    private sealed class LifecycleFixture(string fault, string id = "LifecycleFixture") : IFilterExecution
    {
        internal int Calls;
        private FilterBodyDescription? _body;
        public FilterBodyDescription Body => _body ??= new(new PreparedFilter(id)
        { AnalyticalCost = new(id, 64, [], "fixture"), Survival = FamilySurvivalProjection.Unresolved(id, "fixture"),
            HasConditionalProjections = true, SurvivalResolver = _ => fault == "survival"
                ? throw new IOException("InjectedAuxiliarySurvivalFailure") : FamilySurvivalProjection.Resolved(id, 0, "fixture") },
            "LifecycleFixture", RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
        {
            WindowCapacity = 64, CpuChunkCapacity = 64,
            PerformanceProjection = _ => new(id, "LifecycleFixture", "fixture", 1, "fixture", usesGpu: false),
            Cost = new() {
                Quote = (_, _, _) => fault == "quote" ? throw new IOException("InjectedAuxiliaryQuoteFailure") : null,
                MinimumComparableInput = _ => fault == "observation" ? throw new IOException("InjectedAuxiliaryObservationFailure") : 64,
                CalibrationQuote = _ => fault == "calibration" ? throw new InvalidOperationException("InjectedCalibrationObservationFailure") : null,
                CanonicalWorkShape = _ => fault == "evidence" ? throw new InvalidOperationException("InjectedEvidenceFailure") : null
            },
            CreateExecution = (_, _) => this
        };
        public static implicit operator FilterBodyDescription(LifecycleFixture fixture) => fixture.Body;
        public bool ExecutionFaulted => fault == "recovery" && Calls > 0;
        public ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
        {
            Calls++;
            var output = FamilyCandidateSet.FromSortedAbi1(input.Batch, []);
            services.Costs.ObserveCompleted(Body, input, output.Count, 1);
            return ValueTask.FromResult(output);
        }
        public ValueTask DisposeAsync(FilterRuntimeServices services) => fault == "cleanup"
            ? ValueTask.FromException(new InvalidOperationException("InjectedCleanupFailure")) : ValueTask.CompletedTask;
    }
}
