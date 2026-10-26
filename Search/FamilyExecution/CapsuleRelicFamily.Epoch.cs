using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

// 膠囊遺物的 K 個窗口共用 epoch；Submit/Sync 由 epoch 負責。
internal sealed partial class CapsuleRelicFamily : IFamilyEpochInvocation
{
    private RelicFamilyGpuExecutor.RelicBatchSubmission? _epochSubmission;

    bool IFamilyEpochInvocation.EpochEligible =>
        FamilyPipelineModel.EpochCompiled && FamilyBatchGrouping.Enabled && _gpuPlan is not null &&
        !_gpuDisabled && (_gpu is null || _gpu.SupportsBatchExecution);

    async ValueTask IFamilyEpochInvocation.EnsureEpochExecutorAsync(
        FamilyExecutionContext context, CancellationToken cancellationToken)
    {
        if (_gpuPlan is null) throw new FamilyBatchNotSupportedException("Rfull.EpochCpuPlan");
        if (_gpuDisabled) throw new InvalidOperationException("Rfull.SelectedInvocationFaulted");
        if (_gpu is null)
            _gpu = await context.ExecuteRestGpuAsync(
                rd => RelicFamilyGpuExecutor.Create(rd, _request, _gpuPlan), cancellationToken)
                .ConfigureAwait(false);
        if (!_gpu.SupportsBatchExecution)
            throw new FamilyBatchNotSupportedException("Rfull.EpochExecutorUnavailable");
    }

    void IFamilyEpochInvocation.PrepareEpoch(FamilyEpoch epoch,
        IReadOnlyList<FamilyObservationWindow> windows, IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (_epochSubmission is not null) throw new InvalidOperationException("Rfull.EpochPendingExists");
        if (_gpu is null) throw new InvalidOperationException("Rfull.EpochExecutorMissing");
        if (_gpuDisabled) throw new InvalidOperationException("Rfull.SelectedInvocationFaulted");
        int count = windows.Count;
        if (count != inputs.Count) throw new InvalidOperationException("Rfull.EpochWindowCountMismatch");
        if (count <= 1 || count > FamilyBatchGrouping.GroupSize)
            throw new InvalidOperationException("Rfull.EpochGroupSize");
        for (int index = 0; index < count; index++)
        {
            if (windows[index].ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
                throw new InvalidOperationException("Rfull.EpochObservationContextMismatch");
            if (inputs[index].Batch != windows[index].Batch)
                throw new InvalidOperationException("Rfull.EpochBatchIdentityMismatch");
            if (inputs[index].Count == 0)
                throw new FamilyBatchNotSupportedException("Rfull.EpochEmptyInput");
        }
        _compactInput = !inputs[0].IsDense;
        _epochSubmission = _gpu.PrepareEpochBatch(epoch, inputs, cancellationToken);
    }

    void IFamilyEpochInvocation.RecordEpochDispatches(FamilyEpoch epoch)
    {
        if (_gpu is null) throw new InvalidOperationException("Rfull.EpochExecutorMissing");
        _gpu.RecordEpochDispatches(epoch);
    }

    IReadOnlyList<FamilyCandidateSet> IFamilyEpochInvocation.CompleteEpoch(
        FamilyExecutionContext context, double sharedSubmitMs, double sharedSyncMs)
    {
        RelicFamilyGpuExecutor.RelicBatchSubmission submission = _epochSubmission ??
            throw new InvalidOperationException("Rfull.EpochNoPending");
        try
        {
            if (_gpu is null) throw new InvalidOperationException("Rfull.EpochExecutorMissing");
            (FamilyCandidateSet[] results, RelicFamilyGpuBatchMetrics[] metrics) =
                _gpu.CompleteEpochBatch(submission, CancellationToken.None);
            if (!_referenceParityPassed) ValidateReferenceParity(submission.Inputs[0], results[0]);
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
                RecordEpochInvocation(submission.Inputs[index], results[index], batchMetrics);
            }
            return results;
        }
        catch (Exception ex)
        {
            _gpuDisabled = true;
            _failure = ex.GetType().Name + ":" + ex.Message;
            RuntimeLog.TryBackgroundWarning(
                $"rfullFamilyEpochFault=true;failure={Sanitize(_failure)};recovery=None");
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
                $"rfullEpochAbortFailure=true;reason={ex.GetType().Name}:{ex.Message}");
        }
    }

    private void RecordEpochInvocation(FamilyCandidateSet input, FamilyCandidateSet survivors,
        in RelicFamilyGpuBatchMetrics metrics)
    {
        _gpuBatches++;
        _costSamples.Observe(this, new(!input.IsDense, false, input.Count), input.Count,
            metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs, outputs: metrics.Survivors,
            canonicalMs: metrics.CanonicalAbi1ReadyMs, readbackMs: metrics.ReadbackMs,
            readbackBytes: metrics.ReadbackBytes);
        _inputs += metrics.InputCandidates;
        _outputs += metrics.Survivors;
        _canonicalMs += metrics.CanonicalAbi1ReadyMs;
        double rate = metrics.CanonicalAbi1ReadyMs <= 0d
            ? 0d : metrics.InputCandidates * 1000d / metrics.CanonicalAbi1ReadyMs;
        if (_gpuBatches > 1)
        {
            Interlocked.Add(ref _liveInputs, metrics.InputCandidates);
            _liveMs += metrics.CanonicalAbi1ReadyMs;
            int steadyMinimum = _compactInput
                ? RelicFamilyGpuExecutor.CompactInputCapacity / 4
                : RelicFamilyGpuExecutor.Capacity;
            if (metrics.InputCandidates >= steadyMinimum)
            {
                _steadyBatches++;
                _peak = Math.Max(_peak, rate);
            }
        }
        if (_gpuBatches <= 3 || _gpuBatches % 256 == 0)
            RuntimeLog.TryBackgroundInfo(
                $"rfullFamilyBatch=true;physicalRevision={ResolveConditionPerformance(_compactInput).PhysicalImplementationRevision};" +
                $"arrival={ArrivalName(_gpuPlan!.CapsuleMetadata[0])};" +
                $"batchBase={input.Batch.BatchBase};input={metrics.InputCandidates};processed={metrics.ProcessedCandidates};" +
                $"survivors={metrics.Survivors};groups={metrics.DispatchGroups};readbackBytes={metrics.ReadbackBytes};" +
                $"canonicalAbi1ReadyMs={metrics.CanonicalAbi1ReadyMs:F4};parity={_referenceParityPassed};" +
                "bagSemantics=InitialWorldBag;capsuleProjection=RarityTrackedRank;legacyFallback=false;scope=EpochBatch");
    }
}
