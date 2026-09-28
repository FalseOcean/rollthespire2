using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed class NcPrivateInvocation(ExactSearchExecutionRequest request, CombatRewardGpuPlan c, NeowFamilyGpuPlan n, string mode) : IFamilyInvocation
{
    private NcPrivateGpuExecutor? _gpu;
    private long _input, _output;
    private int _batches;
    private double _ms;
    private bool _faulted;
    public string FamilyId => mode.EndsWith("-nc",StringComparison.Ordinal)?"N.Neow":"C.CombatReward";
    public IReadOnlyList<string> Coverage => ["C.CombatReward","N.Neow"];
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId,PrivateOrdinalBuffer.Capacity,[],"NC.UnpricedEquivalentCoverage");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId,"NC.JointUnpriced");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,
        (mode.EndsWith("-nc",StringComparison.Ordinal)?"N.Neow.Composite.C.CombatReward.":"C.CombatReward.Composite.N.Neow.")+"PrivateOrdinal.CanonicalAbi1Ready.20260908.v2", "NC.Neutral",1,"NC.UnpricedCandidate",usesGpu:true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance,_gpu?.Device??"",_gpu?.SetupMs??0,
        0,0,_batches,0,_input,_output,_ms,false,_faulted?"PhysicalFailureOrRecovery":"NC.CandidateNoCalibration");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow observation, FamilyCandidateSet input, CancellationToken token)
    {
        if (_faulted || request.SnapshotFingerprint != observation.ExactRequest.SnapshotFingerprint) throw new InvalidOperationException("NC.InvocationInvariant");
        try
        {
            _gpu ??= await context.ExecuteGpuAsync(rd=>new NcPrivateGpuExecutor(rd,c,n,(uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(request.SnapshotFingerprint,0),mode.StartsWith("verify-",StringComparison.Ordinal),mode.EndsWith("-nc",StringComparison.Ordinal)),token).ConfigureAwait(false);
            var result = await context.ExecuteGpuAsync(_=> { var output=_gpu.Execute(input,token,out var ms); return (output,ms); },token).ConfigureAwait(false);
            _batches++; _input+=input.Count; _output+=result.output.Count; _ms+=result.ms;
            return result.output;
        }
        catch (OperationCanceledException) { throw; }
        catch { _faulted=true; throw; }
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpu is not null) await context.ExecuteGpuAsync(_=>{_gpu.Dispose();return true;},CancellationToken.None).ConfigureAwait(false);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"ncPrivateSummary=true;mode={mode};batches={_batches};input={_input};output={_output};canonicalMs={_ms};faulted={_faulted};recovery=None");
    }
}
