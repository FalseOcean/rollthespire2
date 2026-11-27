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
    private readonly bool UsesFused, UsesPrivate, _skipExactValidation, _widePublic;
    private FamilyPrivateGpuChain? _chain;
    private NeowFamilyGpuExecutor? _fused;
    private int _fusedCapacity = PrivateOrdinalBuffer.Capacity;
    private bool _fusedCompactStorage;
    private bool _compact, _privateParity;
    private long _inputs, _outputs, _liveInputs, _fusedReadback;
    private double _ms, _liveMs, _fusedDispatch;
    private int _batches;
    public bool ExecutionFaulted => _groupingDisabled || _executions.Any(stage => stage.ExecutionFaulted);
    internal NeowPartyFilterExecution(FilterBodyDescription body, NeowPartyStage[] stages,
        bool fused, bool privateEdges, bool skipExact, FilterRuntimeServices services)
    {
        Body = body; _gpuStages = stages; UsesFused = fused; UsesPrivate = privateEdges; _skipExactValidation = skipExact;
        // Integer N has one public shader per stage. Keep the shared-arrival
        // program and the complete stage order even when the first dispatch grows.
        _widePublic = body.WindowCapacity > PrivateOrdinalBuffer.Capacity &&
            stages.Any(stage => stage.Gpu?.NumericalVariant == NeowGpuNumericalVariant.IntegerUnitFloat);
        var passed = services.CoverageFor(body);
        _executions = stages.Select((stage, index) =>
        {
            var family = _widePublic && index == 0
                ? GpuRootWindow.PrepareStandalone(stage.Family, body.WindowCapacity) : stage.Family;
            services.BindCoverage(family, passed);
            return (family.CreateExecution ??
                throw new InvalidOperationException("PartyN.ChildExecutionUnavailable"))(family, services);
        }).ToArray();
    }
    private (string Device, double SetupMs) CaptureResourceInfo()
    {
        if (UsesFused || _fused is not null)
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
            int capacity = GpuRootWindow.PublicCapacity(Body, input.Count);
            bool wide = capacity > PrivateOrdinalBuffer.Capacity;
            bool useFused = UsesFused || wide && !_widePublic || _fusedCapacity > PrivateOrdinalBuffer.Capacity && _fused is not null;
            if ((useFused || UsesPrivate) && input.Count > 0)
            {
                if (useFused)
                {
                    if (_fused is null || capacity > _fusedCapacity || _fusedCapacity > PrivateOrdinalBuffer.Capacity && _fusedCompactStorage == input.IsDense)
                    {
                        _fused = await services.ExecuteGpuAsync(device => {
                            if (_chain is not null) { _chain.Dispose(); _chain = null; }
                            if (_fused is not null) { _fused.Dispose(); _fused = null; }
                            return GpuRootWindow.BindPublic(FamilyId, capacity, () => CreateFused(device, publicCompact: !input.IsDense));
                        }, token).ConfigureAwait(false);
                        _fusedCapacity = capacity;
                        _fusedCompactStorage = !input.IsDense;
                        _privateParity = false;
                    }
                    result = await services.ExecuteGpuAsync(_ =>
                    {
                        var output = _fused.Execute(input, token, out var metrics);
                        _fusedReadback += metrics.ReadbackBytes; _fusedDispatch += metrics.DispatchSyncMs;
                        return output;
                    }, token).ConfigureAwait(false);
                }
                else
                {
                    _chain ??= await services.ExecuteGpuAsync(device => {
                        if (_fused is not null) { _fused.Dispose(); _fused = null; _privateParity = false; }
                        return new FamilyPrivateGpuChain(device, _gpuStages.Select(stage => stage.Family).ToArray());
                    }, token).ConfigureAwait(false);
                    result = await services.ExecuteGpuAsync(_ => _chain.Execute(input, token), token).ConfigureAwait(false);
                }
                ValidatePrivateParity(input, result);
            }
            else for (int index = 0; index < _executions.Length; index++)
            {
                if (result.Count == 0) break;
                var stage = _executions[index];
                result = _widePublic && index > 0 && !services.BudgetPublicTailBindings
                    ? await GpuRootWindow.FilterPublicTailAsync(stage, services, result, token,
                        supportsWideCompactOrdinals: true).ConfigureAwait(false)
                    : await stage.FilterAsync(services, result, token).ConfigureAwait(false);
            }
            double elapsed = watch.Elapsed.TotalMilliseconds;
            if (input.IsDense) Volatile.Write(ref _lastDenseOutputCount, result.Count);
            _inputs += input.Count; _outputs += result.Count; _ms += elapsed;
            if (_batches++ > 0) { Interlocked.Add(ref _liveInputs, input.Count); _liveMs += elapsed; _liveBatches++; }
            services.Costs.ObserveCompleted(Body, input, result.Count, elapsed, passedCoverage: services.CoverageFor(Body));
            return result;
        }
        catch (GpuRootWindowBindingRefused) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { _groupingDisabled = true; throw; }
    }
    public async ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        _lastResource = CaptureResourceInfo();
        _privatePairDisposed = true;
        await FilterExecutionCleanup.DisposeChildrenAsync(services, FamilyId, _executions,
            _pairFused, _fused, _privatePairChain, _chain).ConfigureAwait(false);
        RuntimeLog.TryBackgroundInfo($"partyNeowPipelineSummary=true;fused={UsesFused};private={UsesPrivate && !UsesFused};stages={_gpuStages.Length};batches={_batches};input={_inputs};output={_outputs};wallMs={_ms};setupMs={(_fused?.SetupMs ?? _chain?.SetupMs ?? 0) + (_pairFused?.SetupMs ?? 0) + (_privatePairChain?.SetupMs ?? 0)};intermediatePayloadReadbackBytes={(_chain is not null || _fused is not null ? 0 : -1)};readbackBytes={(_chain?.ReadbackBytes ?? _fusedReadback) + (_privatePairChain?.ReadbackBytes ?? 0)};dispatchSyncMs={(_chain?.DispatchMs ?? _fusedDispatch) + (_privatePairChain?.DispatchMs ?? 0)};parity={_privateParity};densePairGroups={_densePairGroups};densePairCalibrationSamples=Excluded;privateDensePairGroups={_privateDensePairGroups}");
    }
}
