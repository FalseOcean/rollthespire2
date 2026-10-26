using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// K 個獨立 Neow 稠密窗口共用一次提交；逐批排序、摘要、驗證與記帳維持原規則。
internal sealed partial class NeowFamily : IFamilyBatchInvocation
{
    private bool _batchFallbackLogged;

    int IFamilyBatchInvocation.BatchGroupCapacity =>
        FamilyBatchGrouping.Enabled && _gpuPlan is not null && _composite is null && _transformations is null
            ? FamilyBatchGrouping.GroupSize
            : 1;

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
        if (count != inputs.Count) throw new InvalidOperationException("N.BatchMergeWindowCountMismatch");
        if (count <= 1) throw new InvalidOperationException("N.BatchMergeGroupSize");
        if (count > FamilyBatchGrouping.GroupSize) throw new InvalidOperationException("N.BatchMergeGroupTooLarge");
        for (int index = 0; index < count; index++)
        {
            if (windows[index].ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
                throw new InvalidOperationException("NFamilyObservationContextMismatch");
            FamilyCandidateSet input = inputs[index];
            if (input.Batch != windows[index].Batch)
                throw new InvalidOperationException("N.BatchMergeBatchIdentityMismatch");
            // 只有稠密輸入涵蓋完整窗口；壓縮候選必須走逐窗口路徑。
            if (input.IsDense && input.Count != windows[index].Batch.BatchCandidateCount)
                throw new InvalidDataException("N.BatchMergeInputCountMismatch");
        }

        // 不支援的輸入維持逐窗口執行。
        bool merged = _gpuPlan is not null && _composite is null && _transformations is null;
        if (merged)
        {
            for (int index = 0; index < count; index++)
            {
                if (!inputs[index].IsDense) { merged = false; break; }
            }
        }
        if (!merged)
        {
            LogBatchFallback("compact-input-or-family-variant");
            return await InvokeSequentialAsync(executionContext, windows, inputs, cancellationToken)
                .ConfigureAwait(false);
        }
        if (_disabled) throw new InvalidOperationException("N.SelectedInvocationFaulted");

        _compact = false;
        try
        {
            await EnsureGpuAsync(executionContext, cancellationToken).ConfigureAwait(false);
            if (_gpu is not { SupportsBatchExecution: true })
            {
                LogBatchFallback("executor-fast-path-unavailable");
                return await InvokeSequentialAsync(executionContext, windows, inputs, cancellationToken)
                    .ConfigureAwait(false);
            }
            (FamilyCandidateSet[] Results, NeowFamilyGpuMetrics[] Metrics) batch;
            try
            {
                batch = await executionContext.ExecutePrimaryGpuAsync(
                    _ => _gpu!.ExecuteBatch(inputs, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (FamilyBatchNotSupportedException)
            {
                // 資源準入在提交前拒絕合併，可退回逐窗口；實際執行故障仍須拋出。
                LogBatchFallback("group-resources-unavailable");
                return await InvokeSequentialAsync(executionContext, windows, inputs, cancellationToken)
                    .ConfigureAwait(false);
            }
            for (int index = 0; index < count; index++)
                RecordCompletedBatch(inputs[index], batch.Results[index], batch.Metrics[index], executionContext);
            return batch.Results;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            MarkFamilyFault(ex);
            throw;
        }
    }

    private void MarkFamilyFault(Exception ex)
    {
        if (_disabled) return;
        _disabled = true;
        RuntimeLog.TryBackgroundWarning($"nFamilyPhysicalFault=true;reason={ex.Message};recovery=None");
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
            $"nFamilyBatchMergeFallback=true;family={FamilyId};reason={reason};groupSize={FamilyBatchGrouping.GroupSize}");
    }

    // 流水線先提交下一組，稍後再收取結果。

    private NeowPipelinePending? _pipelinePending;

    internal sealed class NeowPipelinePending
    {
        internal required IReadOnlyList<FamilyObservationWindow> Windows { get; init; }
        internal required IReadOnlyList<FamilyCandidateSet> Inputs { get; init; }
        internal required NeowFamilyGpuExecutor.BatchSubmission Submission { get; init; }
    }

    internal bool PipelineCapable =>
        FamilyPipelineModel.Compiled && FamilyBatchGrouping.Enabled && !_disabled &&
        _gpuPlan is not null && _composite is null && _transformations is null;

    internal bool PipelinePending => _pipelinePending is not null;

    internal async ValueTask SubmitPipelineGroupAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken)
    {
        if (_pipelinePending is not null) throw new InvalidOperationException("N.PipelinePendingExists");
        if (!PipelineCapable) throw new FamilyBatchNotSupportedException("N.PipelineUnsupported");
        int count = windows.Count;
        if (count <= 1 || count > FamilyBatchGrouping.GroupSize)
            throw new InvalidOperationException("N.PipelineGroupSize");
        if (count != inputs.Count) throw new InvalidOperationException("N.PipelineWindowCountMismatch");
        for (int index = 0; index < count; index++)
        {
            if (windows[index].ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
                throw new InvalidOperationException("NFamilyObservationContextMismatch");
            FamilyCandidateSet input = inputs[index];
            if (input.Batch != windows[index].Batch)
                throw new InvalidOperationException("N.PipelineBatchIdentityMismatch");
            if (!input.IsDense || input.Count != windows[index].Batch.BatchCandidateCount)
                throw new InvalidOperationException("N.PipelineDenseOnly");
        }
        _compact = false;
        try
        {
            await EnsureGpuAsync(executionContext, cancellationToken).ConfigureAwait(false);
            if (_gpu is not { SupportsBatchExecution: true })
                throw new FamilyBatchNotSupportedException("N.PipelineExecutorUnavailable");
            NeowFamilyGpuExecutor.BatchSubmission submission = await executionContext.ExecutePrimaryGpuAsync(
                _ => _gpu.SubmitBatch(inputs, cancellationToken), cancellationToken).ConfigureAwait(false);
            _pipelinePending = new NeowPipelinePending
            {
                Windows = windows,
                Inputs = inputs,
                Submission = submission
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (FamilyBatchNotSupportedException) { throw; }
        catch (Exception ex)
        {
            MarkFamilyFault(ex);
            throw;
        }
    }

    internal async ValueTask<IReadOnlyList<FamilyCandidateSet>> CompletePipelineGroupAsync(
        FamilyExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        NeowPipelinePending pending = _pipelinePending ??
            throw new InvalidOperationException("N.PipelineNoPending");
        try
        {
            (FamilyCandidateSet[] Results, NeowFamilyGpuMetrics[] Metrics) batch =
                await executionContext.ExecutePrimaryGpuAsync(
                    _ => _gpu!.CompleteBatch(pending.Submission, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            for (int index = 0; index < batch.Results.Length; index++)
                RecordCompletedBatch(pending.Inputs[index], batch.Results[index], batch.Metrics[index], executionContext);
            return batch.Results;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            MarkFamilyFault(ex);
            throw;
        }
        finally
        {
            _pipelinePending = null;
        }
    }

    // 退出流水線時先 Sync，再丟棄結果，避免主裝置留下未等候的提交。
    internal void AbortPipelineGroup(FamilyExecutionContext executionContext)
    {
        NeowPipelinePending? pending = _pipelinePending;
        _pipelinePending = null;
        if (pending is null) return;
        try
        {
            executionContext.ExecutePrimaryGpuAsync(
                _ => { _gpu?.AbortPendingBatch(); return true; }, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            RuntimeLog.TryBackgroundWarning(
                $"nPipelineAbortFailure=true;reason={ex.GetType().Name}:{ex.Message}");
        }
    }
}
