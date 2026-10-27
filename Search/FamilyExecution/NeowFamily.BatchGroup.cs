using System.Runtime.ExceptionServices;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamily
{
    private NeowFamilyGpuExecutor? _pairGpu;
    private bool _pairParity;
    private int _densePairGroups, _pairLiveGroups, _liveBatches;
    private int _lastDenseOutputCount = -1;
    internal bool CanGroupDenseBatches => !_disabled && _gpuPlan is { DirectNestedMode: 0, JointTransformMetaOffset: < 0 } &&
        _composite is null && _transformations is null;
    bool IDenseFamilyBatchGrouping.CanGroupDenseBatches => CanGroupDenseBatches;
    bool IDenseFamilyBatchGrouping.PreferDenseBatchGrouping => CanGroupDenseBatches && !_compact && !_request.RunOptions.SkipExactValidation &&
        Volatile.Read(ref _lastDenseOutputCount) is >= 0 and <= SmallCandidateExactPolicy.MaximumCandidates;

    async ValueTask<DenseFamilyBatchGroup> IDenseFamilyBatchGrouping.PrepareDensePairAsync(FamilyExecutionContext context,
        FamilyObservationWindow first, FamilyObservationWindow second, CancellationToken token)
    {
        CheckPairObservations(_request.SnapshotFingerprint, first, second);
        token.ThrowIfCancellationRequested();
        if (!CanGroupDenseBatches) throw new InvalidOperationException("N.DensePairNotAdmitted");
        _compact = false;
        try
        {
            await context.ExecuteGpuAsync(rd =>
            {
                uint tag = (uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(_request.SnapshotFingerprint, 0);
                _gpu ??= new NeowFamilyGpuExecutor(rd, _gpuPlan!, tag, optimizations: _gpuOptimizations);
                _pairGpu ??= new NeowFamilyGpuExecutor(rd, _gpuPlan!, tag, sharedDenseScratch: _gpu, optimizations: _gpuOptimizations);
                return true;
            }, token).ConfigureAwait(false);
            try
            {
                var pair = await context.ExecuteGpuAsync(_ => NeowFamilyGpuExecutor.ExecuteDensePair(
                    _gpu!, first.Batch, _pairGpu!, second.Batch, token), token).ConfigureAwait(false);
                _densePairGroups++;
                var left = pair.Left ?? throw new InvalidDataException("N.DensePairMissingPublicLeft");
                ValidateCpuParity(FamilyCandidateSet.Dense(first.Batch), left);
                ObserveGpuResult(context, FamilyCandidateSet.Dense(first.Batch), left, ChargeShared(pair.LeftMetrics, pair.SharedMs), grouped: true);
                try
                {
                    var right = pair.Right ?? throw new InvalidDataException("N.DensePairMissingPublicRight");
                    ValidateCpuParity(FamilyCandidateSet.Dense(second.Batch), right, force: !_pairParity);
                    _pairParity |= second.Batch.BatchCandidateCount > 0;
                    ObserveGpuResult(context, FamilyCandidateSet.Dense(second.Batch), right, pair.RightMetrics, grouped: true);
                    ObserveDensePairLive(first.Batch.BatchCandidateCount + second.Batch.BatchCandidateCount,
                        pair.SharedMs + pair.LeftMetrics.CanonicalMs + pair.RightMetrics.CanonicalMs);
                    LogDensePair(first.Batch, second.Batch, pair.SharedMs, pair.LeftMetrics.Dispatches + pair.RightMetrics.Dispatches);
                    return new(PreparedFamilyBatch.Ready(this, left), PreparedFamilyBatch.Ready(this, right));
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    return FailedRight(left, second.Batch, failure);
                }
            }
            catch (NeowDensePairRightFailure failure)
            {
                if (failure.InnerException is OperationCanceledException canceled) ExceptionDispatchInfo.Capture(canceled).Throw();
                _densePairGroups++;
                var left = failure.Left ?? throw new InvalidDataException("N.DensePairMissingPublicLeft", failure);
                ValidateCpuParity(FamilyCandidateSet.Dense(first.Batch), left);
                ObserveGpuResult(context, FamilyCandidateSet.Dense(first.Batch), left, ChargeShared(failure.LeftMetrics, failure.SharedMs), grouped: true);
                return FailedRight(left, failure.RightBatch, failure.InnerException ?? failure);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure)
        {
            _disabled = true;
            LogPairFailure(FamilyId, first.Batch, second.Batch, failure);
            throw;
        }
    }

    private DenseFamilyBatchGroup FailedRight(FamilyCandidateSet left, SearchBatch right, Exception failure)
    {
        // Log at origin even if the target is reached before this prepared root is consumed.
        LogPairFailure(FamilyId, left.Batch, right, failure);
        return new(PreparedFamilyBatch.Ready(this, left), PreparedFamilyBatch.Failed(this, right, failure, () => _disabled = true));
    }
    private void ObserveDensePairLive(int inputs, double milliseconds)
    {
        if (_pairLiveGroups++ == 0) return;
        Interlocked.Add(ref _liveInputs, inputs); _liveMs += milliseconds; _liveBatches += 2;
    }
    private static NeowFamilyGpuMetrics ChargeShared(NeowFamilyGpuMetrics metrics, double shared) =>
        metrics with { CanonicalMs = metrics.CanonicalMs + shared, DispatchSyncMs = metrics.DispatchSyncMs + shared };
    private static void CheckPairObservations(string fingerprint, FamilyObservationWindow first, FamilyObservationWindow second)
    {
        if (first.ExactRequest.SnapshotFingerprint != fingerprint || second.ExactRequest.SnapshotFingerprint != fingerprint || first.Batch == second.Batch)
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
            var profile = RuntimeProfileRegistry.Select(_request.Detection);
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
    private void ObserveGpuResult(FamilyExecutionContext executionContext, FamilyCandidateSet input,
        FamilyCandidateSet output, NeowFamilyGpuMetrics metrics, bool grouped = false)
    {
        if (!grouped) _costSamples.Observe(this, new(!input.IsDense, false, input.Count), input.Count, metrics.DispatchSyncMs, outputs: output.Count, canonicalMs: metrics.CanonicalMs, readbackMs: metrics.ReadbackMs, readbackBytes: metrics.ReadbackBytes);
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
    internal sealed partial class PartyInitial
    {
        private NeowFamilyGpuExecutor? _pairFused;
        private bool _pairFusedParity, _groupingDisabled;
        private int _densePairGroups, _pairLiveGroups, _liveBatches;
        private int _lastDenseOutputCount = -1;
        internal bool CanGroupDenseBatches => !_privatePairDisposed && !_groupingDisabled && (CanGroupPrivateDenseBatches || UsesFused &&
            _gpuStages.All(stage => stage.Family is NeowFamily { CanGroupDenseBatches: true }));
        bool IDenseFamilyBatchGrouping.CanGroupDenseBatches => CanGroupDenseBatches;
        // Private PartyInitial retains its original complete suffix policy.
        // Public fused small populations have the same CPU boundary as solo N.
        bool IDenseFamilyBatchGrouping.PreferDenseBatchGrouping => CanGroupDenseBatches && UsesFused && !_compact && !_request.RunOptions.SkipExactValidation &&
            Volatile.Read(ref _lastDenseOutputCount) is >= 0 and <= SmallCandidateExactPolicy.MaximumCandidates;
        bool IFamilyInvocation.ExecutionFaulted => _groupingDisabled || _gpuStages.Any(stage => stage.Family.ExecutionFaulted);
        private NeowFamilyGpuExecutor CreateFused(Godot.RenderingDevice rd, NeowFamilyGpuExecutor? donor = null) =>
            new(rd, ((NeowFamily)_gpuStages[0].Family).GpuPlan!, 43,
                partyPlans: _gpuStages.Select(stage => ((NeowFamily)stage.Family).GpuPlan!).ToArray(), sharedDenseScratch: donor);

        async ValueTask<DenseFamilyBatchGroup> IDenseFamilyBatchGrouping.PrepareDensePairAsync(FamilyExecutionContext context,
            FamilyObservationWindow first, FamilyObservationWindow second, CancellationToken token)
        {
            CheckPairObservations(_request.SnapshotFingerprint, first, second);
            token.ThrowIfCancellationRequested();
            if (!CanGroupDenseBatches) throw new InvalidOperationException("PartyN.DensePairNotAdmitted");
            if (!UsesFused) return await PreparePrivateDensePairAsync(context, first, second, token).ConfigureAwait(false);
            _compact = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await context.ExecuteGpuAsync(rd =>
                {
                    _fused ??= CreateFused(rd);
                    _pairFused ??= CreateFused(rd, _fused);
                    return true;
                }, token).ConfigureAwait(false);
                try
                {
                    var pair = await context.ExecuteGpuAsync(_ => NeowFamilyGpuExecutor.ExecuteDensePair(
                        _fused!, first.Batch, _pairFused!, second.Batch, token), token).ConfigureAwait(false);
                    _densePairGroups++;
                    var left = pair.Left ?? throw new InvalidDataException("PartyN.DensePairMissingPublicLeft");
                    ValidatePrivateParity(FamilyCandidateSet.Dense(first.Batch), left);
                    try
                    {
                        var right = pair.Right ?? throw new InvalidDataException("PartyN.DensePairMissingPublicRight");
                        ValidatePrivateParity(FamilyCandidateSet.Dense(second.Batch), right, force: !_pairFusedParity);
                        _pairFusedParity |= second.Batch.BatchCandidateCount > 0;
                        Volatile.Write(ref _lastDenseOutputCount, right.Count);
                        double elapsed = watch.Elapsed.TotalMilliseconds;
                        ObservePreparedPair(first.Batch.BatchCandidateCount + second.Batch.BatchCandidateCount,
                            left.Count + right.Count, 2, elapsed, pair.SharedMs,
                            pair.LeftMetrics.ReadbackBytes + pair.RightMetrics.ReadbackBytes);
                        if (_pairLiveGroups++ > 0)
                        {
                            Interlocked.Add(ref _liveInputs, first.Batch.BatchCandidateCount + second.Batch.BatchCandidateCount);
                            _liveMs += elapsed; _liveBatches += 2;
                        }
                        LogDensePair(first.Batch, second.Batch, pair.SharedMs, pair.LeftMetrics.Dispatches + pair.RightMetrics.Dispatches);
                        return new(PreparedFamilyBatch.Ready(this, left), PreparedFamilyBatch.Ready(this, right));
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException)
                    {
                        ObservePreparedPair(first.Batch.BatchCandidateCount, left.Count, 1, watch.Elapsed.TotalMilliseconds,
                            pair.SharedMs, pair.LeftMetrics.ReadbackBytes + pair.RightMetrics.ReadbackBytes);
                        return FailedFusedRight(left, second.Batch, failure);
                    }
                }
                catch (NeowDensePairRightFailure failure)
                {
                    if (failure.InnerException is OperationCanceledException canceled) ExceptionDispatchInfo.Capture(canceled).Throw();
                    _densePairGroups++;
                    var left = failure.Left ?? throw new InvalidDataException("PartyN.DensePairMissingPublicLeft", failure);
                    ValidatePrivateParity(FamilyCandidateSet.Dense(first.Batch), left);
                    ObservePreparedPair(first.Batch.BatchCandidateCount, left.Count, 1, watch.Elapsed.TotalMilliseconds,
                        failure.SharedMs, failure.LeftMetrics.ReadbackBytes);
                    return FailedFusedRight(left, failure.RightBatch, failure.InnerException ?? failure);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure)
            {
                _groupingDisabled = true;
                LogPairFailure(FamilyId, first.Batch, second.Batch, failure);
                throw;
            }
        }
        private DenseFamilyBatchGroup FailedFusedRight(FamilyCandidateSet left, SearchBatch right, Exception failure)
        {
            LogPairFailure(FamilyId, left.Batch, right, failure);
            return new(PreparedFamilyBatch.Ready(this, left),
                PreparedFamilyBatch.Failed(this, right, failure, () => _groupingDisabled = true));
        }
        private void ObservePreparedPair(int inputs, int outputs, int batches, double elapsed, double shared, long readbackBytes)
        {
            _inputs += inputs; _outputs += outputs; _batches += batches; _ms += elapsed;
            _fusedReadback += readbackBytes; _fusedDispatch += shared;
        }
        private void ValidatePrivateParity(FamilyCandidateSet input, FamilyCandidateSet result, bool force = false)
        {
            if (input.Count > 0 && (!_privateParity || force))
            {
                var accepted = result.ExportAbi1().ToArray();
                foreach (ulong ordinal in input.EnumerateLogicalOrdinals().Take(256))
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, input.Batch.GlobalCandidate(ordinal)));
                    bool expected = _gpuStages.All(s => s.Family switch
                    {
                        NeowFamily n => NeowPartyReplay.Matches(root, n.PricingReplayPlan),
                        CapsuleRelicFamily r => r.MatchesReference(root),
                        _ => throw new InvalidOperationException("PartyN.PrivateReferenceMissing")
                    });
                    if (expected != (Array.BinarySearch(accepted, ordinal) >= 0)) throw new InvalidDataException("PartyN.PrivateReferenceParity");
                }
                _privateParity = true;
            }
        }

    }

}
