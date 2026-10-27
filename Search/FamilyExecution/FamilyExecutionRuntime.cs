using System.Collections.Concurrent;
using Godot;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record FamilyGpuCreationFailure(Exception Error, long Attempt, string EnvironmentKey);

/// <summary>
/// Session-scoped execution services shared by every Family. Search remains
/// externally asynchronous while the single local RenderingDevice is operated
/// synchronously by one bounded, dedicated owner thread.
/// </summary>
public sealed class FamilyExecutionContext : IAsyncDisposable
{
    private readonly FamilyGpuExecutionOwner? _gpuOwner;
    private readonly SemaphoreSlim? _cpuWorkers;
    private int _activeCpuWorkers, _peakCpuWorkers;
    private readonly FamilyPipelineBoundaryAudit? _pipelineBoundaryAudit = FamilyPipelineBoundaryAudit.TryCreate();
    private readonly ConcurrentDictionary<IFamilyInvocation, Exception> _isolatedCpuFamilies = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<(SearchBatch, string), FamilyFilterIsolation> _filterIsolations = new();
    private readonly ConcurrentQueue<Exception> _gpuCleanupFailures = new();

    internal void RecordGpuCleanupFailure(Exception exception, string family, string stage, string correlation)
    {
        _gpuCleanupFailures.Enqueue(new InvalidOperationException(
            $"FamilyGpuCleanupFailed:family={family};stage={stage};context={correlation}", exception));
        FamilyAuxiliaryEvidence.Read(family, stage, () =>
        {
            Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "FamilyGpuCleanupFailed",
                family, stage, "issues.search_failed", correlationId: correlation);
            return true;
        }, false, correlation);
    }

    internal IReadOnlyList<FamilyFilterIsolation> CaptureFilterIsolations(SearchBatch? batch = null) =>
        _filterIsolations.Values.Where(item => batch is null || item.Batch == batch.Value)
            .OrderBy(item => item.Batch.BatchBase).ThenBy(item => item.FamilyId, StringComparer.Ordinal).ToArray();

    // Release only after the consumer has copied this batch's evidence. The failed
    // invocation remains isolated for the session and will not be retried.
    internal void ReleaseFilterIsolations(SearchBatch batch)
    {
        foreach (var key in _filterIsolations.Keys.Where(key => key.Item1 == batch))
            _filterIsolations.TryRemove(key, out _);
    }

    internal bool TryGetCpuFilterIsolation(IFamilyInvocation family, out Exception error) =>
        _isolatedCpuFamilies.TryGetValue(family, out error!);

    internal void RecordCpuFilterIsolation(IFamilyInvocation family, FamilyObservationWindow window,
        FamilyCandidateSet restoredInput, IReadOnlyList<string> unfilteredCoverage, Exception error)
    {
        error = _isolatedCpuFamilies.GetOrAdd(family, error);
        var failures = FamilyCpuMatchException.Matches(error);
        var record = new FamilyFilterIsolation(family.FamilyId, "CpuMatcher", window.Batch, failures[0].Batch,
            failures.Select(item => item.GlobalOrdinal).Distinct().ToArray(), unfilteredCoverage,
            restoredInput.Count, window.ExactRequest.RunOptions.SkipExactValidation, error);
        if (!_filterIsolations.TryAdd((window.Batch, family.FamilyId), record)) return;
        FamilyAuxiliaryEvidence.Read(family.FamilyId, "RecordFilterIsolation", () =>
        {
            Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(error, "FamilyFilterIsolated", family.FamilyId,
                "CpuMatcher", "issues.filter_incomplete", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning,
                window.ExactRequest.SnapshotFingerprint);
            RuntimeLog.TryBackgroundWarning("familyFilterIsolated=true;family=" + family.FamilyId +
                ";stage=CpuMatcher;batchBase=" + window.Batch.BatchBase + ";restoredInputs=" + restoredInput.Count +
                ";remainingCoverage=" + string.Join(',', unfilteredCoverage) +
                ";finalValidationSkipped=" + record.FinalValidationSkipped + ";retry=false;exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
            return true;
        }, false, window.ExactRequest.SnapshotFingerprint);
    }

    internal FamilyExecutionContext(bool requiresGpu, bool failOnPhysicalRecovery = false, int? cpuWorkerCount = null,
        Func<FamilyGpuExecutionOwner>? gpuOwnerFactory = null)
    {
        if (cpuWorkerCount is <= 0) throw new ArgumentOutOfRangeException(nameof(cpuWorkerCount));
        _cpuWorkers = cpuWorkerCount is int count ? new SemaphoreSlim(count, count) : null;
        try { _gpuOwner = requiresGpu ? (gpuOwnerFactory is null ? new FamilyGpuExecutionOwner() : gpuOwnerFactory()) : null; }
        catch { _cpuWorkers?.Dispose(); throw; }
        FailOnPhysicalRecovery = failOnPhysicalRecovery;
    }

    internal bool FailOnPhysicalRecovery { get; }
    // Assigned by the owning session before workers start; nested whole-table
    // filters share the original-query Exact handoff policy.
    internal SmallCandidateExactPolicy? SmallCandidateExact { get; set; }
    internal int PeakCpuWorkers => Volatile.Read(ref _peakCpuWorkers);
    internal void EnterCpu(CancellationToken token)
    {
        _cpuWorkers?.Wait(token);
        ObserveCpuEntry();
    }
    internal async ValueTask EnterCpuAsync(CancellationToken token)
    {
        if (_cpuWorkers is not null) await _cpuWorkers.WaitAsync(token).ConfigureAwait(false);
        ObserveCpuEntry();
    }
    private void ObserveCpuEntry()
    {
        int active = Interlocked.Increment(ref _activeCpuWorkers), peak;
        while (active > (peak = Volatile.Read(ref _peakCpuWorkers)) &&
            Interlocked.CompareExchange(ref _peakCpuWorkers, active, peak) != peak) { }
    }
    internal void ExitCpu()
    {
        Interlocked.Decrement(ref _activeCpuWorkers);
        _cpuWorkers?.Release();
    }

    internal int GpuOwnerThreadId => _gpuOwner?.OwnerThreadId ?? 0;
    internal bool GpuOwnerStable => _gpuOwner?.IsStable ?? true;
    internal bool TryGetInitialGpuCreationFailure(Exception propagated, out FamilyGpuCreationFailure? failure)
    {
        failure = null;
        return _gpuOwner?.TryGetCreationFailure(propagated, out failure) == true;
    }

    // Joining only the failed native owner preserves the session's CPU permits
    // and any Exact work already using them. No new device is created here.
    internal Task JoinFailedGpuOwnerAsync(CancellationToken token) => _gpuOwner is null
        ? Task.CompletedTask : _gpuOwner.DisposeAsync().AsTask().WaitAsync(token);

    internal ValueTask<T> ExecuteGpuAsync<T>(
        Func<RenderingDevice, T> action,
        CancellationToken cancellationToken) =>
        _gpuOwner is not null
            ? _gpuOwner.ExecuteAsync(action, cancellationToken)
            : ValueTask.FromException<T>(new InvalidOperationException("FamilyGpuOwnerUnavailable"));

    internal void RecordPipelineBoundarySource(
        SearchBatch batch,
        MerchantShopColorlessGpuBatchMetrics metrics,
        long ownerCompletionTimestamp,
        int sourceBatchNumber) =>
        FamilyAuxiliaryEvidence.Read(nameof(FamilyPipelineBoundaryAudit), "RecordSource", () =>
        { _pipelineBoundaryAudit?.RecordSource(batch, metrics, ownerCompletionTimestamp, sourceBatchNumber); return true; }, false);

    internal void CompletePipelineBoundaryTarget(
        SearchBatch batch,
        RelicFamilyGpuBatchMetrics metrics,
        long targetOwnerStartTimestamp,
        bool targetDispatched) =>
        FamilyAuxiliaryEvidence.Read(nameof(FamilyPipelineBoundaryAudit), "CompleteTarget", () =>
        { _pipelineBoundaryAudit?.CompleteTarget(batch, metrics, targetOwnerStartTimestamp, targetDispatched); return true; }, false);

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        while (_gpuCleanupFailures.TryDequeue(out var failure)) failures.Add(failure);
        try
        {
            FamilyAuxiliaryEvidence.Read(nameof(FamilyPipelineBoundaryAudit), "WriteSummary", () =>
            { _pipelineBoundaryAudit?.WriteSummary(); return true; }, false);
            if (_gpuOwner is not null)
                await _gpuOwner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally { _cpuWorkers?.Dispose(); }
        if (failures.Count > 0) throw new AggregateException("FamilyExecutionContext.CleanupFailed", failures);
    }
}

