using System.Collections;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    internal sealed record EffectObservedInputs
    {
        public RuntimeEffectSnapshotCaptureResult? Failure { get; init; }
        public bool FailSoft { get; init; } = true;
        public ReflectionSnapshotProfileRules Rules { get; init; } = new(true, true);
        public DateTimeOffset ObservedAtUtc { get; init; }
        public string CanonicalSeed { get; init; } = "";
        public bool IsAuditedVanilla { get; init; }
        public bool SelectedCharacterUnlocked { get; init; }
        public PoolCapture SelectedCards { get; init; } = new(null, false, false, "unobserved");
        public PoolCapture ColorlessCards { get; init; } = new(null, false, false, "unobserved");
        public PoolCapture CurseCards { get; init; } = new(null, false, false, "unobserved");
        public PoolCapture CharacterPotions { get; init; } = new(null, false, false, "unobserved");
        public PoolCapture SharedPotions { get; init; } = new(null, false, false, "unobserved");
        public PoolCapture CharacterRelics { get; init; } = new(null, false, false, "unobserved");
        public PoolCapture SharedRelics { get; init; } = new(null, false, false, "unobserved");
        public IReadOnlyList<CharacterCardPoolSnapshot>? OtherCharacterPools { get; init; }
        public bool OtherPoolsExact { get; init; }
        public IReadOnlyList<NeowEffectCardSnapshot>? Deck { get; init; }
        public bool DeckExact { get; init; }
        public bool? AllCharacterCardPoolsUnlocked { get; init; }
        public bool AllCharacterPoolsAuthorityExact { get; init; }
        public bool EventResultStaticCatalogExact { get; init; }
        public IReadOnlyList<string> UnlockDescriptors { get; init; } = Array.Empty<string>();
        public string UnlockSource { get; init; } = "explicit-picker-seat";
    }

    internal static RuntimeEffectSnapshotCaptureResult DeriveEffect(IRuntimeProfile profile,
        string rawSeed, CharacterIdentity character, int ascension, int playersCount, int playerSlotIndex,
        string capturedGameVersion, EffectObservedInputs observed)
    {
        try { return DeriveEffectCore(profile,rawSeed,character,ascension,playersCount,playerSlotIndex,capturedGameVersion,observed); }
        catch (Exception ex) when (observed.FailSoft)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex,"RuntimeEffectSnapshotCaptureFailed",
                nameof(NeowEffectRuntimeSnapshotCapture),"EffectDerive","issues.capture_failed",
                correlationId:$"profile={profile.ProfileId};character={character.CharacterKey.Serialized};slot={playerSlotIndex}");
            return RuntimeEffectSnapshotCaptureResult.Missing(profile.ProfileId,
                $"RuntimeEffectSnapshotCaptureFailed:{ex.GetType().Name}",PredictionWarningCode.EffectSnapshotIncomplete);
        }
    }

    private static RuntimeEffectSnapshotCaptureResult DeriveEffectCore(IRuntimeProfile profile,
        string rawSeed, CharacterIdentity character, int ascension, int playersCount, int playerSlotIndex,
        string capturedGameVersion, EffectObservedInputs observed)
    {
        if (observed.Failure is not null) return observed.Failure;
        var rules = observed.Rules;
        var warnings = new List<PredictionWarningCode>();
        string canonicalSeed = observed.CanonicalSeed, unlockSource = observed.UnlockSource;
        bool selectedCharacterUnlocked = observed.SelectedCharacterUnlocked;
        var selectedCards = observed.SelectedCards; var colorlessCards = observed.ColorlessCards;
        var curseCards = observed.CurseCards; var otherCharacterPools = observed.OtherCharacterPools;
        bool otherPoolsExact = observed.OtherPoolsExact, deckExact = observed.DeckExact;
        var deck = observed.Deck;
        var characterPotions = observed.CharacterPotions; var sharedPotions = observed.SharedPotions;
        var sharedRelics = observed.SharedRelics; var characterRelics = observed.CharacterRelics;
        bool? allCharacterCardPoolsUnlocked = observed.AllCharacterCardPoolsUnlocked;
        bool allCharacterPoolsAuthorityExact = observed.AllCharacterPoolsAuthorityExact;
        bool eventResultStaticCatalogExact = observed.EventResultStaticCatalogExact;
        bool isAuditedVanilla = observed.IsAuditedVanilla;
        IdentityResolutionStatus resolution = isAuditedVanilla && selectedCharacterUnlocked
            ? IdentityResolutionStatus.Exact
            : IdentityResolutionStatus.Unknown;
        if (!selectedCharacterUnlocked)
        {
            warnings.Add(PredictionWarningCode.CharacterAuthorityUnknown);
        }

        selectedCards = StampCardCatalogProfile(selectedCards, profile.ProfileId);
        colorlessCards = StampCardCatalogProfile(colorlessCards, profile.ProfileId);
        curseCards = StampCardCatalogProfile(curseCards, profile.ProfileId);
        otherCharacterPools = StampCardCatalogProfile(otherCharacterPools, profile.ProfileId);
        deck = StampCardCatalogProfile(deck, profile.ProfileId);

        IReadOnlyList<NeowEffectCardSnapshot>? transformPool = selectedCards.Cards;
        bool transformExact = selectedCards.Exact;

        IReadOnlyList<NeowEffectPotionSnapshot>? potionPool = MergePotions(characterPotions, sharedPotions);
        bool potionExact = characterPotions.Exact && sharedPotions.Exact && potionPool is not null;
        bool characterRewardHooksExact =
            selectedCards.Cards is not null && selectedCards.Cards.All(IsAuditedBaseGameCardSnapshot);
        bool colorlessRewardHooksExact =
            colorlessCards.Cards is not null && colorlessCards.Cards.All(IsAuditedBaseGameCardSnapshot);
        bool otherCharacterRewardHooksExact =
            otherCharacterPools is not null &&
            otherCharacterPools.SelectMany(pool => pool.Cards).All(IsAuditedBaseGameCardSnapshot);
        bool cardRewardHooksExact =
            characterRewardHooksExact && colorlessRewardHooksExact && otherCharacterRewardHooksExact;
        bool potionProcurementHooksExact =
            potionPool is not null && potionPool.All(IsAuditedBaseGamePotionSnapshot);

        IReadOnlyList<NeowEffectRelicSnapshot>? orderedRelicBag = BuildOrderedRelicBag(
            profile,
            canonicalSeed,
            sharedRelics,
            characterRelics,
            out bool relicBagExact,
            out bool nestedRelicHooksExact);

        IReadOnlyList<ModelKey>? generatedCursePool = BuildGeneratedCursePool(
            curseCards,
            rules,
            out bool cursePoolExact);

        IReadOnlyList<ModelKey>? bonesEligibleRelics = BuildBonesEligibleRelics(
            profile.ProfileId,
            playersCount,
            allCharacterCardPoolsUnlocked,
            unlockedCommonCards: selectedCards.Cards?.Count(card => card.Rarity == EffectCardRarity.Common),
            unlockedUncommonCards: selectedCards.Cards?.Count(card => card.Rarity == EffectCardRarity.Uncommon),
            out bool bonesEligibilityExact);
        bonesEligibilityExact &= allCharacterPoolsAuthorityExact;

        ModelKey[] unlockedCharacterCardPoolKeys = new[] { character.CharacterKey }
            .Concat((otherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>()).Select(pool => pool.CharacterKey))
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        bool eventColorfulCharacterPoolsExact =
            profile.ProfileId == RuntimeProfileId.Beta111 &&
            selectedCharacterUnlocked &&
            otherPoolsExact &&
            unlockedCharacterCardPoolKeys.Length > 0;

        IReadOnlyList<NeowEffectCardSnapshot>? merchantColorlessOrderedPool = colorlessCards.Cards?
            .OrderBy(card => card.PoolOrder)
            .ToArray();
        bool merchantColorlessOrderedPoolExact =
            profile.ProfileId == RuntimeProfileId.Beta111 &&
            colorlessCards.Exact &&
            merchantColorlessOrderedPool is not null &&
            merchantColorlessOrderedPool.All(IsAuditedBaseGameCardSnapshot);
        // Shop Colorless v1 uses observable-domain authority, not a generic Merchant-hook gate.
        // Beta111 source audit establishes that the base ModifyMerchantCardPool path preserves
        // Colorless identity and that the only first-party pool override (CharacterCards) is
        // NoImpact for the Colorless pool. The base ModifyMerchantCardRarity path is identity-
        // preserving and Beta111 has no first-party rarity override. Third-party/non-base pool
        // authority remains Unsupported because merchantColorlessOrderedPoolExact is false.
        bool merchantColorlessPoolIdentityHooksExact =
            profile.ProfileId == RuntimeProfileId.Beta111 &&
            isAuditedVanilla &&
            merchantColorlessOrderedPoolExact;
        bool merchantColorlessRarityIdentityHooksExact =
            profile.ProfileId == RuntimeProfileId.Beta111 &&
            isAuditedVanilla &&
            merchantColorlessOrderedPoolExact;

        // The canonical v1 observable is five consecutive successful normal initial inventories,
        // not route occurrence. Prove only the fixed Shops-consumption shape needed to reach and
        // continue past Colorless U/R. For the audited single-player Beta111 base pool, the five
        // character slots have sufficient distinct Attack/Skill/Power candidates and Colorless
        // U/R are non-empty; external/custom behavior never receives this authority here.
        bool merchantInitialInventoryShapeExact =
            profile.ProfileId == RuntimeProfileId.Beta111 &&
            isAuditedVanilla &&
            playersCount >= 1 &&
            selectedCards.Exact && selectedCards.Cards is not null &&
            selectedCards.Cards.All(IsAuditedBaseGameCardSnapshot) &&
            selectedCards.Cards.Count(card => card.CardType == EffectCardType.Attack && !card.IsBasic) >= 2 &&
            selectedCards.Cards.Count(card => card.CardType == EffectCardType.Skill && !card.IsBasic) >= 2 &&
            selectedCards.Cards.Count(card => card.CardType == EffectCardType.Power && !card.IsBasic) >= 1 &&
            merchantColorlessOrderedPool is not null &&
            merchantColorlessOrderedPool.Any(card => card.Rarity == EffectCardRarity.Uncommon) &&
            merchantColorlessOrderedPool.Any(card => card.Rarity == EffectCardRarity.Rare);

        ModelKey? strikeKey = UniqueDeckKey(deck, card => card.IsBasic && card.IsStrike);
        ModelKey? defendKey = UniqueDeckKey(deck, card => card.IsBasic && card.IsDefend);
        ModelKey? clawKey = selectedCards.Cards?
            .FirstOrDefault(card => card.CardKey == BaseGameModelKeys.Cards.Claw)?.CardKey;

        int? commonCount = selectedCards.Cards?.Count(card => card.Rarity == EffectCardRarity.Common);
        int? uncommonCount = selectedCards.Cards?.Count(card => card.Rarity == EffectCardRarity.Uncommon);

        bool cardPoolExact = selectedCards.Exact && colorlessCards.Exact && otherPoolsExact;
        bool cardInstanceIdInvariantSatisfied =
            (selectedCards.Cards is null || NeowEffectSnapshotInvariants.HasUniqueCardInstanceIds(selectedCards.Cards)) &&
            (colorlessCards.Cards is null || NeowEffectSnapshotInvariants.HasUniqueCardInstanceIds(colorlessCards.Cards)) &&
            (curseCards.Cards is null || NeowEffectSnapshotInvariants.HasUniqueCardInstanceIds(curseCards.Cards)) &&
            (otherCharacterPools is null || NeowEffectSnapshotInvariants.HasUniqueCardInstanceIds(
                otherCharacterPools.SelectMany(pool => pool.Cards)));
        bool anyCaptured = selectedCards.Items is not null || deck is not null || potionPool is not null || orderedRelicBag is not null;
        bool complete = isAuditedVanilla && selectedCharacterUnlocked &&
                        cardPoolExact && cardRewardHooksExact &&
                        deckExact && transformExact &&
                        potionExact && potionProcurementHooksExact &&
                        relicBagExact && cursePoolExact && bonesEligibilityExact && nestedRelicHooksExact;
        SnapshotCompleteness completeness = complete
            ? SnapshotCompleteness.Complete
            : anyCaptured ? SnapshotCompleteness.Partial : SnapshotCompleteness.Missing;
        SourceAuthority sourceAuthority = isAuditedVanilla && selectedCharacterUnlocked && anyCaptured
            ? SourceAuthority.OfficialRuntimeExact
            : anyCaptured
                ? isAuditedVanilla ? SourceAuthority.Incomplete : SourceAuthority.ModdedRuntimeBestEffort
                : SourceAuthority.Unknown;

        if (!cardPoolExact || !cardRewardHooksExact) warnings.Add(PredictionWarningCode.EffectSnapshotIncomplete);
        if (!deckExact) warnings.Add(PredictionWarningCode.EffectSnapshotIncomplete);
        if (!potionExact || !potionProcurementHooksExact) warnings.Add(PredictionWarningCode.EffectSnapshotIncomplete);
        if (!relicBagExact) warnings.Add(PredictionWarningCode.EffectSnapshotIncomplete);
        if (!cursePoolExact || !bonesEligibilityExact) warnings.Add(PredictionWarningCode.EffectSnapshotIncomplete);
        if (!nestedRelicHooksExact) warnings.Add(PredictionWarningCode.EffectNestedObtainIncomplete);

        DateTimeOffset capturedAt = observed.ObservedAtUtc;
        string unlockFingerprint = Fingerprint("unlock-runtime-v1", observed.UnlockDescriptors);
        string cardPoolFingerprint = Fingerprint(
            RuntimeProfilePolicies.CatalogFingerprintPrefix(profile.ProfileId) + "-cards-v2",
            EnumerateCardDescriptors(selectedCards.Cards, colorlessCards.Cards, otherCharacterPools));
        string deckFingerprint = Fingerprint("effect-deck-v1", deck?.Select(CardDescriptor));
        string relicBagFingerprint = Fingerprint("effect-relic-bag-v1", orderedRelicBag?.Select(RelicDescriptor));
        string potionFingerprint = Fingerprint("effect-potions-v1", potionPool?.Select(PotionDescriptor));
        string catalogFingerprint = Fingerprint(
            RuntimeProfilePolicies.CatalogFingerprintPrefix(profile.ProfileId) + "-v2",
            new[]
        {
            RuntimeProfilePolicies.AuthorityId(profile.ProfileId),
            RuntimeProfilePolicies.AuditFingerprint(profile.ProfileId),
            capturedGameVersion,
            cardPoolFingerprint,
            potionFingerprint,
            relicBagFingerprint,
            Fingerprint("effect-curses-v1", generatedCursePool?.Select(key => key.Serialized)),
            Fingerprint("effect-bones-v1", bonesEligibleRelics?.Select(key => key.Serialized))
        });
        string snapshotFingerprint = Fingerprint("effect-authority-v2", new[]
        {
            RuntimeProfilePolicies.AuthorityId(profile.ProfileId),
            RuntimeProfilePolicies.AuditFingerprint(profile.ProfileId),
            profile.ProfileId.ToString(), capturedGameVersion, character.CharacterKey.Serialized,
            ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            playersCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            playerSlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            unlockFingerprint, cardPoolFingerprint, deckFingerprint, relicBagFingerprint, potionFingerprint,
            eventResultStaticCatalogExact.ToString(), eventColorfulCharacterPoolsExact.ToString(),
            merchantColorlessOrderedPoolExact.ToString(), merchantColorlessPoolIdentityHooksExact.ToString(),
            merchantColorlessRarityIdentityHooksExact.ToString(), merchantInitialInventoryShapeExact.ToString(),
            completeness.ToString(), sourceAuthority.ToString()
        });

        var effectAuthority = new NeowEffectAuthoritySnapshot(
            deck,
            selectedCards.Cards,
            colorlessCards.Cards,
            otherCharacterPools,
            transformPool,
            potionPool,
            orderedRelicBag,
            generatedCursePool,
            bonesEligibleRelics,
            strikeKey,
            defendKey,
            clawKey,
            CardRewardPoolsExact: cardPoolExact,
            DeckExact: deckExact,
            TransformPoolsExact: transformExact,
            PotionPoolExact: potionExact,
            RelicBagExact: relicBagExact,
            CursePoolExact: cursePoolExact,
            BonesEligibilityExact: bonesEligibilityExact,
            CardRewardHooksNoOpExact: isAuditedVanilla && cardPoolExact && cardRewardHooksExact,
            PotionProcurementHooksNoOpExact: isAuditedVanilla && potionExact && potionProcurementHooksExact,
            NestedRelicHooksExact: nestedRelicHooksExact,
            CurrentGold: null,
            CurrentPotionCount: null,
            PotionCapacity: null,
            BranchBudget: 64,
            SourceAuthority: sourceAuthority,
            Completeness: completeness,
            AuthoritySource: NeowEffectAuthoritySource.RuntimeSnapshot,
            CapturedProfileId: profile.ProfileId,
            CatalogFingerprint: catalogFingerprint,
            UnlockFingerprint: unlockFingerprint,
            DeckFingerprint: deckFingerprint,
            RelicBagFingerprint: relicBagFingerprint,
            PotionPoolFingerprint: potionFingerprint,
            SnapshotFingerprint: snapshotFingerprint,
            CapturedAtUtc: capturedAt,
            WarningCodes: warnings.Distinct().ToArray(),
            CaptureDiagnosticCode: cardInstanceIdInvariantSatisfied
                ? "RuntimeSnapshotCaptured"
                : "RuntimeSnapshotCardInstanceIdInvariantFailed",
            CharacterRewardPoolExact: selectedCards.Exact && selectedCards.Cards is not null,
            ColorlessRewardPoolExact: colorlessCards.Exact && colorlessCards.Cards is not null,
            OtherCharacterPoolsExact: otherPoolsExact && otherCharacterPools is not null,
            CharacterRewardHooksNoOpExact: isAuditedVanilla && characterRewardHooksExact,
            ColorlessRewardHooksNoOpExact: isAuditedVanilla && colorlessRewardHooksExact,
            OtherCharacterRewardHooksNoOpExact: isAuditedVanilla && otherCharacterRewardHooksExact,
            SharedRelicPoolSource: sharedRelics.Relics?.ToArray(),
            CharacterRelicPoolSource: characterRelics.Relics?.ToArray(),
            RelicBagSourcePoolsExact: sharedRelics.Exact && characterRelics.Exact,
            CharacterRewardIdentityPoolExact: selectedCards.IdentityExact && selectedCards.Cards is not null,
            UnlockedCharacterCardPoolKeys: unlockedCharacterCardPoolKeys,
            EventResultStaticCatalogExact: eventResultStaticCatalogExact,
            EventColorfulCharacterPoolsExact: eventColorfulCharacterPoolsExact,
            MerchantColorlessOrderedPool: merchantColorlessOrderedPool,
            MerchantColorlessOrderedPoolExact: merchantColorlessOrderedPoolExact,
            MerchantColorlessPoolIdentityHooksExact: merchantColorlessPoolIdentityHooksExact,
            MerchantColorlessRarityIdentityHooksExact: merchantColorlessRarityIdentityHooksExact,
            MerchantInitialInventoryShapeExact: merchantInitialInventoryShapeExact);

        return new RuntimeEffectSnapshotCaptureResult(
            effectAuthority,
            allCharacterCardPoolsUnlocked,
            commonCount,
            uncommonCount,
            unlockFingerprint,
            isAuditedVanilla && selectedCharacterUnlocked
                ? SourceAuthority.OfficialRuntimeExact
                : isAuditedVanilla ? SourceAuthority.Incomplete : SourceAuthority.ModdedRuntimeBestEffort,
            isAuditedVanilla && selectedCharacterUnlocked
                ? SnapshotCompleteness.Complete
                : SnapshotCompleteness.Partial,
            resolution,
            warnings.Distinct().ToArray(),
            string.Join(";", new[]
            {
                $"source={unlockSource}",
                $"profile={profile.ProfileId}",
                $"characterCards={selectedCards.Cards?.Count ?? -1}",
                $"characterRewardIdentityExact={selectedCards.IdentityExact.ToString().ToLowerInvariant()}",
                $"characterCardUniqueInstanceIds={NeowEffectSnapshotInvariants.CountUniqueCardInstanceIds(selectedCards.Cards)}",
                $"colorlessCards={colorlessCards.Cards?.Count ?? -1}",
                $"colorlessCardUniqueInstanceIds={NeowEffectSnapshotInvariants.CountUniqueCardInstanceIds(colorlessCards.Cards)}",
                $"otherPools={otherCharacterPools?.Count ?? -1}",
                $"otherPoolCards={otherCharacterPools?.Sum(pool => pool.Cards.Count) ?? -1}",
                $"otherPoolCardUniqueInstanceIds={NeowEffectSnapshotInvariants.CountUniqueCardInstanceIds(otherCharacterPools?.SelectMany(pool => pool.Cards))}",
                $"deck={deck?.Count ?? -1}",
                $"potions={potionPool?.Count ?? -1}",
                $"relicBag={orderedRelicBag?.Count ?? -1}",
                $"curses={generatedCursePool?.Count ?? -1}",
                $"eventStaticCatalogExact={eventResultStaticCatalogExact.ToString().ToLowerInvariant()}",
                $"eventColorfulPoolsExact={eventColorfulCharacterPoolsExact.ToString().ToLowerInvariant()}",
                $"merchantColorlessPool={merchantColorlessOrderedPool?.Count ?? -1}",
                $"merchantColorlessPoolExact={merchantColorlessOrderedPoolExact.ToString().ToLowerInvariant()}",
                $"merchantColorlessPoolIdentityHooksExact={merchantColorlessPoolIdentityHooksExact.ToString().ToLowerInvariant()}",
                $"merchantColorlessRarityIdentityHooksExact={merchantColorlessRarityIdentityHooksExact.ToString().ToLowerInvariant()}",
                $"merchantInitialShapeExact={merchantInitialInventoryShapeExact.ToString().ToLowerInvariant()}",
                $"completeness={completeness}"
            }));
    }

    internal static async Task<EffectObservedInputs> ObserveEffect(IRuntimeProfile profile, string rawSeed,
        CharacterIdentity character, int ascension, int playersCount, int playerSlotIndex,
        string gameVersion, object? explicitUnlockState, NativeObservationSchedule schedule, bool failSoft = true)
    {
        try
        {
            var observed = await ObserveEffectCore(profile, rawSeed, character, ascension, playersCount,
                playerSlotIndex, gameVersion, explicitUnlockState, schedule, new(true, true)).ConfigureAwait(false);
            return observed with { FailSoft = failSoft };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (failSoft)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex, "RuntimeEffectSnapshotCaptureFailed",
                nameof(NeowEffectRuntimeSnapshotCapture), "EffectCapture", "issues.capture_failed",
                correlationId: $"profile={profile.ProfileId};character={character.CharacterKey.Serialized};slot={playerSlotIndex}");
            return new() { Failure = RuntimeEffectSnapshotCaptureResult.Missing(profile.ProfileId,
                $"RuntimeEffectSnapshotCaptureFailed:{ex.GetType().Name}", PredictionWarningCode.EffectSnapshotIncomplete) };
        }
    }

    private static async Task<EffectObservedInputs> ObserveEffectCore(IRuntimeProfile profile, string rawSeed,
        CharacterIdentity character, int ascension, int playersCount, int playerSlotIndex,
        string gameVersion, object? explicitUnlockState, NativeObservationSchedule schedule,
        ReflectionSnapshotProfileRules rules)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(profile, rawSeed, out string canonicalSeed, out _))
            return new() { Failure = RuntimeEffectSnapshotCaptureResult.Missing(profile.ProfileId,
                "SeedNotCanonicalizableForEffectSnapshot", PredictionWarningCode.InvalidSeed) };
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            string.Equals(a.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        if (assembly is null) return new() { Failure = RuntimeEffectSnapshotCaptureResult.Missing(profile.ProfileId,
            "Sts2AssemblyNotLoaded", PredictionWarningCode.EffectSnapshotIncomplete) };
        string unlockSource = "explicit-picker-seat";
        var unlockState = explicitUnlockState ?? TryCaptureCurrentUnlockState(assembly, out unlockSource);
        if (unlockState is null) return new() { Failure = RuntimeEffectSnapshotCaptureResult.Missing(profile.ProfileId,
            $"RuntimeUnlockSnapshotUnavailable:{unlockSource}", PredictionWarningCode.SnapshotAuthorityIncomplete,
            PredictionWarningCode.EffectSnapshotIncomplete) };
        var model = FindCharacterModel(assembly, unlockState, character.CharacterKey, out bool unlocked);
        if (model is null) return new() { Failure = RuntimeEffectSnapshotCaptureResult.Missing(profile.ProfileId,
            "SelectedCharacterNotPresentInRuntimeUnlockSnapshot", PredictionWarningCode.CharacterAuthorityUnknown,
            PredictionWarningCode.EffectSnapshotIncomplete) };
        bool audited = character.IsKnownVanilla && IsAuditedVanillaCharacterModel(model);
        async Task<PoolCapture> Pool(object? source, string id, Func<object,int,string,object?> converter) =>
            FilterForPlayers(await ObservePoolObject(source, unlockState, id, converter, playersCount, schedule).ConfigureAwait(false), playersCount);
        object? PropertyPool(params string[] names) => names.Select(n => ReadProperty(model,n)).FirstOrDefault(v => v is not null);
        object? StaticPool(string kind, string group, string name) => ResolveModelDbPool(assembly,kind,"MegaCrit.Sts2.Core.Models."+group,name);
        var selected = await Pool(PropertyPool("CardPool","CharacterCardPool","Cards"), "character", ConvertCardModel).ConfigureAwait(false);
        var colorless = await Pool(StaticPool("CardPool","CardPools","ColorlessCardPool"), "colorless", ConvertCardModel).ConfigureAwait(false);
        var curses = await Pool(StaticPool("CardPool","CardPools","CurseCardPool"), "curse", ConvertCardModel).ConfigureAwait(false);
        var (others, otherExact) = await ObserveOtherCharacterPools(unlockState, character.CharacterKey, playersCount,
            rules, schedule).ConfigureAwait(false);
        var (deck, deckExact) = await ObserveStartingDeck(model, character.CharacterKey, audited, schedule).ConfigureAwait(false);
        var characterPotions = await Pool(PropertyPool("PotionPool","CharacterPotionPool","Potions"), "potion-character", ConvertPotionModel).ConfigureAwait(false);
        var sharedPotions = await Pool(StaticPool("PotionPool","PotionPools","SharedPotionPool"), "potion-shared", ConvertPotionModel).ConfigureAwait(false);
        var sharedRelics = await Pool(StaticPool("RelicPool","RelicPools","SharedRelicPool"), "relic-shared", ConvertRelicModel).ConfigureAwait(false);
        var characterRelics = await Pool(PropertyPool("RelicPool","CharacterRelicPool","Relics"), "relic-character", ConvertRelicModel).ConfigureAwait(false);
        await schedule.Checkpoint("UnlockCoverage").ConfigureAwait(false);
        bool? allUnlocked = CaptureAllCharacterCardPoolsUnlocked(assembly, unlockState, out bool allExact);
        bool events = profile.ProfileId == RuntimeProfileId.Beta111 &&
            ModelDbContainsAll(assembly,"AllCards",Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards) &&
            ModelDbContainsAll(assembly,"AllRelics",Core.Events.Beta111EventResultCatalog.TrashHeapDiveRelics.Concat(Core.Events.Beta111EventResultCatalog.FakeMerchantRelics)) &&
            ModelDbContainsAll(assembly,"AllCharacters",Core.Events.Beta111EventResultCatalog.ColorfulCharacterOrder);
        var descriptors = new List<string>();
        foreach (string descriptor in EnumerateModelKeys(unlockState))
        {
            await schedule.Checkpoint("UnlockIdentity").ConfigureAwait(false);
            descriptors.Add(descriptor);
        }
        return new() { Rules=rules, ObservedAtUtc=DateTimeOffset.UtcNow, CanonicalSeed=canonicalSeed, IsAuditedVanilla=audited, SelectedCharacterUnlocked=unlocked,
            SelectedCards=selected, ColorlessCards=colorless, CurseCards=curses, OtherCharacterPools=others,
            OtherPoolsExact=otherExact, Deck=deck, DeckExact=deckExact, CharacterPotions=characterPotions,
            SharedPotions=sharedPotions, SharedRelics=sharedRelics, CharacterRelics=characterRelics,
            AllCharacterCardPoolsUnlocked=allUnlocked, AllCharacterPoolsAuthorityExact=allExact,
            EventResultStaticCatalogExact=events, UnlockDescriptors=descriptors.ToArray(), UnlockSource=unlockSource };
    }
    private static async Task<PoolCapture> ObservePoolObject(
        object? pool,
        object unlockState,
        string poolId,
        Func<object, int, string, object?> converter,
        int playersCount, NativeObservationSchedule schedule)
    {
        if (pool is null)
        {
            return new PoolCapture(null, false, false, $"{poolId}:missing");
        }
        IEnumerable? models = InvokeUnlockedCollection(pool, unlockState, poolId, playersCount, out bool usedAuditedMethod);
        if (models is null)
        {
            return new PoolCapture(null, false, false, $"{poolId}:unlocked-enumeration-missing");
        }
        var output = new List<object>();
        bool identityExact = usedAuditedMethod;
        bool exact = usedAuditedMethod;
        int order = 0;
        foreach (object model in Enumerate(models))
        {
            await schedule.Checkpoint("Pool:" + poolId + ":" + order).ConfigureAwait(false);
            object? converted = converter(model, order, poolId);
            if (converted is null)
            {
                identityExact = false;
                exact = false;
                continue;
            }
            if (converted is NeowEffectCardSnapshot card && !card.BehaviorMetadataExact)
            {
                // Card identity generation only needs copied key/rarity/source order.
                // Missing deck/edit metadata must not erase an otherwise exact pool,
                // but it still prevents full semantic authority.
                exact = false;
            }
            output.Add(converted);
            order++;
        }
        object typed = output.Count == 0
            ? poolId.StartsWith("potion", StringComparison.Ordinal)
                ? Array.Empty<NeowEffectPotionSnapshot>()
                : poolId.StartsWith("relic", StringComparison.Ordinal)
                    ? Array.Empty<NeowEffectRelicSnapshot>()
                    : Array.Empty<NeowEffectCardSnapshot>()
            : ToTypedReadOnlyList(output);

        bool cardInstanceIdsUnique = typed is not IReadOnlyList<NeowEffectCardSnapshot> cards ||
                                     NeowEffectSnapshotInvariants.HasUniqueCardInstanceIds(cards);
        identityExact &= cardInstanceIdsUnique;
        exact &= cardInstanceIdsUnique;
        return new PoolCapture(
            typed,
            exact,
            identityExact,
            $"{poolId}:{pool.GetType().FullName};cardInstanceIdsUnique={cardInstanceIdsUnique.ToString().ToLowerInvariant()};identityExact={identityExact.ToString().ToLowerInvariant()}");
    }

    private static async Task<(IReadOnlyList<CharacterCardPoolSnapshot>? Pools, bool Exact)> ObserveOtherCharacterPools(
        object unlockState,
        ModelKey selectedCharacter,
        int playersCount,
        ReflectionSnapshotProfileRules rules,
        NativeObservationSchedule schedule)
    {
        bool exact = true;
        var output = new List<CharacterCardPoolSnapshot>();
        object? poolsValue = ReadProperty(unlockState, "CharacterCardPools");
        if (poolsValue is null)
        {
            exact = false;
            return (null, exact);
        }
        var poolObjects = Enumerate(poolsValue)
            .Where(pool => TryModelKey(pool, out _))
            .ToList();
        if (rules.OtherCharacterPoolsUseModelIdOrder)
        {
            poolObjects = poolObjects.OrderBy(pool => TryModelKey(pool, out ModelKey key) ? key.Category : string.Empty, StringComparer.Ordinal)
                .ThenBy(pool => TryModelKey(pool, out ModelKey key) ? key.Entry : string.Empty, StringComparer.Ordinal)
                .ToList();
        }
        IReadOnlyDictionary<ModelKey, ModelKey> characterByPool = CaptureCharacterByCardPool(unlockState);
        int poolOrder = 0;
        foreach (object pool in poolObjects)
        {
            await schedule.Checkpoint("OtherCharacterPool").ConfigureAwait(false);
            if (!TryModelKey(pool, out ModelKey poolKey))
            {
                exact = false;
                continue;
            }
            ModelKey characterKey;
            if (!characterByPool.TryGetValue(poolKey, out characterKey))
            {
                // Compatibility fallback for historical/runtime shapes that expose
                // pool identity but not CharacterModel.CardPool. It is deliberately
                // marked non-exact: arbitrary Mod pool naming must not become semantic
                // character authority merely because it resembles vanilla naming.
                string characterEntry = CharacterEntryFromPool(poolKey.Entry);
                characterKey = new ModelKey(BaseGameModelKeys.Categories.Character, characterEntry);
                exact = false;
            }
            if (characterKey == selectedCharacter)
            {
                continue;
            }
            PoolCapture capture = FilterForPlayers(
                await ObservePoolObject(
                    pool,
                    unlockState,
                    poolKey.Serialized,
                    (model, order, id) => ConvertCardModel(model, order, id),
                    playersCount, schedule).ConfigureAwait(false),
                playersCount);
            if (capture.Cards is null)
            {
                exact = false;
                continue;
            }
            exact &= capture.Exact;
            output.Add(new CharacterCardPoolSnapshot(characterKey, poolKey.Serialized, poolOrder++, capture.Cards));
        }
        return (output.ToArray(), exact);
    }

    private static async Task<(IReadOnlyList<NeowEffectCardSnapshot>? Deck, bool Exact)> ObserveStartingDeck(
        object character,
        ModelKey characterKey,
        bool auditedVanilla,
        NativeObservationSchedule schedule)
    {
        bool exact = auditedVanilla;
        object? deckValue;
        try
        {
            deckValue = ReadProperty(character, "StartingDeck");
        }
        catch
        {
            exact = false;
            return (null, exact);
        }
        if (deckValue is null)
        {
            exact = false;
            return (null, exact);
        }
        var output = new List<NeowEffectCardSnapshot>();
        int order = 0;
        foreach (object card in Enumerate(deckValue))
        {
            await schedule.Checkpoint("StartingDeck:" + order).ConfigureAwait(false);
            NeowEffectCardSnapshot? converted = ConvertCardModel(card, order, "character", $"starter:{characterKey.Entry}:{order}");
            if (converted is null)
            {
                exact = false;
                continue;
            }
            output.Add(converted);
            order++;
        }
        if (output.Count == 0)
        {
            exact = false;
            return (null, exact);
        }
        return (output.ToArray(), exact);
    }

}
