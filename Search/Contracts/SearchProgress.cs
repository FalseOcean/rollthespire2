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
    public long ModeledNonHitCount { get; init; }
    public int ModeledNonHitSampleCount { get; init; }
    public bool ModeledNonHitSampleReceived { get; init; }
    public string ReplayAssumptions { get; init; } = "";
    public int PendingCandidateCount { get; init; }
    public string CleanupIssue { get; init; } = "";
    public IReadOnlyList<string> ExecutionWarnings { get; init; } = [];
    public int DeliveredCandidateCount => MatchCount + UnverifiedCandidateCount;
    public int TargetCandidateCount => DeliveredCandidateCount - ModeledNonHitSampleCount;
    // UI observation only; never a pricing/calibration denominator.
    public double ObservedScanningSeconds { get; init; }
    public long ObservedScanningRoots { get; init; }
}
