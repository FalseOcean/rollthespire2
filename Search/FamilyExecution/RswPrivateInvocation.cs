using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed class RswPrivateInvocation(ExactSearchExecutionRequest request, RelicFamilyPlan r, WorldFamilyGpuPlan w, string order, bool verify) : IFamilyInvocation
{
    private RswPrivateGpuExecutor? _gpu;
    private long _input, _output;
    private int _batches;
    private double _ms;
    private bool _faulted;
    public string FamilyId => Name(order[0]);
    public IReadOnlyList<string> Coverage => order.Select(Name).ToArray();
    internal static string Name(char c) => c switch { 'R'=>"R.Relic",'S'=>"S.MerchantShopColorless",'W'=>"W.World",_=>throw new InvalidOperationException("RSW.Coverage") };
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, PrivateOrdinalBuffer.Capacity, [], "RSW.ExperimentalUnpriced");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId, "RSW.ExperimentalJointUnpriced");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,
        "RSW." + order + ".PrivateSerial.CanonicalAbi1Ready.20260908.v1", "RSW.Neutral", 1, "ExplicitExperiment", usesGpu: true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, _gpu?.Device ?? "", _gpu?.SetupMs ?? 0,
        0, 0, _batches, 0, _input, _output, _ms, false, _faulted ? "PhysicalFailure" : "RSW.ExperimentNoCalibration");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow observation, FamilyCandidateSet input, CancellationToken token)
    {
        if (_faulted || request.SnapshotFingerprint != observation.ExactRequest.SnapshotFingerprint) throw new InvalidOperationException("RSW.InvocationInvariant");
        try
        {
            _gpu ??= await context.ExecuteGpuAsync(rd => new RswPrivateGpuExecutor(rd,request,r,w,order,verify), token).ConfigureAwait(false);
            var outcome = await context.ExecuteGpuAsync(_ => { var output = _gpu.Execute(input,token,out var ms); return (output,ms); }, token).ConfigureAwait(false);
            _batches++; _input += input.Count; _output += outcome.output.Count; _ms += outcome.ms;
            return outcome.output;
        }
        catch (OperationCanceledException) { throw; }
        catch { _faulted = true; throw; }
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpu is not null) await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"rswPrivateSummary=true;order={order};batches={_batches};input={_input};output={_output};canonicalMs={_ms};faulted={_faulted};recovery=None");
    }
}
