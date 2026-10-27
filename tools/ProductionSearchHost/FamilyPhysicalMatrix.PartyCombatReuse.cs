using System.Diagnostics;
using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class FamilyPhysicalMatrix
{
    // C2 only: original per-route body, 512 versus validated actual bag capacity.
    // The old capacity A/B cannot describe a body that no longer observes a bag.
    // Retain the historical source for provenance, but never publish duplicate
    // no-op variants as new performance evidence.
    private static void RequireLatentCapsuleBagExperiment() => throw new InvalidOperationException(
        "MP-C2 latent Capsule bag experiment retired by PartyQueryLiteralOpening.20261027.v1; " +
        "validate current C through the family-combat exported GPU parity fixtures.");

    internal static void RunPartyCombatReuseChecks(ExactSearchExecutionRequest basis, string output, int capacity = 4099)
    {
        RequireLatentCapsuleBagExperiment();
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("MpC2.RealPartyContextRequired");
        if (party.Players.Count is < 2 or > 4 || capacity is < 65 or > CombatRewardGpuExecutor.Capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Directory.CreateDirectory(output);
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidDataException("MpC2.DeviceUnavailable");
        using var modules = new FamilyGpuComputeUtility.ShaderReuse(rd);
        using var reuse = modules.Activate();
        var rows = new List<object>();
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var small = BaseGameModelKeys.Relics.SmallCapsule;
        var large = BaseGameModelKeys.Relics.LargeCapsule;
        int checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new InvalidDataException("MpC2." + why); checks++; }
        for (int slot = 0; slot < party.Players.Count; slot++)
        foreach (bool capsule in slot == party.Players.Count - 1 ? new[] { true, false } : new[] { true })
        {
            ModelKey[] pair = capsule ? [small, large] : [BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.Kaleidoscope];
            string shape = $"{party.Players.Count}p-slot{slot}-" + (capsule ? "BonesCapsules" : "PrefixOnly");
            ExactSearchExecutionRequest Build(ModelKey? target, int order = 0)
            {
                ModelKey[] ordered = order == 3 ? [small] : order == 2 ? pair.AsEnumerable().Reverse().ToArray() : pair;
                var local = SearchQuery.Empty with { OpeningRoute = new(bones),
                    OpeningRouteRelicRequirement = new(bones, ordered, order is 0 or 3 ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder),
                    CombatCardRewards = target is { } card ? new(1, CombatRewardSequenceOrderMode.Ordered, [card]) : null };
                var query = SearchQuery.Empty with { Players = Enumerable.Range(0, party.Players.Count).Select(i =>
                    new PlayerOfferQuery(i, ModelKeySetFilter.Empty) { Conditions = i == slot ? local : SearchQuery.Empty }).ToArray() };
                return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                    new("000000000000", capacity, 30, 4)).Plan ?? throw new InvalidDataException("MpC2.Compile:" + shape);
            }
            foreach (int order in capsule && slot == 0 && party.Players.Count == 2 ? new[] { 0, 1, 2, 3 } : new[] { 0 })
            {
                var preliminary = Build(null, order);
                var personal = ExactSearchExecutionRequestFactory.ForPlayer(preliminary, preliminary.CompiledSearch.PlayerSearches[slot]);
                var identity = NeowReplayPlan.Compile(personal, false);
                ModelKey[] acquisitionOrder = order is 0 or 3 ? [] : order == 2 ? pair.AsEnumerable().Reverse().ToArray() : pair;
                ulong positive = 0; ModelKey? target = null; ModelKey[] sourceActualPair = [];
                // Author each ordering independently: an Any-order route cannot
                // supply the target or positive claimed by an AB/BA control.
                for (; positive < 262144; positive++)
                {
                    string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive);
                    var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                    if (!NeowFamilyReplay.Matches(input.RootHash, identity)) continue;
                    var owner = party.Players[slot];
                    Check(SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension, owner.PlayersCount, slot, owner,
                        AncientOptionConditionProfile.BroadDefault, SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards,
                        10, false, out var source, out _), "SourceRequest");
                    var streams = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, input.RootHash, slot);
                    var choices = PartyNeowProjection.ProjectChoices(source!, input.RootHash, streams.Niche, streams.CombatPotionGeneration,
                        authoredCapsuleEffects: PartyInitialQuery.CapsuleEffectPremise(personal.CompiledSearch.NormalizedQuery));
                    var predicted = NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, input.RootHash, seed, slot,
                        owner.Ascension, choices, owner.EffectAuthority, owner.WorldAuthority,
                        new(NormalCombatRewardProjectionScope.SearchExact, NormalCombatRewardRouteSelectionMode.PinnedRealRoute, bones, acquisitionOrder));
                    var route = predicted.Routes.FirstOrDefault(r => (order is 0 or 3 || r.EquivalentRoutes.Any(e => e.AcquisitionOrder.SequenceEqual(acquisitionOrder))) &&
                        (order != 3 || r.EquivalentRoutes.Any(e => e.AcquisitionOrder.Count == 2 && e.AcquisitionOrder.Contains(small))) &&
                        r.Status == SeedDomainEvaluationStatus.Evaluated &&
                        r.Precision is not (PredictionPrecision.Unknown or PredictionPrecision.Unsupported) && r.Battles.Count > 0 &&
                        r.Battles[0].Cards.Count > 0 && r.Battles[0].Cards.All(c => c.Precision is not (PredictionPrecision.Unknown or PredictionPrecision.Unsupported)));
                    if (route is null) continue;
                    target = route.Battles[0].Cards[0].CardKey;
                    // The partial query independently authors its actual root
                    // pair and target. Never borrow the fixed-pair witness.
                    if (order == 3 && !ProductionExactSearchEvaluator.Evaluate(Build(target, order), input).IsMatch)
                    { target = null; continue; }
                    if (order == 3) sourceActualPair = route.EquivalentRoutes.First(e =>
                        e.AcquisitionOrder.Count == 2 && e.AcquisitionOrder.Contains(small)).AcquisitionOrder.ToArray();
                    break;
                }
                Check(target.HasValue, "BoundedRealPositive:" + shape + ":" + order);
                var request = Build(target, order);
                var child = ExactSearchExecutionRequestFactory.ForPlayer(request, request.CompiledSearch.PlayerSearches[slot]);
                var replay = new CombatRewardReplay(child, precedingNicheDraws: 0);
                var plan = CombatRewardGpuPlan.TryCreate(child, replay, out string issue) ?? throw new InvalidDataException("MpC2.GpuPlan:" + issue);
                Check(!replay.ConservativelyKeeps && !plan.UsesHotLoop && plan.Routes == (order == 0 ? 2 : 1) &&
                    plan.Meta[51] == 0u && plan.Meta[54] == 0u && replay.UsesQueryLiteralPartyOpening == (capsule || order == 3),
                    "ActualCompiledShape:" + shape);
                string name = shape + (order == 0 ? "-Any" : order == 1 ? "-AB-Control" : order == 2 ? "-BA-Control" : "-PartialActualPair");
                var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive)));
                if (!exact.IsMatch) throw new InvalidDataException($"MpC2.ExactPositive:{name}:{positive}:{exact.Evaluation.Disposition}:{exact.FailureCode}:" +
                    JsonSerializer.Serialize(exact.Evaluation.RouteRejections, ProductionSearchReplay.JsonOptions));
                ProductionSearchReplay.Write(Path.Combine(output, name + "-workload.json"), new ProductionSearchReplayWorkload(1,
                    request.CompiledSearch.Query, request.RunOptions with { StartSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive), ScanCount = 65536 }));
                ProductionSearchReplay.Write(Path.Combine(output, name + "-context.json"), new ProductionSearchReplayContext(1,
                    "Real captured party pools; independently source-authored Bones/C positive; C2 bag capacity", context));
                var variants = MpC2Plans(plan);
                Check(variants[0].BagCapacity == 512 &&
                    variants[1].BagCapacity == (capsule ? replay.Catalog.PlayerRelicBuckets.Sum(b => b.RelicIndexes.Length) : 512),
                    "ActualBagCapacity:" + name);
                Check(variants[0].BagCapacityRevision.Length == 0 && (variants[1].BagCapacity == 512
                    ? variants[1].BagCapacityRevision.Length == 0
                    : variants[1].Revision(false).Contains($".Slot{slot}.Entries{variants[1].BagCapacity}", StringComparison.Ordinal)),
                    "CapacityRevisionIncludesSlotAndEntries");
                if (capsule)
                {
                    var narrow = variants[1];
                    var solo = narrow with { Meta = (uint[])narrow.Meta.Clone() };
                    solo.Meta[3] = 1;
                    Check(solo.BagCapacity == 512 && solo.BagCapacityRevision.Length == 0,
                        "SoloRetains512AndOriginalRevision");
                    var truncated = narrow with { Pools = narrow.Pools.Take(47).ToArray() };
                    Check(truncated.BagCapacity == 512, "MalformedBagTableFallback");
                    var empty = narrow with { Pools = (uint[])narrow.Pools.Clone() };
                    for (int b = 0; b < empty.Pools[43]; b++) empty.Pools[checked((int)empty.Pools[45]) + b * 2 + 1] = 0;
                    Check(empty.BagCapacity == 512, "EmptyBagFallback");
                    var invalidId = narrow with { Ids = (uint[])narrow.Ids.Clone() };
                    invalidId.Ids[checked((int)invalidId.Pools[checked((int)invalidId.Pools[45])])] = invalidId.Pools[44];
                    Check(invalidId.BagCapacity == 512, "InvalidBagEntryFallback");
                    var duplicate = narrow with { Ids = (uint[])narrow.Ids.Clone() };
                    int first = checked((int)duplicate.Pools[checked((int)duplicate.Pools[45])]);
                    duplicate.Ids[first + 1] = duplicate.Ids[first];
                    Check(duplicate.BagCapacity == narrow.BagCapacity, "DuplicateEntriesRetainCapacityAndWeight");
                }
                using var disabled = new MpC2Kernel(rd, variants[0], variants[0].ShaderSource(), capacity);
                using var enabled = new MpC2Kernel(rd, variants[1], variants[1].ShaderSource(), capacity);
                var kernels = new[] { disabled, enabled };
                foreach (var batch in new[] { new SearchBatch(0, capacity), new SearchBatch(positive, capacity), new SearchBatch(positive + (ulong)capacity + 17, capacity),
                    new SearchBatch(1564445371644820290UL, 65) })
                foreach (bool compact in new[] { false, true })
                {
                    uint[] input = Enumerable.Range(0, batch.BatchCandidateCount).Where(i => !compact || i % 3 == 0).Select(i => (uint)i).ToArray();
                    ulong Root(uint i) => Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, batch.BatchBase + i));
                    uint[] expected = input.Where(i => replay.Matches(Root(i))).ToArray();
                    var nPlan = NeowReplayPlan.Compile(child, false);
                    uint[] afterN = input.Where(i => NeowFamilyReplay.Matches(Root(i), nPlan)).ToArray();
                    int nThenC = afterN.Count(i => Array.BinarySearch(expected, i) >= 0);
                    if (batch.BatchBase == positive) Check(expected.Contains(0u) && afterN.Contains(0u), "SourcePositiveRetained:" + name);
                    foreach (var kernel in kernels)
                    {
                        Check(kernel.Run(new SearchBatch(0, 0), [], false, out _).Length == 0, "EmptyInput");
                        _ = kernel.Run(batch, input, compact, out _);
                    }
                    var samples = Enumerable.Range(0, 2).Select(_ => new List<double>()).ToArray();
                    for (int round = 0; round < 3; round++)
                    foreach (int variant in MpC2Order(round))
                    {
                        var actual = kernels[variant].Run(batch, input, compact, out double ms);
                        Check(actual.SequenceEqual(expected), $"FullCpuPopulation:{name}:{batch.BatchBase}:{compact}:{variant}");
                        samples[variant].Add(ms);
                    }
                    string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(FamilyGpuComputeUtility.ToBytes(expected)));
                    for (int variant = 0; variant < 2; variant++) rows.Add(new { name, players = party.Players.Count, slot, positive,
                        sourceAcquisitionOrder = acquisitionOrder, sourceActualPair, sourceTarget = target,
                        rootWindow = batch.BatchCandidateCount, batchBase = batch.BatchBase, compact, actualStageInputs = input.Length,
                        nOracleSurvivors = afterN.Length, nThenCOracleSurvivors = nThenC, nArrivalEvidence = "SameWindowCpuNFullPopulation;NotGpuPipelineTiming",
                        output = expected.Length, digest,
                        exactBagCapacity = variants[variant].ExactBagCapacity, bagCapacity = variants[variant].BagCapacity,
                        routes = plan.Routes, actualBonesPair = false, capsuleHeld = false,
                        samplesMs = samples[variant], medianMs = samples[variant].Order().ElementAt(1),
                        boundary = "CFullBodyDispatchSync;UploadReadbackSetupExcluded;NotCanonicalPeakOrProductionE2E" });
                }
                if (capsule && order == 1 && slot == 0 && party.Players.Count == 2)
                {
                    // The independently authored AB source/Exact witness proves
                    // route 0, rather than borrowing an Any-order union success.
                    var faultRequest = Build(target, 0);
                    var faultChild = ExactSearchExecutionRequestFactory.ForPlayer(faultRequest, faultRequest.CompiledSearch.PlayerSearches[slot]);
                    var faultReplay = new CombatRewardReplay(faultChild);
                    ulong faultRoot = Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive));
                    var firstOpening = faultReplay.Opening(faultRoot, 0);
                    Check(firstOpening.ExactProjectionAuthority && Beta110CombatRewardFastStage.EvaluateForFamily(firstOpening,
                        faultReplay.Plan, faultReplay.Catalog, faultChild.Ascension).Status == Beta110CombatRewardRouteProjectionStatus.ExactProjection,
                        "IndependentFirstRoutePass");
                    var faultPlan = CombatRewardGpuPlan.TryCreate(faultChild, faultReplay, out _) ?? throw new InvalidDataException("MpC2.FaultPlan");
                    Check(faultPlan.Routes == 2, "FaultProbeHasBothRoutes");
                    // Source-only probe: the original successful first route runs
                    // fully; the second call must still publish its numeric fault.
                    foreach (var faultVariant in MpC2Plans(faultPlan))
                    {
                        string faultBody = faultVariant.ShaderSource();
                        string fault = faultBody.Replace("bool c_route(uint64_t root,uint route){",
                            "bool c_route(uint64_t root,uint route){if(route==1u)return cr_family_numeric_failure();", StringComparison.Ordinal);
                        Check(fault != faultBody, "SecondRouteFaultSeam");
                        using var probe = new MpC2Kernel(rd, faultVariant, fault, capacity);
                        var actual = probe.Run(new SearchBatch(positive, 1), [0], false, out _, expectedFault: 1);
                        Check(actual.SequenceEqual(new uint[] { 0 }), "SecondFaultAfterFirstPass");
                        var malformed = faultVariant with { Meta = (uint[])faultVariant.Meta.Clone() }; malformed.Meta[7] = 1;
                        using var invalid = new MpC2Kernel(rd, malformed, faultBody, capacity);
                        Check(invalid.Run(new SearchBatch(positive, 1), [0], false, out _, expectedFault: 1).Length == 0, "BonesPrefixFault");
                        var overflow = faultVariant with { Pools = (uint[])faultVariant.Pools.Clone() };
                        overflow.Pools[checked((int)overflow.Pools[45]) + 1] = 513;
                        Check(overflow.BagCapacity == 512, "OversizeBagFallback");
                        using var overflowProbe = new MpC2Kernel(rd, overflow, overflow.ShaderSource(), capacity);
                        Check(overflowProbe.Run(new SearchBatch(positive, 1), [0], false, out _).SequenceEqual(new uint[] { 0 }),
                            "Original512OverflowConservativeKeepWithoutNumericFault");
                    }
                }
                Console.WriteLine($"MP_C2_CASE_PASS name={name};positive={positive};wholeTableExact=true;fullCpuPopulation=true;actualInputs=true");
            }
        }
        File.WriteAllText(Path.Combine(output, "mp-c1-results.json"), JsonSerializer.Serialize(new {
            Experiment = "MP-C2.ExactBagCapacity.20261016.v1", ReferenceCapacity = 512,
            FactoryExactBagCapacity = CombatRewardGpuPlan.FactoryExactBagCapacity, checks, rows }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"MP_C2_PASS players={party.Players.Count};checks={checks};rows={rows.Count};factoryExactBagCapacity={CombatRewardGpuPlan.FactoryExactBagCapacity};timing=DispatchSyncOnly");
    }

    // Uses the exported workload unchanged. Compact inputs are the registered
    // whole-table N output of this same root window, never tiled/spread samples.
    internal static void RunPartyCombatReuseBench(ExactSearchExecutionRequest basis, string output, int rootPopulation = 1 << 20)
    {
        RequireLatentCapsuleBagExperiment();
        var party = basis.CompiledSearch.Context.Party ?? throw new InvalidDataException("MpC2Bench.RealPartyRequired");
        if (party.Players.Count is < 2 or > 4 || (rootPopulation != (1 << 16) && rootPopulation != (1 << 20) && rootPopulation != (1 << 22)))
            throw new ArgumentOutOfRangeException(nameof(rootPopulation));
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        ulong start = 0;
        foreach (char c in basis.CanonicalStartSeed) start = checked(start * 34 + (uint)alphabet.IndexOf(c));
        var batch = new SearchBatch(start, rootPopulation);
        var positive = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, basis.CanonicalStartSeed);
        var exact = ProductionExactSearchEvaluator.Evaluate(basis, positive);
        if (!exact.IsMatch) throw new InvalidDataException($"MpC2Bench.ExactPositive:{exact.FailureCode}:{exact.Evaluation.Disposition}");
        Directory.CreateDirectory(output);
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(basis, true);
        var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true, cpuWorkerCount: basis.WorkerCount);
        try
        {
            var c = families.Single(f => f.FamilyId == "C.CombatReward");
            var active = c is PartyFamily table ? table.PersonalStages.ToArray() : throw new InvalidDataException("MpC2Bench.PartyWrapperRequired");
            if (active.Length != 1 || active[0].Family is not CombatRewardFamily child)
                throw new InvalidDataException("MpC2Bench.UniqueActiveCombatChildRequired");
            var plan = child.GpuPlan ?? throw new InvalidDataException("MpC2Bench.OriginalGpuPlanUnavailable");
            if (plan.UsesHotLoop || child.Replay.ConservativelyKeeps || plan.Meta[3] <= 1)
                throw new InvalidDataException("MpC2Bench.OutsideGenericPartyShape");
            var n = families.Single(f => f.FamilyId == "N.Neow");
            if (!n.ConditionPerformance.UsesGpu) throw new InvalidDataException("MpC2Bench.RegisteredNGpuRequired");
            var dense = FamilyCandidateSet.Dense(batch);
            var afterN = Task.Run(async () => await n.InvokeAsync(execution, new(batch, basis), dense, default)
                .ConfigureAwait(false)).GetAwaiter().GetResult();
            if (n.ExecutionFaulted || afterN.Batch != batch || !afterN.EnumerateLogicalOrdinals().Contains(0UL))
                throw new InvalidDataException("MpC2Bench.ActualNInputOrPositiveFailed");
            var compact = FamilyCandidateSet.FromSortedAbi1(batch, afterN.EnumerateLogicalOrdinals().ToArray());
            using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidDataException("MpC2Bench.DeviceUnavailable");
            using var modules = new FamilyGpuComputeUtility.ShaderReuse(rd);
            using var reuseScope = modules.Activate();
            var variants = MpC2Plans(plan);
            using var reference = new CombatRewardGpuExecutor(rd, variants[0]);
            using var candidate = new CombatRewardGpuExecutor(rd, variants[1]);
            var executors = new[] { reference, candidate };
            var rows = new List<object>();
            var medians = new List<object>();
            FamilyCandidateSet? compactStable = null;
            foreach (var input in new[] { dense, compact })
            {
                FamilyCandidateSet? stable = null;
                var samples = Enumerable.Range(0, 2).Select(_ => new List<double>()).ToArray();
                for (int round = -1; round < 5; round++)
                foreach (int variant in MpC2Order(round))
                {
                    var result = executors[variant].Execute(input, default, out var metrics);
                    stable ??= result;
                    if (result.Batch != batch || metrics.Input != input.Count || metrics.Output != result.Count ||
                        !result.ExportAbi1().Span.SequenceEqual(stable.ExportAbi1().Span) ||
                        !result.EnumerateLogicalOrdinals().Contains(0UL))
                        throw new InvalidDataException("MpC2Bench.FullAbi1OrPositiveParity");
                    string digest = Digest(result);
                    if (round >= 0) samples[variant].Add(metrics.CanonicalMs);
                    rows.Add(new { Compact = !input.IsDense, Round = round, Warmup = round < 0, Variant = variant,
                        ExactBagCapacity = variants[variant].ExactBagCapacity,
                        BagCapacity = variants[variant].BagCapacity,
                        RootWindow = rootPopulation, ActualStageInput = metrics.Input, NSurvivors = compact.Count,
                        Output = result.Count, metrics.Dispatches, metrics.ReadbackBytes, metrics.UploadMs,
                        metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.CanonicalMs, Digest = digest, FullAbi1Parity = true });
                    Console.WriteLine($"MP_C2_BENCH_ROUND experiment=C2;players={party.Players.Count};slot={active[0].Slot};compact={!input.IsDense};round={round};variant={variant};exactBagCapacity={variants[variant].ExactBagCapacity};bagCapacity={variants[variant].BagCapacity};actualStageInput={metrics.Input};nSurvivors={compact.Count};output={result.Count};canonicalMs={metrics.CanonicalMs:G9};digest={digest}");
                }
                for (int variant = 0; variant < 2; variant++) medians.Add(new { Compact = !input.IsDense, Variant = variant,
                    ExactBagCapacity = variants[variant].ExactBagCapacity,
                    BagCapacity = variants[variant].BagCapacity,
                    ActualStageInput = input.Count, MedianCanonicalMs = samples[variant].Order().ElementAt(2) });
                if (!input.IsDense) compactStable = stable;
            }
            // Sustained configured CPU wrapper comparison on the exact N output.
            // Task.Run avoids blocking Godot's context; its queue time is outside
            // the timer. InvokeAsync includes worker permits, filtering, canonical
            // ordering, wrapper composition and the returned ABI1 boundary.
            var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(basis, false);
            var cpuExecution = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true, cpuWorkerCount: basis.WorkerCount);
            var cpuRows = new List<object>();
            var cpuSamples = new List<double>();
            string cpuRevision = "", cpuChildRevision = "";
            int cpuWorkers = FamilyCpuExecution.ConfiguredWorkers(basis);
            try
            {
                var cpu = cpuFamilies.Single(f => f.FamilyId == "C.CombatReward");
                if (cpu is not PartyFamily cpuTable || cpu.ConditionPerformance.UsesGpu)
                    throw new InvalidDataException("MpC2Bench.RegisteredCpuWrapperRequired");
                var cpuStages = cpuTable.PersonalStages.ToArray();
                if (cpuStages.Length != 1 || cpuStages[0].Slot != active[0].Slot ||
                    cpuStages[0].Family is not FamilyCpuExecution cpuChild || cpuChild.Workers != cpuWorkers)
                    throw new InvalidDataException("MpC2Bench.CpuSlotOrConfiguredWorkers");
                cpuRevision = cpu.ResolveConditionPerformance(true).PhysicalImplementationRevision;
                cpuChildRevision = cpuStages[0].Family.ResolveConditionPerformance(true).PhysicalImplementationRevision;
                for (int round = -1; round < 5; round++)
                {
                    var measured = Task.Run(async () =>
                    {
                        long begin = Stopwatch.GetTimestamp();
                        var result = await cpu.InvokeAsync(cpuExecution, new(batch, basis), compact, default).ConfigureAwait(false);
                        return (Result: result, CanonicalMs: Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
                    }).GetAwaiter().GetResult();
                    if (cpu.ExecutionFaulted || cpuExecution.CaptureFilterIsolations(batch).Count != 0 ||
                        compactStable is null || measured.Result.Batch != batch ||
                        !measured.Result.ExportAbi1().Span.SequenceEqual(compactStable.ExportAbi1().Span))
                        throw new InvalidDataException("MpC2Bench.RegisteredCpuFullAbi1Parity");
                    if (round >= 0) cpuSamples.Add(measured.CanonicalMs);
                    string digest = Digest(measured.Result);
                    cpuRows.Add(new { Round = round, Warmup = round < 0, ActualStageInput = compact.Count,
                        ConfiguredWorkers = cpuWorkers, Output = measured.Result.Count, measured.CanonicalMs,
                        Digest = digest, FullAbi1ParityWithBothGpuVariants = true });
                    Console.WriteLine($"MP_C2_CPU_COMPACT_ROUND players={party.Players.Count};slot={active[0].Slot};round={round};workers={cpuWorkers};actualStageInput={compact.Count};output={measured.Result.Count};canonicalMs={measured.CanonicalMs:G9};digest={digest}");
                }
            }
            finally
            {
                Task.Run(async () =>
                {
                    try { foreach (var family in cpuFamilies) await family.DisposeAsync(cpuExecution).ConfigureAwait(false); }
                    finally { await cpuExecution.DisposeAsync().ConfigureAwait(false); }
                }).GetAwaiter().GetResult();
            }
            string Hash(CombatRewardGpuPlan p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(p.ShaderSource())));
            ProductionSearchReplay.Write(Path.Combine(output, "mp-c1-bench-workload.json"), new ProductionSearchReplayWorkload(1,
                basis.CompiledSearch.Query, basis.RunOptions with { StartSeed = basis.CanonicalStartSeed, ScanCount = rootPopulation }));
            ProductionSearchReplay.Write(Path.Combine(output, "mp-c1-bench-context.json"), new ProductionSearchReplayContext(1,
                "Original exported request; registered real party N input; production executor C2 capacity A/B", basis.CompiledSearch.Context));
            File.WriteAllText(Path.Combine(output, "mp-c1-bench.json"), JsonSerializer.Serialize(new {
                Players = party.Players.Count, Slot = active[0].Slot, RootBatchBase = start, RootWindow = rootPopulation,
                PositiveBatchLocalOrdinal = 0, WholeTableExactPositive = true, NSurvivors = compact.Count,
                NArrivalFraction = compact.Count / (double)rootPopulation, NInputEvidence = "RegisteredWholeTableNGpu;SameCompleteQueryAndWindow",
                NInputDigest = Digest(compact), NRevision = n.ConditionPerformance.PhysicalImplementationRevision,
                plan.Routes, CapsuleHeldReplay = false, child.Replay.PrecedingNicheDraws,
                ExperimentRole = plan.Routes == 2 ? "FixedAuthoredDualRoute" : "SingleRouteNegativeControl",
                ActualBonesPair = false,
                CapacityMacro = "RT2_C_BAG_CAPACITY", ProductionDefault = CombatRewardGpuPlan.FactoryExactBagCapacity,
                DefaultBagCapacity = 512, ExperimentRevision = "MP-C2.ExactBagCapacity.20261016.v1",
                DenseRevision = plan.Revision(false), CompactRevision = plan.Revision(true),
                ReferenceSourceSha256 = Hash(variants[0]), CandidateSourceSha256 = Hash(variants[1]), Device = reference.Device,
                ReferenceSetupMs = reference.SetupMs, CandidateSetupMs = candidate.SetupMs,
                Variants = variants.Select((v, i) => new { Variant = i, v.ExactBagCapacity, v.BagCapacity,
                    DenseRevision = v.Revision(false), CompactRevision = v.Revision(true),
                    SetupMs = executors[i].SetupMs, SourceSha256 = Hash(v) }).ToArray(),
                Boundary = "ProductionCombatRewardGpuExecutor.CanonicalAbi1Ready;OneWarmFiveAlternatingRounds;SetupExcluded;NotE2EOrPeak",
                CompactGeometry = "ActualNOutput;MayBeUnderfilled;NoTilingOrRenumbering", Rows = rows, Medians = medians,
                CpuCompactComparison = new { ActualStageInput = compact.Count, ConfiguredWorkers = cpuWorkers,
                    Revision = cpuRevision, ChildRevision = cpuChildRevision,
                    Source = "CreateRegisteredFamilies(basis,false).C.CombatReward;CompletePartyFamilyWrapper;SameQuerySlotWorkers",
                    Boundary = "RegisteredCpuCWrapper.InvokeAsync.CanonicalAbi1Ready;TaskRunQueueAndDigestExcluded;SeparateOneWarmFiveSustainedInvocations;CompactOnly;NotE2E",
                    MedianCanonicalMs = cpuSamples.Order().ElementAt(2), Rows = cpuRows }
            }, ProductionSearchReplay.JsonOptions));
            Console.WriteLine($"MP_C2_BENCH_PASS players={party.Players.Count};slot={active[0].Slot};nSurvivors={compact.Count};factoryExactBagCapacity={CombatRewardGpuPlan.FactoryExactBagCapacity};fullAbi1Parity=true");
        }
        finally
        {
            Task.Run(async () =>
            {
                try { foreach (var family in families) await family.DisposeAsync(execution).ConfigureAwait(false); }
                finally { await execution.DisposeAsync().ConfigureAwait(false); }
            }).GetAwaiter().GetResult();
        }
    }

    // Reference retains the original 512 body; only array capacity changes.
    private static CombatRewardGpuPlan[] MpC2Plans(CombatRewardGpuPlan plan) => [
        plan with { ExactBagCapacity = false }, plan with { ExactBagCapacity = true }];
    private static int[] MpC2Order(int round) => round % 2 == 0 ? [0, 1] : [1, 0];

    private sealed class MpC2Kernel : IDisposable
    {
        private readonly RenderingDevice _rd;
        private readonly List<Rid> _owned = [];
        private readonly Rid[] _buffers;
        private readonly Rid _pipeline, _uniforms;
        private readonly int _capacity, _routes;
        internal MpC2Kernel(RenderingDevice rd, CombatRewardGpuPlan plan, string source, int capacity)
        {
            _rd = rd; _capacity = capacity; _routes = plan.Routes;
            Rid Add(Rid rid) { _owned.Add(rid); return rid; }
            Rid Buffer(uint[] words) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(words), "MpC2"));
            try
            {
                var shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "MpC2"));
                _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "MpC2"));
                _buffers = [Buffer(new uint[8]), Buffer(plan.Meta), Buffer([0]), Buffer(plan.Pools), Buffer(plan.Ids), Buffer([0]),
                    Buffer(plan.Predicates), Buffer(plan.Targets), Buffer(new uint[8]), Buffer(new uint[capacity]), Buffer(new uint[capacity])];
                _uniforms = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, _buffers, "MpC2"));
            }
            catch (Exception ex) { FamilyGpuComputeUtility.CleanupAfterFailure(ex, Dispose, "MpC2"); throw; }
        }
        internal uint[] Run(SearchBatch batch, uint[] input, bool compact, out double ms, uint expectedFault = 0)
        {
            if (input.Length == 0) { ms = 0; return []; }
            FamilyGpuComputeUtility.Update(_rd, _buffers[0], [(uint)batch.BatchBase, (uint)(batch.BatchBase >> 32),
                (uint)batch.BatchCandidateCount, (uint)input.Length, compact ? 1u : 0u, (uint)_capacity, 0u, (uint)_routes], "MpC2.Batch");
            FamilyGpuComputeUtility.Update(_rd, _buffers[8], [0x43464d52, 1, 0, 0, 0, 0x43463031, 0, 0], "MpC2.Header");
            FamilyGpuComputeUtility.Update(_rd, _buffers[10], input, "MpC2.Input");
            long list = _rd.ComputeListBegin(); _rd.ComputeListBindComputePipeline(list, _pipeline); _rd.ComputeListBindUniformSet(list, _uniforms, 0);
            _rd.ComputeListDispatch(list, (uint)((input.Length + 63) / 64), 1, 1); _rd.ComputeListEnd();
            long start = Stopwatch.GetTimestamp(); _rd.Submit(); _rd.Sync(); ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            uint[] header = FamilyGpuComputeUtility.FromUInt32Bytes(_rd.BufferGetData(_buffers[8]));
            // c_route failures return only to main: every lane still reaches all
            // barriers, and lid 0 counts every admitted input regardless of fault.
            if (header[3] != expectedFault || header[4] != input.Length || header[2] > input.Length)
                throw new InvalidDataException("MpC2.HeaderOrNumericFailure:" + string.Join(',', header));
            uint[] result = header[2] == 0 ? [] : FamilyGpuComputeUtility.FromUInt32Bytes(_rd.BufferGetData(_buffers[9], 0, header[2] * 4));
            Array.Sort(result); return result;
        }
        public void Dispose() => FamilyGpuComputeUtility.FreeAll(_rd, _owned);
    }
}
