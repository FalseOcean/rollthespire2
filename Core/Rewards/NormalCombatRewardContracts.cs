using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Rewards;

public enum NormalCombatRewardBaselinePolicy
{
    OpeningRewardsStreamNoInterveningConsumers,
    OpeningRouteContinuationNoInterveningConsumers
}

public enum NormalCombatRewardBattleProjectionScope
{
    OpeningRouteFirstCombat,
    ConsecutiveDefaultMonsterNoInterveningConsumers
}

public enum NormalCombatCardUpgradeState
{
    NotUpgraded,
    Upgraded,
    Unknown
}

public enum NormalCombatCardEnchantment
{
    Glam
}

public enum NormalCombatGoldProjectionStatus
{
    NotPredictedRuntimeDependent,
    ConditionalDefaultMonsterFullKill
}

public enum NormalCombatRewardConditionalAssumption
{
    NonTutorialMonsterPipeline,
    RewardHooksNoOp,
    ForcePotionRewardFalse,
    PositiveGoldProportionAndDefaultGoldRewardPresent,
    NoInterveningRewardsConsumersOrHookStateChanges,
    OpeningRouteContinuationApplied,
    FullKillGoldProportionOne,
    DefaultMonsterGoldRangeNoEncounterOverride,
    KnownOpeningRelicRewardAdaptersApplied
}

public sealed record NormalCombatRewardRngTraceEntry(
    int Sequence,
    int BattleOrdinal,
    string Stage,
    string Operation,
    int CallCountAfter,
    double? NumericResult = null,
    int? IntResult = null,
    ModelKey? SelectedKey = null)
{
    public float? CardRarityOffsetBefore { get; init; }
    public float? RareThreshold { get; init; }
    public float? UncommonThreshold { get; init; }
    public float? CardRarityOffsetAfter { get; init; }
    public EffectCardRarity? RolledCardRarity { get; init; }
}

public sealed record NormalCombatRewardCardResult(
    int Ordinal,
    ModelKey CardKey,
    EffectCardRarity Rarity,
    bool IsUpgradable,
    float UpgradeRoll,
    float BaseUpgradeOdds,
    NormalCombatCardUpgradeState UpgradeState,
    bool IsFromCombat,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode)
{
    public EffectCardType CardType { get; init; } = EffectCardType.Other;

    public IReadOnlyList<RewardImpactSourceKey> AppliedImpactSources { get; init; } =
        Array.Empty<RewardImpactSourceKey>();

    public IReadOnlyList<NormalCombatCardEnchantment> Enchantments { get; init; } =
        Array.Empty<NormalCombatCardEnchantment>();
}

public sealed record NormalCombatRewardGoldRewardResult(
    int RewardOrdinal,
    int Amount,
    bool IsBaseReward,
    bool RngCallConsumed,
    RewardImpactSourceKey? Source,
    EvidenceCode EvidenceCode);

public sealed record NormalCombatRewardCardRewardResult(
    int RewardOrdinal,
    bool IsFromCombat,
    IReadOnlyList<NormalCombatRewardCardResult> Cards,
    EvidenceCode EvidenceCode);

public sealed record NormalCombatRewardPotionResult(
    bool Generated,
    ModelKey? PotionKey,
    EffectPotionRarity? Rarity,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode);

public sealed record NormalCombatRewardBattleResult(
    int BattleOrdinal,
    int Act,
    ModelKey ActKey,
    NormalCombatRewardBattleProjectionScope ProjectionScope,
    int? Gold,
    NormalCombatGoldProjectionStatus GoldStatus,
    bool GoldRngCallConsumed,
    NormalCombatRewardPotionResult Potion,
    IReadOnlyList<NormalCombatRewardCardResult> Cards,
    float PotionOddsBefore,
    float PotionOddsAfter,
    float CardRarityOffsetBefore,
    float CardRarityOffsetAfter,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCountBefore,
    int RngCallCountAfter,
    string ReasonCode,
    EvidenceCode EvidenceCode)
{
    public IReadOnlyList<NormalCombatRewardRngTraceEntry> RngTrace { get; init; } =
        Array.Empty<NormalCombatRewardRngTraceEntry>();

    public IReadOnlyList<NormalCombatRewardConditionalAssumption> ConditionalAssumptions { get; init; } =
        Array.Empty<NormalCombatRewardConditionalAssumption>();

    public IReadOnlyList<NormalCombatRewardCardRewardResult> CardRewards { get; init; } =
        Array.Empty<NormalCombatRewardCardRewardResult>();

    public IReadOnlyList<NormalCombatRewardGoldRewardResult> GoldRewards { get; init; } =
        Array.Empty<NormalCombatRewardGoldRewardResult>();

    public IReadOnlyList<RewardImpactSourceKey> AppliedRewardImpactSources { get; init; } =
        Array.Empty<RewardImpactSourceKey>();

    public string RewardImpactFingerprint { get; init; } = string.Empty;
}

