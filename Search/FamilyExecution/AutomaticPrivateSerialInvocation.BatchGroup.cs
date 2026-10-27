using System.Diagnostics;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class AutomaticPrivateSerialInvocation : IDenseFamilyBatchGrouping
{
    private Gpu? _pairGpu;
    private long _groupedPrefixRoots;
    private int _prefixGroups;
    private int _lastDensePrefixCount = -1;

    bool IDenseFamilyBatchGrouping.CanGroupDenseBatches => !_disposed && !_faulted && !_verify &&
        _families[0] is NeowFamily { CanGroupDenseBatches: true };
    bool IDenseFamilyBatchGrouping.PreferDenseBatchGrouping =>
        ((IDenseFamilyBatchGrouping)this).CanGroupDenseBatches && _smallCandidateExact is not null &&
        _families[0] is NeowFamily { UsesSmallPrivateOutput: true } &&
        Volatile.Read(ref _lastDensePrefixCount) is >= 0 and <= SmallCandidateExactPolicy.MaximumCandidates;

    async ValueTask<DenseFamilyBatchGroup> IDenseFamilyBatchGrouping.PrepareDensePairAsync(
        FamilyExecutionContext context, FamilyObservationWindow first, FamilyObservationWindow second, CancellationToken token)
    {
        if (!((IDenseFamilyBatchGrouping)this).CanGroupDenseBatches ||
            first.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint ||
            second.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
            throw new InvalidOperationException("AutomaticPrivateSerial.DensePairInvariant");
        try
        {
            var prefix = await context.ExecuteGpuAsync(rd =>
            {
                _gpu ??= new Gpu(rd, _families);
                var left = _gpu.EnsureExecutor(0);
                _pairGpu ??= new Gpu(rd, _families, left.NeowExecutor ??
                    throw new InvalidOperationException("AutomaticPrivateSerial.NeowGroupBindingMissing"));
                var right = _pairGpu.EnsureExecutor(0);
                var timer = Stopwatch.StartNew();
                try
                {
                    var run = NeowFamilyGpuExecutor.ExecuteDensePair(left.NeowExecutor!, first.Batch,
                        right.NeowExecutor!, second.Batch, token);
                    var leftOutcome = left.CompletePrepared(first.Batch, first.Batch.BatchCandidateCount, Outcome(run.LeftMetrics));
                    try
                    {
                        return (Left: leftOutcome,
                            Right: (FamilyPrivateGpuOutcome?)right.CompletePrepared(second.Batch, second.Batch.BatchCandidateCount, Outcome(run.RightMetrics)),
                            Failure: (Exception?)null, Elapsed: timer.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException)
                    {
                        RuntimeLog.TryBackgroundWarning($"rootGroupSlotFault=true;family=N;slot=1;batch={second.Batch.BatchBase};stage=CompletedPrefixInvariant;cause={RuntimeLog.SafeJson(failure.ToString())};deferredUntilFrontier=true");
                        return (Left: leftOutcome, Right: (FamilyPrivateGpuOutcome?)null,
                            Failure: (Exception?)failure, Elapsed: timer.Elapsed.TotalMilliseconds);
                    }
                }
                catch (NeowDensePairRightFailure failure)
                {
                    RuntimeLog.TryBackgroundWarning($"rootGroupSlotFault=true;family=N;slot=1;batch={second.Batch.BatchBase};cause={RuntimeLog.SafeJson(failure.InnerException!.ToString())};deferredUntilFrontier=true");
                    return (Left: left.CompletePrepared(first.Batch, first.Batch.BatchCandidateCount, Outcome(failure.LeftMetrics)),
                        Right: (FamilyPrivateGpuOutcome?)null, Failure: failure.InnerException, Elapsed: timer.Elapsed.TotalMilliseconds);
                }
            }, token).ConfigureAwait(false);
            _prefixGroups++;
            _groupedPrefixRoots += first.Batch.BatchCandidateCount + (long)second.Batch.BatchCandidateCount;
            _ms += prefix.Elapsed; // One physical group, including both roots, once.
            RuntimeLog.TryBackgroundDetail($"rootGroupPrepared=true;family=AutomaticPrivateSerial;order={Order};roots=2;first={first.Batch.BatchBase};second={second.Batch.BatchBase};prefixMs={prefix.Elapsed};groups={_prefixGroups};preparedRoots={_groupedPrefixRoots};localCalibration=false");
            PreparedFamilyBatch Member(FamilyObservationWindow window, Gpu gpu, FamilyPrivateGpuOutcome outcome) =>
                new(this, window.Batch, async cancellation =>
                {
                    try
                    {
                        var result = await context.ExecuteGpuAsync(_ => gpu.Execute(FamilyCandidateSet.Dense(window.Batch),
                            Order, false, _smallCandidateExact, cancellation, outcome), cancellation).ConfigureAwait(false);
                        Volatile.Write(ref _lastDensePrefixCount, outcome.Count);
                        _batches++; _inputs += window.Batch.BatchCandidateCount; _outputs += result.Output.Count; _ms += result.Ms;
                        return result.Output;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { _faulted = true; throw; }
                });
            return new(Member(first, _gpu!, prefix.Left), prefix.Failure is null
                ? Member(second, _pairGpu!, prefix.Right!.Value)
                : PreparedFamilyBatch.Failed(this, second.Batch, prefix.Failure, () => _faulted = true));
        }
        catch (OperationCanceledException) { throw; }
        catch { _faulted = true; throw; }
    }

    private static FamilyPrivateGpuOutcome Outcome(NeowFamilyGpuMetrics metrics) =>
        new(metrics.Survivors, null, 0, metrics.ReadbackMs, metrics.ReadbackBytes,
            metrics.CanonicalMs, metrics.Dispatches) { SmallOutput = metrics.SmallOutput };
}
