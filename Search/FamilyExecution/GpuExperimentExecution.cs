using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Existing whole-kernel diagnostic/default units own their internal reference
// checks. This handle adds resource lifetime and independent completed-work
// observation without manufacturing a second semantic reference predicate.
internal sealed class GpuExperimentExecution<TResource>(FilterBodyDescription body,
    Func<RenderingDevice, TResource> create,
    Func<TResource, FamilyCandidateSet, CancellationToken, FilterBatchResult> run,
    Action<TResource> dispose, Func<TResource, (string Device, double SetupMs)> details,
    bool verification = false) : IFilterExecution where TResource : class
{
    public FilterBodyDescription Body => body;
    public bool ExecutionFaulted { get; private set; }
    public (string Device, double SetupMs) ResourceInfo => _resource is { } resource ? details(resource) : _lastResource;
    private TResource? _resource;
    private (string Device, double SetupMs) _lastResource = ("", 0);
    private bool _disposed;
    public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (ExecutionFaulted) throw new InvalidOperationException("FilterExperimentFaulted:" + body.FamilyId);
        try {
            _resource ??= await services.ExecuteGpuAsync(create, token).ConfigureAwait(false);
            var result = await services.ExecuteGpuAsync(_ => run(_resource, input, token), token).ConfigureAwait(false);
            services.Costs.ObserveCompleted(body, input, result.Output.Count, result.CanonicalMs, result.DispatchMs,
                result.ReadbackMs, result.ReadbackBytes, verification);
            return result.Output;
        }
        catch (OperationCanceledException) { throw; }
        catch { ExecutionFaulted = true; throw; }
    }
    public async ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        if (_disposed) return;
        _disposed = true;
        if (_resource is not { } resource) return;
        _lastResource = details(resource);
        _resource = null;
        try { await services.ExecuteGpuAsync(_ => { dispose(resource); return true; }, CancellationToken.None).ConfigureAwait(false); }
        catch { ExecutionFaulted = true; throw; }
    }
}
