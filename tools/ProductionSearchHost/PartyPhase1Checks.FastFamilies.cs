using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Infrastructure.Snapshots;
using MegaCrit.Sts2.Core.Unlocks;
using System.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckFastFamilies(ExactSearchExecutionRequest request)
    {
        string[] arguments = OS.GetCmdlineUserArgs();
        int workersArg = Array.IndexOf(arguments, "--party-cost-workers");
        int workers = workersArg >= 0 && workersArg + 1 < arguments.Length
            ? int.Parse(arguments[workersArg + 1], System.Globalization.CultureInfo.InvariantCulture) : 4;
        request = request with { RunOptions = request.RunOptions with { WorkerCount = workers } };
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var compiled = request.CompiledSearch;
        Check(SearchCompiler.Compile(compiled.NormalizedQuery, compiled.Context).SemanticFingerprint == compiled.SemanticFingerprint,
            "party shared canonicalization idempotent");
        var sharedMap = new StandardMapSearchCondition(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 0);
        var repeated = compiled.Query with { StandardMaps = [sharedMap], Players = compiled.Query.Players.Select(p =>
            p with { Conditions = p.Conditions with { StandardMaps = [sharedMap] } }).ToArray() };
        var bound = SearchCompiler.Compile(repeated, compiled.Context);
        Check(bound.NormalizedQuery.StandardMaps.Count == 1 && bound.PlayerSearches.Take(compiled.Query.Players.Count)
            .All(p => p.NormalizedQuery.StandardMaps.Count == 0), "one shared map predicate despite per-seat legacy copies");
        Check(bound.NormalizedQuery.Players.All(p => p.Conditions.EventSequenceConstraints.Count == 0 &&
            p.Conditions.LegacyWorld.AncientIdentityFilters.Count == 0), "shared world facts leave personal conditions");
        Check(VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, request.CanonicalStartSeed,
            out ulong positive, out _, out _), "positive ordinal");
        if (OS.GetCmdlineUserArgs().Contains("--party-world-fast-checks")) { await CheckWorldFastContexts(request.Detection); return; }
        if (OS.GetCmdlineUserArgs().Contains("--party-event-fast-checks")) { await CheckEventFastSlots(request); return; }
        if (OS.GetCmdlineUserArgs().Contains("--party-combat-openings") || OS.GetCmdlineUserArgs().Contains("--party-nrc-gpu")) { await CheckPartyCombatOpenings(request.Detection); return; }
        var cpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
        var gpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true);
        Check(cpu.Select(f => f.FamilyId).Distinct().Count() == cpu.Count, "one CPU invocation per family");
        Check(gpu.Select(f => f.FamilyId).Distinct().Count() == gpu.Count, "one GPU invocation per family");
        Check(cpu.All(f => !f.ConditionPerformance.UsesGpu), "explicit CPU respected");
        Check(cpu.Select(f => f.FamilyId).SequenceEqual(gpu.Select(f => f.FamilyId)), "same family coverage");
        if (OS.GetCmdlineUserArgs().Contains("--party-cost-measure"))
        {
            // One real captured four-seat UI query. Fixed root range and public
            // family boundary; the first invocation pays compilation/cold setup
            // and is excluded from the three reported steady observations.
            bool large = arguments.Contains("--party-cost-measure-large");
            bool sparse = arguments.Contains("--party-cost-measure-sparse");
            bool cpuOnly = arguments.Contains("--party-cost-cpu-only");
            await using var measured = new FamilyExecutionContext(requiresGpu: !cpuOnly && gpu.Any(f => f.ConditionPerformance.UsesGpu),
                failOnPhysicalRecovery: true);
            try
            {
                foreach (var (mode, families, count) in cpuOnly
                    ? new[] { ("CPU", cpu, 8192) }
                    : large
                    ? new[] { ("GPU", gpu, 1048576) }
                    : sparse ? new[] { ("CPU", cpu, 64), ("GPU", gpu, 64) }
                    : new[] { ("CPU", cpu, 8192), ("GPU", gpu, 262144) })
                foreach (var family in families)
                {
                    var batch = new SearchBatch(positive, count);
                    var input = FamilyCandidateSet.Dense(batch);
                    var window = new FamilyObservationWindow(batch, request);
                    await family.InvokeAsync(measured, window, input, default);
                    var times = new double[3];
                    int survivors = 0;
                    for (int repeat = 0; repeat < times.Length; repeat++)
                    {
                        var watch = Stopwatch.StartNew();
                        var output = await family.InvokeAsync(measured, window, input, default);
                        times[repeat] = watch.Elapsed.TotalMilliseconds;
                        survivors = output.Count;
                    }
                    GD.Print($"PARTY_COST_MEASURE mode={mode};family={family.FamilyId};players=4;workers={workers};" +
                        $"start={positive};roots={count};warmup=1;repeat=3;" +
                        $"boundary=InvokeAsyncCanonicalAbi1Ready;ms={string.Join(',', times.Select(x => x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)))};" +
                        $"survivors={survivors};gpu={family.ConditionPerformance.UsesGpu};" +
                        $"setupMs={family.CapturePerformanceObservation().SetupMs:F4}");
                }
            }
            finally
            {
                foreach (var family in gpu.Concat(cpu)) await family.DisposeAsync(measured);
            }
            foreach (bool gpuMode in new[] { false, true })
            {
                var selected = FamilyExecutionCoordinator.Plan(request, gpuMode);
                GD.Print($"PARTY_COST_PLAN gpu={gpuMode};policy={selected.SelectionPolicyId};" +
                    $"terminal={selected.EstimatedTerminalSurvival};" +
                    $"stageSurvival={string.Join(',', selected.Stages.Select(s => s.Family.FamilyId + ':' + s.Survival))};" +
                    $"missing={string.Join(',', selected.MissingEvidence)}");
            }
            foreach (string shape in new[] { "S-only", "M-only", "M-3act" })
            {
                SearchQuery original = request.CompiledSearch.NormalizedQuery;
                SearchQuery subset = SearchQuery.Empty with
                {
                    Players = original.Players.Select(player => player with
                    {
                        Offers = ModelKeySetFilter.Empty,
                        Results = [],
                        SelectedOption = null,
                        Conditions = shape == "S-only" && player.Slot == 0
                            ? SearchQuery.Empty with
                            { MerchantColorlessSequenceConditions = player.Conditions.MerchantColorlessSequenceConditions }
                            : SearchQuery.Empty
                    }).ToArray(),
                    StandardMaps = shape == "M-only" ? original.StandardMaps :
                        shape == "M-3act" ? Enumerable.Range(1, 3)
                            .Select(act => original.StandardMaps[0] with { Scope = act }).ToArray() : []
                };
                var compiledSubset = SearchCompiler.Compile(subset, request.CompiledSearch.Context);
                var subsetRequest = ExactSearchExecutionRequestFactory.Compile(compiledSubset,
                    new(request.CanonicalStartSeed, 1_000_000, 1, request.WorkerCount)).Plan!;
                var selected = FamilyExecutionCoordinator.Plan(subsetRequest, !cpuOnly);
                var eta = FamilySearchEtaProjectorV1.Project(subsetRequest, selected,
                    liveExactMsPerAttempt: shape == "S-only" ? .01 : null);
                Check(eta.Status == FamilySearchEtaStatus.Ready && eta.TargetSearchMeanMs is > 0 &&
                    eta.FixedScanRangeMs is > 0, "party price and ETA projection: " + shape);
                GD.Print($"PARTY_COST_ETA shape={shape};plan={selected.PlanId};" +
                    $"status={eta.Status};p={eta.AcceptedResultProbability};" +
                    $"scanMs={eta.FixedScanRangeMs};targetMs={eta.TargetSearchMeanMs};" +
                    $"targetRoots={eta.TargetMeanRootHorizon};" +
                    $"policy={selected.SelectionPolicyId}");
                if (shape.StartsWith("M-", StringComparison.Ordinal) && arguments.Contains("--party-map-cost-measure"))
                {
                    ulong mapStart = positive + (shape == "M-3act" ? 1024UL : 0UL);
                    for (ulong i = 0; i < 64; i++)
                    {
                        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, mapStart + i);
                        _ = ProductionExactSearchEvaluator.Evaluate(subsetRequest,
                            TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
                    }
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        var watch = Stopwatch.StartNew();
                        int hits = 0;
                        for (ulong i = 0; i < 64; i++)
                        {
                            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance,
                                mapStart + (ulong)(repeat + 1) * 64 + i);
                            if (ProductionExactSearchEvaluator.Evaluate(subsetRequest,
                                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch) hits++;
                        }
                        GD.Print($"PARTY_MAP_COST shape={shape};repeat={repeat};roots=64;start={mapStart + (ulong)(repeat + 1) * 64};" +
                            $"elapsedMs={watch.Elapsed.TotalMilliseconds:F4};hits={hits};" +
                            "boundary=WholeTableProductionExact;warmup=64DisjointRoots;noFastFamily;noCompilation");
                    }
                }
            }
            if (arguments.Contains("--party-map-seat-cost-measure"))
            {
                foreach (int seats in new[] { 2, 3, 4 })
                {
                    string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive);
                    var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed,
                        BaseGameModelKeys.Characters.All.Take(seats).ToArray(),
                        Enumerable.Range(0, seats).Select(_ => UnlockState.all.ToSerializable()).ToArray(),
                        10, "0.111.0");
                    var context = SearchContextFactory.From(RuntimeProfileId.Beta111,
                        party.Players[0].Character.CharacterKey, 10, party.Players[0], request.Detection,
                        AncientOptionConditionProfile.BroadDefault) with { Party = party };
                    foreach (int acts in new[] { 1, 3 })
                    {
                        var map = SearchQuery.Empty with
                        {
                            Players = party.Players.Select(p => new PlayerOfferQuery(p.PlayerSlotIndex,
                                ModelKeySetFilter.Empty)).ToArray(),
                            StandardMaps = Enumerable.Range(1, acts)
                                .Select(act => new StandardMapSearchCondition(act,
                                    StandardMapMetric.GuaranteedMonster,
                                    StandardMapComparison.AtLeast, 0)).ToArray()
                        };
                        var compiledMap = SearchCompiler.Compile(map, context);
                        var mapRequest = ExactSearchExecutionRequestFactory.Compile(compiledMap,
                            new(seed, 1_000_000, 1, workers)).Plan!;
                        for (ulong i = 0; i < 64; i++)
                        {
                            string warmSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance,
                                positive + i);
                            _ = ProductionExactSearchEvaluator.Evaluate(mapRequest,
                                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, warmSeed));
                        }
                        for (int repeat = 0; repeat < 3; repeat++)
                        {
                            var watch = Stopwatch.StartNew();
                            for (ulong i = 0; i < 64; i++)
                            {
                                string testSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance,
                                    positive + (ulong)(repeat + 1) * 64 + i);
                                _ = ProductionExactSearchEvaluator.Evaluate(mapRequest,
                                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, testSeed));
                            }
                            GD.Print($"PARTY_MAP_SEAT_COST seats={seats};acts={acts};repeat={repeat};" +
                                $"roots=64;elapsedMs={watch.Elapsed.TotalMilliseconds:F4};" +
                                "boundary=WholeTableProductionExact;warmup=64DisjointRoots");
                        }
                    }
                }
            }
            GD.Print("PARTY_COST_MEASURE_PASS");
            return;
        }
        foreach (string family in new[] { "S.MerchantShopColorless", "R.Relic", "A.AncientOption", "E.EventResult" })
            Check(gpu.Any(f => f.FamilyId == family && f.ConditionPerformance.UsesGpu), "GPU registered: " + family);
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        try
        {
            var batch = new SearchBatch(positive, 2051);
            foreach (var input in new[] { FamilyCandidateSet.Dense(batch),
                FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, 1026).Select(i => (ulong)i * 2).ToArray()),
                FamilyCandidateSet.FromSortedAbi1(batch, []) })
            {
                var window = new FamilyObservationWindow(batch, request);
                for (int i = 0; i < gpu.Count; i++)
                {
                    var a = await cpu[i].InvokeAsync(execution, window, input, default);
                    var b = await gpu[i].InvokeAsync(execution, window, input, default);
                    Check(a.EnumerateLogicalOrdinals().SequenceEqual(b.EnumerateLogicalOrdinals()), "CPU/GPU full population: " + gpu[i].FamilyId);
                    if (input.Count > 0) Check(b.EnumerateLogicalOrdinals().Contains(0UL), "Exact-positive retained: " + gpu[i].FamilyId);
                    else Check(b.Count == 0, "empty input: " + gpu[i].FamilyId);
                    GD.Print($"PARTY_FAST_PARITY family={gpu[i].FamilyId};compact={!input.IsDense};inputs={input.Count};survivors={b.Count};gpu={gpu[i].ConditionPerformance.UsesGpu}");
                }
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            foreach (var family in gpu)
            {
                bool cancelled = false;
                try { await family.InvokeAsync(execution, new(batch, request), FamilyCandidateSet.Dense(batch), cancel.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "cancel: " + family.FamilyId);
            }
        }
        finally
        {
            foreach (var family in gpu.Concat(cpu)) await family.DisposeAsync(execution);
        }
        await using var session = FamilyExecutionCoordinator.Start(request, gpuAvailable: true, failOnPhysicalRecovery: true);
        await session.Completion;
        Check(session.TryReadCandidate(out var candidate) && candidate?.Seed == request.CanonicalStartSeed, "grouped families to whole-table Exact");
        GD.Print($"PARTY_FAST_PASS checks={checks};familyMajor=true;denseCompactEmpty=true;noThroughputClaim=true");
    }

    private static async Task CheckEventFastSlots(ExactSearchExecutionRequest parent)
    {
        int cases = 0, positives = 0;
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (int slot in new[] { 1, 3 })
        foreach (var kind in new[] { EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,
            EventResultConditionKind.SymbioteInitialBasicTransform, EventResultConditionKind.AromaOfChaosInitialBasicTransform,
            EventResultConditionKind.WhisperingHollowInitialBasicTransform, EventResultConditionKind.TrialNondescriptInitialBasicsContains })
        {
            var context = parent.CompiledSearch.PlayerSearches[slot].Context;
            var scenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(context.Authority, kind, UnlockState.all);
            var target = scenario.Targets[0].OrderedSourceCandidates![0].CardKey;
            var query = SearchQuery.Empty with { EventResultConditions = [new(kind, target) { MorphicGroveScenario = scenario }] };
            var request = ExactSearchExecutionRequestFactory.ForPlayer(parent, SearchCompiler.CompilePlayer(query, context));
            if (!EventResultFamily.TryCreate(request, out var family) || family is not EventResultFamily events || !events.Plan.GpuSupported)
                throw new Exception("Party event GPU not admitted: " + kind);
            try
            {
                var batch = new SearchBatch(0, 1027);
                var expected = Enumerable.Range(0, batch.BatchCandidateCount).Select(i => (ulong)i).Where(i =>
                    events.Plan.Reference(Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i)))).ToArray();
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 514).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var result = await family.InvokeAsync(execution, new(batch, request), input, default);
                    if (!result.EnumerateLogicalOrdinals().SequenceEqual(expected.Where(i => input.IsDense || i % 2 == 0)))
                        throw new Exception($"Party event source/GPU mismatch: {slot}/{kind}");
                    cases++; positives += result.Count;
                }
                GD.Print($"PARTY_EVENT_FAST slot={slot};kind={kind};sourceMatches={expected.Length};denseCompact=true");
                if (slot == 1 && OS.GetCmdlineUserArgs().Contains("--party-event-cost-measure"))
                {
                    IFamilyInvocation cpu = ((IFamilyInvocation)family).CpuRealizations.First();
                    try
                    {
                        var cpuBatch = new SearchBatch(0, 8192);
                        var cpuDense = FamilyCandidateSet.Dense(cpuBatch);
                        await cpu.InvokeAsync(execution, new(cpuBatch, request), cpuDense, default);
                        for (int repeat = 0; repeat < 3; repeat++)
                        {
                            var watch = Stopwatch.StartNew();
                            var result = await cpu.InvokeAsync(execution, new(cpuBatch, request), cpuDense, default);
                            GD.Print($"PARTY_EVENT_CPU_COST kind={kind};slot=1;repeat={repeat};" +
                                $"roots=8192;elapsedMs={watch.Elapsed.TotalMilliseconds:F4};survivors={result.Count};" +
                                "boundary=InvokeAsyncCanonicalAbi1Ready;warmup=1;noExact");
                        }
                    }
                    finally { await cpu.DisposeAsync(execution); }
                    var full = new SearchBatch(0, 262144);
                    var dense = FamilyCandidateSet.Dense(full);
                    await family.InvokeAsync(execution, new(full, request), dense, default);
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        var watch = Stopwatch.StartNew();
                        var result = await family.InvokeAsync(execution, new(full, request), dense, default);
                        GD.Print($"PARTY_EVENT_COST kind={kind};slot=1;repeat={repeat};" +
                            $"roots=262144;elapsedMs={watch.Elapsed.TotalMilliseconds:F4};survivors={result.Count};" +
                            "boundary=InvokeAsyncCanonicalAbi1Ready;warmup=1;noExact");
                    }
                }
            }
            finally { await family.DisposeAsync(execution); }
        }
        if (positives == 0) throw new Exception("Party event fixtures have no positive evidence");
        GD.Print($"PARTY_EVENT_FAST_PASS cases={cases};positives={positives}");
    }
}
