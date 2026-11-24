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
    bool SkipExactValidation = false,
    IReadOnlyList<string>? ManualFamilyOrder = null,
    MapLossyOptions? MapLossy = null);

/// <summary>Frozen authored requirements and domain computation inputs, independent of a scan schedule.</summary>
public sealed record SearchModel(
    CompiledSearch CompiledSearch,
    ExactSearchEvaluationProjection Evaluation,
    CombatRewardRoutePolicyContract CombatRewardRoutePolicy,
    string SnapshotFingerprint)
{
    internal IReadOnlyList<RolltheSpire2.Core.Neow.NeowChoiceResult>? PartyOpeningChoices { get; init; }
    internal RolltheSpire2.Core.Rewards.CombatRewardPoolPreparation? RewardPoolPreparation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal RolltheSpire2.Search.FamilyExecution.VariantOpeningPreparation? VariantOpening { get; init; }
    internal RolltheSpire2.Search.Runtime.ShionExactTrialOptions ExactOptions { get; init; } =
        RolltheSpire2.Search.Runtime.ShionExactTrialOptions.Default;
    public bool IncludeDiagnostics { get; init; } = true;
    public RuntimeProfileId ProfileId => CompiledSearch.Context.ProfileId;
    public ModelKey CharacterKey => CompiledSearch.Context.CharacterKey;
    public int Ascension => CompiledSearch.Context.Ascension;
    public RuntimeContextAuthoritySnapshot Authority => CompiledSearch.Context.Authority;
    public GameVersionDetection Detection => CompiledSearch.Context.Detection;
}

/// <summary>The admitted model and a separately owned scan schedule. Only the session receives both.</summary>
public sealed record PreparedSearch(
    SearchModel Model,
    SearchRunOptions RunOptions,
    string CanonicalStartSeed,
    long ResolvedScanCount,
    string SnapshotFingerprint)
{
    public long ScanCount => ResolvedScanCount;
    public int TargetMatchCount => RunOptions.TargetMatchCount;
    public int WorkerCount => RunOptions.WorkerCount;
    public MapLossyOptions MapLossy => RunOptions.MapLossy ?? MapLossyOptions.Default;
}

/// <summary>Physical preferences are captured once; they contain no authored query or scan range.</summary>
internal sealed record FilterExecutionPreferences(int WorkerCount, bool SkipExactValidation,
    MapLossyOptions MapLossy)
{
    internal static FilterExecutionPreferences From(SearchRunOptions options) =>
        new(options.WorkerCount, options.SkipExactValidation, options.MapLossy ?? MapLossyOptions.Default);
}

public sealed record SearchPreparationResult(
    bool Success,
    PreparedSearch? Plan,
    SearchDisposition Disposition,
    string Issue)
{
    public static SearchPreparationResult Accepted(PreparedSearch plan) =>
        new(true, plan, SearchDisposition.NoMatch, string.Empty);

    public static SearchPreparationResult Rejected(SearchDisposition disposition, string issue) =>
        new(false, null, disposition, issue);
}
