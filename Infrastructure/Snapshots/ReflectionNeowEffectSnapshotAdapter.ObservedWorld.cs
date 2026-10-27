using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    internal sealed record WorldObservedInputs
    {
        public WorldAuthoritySnapshot? Failure { get; init; }
        public DateTimeOffset ObservedAtUtc { get; init; }
        public IReadOnlyList<WorldActGroupSnapshot> Groups { get; init; } = Array.Empty<WorldActGroupSnapshot>();
        public ModelListCapture SharedEvents { get; init; } = new(Array.Empty<ModelKey>(), false);
        public Beta109EventCatalogAuthoritySnapshot EventAuthority { get; init; } = null!;
        public ModelListCapture SharedAncients { get; init; } = new(Array.Empty<ModelKey>(), false);
        public ModelListCapture UnlockedCharacters { get; init; } = new(Array.Empty<ModelKey>(), false);
        public ModelListCapture RelicCatalog { get; init; } = new(Array.Empty<ModelKey>(), false);
        public Beta109StaticAuthorityCapture? ModernAuthority { get; init; }
        public bool UnlockStateExact { get; init; }
        public string UnlockSource { get; init; } = "explicit-party-slot";
        public int? EncounterNumberOfRuns { get; init; }
    }

    internal static WorldAuthoritySnapshot DeriveWorld(IRuntimeProfile profile, string rawSeed,
        CharacterIdentity character, int ascension, int playersCount, int playerSlotIndex,
        bool? noRunModifiers, NeowEffectAuthoritySnapshot? effects, WorldGameMode predictionGameMode,
        PredictionGameModeAuthority predictionGameModeAuthority, string gameVersion, WorldObservedInputs observed)
    {
        try { return DeriveWorldCore(profile,rawSeed,character,ascension,playersCount,playerSlotIndex,
            noRunModifiers,effects,predictionGameMode,predictionGameModeAuthority,gameVersion,observed); }
        catch (Exception ex)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex,"WorldReflectionCaptureFailed",
                nameof(ReflectionNeowEffectSnapshotAdapter),"WorldDerive","issues.capture_failed",
                correlationId:$"profile={profile.ProfileId};character={character.CharacterKey.Serialized};slot={playerSlotIndex}");
            return MissingWorld(profile.ProfileId,"WorldReflectionCaptureFailed:"+ex.GetType().Name);
        }
    }

    private static WorldAuthoritySnapshot DeriveWorldCore(IRuntimeProfile profile, string rawSeed,
        CharacterIdentity character, int ascension, int playersCount, int playerSlotIndex,
        bool? noRunModifiers, NeowEffectAuthoritySnapshot? effects, WorldGameMode predictionGameMode,
        PredictionGameModeAuthority predictionGameModeAuthority, string gameVersion, WorldObservedInputs observed)
    {
        if (observed.Failure is not null) return observed.Failure;
        var groups = observed.Groups; var sharedEventsCapture = observed.SharedEvents;
        var capturedEventAuthority = observed.EventAuthority; var sharedAncientsCapture = observed.SharedAncients;
        var unlockedCharactersCapture = observed.UnlockedCharacters; var relicCatalogCapture = observed.RelicCatalog;
        var modernStaticAuthority = observed.ModernAuthority;
        bool unlockStateExact = observed.UnlockStateExact; string unlockSource = observed.UnlockSource;
        IReadOnlyList<ModelKey> fallbackRelics = (effects?.SharedRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
            .Concat(effects?.CharacterRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
            .Select(relic => relic.RelicKey)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        IReadOnlyList<ModelKey> ancientOptionRelicCatalog = relicCatalogCapture.Keys.Count > 0
            ? relicCatalogCapture.Keys
            : fallbackRelics;

        IReadOnlyList<ModelKey> sharedEvents = modernStaticAuthority?.SharedEvents ?? sharedEventsCapture.Keys;
        IReadOnlyList<ModelKey> sharedAncients = modernStaticAuthority?.UnlockedSharedAncients ?? sharedAncientsCapture.Keys;
        IReadOnlyList<ModelKey> unlockedCharacters = unlockedCharactersCapture.Keys;
        bool unlockedCharactersExact = unlockedCharactersCapture.Exact;
        bool ancientOptionRelicCatalogExact = relicCatalogCapture.Exact && relicCatalogCapture.Keys.Count > 0;

        bool generationInputsPresent = groups.Count > 0 &&
                                       groups.All(group => group.Acts.Count > 0) &&
                                       groups.SelectMany(group => group.Acts).All(act =>
                                           act.Events.Count + sharedEvents.Count > 0 &&
                                           act.WeakEncounters.Count > 0 &&
                                           act.RegularEncounters.Count > 0 &&
                                           act.EliteEncounters.Count > 0 &&
                                           act.Bosses.Count > 0 &&
                                           act.Ancients.Count > 0);
        bool allActInputsExact = groups.SelectMany(group => group.Acts)
            .All(act => act.GenerationInputsExact);
        bool legacyCatalogOrderExact = profile.ProfileId == RuntimeProfileId.Stable107 &&
                                       unlockStateExact &&
                                       generationInputsPresent &&
                                       allActInputsExact &&
                                       sharedEventsCapture.Exact &&
                                       sharedAncientsCapture.Exact &&
                                       sharedAncients.Count > 0;
        bool modernStaticFoundationExact = modernStaticAuthority is not null &&
                                           modernStaticAuthority.ActSelectionAuthorityExact &&
                                           modernStaticAuthority.ModeFactsExact &&
                                           modernStaticAuthority.OrderedActs.Count > 0 &&
                                           modernStaticAuthority.OrderedActs.All(act => act.HasExactGenerationInputs) &&
                                           modernStaticAuthority.SharedEventCatalogExact &&
                                           modernStaticAuthority.AllSharedAncientCatalogExact &&
                                           modernStaticAuthority.UnlockedSharedAncientCatalogExact &&
                                           modernStaticAuthority.UnlockFactsExact &&
                                           modernStaticAuthority.RelicInitializationExact &&
                                           modernStaticAuthority.IsVanilla;
        bool catalogOrderExact = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
            ? modernStaticFoundationExact
            : legacyCatalogOrderExact;
        bool relicSourceExact = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
            ? modernStaticAuthority?.RelicInitializationExact == true
            : effects?.HasExactRelicBagSourcePools == true;
        bool hasCatalog = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
            ? modernStaticAuthority?.OrderedActs.Count > 0
            : groups.Count > 0 && groups.All(group => group.Acts.Count > 0);
        SourceAuthority authority = hasCatalog && catalogOrderExact && relicSourceExact
            ? SourceAuthority.OfficialRuntimeExact
            : SourceAuthority.Incomplete;
        SnapshotCompleteness completeness = hasCatalog && relicSourceExact
            ? SnapshotCompleteness.Complete
            : hasCatalog ? SnapshotCompleteness.Partial : SnapshotCompleteness.Missing;

        IEnumerable<string> descriptors = groups.SelectMany(group => group.Acts.SelectMany(DescribeAct))
            .Concat(sharedEvents.Select(key => "shared-event:" + key.Serialized))
            .Concat(capturedEventAuthority.EpochsInFilterOrder.Select(epoch =>
                "event-epoch:" + epoch.EpochId + ":" + epoch.IsRevealed + ":" +
                epoch.MembershipExact + ":" + epoch.RevealFactExact + ":" +
                string.Join(",", epoch.OrderedMemberKeys.Select(key => key.Serialized))))
            .Concat(new[]
            {
                "event-shared-order-exact:" + capturedEventAuthority.SharedRawOrderExact,
                "event-filter-order-exact:" + capturedEventAuthority.EpochFilterOrderExact,
                "event-authority-evidence:" + capturedEventAuthority.EvidenceCode
            })
            .Concat(sharedAncients.Select(key => "shared-ancient:" + key.Serialized))
            .Concat(unlockedCharacters.Select(key => "character:" + key.Serialized))
            .Concat(new[] { "characters-exact:" + unlockedCharactersExact })
            .Concat(ancientOptionRelicCatalog.Select(key => "option-relic:" + key.Serialized))
            .Concat(new[] { "option-relic-catalog-exact:" + ancientOptionRelicCatalogExact })
            .Concat(modernStaticAuthority is null
                ? Array.Empty<string>()
                : new[]
                {
                    ModernWorldEvidencePrefix(profile.ProfileId, "static-authority:") + modernStaticAuthority.DiagnosticCode,
                    ModernWorldEvidencePrefix(profile.ProfileId, "act-selection-exact:") + modernStaticAuthority.ActSelectionAuthorityExact,
                    ModernWorldEvidencePrefix(profile.ProfileId, "relic-init-exact:") + modernStaticAuthority.RelicInitializationExact,
                    ModernWorldEvidencePrefix(profile.ProfileId, "relic-shop-eligibility-exact:") + modernStaticAuthority.RelicShopEligibilityAuthorityExact
                });
        string catalogFingerprint = Fingerprint("world-catalog-v2", descriptors);
        string snapshotFingerprint = Fingerprint("world-authority-v2", new[]
        {
            profile.ProfileId.ToString(),
            character.CharacterKey.Serialized,
            catalogFingerprint,
            effects?.RelicBagFingerprint ?? "missing-relic-sources",
            unlockStateExact.ToString(),
            unlockSource,
            catalogOrderExact.ToString(),
            relicSourceExact.ToString(),
            completeness.ToString()
        });

        Beta109WorldGenerationSnapshot? beta109 = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
            ? BuildBeta109WorldGenerationSnapshot(
                profile,
                rawSeed,
                character,
                ascension,
                playersCount,
                playerSlotIndex,
                noRunModifiers,
                modernStaticAuthority!,
                effects,
                ancientOptionRelicCatalog,
                ancientOptionRelicCatalogExact,
                unlockedCharacters,
                unlockedCharactersExact,
                unlockStateExact,
                unlockSource,
                catalogFingerprint,
                gameVersion)
            : null;
        if (beta109 is not null)
        {
            catalogFingerprint = beta109.CatalogFingerprint;
            snapshotFingerprint = beta109.SnapshotFingerprint;
        }

        string captureDiagnostic = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
            ? beta109?.CaptureDiagnosticCode ?? (profile.ProfileId switch
            {
                RuntimeProfileId.Beta111 => "Beta111WorldSnapshotMissing",
                RuntimeProfileId.Beta110 => "Beta110WorldSnapshotMissing",
                _ => "Beta109HistoricalWorldSnapshotMissing"
            })
            : !unlockStateExact
                ? "Stable107WorldUnlockStateUnavailable:" + unlockSource
                : !catalogOrderExact
                    ? "Stable107WorldGenerationInputsIncompleteOrNonVanilla"
                    : string.Empty;

        return new WorldAuthoritySnapshot(
            profile.ProfileId,
            groups,
            sharedEvents,
            sharedAncients,
            effects?.SharedRelicPoolSource,
            effects?.CharacterRelicPoolSource,
            unlockedCharacters,
            unlockedCharactersExact,
            ancientOptionRelicCatalog,
            ancientOptionRelicCatalogExact,
            catalogOrderExact,
            relicSourceExact,
            authority,
            completeness,
            catalogFingerprint,
            snapshotFingerprint,
            observed.ObservedAtUtc,
            captureDiagnostic,
            beta109)
        {
            EventAuthority = beta109?.EventAuthority ?? capturedEventAuthority,
            EncounterNumberOfRuns = observed.EncounterNumberOfRuns
        };
    }

    internal static async Task<WorldObservedInputs> ObserveWorld(IRuntimeProfile profile, CharacterIdentity character,
        int playersCount, int playerSlotIndex, WorldGameMode predictionGameMode,
        PredictionGameModeAuthority predictionGameModeAuthority, object? explicitUnlockState,
        IReadOnlyList<ModelKey>? orderedCharacters, IReadOnlyList<ModelKey>? explicitDiscoveredActs,
        NativeObservationSchedule schedule, string act1OverrideRaw = "random")
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            string.Equals(a.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        if (assembly is null) return new() { Failure = MissingWorld(profile.ProfileId,"Sts2AssemblyNotLoaded") };
        try
        {
            var modelDb = FindType(assembly,"MegaCrit.Sts2.Core.Models.ModelDb","ModelDb");
            if (modelDb is null) return new() { Failure = MissingWorld(profile.ProfileId,"ModelDbNotFound") };
            string unlockSource = "explicit-party-slot";
            var unlockState = explicitUnlockState ?? TryCaptureCurrentUnlockState(assembly,out unlockSource);
            await schedule.Checkpoint("WorldEvents").ConfigureAwait(false);
            var sharedEvents = ReadStaticModelsWorld(modelDb,"AllSharedEvents","SharedEvents");
            var events = CaptureBeta109EventAuthority(assembly,unlockState,sharedEvents);
            var groups = await ObserveActGroups(modelDb,profile.ProfileId,unlockState,events,schedule).ConfigureAwait(false);
            await schedule.Checkpoint("WorldSharedCatalogs").ConfigureAwait(false);
            var ancients = ReadSharedAncientsWorld(modelDb,unlockState);
            var characters = ReadUnlockedCharactersWorld(modelDb,unlockState);
            var relics = ReadStaticRuntimeModelKeysWorld(modelDb,"AllRelics","Relics");
            var modern = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? await ObserveBeta109StaticAuthority(profile.ProfileId,assembly,modelDb,unlockState,
                    character,playersCount,playerSlotIndex,predictionGameMode,predictionGameModeAuthority,
                    act1OverrideRaw,orderedCharacters,explicitDiscoveredActs,schedule).ConfigureAwait(false)
                : null;
            return new() { ObservedAtUtc=DateTimeOffset.UtcNow, Groups=groups, SharedEvents=sharedEvents, EventAuthority=events,
                SharedAncients=ancients, UnlockedCharacters=characters, RelicCatalog=relics,
                ModernAuthority=modern, UnlockStateExact=unlockState is not null, UnlockSource=unlockSource,
                EncounterNumberOfRuns=unlockState is null ? null : ReadProperty(unlockState,"NumberOfRuns") as int? };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex,"WorldReflectionCaptureFailed",
                nameof(ReflectionNeowEffectSnapshotAdapter),"WorldCapture","issues.capture_failed",
                correlationId:$"profile={profile.ProfileId};character={character.CharacterKey.Serialized};slot={playerSlotIndex}");
            return new() { Failure = MissingWorld(profile.ProfileId,"WorldReflectionCaptureFailed:"+ex.GetType().Name) };
        }
    }
}
