using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed class NwaePrivateInvocation(ExactSearchExecutionRequest request, NeowFamilyGpuPlan? n,
    AncientOptionFamilyPlan? a, EventResultFamilyPlan? e, WorldFamilyGpuPlan w, string order, bool verify) : IFamilyInvocation
{
    private NwaePrivateGpuExecutor? _gpu;
    private long _input, _output;
    private int _batches;
    private double _ms;
    private bool _faulted;
    public string FamilyId => order[0] switch { 'N' => "N.Neow", 'A' => "A.AncientOption", 'E' => "E.EventResult", 'W' => "W.World", _ => throw new InvalidOperationException("NWAE.Entry") };
    public IReadOnlyList<string> Coverage => order.Select(c => c switch {
        'N' => "N.Neow", 'A' => "A.AncientOption", 'E' => "E.EventResult", 'W' => "W.World", _ => throw new InvalidOperationException("NWAE.Coverage") }).ToArray();
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, PrivateOrdinalBuffer.Capacity, [], "NWAE.ExperimentalUnpriced");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId, "NWAE.ExperimentalJointUnpriced");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,
        "NWAE." + order + ".PrivateSerial.CanonicalAbi1Ready.20260908.v1", "NWAE.Neutral", 1, "ExplicitExperiment", usesGpu: true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, _gpu?.Device ?? "", _gpu?.SetupMs ?? 0,
        0, 0, _batches, 0, _input, _output, _ms, false, _faulted ? "PhysicalFailure" : "NWAE.ExperimentNoCalibration");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow observation, FamilyCandidateSet input, CancellationToken token)
    {
        if (_faulted || request.SnapshotFingerprint != observation.ExactRequest.SnapshotFingerprint) throw new InvalidOperationException("NWAE.InvocationInvariant");
        try
        {
            _gpu ??= await context.ExecuteGpuAsync(rd => new NwaePrivateGpuExecutor(rd,n,a,e,w,order,verify), token).ConfigureAwait(false);
            var r = await context.ExecuteGpuAsync(_ => { var output = _gpu.Execute(input,token,out var ms); return (output,ms); }, token).ConfigureAwait(false);
            _batches++; _input += input.Count; _output += r.output.Count; _ms += r.ms;
            return r.output;
        }
        catch (OperationCanceledException) { throw; }
        catch { _faulted = true; throw; }
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpu is not null) await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"nwaePrivateSummary=true;order={order};batches={_batches};input={_input};output={_output};canonicalMs={_ms};faulted={_faulted};recovery=None");
    }
}
