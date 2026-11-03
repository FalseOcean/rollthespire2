using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// Selected CPU resource/state owner. Domain preparation and frozen quotes live
// in Body; the original partitioned/packed/four-lane loops below remain local.
internal sealed class CpuFilterExecution : IFilterExecution
{
    internal const int Capacity = 8192;
    public FilterBodyDescription Body { get; }
    public bool ExecutionFaulted => _disabled;
    private readonly IRuntimeProfile _profile;
    private readonly Func<ulong, bool> _matches;
    private readonly NeowCpuFourWayPreGate? _fourWayPreGate;
    private readonly string _id;
    private readonly int _workers, _windowCapacity, _rootMode;
    private readonly bool _partitioned;
    private readonly Action? _disposeDiagnostics;
    private bool _disabled;
    private long _fourWayInputs, _fourWayRejected, _fourWayScalarInputs;
    internal long FourWayInputs => Interlocked.Read(ref _fourWayInputs);
    internal long FourWayRejected => Interlocked.Read(ref _fourWayRejected);
    internal long FourWayScalarInputs => Interlocked.Read(ref _fourWayScalarInputs);
    internal bool UsesFourWayPreGate => _fourWayPreGate is not null;
    internal double? CalibrationMeanInput { get; set; }
    internal CpuFilterExecution(FilterBodyDescription body, IRuntimeProfile profile, Func<ulong, bool> matches,
        bool numericalRoots, bool packedSeedRoots = false, NeowCpuFourWayPreGate? gate = null,Action? disposeDiagnostics=null)
    {
        Body = body; _profile = profile; _matches = matches; _id = body.FamilyId;
        _workers = body.CpuWorkers; _windowCapacity = body.WindowCapacity; _fourWayPreGate = gate;
        _disposeDiagnostics=disposeDiagnostics;
        if (_workers is < 1 or > 32 || _windowCapacity is < 1024 or > (1 << 20))
            throw new ArgumentOutOfRangeException(nameof(body));
        if (packedSeedRoots && !numericalRoots) throw new ArgumentException("Packed roots require numerical roots", nameof(packedSeedRoots));
        _partitioned = numericalRoots || _workers > 1; _rootMode = numericalRoots ? packedSeedRoots ? 3 : 2 : 0;
        if (_rootMode != 0 && _profile.ProfileId != RuntimeProfileId.Beta111)
            throw new InvalidOperationException("CpuByteRootsRequireBeta111");
    }
    internal static FamilyConditionPerformanceProjection Describe(string id, string revision,
        int workers, int capacity, bool numericalRoots, bool packedRoots, bool compact, NeowCpuFourWayPreGate? gate = null) =>
        new(id, revision + ".RootMode" + (numericalRoots ? packedRoots ? 3 : 2 : 0) + ".P" +
            (numericalRoots || workers > 1 ? workers : 0) + ".B" + capacity +
            (compact ? ".CompactAbi1" : ".Dense") + ".CanonicalAbi1Ready" + (gate is null ? "" : NeowCpuFourWayPreGate.Revision),
            "Cpu.NeutralCondition.20260905.v1", 1, "Neutral_NoAcceptedWithinPathCurve;device=CPU", false);
    public ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token) =>
        new(Execute(input, token, services));
    public ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        try { _disposeDiagnostics?.Invoke(); }
        catch(Exception error) { FamilyAuxiliaryEvidence.Report(error,nameof(CpuFilterExecution),"Dispose.Diagnostics",Body.InputIdentity); }
        return ValueTask.CompletedTask;
    }
    private FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, FilterRuntimeServices context)
    {
        token.ThrowIfCancellationRequested();
        if (_disabled) throw new InvalidOperationException("FamilyCpuInvocationFaulted:" + _id);
        if (input.Count == 0) return input;
        var watch = Stopwatch.StartNew();
        try
        {
            int chunks;
            FamilyCandidateSet result;
            if (_partitioned) result = ExecutePartitioned(input, token, _matches, out chunks, context);
            else
            {
                context.EnterCpu(token);
                try
                {
                    var survivors = new List<ulong>(Math.Min(input.Count, Capacity));
                    // Keep one observation window and original batch-local ordinals. Only
                    // the private CPU output staging is chunked; no partial result is handed
                    // to another Family or to Exact before this invocation completes.
                    var chunk = new ulong[Math.Min(input.Count, Capacity)];
                    int chunkSurvivors = 0, chunkInputs = 0; chunks = 0;
                    Span<byte> seedBytes = stackalloc byte[Beta110SeedCodec.SeedLength];
                    bool carry = _rootMode == 2 && input.IsDense;
                    if (carry) Beta110SeedCodec.WriteOrdinal(input.Batch.BatchBase, seedBytes);
                    foreach (ulong ordinal in input.EnumerateLogicalOrdinals())
                    {
                        token.ThrowIfCancellationRequested();
                        ulong global = input.Batch.GlobalCandidate(ordinal);
                        ulong root;
                        if (_rootMode == 0) root = _profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(_profile, global));
                        else
                        {
                            if (!carry) Beta110SeedCodec.WriteOrdinal(global, seedBytes);
                            root = XxHash64.Hash(seedBytes, 0);
                            if (carry && ordinal + 1 < (ulong)input.Count && !Beta110SeedCodec.Advance(seedBytes, 1))
                                throw new InvalidOperationException("CpuDenseSeedCarryOverflow");
                        }
                        bool passed;
                        try { passed = _matches(root); }
                        catch (Exception error) when (error is not OutOfMemoryException &&
                            !(error is OperationCanceledException && token.IsCancellationRequested))
                        { throw new FamilyCpuMatchException(_id, input.Batch, ordinal, global, root, error); }
                        if (passed) chunk[chunkSurvivors++] = ordinal;
                        if (++chunkInputs == Capacity)
                        {
                            for (int i = 0; i < chunkSurvivors; i++) survivors.Add(chunk[i]);
                            chunkInputs = chunkSurvivors = 0; chunks++;
                        }
                    }
                    if (chunkInputs > 0)
                    {
                        for (int i = 0; i < chunkSurvivors; i++) survivors.Add(chunk[i]);
                        chunks++;
                    }
                    token.ThrowIfCancellationRequested();
                    result = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors.ToArray());
                }
                finally { context.ExitCpu(); }
            }
            watch.Stop();
            context.Costs.ObserveCompleted(Body, input, result.Count, watch.Elapsed.TotalMilliseconds,
                passedCoverage: context.CoverageFor(Body), modeledInput: CalibrationMeanInput);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _disabled = true;
            context.Costs.Invalidate(Body);
            RuntimeLog.TryBackgroundWarning($"familyPhysicalFault=true;family={_id};reason={ex.GetType().Name}:{ex.Message};recovery=None");
            throw;
        }
    }
    private FamilyCandidateSet ExecutePartitioned(FamilyCandidateSet input, CancellationToken token,
        Func<ulong, bool> matches, out int parts, FilterRuntimeServices? context, int firstIndex = 0, int? inputLength = null)
    {
        // Preserve the measured CPU window inside a larger GPU root batch.
        // No intermediate CPU chunk is published to the next Family.
        if (inputLength is null && input.Count > _windowCapacity)
        {
            var combined = new List<ulong>(); parts = 0;
            for(int start=0;start<input.Count;start+=_windowCapacity)
            {
                var result = ExecutePartitioned(input,token,matches,out int chunkParts,context,start,Math.Min(_windowCapacity,input.Count-start));
                combined.AddRange(result.ExportAbi1().ToArray()); parts += chunkParts;
            }
            token.ThrowIfCancellationRequested();
            return FamilyCandidateSet.FromSortedAbi1(input.Batch,combined.ToArray());
        }
        int length = inputLength ?? input.Count;
        // Pure, batch-local CPU work. All partitions join before publishing ABI1;
        // the existing producer, Exact pipeline and safe cursor remain owners.
        int count = Math.Min(_workers, Math.Max(1, length / 1024));
        parts = count;
        var populations = new List<ulong>[count];
        void Partition(int part)
        {
            context?.EnterCpu(token);
            try
            {
                int first = firstIndex + (int)((long)part * length / count);
                int end = firstIndex + (int)((long)(part + 1) * length / count);
                var output = new List<ulong>(Math.Min(end - first, 256));
                ReadOnlySpan<ulong> ordinals = input.IsDense ? default : input.ExportAbi1().Span;
                // Only an explicitly bound owner uses packed scalar roots. Keep
                // the existing byte/profile loop below unchanged for all others.
                if (_rootMode == 3)
                {
                    bool packedCarry = input.IsDense;
                    CpuPackedSeed12 seed = packedCarry ? CpuPackedSeed12.FromOrdinal(input.Batch.GlobalCandidate((ulong)first)) : default;
                    int i = first;
                    if (packedCarry && _fourWayPreGate is { } preGate)
                    {
                        Span<ulong> roots = stackalloc ulong[4];
                        long vectorInputs = 0, rejected = 0;
                        for (; i + 3 < end; i += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            for (int lane = 0; lane < 4; lane++)
                            {
                                roots[lane] = seed.Hash();
                                if (i + lane + 1 < end && !seed.Advance())
                                    throw new InvalidOperationException("CpuDensePartitionCarryOverflow");
                            }
                            int passMask = preGate.PassMask(roots[0], roots[1], roots[2], roots[3]);
                            vectorInputs += 4;
                            for (int lane = 0; lane < 4; lane++)
                            {
                                token.ThrowIfCancellationRequested();
                                if ((passMask & (1 << lane)) == 0) { rejected++; continue; }
                                ulong ordinal = (ulong)(i + lane), global = input.Batch.GlobalCandidate(ordinal), root = roots[lane];
                                bool passed;
                                try { passed = matches(root); }
                                catch (Exception error) when (error is not OutOfMemoryException &&
                                    !(error is OperationCanceledException && token.IsCancellationRequested))
                                { throw new FamilyCpuMatchException(_id, input.Batch, ordinal, global, root, error); }
                                if (passed) output.Add(ordinal);
                            }
                        }
                        Interlocked.Add(ref _fourWayInputs, vectorInputs);
                        Interlocked.Add(ref _fourWayRejected, rejected);
                    }
                    if (_fourWayPreGate is not null) Interlocked.Add(ref _fourWayScalarInputs, end - i);
                    for (; i < end; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        ulong ordinal = input.IsDense ? (ulong)i : ordinals[i];
                        ulong global = input.Batch.GlobalCandidate(ordinal);
                        if (!packedCarry) seed = CpuPackedSeed12.FromOrdinal(global);
                        ulong root = seed.Hash();
                        if (packedCarry && i + 1 < end && !seed.Advance())
                            throw new InvalidOperationException("CpuDensePartitionCarryOverflow");
                        bool passed;
                        try { passed = matches(root); }
                        catch (Exception error) when (error is not OutOfMemoryException &&
                            !(error is OperationCanceledException && token.IsCancellationRequested))
                        { throw new FamilyCpuMatchException(_id, input.Batch, ordinal, global, root, error); }
                        if (passed) output.Add(ordinal);
                    }
                    populations[part] = output;
                    return;
                }
                Span<byte> bytes = stackalloc byte[Beta110SeedCodec.SeedLength];
                bool carry = _rootMode == 2 && input.IsDense;
                if (carry) Beta110SeedCodec.WriteOrdinal(input.Batch.GlobalCandidate((ulong)first), bytes);
                for (int i = first; i < end; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ulong ordinal = input.IsDense ? (ulong)i : ordinals[i];
                    ulong global = input.Batch.GlobalCandidate(ordinal);
                    ulong root;
                    if (_rootMode == 0) root = _profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(_profile, global));
                    else
                    {
                        if (!carry) Beta110SeedCodec.WriteOrdinal(global, bytes);
                        root = XxHash64.Hash(bytes, 0);
                        if (carry && i + 1 < end && !Beta110SeedCodec.Advance(bytes, 1))
                            throw new InvalidOperationException("CpuDensePartitionCarryOverflow");
                    }
                    bool passed;
                    try { passed = matches(root); }
                    catch (Exception error) when (error is not OutOfMemoryException &&
                        !(error is OperationCanceledException && token.IsCancellationRequested))
                    { throw new FamilyCpuMatchException(_id, input.Batch, ordinal, global, root, error); }
                    if (passed) output.Add(ordinal);
                }
                populations[part] = output;
            }
            finally { context?.ExitCpu(); }
        }
        if (count == 1) Partition(0);
        else Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = _workers, CancellationToken = token }, Partition);
        token.ThrowIfCancellationRequested();
        int total = 0;
        foreach (var population in populations) total = checked(total + population.Count);
        var survivors = new ulong[total];
        int offset = 0;
        foreach (var population in populations) { population.CopyTo(survivors, offset); offset += population.Count; }
        return FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors);
    }

}
