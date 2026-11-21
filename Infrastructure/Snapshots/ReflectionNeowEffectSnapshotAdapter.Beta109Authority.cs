using RolltheSpire2.Core.Authority;
using System.Collections;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    internal sealed record Beta109StaticAuthorityCapture(
        IReadOnlyList<Beta109ActSelectionGroupSnapshot> ActSelectionGroups,
        bool ActSelectionAuthorityExact,
        bool IsMultiplayer,
        bool IsMultiplayerExact,
        bool TestModeIsOff,
        bool TestModeFactExact,
        bool TutorialBossOverrideWillApply,
        bool TutorialBossOverrideAuthorityExact,
        string Act1OverrideRaw,
        ModelKey? Act1OverrideResolvedKey,
        bool Act1OverrideExact,
        IReadOnlyList<Beta109LobbyPlayerSnapshot> LobbyPlayers,
        IReadOnlyList<ModelKey> AllCharactersInSourceOrder,
        bool RandomCharacterAuthorityExact,
        WorldGameMode GameMode,
        bool ModeFactsExact,
        WorldGameMode RequestedGameMode,
        bool RequestedGameModeExact,
        string GameModeEvidenceCode,
        IReadOnlyList<Beta109ActGenerationSnapshot> OrderedActs,
        IReadOnlyList<ModelKey> SharedEvents,
        bool SharedEventCatalogExact,
        Beta109EventCatalogAuthoritySnapshot EventAuthority,
        IReadOnlyList<ModelKey> AllSharedAncients,
        bool AllSharedAncientCatalogExact,
        IReadOnlyList<ModelKey> UnlockedSharedAncients,
        bool UnlockedSharedAncientCatalogExact,
        bool UnlockFactsExact,
        IReadOnlyList<Beta109RelicBucketSnapshot> SharedRelicBuckets,
        IReadOnlyList<Beta109RelicBucketSnapshot> PlayerRelicBuckets,
        bool SharedRelicPoolOrderExact,
        bool CharacterRelicPoolOrderExact,
        bool RelicRarityAuthorityExact,
        bool RelicShopEligibilityAuthorityExact,
        bool PlayerRelicPoolCompositionExact,
        string RelicAuthorityEvidenceCode,
        bool RelicInitializationExact,
        bool IsVanilla,
        string DiagnosticCode)
    {
        public int? InitialRelicShuffleCount { get; init; }
        public IReadOnlyList<int>? SharedAncientReferenceIds { get; init; }
        public bool WorldMethodsExact { get; init; }
        public string RoomReplayAssumption { get; init; } = string.Empty;
        public string InitialRelicReplayAssumption { get; init; } = string.Empty;
        public int? ObservedInitialRelicShuffleCount { get; init; }
        public Beta109AncientRuleObservation? AncientRules { get; init; }
    }

    private sealed record Beta109ActSourceGroup(
        int SourceActIndex,
        int ActNumber,
        IReadOnlyList<object> Models,
        bool SourceOrderExact);

    private sealed record Beta109WorldRelicPoolCapture(
        IReadOnlyList<Beta109RelicSourceEntry> Relics,
        bool Exact,
        bool ShopEligibilityExact,
        string EvidenceCode)
    {
        public IReadOnlySet<string>? ObservedRarities { get; init; }
        public bool AllRelicsFromGameAssembly { get; init; }
    }

    private sealed record Beta109EncounterCatalogCapture(
        IReadOnlyList<Beta109EncounterEntrySnapshot> Encounters,
        bool OrderExact,
        bool ClassificationExact,
        bool TagIdentityExact,
        bool TagComparerExact,
        bool ReferenceIdentityExact,
        bool WeightRuleExact,
        bool RetryShapeExact,
        bool OfficialVanillaCatalog,
        string EvidenceCode);

    private static readonly HashSet<string> Beta109KnownPureVanillaActTypes = new(
        new[]
        {
            "MegaCrit.Sts2.Core.Models.Acts.Overgrowth",
            "MegaCrit.Sts2.Core.Models.Acts.Hive",
            "MegaCrit.Sts2.Core.Models.Acts.Glory",
            "MegaCrit.Sts2.Core.Models.Acts.Underdocks",
            "MegaCrit.Sts2.Core.Models.Acts.DeprecatedAct"
        },
        StringComparer.Ordinal);

    private static MethodBase? UnknownRoomGenerationHook(IReadOnlySet<MethodBase> patched,
        IReadOnlyCollection<Type>? sharedAncientTypes, bool allLocalAncientsNative) =>
        patched.FirstOrDefault(method => method.DeclaringType?.FullName switch
        {
            "MegaCrit.Sts2.Core.Runs.RunManager" or "MegaCrit.Sts2.Core.Models.ActModel" when method.Name == "GenerateRooms" =>
                !WorldRoomPatchObservation.CanReplay(method, patched, sharedAncientTypes, hook =>
                    CanReplayCardGuardAncientFilter(hook, patched, sharedAncientTypes, allLocalAncientsNative)),
            "MegaCrit.Sts2.Core.Models.ActModel" => method.Name is "AddWithoutRepeatingTags" or
                "SetSharedAncientSubset" or "get_AllWeakEncounters" or "get_AllRegularEncounters" or
                "get_AllEliteEncounters" or "get_AllBossEncounters",
            "MegaCrit.Sts2.Core.Rooms.RoomSet" => method.Name is "set_Ancient" or "set_Boss" or "set_SecondBoss",
            "MegaCrit.Sts2.Core.Helpers.GrabBag`1" => method.Name is "Add" or "GrabAndRemove" or "GrabIndex",
            "MegaCrit.Sts2.Core.Random.Rng" => method.Name == "NextItem",
            _ => false
        });

    private static async Task<Beta109StaticAuthorityCapture> ObserveBeta109StaticAuthority(
        RuntimeProfileId profileId,
        Assembly assembly,
        Type modelDb,
        object? unlockState,
        CharacterIdentity character,
        int playersCount,
        int playerSlotIndex,
        WorldGameMode predictionGameMode,
        PredictionGameModeAuthority predictionGameModeAuthority,
        string act1OverrideRaw,
        IReadOnlyList<ModelKey>? orderedCharacters,
        IReadOnlyList<ModelKey>? explicitDiscoveredActs, NativeObservationSchedule schedule)
    {
        bool unlockStateExact = unlockState is not null;
        (bool runtimeIsMultiplayer, bool runtimeModeExact, string runtimeModeEvidence) = CaptureBeta109MultiplayerMode(assembly);
        PredictionGameModeResolution modeResolution = PredictionGameModeResolver.Resolve(
            predictionGameMode,
            predictionGameModeAuthority,
            playersCount,
            playerSlotIndex,
            runtimeIsMultiplayer,
            runtimeModeExact,
            runtimeModeEvidence);
        bool isMultiplayer = modeResolution.IsMultiplayer;
        bool multiplayerExact = modeResolution.IsExact;
        string multiplayerEvidence = modeResolution.EvidenceCode;
        (bool testModeIsOff, bool testModeExact) = CaptureBeta109TestMode(assembly);
        IReadOnlyList<Beta109ActSourceGroup> sourceGroups = CaptureBeta109ActSourceGroups(modelDb);
        (HashSet<ModelKey> discoveredActs, bool discoveredExact) = explicitDiscoveredActs == null
            ? CaptureBeta109DiscoveredActs(assembly)
            : CaptureDeclaredDiscoveredActs(explicitDiscoveredActs, sourceGroups);
        bool actSelectionMethodExact = !HarmonyLib.Harmony.GetAllPatchedMethods().Any(method =>
            method.DeclaringType?.FullName == "MegaCrit.Sts2.Core.Models.ActModel" && method.Name == "GetRandomList");
        bool sourceGroupsExact = actSelectionMethodExact && sourceGroups.Count > 0 && sourceGroups.All(group => group.SourceOrderExact);

        var selectionGroups = new List<Beta109ActSelectionGroupSnapshot>();
        foreach (Beta109ActSourceGroup group in sourceGroups.OrderBy(group => group.ActNumber))
        {
            var candidates = new List<Beta109ActSelectionCandidateSnapshot>();
            for (int ordinal = 0; ordinal < group.Models.Count; ordinal++)
            {
                object model = group.Models[ordinal];
                bool keyExact = TryModelKey(model, out ModelKey actKey);
                int? directIndex = TryReadInt(model, "Index", "ActIndex");
                bool? isDefault = TryReadBool(model, "IsDefault", "Default");
                (bool isUnlocked, bool unlockPredicateExact) = TryInvokeOfficialBooleanPredicate(
                    model,
                    "IsUnlocked",
                    unlockState);
                bool discovered = keyExact && discoveredActs.Contains(actKey);
                bool predicateExact = keyExact &&
                                      directIndex.HasValue &&
                                      directIndex.Value == group.SourceActIndex &&
                                      isDefault.HasValue &&
                                      unlockPredicateExact &&
                                      discoveredExact &&
                                      multiplayerExact &&
                                      testModeExact;
                bool deterministic = predicateExact &&
                                     isUnlocked &&
                                     !isDefault!.Value &&
                                     !isMultiplayer &&
                                     !discovered &&
                                     testModeIsOff;
                var candidateIssues = new List<string>();
                if (!keyExact) candidateIssues.Add("MissingActKey");
                if (!directIndex.HasValue || directIndex.Value != group.SourceActIndex)
                    candidateIssues.Add("MissingOrMismatchedActIndex");
                if (!isDefault.HasValue) candidateIssues.Add("MissingActIsDefault");
                if (!unlockPredicateExact) candidateIssues.Add("MissingActUnlockPredicate");
                if (!discoveredExact) candidateIssues.Add("MissingDiscoveredActs");
                if (!multiplayerExact) candidateIssues.Add(multiplayerEvidence);
                if (!testModeExact) candidateIssues.Add("MissingTestMode");
                candidates.Add(new Beta109ActSelectionCandidateSnapshot(
                    SourceActIndex: directIndex ?? group.SourceActIndex,
                    CandidateOrdinal: ordinal,
                    ActKey: actKey,
                    IsDefault: isDefault ?? false,
                    IsUnlocked: isUnlocked,
                    DiscoveredInSingleplayer: discovered,
                    DeterministicFirstEligible: deterministic,
                    PredicateExact: predicateExact,
                    EvidenceCode: predicateExact
                        ? "ModelDb.ActsByIndex+ActModel.IsUnlocked+" +
                          (explicitDiscoveredActs == null ? "Progress.DiscoveredActs" : "Declared.DiscoveredActs")
                        : string.Join("+", candidateIssues)));
            }

            Beta109ActSelectionCandidateSnapshot? deterministicFirst = candidates
                .FirstOrDefault(candidate => candidate.DeterministicFirstEligible);
            IReadOnlyList<ModelKey> eligible;
            Beta109ActSelectionMode mode;
            if (deterministicFirst is not null)
            {
                eligible = new[] { deterministicFirst.ActKey };
                mode = Beta109ActSelectionMode.DeterministicFirst;
            }
            else
            {
                eligible = candidates
                    .Where(candidate => candidate.IsUnlocked)
                    .Select(candidate => candidate.ActKey)
                    .Where(key => key.IsValid)
                    .ToArray();
                mode = eligible.Count > 0
                    ? Beta109ActSelectionMode.RandomNextItem
                    : Beta109ActSelectionMode.Unsupported;
            }

            bool groupExact = actSelectionMethodExact && group.SourceOrderExact &&
                              candidates.Count == group.Models.Count &&
                              candidates.All(candidate => candidate.PredicateExact) &&
                              eligible.Count > 0 &&
                              mode != Beta109ActSelectionMode.Unsupported;
            selectionGroups.Add(new Beta109ActSelectionGroupSnapshot(
                group.ActNumber,
                eligible,
                groupExact,
                mode,
                candidates));
        }

        await schedule.Checkpoint("WorldCharacters").ConfigureAwait(false);
        ModelListCapture allCharactersCapture = ReadStaticModelsWorld(modelDb, "AllCharacters", "Characters");
        bool partyExact = orderedCharacters is not null && orderedCharacters.Count == playersCount &&
            playerSlotIndex >= 0 && playerSlotIndex < playersCount && orderedCharacters[playerSlotIndex] == character.CharacterKey &&
            orderedCharacters.All(k => k.IsValid);
        bool lobbyPlayersExact = partyExact || playersCount == 1 && character.CharacterKey.IsValid;
        IReadOnlyList<Beta109LobbyPlayerSnapshot> lobbyPlayers = partyExact
            ? orderedCharacters!.Select((key, slot) => new Beta109LobbyPlayerSnapshot(slot, key, false, true)).ToArray()
            : lobbyPlayersExact
            ? new[] { new Beta109LobbyPlayerSnapshot(0, character.CharacterKey, IsRandomCharacter: false, Exact: true) }
            : new[] { new Beta109LobbyPlayerSnapshot(0, character.CharacterKey, IsRandomCharacter: false, Exact: false) };
        bool randomCharacterAuthorityExact = lobbyPlayersExact ||
                                             (allCharactersCapture.Exact && allCharactersCapture.Keys.Count > 0);

        string normalizedOverride = string.IsNullOrWhiteSpace(act1OverrideRaw)
            ? string.Empty
            : act1OverrideRaw.Trim();
        ModelKey? overrideKey = ResolveBeta109Act1Override(normalizedOverride, selectionGroups);
        string overrideToken = NormalizeTokenWorld(normalizedOverride);
        bool act1OverrideExact = overrideToken == "RANDOM" ||
                                 (overrideToken is "OVERGROWTH" or "UNDERDOCKS" &&
                                  overrideKey is ModelKey resolvedOverride && resolvedOverride.IsValid);

        bool singleplayerRequestExact = predictionGameModeAuthority == PredictionGameModeAuthority.ExplicitRequest &&
                                        predictionGameMode == WorldGameMode.Singleplayer &&
                                        playersCount == 1 &&
                                        playerSlotIndex == 0 &&
                                        multiplayerExact &&
                                        !isMultiplayer;
        bool multiplayerRequestExact = predictionGameModeAuthority == PredictionGameModeAuthority.ExplicitRequest &&
                                       predictionGameMode == WorldGameMode.Multiplayer &&
                                       playersCount > 1 &&
                                       multiplayerExact &&
                                       isMultiplayer;
        WorldGameMode gameMode = !testModeExact
            ? WorldGameMode.Unknown
            : !testModeIsOff
                ? WorldGameMode.Test
                : singleplayerRequestExact
                    ? WorldGameMode.Singleplayer
                    : multiplayerRequestExact
                        ? WorldGameMode.Multiplayer
                        : runtimeModeExact
                            ? runtimeIsMultiplayer ? WorldGameMode.Multiplayer : WorldGameMode.Singleplayer
                            : WorldGameMode.Unknown;
        bool modeFactsExact = testModeExact &&
                              multiplayerExact &&
                              gameMode != WorldGameMode.Unknown &&
                              playersCount > 0;

        var sharedEventEvidence = new SharedEventCaptureEvidence(assembly);
        ModelListCapture sharedEventsRawCapture = CapturePossiblyEmptyStaticModelsWorld(
            modelDb,
            sharedEventEvidence,
            "AllSharedEvents",
            "SharedEvents");
        Beta109EventCatalogAuthoritySnapshot eventAuthority = CaptureBeta109EventAuthority(
            assembly,
            unlockState,
            sharedEventsRawCapture);
        var (orderedActs, allLocalAncientsNative) = await ObserveBeta109ActGenerationSnapshots(
            sourceGroups,
            unlockState,
            isMultiplayer,
            modeFactsExact,
            eventAuthority, schedule).ConfigureAwait(false);
        bool eventCatalogExact = eventAuthority.HasExactFilteringAuthority &&
                                 orderedActs.Count > 0 &&
                                 orderedActs.All(act => act.EventRngConsumptionExact);
        await schedule.Checkpoint("WorldAncients").ConfigureAwait(false);
        ModelListCapture allSharedAncientsCapture = CapturePossiblyEmptyStaticModelsWorld(
            modelDb,
            "AllSharedAncients",
            "SharedAncients",
            "AllSharedAncientEvents");
        ModelListCapture unlockedSharedAncientsCapture = CapturePossiblyEmptyModelListWorld(
            unlockState is null ? null : ReadProperty(unlockState, "SharedAncients"), captureReferences: true);

        object? characterModel = unlockState is null
            ? null
            : FindCharacterModel(assembly, unlockState, character.CharacterKey, out _);
        // Character identity provenance is not the authority for relic-bag replay.
        // We invoke the live runtime CharacterRelicPool.GetUnlockedRelics and preserve
        // its returned order/rarity. A modded Character can therefore contribute an
        // exact runtime relic source when that structural capture itself is complete.
        // The requested player is fixed. Populate reads that registered character's
        // relic pool with this unlock state even if the account cannot select them.
        bool characterAuthorityExact = characterModel is not null;
        await schedule.Checkpoint("WorldRelicPools").ConfigureAwait(false);
        Beta109WorldRelicPoolCapture sharedRelics = CaptureBeta109SharedRelicPool(
            assembly,
            unlockState);
        Beta109WorldRelicPoolCapture characterRelics = CaptureBeta109CharacterRelicPool(
            characterModel,
            unlockState);
        IReadOnlyList<Beta109RelicBucketSnapshot> sharedBuckets = Beta109RelicBagInitializer.BuildBuckets(
            sharedRelics.Relics,
            "shared",
            filterPlayerGrabBagRarities: false);
        IReadOnlyList<Beta109RelicBucketSnapshot> playerBuckets = Beta109RelicBagInitializer.BuildBuckets(
            sharedRelics.Relics.Concat(characterRelics.Relics),
            "player",
            filterPlayerGrabBagRarities: true);
        bool sharedRelicPoolOrderExact = sharedRelics.Exact;
        bool characterRelicPoolOrderExact = characterAuthorityExact && characterRelics.Exact;
        bool relicRarityAuthorityExact = sharedRelics.ObservedRarities != null && characterRelics.ObservedRarities != null;
        bool relicShopEligibilityAuthorityExact =
            sharedRelics.ShopEligibilityExact && characterRelics.ShopEligibilityExact;
        bool playerRelicPoolCompositionExact = sharedRelicPoolOrderExact && characterRelicPoolOrderExact;
        bool relicBucketOrderExact = playerRelicPoolCompositionExact &&
                                     sharedBuckets.All(bucket => bucket.OrderExact) &&
                                     playerBuckets.All(bucket => bucket.OrderExact);
        int? initialRelicShuffleCount = multiplayerExact && !isMultiplayer && playersCount == 1 && characterAuthorityExact &&
            sharedRelics.ObservedRarities is { } sharedRarities && characterRelics.ObservedRarities is { } characterRarities
            ? sharedRarities.Count + sharedRarities.Union(characterRarities).Count(Beta109RelicBagInitializer.IsPlayerGrabBagRarity)
            : null;
        var patchedMethods = HarmonyLib.Harmony.GetAllPatchedMethods().ToHashSet();
        eventAuthority = eventAuthority with
        {
            LockedEventsAfterShuffle = WorldRoomPatchObservation.CaptureLockedEvents(patchedMethods,
                unlockState as MegaCrit.Sts2.Core.Unlocks.UnlockState,
                orderedActs.SelectMany(act => act.OrderedEligibleEvents).ToHashSet())
        };
        MethodBase? unknownBagHook = UnknownRelicInitializationHook(patchedMethods,
            sharedRelics.AllRelicsFromGameAssembly, characterRelics.AllRelicsFromGameAssembly);
        bool bagMethodsExact = unknownBagHook is null;
        int? observedShuffleCount = initialRelicShuffleCount;
        if (!bagMethodsExact) initialRelicShuffleCount = null;
        bool relicInitializationExact = bagMethodsExact && playerRelicPoolCompositionExact &&
                                        relicRarityAuthorityExact &&
                                        relicBucketOrderExact;
        string relicAuthorityEvidenceCode = string.Join("|", new[]
        {
            sharedRelics.EvidenceCode,
            characterRelics.EvidenceCode,
            sharedRelicPoolOrderExact ? "SharedRelicPoolOrderExact" : "MissingSharedRelicPoolOrder",
            characterRelicPoolOrderExact ? "CharacterRelicPoolOrderExact" : "MissingCharacterRelicPoolOrder",
            relicRarityAuthorityExact ? "RelicRarityAuthorityExact" : "MissingRelicRarityAuthority",
            relicShopEligibilityAuthorityExact ? "RelicShopEligibilityAuthorityExact" : "MissingRelicShopEligibilityAuthority",
            playerRelicPoolCompositionExact ? "PlayerRelicPoolCompositionExact" : "MissingPlayerRelicPoolComposition",
            relicBucketOrderExact ? "RelicBucketOrderExact" : "MissingRelicBucketOrder",
            "InitialRelicShuffleCount=" + initialRelicShuffleCount,
            bagMethodsExact ? "NativeBagInitialization" :
                $"RelicInitializationMethodPatched:{unknownBagHook!.DeclaringType?.FullName}.{unknownBagHook.Name}"
        });

        bool actsExact = orderedActs.Count > 0 && orderedActs.All(act => act.HasExactGenerationInputs);
        bool actSelectionExact = sourceGroupsExact &&
                                 unlockStateExact &&
                                 discoveredExact &&
                                 multiplayerExact &&
                                 testModeExact &&
                                 act1OverrideExact &&
                                 lobbyPlayersExact &&
                                 selectionGroups.Count > 0 &&
                                 selectionGroups.All(group => group.EligibilityAndOrderExact);
        // World unlock authority is intentionally independent from relic-bag
        // initialization. A missing relic rarity or pool order must not be reported
        // as MissingUnlockAuthority and must not contaminate otherwise exact Act,
        // Event, or Ancient unlock facts.
        bool unlockFactsExact = unlockStateExact && actSelectionExact;
        var unknownRoomHook = UnknownRoomGenerationHook(patchedMethods,
            unlockedSharedAncientsCapture.AncientTypes, allLocalAncientsNative);
        bool worldMethodsExact = bagMethodsExact && unknownRoomHook is null;

        var issues = new List<string>();
        if (!actSelectionMethodExact) issues.Add("UnknownActSelectionHook");
        if (sourceGroups.Count == 0) issues.Add("MissingActCatalog");
        else if (!sourceGroupsExact) issues.Add("MissingActsByIndexOrder");
        if (!unlockStateExact) issues.Add("MissingUnlockState");
        if (!discoveredExact) issues.Add("MissingDiscoveredActs");
        if (!multiplayerExact) issues.Add(multiplayerEvidence);
        if (!testModeExact) issues.Add("MissingTestMode");
        if (!act1OverrideExact) issues.Add("MissingAct1Override");
        if (!lobbyPlayersExact) issues.Add("MissingLobbyPlayerAuthority");
        foreach (Beta109ActSelectionGroupSnapshot group in selectionGroups.Where(group => !group.EligibilityAndOrderExact))
        {
            string candidateEvidence = string.Join(",", group.Candidates
                .Where(candidate => !candidate.PredicateExact)
                .Select(candidate => candidate.EvidenceCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.Ordinal));
            issues.Add(string.IsNullOrWhiteSpace(candidateEvidence)
                ? $"ActSelectionGroupIncomplete:Act{group.Act}"
                : $"ActSelectionGroupIncomplete:Act{group.Act}:{candidateEvidence}");
        }
        if (!actSelectionExact && issues.Count == 0) issues.Add("ActSelectionAuthorityIncomplete");
        if (!modeFactsExact) issues.Add("ModeFactsIncomplete");
        issues.Add("MissingTutorialBossOverrideAuthority");
        if (!actsExact) issues.Add(DetermineBeta109ActCatalogIssue(orderedActs));
        if (!eventAuthority.SharedRawOrderExact) issues.Add("MissingSharedEventCatalog");
        if (!eventAuthority.RequiredEpochMembershipExact) issues.Add("MissingEventEpochMembership");
        if (!eventAuthority.EpochRevealFactsExact) issues.Add("MissingEventEpochAuthority");
        if (!eventCatalogExact) issues.Add("MissingEventCatalog");
        if (!unlockedSharedAncientsCapture.Exact) issues.Add("MissingUnlockedSharedAncientCatalog");
        if (!sharedRelicPoolOrderExact) issues.Add("MissingSharedRelicPoolOrder:" + sharedRelics.EvidenceCode);
        if (!characterRelicPoolOrderExact) issues.Add("MissingCharacterRelicPoolOrder:" + characterRelics.EvidenceCode);
        if (!relicRarityAuthorityExact) issues.Add("MissingRelicRarityAuthority");
        if (!relicShopEligibilityAuthorityExact) issues.Add("MissingRelicShopEligibilityAuthority");
        if (!playerRelicPoolCompositionExact) issues.Add("MissingPlayerRelicPoolComposition");
        if (!relicBucketOrderExact) issues.Add("MissingRelicBucketOrder");
        if (!relicInitializationExact) issues.Add("MissingRelicInitialization");
        if (!unlockFactsExact) issues.Add("MissingUnlockAuthority");
        if (!worldMethodsExact) issues.Add("UnknownWorldGenerationHook");

        sharedEventEvidence.Write(profileId.ToString(), character.CharacterKey.Serialized, modelDb,
            sharedEventsRawCapture.Exact, eventCatalogExact, issues, patchedMethods, unknownRoomHook, unknownBagHook);

        return new Beta109StaticAuthorityCapture(
            ActSelectionGroups: selectionGroups,
            ActSelectionAuthorityExact: actSelectionExact,
            IsMultiplayer: isMultiplayer,
            IsMultiplayerExact: multiplayerExact,
            TestModeIsOff: testModeIsOff,
            TestModeFactExact: testModeExact,
            TutorialBossOverrideWillApply: false,
            TutorialBossOverrideAuthorityExact: false,
            Act1OverrideRaw: normalizedOverride,
            Act1OverrideResolvedKey: overrideKey,
            Act1OverrideExact: act1OverrideExact,
            LobbyPlayers: lobbyPlayers,
            AllCharactersInSourceOrder: allCharactersCapture.Keys,
            RandomCharacterAuthorityExact: randomCharacterAuthorityExact,
            GameMode: gameMode,
            ModeFactsExact: modeFactsExact,
            RequestedGameMode: predictionGameMode,
            RequestedGameModeExact: modeResolution.RequestedGameModeExact,
            GameModeEvidenceCode: multiplayerEvidence,
            OrderedActs: orderedActs,
            SharedEvents: sharedEventsRawCapture.Keys,
            SharedEventCatalogExact: eventCatalogExact,
            EventAuthority: eventAuthority,
            AllSharedAncients: allSharedAncientsCapture.Keys,
            AllSharedAncientCatalogExact: allSharedAncientsCapture.Exact,
            UnlockedSharedAncients: unlockedSharedAncientsCapture.Keys,
            UnlockedSharedAncientCatalogExact: unlockedSharedAncientsCapture.Exact,
            UnlockFactsExact: unlockFactsExact,
            SharedRelicBuckets: sharedBuckets,
            PlayerRelicBuckets: playerBuckets,
            SharedRelicPoolOrderExact: sharedRelicPoolOrderExact,
            CharacterRelicPoolOrderExact: characterRelicPoolOrderExact,
            RelicRarityAuthorityExact: relicRarityAuthorityExact,
            RelicShopEligibilityAuthorityExact: relicShopEligibilityAuthorityExact,
            PlayerRelicPoolCompositionExact: playerRelicPoolCompositionExact,
            RelicAuthorityEvidenceCode: relicAuthorityEvidenceCode,
            RelicInitializationExact: relicInitializationExact,
            IsVanilla: true,
            DiagnosticCode: issues.Count == 0
                ? profileId switch
                {
                    RuntimeProfileId.Beta111 => "Beta111StaticRuntimeAuthorityCaptured",
                    RuntimeProfileId.Beta110 => "Beta110StaticRuntimeAuthorityCaptured",
                    _ => "Beta109HistoricalStaticRuntimeAuthorityCaptured"
                }
                : string.Join("|", issues.Distinct(StringComparer.Ordinal)))
        {
            InitialRelicShuffleCount = initialRelicShuffleCount,
            SharedAncientReferenceIds = unlockedSharedAncientsCapture.ReferenceIds,
            WorldMethodsExact = worldMethodsExact,
            RoomReplayAssumption = unknownRoomHook is null ? string.Empty :
                $"RoomGenerationMethodPatched:{unknownRoomHook.DeclaringType?.FullName}.{unknownRoomHook.Name}",
            InitialRelicReplayAssumption = unknownBagHook is null ? string.Empty :
                $"RelicInitializationMethodPatched:{unknownBagHook.DeclaringType?.FullName}.{unknownBagHook.Name}",
            ObservedInitialRelicShuffleCount = observedShuffleCount,
            AncientRules = ObserveAncientRules(modelDb, unlockedSharedAncientsCapture.UnknownAncientGenerators)
        };
    }

    private static IReadOnlyList<Beta109ActSourceGroup> CaptureBeta109ActSourceGroups(Type modelDb)
    {
        // ActModel.GetRandomList consumes ModelDb.ActsByIndex directly. Reading the
        // flat Acts catalog and regrouping it is not equivalent authority: it can
        // lose the exact inner-list order and, on some runtime builds, the flat
        // property is not exposed under the reflected alias at all.
        object? actsByIndex = ReadStaticMemberWorld(modelDb, "ActsByIndex");
        if (actsByIndex is not null)
        {
            var captured = new List<(int SourceIndex, int OuterOrdinal, IReadOnlyList<object> Models, bool Exact)>();
            int outerOrdinal = 0;
            foreach (object item in Enumerate(actsByIndex))
            {
                object? value = item;
                int sourceIndex = int.MinValue;
                if (TryReadKeyValueWorld(item, out int keyedIndex, out object? keyedValue))
                {
                    sourceIndex = keyedIndex;
                    value = keyedValue;
                }

                List<object> models = Enumerate(value).ToList();
                if (models.Count == 0)
                {
                    outerOrdinal++;
                    continue;
                }

                int? modelIndex = null;
                bool exact = true;
                foreach (object model in models)
                {
                    int? candidateIndex = TryReadInt(model, "Index", "ActIndex");
                    bool candidateExact = candidateIndex.HasValue &&
                                          TryModelKey(model, out _);
                    exact &= candidateExact;
                    if (!candidateIndex.HasValue) continue;
                    if (!modelIndex.HasValue) modelIndex = candidateIndex.Value;
                    else if (modelIndex.Value != candidateIndex.Value) exact = false;
                }

                if (sourceIndex == int.MinValue)
                {
                    sourceIndex = modelIndex ?? outerOrdinal;
                }
                else if (modelIndex.HasValue && modelIndex.Value != sourceIndex)
                {
                    exact = false;
                }

                captured.Add((sourceIndex, outerOrdinal, models, exact));
                outerOrdinal++;
            }

            if (captured.Count > 0)
            {
                int minIndex = captured.Min(group => group.SourceIndex);
                bool zeroBased = minIndex == 0;
                return captured
                    .OrderBy(group => group.SourceIndex)
                    .ThenBy(group => group.OuterOrdinal)
                    .Select(group => new Beta109ActSourceGroup(
                        SourceActIndex: group.SourceIndex,
                        ActNumber: zeroBased ? group.SourceIndex + 1 : group.SourceIndex,
                        Models: group.Models,
                        SourceOrderExact: group.Exact))
                    .ToArray();
            }
        }

        // A flat catalog remains useful for diagnostics, but it is deliberately
        // non-exact because it cannot prove the source order consumed by
        // ActModel.GetRandomList.
        object? fallbackSource = ReadStaticMemberWorld(modelDb, "Acts") ??
                                 ReadStaticMemberWorld(modelDb, "AllActs");
        List<object> orderedActs = Enumerate(fallbackSource).ToList();
        if (orderedActs.Count == 0)
        {
            return Array.Empty<Beta109ActSourceGroup>();
        }

        var indexed = new List<(object Model, int Index)>();
        foreach (object model in orderedActs)
        {
            int? index = TryReadInt(model, "Index", "ActIndex");
            if (!index.HasValue || !TryModelKey(model, out _))
            {
                return Array.Empty<Beta109ActSourceGroup>();
            }
            indexed.Add((model, index.Value));
        }

        int fallbackMinIndex = indexed.Min(item => item.Index);
        bool fallbackZeroBased = fallbackMinIndex == 0;
        return indexed
            .GroupBy(item => item.Index)
            .OrderBy(group => group.Key)
            .Select(group => new Beta109ActSourceGroup(
                SourceActIndex: group.Key,
                ActNumber: fallbackZeroBased ? group.Key + 1 : group.Key,
                Models: group.Select(item => item.Model).ToArray(),
                SourceOrderExact: false))
            .ToArray();
    }

    private static async Task<(IReadOnlyList<Beta109ActGenerationSnapshot> Acts, bool AllLocalAncientsNative)> ObserveBeta109ActGenerationSnapshots(
        IReadOnlyList<Beta109ActSourceGroup> groups,
        object? unlockState,
        bool isMultiplayer,
        bool modeFactsExact,
        Beta109EventCatalogAuthoritySnapshot eventAuthority, NativeObservationSchedule schedule)
    {
        var output = new List<Beta109ActGenerationSnapshot>();
        bool allLocalAncientsNative = true;
        foreach (Beta109ActSourceGroup group in groups.OrderBy(group => group.ActNumber))
        {
            foreach (object model in group.Models)
            {
                await schedule.Checkpoint("ActGeneration").ConfigureAwait(false);
                if (!TryModelKey(model, out ModelKey actKey))
                {
                    allLocalAncientsNative = false;
                    continue;
                }

                ModelListCapture rawEvents = CapturePossiblyEmptyInstanceModelsWorld(
                    model,
                    "AllEvents",
                    "Events");
                IReadOnlyList<ModelKey> eligibleEvents = FilterBeta109Events(
                    rawEvents.Keys,
                    eventAuthority.OrderedSharedEventsRaw,
                    eventAuthority);
                bool eventEligibleExact = rawEvents.Exact && eventAuthority.HasExactFilteringAuthority;
                ModelListCapture ancients = ReadUnlockedInstanceModelsWorld(
                    model,
                    unlockState,
                    "GetUnlockedAncients",
                    "AllAncients",
                    "Ancients",
                    "AncientEvents");
                allLocalAncientsNative &= ancients.Exact && ancients.AncientTypes is { } types &&
                    types.All(t => t.Assembly == typeof(MegaCrit.Sts2.Core.Models.AncientEventModel).Assembly);
                Beta109EncounterCatalogCapture encounterCapture =
                    await ObserveBeta109GenerateAllEncounters(model, schedule).ConfigureAwait(false);
                IReadOnlyList<Beta109EncounterEntrySnapshot> encounters =
                    encounterCapture.Encounters;
                bool encounterOrderExact = encounterCapture.OrderExact;
                (int weakSlots, int totalRooms, bool roomShapeExact) = CaptureBeta109RoomShape(
                    model,
                    isMultiplayer,
                    modeFactsExact);

                bool official = IsOfficialSts2ModelWorld(model);
                bool catalogExact = official &&
                                    ancients.Exact &&
                                    encounterCapture.OrderExact &&
                                    encounterCapture.ClassificationExact &&
                                    encounterCapture.TagIdentityExact &&
                                    encounterCapture.TagComparerExact &&
                                    encounterCapture.ReferenceIdentityExact &&
                                    encounterCapture.WeightRuleExact &&
                                    encounterCapture.RetryShapeExact &&
                                    encounterCapture.OfficialVanillaCatalog;
                output.Add(new Beta109ActGenerationSnapshot(
                    group.ActNumber,
                    actKey,
                    weakSlots,
                    totalRooms,
                    rawEvents.Keys,
                    encounters,
                    ancients.Keys,
                    CatalogOrderExact: catalogExact,
                    UnlockFilteringExact: ancients.Exact,
                    RoomShapeExact: roomShapeExact,
                    GenerateAllEncountersOrderExact: encounterOrderExact,
                    EncounterTagComparerExact: encounterCapture.TagComparerExact,
                    EncounterWeightModelExact: encounterCapture.WeightRuleExact,
                    EncounterClassificationExact: encounterCapture.ClassificationExact,
                    EncounterTagIdentityExact: encounterCapture.TagIdentityExact,
                    EncounterReferenceIdentityExact: encounterCapture.ReferenceIdentityExact,
                    EncounterRetryShapeExact: encounterCapture.RetryShapeExact,
                    EliteEncounterSlots: 15,
                    EncounterCatalogOfficialVanilla: encounterCapture.OfficialVanillaCatalog,
                    EncounterAuthorityEvidenceCode: encounterCapture.EvidenceCode)
                {
                    OrderedRawEvents = rawEvents.Keys,
                    OrderedEligibleEvents = eligibleEvents,
                    UnlockedAncientOrderExact = ancients.Exact,
                    AncientsWithUnknownGenerator = ancients.UnknownAncientGenerators,
                    RawEventCatalogOrderExact = rawEvents.Exact,
                    EventEpochMembershipExact = eventAuthority.RequiredEpochMembershipExact,
                    EventEpochRevealFactsExact = eventAuthority.EpochRevealFactsExact,
                    EligibleEventOrderExact = eventEligibleExact
                });
            }
        }
        return (output, allLocalAncientsNative);
    }

    private static async Task<Beta109EncounterCatalogCapture> ObserveBeta109GenerateAllEncounters(
        object actModel, NativeObservationSchedule schedule)
    {
        Type actType = actModel.GetType();
        string fullName = actType.FullName ?? string.Empty;
        bool knownPureVanillaAct = Beta109KnownPureVanillaActTypes.Contains(fullName) &&
                                   IsOfficialSts2ModelWorld(actModel);
        if (!knownPureVanillaAct)
        {
            return new Beta109EncounterCatalogCapture(
                Array.Empty<Beta109EncounterEntrySnapshot>(),
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                "UnsupportedModEncounter");
        }

        // Read the same cached enumerable used by the native partition getters.
        object? source = ReadProperty(actModel, "AllEncounters");
        if (source is not System.Collections.IEnumerable enumerable || source is string)
            return new Beta109EncounterCatalogCapture([], false, false, false, false, false, false, false, true,
                "MissingGenerateAllEncountersMember");
        var models = new List<object>();
        try
        {
            foreach (object? encounter in enumerable)
            {
                if (encounter is null)
                    return new Beta109EncounterCatalogCapture([], false, false, false, false, false, false, false, true,
                        "NullEncounterCatalogEntry");
                models.Add(encounter);
            }
        }
        catch (Exception ex)
        {
            RecordCaptureFailure(ex, fullName + ".AllEncounters");
            return new Beta109EncounterCatalogCapture([], false, false, false, false, false, false, false, true,
                "EncounterCatalogEnumerationFailed");
        }

        var referenceIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        int nextReferenceId = 0;
        var baseEntries = new List<Beta109EncounterEntrySnapshot>(models.Count);
        bool classificationExact = true;
        bool tagIdentityExact = true;
        for (int ordinal = 0; ordinal < models.Count; ordinal++)
        {
            await schedule.Checkpoint("EncounterProperties:" + ordinal).ConfigureAwait(false);
            object model = models[ordinal];
            if (!referenceIds.TryGetValue(model, out int referenceId))
            {
                referenceId = nextReferenceId++;
                referenceIds.Add(model, referenceId);
            }

            if (!TryModelKey(model, out ModelKey key) ||
                !TryParseBeta109RoomType(ReadEnumName(model, "RoomType"), out Beta109RoomType roomType))
            {
                classificationExact = false;
                continue;
            }

            bool? isWeak = TryReadBool(model, "IsWeak", "Weak");
            string[] tags = CaptureExactTagTokens(model, out bool tagsExact);
            classificationExact &= isWeak.HasValue;
            tagIdentityExact &= tagsExact;
            baseEntries.Add(new Beta109EncounterEntrySnapshot(
                EncounterKey: key,
                RoomType: roomType,
                IsWeak: isWeak ?? false,
                Tags: tags,
                Weight: 1d,
                SharesTagsWith: Array.Empty<ModelKey>(),
                TagComparisonExact: false,
                SourceOrdinal: ordinal,
                ReferenceIdentityId: referenceId,
                SharesTagsWithSourceOrdinals: Array.Empty<int>(),
                TagIdentityExact: tagsExact,
                ClassificationExact: isWeak.HasValue,
                ReferenceIdentityExact: true,
                IsOfficialVanilla: IsOfficialSts2ModelWorld(model)));
        }

        bool orderExact = classificationExact &&
                          baseEntries.Count == models.Count &&
                          baseEntries.Select(entry => entry.SourceOrdinal)
                              .SequenceEqual(Enumerable.Range(0, models.Count));
        if (!orderExact)
        {
            return new Beta109EncounterCatalogCapture(
                baseEntries,
                false,
                classificationExact,
                tagIdentityExact,
                false,
                false,
                true,
                true,
                true,
                "EncounterCatalogProjectionIncomplete");
        }

        bool comparerExact = true;
        var withConflicts = new List<Beta109EncounterEntrySnapshot>(baseEntries.Count);
        var nativeEncounterType = actType.Assembly.GetType("MegaCrit.Sts2.Core.Models.EncounterModel");
        var sharesTagsWith = nativeEncounterType?.GetMethod("SharesTagsWith", [nativeEncounterType]);
        for (int i = 0; i < models.Count; i++)
        {
            await schedule.Checkpoint("EncounterComparerRow:" + i).ConfigureAwait(false);
            if (sharesTagsWith is null)
            {
                comparerExact = false;
                withConflicts.Add(baseEntries[i]);
                continue;
            }

            var conflictKeys = new List<ModelKey>();
            var conflictOrdinals = new List<int>();
            for (int j = 0; j < models.Count; j++)
            {
                try
                {
                    object? result = sharesTagsWith.Invoke(models[i], new[] { models[j] });
                    if (result is not bool shares)
                    {
                        comparerExact = false;
                        continue;
                    }
                    if (shares)
                    {
                        conflictKeys.Add(baseEntries[j].EncounterKey);
                        conflictOrdinals.Add(baseEntries[j].SourceOrdinal);
                    }
                }
                catch (Exception ex)
                {
                    if (comparerExact)
                        Diagnostics.RuntimeIssueLedger.Current.Record(ex, "EncounterTagComparisonFailed",
                            nameof(ReflectionNeowEffectSnapshotAdapter), "WorldEncounterCapture", "issues.capture_failed",
                            correlationId: $"{fullName}:{baseEntries[i].EncounterKey}:{baseEntries[j].EncounterKey}");
                    comparerExact = false;
                }
            }

            withConflicts.Add(baseEntries[i] with
            {
                SharesTagsWith = conflictKeys,
                SharesTagsWithSourceOrdinals = conflictOrdinals
            });
        }

        // The source predicate calls EncounterModel.SharesTagsWith. An exact
        // pairwise result matrix is therefore a sufficient immutable worker
        // primitive even when EncounterTag itself cannot be reduced to a stable
        // string/enum token. Keep the best-effort tokens for diagnostics, but
        // base production tag authority on the captured comparer matrix.
        bool replayTagAuthorityExact = comparerExact;
        withConflicts = withConflicts
            .Select(entry => entry with
            {
                TagComparisonExact = replayTagAuthorityExact,
                TagIdentityExact = replayTagAuthorityExact
            })
            .ToList();

        return new Beta109EncounterCatalogCapture(
            withConflicts,
            OrderExact: true,
            ClassificationExact: classificationExact,
            TagIdentityExact: replayTagAuthorityExact,
            TagComparerExact: comparerExact,
            ReferenceIdentityExact: true,
            WeightRuleExact: true,
            RetryShapeExact: true,
            OfficialVanillaCatalog: true,
            EvidenceCode: "Beta109VanillaGenerateAllEncountersDirectSource");
    }

    private static (int WeakSlots, int TotalRooms, bool Exact) CaptureBeta109RoomShape(
        object actModel,
        bool isMultiplayer,
        bool modeFactsExact)
    {
        int? weak = TryReadInt(actModel, "NumberOfWeakEncounters", "NumWeakEncounters");
        MethodInfo? method = actModel.GetType().GetMethods(PublicInstance | BindingFlags.NonPublic)
            .Where(candidate => string.Equals(candidate.Name, "GetNumberOfRooms", StringComparison.Ordinal))
            .FirstOrDefault(candidate =>
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
            });
        int? total = null;
        if (method is not null && modeFactsExact)
        {
            try
            {
                object? value = method.Invoke(actModel, new object[] { isMultiplayer });
                if (value is int direct) total = direct;
                else if (value is IConvertible convertible)
                    total = convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                total = null;
            }
        }
        bool exact = IsOfficialSts2ModelWorld(actModel) && weak.HasValue && total.HasValue;
        return (Math.Max(0, weak ?? 0), Math.Max(weak ?? 0, total ?? 0), exact);
    }

    private static ModelListCapture CaptureBeta109UnlockedStaticModels(
        Type modelDb,
        object? unlockState,
        params string[] names)
    {
        object? value = names.Select(name => ReadStaticMemberWorld(modelDb, name))
            .FirstOrDefault(candidate => candidate is not null);
        List<object> models = Enumerate(value).ToList();
        if (value is null) return new ModelListCapture(Array.Empty<ModelKey>(), false);
        var keys = new List<ModelKey>();
        bool exact = unlockState is not null;
        foreach (object model in models)
        {
            if (!TryModelKey(model, out ModelKey key) || !IsOfficialSts2ModelWorld(model))
            {
                exact = false;
                continue;
            }
            (bool unlocked, bool predicateExact) = TryInvokeOptionalOfficialBooleanPredicate(
                model,
                "IsUnlocked",
                unlockState);
            exact &= predicateExact;
            if (unlocked) keys.Add(key);
        }
        return new ModelListCapture(keys, exact && keys.Count <= models.Count);
    }

    private static ModelListCapture CapturePossiblyEmptyInstanceModelsWorld(object owner, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadProperty(owner, name);
            if (value is not null) return CapturePossiblyEmptyModelListWorld(value);
        }
        return new ModelListCapture(Array.Empty<ModelKey>(), false);
    }

    private static ModelListCapture CapturePossiblyEmptyStaticModelsWorld(Type owner, params string[] names)
        => CapturePossiblyEmptyStaticModelsWorld(owner, null, names);

    private static ModelListCapture CapturePossiblyEmptyStaticModelsWorld(Type owner,
        SharedEventCaptureEvidence? evidence, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadStaticMemberWorld(owner, name);
            evidence?.ObserveSource(name, value);
            if (value is not null) return CapturePossiblyEmptyModelListWorld(value, evidence: evidence);
        }
        return new ModelListCapture(Array.Empty<ModelKey>(), false);
    }

    private static ModelListCapture CapturePossiblyEmptyModelListWorld(object? value, bool captureReferences = false,
        bool captureAncientTypes = false, SharedEventCaptureEvidence? evidence = null)
    {
        if (value is not System.Collections.IEnumerable source || value is string)
            return new ModelListCapture([], false);
        var keys = new List<ModelKey>();
        var referenceIds = captureReferences ? new Dictionary<object, int>(ReferenceEqualityComparer.Instance) : null;
        var occurrences = captureReferences ? new List<int>() : null;
        var unknownGenerators = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        HashSet<Type>? ancientTypes = captureAncientTypes || captureReferences ? [] : null;
        bool exact = true;
        try
        {
            foreach (object? model in source)
            {
                bool hasKey = TryModelKey(model, out ModelKey key);
                evidence?.ObserveEntry(model, hasKey ? key.Serialized : null);
                if (!hasKey) { exact = false; continue; }
                keys.Add(key);
                if (ancientTypes is not null)
                {
                    if (model is MegaCrit.Sts2.Core.Models.AncientEventModel ancient) ancientTypes.Add(ancient.GetType());
                    else ancientTypes = null;
                }
                if (referenceIds is not null)
                {
                    if (!referenceIds.TryGetValue(model!, out int id)) referenceIds[model!] = id = referenceIds.Count;
                    occurrences!.Add(id);
                }
                if (key.Category == BaseGameModelKeys.Categories.Event &&
                    Beta109AncientRuntimeTypes.TryGetValue(NormalizeAncientEntry(key.Entry), out var name) &&
                    (model!.GetType().FullName != name || !IsOfficialSts2ModelWorld(model)))
                    unknownGenerators.Add(key);
            }
        }
        catch (Exception ex)
        {
            evidence?.EnumerationFailed(ex);
            Diagnostics.RuntimeIssueLedger.Current.Record(ex, "WorldCatalogEnumerationFailed",
                nameof(ReflectionNeowEffectSnapshotAdapter), "WorldCatalog", "issues.capture_failed",
                correlationId: source.GetType().FullName);
            exact = false;
        }
        return new ModelListCapture(keys, exact)
        {
            UnknownAncientGenerators = unknownGenerators.ToArray(), ReferenceIds = occurrences,
            AncientTypes = exact ? ancientTypes : null
        };
    }

    private static Beta109WorldRelicPoolCapture CaptureBeta109SharedRelicPool(
        Assembly assembly,
        object? unlockState)
    {
        if (unlockState is null)
            return new Beta109WorldRelicPoolCapture(Array.Empty<Beta109RelicSourceEntry>(), false, false, "MissingUnlockState");
        object? pool = ResolveModelDbPool(
            assembly,
            "RelicPool",
            "MegaCrit.Sts2.Core.Models.RelicPools",
            "SharedRelicPool");
        return CaptureBeta109WorldRelicPool(pool, unlockState, "SharedRelicPool");
    }

    private static Beta109WorldRelicPoolCapture CaptureBeta109CharacterRelicPool(
        object? characterModel,
        object? unlockState)
    {
        if (characterModel is null || unlockState is null)
            return new Beta109WorldRelicPoolCapture(Array.Empty<Beta109RelicSourceEntry>(), false, false, "MissingCharacterRelicPool");
        object? pool = new[] { "RelicPool", "CharacterRelicPool", "Relics" }
            .Select(name => ReadProperty(characterModel, name))
            .FirstOrDefault(value => value is not null);
        return CaptureBeta109WorldRelicPool(pool, unlockState, "CharacterRelicPool");
    }

    private static Beta109WorldRelicPoolCapture CaptureBeta109WorldRelicPool(
        object? pool,
        object unlockState,
        string source)
    {
        if (pool is null)
            return new Beta109WorldRelicPoolCapture(Array.Empty<Beta109RelicSourceEntry>(), false, false, source + ":Missing");
        MethodInfo? method = RuntimeMethodsFor(pool.GetType(), PublicInstance | BindingFlags.NonPublic)
            .Where(candidate => string.Equals(candidate.Name, "GetUnlockedRelics", StringComparison.Ordinal))
            .FirstOrDefault(candidate =>
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(unlockState);
            });
        if (method is null)
            return new Beta109WorldRelicPoolCapture(Array.Empty<Beta109RelicSourceEntry>(), false, false, source + ":GetUnlockedRelicsMissing");

        object?[] models;
        try
        {
            if (method.Invoke(pool, new[] { unlockState }) is not IEnumerable values || values is string)
                return new([], false, false, source + ":GetUnlockedRelicsDidNotReturnCollection");
            models = values.Cast<object?>().ToArray();
        }
        catch (Exception ex)
        {
            RecordCaptureFailure(ex, source + ".GetUnlockedRelics");
            return new Beta109WorldRelicPoolCapture(Array.Empty<Beta109RelicSourceEntry>(), false, false, source + ":InvocationFailed");
        }

        var output = new List<Beta109RelicSourceEntry>(models.Length);
        var rarities = new HashSet<string>(StringComparer.Ordinal);
        bool raritiesExact = true;
        bool shopEligibilityExact = true;
        int externalModelCount = 0;
        var patched = HarmonyLib.Harmony.GetAllPatchedMethods().ToHashSet();
        foreach (object? model in models)
        {
            string rarityCode = model == null ? string.Empty : ReadEnumName(model, "Rarity");
            if (model == null || string.IsNullOrWhiteSpace(rarityCode))
            {
                raritiesExact = false;
                continue;
            }
            rarities.Add(rarityCode);
            if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Relic) continue;

            if (model.GetType().Assembly != typeof(MegaCrit.Sts2.Core.Models.RelicModel).Assembly) externalModelCount++;
            bool? isAllowedInShops = TryReadBool(model, "IsAllowedInShops");
            var runEligibility = model.GetType().GetMethod("IsAllowedInternal", PublicInstance | BindingFlags.NonPublic);
            bool unknownRunEligibility = runEligibility is { } rule &&
                (rule.DeclaringType?.Assembly != typeof(MegaCrit.Sts2.Core.Models.RelicModel).Assembly || patched.Contains(rule));
            if (!isAllowedInShops.HasValue) shopEligibilityExact = false;
            output.Add(new Beta109RelicSourceEntry(
                key,
                rarityCode,
                isAllowedInShops ?? false,
                isAllowedInShops.HasValue) { UnmodeledRunEligibility = unknownRunEligibility });
        }
        return new Beta109WorldRelicPoolCapture(
            output,
            output.Count == models.Length,
            shopEligibilityExact && output.Count == models.Length,
            source + ":GetUnlockedRelics:RuntimeOrderRarityAndShopEligibilityPreserved:AllRaritiesPreserved:ExternalModels=" +
            externalModelCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":Captured=" + output.Count + "/" + models.Length)
        {
            ObservedRarities = raritiesExact ? rarities : null,
            AllRelicsFromGameAssembly = output.Count == models.Length && externalModelCount == 0
        };
    }

    private static (bool Value, bool Exact) TryInvokeOfficialBooleanPredicate(
        object owner,
        string methodName,
        object? argument)
    {
        if (argument is null) return (false, false);
        MethodInfo? method = owner.GetType().GetMethods(PublicInstance | BindingFlags.NonPublic)
            .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(candidate =>
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument);
            });
        if (method is null) return (false, false);
        try
        {
            return method.Invoke(owner, new[] { argument }) is bool result
                ? (result, true)
                : (false, false);
        }
        catch
        {
            return (false, false);
        }
    }

    private static (bool Value, bool Exact) TryInvokeOptionalOfficialBooleanPredicate(
        object owner,
        string methodName,
        object? argument)
    {
        if (argument is null || !IsOfficialSts2ModelWorld(owner)) return (false, false);
        MethodInfo? method = owner.GetType().GetMethods(PublicInstance | BindingFlags.NonPublic)
            .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(candidate =>
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument);
            });
        if (method is null)
        {
            return (false, false);
        }
        try
        {
            return method.Invoke(owner, new[] { argument }) is bool result
                ? (result, true)
                : (false, false);
        }
        catch
        {
            return (false, false);
        }
    }

    private static (HashSet<ModelKey> Keys, bool Exact) CaptureDeclaredDiscoveredActs(
        IReadOnlyList<ModelKey> declared, IReadOnlyList<Beta109ActSourceGroup> groups)
    {
        var known = groups.SelectMany(group => group.Models)
            .Select(model => TryModelKey(model, out ModelKey key) ? key : default)
            .Where(key => key.IsValid).ToHashSet(ModelKeyComparer.Instance);
        var keys = declared.ToHashSet(ModelKeyComparer.Instance);
        bool exact = keys.Count == declared.Count && keys.All(key => key.IsValid && known.Contains(key));
        return (keys, exact);
    }

    private static (HashSet<ModelKey> Keys, bool Exact) CaptureBeta109DiscoveredActs(Assembly assembly)
    {
        Type? saveType = FindType(assembly, "MegaCrit.Sts2.Core.Saves.SaveManager", "SaveManager");
        object? save = saveType is null ? null : ReadStaticMemberWorld(saveType, "Instance");
        object? progress = ReadProperty(save, "Progress");
        object? discovered = ReadProperty(progress, "DiscoveredActs");
        if (discovered is null) return (new HashSet<ModelKey>(ModelKeyComparer.Instance), false);
        ModelListCapture capture = CapturePossiblyEmptyModelListWorld(discovered);
        return (capture.Keys.ToHashSet(ModelKeyComparer.Instance), capture.Exact);
    }

    private static (bool Value, bool Exact) CaptureBeta109TestMode(Assembly assembly)
    {
        Type? type = FindType(assembly, "MegaCrit.Sts2.Core.TestMode", "TestMode");
        object? value = type is null ? null : ReadStaticMemberWorld(type, "IsOff");
        return value is bool result ? (result, true) : (false, false);
    }

    private static (bool Value, bool Exact, string EvidenceCode) CaptureBeta109MultiplayerMode(Assembly assembly)
    {
        // Direct 0.109.1 CLR authority:
        // RunManager.Instance.NetService : INetGameService
        // INetGameService.Type           : NetGameType
        // NetGameTypeExtensions.IsMultiplayer(type) returns true only for Host/Client.
        //
        // `NetService` is not a static type. The previous implementation searched
        // for a fictitious static NetService type and could therefore never
        // establish the main-menu multiplayer fact.
        Type? runManagerType = FindType(
            assembly,
            "MegaCrit.Sts2.Core.Runs.RunManager",
            "RunManager");
        if (runManagerType is null)
        {
            return (false, false, "MissingMultiplayerMode:MissingRunManagerType");
        }

        object? runManager = ReadStaticMemberWorldHierarchy(runManagerType, "Instance");
        if (runManager is null || !runManagerType.IsInstanceOfType(runManager))
        {
            return (false, false, "MissingMultiplayerMode:MissingRunManagerInstance");
        }

        object? netService = ReadProperty(runManager, "NetService");
        if (netService is null)
        {
            return (false, false, "MissingMultiplayerMode:MissingRunManagerNetService");
        }

        object? netGameTypeValue = ReadProperty(netService, "Type");
        Type? netGameType = FindType(
            assembly,
            "MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType",
            "NetGameType");
        if (netGameTypeValue is null)
        {
            return (false, false, "MissingMultiplayerMode:MissingNetGameTypeValue");
        }
        if (netGameType is null || !netGameType.IsEnum)
        {
            return (false, false, "MissingMultiplayerMode:MissingNetGameTypeDefinition");
        }
        if (netGameTypeValue.GetType() != netGameType)
        {
            return (false, false, "MissingMultiplayerMode:NetGameTypeMismatch");
        }
        if (!HasExpectedBeta109NetGameTypeShape(netGameType))
        {
            return (false, false, "MissingMultiplayerMode:NetGameTypeAbiMismatch");
        }

        try
        {
            int numericValue = Convert.ToInt32(
                netGameTypeValue,
                System.Globalization.CultureInfo.InvariantCulture);
            return (
                numericValue is 2 or 3,
                true,
                $"RunManager.Instance.NetService.Type:NetGameType={numericValue}");
        }
        catch
        {
            return (false, false, "MissingMultiplayerMode:NetGameTypeConversionFailed");
        }
    }

    private static object? ReadStaticMemberWorldHierarchy(Type type, string name)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Public |
                                       BindingFlags.NonPublic |
                                       BindingFlags.Static |
                                       BindingFlags.FlattenHierarchy;
            return type.GetProperty(name, flags)?.GetValue(null) ??
                   type.GetField(name, flags)?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasExpectedBeta109NetGameTypeShape(Type enumType)
    {
        // Validate the directly audited ABI before treating the numeric mapping as
        // exact. A later game update that changes names or values must fail closed.
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["None"] = 0,
            ["Singleplayer"] = 1,
            ["Host"] = 2,
            ["Client"] = 3,
            ["Replay"] = 4
        };
        string[] names = Enum.GetNames(enumType);
        if (names.Length != expected.Count) return false;
        foreach (string name in names)
        {
            if (!expected.TryGetValue(name, out int expectedValue)) return false;
            object? value = Enum.Parse(enumType, name, ignoreCase: false);
            int actualValue = Convert.ToInt32(
                value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (actualValue != expectedValue) return false;
        }
        return true;
    }

    private static ModelKey? ResolveBeta109Act1Override(
        string raw,
        IReadOnlyList<Beta109ActSelectionGroupSnapshot> groups)
    {
        string token = NormalizeTokenWorld(raw);
        if (token is not ("OVERGROWTH" or "UNDERDOCKS")) return null;
        ModelKey key = groups
            .Where(group => group.Act == 1)
            .SelectMany(group => group.Candidates)
            .Select(candidate => candidate.ActKey)
            .FirstOrDefault(candidate => NormalizeTokenWorld(candidate.Entry) == token);
        return key.IsValid ? key : null;
    }

    private static string[] CaptureExactTagTokens(object encounter, out bool exact)
    {
        object? value = ReadProperty(encounter, "Tags");
        if (value is null)
        {
            exact = true;
            return Array.Empty<string>();
        }
        var output = new List<string>();
        exact = true;
        foreach (object tag in Enumerate(value))
        {
            if (TryModelKey(tag, out ModelKey key))
            {
                output.Add(key.Serialized);
                continue;
            }
            Type type = tag.GetType();
            if (type.IsEnum)
            {
                output.Add(type.FullName + ":" + tag);
                continue;
            }
            exact = false;
        }
        return output.ToArray();
    }

    private static bool TryParseBeta109RoomType(string name, out Beta109RoomType roomType)
    {
        roomType = NormalizeTokenWorld(name) switch
        {
            "EVENT" => Beta109RoomType.Event,
            "WEAK" => Beta109RoomType.Weak,
            "MONSTER" or "REGULAR" or "NORMAL" => Beta109RoomType.Regular,
            "ELITE" => Beta109RoomType.Elite,
            "BOSS" => Beta109RoomType.Boss,
            _ => Beta109RoomType.Other
        };
        return roomType != Beta109RoomType.Other;
    }

    private static string DetermineBeta109ActCatalogIssue(
        IReadOnlyList<Beta109ActGenerationSnapshot> acts)
    {
        if (acts.Count == 0) return "MissingActCatalog";
        if (acts.Any(act => !act.RawEventCatalogOrderExact)) return "MissingActEventCatalogOrder";
        if (acts.Any(act => !act.EventEpochMembershipExact)) return "MissingEventEpochMembership";
        if (acts.Any(act => !act.EventEpochRevealFactsExact)) return "MissingEventEpochAuthority";
        if (acts.Any(act => !act.EligibleEventOrderExact)) return "MissingEventEligibleOrder";
        if (acts.Any(act => !act.EncounterCatalogOfficialVanilla)) return "UnsupportedModEncounter";
        if (acts.Any(act => act.OrderedGenerateAllEncounters is null)) return "MissingEncounterCatalog";
        if (acts.Any(act => string.Equals(
                act.EncounterAuthorityEvidenceCode,
                "VanillaEncounterSourceShapeMismatch",
                StringComparison.Ordinal)))
            return "EncounterCatalogShapeMismatch";
        if (acts.Any(act => !act.GenerateAllEncountersOrderExact)) return "MissingGenerateAllEncountersOrder";
        if (acts.Any(act => !act.EncounterClassificationExact)) return "MissingEncounterClassificationAuthority";
        if (acts.Any(act => !act.EncounterTagIdentityExact)) return "MissingEncounterTagAuthority";
        if (acts.Any(act => !act.EncounterTagComparerExact)) return "MissingEncounterTagComparer";
        if (acts.Any(act => !act.EncounterReferenceIdentityExact)) return "MissingEncounterReferenceIdentity";
        if (acts.Any(act => !act.EncounterWeightModelExact)) return "MissingEncounterWeightAuthority";
        if (acts.Any(act => !act.EncounterRetryShapeExact)) return "MissingEncounterRetryCallShape";
        if (acts.Any(act => !act.RoomShapeExact)) return "MissingRoomShapeAuthority";
        if (acts.Any(act => !act.UnlockFilteringExact)) return "MissingEventOrAncientUnlockFacts";
        return "MissingCatalog";
    }
}
