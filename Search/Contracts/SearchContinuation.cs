namespace RolltheSpire2.Search.Contracts;

// A failed attempt is retained work, not a model disposition or a result.
public sealed record SearchPendingCandidate(
    ulong Ordinal, string Seed, string Stage, string FailureCode, string ExceptionText,
    string QueryFingerprint, string SnapshotFingerprint, DateTimeOffset FirstFailedAtUtc,
    int Attempts = 1)
{
    public string ContinuationFingerprint { get; init; } = "";
}

// Persist this together with received results before committing NextOrdinal.
// The frontier accounts for retained pending work; it does not prove Match/NoMatch.
public sealed record SearchContinuationSnapshot(
    string SemanticFingerprint, string SnapshotFingerprint, ulong NextOrdinal,
    IReadOnlyList<SearchPendingCandidate> PendingCandidates,
    IReadOnlyList<ulong> PublishedOrdinals)
{
    // Absent in older files: legacy recovery still needs its exact original
    // attempt identity and must not borrow today's request/environment.
    public string ContinuationFingerprint { get; init; } = "";

    // Null denotes a legacy snapshot whose unreceived deliveries are represented
    // only by NextOrdinal. New snapshots retain every delivery independently of
    // the ordinary scan frontier, including after an explicit origin change.
    public IReadOnlyList<ulong>? UnreceivedOrdinals { get; init; }
}
