using System.Runtime.ExceptionServices;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFilterExecution
{
    private NeowFamilyGpuExecutor? _pairGpu;
    private bool _pairParity;
    private int _densePairGroups, _pairLiveGroups, _liveBatches;
    private int _lastDenseOutputCount = -1;
    internal bool CanGroupDenseBatches => !_disabled && _gpuPlan is { DirectNestedMode: 0, JointTransformMetaOffset: < 0 } &&
        _composite is null && _transformations is null;
    bool IDenseFamilyBatchGrouping.CanGroupDenseBatches => CanGroupDenseBatches;
    bool IDenseFamilyBatchGrouping.PreferDenseBatchGrouping => CanGroupDenseBatches && !_compact && !_skipExactValidation &&
        Volatile.Read(ref _lastDenseOutputCount) is >= 0 and <= SmallCandidateExactPolicy.MaximumCandidates;

    async ValueTask<DenseFamilyBatchGroup> IDenseFamilyBatchGrouping.PrepareDensePairAsync(FilterRuntimeServices context,
        SearchBatch first, SearchBatch second, CancellationToken token)
    {
        CheckPairBatches(first,second);
        token.ThrowIfCancellationRequested();
        if (!CanGroupDenseBatches) throw new InvalidOperationException("N.DensePairNotAdmitted");
        _compact = false;
        try
        {
            await context.ExecuteGpuAsync(rd =>
            {
                uint tag = (uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(Body.InputIdentity, 0);
                _gpu ??= new NeowFamilyGpuExecutor(rd, _gpuPlan!, tag, optimizations: _gpuOptimizations);
                _pairGpu ??= new NeowFamilyGpuExecutor(rd, _gpuPlan!, tag, sharedDenseScratch: _gpu, optimizations: _gpuOptimizations);
                return true;
            }, token).ConfigureAwait(false);
            try
            {
                var pair = await context.ExecuteGpuAsync(_ => NeowFamilyGpuExecutor.ExecuteDensePair(
                    _gpu!, first, _pairGpu!, second, token), token).ConfigureAwait(false);
                _densePairGroups++;
                var left = pair.Left ?? throw new InvalidDataException("N.DensePairMissingPublicLeft");
                ValidateCpuParity(FamilyCandidateSet.Dense(first), left);
                left=RefinePublic(context,left,token,out double leftRefineMs);
                ObserveGpuResult(context, FamilyCandidateSet.Dense(first), left, ChargeShared(pair.LeftMetrics with {CanonicalMs=pair.LeftMetrics.CanonicalMs+leftRefineMs}, pair.SharedMs), grouped: true);
                try
                {
                    var right = pair.Right ?? throw new InvalidDataException("N.DensePairMissingPublicRight");
                    ValidateCpuParity(FamilyCandidateSet.Dense(second), right, force: !_pairParity);
                    _pairParity |= second.BatchCandidateCount > 0;
                    right=RefinePublic(context,right,token,out double rightRefineMs);
                    ObserveGpuResult(context, FamilyCandidateSet.Dense(second), right, pair.RightMetrics with {CanonicalMs=pair.RightMetrics.CanonicalMs+rightRefineMs}, grouped: true);
                    ObserveDensePairLive(context, first.BatchCandidateCount + second.BatchCandidateCount, left.Count + right.Count,
                        pair.SharedMs + pair.LeftMetrics.CanonicalMs + pair.RightMetrics.CanonicalMs+leftRefineMs+rightRefineMs);
                    LogDensePair(first, second, pair.SharedMs, pair.LeftMetrics.Dispatches + pair.RightMetrics.Dispatches);
                    return new(PreparedFamilyBatch.Ready(this, left), PreparedFamilyBatch.Ready(this, right));
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    context.Costs.ObserveCompletedGroup(Body, false, first.BatchCandidateCount, left.Count,
                        1, pair.SharedMs + pair.LeftMetrics.CanonicalMs+leftRefineMs, includeLive: false);
                    return FailedRight(left, second, failure);
                }
            }
            catch (NeowDensePairRightFailure failure)
            {
                if (failure.InnerException is OperationCanceledException canceled) ExceptionDispatchInfo.Capture(canceled).Throw();
                _densePairGroups++;
                var left = failure.Left ?? throw new InvalidDataException("N.DensePairMissingPublicLeft", failure);
                ValidateCpuParity(FamilyCandidateSet.Dense(first), left);
                left=RefinePublic(context,left,token,out double leftRefineMs);
                ObserveGpuResult(context, FamilyCandidateSet.Dense(first), left, ChargeShared(failure.LeftMetrics with {CanonicalMs=failure.LeftMetrics.CanonicalMs+leftRefineMs}, failure.SharedMs), grouped: true);
                context.Costs.ObserveCompletedGroup(Body, false, first.BatchCandidateCount, left.Count,
                    1, failure.SharedMs + failure.LeftMetrics.CanonicalMs+leftRefineMs, includeLive: false);
                return FailedRight(left, failure.RightBatch, failure.InnerException ?? failure);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure)
        {
            _disabled = true;
            LogPairFailure(FamilyId, first, second, failure);
            throw;
        }
    }

    private DenseFamilyBatchGroup FailedRight(FamilyCandidateSet left, SearchBatch right, Exception failure)
    {
        // Log at origin even if the target is reached before this prepared root is consumed.
        LogPairFailure(FamilyId, left.Batch, right, failure);
        return new(PreparedFamilyBatch.Ready(this, left), PreparedFamilyBatch.Failed(this, right, failure, () => _disabled = true));
    }
    private void ObserveDensePairLive(FilterRuntimeServices context, int inputs, int outputs, double milliseconds)
    {
        bool includeLive = _pairLiveGroups++ > 0;
        context.Costs.ObserveCompletedGroup(Body, false, inputs, outputs, 2, milliseconds, includeLive);
        if (!includeLive) return;
        Interlocked.Add(ref _liveInputs, inputs); _liveMs += milliseconds; _liveBatches += 2;
    }
    private static NeowFamilyGpuMetrics ChargeShared(NeowFamilyGpuMetrics metrics, double shared) =>
        metrics with { CanonicalMs = metrics.CanonicalMs + shared, DispatchSyncMs = metrics.DispatchSyncMs + shared };
    private static void CheckPairBatches(SearchBatch first, SearchBatch second)
    {
        if (first == second)
            throw new InvalidOperationException("N.DensePairObservationContextMismatch");
    }
    private static void LogPairFailure(string family, SearchBatch first, SearchBatch second, Exception failure) =>
        RuntimeLog.TryBackgroundWarning($"nDensePairPhysicalFault=true;family={family};firstBatch={first.BatchBase};secondBatch={second.BatchBase};recovery=None;exceptionJson=" + RuntimeLog.SafeJson(failure.ToString()));
    private static void LogDensePair(SearchBatch first, SearchBatch second, double shared, int dispatches) =>
        RuntimeLog.TryBackgroundDetail($"nDensePair=true;firstBatch={first.BatchBase};secondBatch={second.BatchBase};rootBatches=2;submissionGroups={(first.BatchCandidateCount > 0 || second.BatchCandidateCount > 0 ? 1 : 0)};dispatches={dispatches};sharedSubmitSyncMs={shared};sharedWaitChargedOnce=true;localCalibrationEligible=false");
    private static void DisposePair(NeowFamilyGpuExecutor? borrower, NeowFamilyGpuExecutor? donor)
    {
        List<Exception>? failures = null;
        try { borrower?.Dispose(); } catch (Exception failure) { (failures ??= []).Add(failure); }
        try { donor?.Dispose(); } catch (Exception failure) { (failures ??= []).Add(failure); }
        if (failures is not null) throw new AggregateException("N.DensePairDispose", failures);
    }
    private void ValidateCpuParity(FamilyCandidateSet input, FamilyCandidateSet output, bool force = false)
    {
        if (input.Count > 0 && (!_parity || force))
        {
            var profile = _profile;
            ulong[] survivors = output.ExportAbi1().ToArray();
            foreach (ulong ordinal in input.EnumerateLogicalOrdinals().Take(256))
            {
                ulong root = profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, input.Batch.GlobalCandidate(ordinal)));
                if ((_cpuMatcher(root) && (_composite?.Reference.Matches(root) ?? true)) != (Array.BinarySearch(survivors, ordinal) >= 0))
                    throw new InvalidDataException("NFamilyCpuReferenceParityMismatch");
            }
            _parity = true;
        }
    }
    private void ObserveGpuResult(FilterRuntimeServices executionContext, FamilyCandidateSet input,
        FamilyCandidateSet output, NeowFamilyGpuMetrics metrics, bool grouped = false)
    {
        if (!grouped) executionContext.Costs.ObserveCompleted(Body,input,output.Count,metrics.CanonicalMs,metrics.DispatchSyncMs,metrics.ReadbackMs,metrics.ReadbackBytes,passedCoverage:executionContext.CoverageFor(Body));
        _batches++; _inputs += input.Count; _outputs += output.Count; _ms += metrics.CanonicalMs;
        if (input.IsDense) Volatile.Write(ref _lastDenseOutputCount, output.Count);
        _bonesEntryPass += metrics.CurseSurvivors; _bonesPairPass += metrics.PairSurvivors;
        _uploadMs += metrics.UploadMs; _dispatchMs += metrics.DispatchSyncMs;
        _readbackMs += metrics.ReadbackMs; _canonicalizeMs += metrics.CanonicalizeMs;
        if (input.Count == NeowFamilyGpuExecutor.Capacity)
        {
            _fullBatchInputs += input.Count; _fullBatchOutputs += output.Count;
            _fullBatchDispatchMs += metrics.DispatchSyncMs; _fullBatchCanonicalMs += metrics.CanonicalMs;
        }
        if (_composite?.UsesBonesArcane == true)
        {
            _nrPairPass += metrics.NrPairPass; _arcanePass += metrics.ArcanePass;
            _rarityPass += metrics.CapsuleRarityPass; _heavyEntered += metrics.HeavyEntered;
            if (output.Count == 0) _zeroSurvivorBatches++; else _payloadReadbackCount++;
            _payloadReadbackMs += metrics.PayloadReadbackMs;
            if (RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled)
                RuntimeLog.TryBackgroundDetail($"boundedBonesCapsuleArcaneNr=True;batchBase={input.Batch.BatchBase};pairPass={metrics.NrPairPass};arcanePass={metrics.ArcanePass};capsuleRarityPass={metrics.CapsuleRarityPass};heavyEntered={metrics.HeavyEntered};nrOutput={output.Count};zeroSurvivorBatches={_zeroSurvivorBatches};payloadReadbackCount={_payloadReadbackCount};payloadReadbackMs={metrics.PayloadReadbackMs}");
        }
        if (!grouped && _batches > 1)
        {
            Interlocked.Add(ref _liveInputs, input.Count); _liveMs += metrics.CanonicalMs; _liveBatches++;
            if (input.Count >= (_compact ? NeowFamilyGpuExecutor.Capacity / 4 : NeowFamilyGpuExecutor.Capacity))
            { _steady++; _peak = Math.Max(_peak, input.Count * 1000d / metrics.CanonicalMs); }
        }
        if (RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled && _gpuPlan is { } gpuPlan)
            RuntimeLog.TryBackgroundDetail($"nFamilyBatch=true;densePairMember={grouped};localCalibrationEligible={!grouped};physicalRevision={ResolveConditionPerformance(_compact).PhysicalImplementationRevision};batchBase={input.Batch.BatchBase};input={input.Count};processed={metrics.Processed};survivors={output.Count};groups={metrics.Groups};dispatches={metrics.Dispatches};curseSurvivors={metrics.CurseSurvivors};pairSurvivors={metrics.PairSurvivors};uploadMs={metrics.UploadMs};dispatchSyncMs={metrics.DispatchSyncMs};readbackMs={metrics.ReadbackMs};readbackBytes={metrics.ReadbackBytes};canonicalizeMs={metrics.CanonicalizeMs};canonicalAbi1ReadyMs={metrics.CanonicalMs};firstDrawAcceptMask={gpuPlan.Meta[68]};firstDrawRejectMask={gpuPlan.Meta[69]};trackedBones={gpuPlan.Meta[67]};boundedBonesNr={_composite?.UsesBonesCheckpoint == true};leafyPreGate={gpuPlan.UsesLeafyPreGate};parity={_parity};workspaceBytes={(_gpu?.ResidentBytes ?? 0) + (_pairGpu?.ResidentBytes ?? 0)};scratchBytes=0;ownerThread={executionContext.GpuOwnerThreadId}");
    }
}
