using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// K 個獨立 Relic 壓縮窗口共用一次提交；逐批記帳、驗證與排序維持原規則。
internal sealed partial class RelicFamily : IFamilyBatchInvocation
{
    private bool _batchFallbackLogged;

    int IFamilyBatchInvocation.BatchGroupCapacity =>
        FamilyBatchGrouping.RelicEnabled ? FamilyBatchGrouping.GroupSize : 1;

    async ValueTask<IReadOnlyList<FamilyCandidateSet>> IFamilyBatchInvocation.InvokeBatchAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(inputs);
        int count = windows.Count;
        if (count != inputs.Count) throw new InvalidOperationException("R.BatchMergeWindowCountMismatch");
        if (count <= 1) throw new InvalidOperationException("R.BatchMergeGroupSize");
        if (count > FamilyBatchGrouping.GroupSize) throw new InvalidOperationException("R.BatchMergeGroupTooLarge");
        for (int index = 0; index < count; index++)
        {
            if (windows[index].ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
                throw new InvalidOperationException("RFamilyObservationContextMismatch");
            if (inputs[index].Batch != windows[index].Batch)
                throw new InvalidOperationException("R.BatchMergeBatchIdentityMismatch");
        }

        bool merged = FamilyBatchGrouping.RelicEnabled && !_gpuDisabled;
        if (merged)
        {
            for (int index = 0; index < count; index++)
            {
                if (!RelicFamilyGpuExecutor.CanBatchInput(inputs[index])) { merged = false; break; }
            }
        }
        if (!merged)
        {
            LogBatchFallback("compact-shape-unavailable");
            return await InvokeSequentialAsync(executionContext, windows, inputs, cancellationToken)
                .ConfigureAwait(false);
        }

        ObserveInputMode(compactAbi1Input: true);
        try
        {
            await EnsureGpuAsync(executionContext, cancellationToken).ConfigureAwait(false);
            if (_gpu is not { SupportsBatchExecution: true })
            {
                LogBatchFallback("executor-fast-path-unavailable");
                return await InvokeSequentialAsync(executionContext, windows, inputs, cancellationToken)
                    .ConfigureAwait(false);
            }
            var wall = Stopwatch.StartNew();
            (FamilyCandidateSet[] Results, RelicFamilyGpuBatchMetrics[] Metrics, long OwnerStartTimestamp) batch;
            try
            {
                batch = await executionContext.ExecuteGpuAsync(
                    _ =>
                    {
                        long targetStartTimestamp = Stopwatch.GetTimestamp();
                        var result = _gpu.ExecuteBatch(inputs, cancellationToken);
                        return (result.Results, result.Metrics, targetStartTimestamp);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (FamilyBatchNotSupportedException)
            {
                wall.Stop();
                LogBatchFallback("group-resources-unavailable");
                return await InvokeSequentialAsync(executionContext, windows, inputs, cancellationToken)
                    .ConfigureAwait(false);
            }
            wall.Stop();
            _invocations += count;
            _compactInputs += count;
            double wallPerBatch = wall.Elapsed.TotalMilliseconds / count;
            for (int index = 0; index < count; index++)
                RecordCompletedBatch(inputs[index], batch.Results[index], batch.Metrics[index],
                    wallPerBatch, batch.OwnerStartTimestamp, executionContext);
            return batch.Results;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await DisableGpuAsync(executionContext, ex.GetType().Name + ":" + ex.Message).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<IReadOnlyList<FamilyCandidateSet>> InvokeSequentialAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken)
    {
        var results = new FamilyCandidateSet[windows.Count];
        for (int index = 0; index < windows.Count; index++)
            results[index] = await InvokeAsync(executionContext, windows[index], inputs[index], cancellationToken)
                .ConfigureAwait(false);
        return results;
    }

    private void LogBatchFallback(string reason)
    {
        if (_batchFallbackLogged) return;
        _batchFallbackLogged = true;
        RuntimeLog.TryBackgroundWarning(
            $"rFamilyBatchMergeFallback=true;family={FamilyId};reason={reason};groupSize={FamilyBatchGrouping.GroupSize}");
    }
}
