using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct CombatRewardGpuMetrics(int Input, int Output, int Dispatches, long ReadbackBytes,
    double UploadMs, double DispatchSyncMs, double ReadbackMs, double CanonicalMs)
{
    internal int SubmissionGroups { get; init; }
}

// Bounded private dispatch windows, never a change to the root SearchBatch/ABI.
internal sealed class CombatRewardGpuExecutor : IDisposable
{
    internal const int Capacity = 1 << 20;
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = new();
    private readonly Rid[] _batch, _header, _input, _output, _uniforms;
    private readonly Rid _pipeline;
    private readonly int _groupingK;
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private readonly int _routes;
    private readonly PrivateOrdinalBuffer? _privateOutput;
    private readonly bool _privateInput;
    private readonly PrivateOrdinalBuffer? _privateInputBuffer;
    private bool _disposed;
    private readonly bool _tracePrivate = NcPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested;
    internal string Device { get; }
    internal double SetupMs { get; }
    internal CombatRewardGpuExecutor(RenderingDevice rd, CombatRewardGpuPlan plan, bool forceGeneric = false, PrivateOrdinalBuffer? privateOutput = null, PrivateOrdinalBuffer? privateInput = null)
    {
        if (plan.NumericalVariant != GpuNumericalVariant.NativeFloat64 &&
            (privateInput is not null || privateOutput is not null))
            throw new InvalidOperationException("C.IntegerRne.PublicTransportOnly");
        _rd = rd; _routes = plan.Routes; _privateOutput = privateOutput;
        _privateInput=privateInput is not null; _privateInputBuffer=privateInput;
        if (_privateInput && ReferenceEquals(privateInput,privateOutput)) throw new ArgumentException("C.PrivateBuffersAlias");
        // Public transport adds at most one 8 MiB input/output pair. Resident
        // transport borrows its ordinal buffers and needs only small meta slots.
        _groupingK = privateOutput is null ? 2 : 4;
        _batch=new Rid[_groupingK]; _header=new Rid[_groupingK]; _input=new Rid[_groupingK];
        _output=new Rid[_groupingK]; _uniforms=new Rid[_groupingK];
        var watch = Stopwatch.StartNew();
        Rid Add(Rid rid) { _owned.Add(rid); return rid; }
        Rid Buffer(uint[] values) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(values)));
        try
        {
            Rid[] buffers=[default,Buffer(plan.Meta),Buffer([0]),Buffer(plan.Pools),Buffer(plan.Ids),Buffer([0]),
                Buffer(plan.Predicates),Buffer(plan.Targets),default,default,default];
            string source=plan.ShaderSource(forceGeneric);
            if (_privateInput)
            {
                const string read="input_ordinals.values[index]";
                if(!source.Contains(read,StringComparison.Ordinal))throw new InvalidOperationException("C.PrivateInputSeamChanged");
                source=source.Replace(read,"input_ordinals.values[batch_meta.values[6]+index]",StringComparison.Ordinal);
            }
            Rid shader=Add(FamilyGpuComputeUtility.CompileShader(rd,source,"CombatRewardFamily"));
            _pipeline=Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "C.CombatReward"));
            if(!rd.ComputePipelineIsValid(_pipeline))throw new InvalidOperationException("C.GpuPipelineInvalid");
            for (int slot=0;slot<_groupingK;slot++)
            {
                buffers[0]=_batch[slot]=Buffer(new uint[8]);
                buffers[8]=_header[slot]=privateOutput is not null && slot>0 ? _header[0] : Buffer(Header());
                buffers[9]=_output[slot]=privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity*4));
                buffers[10]=_input[slot]=privateInput?.Buffer ?? (privateOutput is not null && slot>0
                    ? _input[0] : Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity*4)));
                _uniforms[slot]=Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers, "C.CombatReward"));
            }
            Device=rd.GetDeviceName(); SetupMs=watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), "C.CombatReward");
            throw;
        }
    }
    private static uint[] Header() => [0x43464d52,1,0,0,0,0x43463031,0,0];
    private void AssertOwner()
    {
        if(_owner!=System.Environment.CurrentManagedThreadId)throw new InvalidOperationException("C.GpuOwnerMismatch");
        ObjectDisposedException.ThrowIf(_disposed,this);
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input,CancellationToken token,out CombatRewardGpuMetrics metrics)
    {
        if (_privateOutput is not null || _privateInput) throw new InvalidOperationException("C.PrivateTransportRequiresPrivateEntry");
        return ExecuteCore(input, token, out metrics, out _)!;
    }
    internal FamilyCandidateSet ExecutePrivateInput(SearchBatch batch,int count,CancellationToken token,out CombatRewardGpuMetrics metrics)
    {
        if(!_privateInput || _privateOutput is not null || count<0 || count>batch.BatchCandidateCount || count>PrivateOrdinalBuffer.Capacity)throw new InvalidDataException("C.PrivateInputCountInvalid");
        _privateInputBuffer!.CheckPopulation(batch,count);
        return ExecuteCore(FamilyCandidateSet.Dense(batch),token,out metrics,out _,count)!;
    }

    internal int ExecutePrivate(FamilyCandidateSet input,CancellationToken token,out CombatRewardGpuMetrics metrics)
    {
        if (_privateOutput is null || _privateInput || !input.IsDense) throw new InvalidOperationException("C.PrivateDenseOnly");
        _privateOutput.CheckPopulation(input.Batch,input.Count);
        ExecuteCore(input, token, out metrics, out int count);
        return count;
    }
    // Both endpoints borrow distinct buffers; ordinal order is private and arbitrary.
    internal int ExecutePrivateStage(SearchBatch batch,int count,CancellationToken token,out CombatRewardGpuMetrics metrics)
    {
        if(!_privateInput || _privateOutput is null) throw new InvalidOperationException("C.PrivateMiddlePortsRequired");
        _privateInputBuffer!.CheckPopulation(batch,count);
        _privateOutput.CheckPopulation(batch,count);
        ExecuteCore(FamilyCandidateSet.Dense(batch),token,out metrics,out int output,count);
        return output;
    }
    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input,CancellationToken token,out CombatRewardGpuMetrics metrics, out int privateCount,int? privateInputCount=null)
    {
        AssertOwner(); token.ThrowIfCancellationRequested();
        privateCount = 0;
        bool resident = _privateOutput is not null;
        int inputCount=privateInputCount??input.Count;
        bool dense=input.IsDense&&!_privateInput;
        if (resident && !input.IsDense) throw new InvalidOperationException("C.PrivateDenseOnly");
        var watch=Stopwatch.StartNew(); var survivors=new List<ulong>();
        double upload=0,sync=0,read=0,widen=0; long bytes=0; int dispatches=0,groups=0;
        if (resident) FamilyGpuComputeUtility.Update(_rd,_header[0],Header(),"C.PrivateHeader");
        for(int offset=0;offset<inputCount;)
        {
            token.ThrowIfCancellationRequested();
            int slots=Math.Min(_groupingK,(inputCount-offset-1)/Capacity+1);
            double start=watch.Elapsed.TotalMilliseconds;
            for(int slot=0;slot<slots;slot++)
            {
                token.ThrowIfCancellationRequested();
                int windowOffset=offset+slot*Capacity,count=Math.Min(Capacity,inputCount-windowOffset);
                FamilyGpuComputeUtility.Update(_rd,_batch[slot],[(uint)input.Batch.BatchBase,(uint)(input.Batch.BatchBase>>32),
                    (uint)input.Batch.BatchCandidateCount,(uint)count,dense?0u:1u,(uint)(resident?PrivateOrdinalBuffer.Capacity:Capacity),(uint)windowOffset,(uint)_routes],"C.Batch");
                if (!resident) FamilyGpuComputeUtility.Update(_rd,_header[slot],Header(),"C.Header");
                if(!input.IsDense)
                    FamilyGpuComputeUtility.UpdateBytes(_rd,_input[slot],FamilyGpuComputeUtility.PackAbi1UInt32(input,windowOffset,count),"C.CompactInput");
            }
            upload+=watch.Elapsed.TotalMilliseconds-start;
            token.ThrowIfCancellationRequested();
            int processed=offset+Math.Min(inputCount-offset,slots*Capacity);
            int headerCount=resident ? processed==inputCount ? 1 : 0 : slots;
            var pendingHeaders=new FamilyGpuReadback.Pending[headerCount];
            var headerData=new byte[headerCount][];
            start=watch.Elapsed.TotalMilliseconds;
            Exception? recordingFailure=null;
            long list=_rd.ComputeListBegin();
            try
            {
                _rd.ComputeListBindComputePipeline(list,_pipeline);
                for(int slot=0;slot<slots;slot++)
                {
                    // Cancellation is observed around each bounded group. Once a list
                    // starts, drain it before reusing/disposal; at most four windows.
                    if(slot>0)_rd.ComputeListAddBarrier(list);
                    _rd.ComputeListBindUniformSet(list,_uniforms[slot],0);
                    int count=Math.Min(Capacity,inputCount-offset-slot*Capacity);
                    _rd.ComputeListDispatch(list,(uint)((count+63)/64),1,1);
                    dispatches++;
                }
            }
            catch(Exception failure) { recordingFailure=failure; }
            try { _rd.ComputeListEnd(); }
            catch(Exception failure) { recordingFailure=recordingFailure is null ? failure : new AggregateException(recordingFailure,failure); }
            if(recordingFailure is not null)
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(recordingFailure,
                    ()=>FamilyGpuComputeUtility.SubmitAndSync(_rd,"C.CombatReward"),"C.RecordAndDrain");
                ExceptionDispatchInfo.Capture(recordingFailure).Throw();
            }
            for(int slot=0;slot<headerCount;slot++)
                pendingHeaders[slot]=FamilyGpuReadback.Queue(_rd,_header[slot],0,32,"C.Header");
            FamilyGpuComputeUtility.SubmitAndSync(_rd, "C.CombatReward"); groups++;
            for(int slot=0;slot<headerCount;slot++) headerData[slot]=pendingHeaders[slot].GetData();
            sync+=watch.Elapsed.TotalMilliseconds-start;
            token.ThrowIfCancellationRequested();
            // These windows append independently. Only the final cumulative header
            // is needed; its sticky faults/processed count cover every dispatch.
            if (resident && processed<inputCount) { offset=processed; continue; }
            start=watch.Elapsed.TotalMilliseconds;
            for(int slot=0;slot<(resident?1:slots);slot++)
            {
                ReadOnlySpan<uint> header=FamilyGpuComputeUtility.UInt32Span(headerData[slot]);
                int expected=resident?inputCount:Math.Min(Capacity,inputCount-offset-slot*Capacity);
                if(header.Length!=8||header[0]!=0x43464d52||header[1]!=1||header[5]!=0x43463031||
                    header[3]!=0||header[4]!=expected||header[2]>expected)
                    throw new InvalidDataException($"C.GpuHeaderOrNumericFailure:batch={input.Batch.BatchBase};offset={offset+slot*Capacity};header={string.Join(',',header.ToArray())}");
                bytes+=32;
                if (resident)
                {
                    privateCount=checked((int)header[2]); read+=watch.Elapsed.TotalMilliseconds-start;
                    metrics=new(inputCount,privateCount,dispatches,bytes,upload,sync,read,watch.Elapsed.TotalMilliseconds) { SubmissionGroups=groups };
                    return null;
                }
                uint n=header[2];bytes+=n*4L;
                ReadOnlySpan<uint> packedOutput=n==0?[]:FamilyGpuComputeUtility.UInt32Span(_rd.BufferGetData(_output[slot],0,n*4));
                if(packedOutput.Length!=n)throw new InvalidDataException("C.GpuPayloadCountMismatch");
                read+=watch.Elapsed.TotalMilliseconds-start;
                start=watch.Elapsed.TotalMilliseconds;
                foreach(uint ordinal in packedOutput)survivors.Add(ordinal);
                widen+=watch.Elapsed.TotalMilliseconds-start;
                start=watch.Elapsed.TotalMilliseconds;
            }
            offset=processed;
        }
        if(resident)
        {
            // Zero input queues no dispatch and has no stale header dependency.
            metrics=new(inputCount,0,dispatches,bytes,upload,sync,read,watch.Elapsed.TotalMilliseconds) { SubmissionGroups=groups };
            return null;
        }
        double sortStart=watch.Elapsed.TotalMilliseconds;
        survivors.Sort();
        double sort=watch.Elapsed.TotalMilliseconds-sortStart,validationStart=watch.Elapsed.TotalMilliseconds;
        var result=FamilyCandidateSet.FromSortedAbi1(input.Batch,survivors.ToArray());
        if(!input.IsDense)
        {
            var source=input.ExportAbi1().Span;int index=0;
            foreach(ulong ordinal in survivors)
            {
                while(index<source.Length&&source[index]<ordinal)index++;
                if(index==source.Length||source[index]!=ordinal)throw new InvalidDataException("C.GpuOutputNotInputSubset");
            }
        }
        token.ThrowIfCancellationRequested();
        metrics=new(inputCount,result.Count,dispatches,bytes,upload,sync,read,watch.Elapsed.TotalMilliseconds) { SubmissionGroups=groups };
        if (_tracePrivate)
            Bootstrap.RuntimeLog.TryBackgroundDetail($"ncStageTiming=true;stage=C;input={inputCount};output={result.Count};dispatches={dispatches};groups={groups};uploadMs={upload};dispatchSyncMs={sync};readbackMs={read};readbackBytes={bytes};widenMs={widen};sortMs={sort};validationMs={watch.Elapsed.TotalMilliseconds-validationStart};canonicalMs={metrics.CanonicalMs}");
        return result;
    }
    public void Dispose(){AssertOwner();_disposed=true;FamilyGpuComputeUtility.FreeAll(_rd, _owned);}
}
