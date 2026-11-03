using System.Diagnostics;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowPartyFilterExecution
{
        private FamilyPrivateGpuChain? _privatePairChain;
        private bool _privatePairDisposed, _privatePairParity;
        private int _privateDensePairGroups;
        private bool CanGroupPrivateDenseBatches => !_privatePairDisposed && !UsesFused && UsesPrivate &&
            _executions[0] is IDenseFamilyBatchGrouping { CanGroupDenseBatches: true } &&
            _executions.All(stage => !stage.ExecutionFaulted);

        private async ValueTask<DenseFamilyBatchGroup> PreparePrivateDensePairAsync(FilterRuntimeServices context,
            SearchBatch first, SearchBatch second, CancellationToken token)
        {
            _compact = false;
            try
            {
                var prefix = await context.ExecuteGpuAsync(rd =>
                {
                    // Freeze the existing slot/predicate sequence. Sharing only N's
                    // staged scratch does not share either root's output or suffix.
                    var leftChain = _chain ??= new FamilyPrivateGpuChain(rd, _gpuStages.Select(stage => stage.Family).ToArray());
                    var rightChain = _privatePairChain ??= new FamilyPrivateGpuChain(rd,
                        _gpuStages.Select(stage => stage.Family).ToArray(), leftChain.DensePrefixExecutor.ScratchLease);
                    return FamilyPrivateGpuChain.PrepareDensePair(leftChain, first, rightChain, second, token);
                }, token).ConfigureAwait(false);
                bool includeLive = _privateDensePairGroups++ > 0;
                _densePairGroups++;
                _ms += prefix.ElapsedMs; // Both prefix roots share this physical wait once.
                RuntimeLog.TryBackgroundDetail($"rootGroupPrepared=true;family=PartyInitial.Private;first={first.BatchBase};second={second.BatchBase};groups={_privateDensePairGroups};prefixMs={prefix.ElapsedMs};localCalibration=false;smallCandidateExactPolicy=UnchangedDisabled");
                PreparedFamilyBatch Member(SearchBatch observation, FamilyPrivateGpuChain chain,
                    FamilyPrivateGpuOutcome outcome, bool right) => new(this, observation, async cancellation =>
                {
                    try
                    {
                        var result = await context.ExecuteGpuAsync(_ =>
                        {
                            var timer = Stopwatch.StartNew();
                            // PartyInitial did not enable the chain's small-Exact
                            // policy before grouping; leave that rule unchanged.
                            var output = chain.Execute(FamilyCandidateSet.Dense(observation), cancellation, preparedPrefix: outcome);
                            return (Output: output, Ms: timer.Elapsed.TotalMilliseconds);
                        }, cancellation).ConfigureAwait(false);
                        try
                        {
                            ValidatePrivateParity(FamilyCandidateSet.Dense(observation), result.Output,
                                force: right && !_privatePairParity);
                        }
                        catch (Exception failure) when (right && failure is not OperationCanceledException)
                        {
                            // The suffix returned normally, so this host parity
                            // failure is isolated to the completed later root.
                            LogPairFailure(FamilyId, first, second, failure);
                            throw new DeferredRootBatchFailure(observation, failure);
                        }
                        if (right) _privatePairParity |= observation.BatchCandidateCount > 0;
                        _inputs += observation.BatchCandidateCount; _outputs += result.Output.Count;
                        _batches++; _ms += result.Ms;
                        if (includeLive)
                        {
                            Interlocked.Add(ref _liveInputs, observation.BatchCandidateCount);
                            _liveMs += result.Ms + (right ? 0 : prefix.ElapsedMs); _liveBatches++;
                        }
                        context.Costs.ObserveCompletedGroup(Body, false, observation.BatchCandidateCount, result.Output.Count,
                            1, result.Ms + (right ? 0 : prefix.ElapsedMs), includeLive);
                        return result.Output;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { _groupingDisabled = true; throw; }
                });
                return new(Member(first, _chain!, prefix.Left, right: false), prefix.Failure is null
                    ? Member(second, _privatePairChain!, prefix.Right!.Value, right: true)
                    : PreparedFamilyBatch.Failed(this, second, prefix.Failure, () => _groupingDisabled = true));
            }
            catch (OperationCanceledException) { throw; }
            catch { _groupingDisabled = true; throw; }
        }
}
