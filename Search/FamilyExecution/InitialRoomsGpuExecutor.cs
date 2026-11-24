using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Godot;
namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct InitialRoomsGpuMetrics(int Input, int Output, int Dispatches, long ReadbackBytes,
    double ReadbackMs, double SortMs, double CanonicalMs, double DispatchSyncMs = 0)
{
    internal int SubmissionGroups { get; init; }
    internal int HeaderReadCalls { get; init; }
}
internal sealed class InitialRoomsGpuExecutor : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = [];
    // Two windows amortize submission while bounding output storage and cancellation latency.
    private const int GroupingK = 2;
    private readonly Rid[] _batch = new Rid[GroupingK], _input = new Rid[GroupingK],
        _header = new Rid[GroupingK], _output = new Rid[GroupingK], _uniforms = new Rid[GroupingK];
    private readonly Rid _pipeline, _headerBank;
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private bool _disposed;
    internal int Capacity { get; }
    internal string Device { get; }
    internal double SetupMs { get; }
    internal long WorkspaceBytes { get; }
    internal InitialRoomsGpuExecutor(RenderingDevice rd, InitialRoomsPlan plan)
    {
        _rd = rd; Capacity = InitialRoomsPlan.Capacity; var watch = Stopwatch.StartNew();
        Rid Add(Rid r) { _owned.Add(r); return r; }
        Rid Data(uint[] data) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(data)));
        try
        {
            var buffers = new Rid[6];
            for (int i = 0; i < plan.Buffers.Length; i++) buffers[i + 1] = Data(plan.Buffers[i]);

            string source = plan.ShaderSource();
            Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "InitialRoomsFamily"));
            _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "InitialRooms"));
            if (!rd.ComputePipelineIsValid(_pipeline)) throw new InvalidOperationException("InitialRooms.PipelineInvalid");
            for (int slot = 0; slot < GroupingK; slot++)
            {
                buffers[0] = _batch[slot] = Data(new uint[8]);
                buffers[3] = _input[slot] = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4));
                buffers[4] = _header[slot] = Data(Header());
                buffers[5] = _output[slot] = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4));
                _uniforms[slot] = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers, "InitialRooms"));
            }
            _headerBank = Data(new uint[GroupingK * 8]);
            WorkspaceBytes = GroupingK * (96L + Capacity * 8L) + plan.Buffers.Sum(b => Math.Max(4, b.Length * 4L));
            Device = rd.GetDeviceName(); SetupMs = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), "InitialRooms");
            throw;
        }
    }
    private static uint[] Header() => [0x494E4954u, 1, 0, 0, 0, 0x494E4954u, 0, 0];
    private void Check() { if (_owner != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("InitialRooms.OwnerMismatch"); ObjectDisposedException.ThrowIf(_disposed, this); }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out InitialRoomsGpuMetrics metrics)
    {
        int inputCount = input.Count;
        Check(); token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew(); var survivors = new List<ulong>();
        int dispatches = 0, groups = 0, headerCalls = 0; long bytes = 0;
        double read = 0, submitSync = 0;
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
                    (uint)input.Batch.BatchCandidateCount, (uint)count, input.IsDense ? 0u : 1u,
                    (uint)Capacity, (uint)windowOffset, 0], "InitialRooms.Batch");
                FamilyGpuComputeUtility.Update(_rd, _header[slot], Header(), "InitialRooms.Header");
                if (!input.IsDense)
                    FamilyGpuComputeUtility.UpdateBytes(_rd, _input[slot], FamilyGpuComputeUtility.PackAbi1UInt32(input, windowOffset, count), "InitialRooms.Input");
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
                    // Finish and drain this bounded group before observing cancellation.
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
                if (recordingFailure is null && slots > 1)
                    for (int slot = 0; slot < slots; slot++)
                    {
                        Error error = _rd.BufferCopy(_header[slot], _headerBank, 0, (uint)(slot * 32), 32);
                        if (error != Error.Ok)
                            throw new InvalidOperationException($"InitialRooms.HeaderBankCopyFailure:batch={input.Batch.BatchBase};offset={offset + slot * Capacity};error={error}");
                    }
            }
            catch (Exception failure) { recordingFailure = failure; }
            // Report record/end/copy failures after draining the group. Pending
            // likewise retains scheduling failures until after Sync.
            var pending = recordingFailure is null
                ? FamilyGpuReadback.Queue(_rd, slots == 1 ? _header[0] : _headerBank, 0, (uint)(slots * 32), "InitialRooms.Header") : null;
            try { FamilyGpuComputeUtility.SubmitAndSync(_rd, "InitialRooms"); }
            catch (Exception syncFailure) when (recordingFailure is not null)
            {
                throw new AggregateException("InitialRooms.RecordAndDrain", recordingFailure, syncFailure);
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
            double start = watch.Elapsed.TotalMilliseconds;
            var headers = FamilyGpuComputeUtility.UInt32Span(headerData!);
            if (headers.Length != slots * 8) throw new InvalidDataException("InitialRooms.HeaderBankLength");
            for (int slot = 0; slot < slots; slot++)
            {
                int h = slot * 8, expected = Math.Min(Capacity, inputCount - offset - slot * Capacity);
                if (headers[h] != 0x494E4954u || headers[h + 1] != 1 || headers[h + 5] != 0x494E4954u ||
                    headers[h + 3] != 0 || headers[h + 4] != expected || headers[h + 2] > expected)
                    throw new InvalidDataException($"InitialRooms.HeaderFault:batch={input.Batch.BatchBase};offset={offset + slot * Capacity};header={string.Join(',', headers.ToArray())}");
            }
            headerCalls++; bytes += slots * 32L;
            read += watch.Elapsed.TotalMilliseconds - start;
            for (int slot = 0; slot < slots; slot++)
            {
                token.ThrowIfCancellationRequested();
                uint n = headers[slot * 8 + 2];
                if (n == 0) continue;
                start = watch.Elapsed.TotalMilliseconds;
                var output = FamilyGpuComputeUtility.UInt32Span(_rd.BufferGetData(_output[slot], 0, n * 4));
                if (output.Length != n) throw new InvalidDataException("InitialRooms.PayloadMismatch");
                bytes += n * 4L; read += watch.Elapsed.TotalMilliseconds - start;
                foreach (uint ordinal in output) survivors.Add(ordinal);
            }
            offset = processed;
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
                if (i == source.Length || source[i] != ordinal) throw new InvalidDataException("InitialRooms.OutputNotInputSubset");
            }
        }
        token.ThrowIfCancellationRequested();
        double validation = watch.Elapsed.TotalMilliseconds - validationStart;
        metrics = new(inputCount, result.Count, dispatches, bytes, read, sort, watch.Elapsed.TotalMilliseconds, submitSync)
            { SubmissionGroups = groups, HeaderReadCalls = headerCalls };
        return result;
    }
    public void Dispose() { if (_disposed) return; Check(); _disposed = true; FamilyGpuComputeUtility.FreeAll(_rd, _owned); }
}
