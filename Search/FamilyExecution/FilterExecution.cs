using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

internal interface IFilterExecution
{
    FilterBodyDescription Body { get; }
    bool ExecutionFaulted { get; }
    (string Device,double SetupMs) ResourceInfo => ("",0);
    ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token);
    ValueTask DisposeAsync(FilterRuntimeServices services);
}

// A capacity change retires the whole invocation before any replacement can bind.
internal static class FilterExecutionCleanup
{
    internal static async ValueTask DisposeChildrenAsync(FilterRuntimeServices services, string family,
        IEnumerable<IFilterExecution?> children, params IDisposable?[] deviceResources)
    {
        List<Exception>? failures = null;
        if (deviceResources.Any(resource => resource is not null))
        {
            try
            {
                await services.ExecuteGpuAsync(_ => { PrivateOrdinalBuffer.DisposeAll(deviceResources); return true; },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        foreach (var child in children)
        {
            if (child is null) continue;
            try { await child.DisposeAsync(services).ConfigureAwait(false); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        if (failures is not null) throw new AggregateException(family + ".CleanupFailed", failures);
    }
}

internal readonly record struct FilterBatchResult(FamilyCandidateSet Output, double CanonicalMs,
    double DispatchMs = -1, double ReadbackMs = -1, long ReadbackBytes = -1);

// A batch-level resource lifecycle around an existing typed numerical executor.
// This layer never interprets a query or changes a per-seed numerical algorithm.
internal sealed class GpuFilterExecution<TResource> : IFilterExecution where TResource : class
{
    public FilterBodyDescription Body { get; }
    public bool ExecutionFaulted { get; private set; }
    public (string Device,double SetupMs) ResourceInfo => _resource is {} resource ? _details(resource) : _lastResource;
    private (string Device,double SetupMs) _lastResource;
    private readonly Func<RenderingDevice,TResource> _create;
    private readonly Func<TResource,FamilyCandidateSet,CancellationToken,FilterBatchResult> _run;
    private readonly Action<TResource> _dispose;
    private readonly Func<ulong,bool> _reference;
    private readonly IRuntimeProfile _profile;
    private readonly Func<TResource,(string Device,double SetupMs)> _details;
    private readonly int _paritySample;
    private readonly string _faultCleanupStage;
    private TResource? _resource;
    private bool _parity;
    private int _resourceCapacity;
    internal GpuFilterExecution(FilterBodyDescription body, Func<RenderingDevice,TResource> create,
        Func<TResource,FamilyCandidateSet,CancellationToken,FilterBatchResult> run, Action<TResource> dispose,
        Func<ulong,bool> reference, IRuntimeProfile profile, Func<TResource,(string Device,double SetupMs)> details,
        int paritySample = 2048, string faultCleanupStage = "DisableGpu.Dispose")
    { Body=body;_create=create;_run=run;_dispose=dispose;_reference=reference;_profile=profile;_details=details;_paritySample=paritySample;_faultCleanupStage=faultCleanupStage; }
    public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(ExecutionFaulted) throw new InvalidOperationException("FilterExecutionFaulted:"+Body.FamilyId);
        if(input.Count==0)return input;
        try
        {
            int capacity = GpuRootWindow.PublicCapacity(Body, input.Count);
            if (_resource is null || capacity > _resourceCapacity)
            {
                _resource = await services.ExecuteGpuAsync(rd => {
                    if (_resource is { } old) { _resource = null; _dispose(old); }
                    return GpuRootWindow.BindPublic(Body.FamilyId, capacity, () => _create(rd));
                },token).ConfigureAwait(false);
                _resourceCapacity = capacity;
                _parity = false;
            }
            _lastResource=_details(_resource);
            if(!_parity)
            {
                var sample=FamilyCandidateSet.FromSortedAbi1(input.Batch,input.EnumerateLogicalOrdinals().Take(_paritySample).ToArray());
                var actual=await services.ExecuteGpuAsync(_=>_run(_resource,sample,token),token).ConfigureAwait(false);
                var expected=sample.EnumerateLogicalOrdinals().Where(i=>_reference(_profile.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(_profile,input.Batch.GlobalCandidate(i)))));
                if(!actual.Output.EnumerateLogicalOrdinals().SequenceEqual(expected))
                    throw new InvalidDataException(Body.FamilyId+".CanonicalReferenceParityMismatch");
                _parity=true;
            }
            var result=await services.ExecuteGpuAsync(_=>_run(_resource,input,token),token).ConfigureAwait(false);
            services.Costs.ObserveCompleted(Body,input,result.Output.Count,result.CanonicalMs,result.DispatchMs,
                result.ReadbackMs,result.ReadbackBytes,passedCoverage:services.CoverageFor(Body));
            return result.Output;
        }
        catch(GpuRootWindowBindingRefused){throw;}
        catch(OperationCanceledException){throw;}
        catch
        {
            ExecutionFaulted=true;
            services.Costs.Invalidate(Body);
            // Drop ownership before attempting release: neither later cleanup
            // nor a rejected call may retry a faulted numerical resource.
            var resource=_resource;
            _resource=null;
            if(resource is not null)
            {
                try
                {
                    await services.ExecuteGpuAsync(_=>{_dispose(resource);return true;},CancellationToken.None).ConfigureAwait(false);
                }
                catch(Exception cleanupFailure)
                {
                    services.RecordGpuCleanupFailure(cleanupFailure,Body.FamilyId,_faultCleanupStage,services.CorrelationId);
                }
            }
            throw;
        }
    }
    public async ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        if(_resource is not {} resource)return;
        _resource=null;
        try{await services.ExecuteGpuAsync(_=>{_dispose(resource);return true;},CancellationToken.None).ConfigureAwait(false);}
        catch{ExecutionFaulted=true;services.Costs.Invalidate(Body);throw;}
    }
}
