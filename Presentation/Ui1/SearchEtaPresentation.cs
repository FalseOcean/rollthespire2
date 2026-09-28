using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Presentation.Ui1;

internal enum SearchEtaQuickViewStatus : byte
{
    Ready = 0,
    Calibrating = 1,
    Unavailable = 2,
    Impossible = 3,
    NoConditions = 4
}

internal sealed record SearchEtaQuickView(
    SearchEtaQuickViewStatus Status,
    double? AcceptedResultSurvivalRate,
    string SelectedPlanId,
    double? FirstResultMeanMs,
    double? FirstResultP99Ms,
    int TargetCount,
    double? TargetMeanMs,
    IReadOnlyList<string> MissingEvidence)
{
    public bool IsLive { get; init; }
    public int RemainingTargetCount { get; init; }
    public static SearchEtaQuickView Unavailable(int targetCount = 1) => new(
        SearchEtaQuickViewStatus.Unavailable,
        null,
        string.Empty,
        null,
        null,
        Math.Max(1, targetCount),
        null,
        Array.Empty<string>());
}

internal static class SearchEtaPresentationBuilder
{
    // UI-only projection. A completed-root wall-rate includes normal pipeline
    // stalls; it is not a shader peak, a probability fit or a new Cost quote.
    public static SearchEtaQuickView Live(SearchEtaQuickView predicted,
        RolltheSpire2.Search.Contracts.SearchProgressSnapshot progress, double? stableRootsPerSecond)
    {
        if (predicted.Status == SearchEtaQuickViewStatus.NoConditions ||
            progress.State != RolltheSpire2.Search.Contracts.SearchRunState.Running ||
            predicted.AcceptedResultSurvivalRate is not (> 0 and <= 1) ||
            stableRootsPerSecond is not > 0 || !double.IsFinite(stableRootsPerSecond.Value)) return predicted;
        double probability = predicted.AcceptedResultSurvivalRate.Value;
        double meanMs = 1000 / probability / stableRootsPerSecond.Value;
        double logMiss = probability < 1e-8 ? -probability * (1 + probability / 2) : Math.Log(1 - probability);
        double p99Roots = probability == 1 ? 1 : Math.Ceiling(Math.Log(.01) / logMiss);
        // Very rare p may round 1-p to 1; its limiting exponential horizon is safe
        // for presentation without changing the frozen probability authority.
        if (!double.IsFinite(p99Roots)) p99Roots = -Math.Log(.01) / probability;
        int remaining = Math.Max(0, progress.TargetMatchCount - progress.MatchCount);
        double targetMs = remaining * meanMs;
        double p99Ms = 1000 * p99Roots / stableRootsPerSecond.Value;
        if (!double.IsFinite(meanMs) || !double.IsFinite(targetMs) || !double.IsFinite(p99Ms)) return predicted;
        return predicted with { Status = SearchEtaQuickViewStatus.Ready, IsLive = true,
            RemainingTargetCount = remaining, FirstResultMeanMs = remaining == 0 ? 0 : meanMs,
            FirstResultP99Ms = remaining == 0 ? 0 : p99Ms, TargetMeanMs = targetMs,
            MissingEvidence = [] };
    }

    public static SearchEtaQuickView Build(FamilySearchEtaProjectionV1 projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.AcceptedResultProbabilityEvidence == "Joint.NoPredicates")
            return SearchEtaQuickView.Unavailable(projection.TargetCount) with { Status = SearchEtaQuickViewStatus.NoConditions };
        return new SearchEtaQuickView(
            projection.FirstResultSearchMeanMs.HasValue && projection.FirstResultSearchP99Ms.HasValue && projection.TargetSearchMeanMs.HasValue
                ? SearchEtaQuickViewStatus.Ready : projection.Status switch
            {
                FamilySearchEtaStatus.Ready => SearchEtaQuickViewStatus.Ready,
                FamilySearchEtaStatus.Calibrating => SearchEtaQuickViewStatus.Calibrating,
                FamilySearchEtaStatus.Impossible => SearchEtaQuickViewStatus.Impossible,
                _ => SearchEtaQuickViewStatus.Unavailable
            },
            projection.AcceptedResultProbability,
            projection.Plan.PlanId,
            projection.FirstResultSearchMeanMs,
            projection.FirstResultSearchP99Ms,
            projection.TargetCount,
            projection.TargetSearchMeanMs,
            projection.MissingEvidence.Where(e => e != "CompleteAllocationSetupUnavailable").ToArray());
    }
}
