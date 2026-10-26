using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

// Neow 群組記入共用 epoch，Submit/Sync 由 epoch 負責。
internal sealed partial class NeowFamily : IFamilyEpochInvocation
{
    private NeowFamilyGpuExecutor.BatchSubmission? _epochSubmission;

    bool IFamilyEpochInvocation.EpochEligible =>
        FamilyPipelineModel.EpochCompiled && FamilyBatchGrouping.Enabled && !_disabled &&
        _gpuPlan is not null && _composite is null && _transformations is null;

    async ValueTask IFamilyEpochInvocation.EnsureEpochExecutorAsync(
        FamilyExecutionContext context, CancellationToken cancellationToken)
    {
        await EnsureGpuAsync(context, cancellationToken).ConfigureAwait(false);
        if (_gpu is not { SupportsBatchExecution: true })
            throw new FamilyBatchNotSupportedException("N.EpochExecutorUnavailable");
    }

    void IFamilyEpochInvocation.PrepareEpoch(FamilyEpoch epoch,
        IReadOnlyList<FamilyObservationWindow> windows, IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (_epochSubmission is not null) throw new InvalidOperationException("N.EpochPendingExists");
        if (_gpu is null) throw new InvalidOperationException("N.EpochExecutorMissing");
        if (_disabled) throw new InvalidOperationException("N.SelectedInvocationFaulted");
        int count = windows.Count;
        if (count != inputs.Count) throw new InvalidOperationException("N.EpochWindowCountMismatch");
        if (count <= 1 || count > FamilyBatchGrouping.GroupSize)
            throw new InvalidOperationException("N.EpochGroupSize");
        for (int index = 0; index < count; index++)
        {
            if (windows[index].ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
                throw new InvalidOperationException("NEpochObservationContextMismatch");
            FamilyCandidateSet input = inputs[index];
            if (input.Batch != windows[index].Batch)
                throw new InvalidOperationException("N.EpochBatchIdentityMismatch");
            if (!input.IsDense || input.Count != windows[index].Batch.BatchCandidateCount)
                throw new FamilyBatchNotSupportedException("N.EpochDenseOnly");
        }
        _epochSubmission = _gpu.PrepareEpochBatch(epoch, inputs, cancellationToken);
        _compact = false;
    }

    void IFamilyEpochInvocation.RecordEpochDispatches(FamilyEpoch epoch)
    {
        if (_gpu is null) throw new InvalidOperationException("N.EpochExecutorMissing");
        _gpu.RecordEpochDispatches(epoch);
    }

    IReadOnlyList<FamilyCandidateSet> IFamilyEpochInvocation.CompleteEpoch(
        FamilyExecutionContext context, double sharedSubmitMs, double sharedSyncMs)
    {
        NeowFamilyGpuExecutor.BatchSubmission submission = _epochSubmission ??
            throw new InvalidOperationException("N.EpochNoPending");
        try
        {
            if (_gpu is null) throw new InvalidOperationException("N.EpochExecutorMissing");
            (FamilyCandidateSet[] results, NeowFamilyGpuMetrics[] metrics) =
                _gpu.CompleteBatch(submission, CancellationToken.None);
            int count = results.Length;
            double sharedDispatchSyncMs = (sharedSubmitMs + sharedSyncMs) / count;
            for (int index = 0; index < count; index++)
            {
                NeowFamilyGpuMetrics adjusted = metrics[index] with
                {
                    CanonicalMs = metrics[index].CanonicalMs + sharedDispatchSyncMs,
                    DispatchSyncMs = metrics[index].DispatchSyncMs + sharedDispatchSyncMs,
                    SubmitMs = sharedSubmitMs / count,
                    SyncMs = sharedSyncMs / count
                };
                RecordCompletedBatch(submission.Inputs[index], results[index], adjusted, context);
            }
            return results;
        }
        catch (Exception ex)
        {
            try { _gpu?.ClearPendingBatch(); }
            catch { /* 僅盡力清理。 */ }
            MarkFamilyFault(ex);
            throw;
        }
        finally
        {
            _epochSubmission = null;
        }
    }

    void IFamilyEpochInvocation.AbortEpoch() => AbortEpoch();

    // epoch 中止時盡量恢復裝置狀態。
    internal void AbortEpoch()
    {
        if (_epochSubmission is null) return;
        _epochSubmission = null;
        try
        {
            _gpu?.ClearPendingBatch();
            _gpu?.AbortPendingBatch();
        }
        catch (Exception ex)
        {
            RuntimeLog.TryBackgroundWarning(
                $"nEpochAbortFailure=true;reason={ex.GetType().Name}:{ex.Message}");
        }
    }
}
