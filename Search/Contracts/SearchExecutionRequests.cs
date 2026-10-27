using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Contracts;

public sealed record SearchRunOptions(
    string StartSeed,
    long ScanCount,
    int TargetMatchCount,
    int WorkerCount,
    bool IncludeDiagnostics = true,
    bool SkipExactValidation = false);

public sealed record ExactSearchExecutionRequest(
    CompiledSearch CompiledSearch,
    SearchRunOptions RunOptions,
    string CanonicalStartSeed,
    long ResolvedScanCount,
    ExactSearchEvaluationProjection Evaluation,
    CombatRewardRoutePolicyContract CombatRewardRoutePolicy,
    string SnapshotFingerprint)
{
    internal IReadOnlyList<RolltheSpire2.Core.Neow.NeowChoiceResult>? PartyOpeningChoices { get; init; }
    internal RolltheSpire2.Core.Rewards.CombatRewardPoolPreparation? RewardPoolPreparation { get; init; }
    internal RolltheSpire2.Search.Runtime.ShionExactTrialOptions ExactOptions { get; init; } =
        RolltheSpire2.Search.Runtime.ShionExactTrialOptions.Default;
    public RuntimeProfileId ProfileId => CompiledSearch.Context.ProfileId;
    public ModelKey CharacterKey => CompiledSearch.Context.CharacterKey;
    public int Ascension => CompiledSearch.Context.Ascension;
    public long ScanCount => ResolvedScanCount;
    public int TargetMatchCount => RunOptions.TargetMatchCount;
    public int WorkerCount => RunOptions.WorkerCount;
    public RuntimeContextAuthoritySnapshot Authority => CompiledSearch.Context.Authority;
    public GameVersionDetection Detection => CompiledSearch.Context.Detection;
    public bool IncludeDiagnostics => RunOptions.IncludeDiagnostics;
}

public sealed record ExactSearchExecutionCompileResult(
    bool Success,
    ExactSearchExecutionRequest? Plan,
    SearchDisposition Disposition,
    string Issue)
{
    public static ExactSearchExecutionCompileResult Accepted(ExactSearchExecutionRequest plan) =>
        new(true, plan, SearchDisposition.NoMatch, string.Empty);

    public static ExactSearchExecutionCompileResult Rejected(SearchDisposition disposition, string issue) =>
        new(false, null, disposition, issue);
}
