using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class FamilyPrivateGpuChain
{
    internal bool CanGroupDensePrefix => !_disposed && _families[0].SupportsDenseBatchGrouping;
    internal FamilyPrivateGpuExecution DensePrefixExecutor
    {
        get {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CheckOwner();
            if (!CanGroupDensePrefix) throw new InvalidOperationException("FamilyParty.DensePrefixNotAdmitted");
            return EnsureExecutor(0, false);
        }
    }
    internal static (FamilyPrivateGpuOutcome Left, FamilyPrivateGpuOutcome? Right, Exception? Failure, double ElapsedMs)
        PrepareDensePair(FamilyPrivateGpuChain first, SearchBatch firstBatch,
            FamilyPrivateGpuChain second, SearchBatch secondBatch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var left = first.DensePrefixExecutor;
        var right = second.DensePrefixExecutor;
        var pair = (left.DensePair ?? throw new InvalidOperationException("FamilyParty.DensePairBindingUnavailable"))(right, firstBatch, secondBatch, token);
        first.DispatchMs += pair.SharedDispatchMs;
        first.ReadbackBytes += pair.Left.ReadbackBytes;
        second.ReadbackBytes += pair.Right?.ReadbackBytes ?? 0;
        var leftOutcome = left.CompletePrepared(firstBatch, firstBatch.BatchCandidateCount, pair.Left);
        if (pair.Failure is { } original) {
            LogRightFailure(firstBatch, secondBatch, original);
            return (leftOutcome, null, original, pair.ElapsedMs);
        }
        try {
            var rightOutcome = right.CompletePrepared(secondBatch, secondBatch.BatchCandidateCount,
                pair.Right ?? throw new InvalidDataException("FamilyParty.DensePairRightOutcomeMissing"));
            return (leftOutcome, rightOutcome, null, pair.ElapsedMs);
        }
        catch (Exception failure) when (failure is not OperationCanceledException) {
            LogRightFailure(firstBatch, secondBatch, failure);
            return (leftOutcome, null, failure, pair.ElapsedMs);
        }
    }
    private static void LogRightFailure(SearchBatch first, SearchBatch second, Exception failure) =>
        RuntimeLog.TryBackgroundWarning($"rootGroupSlotFault=true;family=PartyPrivate.Prefix;slot=1;firstBatch={first.BatchBase};batch={second.BatchBase};deferredUntilFrontier=true;exceptionJson=" +
            RuntimeLog.SafeJson(failure.ToString()));
}