internal sealed class FamilyGpuExecutionOwner : IAsyncDisposable
{
    private readonly long _requestedAt = System.Diagnostics.Stopwatch.GetTimestamp();
    [ThreadStatic] private static bool _firstSubmitObserved;
    internal static bool ObserveFirstSubmit(string family = "Unknown")
    {
        if (_firstSubmitObserved) return false;
        _firstSubmitObserved=true;
        RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FirstSubmit;family={Sanitize(family)};ownerThreadId={System.Environment.CurrentManagedThreadId};processId={System.Environment.ProcessId}");
        return true;
    }
    private const int QueueCapacity = 1;
    private readonly BlockingCollection<IWorkItem> _queue =
        new(new ConcurrentQueue<IWorkItem>(), QueueCapacity);
    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<RenderingDevice?> _createDevice;
    private readonly FamilyGpuInitialization? _initialization;
    private readonly long _initializationAttempt;
    private readonly string _initializationEnvironment;
    private FamilyGpuCreationFailure? _creationFailure;
    private int _disposed;
    private int _faulted;
    private Exception? _ownerFault;

    public FamilyGpuExecutionOwner() : this(() => RenderingServer.CreateLocalRenderingDevice(),
        FamilyDeviceProfileFoundation.Initialization)
    {
    }

