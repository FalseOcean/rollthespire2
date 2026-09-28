using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit ECW connectivity probe only. Three existing bodies, two borrowed
// ordinal buffers; no ordering economics or generic chain representation.
internal sealed class EcwPrivateClosureProbe(ExactSearchExecutionRequest request, EventResultFamilyPlan e,
    CombatRewardGpuPlan c, WorldFamilyGpuPlan w, bool verify) : IFamilyInvocation
{
    private Gpu? _gpu;
    private bool _faulted;
    public string FamilyId => "E.EventResult";
    public IReadOnlyList<string> Coverage => ["E.EventResult","C.CombatReward","W.World"];
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId,PrivateOrdinalBuffer.Capacity,[],"ECW.ClosureProbe.Unpriced");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId,"ECW.ClosureProbe");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,"ECW.PrivateConnectivity.Probe.20260910.v1","Neutral",1,"ClosureProbe",usesGpu:true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance,"",_gpu?.SetupMs??0,0,0,0,0,0,0,0,false,_faulted?"PhysicalFailure":"ClosureProbeNoCalibration");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context,FamilyObservationWindow window,FamilyCandidateSet input,CancellationToken token)
    {
        if(_faulted || window.ExactRequest.SnapshotFingerprint!=request.SnapshotFingerprint)throw new InvalidOperationException("ECW.ObservationInvariant");
        try {
            _gpu ??= await context.ExecuteGpuAsync(rd=>new Gpu(rd,e,c,w,verify),token).ConfigureAwait(false);
            return await context.ExecuteGpuAsync(_=>_gpu.Execute(input,token),token).ConfigureAwait(false);
        } catch(OperationCanceledException){throw;} catch{_faulted=true;throw;}
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if(_gpu is not null)await context.ExecuteGpuAsync(_=>{_gpu.Dispose();return true;},CancellationToken.None).ConfigureAwait(false);
    }
    private sealed class Gpu : IDisposable
    {
        private readonly PrivateOrdinalBuffer _ec=null!,_cw=null!;
        private readonly EventResultFamilyGpuExecutor _e=null!,_referenceE=null!;
        private readonly CombatRewardGpuExecutor _c=null!,_referenceC=null!;
        private readonly WorldFamilyGpuExecutor _w=null!,_referenceW=null!;
        private readonly bool _verify;
        private bool _disposed;
        internal double SetupMs { get; }
        internal Gpu(RenderingDevice rd,EventResultFamilyPlan e,CombatRewardGpuPlan c,WorldFamilyGpuPlan w,bool verify)
        {
            _verify=verify;
            var setup=System.Diagnostics.Stopwatch.StartNew();
            try {
                _ec=new(rd);_cw=new(rd);
                _e=new(rd,e,privateOutput:_ec);
                _c=new(rd,c,privateInput:_ec,privateOutput:_cw);
                _w=new(rd,w,_cw.Buffer);
                SetupMs=setup.Elapsed.TotalMilliseconds;
                if(verify){_referenceE=new(rd,e);_referenceC=new(rd,c);_referenceW=new(rd,w);}
                Bootstrap.RuntimeLog.TryBackgroundInfo($"ecwClosureSetup=true;productionSetupMs={SetupMs};retainedOrdinalBytes={2L*PrivateOrdinalBuffer.Capacity*4};verificationExcluded=true");
            } catch{Release();throw;}
        }
        internal FamilyCandidateSet Execute(FamilyCandidateSet input,CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!input.IsDense)throw new InvalidOperationException("ECW.DenseEntryRequired");
            _ec.CheckPopulation(input.Batch,input.Count);
            int ep=_e.ExecutePrivateStage(input.Batch,input.Count,token,out var em);
            _ec.CheckPopulation(input.Batch,ep);
            int cp=_c.ExecutePrivateStage(input.Batch,ep,token,out var cm);
            _cw.CheckPopulation(input.Batch,cp);
            if(cp>ep)throw new InvalidDataException("ECW.CExpandedPopulation");
            var output=_w.ExecutePrivate(input.Batch,cp,token,out var wm);
            if(_verify){
                var expectedE=_referenceE.Execute(input,token,out _);_ec.VerifyPopulation(input.Batch,ep,expectedE);
                var expectedC=_referenceC.Execute(expectedE,token,out _);_cw.VerifyPopulation(input.Batch,cp,expectedC);
                var expectedW=_referenceW.Execute(expectedC,token,out _);
                if(!expectedW.EnumerateLogicalOrdinals().SequenceEqual(output.EnumerateLogicalOrdinals()))throw new InvalidDataException("ECW.FinalPopulationMismatch");
            }
            Bootstrap.RuntimeLog.TryBackgroundInfo($"ecwClosureBatch=true;input={input.Count};eOutput={ep};cOutput={cp};wOutput={output.Count};cWindows={cm.Dispatches};cHeaderBytes={cm.ReadbackBytes};eDispatchSyncMs={em.DispatchSyncMs};cDispatchSyncMs={cm.DispatchSyncMs};wDispatchSyncMs={wm.DispatchSyncMs};intermediatePayloadBytes=0;verificationPayloadExcluded=true;fullPopulationParity={_verify};recovery=None");
            return output;
        }
        public void Dispose(){if(_disposed)return;_disposed=true;Release();}
        private void Release()=>PrivateOrdinalBuffer.DisposeAll(_referenceW,_referenceC,_referenceE,_w,_c,_e,_cw,_ec);
    }
}
