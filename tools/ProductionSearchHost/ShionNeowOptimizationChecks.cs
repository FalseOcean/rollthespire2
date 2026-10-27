using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Existing ProductionSearchHost entry point: full ABI1 equality is the oracle
// for the scanned population; bounded dense/compact populations also use CPU.
internal static class ShionNeowOptimizationChecks
{
    internal static void Run(ExactSearchExecutionRequest request, string output)
    {
        var family = (NeowFamily)FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true)
            .Single(f => f.FamilyId == "N.Neow");
        var native = family.GpuPlan ?? throw new InvalidOperationException("ShionN.NoOrdinaryGpuPlan");
        if (native.IsMultiplayer || native.DirectNestedMode != 0 || native.JointTransformMetaOffset >= 0)
            throw new InvalidOperationException("ShionN.RequiresOrdinarySolo");
        var profile = RuntimeProfileRegistry.Select(request.Detection);
        var matcher = NeowFamilyReplay.Bind(family.PricingReplayPlan);
        bool Cpu(ulong ordinal) => matcher(profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, ordinal)));
        ulong start = ParseOrdinal(request.CanonicalStartSeed);
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("ShionN.NoGpu");
        string[] modes = ["leafy-lazy", "draw-intervals", "gpu-prefix", "n-curse-first",
            "leafy-lazy,draw-intervals,gpu-prefix,n-curse-first"];
        if (System.Environment.GetEnvironmentVariable("RT2_NEOW_CHECK_MODE") == "combined") modes = [modes[^1]];
        var records = new List<object>();
        long populationChecks = 0, cpuChecks = 0;
        foreach (var numeric in new[] { NeowGpuNumericalVariant.NativeFloat64, NeowGpuNumericalVariant.IntegerUnitFloat })
        {
            var plan = native with { NumericalVariant = numeric };
            foreach (string mode in modes)
            {
                var trials = NeowGpuOptimizations.Parse(mode);
                if (trials.Applicable(plan, false).Revision.Length == 0 &&
                    trials.Applicable(plan, true).Revision.Length == 0)
                {
                    records.Add(new { mode, numeric = numeric.ToString(), applicable = false,
                        reason = "InputDoesNotExerciseTrial" });
                    continue;
                }
                using var off = new NeowFamilyGpuExecutor(rd, plan, 71, optimizations: default(NeowGpuOptimizations));
                using var on = new NeowFamilyGpuExecutor(rd, plan, 72, optimizations: trials);
                ulong? positive = null;
                long scanned = 0, survivors = 0;
                int dispatches = 0;
                int windows = checked((int)((request.ScanCount + NeowFamilyGpuExecutor.Capacity - 1) / NeowFamilyGpuExecutor.Capacity));
                if (windows > 64) throw new InvalidOperationException("ShionN.Bounded64ValidationWindows");
                for (int w = 0; w < windows; w++)
                {
                    int size = (int)Math.Min(NeowFamilyGpuExecutor.Capacity, request.ScanCount - (long)w * NeowFamilyGpuExecutor.Capacity);
                    var batch = new SearchBatch(start + (ulong)w * NeowFamilyGpuExecutor.Capacity, size);
                    var input = FamilyCandidateSet.Dense(batch);
                    var expected = off.Execute(input, CancellationToken.None, out _);
                    var actual = on.Execute(input, CancellationToken.None, out var metrics);
                    Equal(actual, expected, "FullPopulation"); populationChecks += size;
                    if (actual.Count > 0) positive ??= batch.GlobalCandidate(actual.EnumerateLogicalOrdinals().First());
                    scanned += size; survivors += actual.Count; dispatches += metrics.Dispatches;
                }
                if (positive is null) throw new InvalidDataException("ShionN.PositiveRequired:" + mode + ":" + numeric);
                if (!Cpu(positive.Value)) throw new InvalidDataException("ShionN.CpuPositive");
                foreach (int size in new[] { 1, 511, 512, 513, 4099 })
                {
                    var batch = new SearchBatch(positive.Value, size);
                    VerifyCpu(off, on, FamilyCandidateSet.Dense(batch));
                    ulong[] compact = Enumerable.Range(0, size).Where(x => x % 3 == 0 || x == size - 1).Select(x => (ulong)x).ToArray();
                    VerifyCpu(off, on, FamilyCandidateSet.FromAbi1(batch, compact));
                }
                VerifyCpu(off, on, FamilyCandidateSet.Dense(new(NeowPrefixHashCache.BlockSize - 257UL, 1027)));
                VerifyCpu(off, on, FamilyCandidateSet.Dense(new(NeowPrefixHashCache.SpaceSize - 1027UL, 1027)));
                VerifyCpu(off, on, FamilyCandidateSet.Dense(new(NeowPrefixHashCache.SpaceSize, 0)));
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    try { on.Execute(FamilyCandidateSet.Dense(new(positive.Value, 513)), canceled.Token, out _); throw new InvalidDataException("ShionN.CancelNotObserved"); }
                    catch (OperationCanceledException) { }
                }
                VerifyCpu(off, on, FamilyCandidateSet.Dense(new(positive.Value, 513)));

                // The borrower must inherit immutable options, own its table and
                // output, and retain root-local ordinals across shared scratch.
                using (var right = new NeowFamilyGpuExecutor(rd, plan, 72, sharedDenseScratch: on))
                {
                    var leftBatch = new SearchBatch(positive.Value, 513);
                    var rightBatch = new SearchBatch(NeowPrefixHashCache.BlockSize - 257UL, 1027);
                    var pair = NeowFamilyGpuExecutor.ExecuteDensePair(on, leftBatch, right, rightBatch, CancellationToken.None);
                    Equal(pair.Left!, off.Execute(FamilyCandidateSet.Dense(leftBatch), CancellationToken.None, out _), "PairLeft");
                    Equal(pair.Right!, off.Execute(FamilyCandidateSet.Dense(rightBatch), CancellationToken.None, out _), "PairRight");
                    if (right.OptimizationRevisionFor(false) != on.OptimizationRevisionFor(false)) throw new InvalidDataException("ShionN.BorrowerIdentity");
                }
                if (numeric == NeowGpuNumericalVariant.NativeFloat64)
                {
                    using var buffer = new PrivateOrdinalBuffer(rd);
                    var batch = new SearchBatch(positive.Value, 513);
                    using (var privateOut = new NeowFamilyGpuExecutor(rd, plan, 73, privateOutput: buffer, optimizations: trials))
                    {
                        int count = privateOut.ExecutePrivateOutput(FamilyCandidateSet.Dense(batch), CancellationToken.None, out _);
                        Equal(buffer.ReadForVerification(batch, count), off.Execute(FamilyCandidateSet.Dense(batch), CancellationToken.None, out _), "PrivateOutput");
                    }
                    using var incoming = new PrivateOrdinalBuffer(rd);
                    uint[] ordinals = [512, 4, 3, 2, 1, 0];
                    FamilyGpuComputeUtility.Update(rd, incoming.Buffer, ordinals, "ShionN.PrivateCompact");
                    using var privateCompact = new NeowFamilyGpuExecutor(rd, plan, 74, privateInput: incoming, privateOutput: buffer, optimizations: trials);
                    int compactCount = privateCompact.ExecutePrivateStage(batch, ordinals.Length, CancellationToken.None, out _);
                    var compactExpected = FamilyCandidateSet.FromAbi1(batch, ordinals.Where(x => Cpu(batch.GlobalCandidate(x))).Order().Select(x => (ulong)x).ToArray());
                    Equal(buffer.ReadForVerification(batch, compactCount), compactExpected, "PrivateCompactCpu");
                }
                records.Add(new { mode, numeric = numeric.ToString(), applicable = true, denseRevision = on.OptimizationRevisionFor(false),
                    compactRevision = on.OptimizationRevisionFor(true), scanned, survivors, positive, dispatches,
                    privateChecked = numeric == NeowGpuNumericalVariant.NativeFloat64 });
                Console.WriteLine($"shionNeow=PASS;mode={mode};numeric={numeric};scanned={scanned};survivors={survivors};dense={on.OptimizationRevisionFor(false)};compact={on.OptimizationRevisionFor(true)}");
            }
        }
        File.WriteAllText(Path.Combine(output, "shion-neow-optimization-checks.json"), JsonSerializer.Serialize(new {
            passed = true, device = rd.GetDeviceName(), populationChecks, cpuChecks, records,
            timingIsPerformanceSample = false, attribution = "shion0528 resource.17 da82f7f9023c3d3598d50b8642e93f2801c37ccc"
        }, new JsonSerializerOptions { WriteIndented = true }));

        void VerifyCpu(NeowFamilyGpuExecutor off, NeowFamilyGpuExecutor on, FamilyCandidateSet input)
        {
            var actual = on.Execute(input, CancellationToken.None, out _);
            Equal(actual, off.Execute(input, CancellationToken.None, out _), "BoundedSameBody");
            var expected = FamilyCandidateSet.FromAbi1(input.Batch, input.EnumerateLogicalOrdinals()
                .Where(ordinal => Cpu(input.Batch.GlobalCandidate(ordinal))).ToArray());
            Equal(actual, expected, "BoundedCpuOracle"); cpuChecks += input.Count;
        }
    }

    private static void Equal(FamilyCandidateSet left, FamilyCandidateSet right, string scope)
    {
        if (!left.ExportAbi1().Span.SequenceEqual(right.ExportAbi1().Span)) throw new InvalidDataException("ShionN." + scope);
    }
    private static ulong ParseOrdinal(string seed)
    {
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        ulong ordinal = 0;
        foreach (char c in seed) ordinal = checked(ordinal * 34 + (uint)alphabet.IndexOf(c));
        return ordinal;
    }
}