    internal FamilyGpuExecutionOwner(Func<RenderingDevice?> createDevice,
        FamilyGpuInitialization? initialization = null, bool explicitRetry = false)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        _createDevice = createDevice;
        _initialization = initialization;
        var previousState = initialization?.Capture().State ?? FamilyGpuInitializationState.Unverified;
        try { _initializationAttempt = initialization?.BeginCreation(explicitRetry) ?? 0; }
        catch { _queue.Dispose(); throw; }
        _initializationEnvironment = initialization?.Capture().EnvironmentKey ?? "UntrackedOwner";
        RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=GpuOwnerRequested;processId={System.Environment.ProcessId};lifetime={(explicitRetry ? "InitializationProbe" : "PerSearch")}");
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "RolltheSpire2.FamilyExecution.GpuOwner"
        };
        try { _thread.Start(); }
        catch
        {
            // Thread startup is not a Local RenderingDevice creation failure.
            _initialization?.CreationAborted(_initializationAttempt, previousState);
            _initialization?.OwnerStopped();
            _queue.Dispose();
            throw;
        }
    }

    public int OwnerThreadId { get; private set; }
    public bool IsStable => Volatile.Read(ref _faulted) == 0;

    internal bool TryGetCreationFailure(Exception propagated, out FamilyGpuCreationFailure? failure)
    {
        failure = Volatile.Read(ref _creationFailure);
        return failure is not null && ContainsOriginal(propagated, failure.Error);
    }

    private static bool ContainsOriginal(Exception propagated, Exception original) =>
        ReferenceEquals(propagated, original) ||
        (propagated is AggregateException aggregate
            ? aggregate.InnerExceptions.Any(inner => ContainsOriginal(inner, original))
            : propagated.InnerException is { } inner && ContainsOriginal(inner, original));

    public ValueTask<T> ExecuteAsync<T>(Func<RenderingDevice, T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? ownerFault = Volatile.Read(ref _ownerFault);
        if (ownerFault is not null)
            return ValueTask.FromException<T>(ownerFault);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var item = new WorkItem<T>(action, cancellationToken);
        try
        {
            _queue.Add(item, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            CancellationToken token = exception.CancellationToken.CanBeCanceled
                ? exception.CancellationToken
                : cancellationToken;
            item.Cancel(token);
        }
        catch (InvalidOperationException ex)
        {
            item.Fail(Volatile.Read(ref _ownerFault) ??
                new ObjectDisposedException(nameof(FamilyGpuExecutionOwner), ex));
        }
        return new ValueTask<T>(item.Task);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            await _stopped.Task.ConfigureAwait(false);
            return;
        }

        _queue.CompleteAdding();
        try { await _stopped.Task.ConfigureAwait(false); }
        finally { _queue.Dispose(); }
    }

    private void ThreadMain()
    {
        RenderingDevice? device = null;
        OwnerThreadId = Thread.CurrentThread.ManagedThreadId;
        RuntimeLog.TryBackgroundInfo(
            $"familyGpuOwnerStarted=true;ownerThreadId={OwnerThreadId};queueCapacity={QueueCapacity};" +
            "renderingDeviceExecution=Synchronous");
        try
        {
            try
            {
                device = _createDevice() ??
                         throw new InvalidOperationException("FamilyGpuRenderingDeviceUnavailable: CreateLocalRenderingDevice returned null; see Godot log for native driver details.");
            }
            catch (Exception creationFailure)
            {
                Volatile.Write(ref _creationFailure,
                    new FamilyGpuCreationFailure(creationFailure, _initializationAttempt, _initializationEnvironment));
                _initialization?.CreationFailed(_initializationAttempt, creationFailure);
                throw;
            }
            _initialization?.CreationSucceeded(_initializationAttempt);
            RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=GpuOwnerReady;ownerThreadId={OwnerThreadId};requestedToReadyMs={System.Diagnostics.Stopwatch.GetElapsedTime(_requestedAt).TotalMilliseconds:F4};processId={System.Environment.ProcessId}");
            try
            {
                LogDeviceFacts(device);
                FamilyDeviceProfileFoundation.ObserveFamilyComputeAvailable(device.GetDeviceName()?.Trim() ?? string.Empty);
            }
            catch (Exception diagnosticsFailure)
            { RuntimeLog.TryBackgroundWarning("familyGpuDeviceDiagnosticsFailed=true;failure=" + diagnosticsFailure); }
            foreach (IWorkItem item in _queue.GetConsumingEnumerable())
                item.Execute(device);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _ownerFault, ex);
            Interlocked.Exchange(ref _faulted, 1);
            try { _queue.CompleteAdding(); }
            catch { }
            while (_queue.TryTake(out IWorkItem? pending))
                pending.Fail(ex);
            RuntimeLog.TryBackgroundWarning(
                $"familyGpuOwnerFault=true;ownerThreadId={OwnerThreadId};" +
                $"failure={ex.GetType().Name}:{Sanitize(ex.Message)}");
        }
        finally
        {
            Exception? releaseFailure = null;
            if (device is not null)
            {
                // Work items finish their own Submit/Sync pairs. An extra Sync
                // without an outstanding Submit is a Godot API error.
                try { device.Free(); }
                catch (Exception freeFailure)
                {
                    releaseFailure = freeFailure;
                    try { device.Dispose(); }
                    catch (Exception disposeFailure)
                    {
                        releaseFailure = new AggregateException("FamilyGpuOwner.DeviceReleaseFailed", freeFailure, disposeFailure);
                    }
                    Interlocked.Exchange(ref _faulted, 1);
                    RuntimeLog.TryBackgroundWarning($"familyGpuDeviceReleaseFailed=true;ownerThreadId={OwnerThreadId};failure={Sanitize(releaseFailure.ToString())}");
                }
            }
            RuntimeLog.TryBackgroundInfo(
                $"familyGpuOwnerStopped=true;ownerThreadId={OwnerThreadId};deviceCreated={device is not null};deviceReleased={device is not null && releaseFailure is null}");
            _initialization?.OwnerStopped();
            if (releaseFailure is null) _stopped.TrySetResult(true);
            else _stopped.TrySetException(releaseFailure);
        }
    }

    private void LogDeviceFacts(RenderingDevice device)
    {
        // Report the engine's actual limits. These are diagnostic facts, not a
        // new admission policy or an automatic change to batch geometry.
        using var version = Engine.GetVersionInfo();
        RenderingDevice.Limit[] limits =
        [
            RenderingDevice.Limit.MaxStorageBuffersPerUniformSet,
            RenderingDevice.Limit.MaxStorageBuffersPerShaderStage,
            RenderingDevice.Limit.MaxComputeSharedMemorySize,
            RenderingDevice.Limit.MaxComputeWorkgroupInvocations,
            RenderingDevice.Limit.MaxComputeWorkgroupSizeX,
            RenderingDevice.Limit.MaxComputeWorkgroupSizeY,
            RenderingDevice.Limit.MaxComputeWorkgroupSizeZ,
            RenderingDevice.Limit.MaxComputeWorkgroupCountX,
            RenderingDevice.Limit.MaxComputeWorkgroupCountY,
            RenderingDevice.Limit.MaxComputeWorkgroupCountZ
        ];
        string facts = string.Join(';', limits.Select(limit => $"{limit}={device.LimitGet(limit)}"));
        RuntimeLog.TryBackgroundInfo($"familyGpuDeviceFacts=true;ownerThreadId={OwnerThreadId};godot={Sanitize(version["string"].AsString())};driver={Sanitize(RenderingServer.GetCurrentRenderingDriverName())};vendor={Sanitize(device.GetDeviceVendorName())};device={Sanitize(device.GetDeviceName())};pipelineCacheUuid={Sanitize(device.GetDevicePipelineCacheUuid())};{facts}");
    }

    private interface IWorkItem
    {
        void Execute(RenderingDevice device);
        void Fail(Exception exception);
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<RenderingDevice, T> _action;
        private readonly string _logSession = RuntimeLog.SessionId;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkItem(Func<RenderingDevice, T> action, CancellationToken cancellationToken)
        {
            _action = action;
            _cancellationToken = cancellationToken;
        }

        public Task<T> Task => _completion.Task;

        public void Cancel(CancellationToken cancellationToken) =>
            _completion.TrySetCanceled(cancellationToken);

        public void Execute(RenderingDevice device)
        {
            using var logScope = RuntimeLog.SearchScope(_logSession);
            if (_cancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(_cancellationToken);
                return;
            }

            try
            {
                _completion.TrySetResult(_action(device));
            }
            catch (OperationCanceledException exception)
            {
                CancellationToken token = exception.CancellationToken.CanBeCanceled
                    ? exception.CancellationToken
                    : _cancellationToken;
                _completion.TrySetCanceled(token);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private static string Sanitize(string value) => string.IsNullOrWhiteSpace(value)
        ? "none"
        : value.Replace(';', '_').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
