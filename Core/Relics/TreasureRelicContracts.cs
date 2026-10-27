using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Relics;

public enum TreasureRelicSource
{
    SharedBag,
    ExhaustedBagCirclet,
    SilverCrucibleSuppression
}

public sealed record TreasureRelicEntryResult(
    int EncounterOrdinal,
    ModelKey? RelicKey,
    string RolledRarityCode,
    string ActualRarityCode,
    TreasureRelicSource Source,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode)
{
    public bool IsSuppressed => Source == TreasureRelicSource.SilverCrucibleSuppression;
}

/// <summary>
/// Ordinary non-tutorial single-player chest opportunities, before floor 41,
/// with no intervening shared-bag consumers beyond the supplied actual opening
/// acquisitions. Ordinals count encountered treasure rooms, including ? chests.
/// Opening acquisitions remain separate evidence; they are never chest awards.
/// </summary>
public sealed record TreasureRelicPredictionResult(
    RuntimeProfileId ProfileId,
    SeedDomainEvaluationStatus Status,
    IReadOnlyList<TreasureRelicEntryResult> Entries,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IssueCode,
    int RngCallCount,
    EvidenceCode EvidenceCode)
{
    public const string RngStream = "treasure_room_relics";
    public const int MaximumCount = 2;
}

/// <summary>
/// Initial-order reference only: nine ordinary non-tutorial solo chest draws,
/// holding eligibility before floor 41 and ignoring opening/earlier acquisitions.
/// This is separate from the first-two-chest search/prediction contract.
/// </summary>
public sealed record InitialTreasureRelicPreviewResult(
    RuntimeProfileId ProfileId,
    SeedDomainEvaluationStatus Status,
    IReadOnlyList<TreasureRelicEntryResult> Entries,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IssueCode,
    int RngCallCount,
    EvidenceCode EvidenceCode)
{
    public const int PreviewCount = 9;
}
