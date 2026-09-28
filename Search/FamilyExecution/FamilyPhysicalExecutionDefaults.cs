namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Default physical sizing for Family implementations. This is an implementation
/// policy, not a SearchBatch, LogicalOrdinal, or ABI1 invariant; an individual
/// Family may select different geometry when runtime evidence requires it.
/// </summary>
internal static class FamilyPhysicalExecutionDefaults
{
    internal const int DefaultCandidateBatchSize = 1 << 24;
}
