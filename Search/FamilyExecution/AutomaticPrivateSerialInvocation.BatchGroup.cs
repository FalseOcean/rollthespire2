using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class AutomaticPrivateSerialInvocation
{
    private sealed partial class Runtime
    {
        private Gpu? _pairGpu;
        private long _groupedPrefixRoots;
        private int _prefixGroups;
        private int _lastDensePrefixCount = -1;
        bool IDenseFamilyBatchGrouping.CanGroupDenseBatches => !_disposed && !_faulted && !_verify &&
            _families[0].SupportsDenseBatchGrouping;
        bool IDenseFamilyBatchGrouping.PreferDenseBatchGrouping =>
            ((IDenseFamilyBatchGrouping)this).CanGroupDenseBatches && _smallCandidateExact is not null &&
            _families[0].UsesSmallPrivateOutput &&
            Volatile.Read(ref _lastDensePrefixCount) is >= 0 and <= SmallCandidateExactPolicy.MaximumCandidates;

        async ValueTask<DenseFamilyBatchGroup> IDenseFamilyBatchGrouping.PrepareDensePairAsync(
            FilterRuntimeServices services, SearchBatch first, SearchBatch second, CancellationToken token)
        {
            if (!((IDenseFamilyBatchGrouping)this).CanGroupDenseBatches)
                throw new InvalidOperationException("AutomaticPrivateSerial.DensePairInvariant");
            try {
                var prefix = await services.ExecuteGpuAsync(rd => {
                    _gpu ??= new Gpu(rd, _families, services.Costs);
                    var left = _gpu.EnsureExecutor(0);
                    _pairGpu ??= new Gpu(rd, _families, services.Costs, left.ScratchLease ??
                        throw new InvalidOperationException("AutomaticPrivateSerial.DenseScratchLeaseMissing"));
                    var right = _pairGpu.EnsureExecutor(0);
                    var pair = (left.DensePair ?? throw new InvalidOperationException("AutomaticPrivateSerial.DensePairBindingMissing"))
                        (right, first, second, token);
                    var leftOutcome = left.CompletePrepared(first, first.BatchCandidateCount, pair.Left);
                    if (pair.Failure is { } original) {
                        LogFailure(original, second);
                        return (Left: leftOutcome, Right: (FamilyPrivateGpuOutcome?)null, Failure: original, Elapsed: pair.ElapsedMs);
                    }
                    try {
                        var rightOutcome = right.CompletePrepared(second, second.BatchCandidateCount,
                            pair.Right ?? throw new InvalidDataException("AutomaticPrivateSerial.DensePairRightOutcomeMissing"));
                        return (Left: leftOutcome, Right: (FamilyPrivateGpuOutcome?)rightOutcome, Failure: (Exception?)null, Elapsed: pair.ElapsedMs);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException) {
                        LogFailure(failure, second);
                        return (Left: leftOutcome, Right: (FamilyPrivateGpuOutcome?)null, Failure: (Exception?)failure, Elapsed: pair.ElapsedMs);
                    }
                }, token).ConfigureAwait(false);
                bool includeLive = _prefixGroups++ > 0;
                _groupedPrefixRoots += first.BatchCandidateCount + (long)second.BatchCandidateCount;
                _ms += prefix.Elapsed;
                RuntimeLog.TryBackgroundDetail($"rootGroupPrepared=true;family=AutomaticPrivateSerial;order={Order};roots=2;first={first.BatchBase};second={second.BatchBase};prefixMs={prefix.Elapsed};groups={_prefixGroups};preparedRoots={_groupedPrefixRoots};localCalibration=false");
                PreparedFamilyBatch Member(SearchBatch batch, Gpu gpu, FamilyPrivateGpuOutcome outcome, double groupMs) =>
                    new(this, batch, async cancellation => {
                        try {
                            var input = FamilyCandidateSet.Dense(batch);
                            var result = await services.ExecuteGpuAsync(_ => gpu.Execute(input, Order, false,
                                _smallCandidateExact, FollowingCoverage, cancellation, outcome), cancellation).ConfigureAwait(false);
                            Volatile.Write(ref _lastDensePrefixCount, outcome.Count);
                            _batches++; _inputs += batch.BatchCandidateCount; _outputs += result.Output.Count; _ms += result.Ms;
                            services.Costs.ObserveCompletedGroup(Body, false, input.Count, result.Output.Count,
                                1, result.Ms + groupMs, includeLive);
                            return result.Output;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { _faulted = true; throw; }
                    });
                return new(Member(first, _gpu!, prefix.Left, prefix.Elapsed), prefix.Failure is null
                    ? Member(second, _pairGpu!, prefix.Right!.Value, 0)
                    : PreparedFamilyBatch.Failed(this, second, prefix.Failure, () => _faulted = true));
            }
            catch (OperationCanceledException) { throw; }
            catch { _faulted = true; throw; }
        }
        private static void LogFailure(Exception failure, SearchBatch second) =>
            RuntimeLog.TryBackgroundWarning($"rootGroupSlotFault=true;family=PrivatePrefix;slot=1;batch={second.BatchBase};cause={RuntimeLog.SafeJson(failure.ToString())};deferredUntilFrontier=true");
    }
}
