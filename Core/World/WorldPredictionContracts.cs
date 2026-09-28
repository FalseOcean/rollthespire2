using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Events;

namespace RolltheSpire2.Core.World;

public enum SeedDomainEvaluationStatus
{
    Evaluated,
    Unsupported,
    Unknown
}


public enum AncientOptionsEvaluationStatus
{
    EvaluatedNonEmpty,
    EvaluatedKnownEmpty,
    EvaluatedLocked,
    NotEvaluatedByPolicy,
    UnknownMissingAuthority,
    Unsupported
}

public enum WorldRngConsumptionShape
{
    Fixed,
    Variable,
    PredicateAttempt,
    Fallback,
    Shuffle
}

public sealed record WorldRngTraceEntry(
    int Sequence,
    string StreamDomain,
    string SourceStage,
    string Operation,
    int CallCountAfter,
    WorldRngConsumptionShape ConsumptionShape = WorldRngConsumptionShape.Fixed,
    int? Bound = null,
    int? IntResult = null,
    double? DoubleResult = null,
    ModelKey? SelectedKey = null);

public sealed record BossPredictionResult(
    int Act,
    int Ordinal,
    ModelKey BossKey,
    PredictionPrecision IdentityPrecision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCount,
    EvidenceCode EvidenceCode)
{
    public IReadOnlyList<WorldRngTraceEntry> RngTrace { get; init; } = Array.Empty<WorldRngTraceEntry>();
    public string AuthorityFingerprint { get; init; } = string.Empty;
    public string GenerationRuleFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// True only when the Boss identity was deterministically replayed from the
    /// captured immutable normal-flow inputs. Search may consume this identity
    /// even when version/fixture confidence keeps IdentityPrecision at Partial.
    /// </summary>
    public bool SearchIdentityReplayable { get; init; }

    /// <summary>
    /// Non-empty when Search accepted a replayable identity under an explicit
    /// compatibility assumption rather than Production Exact authority.
    /// </summary>
    public string SearchCompatibilityIssueCode { get; init; } = string.Empty;
}

public sealed record AncientOptionCharacterTargetProjection(
    ModelKey? CharacterKey,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode,
    string IssueCode = "")
{
    public bool IsKnown =>
        Precision is PredictionPrecision.Exact or PredictionPrecision.Partial &&
        CharacterKey.HasValue &&
        CharacterKey.Value.IsValid;

    public static AncientOptionCharacterTargetProjection Exact(
        ModelKey characterKey,
        EvidenceCode evidenceCode) =>
        new(characterKey, PredictionPrecision.Exact, evidenceCode);

    public static AncientOptionCharacterTargetProjection Unknown(
        EvidenceCode evidenceCode,
        string issueCode) =>
        new(null, PredictionPrecision.Unknown, evidenceCode, issueCode);
}

public sealed record AncientOptionPredictionResult(
    int Ordinal,
    ModelKey OptionKey,
    PredictionPrecision OptionPrecision,
    EvidenceCode EvidenceCode,
    string? VariantId = null)
{
    public IReadOnlyList<WorldRngTraceEntry> RngTrace { get; init; } = Array.Empty<WorldRngTraceEntry>();
    public string AuthorityFingerprint { get; init; } = string.Empty;
    public bool IsVisible { get; init; } = true;
    public bool IsSelectable { get; init; } = true;
    public bool IsLocked { get; init; }
    public bool IsProceed { get; init; }
    public bool WillKillPlayer { get; init; }
    public PredictionPrecision AppearancePrecision { get; init; } = OptionPrecision;
    public PredictionPrecision SelectabilityPrecision { get; init; } = OptionPrecision;
    public string StableTextKey { get; init; } = string.Empty;
    public AncientOptionCharacterTargetProjection? CharacterTarget { get; init; }
}

public sealed record AncientPredictionResult(
    int Act,
    ModelKey AncientKey,
    PredictionPrecision IdentityPrecision,
    PredictionPrecision OptionPrecision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IdentityRngStream,
    int IdentityRngCallCount,
    string OptionRngStream,
    int OptionRngCallCount,
    IReadOnlyList<AncientOptionPredictionResult> Options,
    EvidenceCode IdentityEvidenceCode,
    EvidenceCode OptionEvidenceCode)
{
    public IReadOnlyList<WorldRngTraceEntry> IdentityRngTrace { get; init; } = Array.Empty<WorldRngTraceEntry>();
    public IReadOnlyList<WorldRngTraceEntry> OptionRngTrace { get; init; } = Array.Empty<WorldRngTraceEntry>();
    public string AuthorityFingerprint { get; init; } = string.Empty;
    public string GenerationRuleFingerprint { get; init; } = string.Empty;
    public AncientOptionsEvaluationStatus OptionsEvaluationStatus { get; init; } = AncientOptionsEvaluationStatus.UnknownMissingAuthority;
    public string OptionIssueCode { get; init; } = string.Empty;
}

public sealed record EncounterSequenceEntryResult(int Ordinal, ModelKey EncounterKey);

// Read-only initial queues, not map positions or Search predicates.
public sealed record ActEncounterSequenceResult(
    int Act, ModelKey ActKey,
    IReadOnlyList<EncounterSequenceEntryResult> Normal,
    IReadOnlyList<EncounterSequenceEntryResult> Elite,
    PredictionPrecision Precision, string IssueCode);

public sealed record WorldPredictionResult(
    SeedDomainEvaluationStatus BossStatus,
    SeedDomainEvaluationStatus AncientStatus,
    IReadOnlyList<BossPredictionResult> Bosses,
    IReadOnlyList<AncientPredictionResult> Ancients,
    IReadOnlyList<PredictionDiagnostic> Diagnostics,
    string BossIssueCode = "",
    string AncientIssueCode = "")
{
    public EventPoolSequencePredictionResult? EventPoolSequencePrediction { get; init; }
    public IReadOnlyList<ActEncounterSequenceResult> EncounterSequences { get; init; } = [];
    public static WorldPredictionResult Unsupported(string bossIssue, string ancientIssue) => new(
        SeedDomainEvaluationStatus.Unsupported,
        SeedDomainEvaluationStatus.Unsupported,
        Array.Empty<BossPredictionResult>(),
        Array.Empty<AncientPredictionResult>(),
        Array.Empty<PredictionDiagnostic>(),
        bossIssue,
        ancientIssue);

    public static WorldPredictionResult Unknown(string issue) => new(
        SeedDomainEvaluationStatus.Unknown,
        SeedDomainEvaluationStatus.Unknown,
        Array.Empty<BossPredictionResult>(),
        Array.Empty<AncientPredictionResult>(),
        Array.Empty<PredictionDiagnostic>(),
        issue,
        issue);
}
