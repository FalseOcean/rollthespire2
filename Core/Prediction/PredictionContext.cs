using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Prediction;

/// <summary>
/// Immutable identity of one analysis request. A document keeps this context forever;
/// later UI changes can only create a new request/document and cannot reinterpret an
/// existing result.
/// </summary>
public readonly record struct PredictionRequestId(Guid Value)
{
    public static PredictionRequestId Create() => new(Guid.NewGuid());
    public bool IsValid => Value != Guid.Empty;
    public string Serialized => Value.ToString("N");
    public override string ToString() => Serialized;
}

public sealed record PredictionContext(
    PredictionRequestId RequestId,
    RuntimeProfileId ProfileId,
    string GameVersion,
    CharacterIdentity Character,
    int Ascension,
    int PlayersCount,
    int PlayerSlotIndex,
    string UnlockSnapshotFingerprint,
    string CatalogFingerprint,
    SourceAuthority SourceAuthority,
    SnapshotCompleteness SnapshotCompleteness,
    IdentityResolutionStatus ResolutionStatus,
    bool IsVanilla,
    string? SourceModId,
    string? SourceAssembly)
{
    public ModelKey CharacterKey => Character.CharacterKey;
    public KnownVanillaCharacterKind? KnownVanillaCharacterKind => Character.KnownVanillaKind;
}
