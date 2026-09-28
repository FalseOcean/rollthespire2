using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Main-thread sealing boundary for the shared Modern world capture DTO.
/// The caller must read game objects before this call and project them into the
/// immutable snapshot. No game object or live RNG is retained by the result.
/// </summary>
public static class Beta109WorldRuntimeCaptureSealer
{
    public static WorldAuthoritySnapshot Seal(
        Beta109WorldGenerationSnapshot generation,
        SourceAuthority sourceAuthority,
        SnapshotCompleteness completeness,
        DateTimeOffset capturedAtUtc,
        string diagnosticCode)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(generation);
        if (!RuntimeProfilePolicies.IsModernCore(generation.Profile))
            throw new InvalidOperationException("ModernRuntimeCaptureProfileMismatch");
        if (string.IsNullOrWhiteSpace(generation.CatalogFingerprint) ||
            string.IsNullOrWhiteSpace(generation.UnlockFingerprint) ||
            string.IsNullOrWhiteSpace(generation.GenerationRuleFingerprint) ||
            string.IsNullOrWhiteSpace(generation.SnapshotFingerprint))
        {
            throw new InvalidOperationException("ModernRuntimeCaptureFingerprintMissing");
        }

        IReadOnlyList<WorldActGroupSnapshot> actGroups = generation.OrderedActCatalog
            .GroupBy(act => act.Act)
            .OrderBy(group => group.Key)
            .Select(group => new WorldActGroupSnapshot(
                group.Key,
                group.Select(ToOuterActSnapshot).ToArray()))
            .ToArray();

        ModelKey[] optionCatalog = generation.AncientEventContexts
            .Where(context => context.Catalog is not null)
            .SelectMany(context => context.Catalog!.Pools)
            .SelectMany(pool => pool.OrderedOptions)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        bool optionCatalogExact = generation.AncientEventContexts.Count > 0 &&
                                  generation.AncientEventContexts.All(context =>
                                      context.Catalog is { CatalogExact: true } &&
                                      context.Catalog.Pools.All(pool => pool.OrderExact));

        bool catalogOrderExact = generation.ActSelectionAuthorityExact &&
                                 generation.SharedEventCatalogExact &&
                                 generation.AllSharedAncientCatalogExact &&
                                 generation.SharedAncientCatalogExact &&
                                 generation.OrderedActCatalog.Count > 0 &&
                                 generation.OrderedActCatalog.All(act => act.HasExactGenerationInputs);

        return new WorldAuthoritySnapshot(
            CapturedProfileId: generation.Profile,
            ActGroups: actGroups,
            SharedEvents: generation.SharedEvents,
            SharedAncients: generation.SharedAncients,
            // Shared Modern run-start relic inputs are owned by the nested versioned
            // snapshot. They are rarity buckets, not the Legacy/Neow source-pool
            // contract represented by these two outer fields.
            SharedRelicPoolSource: null,
            CharacterRelicPoolSource: null,
            UnlockedCharacters: generation.UnlockedCharacters,
            UnlockedCharactersExact: generation.UnlockedCharactersExact,
            AncientOptionRelicCatalog: optionCatalog,
            AncientOptionRelicCatalogExact: optionCatalogExact,
            CatalogOrderExact: catalogOrderExact,
            RelicInitializationExact: generation.RelicInitializationExact,
            SourceAuthority: sourceAuthority,
            Completeness: completeness,
            CatalogFingerprint: generation.CatalogFingerprint,
            SnapshotFingerprint: generation.SnapshotFingerprint,
            CapturedAtUtc: capturedAtUtc,
            CaptureDiagnosticCode: string.IsNullOrWhiteSpace(diagnosticCode)
                ? generation.CaptureDiagnosticCode
                : diagnosticCode,
            Beta109Generation: generation);
    }

    private static WorldActSnapshot ToOuterActSnapshot(Beta109ActGenerationSnapshot act) => new(
        Act: act.Act,
        ActKey: act.ActKey,
        WeakEncounterSlots: act.WeakEncounterSlots,
        TotalNormalRooms: act.TotalNormalRooms,
        Events: act.OrderedEvents,
        WeakEncounters: act.WeakEncounters.Select(ToOuterEncounterSnapshot).ToArray(),
        RegularEncounters: act.RegularEncounters.Select(ToOuterEncounterSnapshot).ToArray(),
        EliteEncounters: act.EliteEncounters.Select(ToOuterEncounterSnapshot).ToArray(),
        Bosses: act.Bosses,
        Ancients: act.OrderedAncients,
        GenerationInputsExact: act.HasExactGenerationInputs);

    private static WorldEncounterSnapshot ToOuterEncounterSnapshot(
        Beta109EncounterEntrySnapshot encounter) => new(
        encounter.EncounterKey,
        encounter.Tags);
}
