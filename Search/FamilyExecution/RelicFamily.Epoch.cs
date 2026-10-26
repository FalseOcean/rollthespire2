using System.Diagnostics;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

// 非膠囊 Relic 的共用 epoch 路徑。
internal sealed partial class RelicFamily : IFamilyEpochInvocation
{
    private RelicFamilyGpuExecutor.RelicBatchSubmission? _epochSubmission;

    bool IFamilyEpochInvocation.EpochEligible =>
        FamilyPipelineModel.EpochCompiled && FamilyBatchGrouping.Enabled && !_gpuDisabled &&
        (_gpu is null || _gpu.SupportsBatchExecution);

    async ValueTask IFamilyEpochInvocation.EnsureEpochExecutorAsync(
        FamilyExecutionContext context, CancellationToken cancellationToken)
    {
        if (_gpuDisabled) throw new InvalidOperationException("R.SelectedInvocationFaulted");
        await EnsureGpuAsync(context, cancellationToken).ConfigureAwait(false);
        if (_gpu is not { SupportsBatchExecution: true })
            throw new FamilyBatchNotSupportedException("R.EpochExecutorUnavailable");
    }

    void IFamilyEpochInvocation.PrepareEpoch(FamilyEpoch epoch,
        IReadOnlyList<FamilyObservationWindow> windows, IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (_epochSubmission is not null) throw new InvalidOperationException("R.EpochPendingExists");
        if (_gpu is null) throw new InvalidOperationException("R.EpochExecutorMissing");
        if (_gpuDisabled) throw new InvalidOperationException("R.SelectedInvocationFaulted");
        int count = windows.Count;
        if (count != inputs.Count) throw new InvalidOperationException("R.EpochWindowCountMismatch");
        if (count <= 1 || count > FamilyBatchGrouping.GroupSize)
            throw new InvalidOperationException("R.EpochGroupSize");
        for (int index = 0; index < count; index++)
        {
            if (windows[index].ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
                throw new InvalidOperationException("REpochObservationContextMismatch");
            if (inputs[index].Batch != windows[index].Batch)
                throw new InvalidOperationException("R.EpochBatchIdentityMismatch");
            if (inputs[index].Count == 0)
                throw new FamilyBatchNotSupportedException("R.EpochEmptyInput");
        }
        ObserveInputMode(compactAbi1Input: !inputs[0].IsDense);
        _epochSubmission = _gpu.PrepareEpochBatch(epoch, inputs, cancellationToken);
    }

    void IFamilyEpochInvocation.RecordEpochDispatches(FamilyEpoch epoch)
    {
        if (_gpu is null) throw new InvalidOperationException("R.EpochExecutorMissing");
        _gpu.RecordEpochDispatches(epoch);
    }

    IReadOnlyList<FamilyCandidateSet> IFamilyEpochInvocation.CompleteEpoch(
        FamilyExecutionContext context, double sharedSubmitMs, double sharedSyncMs)
    {
        RelicFamilyGpuExecutor.RelicBatchSubmission submission = _epochSubmission ??
            throw new InvalidOperationException("R.EpochNoPending");
        try
        {
            if (_gpu is null) throw new InvalidOperationException("R.EpochExecutorMissing");
            (FamilyCandidateSet[] results, RelicFamilyGpuBatchMetrics[] metrics) =
                _gpu.CompleteEpochBatch(submission, CancellationToken.None);
            int count = results.Length;
            double submitShare = sharedSubmitMs / count;
            double syncShare = sharedSyncMs / count;
            for (int index = 0; index < results.Length; index++)
            {
                RelicFamilyGpuBatchMetrics batchMetrics = sharedSubmitMs == 0d && sharedSyncMs == 0d
                    ? metrics[index]
                    : metrics[index] with
                    {
                        SubmitMs = metrics[index].SubmitMs + submitShare,
                        SyncMs = metrics[index].SyncMs + syncShare,
                        CanonicalAbi1ReadyMs = metrics[index].CanonicalAbi1ReadyMs + submitShare + syncShare
                    };
                FamilyCandidateSet input = submission.Inputs[index];
                _invocations++;
                if (input.IsDense) _denseInputs++; else _compactInputs++;
                RecordCompletedBatch(input, results[index], batchMetrics, 0d,
                    Stopwatch.GetTimestamp(), context);
            }
            return results;
        }
        catch (Exception ex)
        {
            _gpuDisabled = true;
            RuntimeLog.TryBackgroundWarning(
                $"rFamilyEpochFault=true;failure={ex.GetType().Name}:{ex.Message};recovery=None");
            throw;
        }
        finally
        {
            _epochSubmission = null;
        }
    }

    void IFamilyEpochInvocation.AbortEpoch()
    {
        if (_epochSubmission is null) return;
        _epochSubmission = null;
        try { _gpu?.AbortPendingBatch(); }
        catch (Exception ex)
        {
            RuntimeLog.TryBackgroundWarning(
                $"rEpochAbortFailure=true;reason={ex.GetType().Name}:{ex.Message}");
        }
    }
}
