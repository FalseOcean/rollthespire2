using System.Diagnostics;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class FamilyPrivateGpuChain
{
    // Only the Dense first N is prepared. Each chain retains its original lazy
    // suffix and independent ping-pong buffers until that root is consumed.
    internal bool CanGroupDensePrefix => !_disposed && _families[0] is NeowFamily { CanGroupDenseBatches: true };
    internal NeowFamilyGpuExecutor DensePrefixExecutor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CheckOwner();
            if (!CanGroupDensePrefix) throw new InvalidOperationException("FamilyParty.DensePrefixNotAdmitted");
            return EnsureExecutor(0, false).NeowExecutor ?? throw new InvalidOperationException("FamilyParty.DenseNeowBindingMissing");
        }
    }
    internal static (FamilyPrivateGpuOutcome Left, FamilyPrivateGpuOutcome? Right, Exception? Failure, double ElapsedMs)
        PrepareDensePair(FamilyPrivateGpuChain first, SearchBatch firstBatch,
            FamilyPrivateGpuChain second, SearchBatch secondBatch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var left = first.DensePrefixExecutor;
        var right = second.DensePrefixExecutor;
        var leftBinding = first.EnsureExecutor(0, false);
        var rightBinding = second.EnsureExecutor(0, false);
        var timer = Stopwatch.StartNew();
        FamilyPrivateGpuOutcome Finish(FamilyPrivateGpuExecution binding, SearchBatch batch, NeowFamilyGpuMetrics metrics) =>
            binding.CompletePrepared(batch, batch.BatchCandidateCount, new(metrics.Survivors, null, 0,
                metrics.ReadbackMs, metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches) { SmallOutput = metrics.SmallOutput });
        try
        {
            var pair = NeowFamilyGpuExecutor.ExecuteDensePair(left, firstBatch, right, secondBatch, token);
            first.DispatchMs += pair.SharedMs;
            first.ReadbackBytes += pair.LeftMetrics.ReadbackBytes;
            second.ReadbackBytes += pair.RightMetrics.ReadbackBytes;
            var leftOutcome = Finish(leftBinding, firstBatch, pair.LeftMetrics);
            try
            {
                var rightOutcome = Finish(rightBinding, secondBatch, pair.RightMetrics);
                return (leftOutcome, rightOutcome, null, timer.Elapsed.TotalMilliseconds);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                LogRightFailure(firstBatch, secondBatch, failure);
                return (leftOutcome, null, failure, timer.Elapsed.TotalMilliseconds);
            }
        }
        catch (NeowDensePairRightFailure failure)
        {
            first.DispatchMs += failure.SharedMs;
            first.ReadbackBytes += failure.LeftMetrics.ReadbackBytes;
            var leftOutcome = Finish(leftBinding, firstBatch, failure.LeftMetrics);
            var original = failure.InnerException ?? failure;
            LogRightFailure(firstBatch, secondBatch, original);
            return (leftOutcome, null, original, timer.Elapsed.TotalMilliseconds);
        }
    }
    private static void LogRightFailure(SearchBatch first, SearchBatch second, Exception failure) =>
        RuntimeLog.TryBackgroundWarning($"rootGroupSlotFault=true;family=PartyPrivate.N;slot=1;firstBatch={first.BatchBase};batch={second.BatchBase};deferredUntilFrontier=true;exceptionJson=" +
            RuntimeLog.SafeJson(failure.ToString()));
}
