using System.Diagnostics;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamily
{
    internal sealed partial class PartyInitial
    {
        private FamilyPrivateGpuChain? _privatePairChain;
        private bool _privatePairDisposed, _privatePairParity;
        private int _privateDensePairGroups;
        private bool CanGroupPrivateDenseBatches => !_privatePairDisposed && !UsesFused && UsesPrivate &&
            _gpuStages[0].Family is NeowFamily { CanGroupDenseBatches: true } &&
            _gpuStages.All(stage => !stage.Family.ExecutionFaulted);

        private async ValueTask<DenseFamilyBatchGroup> PreparePrivateDensePairAsync(FamilyExecutionContext context,
            FamilyObservationWindow first, FamilyObservationWindow second, CancellationToken token)
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
                        _gpuStages.Select(stage => stage.Family).ToArray(), leftChain.DensePrefixExecutor);
                    return FamilyPrivateGpuChain.PrepareDensePair(leftChain, first.Batch, rightChain, second.Batch, token);
                }, token).ConfigureAwait(false);
                bool includeLive = _privateDensePairGroups++ > 0;
                _densePairGroups++;
                _ms += prefix.ElapsedMs; // Both prefix roots share this physical wait once.
                RuntimeLog.TryBackgroundDetail($"rootGroupPrepared=true;family=PartyInitial.Private;first={first.Batch.BatchBase};second={second.Batch.BatchBase};groups={_privateDensePairGroups};prefixMs={prefix.ElapsedMs};localCalibration=false;smallCandidateExactPolicy=UnchangedDisabled");
                PreparedFamilyBatch Member(FamilyObservationWindow observation, FamilyPrivateGpuChain chain,
                    FamilyPrivateGpuOutcome outcome, bool right) => new(this, observation.Batch, async cancellation =>
                {
                    try
                    {
                        var result = await context.ExecuteGpuAsync(_ =>
                        {
                            var timer = Stopwatch.StartNew();
                            // PartyInitial did not enable the chain's small-Exact
                            // policy before grouping; leave that rule unchanged.
                            var output = chain.Execute(FamilyCandidateSet.Dense(observation.Batch), cancellation, preparedPrefix: outcome);
                            return (Output: output, Ms: timer.Elapsed.TotalMilliseconds);
                        }, cancellation).ConfigureAwait(false);
                        try
                        {
                            ValidatePrivateParity(FamilyCandidateSet.Dense(observation.Batch), result.Output,
                                force: right && !_privatePairParity);
                        }
                        catch (Exception failure) when (right && failure is not OperationCanceledException)
                        {
                            // The suffix returned normally, so this host parity
                            // failure is isolated to the completed later root.
                            LogPairFailure(FamilyId, first.Batch, second.Batch, failure);
                            throw new DeferredRootBatchFailure(observation.Batch, failure);
                        }
                        if (right) _privatePairParity |= observation.Batch.BatchCandidateCount > 0;
                        _inputs += observation.Batch.BatchCandidateCount; _outputs += result.Output.Count;
                        _batches++; _ms += result.Ms;
                        if (includeLive)
                        {
                            Interlocked.Add(ref _liveInputs, observation.Batch.BatchCandidateCount);
                            _liveMs += result.Ms + (right ? 0 : prefix.ElapsedMs); _liveBatches++;
                        }
                        return result.Output;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { _groupingDisabled = true; throw; }
                });
                return new(Member(first, _chain!, prefix.Left, right: false), prefix.Failure is null
                    ? Member(second, _privatePairChain!, prefix.Right!.Value, right: true)
                    : PreparedFamilyBatch.Failed(this, second.Batch, prefix.Failure, () => _groupingDisabled = true));
            }
            catch (OperationCanceledException) { throw; }
            catch { _groupingDisabled = true; throw; }
        }
    }
}
