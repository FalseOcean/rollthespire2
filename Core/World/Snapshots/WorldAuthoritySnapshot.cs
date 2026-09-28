using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.World.Snapshots;

public sealed record WorldEncounterSnapshot(
    ModelKey EncounterKey,
    IReadOnlyList<string> Tags);

public sealed record WorldActSnapshot(
    int Act,
    ModelKey ActKey,
    int WeakEncounterSlots,
    int TotalNormalRooms,
    IReadOnlyList<ModelKey> Events,
    IReadOnlyList<WorldEncounterSnapshot> WeakEncounters,
    IReadOnlyList<WorldEncounterSnapshot> RegularEncounters,
    IReadOnlyList<WorldEncounterSnapshot> EliteEncounters,
    IReadOnlyList<ModelKey> Bosses,
    IReadOnlyList<ModelKey> Ancients,
    bool GenerationInputsExact = false)
{
    public IReadOnlyList<ModelKey> OrderedRawEvents { get; init; } = Events;
    public IReadOnlyList<ModelKey> OrderedEligibleEvents { get; init; } = Events;
    public bool RawEventCatalogOrderExact { get; init; }
    public bool EventEpochMembershipExact { get; init; }
    public bool EventEpochRevealFactsExact { get; init; }
    public bool EligibleEventOrderExact { get; init; }

    public bool EventSequenceAuthorityExact =>
        RawEventCatalogOrderExact &&
        EventEpochMembershipExact &&
        EventEpochRevealFactsExact &&
        EligibleEventOrderExact;
}

public sealed record WorldActGroupSnapshot(
    int Act,
    IReadOnlyList<WorldActSnapshot> Acts);

/// <summary>
/// Main-thread copy of the catalog and source relic pools needed by the pure
/// Boss/Ancient predictor. No game model, Godot object, live run state, player,
/// save state, or game RNG object may escape into this DTO.
/// </summary>
public sealed record WorldAuthoritySnapshot(
    RuntimeProfileId CapturedProfileId,
    IReadOnlyList<WorldActGroupSnapshot>? ActGroups,
    IReadOnlyList<ModelKey>? SharedEvents,
    IReadOnlyList<ModelKey>? SharedAncients,
    IReadOnlyList<NeowEffectRelicSnapshot>? SharedRelicPoolSource,
    IReadOnlyList<NeowEffectRelicSnapshot>? CharacterRelicPoolSource,
    IReadOnlyList<ModelKey>? UnlockedCharacters,
    bool UnlockedCharactersExact,
    IReadOnlyList<ModelKey>? AncientOptionRelicCatalog,
    bool AncientOptionRelicCatalogExact,
    bool CatalogOrderExact,
    bool RelicInitializationExact,
    SourceAuthority SourceAuthority,
    SnapshotCompleteness Completeness,
    string CatalogFingerprint,
    string SnapshotFingerprint,
    DateTimeOffset? CapturedAtUtc,
    string CaptureDiagnosticCode,
    Beta109WorldGenerationSnapshot? Beta109Generation = null)
{
    public Beta109EventCatalogAuthoritySnapshot EventAuthority { get; init; } =
        Beta109EventCatalogAuthoritySnapshot.Missing("MissingEventAuthority");
    public bool HasCatalog =>
        ActGroups is { Count: > 0 } &&
        SharedEvents is not null &&
        SharedAncients is not null;

    public bool HasExactLegacyFoundation =>
        CapturedProfileId == RuntimeProfileId.Stable107 &&
        HasCatalog &&
        CatalogOrderExact &&
        RelicInitializationExact &&
        SharedRelicPoolSource is not null &&
        CharacterRelicPoolSource is not null &&
        ActGroups!.SelectMany(group => group.Acts).All(act => act.GenerationInputsExact) &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority) &&
        Completeness == SnapshotCompleteness.Complete;

    public bool HasExactModernFoundation =>
        RuntimeProfilePolicies.IsModernCore(CapturedProfileId) &&
        Beta109Generation is not null &&
        Beta109Generation.Profile == CapturedProfileId &&
        Beta109Generation.AllowsProductionExact &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority) &&
        Completeness == SnapshotCompleteness.Complete;
}
