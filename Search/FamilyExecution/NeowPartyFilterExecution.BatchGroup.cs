using System.Runtime.ExceptionServices;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowPartyFilterExecution
{
    private static void CheckPairObservations(SearchBatch first, SearchBatch second)
    {
        if (first == second) throw new InvalidOperationException("PartyN.DensePairObservationContextMismatch");
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

        private NeowFamilyGpuExecutor? _pairFused;
        private bool _pairFusedParity, _groupingDisabled;
        private int _densePairGroups, _pairLiveGroups, _liveBatches;
        private int _lastDenseOutputCount = -1;
        internal bool CanGroupDenseBatches => !_privatePairDisposed && !_groupingDisabled && (CanGroupPrivateDenseBatches || UsesFused &&
            _executions.All(stage => stage is IDenseFamilyBatchGrouping { CanGroupDenseBatches: true }));
        bool IDenseFamilyBatchGrouping.CanGroupDenseBatches => CanGroupDenseBatches;
        // Private PartyInitial retains its original complete suffix policy.
        // Public fused small populations have the same CPU boundary as solo N.
        bool IDenseFamilyBatchGrouping.PreferDenseBatchGrouping => CanGroupDenseBatches && UsesFused && !_compact && !_skipExactValidation &&
            Volatile.Read(ref _lastDenseOutputCount) is >= 0 and <= SmallCandidateExactPolicy.MaximumCandidates;
        private NeowFamilyGpuExecutor CreateFused(Godot.RenderingDevice rd, NeowFamilyGpuExecutor? donor = null) =>
            new(rd, _gpuStages[0].Gpu!, 43,
                partyPlans: _gpuStages.Select(stage => stage.Gpu!).ToArray(), sharedDenseScratch: donor);

        async ValueTask<DenseFamilyBatchGroup> IDenseFamilyBatchGrouping.PrepareDensePairAsync(FilterRuntimeServices context,
            SearchBatch first, SearchBatch second, CancellationToken token)
        {
            CheckPairObservations(first, second);
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
                        _fused!, first, _pairFused!, second, token), token).ConfigureAwait(false);
                    _densePairGroups++;
                    var left = pair.Left ?? throw new InvalidDataException("PartyN.DensePairMissingPublicLeft");
                    ValidatePrivateParity(FamilyCandidateSet.Dense(first), left);
                    try
                    {
                        var right = pair.Right ?? throw new InvalidDataException("PartyN.DensePairMissingPublicRight");
                        ValidatePrivateParity(FamilyCandidateSet.Dense(second), right, force: !_pairFusedParity);
                        _pairFusedParity |= second.BatchCandidateCount > 0;
                        Volatile.Write(ref _lastDenseOutputCount, right.Count);
                        double elapsed = watch.Elapsed.TotalMilliseconds;
                        ObservePreparedPair(first.BatchCandidateCount + second.BatchCandidateCount,
                            left.Count + right.Count, 2, elapsed, pair.SharedMs,
                            pair.LeftMetrics.ReadbackBytes + pair.RightMetrics.ReadbackBytes);
                        bool includeLive = _pairLiveGroups++ > 0;
                        if (includeLive)
                        {
                            Interlocked.Add(ref _liveInputs, first.BatchCandidateCount + second.BatchCandidateCount);
                            _liveMs += elapsed; _liveBatches += 2;
                        }
                        context.Costs.ObserveCompletedGroup(Body, false,
                            first.BatchCandidateCount + second.BatchCandidateCount, left.Count + right.Count,
                            2, elapsed, includeLive);
                        LogDensePair(first, second, pair.SharedMs, pair.LeftMetrics.Dispatches + pair.RightMetrics.Dispatches);
                        return new(PreparedFamilyBatch.Ready(this, left), PreparedFamilyBatch.Ready(this, right));
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException)
                    {
                        ObservePreparedPair(first.BatchCandidateCount, left.Count, 1, watch.Elapsed.TotalMilliseconds,
                            pair.SharedMs, pair.LeftMetrics.ReadbackBytes + pair.RightMetrics.ReadbackBytes);
                        context.Costs.ObserveCompletedGroup(Body, false, first.BatchCandidateCount, left.Count,
                            1, pair.SharedMs + pair.LeftMetrics.CanonicalMs, includeLive: false);
                        return FailedFusedRight(left, second, failure);
                    }
                }
                catch (NeowDensePairRightFailure failure)
                {
                    if (failure.InnerException is OperationCanceledException canceled) ExceptionDispatchInfo.Capture(canceled).Throw();
                    _densePairGroups++;
                    var left = failure.Left ?? throw new InvalidDataException("PartyN.DensePairMissingPublicLeft", failure);
                    ValidatePrivateParity(FamilyCandidateSet.Dense(first), left);
                    ObservePreparedPair(first.BatchCandidateCount, left.Count, 1, watch.Elapsed.TotalMilliseconds,
                        failure.SharedMs, failure.LeftMetrics.ReadbackBytes);
                    context.Costs.ObserveCompletedGroup(Body, false, first.BatchCandidateCount, left.Count,
                        1, failure.SharedMs + failure.LeftMetrics.CanonicalMs, includeLive: false);
                    return FailedFusedRight(left, failure.RightBatch, failure.InnerException ?? failure);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure)
            {
                _groupingDisabled = true;
                LogPairFailure(FamilyId, first, second, failure);
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
                    bool expected = _gpuStages.All(stage => NeowPartyReplay.Matches(root, stage.Replay));
                    if (expected != (Array.BinarySearch(accepted, ordinal) >= 0)) throw new InvalidDataException("PartyN.PrivateReferenceParity");
                }
                _privateParity = true;
            }
        }

}
