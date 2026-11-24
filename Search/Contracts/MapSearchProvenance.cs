namespace RolltheSpire2.Search.Contracts;

// A source interval and the execution policy selected for it, not proof that
// every ordinal reached M. Early Exact handoffs can bypass a selected family.
public sealed record MapSearchProvenance(
    MapLossyOptions RequestedOptions, bool Applied, string Backend, string Strategy,
    string Reason, string Scope, IReadOnlyList<string> FullFinalRemainders,
    ulong StartOrdinal, ulong EndOrdinalExclusive, string AttemptFingerprint)
{
    public long ObservedProposalInputs { get; init; }
    public long ObservedProposalCalls { get; init; }
}
