using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// A certificate for a failed pure matcher, not for a root/identity/ABI/device failure.
internal sealed class FamilyCpuMatchException(string familyId, SearchBatch batch,
    ulong logicalOrdinal, ulong globalOrdinal, ulong rootHash, Exception cause)
    : Exception("FamilyCpuMatchFailed:" + familyId + ":" + globalOrdinal, cause)
{
    internal string FamilyId { get; } = familyId;
    internal SearchBatch Batch { get; } = batch;
    internal ulong LogicalOrdinal { get; } = logicalOrdinal;
    internal ulong GlobalOrdinal { get; } = globalOrdinal;
    internal ulong RootHash { get; } = rootHash;

    internal static bool IsRecoverable(Exception error) => error is FamilyCpuMatchException ||
        error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Count > 0 &&
            aggregate.Flatten().InnerExceptions.All(e => e is FamilyCpuMatchException);

    internal static IReadOnlyList<FamilyCpuMatchException> Matches(Exception error) =>
        error is FamilyCpuMatchException match ? [match] :
        ((AggregateException)error).Flatten().InnerExceptions.Cast<FamilyCpuMatchException>().ToArray();
}

internal sealed record FamilyFilterIsolation(
    string FamilyId,
    string Stage,
    SearchBatch Batch,
    SearchBatch FailureBatch,
    IReadOnlyList<ulong> FailedGlobalOrdinals,
    IReadOnlyList<string> UnfilteredCoverage,
    int RestoredInputCount,
    bool FinalValidationSkipped,
    Exception Error);
