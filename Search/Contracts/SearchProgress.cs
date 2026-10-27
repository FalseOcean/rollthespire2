namespace RolltheSpire2.Search.Contracts;

public enum SearchRunState
{
    Idle,
    Running,
    Completed,
    Cancelled,
    Faulted
}

public sealed record SearchProgressSnapshot(
    SearchRunState State,
    long ScannedCount,
    int MatchCount,
    long RequestedScanCount,
    int TargetMatchCount,
    double ElapsedSeconds,
    double SeedsPerSecond,
    SearchDisposition LastDisposition,
    string FailureCode,
    bool CancellationRequested)
{
    public int UnverifiedCandidateCount { get; init; }
    public int PendingCandidateCount { get; init; }
    public string CleanupIssue { get; init; } = "";
    public IReadOnlyList<string> ExecutionWarnings { get; init; } = [];
    public int DeliveredCandidateCount => MatchCount + UnverifiedCandidateCount;
    // UI observation only; never a pricing/calibration denominator.
    public double ObservedScanningSeconds { get; init; }
    public long ObservedScanningRoots { get; init; }
}
