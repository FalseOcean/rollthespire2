using System.Diagnostics;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

// The selected N party pipeline owns mutable resources. Descriptions retain the
// ordered shared-arrival program and prices, never this runtime's state.
internal sealed partial class NeowPartyFilterExecution : IFilterExecution, IDenseFamilyBatchGrouping
{
    public FilterBodyDescription Body { get; }
    public (string Device, double SetupMs) ResourceInfo
    {
        get
        {
            var current = CaptureResourceInfo();
            return string.IsNullOrEmpty(current.Device) && current.SetupMs == 0 ? _lastResource : current;
        }
    }
    private (string Device, double SetupMs) _lastResource = ("", 0);
    private string FamilyId => Body.FamilyId;
    private readonly NeowPartyStage[] _gpuStages;
    private readonly IFilterExecution[] _executions;
    private readonly bool UsesFused, UsesPrivate, _skipExactValidation;
    private FamilyPrivateGpuChain? _chain;
    private NeowFamilyGpuExecutor? _fused;
    private bool _compact, _privateParity;
    private long _inputs, _outputs, _liveInputs, _fusedReadback;
    private double _ms, _liveMs, _fusedDispatch;
    private int _batches;
    public bool ExecutionFaulted => _groupingDisabled || _executions.Any(stage => stage.ExecutionFaulted);
    internal NeowPartyFilterExecution(FilterBodyDescription body, NeowPartyStage[] stages,
        bool fused, bool privateEdges, bool skipExact, FilterRuntimeServices services)
    {
        Body = body; _gpuStages = stages; UsesFused = fused; UsesPrivate = privateEdges; _skipExactValidation = skipExact;
        var passed = services.CoverageFor(body);
        _executions = stages.Select(stage =>
        {
            services.BindCoverage(stage.Family, passed);
            return (stage.Family.CreateExecution ??
                throw new InvalidOperationException("PartyN.ChildExecutionUnavailable"))(stage.Family, services);
        }).ToArray();
    }
    private (string Device, double SetupMs) CaptureResourceInfo()
    {
        if (UsesFused)
            return (_fused?.Device ?? _pairFused?.Device ?? "", (_fused?.SetupMs ?? 0) + (_pairFused?.SetupMs ?? 0));
        if (UsesPrivate)
            return (_chain?.Device ?? _privatePairChain?.Device ?? "", (_chain?.SetupMs ?? 0) + (_privatePairChain?.SetupMs ?? 0));
        var children = _executions.Select(stage => stage.ResourceInfo).ToArray();
        return (children.FirstOrDefault(info => !string.IsNullOrEmpty(info.Device)).Device ?? "",
            children.Sum(info => info.SetupMs));
    }
    public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ExecutionFaulted) throw new InvalidOperationException("PartyN.SelectedInvocationFaulted");
        _compact = !input.IsDense;
        var watch = Stopwatch.StartNew(); var result = input;
        try
        {
            if ((UsesFused || UsesPrivate) && input.Count > 0)
            {
                if (UsesFused)
                {
                    _fused ??= await services.ExecuteGpuAsync(device => CreateFused(device), token).ConfigureAwait(false);
                    result = await services.ExecuteGpuAsync(_ =>
                    {
                        var output = _fused.Execute(input, token, out var metrics);
                        _fusedReadback += metrics.ReadbackBytes; _fusedDispatch += metrics.DispatchSyncMs;
                        return output;
                    }, token).ConfigureAwait(false);
                }
                else
                {
                    _chain ??= await services.ExecuteGpuAsync(device => new FamilyPrivateGpuChain(device,
                        _gpuStages.Select(stage => stage.Family).ToArray()), token).ConfigureAwait(false);
                    result = await services.ExecuteGpuAsync(_ => _chain.Execute(input, token), token).ConfigureAwait(false);
                }
                ValidatePrivateParity(input, result);
            }
            else foreach (var stage in _executions)
            {
                if (result.Count == 0) break;
                result = await stage.FilterAsync(services, result, token).ConfigureAwait(false);
            }
            double elapsed = watch.Elapsed.TotalMilliseconds;
            if (input.IsDense) Volatile.Write(ref _lastDenseOutputCount, result.Count);
            _inputs += input.Count; _outputs += result.Count; _ms += elapsed;
            if (_batches++ > 0) { Interlocked.Add(ref _liveInputs, input.Count); _liveMs += elapsed; _liveBatches++; }
            services.Costs.ObserveCompleted(Body, input, result.Count, elapsed, passedCoverage: services.CoverageFor(Body));
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch { _groupingDisabled = true; throw; }
    }
    public async ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        _lastResource = CaptureResourceInfo();
        _privatePairDisposed = true;
        if (_fused is not null || _pairFused is not null)
            await services.ExecuteGpuAsync(_ => { DisposePair(_pairFused, _fused); return true; }, CancellationToken.None).ConfigureAwait(false);
        if (_chain is not null || _privatePairChain is not null)
            await services.ExecuteGpuAsync(_ => { PrivateOrdinalBuffer.DisposeAll(_privatePairChain, _chain); return true; }, CancellationToken.None).ConfigureAwait(false);
        foreach (var stage in _executions) await stage.DisposeAsync(services).ConfigureAwait(false);
        RuntimeLog.TryBackgroundInfo($"partyNeowPipelineSummary=true;fused={UsesFused};private={UsesPrivate && !UsesFused};stages={_gpuStages.Length};batches={_batches};input={_inputs};output={_outputs};wallMs={_ms};setupMs={(_fused?.SetupMs ?? _chain?.SetupMs ?? 0) + (_pairFused?.SetupMs ?? 0) + (_privatePairChain?.SetupMs ?? 0)};intermediatePayloadReadbackBytes={(_chain is not null || _fused is not null ? 0 : -1)};readbackBytes={(_chain?.ReadbackBytes ?? _fusedReadback) + (_privatePairChain?.ReadbackBytes ?? 0)};dispatchSyncMs={(_chain?.DispatchMs ?? _fusedDispatch) + (_privatePairChain?.DispatchMs ?? 0)};parity={_privateParity};densePairGroups={_densePairGroups};densePairCalibrationSamples=Excluded;privateDensePairGroups={_privateDensePairGroups}");
    }
}
