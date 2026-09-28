using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Events;

public enum EventPoolSourceKind
{
    ActLocal,
    Shared
}


public enum EventRuntimeEligibilityProjectionStatus
{
    ClassifiedAtRunStart,
    Unsupported
}

public enum EventCandidateEligibilityKind
{
    StaticEligible,
    RuntimeDependent
}

public enum ActualVisitedEventSequenceStatus
{
    Unsupported
}

public enum EventStaticExclusionKind
{
    OpeningAncientCursorOffset,
    ActIndex,
    PlayerMode,
    UnlockState,
    AlwaysDisallowed
}

public sealed record EventImmutableEligibilityContext(
    WorldGameMode GameMode,
    bool GameModeExact,
    int PlayerCount,
    bool PlayerCountExact,
    int CharacterCardPoolCount,
    bool CharacterCardPoolCountExact)
{
    public static EventImmutableEligibilityContext Unknown { get; } = new(
        WorldGameMode.Unknown, false, 0, false, 0, false);

    public bool IsSinglePlayerExact =>
        GameModeExact && PlayerCountExact &&
        GameMode == WorldGameMode.Singleplayer && PlayerCount == 1;

    public bool IsMultiplayerExact =>
        GameModeExact && PlayerCountExact &&
        (GameMode == WorldGameMode.Multiplayer || PlayerCount > 1);
}

public sealed record EventEffectiveCandidateContext(
    int OpeningAncientCursorOffset,
    bool OpeningAncientCursorAuthorityExact,
    string OpeningAncientCursorAuthorityCode,
    EventImmutableEligibilityContext ImmutableEligibility)
{
    public static EventEffectiveCandidateContext AuthoritativeOpeningAncient(
        EventImmutableEligibilityContext? immutableEligibility = null,
        string authorityCode = "NormalNeowOpeningAncient") => new(
            1,
            true,
            authorityCode,
            immutableEligibility ?? EventImmutableEligibilityContext.Unknown);
}

public sealed record EventEpochFilterEvidence(
    string EpochId,
    bool IsRevealed,
    int MemberCount,
    int RemovedCount,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode);

public sealed record EventPoolRawSequenceEntryResult(
    int RawOrdinal,
    int SourceOrdinal,
    ModelKey EventKey,
    EventPoolSourceKind Source,
    IReadOnlyList<string> EpochIds,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode);

public sealed record EventStaticExclusionEvidence(
    int RawOrdinal,
    ModelKey EventKey,
    EventPoolSourceKind Source,
    EventStaticExclusionKind Kind,
    string RuleId,
    IReadOnlyList<int> AllowedActs,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode);

public sealed record EventPoolSequenceEntryResult(
    int Ordinal,
    int RawOrdinal,
    int SourceOrdinal,
    ModelKey EventKey,
    EventPoolSourceKind Source,
    IReadOnlyList<string> EpochIds,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode)
{
    public EventCandidateEligibilityKind EligibilityKind { get; init; } =
        EventCandidateEligibilityKind.StaticEligible;
    public string EligibilityReasonCode { get; init; } = string.Empty;
}

public sealed record EventPoolActSequenceResult(
    int Act,
    ModelKey ActKey,
    IReadOnlyList<EventPoolRawSequenceEntryResult> RawEntries,
    IReadOnlyList<EventPoolSequenceEntryResult> Entries,
    int RawActLocalCount,
    int RawSharedCount,
    int EligibleCount,
    int EffectiveCount,
    int FilteredOutCount,
    int OpeningAncientCursorOffset,
    int OpeningAncientSkippedCount,
    int StaticFilteredOutCount,
    int DuplicateFilteredOutCount,
    IReadOnlyList<EventEpochFilterEvidence> EpochFilters,
    IReadOnlyList<EventStaticExclusionEvidence> StaticExclusions,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCountBefore,
    int RngCallCountAfter,
    EvidenceCode EvidenceCode);

/// <summary>
/// Immutable Search-ready output for each Act's source-audited initial effective
/// ordinary-event candidate sequence. RawEntries preserves the initialized queue
/// after epoch filtering and UpFront shuffle. Entries first applies an authoritative
/// opening EventRoom cursor offset, then removes candidates disproven by immutable
/// necessary conditions. Retained entries are classified StaticEligible or
/// RuntimeDependent. It is not the actual future visited-event sequence produced by
/// route-state IsAllowed, global visited history, tutorial reordering, or hooks.
/// </summary>
public sealed record EventPoolSequencePredictionResult(
    RuntimeProfileId ProfileId,
    SeedDomainEvaluationStatus Status,
    IReadOnlyList<EventPoolActSequenceResult> Acts,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IssueCode,
    string AuthorityFingerprint,
    string CatalogFingerprint,
    EvidenceCode EvidenceCode,
    IReadOnlyList<PredictionDiagnostic> Diagnostics)
{
    public PredictionPrecision RawEventOrderPrecision { get; init; } = PredictionPrecision.Unknown;
    public PredictionPrecision StaticActCleaningPrecision { get; init; } = PredictionPrecision.Unknown;
    public PredictionPrecision StaticCandidateOrdinalPrecision { get; init; } = PredictionPrecision.Unknown;
    public EventRuntimeEligibilityProjectionStatus RuntimeEligibility { get; init; } =
        EventRuntimeEligibilityProjectionStatus.ClassifiedAtRunStart;
    public ActualVisitedEventSequenceStatus ActualVisitedEventSequence { get; init; } =
        ActualVisitedEventSequenceStatus.Unsupported;

    public static EventPoolSequencePredictionResult Unknown(
        RuntimeProfileId profileId,
        string issueCode,
        string authorityFingerprint = "",
        string catalogFingerprint = "") => new(
        profileId,
        SeedDomainEvaluationStatus.Unknown,
        Array.Empty<EventPoolActSequenceResult>(),
        PredictionPrecision.Unknown,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        issueCode,
        authorityFingerprint,
        catalogFingerprint,
        "event-pool-sequence.unknown",
        new[] { new PredictionDiagnostic("event-pool-sequence-issue", issueCode) });

    public static EventPoolSequencePredictionResult Unsupported(
        RuntimeProfileId profileId,
        string issueCode) => new(
        profileId,
        SeedDomainEvaluationStatus.Unsupported,
        Array.Empty<EventPoolActSequenceResult>(),
        PredictionPrecision.Unsupported,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        issueCode,
        string.Empty,
        string.Empty,
        "event-pool-sequence.unsupported",
        new[] { new PredictionDiagnostic("event-pool-sequence-issue", issueCode) });
}