public sealed record NormalCombatRewardRoutePredictionResult(
    string RouteGroupId,
    OpeningRewardContinuation RepresentativeContinuation,
    IReadOnlyList<OpeningRewardRouteDescriptor> EquivalentRoutes,
    SeedDomainEvaluationStatus Status,
    IReadOnlyList<NormalCombatRewardBattleResult> Battles,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IssueCode,
    string ContinuationFingerprint,
    EvidenceCode EvidenceCode);

public sealed record NormalCombatRewardSequencePredictionResult(
    SeedDomainEvaluationStatus Status,
    RuntimeProfileId ProfileId,
    int Act,
    ModelKey ActKey,
    NormalCombatRewardBaselinePolicy BaselinePolicy,
    IReadOnlyList<NormalCombatRewardBattleResult> Battles,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCount,
    string IssueCode,
    string AuthorityFingerprint,
    EvidenceCode EvidenceCode,
    IReadOnlyList<PredictionDiagnostic> Diagnostics)
{
    public IReadOnlyList<NormalCombatRewardRoutePredictionResult> Routes { get; init; } =
        Array.Empty<NormalCombatRewardRoutePredictionResult>();

    public NormalCombatRewardProjectionScope ProjectionScope { get; init; } =
        NormalCombatRewardProjectionScope.RichAnalysis;

    public NormalCombatRewardRouteSelectionMode RouteSelectionMode { get; init; } =
        NormalCombatRewardRouteSelectionMode.AllRealRoutes;

    public int RealOpeningRewardRouteCount { get; init; }
    public int EligibleOpeningRewardRouteCount { get; init; }
    public int ProjectedOpeningRewardRouteCount { get; init; }
    public int CanonicalRewardRouteCount { get; init; }
    public int IgnoredPerturbingRouteCount { get; init; }
    public int IgnoredUnknownRouteCount { get; init; }
    public bool NoEligibleSearchRewardRoute { get; init; }

    public static NormalCombatRewardSequencePredictionResult Unknown(
        RuntimeProfileId profileId,
        string issueCode,
        string authorityFingerprint = "") => new(
        SeedDomainEvaluationStatus.Unknown,
        profileId,
        0,
        default,
        NormalCombatRewardBaselinePolicy.OpeningRouteContinuationNoInterveningConsumers,
        Array.Empty<NormalCombatRewardBattleResult>(),
        PredictionPrecision.Unknown,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        "player.rewards",
        0,
        issueCode,
        authorityFingerprint ?? string.Empty,
        "normal-combat-reward-unknown",
        new[]
        {
            new PredictionDiagnostic(PredictionDiagnosticCodes.PredictionDomain, PredictionDomain.NormalCombatRewardSequence.ToString()),
            new PredictionDiagnostic(PredictionDiagnosticCodes.ValidationIssue, issueCode)
        });

    public static NormalCombatRewardSequencePredictionResult Unsupported(
        RuntimeProfileId profileId,
        string issueCode,
        string authorityFingerprint = "") => new(
        SeedDomainEvaluationStatus.Unsupported,
        profileId,
        0,
        default,
        NormalCombatRewardBaselinePolicy.OpeningRouteContinuationNoInterveningConsumers,
        Array.Empty<NormalCombatRewardBattleResult>(),
        PredictionPrecision.Unsupported,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        "player.rewards",
        0,
        issueCode,
        authorityFingerprint ?? string.Empty,
        "normal-combat-reward-unsupported",
        new[]
        {
            new PredictionDiagnostic(PredictionDiagnosticCodes.PredictionDomain, PredictionDomain.NormalCombatRewardSequence.ToString()),
            new PredictionDiagnostic(PredictionDiagnosticCodes.ValidationIssue, issueCode)
        });
}
