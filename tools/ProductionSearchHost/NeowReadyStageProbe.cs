using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Existing Y2 diagnostic entry only. Compare a known old T stage plus a new
// Dense N root at the physical boundary; neither numerical body is replaced.
// This is not a Session, E2E, first-result, calibration or pricing experiment.
internal static class NeowReadyStageProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const int WarmupCycles = 2, MeasuredCycles = 12;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private sealed record Boundary(double Milliseconds, int TCount, int NCount, int Submissions,
        int Dispatches, long HeaderBytes, double? UploadMs, double? RecordQueueSyncMs, double? CollectMs);
    private sealed record DeviceMemory(ulong Total, ulong Buffers, ulong Textures);

    private sealed class Bindings
    {
        internal readonly MethodInfo Prepare = typeof(NeowFamilyGpuExecutor).GetMethod("Prepare", Instance)!;
        internal readonly MethodInfo Record = typeof(NeowFamilyGpuExecutor).GetMethod("Record", Instance)!;
        internal readonly MethodInfo Queue = typeof(NeowFamilyGpuExecutor).GetMethod("QueueReadback", Instance)!;
        internal readonly MethodInfo Collect = typeof(NeowFamilyGpuExecutor).GetMethod("Collect", Instance)!;
        internal readonly FieldInfo NPending, NReadback, NQueueFailure;
        internal readonly Rid TBatch, THeader, TPipeline, TUniforms;
        internal readonly int TGroupRoots;
        internal readonly uint[] InitialTHeader;

        internal Bindings(TransformationAggregateGpuExecutor transformation, PrivateOrdinalBuffer oldInput, PrivateOrdinalBuffer tOutput)
        {
            Type prepared = typeof(NeowFamilyGpuExecutor).GetNestedType("Prepared", BindingFlags.NonPublic)!;
            NPending = prepared.GetField("Pending", Instance)!;
            NReadback = prepared.GetField("Readback", Instance)!;
            NQueueFailure = prepared.GetField("QueueFailure", Instance)!;
            Type t = typeof(TransformationAggregateGpuExecutor);
            Rid[] Rids(string name) => (Rid[])t.GetField(name, Instance)!.GetValue(transformation)!;
            TBatch = Rids("_batch")[0]; THeader = Rids("_header")[0];
            TUniforms = Rids("_uniforms")[0]; TPipeline = (Rid)t.GetField("_pipeline", Instance)!.GetValue(transformation)!;
            TGroupRoots = checked(64 * (int)t.GetField("_seedsPerInvocation", Instance)!.GetValue(transformation)!);
            InitialTHeader = (uint[])t.GetMethod("Header", Static)!.Invoke(null, null)!;
            if (!(bool)t.GetField("_privateInput", Instance)!.GetValue(transformation)! ||
                !(bool)t.GetField("_privateOutput", Instance)!.GetValue(transformation)! ||
                Rids("_input")[0] != oldInput.Buffer || Rids("_output")[0] != tOutput.Buffer || TGroupRoots <= 0)
                throw new InvalidOperationException("NReadyT.TPrivateBindingMismatch");
        }
    }

    internal static void Run(ExactSearchExecutionRequest request, string output)
    {
        Directory.CreateDirectory(output);
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true);
        var neow = families.OfType<NeowFamily>().SingleOrDefault(f => f.FamilyId == "N.Neow");
        var transformation = families.OfType<TransformationAggregateFamily>().SingleOrDefault();
        if (neow?.GpuPlan is not { NumericalVariant: NeowGpuNumericalVariant.NativeFloat64 } nPlan ||
            transformation?.GpuPlan is not { Supported: true, NumericalVariant: GpuNumericalVariant.NativeFloat64 } tPlan)
        {
            NotApplicable(output, "Requires actual ordinary Native N and supported Native T plans"); return;
        }
        var profile = RuntimeProfileRegistry.Select(request.Detection);
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(profile, request.CanonicalStartSeed, out ulong start, out _, out string issue))
            throw new InvalidDataException(issue);
        int roots = checked((int)Math.Min(NeowFamilyGpuExecutor.Capacity, request.ScanCount / 2));
        if (roots <= 0) { NotApplicable(output, "Requires two nonempty independent root batches"); return; }
        var oldBatch = new SearchBatch(start, roots);
        var newBatch = new SearchBatch(checked(start + (ulong)roots), roots);
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("NReadyT.NoGpu");
        var memoryAtDevice = Memory(rd);
        using var oldInput = new PrivateOrdinalBuffer(rd);
        using var producer = new NeowFamilyGpuExecutor(rd, nPlan, 90, privateOutput: oldInput);
        if (!producer.SupportsDensePair)
        {
            NotApplicable(output, "N ordinary Dense scope excludes private input, composites, joint transforms, BonesK and physical modes"); return;
        }
        // This real producer is invoked once, outside every measured boundary.
        // Its complete output stays resident and read-only throughout A and B.
        int oldCount = producer.ExecutePrivateOutput(FamilyCandidateSet.Dense(oldBatch), CancellationToken.None, out var producerMetrics);
        var oldPopulation = oldInput.ReadForVerification(oldBatch, oldCount);
        string oldHash = Digest(oldPopulation);
        if (oldCount <= 8 || oldCount > tPlan.Capacity)
        {
            NotApplicable(output, $"Requires old actual N count >8 and <= one T window; actual={oldCount};TCapacity={tPlan.Capacity}"); return;
        }
        using var tOutput = new PrivateOrdinalBuffer(rd);
        using var newNOutput = new PrivateOrdinalBuffer(rd);
        using var tGpu = new TransformationAggregateGpuExecutor(rd, tPlan, oldInput, tOutput);
        // The old producer never executes again. Its ordinary scratch can be
        // borrowed while keeping both root output buffers independent.
        using var newN = new NeowFamilyGpuExecutor(rd, nPlan, 91, privateOutput: newNOutput, sharedDenseScratch: producer);
        var bindings = new Bindings(tGpu, oldInput, tOutput);
        if (oldInput.Buffer == tOutput.Buffer || oldInput.Buffer == newNOutput.Buffer || tOutput.Buffer == newNOutput.Buffer)
            throw new InvalidOperationException("NReadyT.RootOutputAliasing");
        var memoryAfterSetup = Memory(rd);

        // Independent ordinary API execution establishes complete populations.
        // Its wall time is kept separate from warmup and measured ABBA samples.
        Boundary reference = Separate(tGpu, newN, oldBatch, oldCount, newBatch);
        var expectedT = tOutput.ReadForVerification(oldBatch, reference.TCount);
        var expectedN = newNOutput.ReadForVerification(newBatch, reference.NCount);
        string tHash = Digest(expectedT), nHash = Digest(expectedN);
        var rows = new List<object>();
        var measuredA = new List<double>(); var measuredB = new List<double>(); var pairedSavings = new List<double>();
        ulong observedMemoryMaximum = Math.Max(memoryAtDevice.Total, memoryAfterSetup.Total);
        string rawPath = Path.Combine(output, "neow-ready-stage-raw.jsonl");
        using (var raw = new StreamWriter(rawPath, append: false))
        {
            for (int cycle = -WarmupCycles; cycle < MeasuredCycles; cycle++)
            {
                double a = 0, b = 0;
                foreach (var (variant, position) in new[] { "A", "B", "B", "A" }.Select((name, index) => (name, index)))
                {
                    var before = Memory(rd);
                    Boundary result = variant == "A" ? Separate(tGpu, newN, oldBatch, oldCount, newBatch) :
                        Combined(rd, bindings, newN, oldBatch, oldCount, newBatch);
                    var after = Memory(rd);
                    observedMemoryMaximum = Math.Max(observedMemoryMaximum, Math.Max(before.Total, after.Total));
                    // Both full reads and complete ABI1 comparison are excluded
                    // from result.Milliseconds, identically for A and B.
                    var actualT = tOutput.ReadForVerification(oldBatch, result.TCount);
                    var actualN = newNOutput.ReadForVerification(newBatch, result.NCount);
                    Equal(actualT, expectedT, "TCompletePopulation"); Equal(actualN, expectedN, "NCompletePopulation");
                    var row = new { cycle, warmup = cycle < 0, position, variant, result.Milliseconds,
                        oldBatchBase = oldBatch.BatchBase, newBatchBase = newBatch.BatchBase, rootsPerBatch = roots,
                        tInputCount = oldCount, tOutputCount = result.TCount, nOutputCount = result.NCount,
                        tAbi1Sha256 = Digest(actualT), nAbi1Sha256 = Digest(actualN), result.Submissions, result.Dispatches,
                        result.HeaderBytes, result.UploadMs, result.RecordQueueSyncMs, result.CollectMs,
                        memoryBefore = before, memoryAfter = after, fullPopulationVerificationOutsideTiming = true };
                    rows.Add(row); raw.WriteLine(JsonSerializer.Serialize(row)); raw.Flush();
                    if (cycle >= 0)
                    {
                        if (variant == "A") { measuredA.Add(result.Milliseconds); a += result.Milliseconds; }
                        else { measuredB.Add(result.Milliseconds); b += result.Milliseconds; }
                    }
                }
                if (cycle >= 0) pairedSavings.Add((a - b) / 2);
            }
        }
        Equal(oldInput.ReadForVerification(oldBatch, oldCount), oldPopulation, "OldResidentInputRemainedReadonly");
        double medianA = Median(measuredA), medianB = Median(measuredB);
        var summary = new { applicable = true, passed = true, diagnosticOnly = true, kind = "N-readyT", device = rd.GetDeviceName(),
            request.SnapshotFingerprint, ownerThreadId = System.Environment.CurrentManagedThreadId,
            oldBatch, newBatch, oldNCount = oldCount, tCapacity = tPlan.Capacity, tOutputCount = reference.TCount,
            newNOutputCount = reference.NCount, oldInputSha256 = oldHash, tAbi1Sha256 = tHash, nAbi1Sha256 = nHash,
            oldResidentInputRemainedReadonly = true, nStagedDense = nPlan.UsesStagedDense, tSeedsPerInvocation = bindings.TGroupRoots / 64,
            producerMetrics, referenceOutsideSamples = reference, warmupCycles = WarmupCycles, measuredCycles = MeasuredCycles,
            pattern = "ABBA", samplesPerVariant = measuredA.Count, medianASeparateMs = medianA, medianBCombinedMs = medianB,
            medianFixedBoundarySavingsMs = medianA - medianB, medianPairedCycleSavingsMs = Median(pairedSavings),
            observedMaximumPairedCycleSavingsMs = pairedSavings.Max(), memoryAtDevice, memoryAfterSetup,
            observedMaximumDeviceReportedTotalBytes = observedMemoryMaximum, rawPath, rows,
            reflection = "B includes cached MethodInfo.Invoke calls for actual N Prepare/Record/Queue/Collect; A uses ordinary executor APIs",
            stageTiming = "Upload/record-queue-sync/collect subphases are B-only; both variants include their complete physical boundary in Milliseconds",
            scope = "One ready old T window and one independent new Dense N sequence; same owner and device; complete resident outputs verified outside timers",
            exclusions = "No numerical-body replacement, RunState mutation, Session/E2E/first-result claim, calibration/price update or production enablement" };
        File.WriteAllText(Path.Combine(output, "neow-ready-stage-probe.json"), JsonSerializer.Serialize(summary, Json));
        Console.WriteLine($"nReadyStageProbe=PASS;oldN={oldCount};T={reference.TCount};newN={reference.NCount};samples={rows.Count};medianSeparateMs={medianA};medianCombinedMs={medianB};scope=PhysicalBoundaryOnly");
    }

    private static Boundary Separate(TransformationAggregateGpuExecutor transformation, NeowFamilyGpuExecutor neow,
        SearchBatch oldBatch, int oldCount, SearchBatch newBatch)
    {
        var timer = Stopwatch.StartNew();
        int tCount = transformation.ExecutePrivateStage(oldBatch, oldCount, CancellationToken.None, out var t);
        int nCount = neow.ExecutePrivateOutput(FamilyCandidateSet.Dense(newBatch), CancellationToken.None, out var n);
        double elapsed = timer.Elapsed.TotalMilliseconds;
        if (t.SubmissionGroups != 1 || t.Dispatches != 1) throw new InvalidDataException("NReadyT.ExpectedExactlyOneReadyTWindow");
        return new(elapsed, tCount, nCount, t.SubmissionGroups + 1, t.Dispatches + n.Dispatches,
            t.ReadbackBytes + n.ReadbackBytes, null, null, null);
    }

    private static Boundary Combined(RenderingDevice rd, Bindings bindings, NeowFamilyGpuExecutor neow,
        SearchBatch oldBatch, int oldCount, SearchBatch newBatch)
    {
        var timer = Stopwatch.StartNew();
        object prepared = Invoke(bindings.Prepare, neow,
            [FamilyCandidateSet.Dense(newBatch), newBatch.BatchCandidateCount, CancellationToken.None])!;
        FamilyGpuComputeUtility.Update(rd, bindings.TBatch,
            [(uint)oldBatch.BatchBase, (uint)(oldBatch.BatchBase >> 32), (uint)oldBatch.BatchCandidateCount,
                (uint)oldCount, 1, PrivateOrdinalBuffer.Capacity, 0, 0], "NReadyT.ActualTBatch");
        FamilyGpuComputeUtility.Update(rd, bindings.THeader, bindings.InitialTHeader, "NReadyT.ActualTHeader");
        double uploadMs = timer.Elapsed.TotalMilliseconds;
        Exception? failure = null;
        long list = rd.ComputeListBegin();
        try
        {
            rd.ComputeListBindComputePipeline(list, bindings.TPipeline);
            rd.ComputeListBindUniformSet(list, bindings.TUniforms, 0);
            rd.ComputeListDispatch(list, checked((uint)((oldCount + bindings.TGroupRoots - 1) / bindings.TGroupRoots)), 1, 1);
            rd.ComputeListAddBarrier(list);
            Invoke(bindings.Record, neow, [list, prepared]);
        }
        catch (Exception exception) { failure = exception; }
        try { rd.ComputeListEnd(); }
        catch (Exception exception) { failure = Combine(failure, exception); }
        FamilyGpuReadback.Pending? tPending = null;
        if (failure is null)
        {
            try
            {
                tPending = FamilyGpuReadback.Queue(rd, bindings.THeader, 0, 32, $"NReadyT.OldT:batch={oldBatch.BatchBase}");
                Invoke(bindings.Queue, neow, [prepared]);
            }
            catch (Exception exception) { failure = exception; }
        }
        // Recording/queue failures never bypass the one submit/drain. No token
        // or reflection error is allowed to leave a partially recorded list.
        try { FamilyGpuComputeUtility.SubmitAndSync(rd, "NReadyT.DiagnosticOnly"); }
        catch (Exception exception) { failure = Combine(failure, exception); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        double syncedMs = timer.Elapsed.TotalMilliseconds;
        byte[] tHeader = tPending!.GetData();
        if (bindings.NQueueFailure.GetValue(prepared) is Exception queueFailure) ExceptionDispatchInfo.Capture(queueFailure).Throw();
        var nPending = (FamilyGpuReadback.Pending)bindings.NPending.GetValue(prepared)!;
        byte[] nHeader = nPending.GetData();
        bindings.NReadback.SetValue(prepared, nHeader);
        ReadOnlySpan<uint> header = FamilyGpuComputeUtility.UInt32Span(tHeader);
        if (header.Length != 8 || header[0] != 0x5452414Eu || header[1] != 1 || header[5] != 0x5452414Eu ||
            header[3] != 0 || header[4] != oldCount || header[2] > oldCount)
            throw new InvalidDataException($"NReadyT.THeaderFault:batch={oldBatch.BatchBase};header={string.Join(',', header.ToArray())}");
        object?[] collectArgs = [prepared, 0d, CancellationToken.None, null];
        object? publicOutput = Invoke(bindings.Collect, neow, collectArgs);
        var nMetrics = (NeowFamilyGpuMetrics)collectArgs[^1]!;
        if (publicOutput is not null) throw new InvalidDataException("NReadyT.PrivateNReturnedPublicPayload");
        double elapsed = timer.Elapsed.TotalMilliseconds;
        GC.KeepAlive(prepared); GC.KeepAlive(tPending);
        return new(elapsed, checked((int)header[2]), nMetrics.Survivors, 1, 1 + nMetrics.Dispatches,
            tHeader.Length + nHeader.Length, uploadMs, syncedMs - uploadMs, elapsed - syncedMs);
    }

    private static object? Invoke(MethodInfo method, object target, object?[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }
    private static Exception Combine(Exception? first, Exception second) => first is null ? second : new AggregateException(first, second);
    private static DeviceMemory Memory(RenderingDevice rd) => new(
        rd.GetMemoryUsage(RenderingDevice.MemoryType.Total), rd.GetMemoryUsage(RenderingDevice.MemoryType.Buffers), rd.GetMemoryUsage(RenderingDevice.MemoryType.Textures));
    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.Order().ToArray(); int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }
    private static string Digest(FamilyCandidateSet set) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(set.ExportAbi1().Span)));
    private static void Equal(FamilyCandidateSet actual, FamilyCandidateSet expected, string scope)
    {
        if (actual.Batch != expected.Batch || !actual.EnumerateLogicalOrdinals().SequenceEqual(expected.EnumerateLogicalOrdinals()))
            throw new InvalidDataException("NReadyT." + scope);
    }
    private static void NotApplicable(string output, string reason)
    {
        File.WriteAllText(Path.Combine(output, "neow-ready-stage-probe.json"), JsonSerializer.Serialize(
            new { applicable = false, passed = false, kind = "N-readyT", disposition = "NotApplicable", reason, diagnosticOnly = true }, Json));
        Console.WriteLine("nReadyStageProbe=NOT_APPLICABLE;reason=" + reason);
    }
}
