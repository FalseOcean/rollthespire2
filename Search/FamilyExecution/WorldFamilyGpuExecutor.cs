using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct WorldFamilyGpuMetrics(int Input, int Output, int Dispatches,
    int SubmissionGroups, int HeaderReadCalls, long HeaderReadBytes, int PayloadReadCalls, long PayloadReadBytes,
    double DispatchSyncMs, double HeaderReadbackMs, double CanonicalMs);
// Bounded windows within one SearchBatch. No numerical work or public state is changed by grouping.
internal sealed class WorldFamilyGpuExecutor : IDisposable
{
    internal int Capacity { get; }
    internal GpuNumericalVariant NumericalVariant { get; }
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = new();
    private readonly Rid[] _batch, _header, _input, _output, _uniforms;
    private readonly byte[][] _batchBytes;
    private readonly byte[] _headerResetBytes = FamilyGpuComputeUtility.ToBytes(Header());
    private readonly Rid _pipeline, _headerBank;
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private bool _disposed;
    private readonly bool _tracePrivate = EwPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    private readonly bool _privateInput;
    private readonly PrivateOrdinalBuffer? _privateOutput;
    internal int GroupingK { get; }
    internal string Device { get; }
    internal double SetupMs { get; }
    private readonly string _familyId, _scope;

    internal WorldFamilyGpuExecutor(RenderingDevice rd, WorldFamilyGpuPlan plan, Rid? privateInput = null, PrivateOrdinalBuffer? privateOutput = null)
        : this(rd, plan.Buffers, plan.Capacity, plan.GroupingK, plan.ScratchWords, plan.NumericalVariant,
            plan.ShaderSource(), "W.World", privateInput, privateOutput) { }

    // V uses the same proven ordinal ports and drain/header lifecycle, with its
    // own immutable short numerical program. This does not compose W work.
    internal WorldFamilyGpuExecutor(RenderingDevice rd, VariantOpeningGpuPlan plan, Rid? privateInput = null, PrivateOrdinalBuffer? privateOutput = null)
        : this(rd, plan.Buffers, plan.Capacity, plan.GroupingK, plan.ScratchWords, plan.NumericalVariant,
            plan.ShaderSource(), "V.VariantOpening", privateInput, privateOutput) { }

