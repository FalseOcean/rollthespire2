using System.Collections.Concurrent;
using Godot;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Session-scoped execution services shared by every Family. Search remains
/// externally asynchronous while the single local RenderingDevice is operated
/// synchronously by one bounded, dedicated owner thread.
/// </summary>
public sealed class FamilyExecutionContext : IAsyncDisposable
{
    private readonly FamilyGpuExecutionOwner? _gpuOwner;
    private readonly FamilyPipelineBoundaryAudit? _pipelineBoundaryAudit = FamilyPipelineBoundaryAudit.TryCreate();

    internal FamilyExecutionContext(bool requiresGpu, bool failOnPhysicalRecovery = false)
    {
        _gpuOwner = requiresGpu ? new FamilyGpuExecutionOwner() : null;
        FailOnPhysicalRecovery = failOnPhysicalRecovery;
    }

    internal bool FailOnPhysicalRecovery { get; }

    internal int GpuOwnerThreadId => _gpuOwner?.OwnerThreadId ?? 0;
    internal bool GpuOwnerStable => _gpuOwner?.IsStable ?? true;

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
        _pipelineBoundaryAudit?.RecordSource(batch, metrics, ownerCompletionTimestamp, sourceBatchNumber);

    internal void CompletePipelineBoundaryTarget(
        SearchBatch batch,
        RelicFamilyGpuBatchMetrics metrics,
        long targetOwnerStartTimestamp,
        bool targetDispatched) =>
        _pipelineBoundaryAudit?.CompleteTarget(batch, metrics, targetOwnerStartTimestamp, targetDispatched);

    public async ValueTask DisposeAsync()
    {
        _pipelineBoundaryAudit?.WriteSummary();
        if (_gpuOwner is not null)
            await _gpuOwner.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class FamilyGpuExecutionOwner : IAsyncDisposable
{
    private readonly long _requestedAt = System.Diagnostics.Stopwatch.GetTimestamp();
    [ThreadStatic] private static bool _firstSubmitObserved;
    internal static void ObserveFirstSubmit()
    {
        if (_firstSubmitObserved) return;
        _firstSubmitObserved=true;
        RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FirstSubmit;ownerThreadId={System.Environment.CurrentManagedThreadId};processId={System.Environment.ProcessId}");
    }
    private const int QueueCapacity = 1;
    private readonly BlockingCollection<IWorkItem> _queue =
        new(new ConcurrentQueue<IWorkItem>(), QueueCapacity);
    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<RenderingDevice?> _createDevice;
    private int _disposed;
    private int _faulted;
    private Exception? _ownerFault;

    public FamilyGpuExecutionOwner() : this(() => RenderingServer.CreateLocalRenderingDevice())
    {
    }

    internal FamilyGpuExecutionOwner(Func<RenderingDevice?> createDevice)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        _createDevice = createDevice;
        RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=GpuOwnerRequested;processId={System.Environment.ProcessId};lifetime=PerSearch");
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "RolltheSpire2.FamilyExecution.GpuOwner"
        };
        _thread.Start();
    }

    public int OwnerThreadId { get; private set; }
    public bool IsStable => Volatile.Read(ref _faulted) == 0;

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
        await _stopped.Task.ConfigureAwait(false);
        _queue.Dispose();
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
            device = _createDevice() ??
                     throw new InvalidOperationException("FamilyGpuRenderingDeviceUnavailable");
            RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=GpuOwnerReady;ownerThreadId={OwnerThreadId};requestedToReadyMs={System.Diagnostics.Stopwatch.GetElapsedTime(_requestedAt).TotalMilliseconds:F4};processId={System.Environment.ProcessId}");
            FamilyDeviceProfileFoundation.ObserveFamilyComputeAvailable(
                device.GetDeviceName()?.Trim() ?? string.Empty);
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
            if (device is not null)
            {
                // Work items finish their own Submit/Sync pairs. An extra Sync
                // without an outstanding Submit is a Godot API error.
                try { device.Free(); }
                catch { try { device.Dispose(); } catch { } }
            }
            RuntimeLog.TryBackgroundInfo(
                $"familyGpuOwnerStopped=true;ownerThreadId={OwnerThreadId};deviceReleased=true");
            _stopped.TrySetResult(true);
        }
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
