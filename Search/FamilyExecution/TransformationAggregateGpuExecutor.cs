using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Godot;
namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct TransformationAggregateGpuMetrics(int Input, int Output, int Dispatches, long ReadbackBytes,
    double ReadbackMs, double SortMs, double CanonicalMs, double DispatchSyncMs = 0)
{
    internal int SubmissionGroups { get; init; }
}
internal sealed class TransformationAggregateGpuExecutor : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = [];
    private readonly Rid[] _batch, _input, _header, _output, _uniforms;
    private readonly Rid _pipeline;
    private readonly int _groupingK;
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private bool _disposed;
    private readonly bool _privateInput, _privateOutput;
    private readonly int _seedsPerInvocation;
    private readonly bool _tracePrivate = EwPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    internal int Capacity { get; }
    internal GpuNumericalVariant NumericalVariant { get; }
    internal string Device { get; }
    internal double SetupMs { get; }
    internal long WorkspaceBytes { get; }
    internal TransformationAggregateGpuExecutor(RenderingDevice rd, TransformationAggregateGpuPlan plan,
        PrivateOrdinalBuffer? privateInput = null, PrivateOrdinalBuffer? privateOutput = null)
    {
        if (plan.NumericalVariant == GpuNumericalVariant.IntegerRne && (privateInput is not null || privateOutput is not null))
            throw new InvalidOperationException("T.IntegerRne.PublicTransportOnly");
        _privateInput = privateInput is not null; _privateOutput = privateOutput is not null;
        if (_privateInput && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("T.PrivateBuffersAlias");
        _rd = rd; Capacity = plan.Capacity; NumericalVariant = plan.NumericalVariant; _seedsPerInvocation=plan.SeedsPerInvocation; var watch = Stopwatch.StartNew();
        // The full-target 16m window already uses 128 MiB for public ordinals.
        // Keep it single; normal windows add at most one 8 MiB ordinal pair.
        _groupingK = _privateOutput ? 4 : Capacity <= TransformationAggregateGpuPlan.WindowCapacity ? 2 : 1;
        _batch=new Rid[_groupingK]; _input=new Rid[_groupingK]; _header=new Rid[_groupingK];
        _output=new Rid[_groupingK]; _uniforms=new Rid[_groupingK];
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
                    throw new InvalidOperationException("T.PrivateInputShaderSeam");
                source = source.Replace(address, "input_ids.v[batch.v[6]+i]", StringComparison.Ordinal);
            }
            Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "TransformationAggregateFamily"));
            _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "T.TransformationAggregate"));
            if (!rd.ComputePipelineIsValid(_pipeline)) throw new InvalidOperationException("T.TransformationAggregate.PipelineInvalid");
            for(int slot=0;slot<_groupingK;slot++)
            {
                buffers[0]=_batch[slot]=Data(new uint[8]);
                buffers[3]=_input[slot]=privateInput?.Buffer ?? (_privateOutput && slot>0 ? _input[0] : Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity*4)));
                buffers[4]=_header[slot]=_privateOutput && slot>0 ? _header[0] : Data(Header());
                buffers[5]=_output[slot]=privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity*4));
                _uniforms[slot]=Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers, "T.TransformationAggregate"));
            }
            WorkspaceBytes = _groupingK*32L + (_privateOutput ? 32 : _groupingK*32L) +
                (privateInput is not null ? 0 : Capacity*4L*(_privateOutput ? 1 : _groupingK)) +
                (privateOutput is not null ? 0 : Capacity*4L*_groupingK) + plan.Buffers.Sum(b => Math.Max(4, b.Length*4L));
            Device = rd.GetDeviceName(); SetupMs = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), "T.TransformationAggregate");
            throw;
        }
    }
    private static uint[] Header() => [0x5452414Eu, 1, 0, 0, 0, 0x5452414Eu, 0, 0];
    private void Check() { if (_owner != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("T.TransformationAggregate.OwnerMismatch"); ObjectDisposedException.ThrowIf(_disposed, this); }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out TransformationAggregateGpuMetrics metrics)
    {
        if (_privateInput || _privateOutput) throw new InvalidOperationException("T.PrivateEntryRequired");
        return ExecuteCore(input, input.Count, token, out metrics)!;
    }
    internal FamilyCandidateSet ExecutePrivate(SearchBatch batch, int count, CancellationToken token, out TransformationAggregateGpuMetrics metrics)
    {
        if (!_privateInput || _privateOutput || count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity)
            throw new InvalidDataException("T.PrivateTerminalBounds");
        return ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics)!;
    }
    internal int ExecutePrivateStage(SearchBatch batch, int count, CancellationToken token, out TransformationAggregateGpuMetrics metrics)
    {
        if (!_privateOutput || (!_privateInput && count != batch.BatchCandidateCount) || count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity)
            throw new InvalidDataException("T.PrivateStageBounds");
        ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics);
        return metrics.Output;
    }
    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input, int inputCount, CancellationToken token, out TransformationAggregateGpuMetrics metrics)
    {
        Check(); token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew(); var survivors = new List<ulong>(); int dispatches = 0, groups = 0; long bytes = 0; double read = 0;
        double submitSync = 0, widen = 0;
        int privateCount = 0;
        if (_privateOutput) FamilyGpuComputeUtility.Update(_rd, _header[0], Header(), "T.PrivateHeader");
        for (int offset = 0; offset < inputCount;)
        {
            token.ThrowIfCancellationRequested();
            int slots=Math.Min(_groupingK,(inputCount-offset-1)/Capacity+1);
            for(int slot=0;slot<slots;slot++)
            {
                token.ThrowIfCancellationRequested();
                int windowOffset=offset+slot*Capacity,count=Math.Min(Capacity,inputCount-windowOffset);
                FamilyGpuComputeUtility.Update(_rd, _batch[slot], [(uint)input.Batch.BatchBase,(uint)(input.Batch.BatchBase>>32),
                    (uint)input.Batch.BatchCandidateCount,(uint)count,input.IsDense && !_privateInput?0u:1u,
                    (uint)(_privateOutput ? PrivateOrdinalBuffer.Capacity : Capacity),(uint)windowOffset,0], "T.TransformationAggregate.Batch");
                if (!_privateOutput) FamilyGpuComputeUtility.Update(_rd, _header[slot], Header(), "T.TransformationAggregate.Header");
                if(!input.IsDense && !_privateInput)
                    FamilyGpuComputeUtility.UpdateBytes(_rd,_input[slot],FamilyGpuComputeUtility.PackAbi1UInt32(input,windowOffset,count),"T.TransformationAggregate.Input");
            }
            token.ThrowIfCancellationRequested();
            int processed=offset+Math.Min(inputCount-offset,slots*Capacity);
            int headerCount=_privateOutput ? processed==inputCount ? 1 : 0 : slots;
            var pendingHeaders=new FamilyGpuReadback.Pending[headerCount];
            var headerData=new byte[headerCount][];
            double dispatchStart = watch.Elapsed.TotalMilliseconds;
            Exception? recordingFailure=null;
            long list = _rd.ComputeListBegin();
            try
            {
                _rd.ComputeListBindComputePipeline(list, _pipeline);
                for(int slot=0;slot<slots;slot++)
                {
                    // Finish this bounded group before observing cancellation so
                    // queued work cannot outlive reused/freed buffers (<=4 windows).
                    if(slot>0)_rd.ComputeListAddBarrier(list);
                    _rd.ComputeListBindUniformSet(list, _uniforms[slot], 0);
                    int count=Math.Min(Capacity,inputCount-offset-slot*Capacity),groupRoots=64*_seedsPerInvocation;
                    _rd.ComputeListDispatch(list, (uint)((count + groupRoots - 1) / groupRoots), 1, 1);
                    dispatches++;
                }
            }
            catch(Exception failure) { recordingFailure=failure; }
            try { _rd.ComputeListEnd(); }
            catch(Exception failure) { recordingFailure=recordingFailure is null ? failure : new AggregateException(recordingFailure,failure); }
            if(recordingFailure is not null)
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(recordingFailure,
                    ()=>FamilyGpuComputeUtility.SubmitAndSync(_rd,"T.TransformationAggregate"),"T.RecordAndDrain");
                ExceptionDispatchInfo.Capture(recordingFailure).Throw();
            }
            for(int slot=0;slot<headerCount;slot++)
                pendingHeaders[slot]=FamilyGpuReadback.Queue(_rd,_header[slot],0,32,"T.Header");
            FamilyGpuComputeUtility.SubmitAndSync(_rd, "T.TransformationAggregate"); groups++;
            for(int slot=0;slot<headerCount;slot++) headerData[slot]=pendingHeaders[slot].GetData();
            submitSync += watch.Elapsed.TotalMilliseconds - dispatchStart;
            token.ThrowIfCancellationRequested();
            // Appends have no CPU dependency; sticky fault and processed counters
            // validate all resident windows together after the final submission.
            if(_privateOutput && processed<inputCount) { offset=processed; continue; }
            double start = watch.Elapsed.TotalMilliseconds;
            for(int slot=0;slot<(_privateOutput?1:slots);slot++)
            {
                ReadOnlySpan<uint> h = FamilyGpuComputeUtility.UInt32Span(headerData[slot]);
                int expected = _privateOutput ? inputCount : Math.Min(Capacity,inputCount-offset-slot*Capacity);
                if (h.Length != 8 || h[0] != 0x5452414Eu || h[1] != 1 || h[5] != 0x5452414Eu || h[3] != 0 || h[4] != expected || h[2] > expected)
                    throw new InvalidDataException($"T.TransformationAggregate.HeaderFault:batch={input.Batch.BatchBase};offset={offset+slot*Capacity};header={string.Join(',', h.ToArray())}");
                bytes+=32;
                if (_privateOutput) { privateCount = checked((int)h[2]); read += watch.Elapsed.TotalMilliseconds-start; break; }
                uint n = h[2]; bytes += n * 4L;
                ReadOnlySpan<uint> output = n == 0 ? [] : FamilyGpuComputeUtility.UInt32Span(_rd.BufferGetData(_output[slot], 0, n * 4));
                if (output.Length != n) throw new InvalidDataException("T.TransformationAggregate.PayloadMismatch");
                read += watch.Elapsed.TotalMilliseconds - start;
                double widenStart = watch.Elapsed.TotalMilliseconds;
                foreach (uint ordinal in output) survivors.Add(ordinal);
                widen += watch.Elapsed.TotalMilliseconds - widenStart;
                start=watch.Elapsed.TotalMilliseconds;
            }
            offset=processed;
        }
        if (_privateOutput)
        {
            token.ThrowIfCancellationRequested();
            metrics = new(inputCount, privateCount, dispatches, bytes, read, 0, watch.Elapsed.TotalMilliseconds, submitSync) { SubmissionGroups=groups };
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
                if (i == source.Length || source[i] != ordinal) throw new InvalidDataException("T.TransformationAggregate.OutputNotInputSubset");
            }
        }
        token.ThrowIfCancellationRequested();
        double validation = watch.Elapsed.TotalMilliseconds - validationStart;
        metrics = new(inputCount, result.Count, dispatches, bytes, read, sort, watch.Elapsed.TotalMilliseconds, submitSync) { SubmissionGroups=groups };
        if (_tracePrivate)
            RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundDetail($"transformationStageTiming=true;stage=T;input={input.Count};output={result.Count};dispatches={dispatches};groups={groups};dispatchSyncMs={submitSync};readbackBytes={bytes};readbackMs={read};widenMs={widen};sortMs={sort};validationMs={validation};prepareOtherMs={metrics.CanonicalMs-submitSync-read-widen-sort-validation};canonicalMs={metrics.CanonicalMs}");
        return result;
    }
    public void Dispose() { if (_disposed) return; Check(); _disposed = true; FamilyGpuComputeUtility.FreeAll(_rd, _owned); }
}