    private WorldFamilyGpuExecutor(RenderingDevice rd, uint[][] packed, int capacity, int groupingK, int scratchWords,
        GpuNumericalVariant numericalVariant, string source, string familyId, Rid? privateInput, PrivateOrdinalBuffer? privateOutput)
    {
        _rd = rd; Capacity = capacity; GroupingK = groupingK; NumericalVariant = numericalVariant; _familyId = familyId; _scope = familyId[..1];
        _privateInput = privateInput.HasValue; _privateOutput = privateOutput;
        if (privateInput.HasValue && privateOutput is not null && privateInput.Value == privateOutput.Buffer)
            throw new ArgumentException(_scope + ".PrivateBuffersAlias");
        var watch = Stopwatch.StartNew();
        Rid Add(Rid rid) { _owned.Add(rid); return rid; }
        Rid Buffer(uint[] values) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(values)));
        _batch = new Rid[GroupingK]; _header = new Rid[GroupingK]; _input = new Rid[GroupingK];
        _batchBytes = new byte[GroupingK][];
        _output = new Rid[GroupingK]; _uniforms = new Rid[GroupingK];
        try
        {
            Rid[] buffers = new Rid[packed.Length];
            for (int i = 0; i < packed.Length; i++)
                if (i is not (0 or 12 or 16 or 17 or 18)) buffers[i] = Buffer(packed[i]);
            buffers[12] = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, checked(Capacity * scratchWords * 4)));
            if (_privateInput)
            {
                if (!source.Contains("input_ids.v[i]", StringComparison.Ordinal)) throw new InvalidOperationException(_scope + ".PrivateInputSeamChanged");
                source = source.Replace("input_ids.v[i]", "input_ids.v[batch.v[6]+i]", StringComparison.Ordinal);
            }
            if (privateOutput is not null)
            {
                // batch[5] also locates the event scratch after encounter scratch.
                // Keep that window geometry; only append capacity uses reserved batch[7].
                const string bound = "if(p>=batch.v[5])";
                if (!source.Contains(bound, StringComparison.Ordinal)) throw new InvalidOperationException(_scope + ".PrivateOutputSeamChanged");
                source = source.Replace(bound, "if(p>=batch.v[7])", StringComparison.Ordinal);
            }
            Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, _familyId));
            _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, _familyId));
            if (!rd.ComputePipelineIsValid(_pipeline)) throw new InvalidOperationException(_scope + ".GpuPipelineInvalid");
            for (int slot = 0; slot < GroupingK; slot++)
            {
                _batchBytes[slot] = new byte[8 * sizeof(uint)];
                buffers[0] = _batch[slot] = Buffer(new uint[8]);
                buffers[16] = _header[slot] = privateOutput is not null && slot > 0 ? _header[0] : Buffer(Header());
                buffers[18] = _input[slot] = privateInput ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4));
                buffers[17] = _output[slot] = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4));
                _uniforms[slot] = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers, _familyId));
            }
            // K1 queues its own header directly and has no copy/bank.
            _headerBank = GroupingK == 1 || privateOutput is not null ? default : Buffer(new uint[GroupingK * 8]);
            Device = rd.GetDeviceName(); SetupMs = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), _familyId);
            throw;
        }
    }

    private static uint[] Header() => [0x57464d52, 1, 0, 0, 0, 0x57463031, 0, 0];
    private void AssertOwner()
    {
        if (_owner != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException(_scope + ".GpuOwnerMismatch");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out WorldFamilyGpuMetrics metrics)
        {
        if (_privateInput || _privateOutput is not null) throw new InvalidOperationException(_scope + ".PrivateEntryRequired");
        return ExecuteCore(input, token, out metrics, input.Count)!;
    }

    // Only the bounded EW owner can supply this count/buffer; no public ABI state.
    internal FamilyCandidateSet ExecutePrivate(SearchBatch batch, int count, CancellationToken token, out WorldFamilyGpuMetrics metrics)
    {
        if (!_privateInput || _privateOutput is not null || count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity) throw new InvalidDataException(_scope + ".PrivateCountInvalid");
        return ExecuteCore(FamilyCandidateSet.Dense(batch), token, out metrics, count)!;
    }

    internal int ExecutePrivateStage(SearchBatch batch, int count, CancellationToken token, out WorldFamilyGpuMetrics metrics)
    {
        if (_privateOutput is null || (!_privateInput && count != batch.BatchCandidateCount)) throw new InvalidOperationException(_scope + ".PrivateStagePortsRequired");
        _privateOutput.CheckPopulation(batch, count);
        ExecuteCore(FamilyCandidateSet.Dense(batch), token, out metrics, count);
        return metrics.Output;
    }

    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input, CancellationToken token, out WorldFamilyGpuMetrics metrics, int inputCount)
    {
        AssertOwner(); token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew(); var survivors = new List<ulong>();
        double dispatchSync = 0, headerRead = 0;
        double payloadRead = 0;
        int dispatches = 0, groups = 0, headerCalls = 0, payloadCalls = 0;
        long headerBytes = 0, payloadBytes = 0;
        int privateCount = 0;
        bool resident = _privateOutput is not null;
        if (resident) FamilyGpuComputeUtility.UpdateBytes(_rd, _header[0], _headerResetBytes, _scope + ".PrivateHeader");
        for (int offset = 0; offset < inputCount;)
        {
            token.ThrowIfCancellationRequested();
            int slots = Math.Min(GroupingK, (inputCount - offset - 1) / Capacity + 1);
            double start;
            for (int slot = 0; slot < slots; slot++)
            {
                token.ThrowIfCancellationRequested();
                int windowOffset = offset + slot * Capacity, count = Math.Min(Capacity, inputCount - windowOffset);
                Span<uint> batch = FamilyGpuComputeUtility.UInt32Span(_batchBytes[slot]);
                batch[0] = (uint)input.Batch.BatchBase; batch[1] = (uint)(input.Batch.BatchBase >> 32);
                batch[2] = (uint)input.Batch.BatchCandidateCount; batch[3] = (uint)count;
                batch[4] = input.IsDense && !_privateInput ? 0u : 1u; batch[5] = (uint)Capacity;
                batch[6] = (uint)windowOffset; batch[7] = resident ? (uint)PrivateOrdinalBuffer.Capacity : 0u;
                FamilyGpuComputeUtility.UpdateBytes(_rd, _batch[slot], _batchBytes[slot], _scope + ".Batch");
                if (!resident) FamilyGpuComputeUtility.UpdateBytes(_rd, _header[slot], _headerResetBytes, _scope + ".Header");
                if (!input.IsDense && !_privateInput)
                    FamilyGpuComputeUtility.UpdateBytes(_rd, _input[slot],
                        FamilyGpuComputeUtility.PackAbi1UInt32(input, windowOffset, count), _scope + ".CompactInput");
            }
            token.ThrowIfCancellationRequested();
            int processed = offset + Math.Min(inputCount - offset, slots * Capacity);
            start = watch.Elapsed.TotalMilliseconds;
            Exception? recordingFailure = null;
            long list = _rd.ComputeListBegin();
            try
            {
                _rd.ComputeListBindComputePipeline(list, _pipeline);
                for (int slot = 0; slot < slots; slot++)
                {
                    // Once recording starts, finish/submit/sync even if cancellation arrives.
                    // This prevents leaving an active list or queued work using disposed/reused buffers.
                    if (slot > 0) _rd.ComputeListAddBarrier(list);
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
            if (recordingFailure is null && GroupingK > 1 && !resident)
            {
                try
                {
                    // BufferCopy is outside the active compute list. RD tracks compute-write -> transfer-read.
                    for (int slot = 0; slot < slots; slot++)
                    {
                        Error error = _rd.BufferCopy(_header[slot], _headerBank, 0, (uint)(slot * 32), 32);
                        if (error != Error.Ok)
                            throw new InvalidOperationException($"{_scope}.HeaderBankCopyFailure:batch={input.Batch.BatchBase};offset={offset + slot * Capacity};error={error}");
                    }
                }
                catch (Exception failure) { recordingFailure = failure; }
            }
            if (recordingFailure is not null)
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(recordingFailure,
                    () => FamilyGpuComputeUtility.SubmitAndSync(_rd, _familyId), _scope + ".RecordCopyAndDrain");
                ExceptionDispatchInfo.Capture(recordingFailure).Throw();
            }
            // Private output appends to one cumulative header. The shader's
            // sticky fault bits and processed count cover every prior group;
            // only the completed population boundary needs CPU observation.
            FamilyGpuReadback.Pending? pendingHeader = !resident || processed == inputCount
                ? FamilyGpuReadback.Queue(_rd, resident || GroupingK == 1 ? _header[0] : _headerBank, 0,
                    checked((uint)(resident ? 32 : slots * 32)),
                    $"{_scope}.Header:batch={input.Batch.BatchBase};offset={offset}")
                : null;
            FamilyGpuComputeUtility.SubmitAndSync(_rd, _familyId); dispatchSync += watch.Elapsed.TotalMilliseconds - start; groups++;
            start = watch.Elapsed.TotalMilliseconds;
            byte[]? headerData = pendingHeader?.GetData();
            token.ThrowIfCancellationRequested();
            if (resident)
            {
                if (processed < inputCount) { offset = processed; continue; }
                ReadOnlySpan<uint> h = FamilyGpuComputeUtility.UInt32Span(headerData!);
                if (h.Length != 8 || h[0] != 0x57464d52 || h[1] != 1 || h[5] != 0x57463031 ||
                    h[3] != 0 || h[4] != inputCount || h[2] > inputCount)
                    throw new InvalidDataException(_scope + ".PrivateHeaderFault:" + string.Join(',', h.ToArray()));
                privateCount = checked((int)h[2]);
                headerCalls++; headerBytes += 32; headerRead += watch.Elapsed.TotalMilliseconds - start;
                offset = processed;
                continue;
            }
            ReadOnlySpan<uint> headers = FamilyGpuComputeUtility.UInt32Span(headerData!);
            if (headers.Length != slots * 8) throw new InvalidDataException($"{_scope}.GpuHeaderBankLength:offset={offset}");
            for (int slot = 0; slot < slots; slot++)
            {
                int h = slot * 8, count = Math.Min(Capacity, inputCount - offset - slot * Capacity);
                if (headers[h] != 0x57464d52 || headers[h + 1] != 1 || headers[h + 5] != 0x57463031 ||
                    headers[h + 3] != 0 || headers[h + 4] != count || headers[h + 2] > count)
                    throw new InvalidDataException($"{_scope}.GpuHeaderOrNumericFailure:batch={input.Batch.BatchBase};offset={offset + slot * Capacity};slot={slot};fault={headers[h + 3]};processed={headers[h + 4]};output={headers[h + 2]}");
            }
            headerCalls++; headerBytes += slots * 32L;
            headerRead += watch.Elapsed.TotalMilliseconds - start;
            token.ThrowIfCancellationRequested();
            for (int slot = 0; slot < slots; slot++)
            {
                uint n = headers[slot * 8 + 2];
                if (n == 0) continue;
                double payloadStart = watch.Elapsed.TotalMilliseconds;
                ReadOnlySpan<uint> packed = FamilyGpuComputeUtility.UInt32Span(_rd.BufferGetData(_output[slot], 0, n * 4));
                if (packed.Length != n) throw new InvalidDataException($"{_scope}.GpuPayloadCountMismatch:offset={offset + slot * Capacity}");
                payloadCalls++; payloadBytes += n * 4L;
                foreach (uint ordinal in packed) survivors.Add(ordinal);
                payloadRead += watch.Elapsed.TotalMilliseconds - payloadStart;
            }
            token.ThrowIfCancellationRequested();
            offset = processed;
        }
        if (resident)
        {
            token.ThrowIfCancellationRequested();
            metrics = new(inputCount, privateCount, dispatches, groups, headerCalls, headerBytes, 0, 0,
                dispatchSync, headerRead, watch.Elapsed.TotalMilliseconds);
            return null;
        }
        double sortStart = watch.Elapsed.TotalMilliseconds;
        survivors.Sort();
        double sortMs = watch.Elapsed.TotalMilliseconds - sortStart;
        double validationStart = watch.Elapsed.TotalMilliseconds;
        var result = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors.ToArray());
        if (!input.IsDense && !_privateInput)
        {
            var source = input.ExportAbi1().Span; int index = 0;
            foreach (ulong ordinal in survivors)
            {
                while (index < source.Length && source[index] < ordinal) index++;
                if (index == source.Length || source[index] != ordinal) throw new InvalidDataException(_scope + ".GpuOutputNotInputSubset");
            }
        }
        token.ThrowIfCancellationRequested();
        metrics = new(inputCount, result.Count, dispatches, groups, headerCalls, headerBytes, payloadCalls, payloadBytes,
            dispatchSync, headerRead, watch.Elapsed.TotalMilliseconds);
        double validationMs = watch.Elapsed.TotalMilliseconds - validationStart;
        if (_tracePrivate)
            RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundDetail($"ewStageTiming=true;stage={_scope};input={inputCount};output={result.Count};dispatches={dispatches};groups={groups};dispatchSyncMs={dispatchSync};headerMs={headerRead};payloadMs={payloadRead};readbackBytes={headerBytes+payloadBytes};sortMs={sortMs};validationMs={validationMs};prepareOtherMs={metrics.CanonicalMs-dispatchSync-headerRead-payloadRead-sortMs-validationMs};canonicalMs={metrics.CanonicalMs}");
        return result;
    }
    public void Dispose() { AssertOwner(); _disposed = true; FamilyGpuComputeUtility.FreeAll(_rd, _owned); }
}
