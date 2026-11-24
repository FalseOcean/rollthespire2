using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Godot;
namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct EventResultFamilyGpuMetrics(int Input, int Output, int Dispatches, long ReadbackBytes,
    double ReadbackMs, double SortMs, double CanonicalMs, double DispatchSyncMs = 0)
{
    internal int SubmissionGroups { get; init; }
    internal int HeaderReadCalls { get; init; }
}
internal sealed class EventResultFamilyGpuExecutor : IDisposable
{
    internal const string FinalPrivateHeaderRevision = ".PrivateFinalHeaderReadback.20261026.v1";
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = [];
    // Two windows amortize submission while bounding output storage and cancellation latency.
    private const int GroupingK = 2;
    private readonly Rid[] _batch = new Rid[GroupingK], _input = new Rid[GroupingK],
        _header = new Rid[GroupingK], _output = new Rid[GroupingK], _uniforms = new Rid[GroupingK];
    private readonly Rid _pipeline, _headerBank;
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private bool _disposed;
    private readonly bool _privateInput, _privateOutput;
    private readonly bool _tracePrivate = EwPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    internal int Capacity { get; }
    internal string Device { get; }
    internal double SetupMs { get; }
    internal long WorkspaceBytes { get; }
    internal EventResultFamilyGpuExecutor(RenderingDevice rd, EventResultFamilyPlan plan,
        PrivateOrdinalBuffer? privateInput = null, PrivateOrdinalBuffer? privateOutput = null)
    {
        if (plan.NumericalVariant != GpuNumericalVariant.NativeFloat64 && (privateInput is not null || privateOutput is not null))
            throw new InvalidOperationException("E.IntegerRne.PrivateTransportNotValidated");
        _privateInput = privateInput is not null; _privateOutput = privateOutput is not null;
        if (_privateInput && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("E.PrivateBuffersAlias");
        _rd = rd; Capacity = plan.Capacity; var watch = Stopwatch.StartNew();
        Rid Add(Rid r) { _owned.Add(r); return r; }
        Rid Data(uint[] data) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(data)));
        try
        {
            var buffers = new Rid[6];
            for (int i = 0; i < plan.Buffers.Length; i++) buffers[i + 1] = Data(plan.Buffers[i]);

            string source = plan.ShaderSource();
            if (_privateInput)
            {
                const string address = "input_ids.v[i]";
                if (source.IndexOf(address, StringComparison.Ordinal) < 0 ||
                    source.IndexOf(address, StringComparison.Ordinal) != source.LastIndexOf(address, StringComparison.Ordinal))
                    throw new InvalidOperationException("E.PrivateInputShaderSeam");
                source = source.Replace(address, "input_ids.v[batch.v[6]+i]", StringComparison.Ordinal);
            }
            Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "EventResultFamily"));
            _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "E.EventResult"));
            if (!rd.ComputePipelineIsValid(_pipeline)) throw new InvalidOperationException("E.EventResult.PipelineInvalid");
            for (int slot = 0; slot < GroupingK; slot++)
            {
                buffers[0] = _batch[slot] = Data(new uint[8]);
                buffers[3] = _input[slot] = privateInput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4));
                buffers[4] = _header[slot] = _privateOutput && slot > 0 ? _header[0] : Data(Header());
                buffers[5] = _output[slot] = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4));
                _uniforms[slot] = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers, "E.EventResult"));
            }
            _headerBank = _privateOutput ? default : Data(new uint[GroupingK * 8]);
            WorkspaceBytes = GroupingK * 32L + (_privateOutput ? 32L : GroupingK * 64L) +
                (_privateInput ? 0 : GroupingK * Capacity * 4L) + (_privateOutput ? 0 : GroupingK * Capacity * 4L) + plan.Buffers.Sum(b => Math.Max(4, b.Length * 4L));
            Device = rd.GetDeviceName(); SetupMs = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), "E.EventResult");
            throw;
        }
    }
    private static uint[] Header() => [0x45524652u, 1, 0, 0, 0, 0x45524652u, 0, 0];
    private void Check() { if (_owner != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("E.EventResult.OwnerMismatch"); ObjectDisposedException.ThrowIf(_disposed, this); }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out EventResultFamilyGpuMetrics metrics)
    {
        if (_privateInput || _privateOutput) throw new InvalidOperationException("E.PrivateEntryRequired");
        return ExecuteCore(input, input.Count, token, out metrics)!;
    }
    internal FamilyCandidateSet ExecutePrivate(SearchBatch batch, int count, CancellationToken token, out EventResultFamilyGpuMetrics metrics)
    {
        if (!_privateInput || _privateOutput || count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity)
            throw new InvalidDataException("E.PrivateTerminalBounds");
        return ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics)!;
    }
    internal int ExecutePrivateStage(SearchBatch batch, int count, CancellationToken token, out EventResultFamilyGpuMetrics metrics)
    {
        if (!_privateOutput || (!_privateInput && count != batch.BatchCandidateCount) || count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity)
            throw new InvalidDataException("E.PrivateStageBounds");
        ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics);
        return metrics.Output;
    }
    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input, int inputCount, CancellationToken token, out EventResultFamilyGpuMetrics metrics)
    {
        Check(); token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew(); var survivors = new List<ulong>();
        int dispatches = 0, groups = 0, headerCalls = 0; long bytes = 0;
        double read = 0, submitSync = 0, widen = 0;
        int privateCount = 0;
        bool resident = _privateOutput;
        if (resident) FamilyGpuComputeUtility.Update(_rd, _header[0], Header(), "E.EventResult.PrivateHeader");
        for (int offset = 0; offset < inputCount;)
        {
            token.ThrowIfCancellationRequested();
            int slots = Math.Min(GroupingK, (inputCount - offset - 1) / Capacity + 1);
            int processed = offset + Math.Min(inputCount - offset, slots * Capacity);
            for (int slot = 0; slot < slots; slot++)
            {
                token.ThrowIfCancellationRequested();
                int windowOffset = offset + slot * Capacity, count = Math.Min(Capacity, inputCount - windowOffset);
                FamilyGpuComputeUtility.Update(_rd, _batch[slot], [(uint)input.Batch.BatchBase, (uint)(input.Batch.BatchBase >> 32),
                    (uint)input.Batch.BatchCandidateCount, (uint)count, input.IsDense && !_privateInput ? 0u : 1u,
                    (uint)(resident ? PrivateOrdinalBuffer.Capacity : Capacity), (uint)windowOffset, 0], "E.EventResult.Batch");
                if (!resident) FamilyGpuComputeUtility.Update(_rd, _header[slot], Header(), "E.EventResult.Header");
                if (!input.IsDense && !_privateInput)
                    FamilyGpuComputeUtility.UpdateBytes(_rd, _input[slot], FamilyGpuComputeUtility.PackAbi1UInt32(input, windowOffset, count), "E.EventResult.Input");
            }
            token.ThrowIfCancellationRequested();
            double dispatchStart = watch.Elapsed.TotalMilliseconds;
            // Once Begin returns, always attempt End and one bounded drain.
            Exception? recordingFailure = null;
            long list = _rd.ComputeListBegin();
            try
            {
                _rd.ComputeListBindComputePipeline(list, _pipeline);
                for (int slot = 0; slot < slots; slot++)
                {
                    // Keep cumulative private header writes ordered. Once recording
                    // starts, drain the group before observing cancellation.
                    if (resident && slot > 0) _rd.ComputeListAddBarrier(list);
                    _rd.ComputeListBindUniformSet(list, _uniforms[slot], 0);
                    int count = Math.Min(Capacity, inputCount - offset - slot * Capacity);
                    _rd.ComputeListDispatch(list, (uint)((count + 63) / 64), 1, 1);
                    dispatches++;
                }
            }
            catch (Exception failure) { recordingFailure = failure; }
            try { _rd.ComputeListEnd(); }
            catch (Exception failure)
            {
                recordingFailure = recordingFailure is null ? failure : new AggregateException(recordingFailure, failure);
            }
            try
            {
                if (recordingFailure is null && !resident && slots > 1)
                    for (int slot = 0; slot < slots; slot++)
                    {
                        Error error = _rd.BufferCopy(_header[slot], _headerBank, 0, (uint)(slot * 32), 32);
                        if (error != Error.Ok)
                            throw new InvalidOperationException($"E.EventResult.HeaderBankCopyFailure:batch={input.Batch.BatchBase};offset={offset + slot * Capacity};error={error}");
                    }
            }
            catch (Exception failure) { recordingFailure = failure; }
            // Report record/end/copy failures after draining the group. Pending
            // likewise retains scheduling failures until after Sync.
            var pending = recordingFailure is null && (!resident || processed == inputCount)
                ? FamilyGpuReadback.Queue(_rd, resident || slots == 1 ? _header[0] : _headerBank, 0, (uint)(resident ? 32 : slots * 32), "E.EventResult.Header") : null;
            try { FamilyGpuComputeUtility.SubmitAndSync(_rd, "E.EventResult"); }
            catch (Exception syncFailure) when (recordingFailure is not null)
            {
                throw new AggregateException("E.EventResult.RecordAndDrain", recordingFailure, syncFailure);
            }
            groups++;
            submitSync += watch.Elapsed.TotalMilliseconds - dispatchStart;
            if (recordingFailure is not null) ExceptionDispatchInfo.Capture(recordingFailure).Throw();
            byte[]? headerData = null;
            if (pending is not null)
            {
                double collectStart = watch.Elapsed.TotalMilliseconds;
                headerData = pending.GetData();
                read += watch.Elapsed.TotalMilliseconds - collectStart;
            }
            token.ThrowIfCancellationRequested();
            // Cumulative private counters have no CPU consumer until the entire
            // family finishes. Preserve every bounded submit/sync boundary.
            if (resident && processed < inputCount) { offset = processed; continue; }
            double start = watch.Elapsed.TotalMilliseconds;
            var headers = FamilyGpuComputeUtility.UInt32Span(headerData!);
            if (headers.Length != (resident ? 8 : slots * 8)) throw new InvalidDataException("E.EventResult.HeaderBankLength");
            for (int slot = 0; slot < (resident ? 1 : slots); slot++)
            {
                int h = slot * 8, expected = resident ? inputCount : Math.Min(Capacity, inputCount - offset - slot * Capacity);
                if (headers[h] != 0x45524652u || headers[h + 1] != 1 || headers[h + 5] != 0x45524652u ||
                    headers[h + 3] != 0 || headers[h + 4] != expected || headers[h + 2] > expected)
                    throw new InvalidDataException($"E.EventResult.HeaderFault:batch={input.Batch.BatchBase};offset={offset + slot * Capacity};header={string.Join(',', headers.ToArray())}");
            }
            headerCalls++; bytes += resident ? 32 : slots * 32L;
            read += watch.Elapsed.TotalMilliseconds - start;
            if (resident) { privateCount = checked((int)headers[2]); offset = processed; continue; }
            for (int slot = 0; slot < slots; slot++)
            {
                token.ThrowIfCancellationRequested();
                uint n = headers[slot * 8 + 2];
                if (n == 0) continue;
                start = watch.Elapsed.TotalMilliseconds;
                var output = FamilyGpuComputeUtility.UInt32Span(_rd.BufferGetData(_output[slot], 0, n * 4));
                if (output.Length != n) throw new InvalidDataException("E.EventResult.PayloadMismatch");
                bytes += n * 4L; read += watch.Elapsed.TotalMilliseconds - start;
                double widenStart = watch.Elapsed.TotalMilliseconds;
                foreach (uint ordinal in output) survivors.Add(ordinal);
                widen += watch.Elapsed.TotalMilliseconds - widenStart;
            }
            offset = processed;
        }
        if (resident)
        {
            token.ThrowIfCancellationRequested();
            metrics = new(inputCount, privateCount, dispatches, bytes, read, 0, watch.Elapsed.TotalMilliseconds, submitSync)
                { SubmissionGroups = groups, HeaderReadCalls = headerCalls };
            return null;
        }
        token.ThrowIfCancellationRequested(); double sortStart = watch.Elapsed.TotalMilliseconds;
        survivors.Sort(); double sort = watch.Elapsed.TotalMilliseconds - sortStart;
        double validationStart = watch.Elapsed.TotalMilliseconds;
        var result = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors.ToArray());
        if (!input.IsDense)
        {
            var source = input.ExportAbi1().Span; int i = 0;
            foreach (ulong ordinal in survivors)
            {
                while (i < source.Length && source[i] < ordinal) i++;
                if (i == source.Length || source[i] != ordinal) throw new InvalidDataException("E.EventResult.OutputNotInputSubset");
            }
        }
        token.ThrowIfCancellationRequested();
        double validation = watch.Elapsed.TotalMilliseconds - validationStart;
        metrics = new(inputCount, result.Count, dispatches, bytes, read, sort, watch.Elapsed.TotalMilliseconds, submitSync)
            { SubmissionGroups = groups, HeaderReadCalls = headerCalls };
        if (_tracePrivate)
            RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundDetail($"ewStageTiming=true;stage=E;input={input.Count};output={result.Count};dispatches={dispatches};groups={groups};headerReadCalls={headerCalls};dispatchSyncMs={submitSync};readbackBytes={bytes};readbackMs={read};widenMs={widen};sortMs={sort};validationMs={validation};prepareOtherMs={metrics.CanonicalMs-submitSync-read-widen-sort-validation};canonicalMs={metrics.CanonicalMs}");
        return result;
    }
    public void Dispose() { if (_disposed) return; Check(); _disposed = true; FamilyGpuComputeUtility.FreeAll(_rd, _owned); }
}
