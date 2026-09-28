using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Relics;

public enum RelicSequenceKind
{
    Common,
    Uncommon,
    Rare,
    Shop
}

public enum RelicSequencePullDirection
{
    Front,
    Back
}

public sealed record RelicSequenceEntryResult(
    int Position,
    ModelKey RelicKey,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode);

public sealed record RelicSequenceLaneResult(
    RelicSequenceKind Kind,
    string RarityCode,
    RelicSequencePullDirection PullDirection,
    IReadOnlyList<RelicSequenceEntryResult> Entries,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    EvidenceCode EvidenceCode)
{
    /// <summary>
    /// Total number of entries in this projected lane after any lane-specific
    /// eligibility filtering (for example Shop IsAllowedInShops).
    /// </summary>
    public int TotalCount { get; init; }
    // Complete already-generated pull order for read-only overview presentation.
    public IReadOnlyList<RelicSequenceEntryResult> FullEntries { get; init; } = [];

    /// <summary>
    /// Presentation-ready preview from the physical back of the same shuffled
    /// bag. Position 1 is the first item a PullFromBack consumer would inspect.
    /// Common/Uncommon/Rare use this for dual-ended Analysis presentation.
    /// Existing Entries semantics remain unchanged.
    /// </summary>
    public IReadOnlyList<RelicSequenceEntryResult> TailEntries { get; init; } =
        Array.Empty<RelicSequenceEntryResult>();
}

/// <summary>
/// Search-ready immutable output for initial player RelicGrabBag sequences.
/// Common/Uncommon/Rare are pulled from the front. Shop is read from the back
/// and excludes relics whose captured IsAllowedInShops value is false.
/// </summary>
public sealed record RelicSequencePredictionResult(
    RuntimeProfileId ProfileId,
    SeedDomainEvaluationStatus Status,
    IReadOnlyList<RelicSequenceLaneResult> Lanes,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IssueCode,
    string AuthorityFingerprint,
    string CatalogFingerprint,
    string RngStream,
    int RngCallCount,
    EvidenceCode EvidenceCode,
    IReadOnlyList<PredictionDiagnostic> Diagnostics)
{
    /// <summary>
    /// Initial front-to-back order of the shared grab bag used by treasure rooms.
    /// Other relic sources may drain this bag before a chest is opened; these lanes
    /// are not a prediction of the relic awarded by each Act's chest.
    /// </summary>
    public IReadOnlyList<RelicSequenceLaneResult> TreasureRoomLanes { get; init; } = [];

    public static RelicSequencePredictionResult Unknown(
        RuntimeProfileId profileId,
        string issueCode,
        string authorityFingerprint = "",
        string catalogFingerprint = "") => new(
        profileId,
        SeedDomainEvaluationStatus.Unknown,
        Array.Empty<RelicSequenceLaneResult>(),
        PredictionPrecision.Unknown,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        issueCode,
        authorityFingerprint,
        catalogFingerprint,
        "up_front",
        0,
        "relic-sequence.unknown",
        new[] { new PredictionDiagnostic("relic-sequence-issue", issueCode) });

    public static RelicSequencePredictionResult Unsupported(
        RuntimeProfileId profileId,
        string issueCode) => new(
        profileId,
        SeedDomainEvaluationStatus.Unsupported,
        Array.Empty<RelicSequenceLaneResult>(),
        PredictionPrecision.Unsupported,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        issueCode,
        string.Empty,
        string.Empty,
        "up_front",
        0,
        "relic-sequence.unsupported",
        new[] { new PredictionDiagnostic("relic-sequence-issue", issueCode) });
}
