using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed record ReflectionSnapshotProfileRules(
    bool GeneratedCursePoolRequiresModifierFlag,
    bool OtherCharacterPoolsUseModelIdOrder);

/// <summary>
/// Narrow reflection boundary over the live STS2 model catalog/profile. It is intentionally
/// isolated from the pure core so the same DLL can run against both accepted game ABIs.
/// All returned values are copied scalar DTOs; no game object escapes this method.
/// </summary>
internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private static readonly string[] AuditedVanillaCharacters =
    {
        "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER"
    };

    public static RuntimeEffectSnapshotCaptureResult Capture(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeProfileId expectedProfile,
        string capturedGameVersion,
        ReflectionSnapshotProfileRules rules,
        object? explicitUnlockState = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (profile.ProfileId != expectedProfile)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                expectedProfile,
                "ProfileSpecificSnapshotAdapterMismatch",
                PredictionWarningCode.EffectSnapshotIncomplete);
        }
        if (!profile.TryCanonicalizeSeed(rawSeed, out string canonicalSeed, out _))
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                expectedProfile,
                "SeedNotCanonicalizableForEffectSnapshot",
                PredictionWarningCode.InvalidSeed);
        }

        Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        if (gameAssembly is null)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                expectedProfile,
                "Sts2AssemblyNotLoaded",
                PredictionWarningCode.EffectSnapshotIncomplete);
        }

        var warnings = new List<PredictionWarningCode>();
        string unlockSource = "explicit-picker-seat";
        object? unlockState = explicitUnlockState ?? TryCaptureCurrentUnlockState(gameAssembly, out unlockSource);
        if (unlockState is null)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                expectedProfile,
                $"RuntimeUnlockSnapshotUnavailable:{unlockSource}",
                PredictionWarningCode.SnapshotAuthorityIncomplete,
                PredictionWarningCode.EffectSnapshotIncomplete);
        }

        object? characterModel = FindCharacterModel(
            gameAssembly,
            unlockState,
            character.CharacterKey,
            out bool selectedCharacterUnlocked);
        if (characterModel is null)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                expectedProfile,
                "SelectedCharacterNotPresentInRuntimeUnlockSnapshot",
                PredictionWarningCode.CharacterAuthorityUnknown,
                PredictionWarningCode.EffectSnapshotIncomplete);
        }

        bool isAuditedVanilla = character.IsKnownVanilla && IsAuditedVanillaCharacterModel(characterModel);
        IdentityResolutionStatus resolution = isAuditedVanilla && selectedCharacterUnlocked
            ? IdentityResolutionStatus.Exact
            : IdentityResolutionStatus.Unknown;
        if (!selectedCharacterUnlocked)
        {
            warnings.Add(PredictionWarningCode.CharacterAuthorityUnknown);
        }

        PoolCapture selectedCards = FilterForPlayers(
            CaptureCharacterCardPool(characterModel, unlockState, character.CharacterKey, "character", playersCount), playersCount);
        PoolCapture colorlessCards = FilterForPlayers(
            CaptureStaticCardPool(gameAssembly, unlockState, "ColorlessCardPool", "colorless", playersCount), playersCount);
        PoolCapture curseCards = FilterForPlayers(
            CaptureStaticCardPool(gameAssembly, unlockState, "CurseCardPool", "curse", playersCount), playersCount);
        IReadOnlyList<CharacterCardPoolSnapshot>? otherCharacterPools = CaptureOtherCharacterPools(
            unlockState,
            character.CharacterKey,
            playersCount,
            rules,
            out bool otherPoolsExact);

        IReadOnlyList<NeowEffectCardSnapshot>? deck = CaptureStartingDeck(
            characterModel,
            character.CharacterKey,
            isAuditedVanilla,
            out bool deckExact);

        selectedCards = StampCardCatalogProfile(selectedCards, profile.ProfileId);
        colorlessCards = StampCardCatalogProfile(colorlessCards, profile.ProfileId);
        curseCards = StampCardCatalogProfile(curseCards, profile.ProfileId);
        otherCharacterPools = StampCardCatalogProfile(otherCharacterPools, profile.ProfileId);
        deck = StampCardCatalogProfile(deck, profile.ProfileId);

        IReadOnlyList<NeowEffectCardSnapshot>? transformPool = selectedCards.Cards;
        bool transformExact = selectedCards.Exact;

        PoolCapture characterPotions = FilterForPlayers(CapturePoolProperty(
            characterModel,
            unlockState,
            new[] { "PotionPool", "CharacterPotionPool", "Potions" },
            "potion-character",
            ConvertPotionModel,
            playersCount), playersCount);
        PoolCapture sharedPotions = FilterForPlayers(
            CaptureStaticPotionPool(gameAssembly, unlockState, "SharedPotionPool", "potion-shared", playersCount), playersCount);
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

        PoolCapture sharedRelics = FilterForPlayers(
            CaptureStaticRelicPool(gameAssembly, unlockState, "SharedRelicPool", "relic-shared", playersCount), playersCount);
        PoolCapture characterRelics = FilterForPlayers(CapturePoolProperty(
            characterModel,
            unlockState,
            new[] { "RelicPool", "CharacterRelicPool", "Relics" },
            "relic-character",
            ConvertRelicModel,
            playersCount), playersCount);
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

        bool? allCharacterCardPoolsUnlocked = CaptureAllCharacterCardPoolsUnlocked(
            gameAssembly,
            unlockState,
            out bool allCharacterPoolsAuthorityExact);
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
        bool eventResultStaticCatalogExact =
            profile.ProfileId == RuntimeProfileId.Beta111 &&
            ModelDbContainsAll(
                gameAssembly,
                "AllCards",
                RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards) &&
            ModelDbContainsAll(
                gameAssembly,
                "AllRelics",
                RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapDiveRelics
                    .Concat(RolltheSpire2.Core.Events.Beta111EventResultCatalog.FakeMerchantRelics)) &&
            ModelDbContainsAll(
                gameAssembly,
                "AllCharacters",
                RolltheSpire2.Core.Events.Beta111EventResultCatalog.ColorfulCharacterOrder);
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

        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        string unlockFingerprint = Fingerprint("unlock-runtime-v1", EnumerateModelKeys(unlockState));
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

    private sealed record PoolCapture(object? Items, bool Exact, bool IdentityExact, string Source)
    {
        public IReadOnlyList<NeowEffectCardSnapshot>? Cards => Items as IReadOnlyList<NeowEffectCardSnapshot>;
        public IReadOnlyList<NeowEffectPotionSnapshot>? Potions => Items as IReadOnlyList<NeowEffectPotionSnapshot>;
        public IReadOnlyList<NeowEffectRelicSnapshot>? Relics => Items as IReadOnlyList<NeowEffectRelicSnapshot>;
    }

    private static object? TryCaptureCurrentUnlockState(Assembly assembly, out string source)
    {
        Type? saveType = FindType(assembly, "MegaCrit.Sts2.Core.Saves.SaveManager", "SaveManager");
        object? save = saveType?.GetProperty("Instance", PublicStatic)?.GetValue(null);
        if (save is null)
        {
            source = "SaveManager.Instance missing";
            return null;
        }
        if (TryReadBool(save, "IsProfileInitialized") != true)
        {
            source = "Save profile not initialized";
            return null;
        }
        MethodInfo? method = save.GetType().GetMethod("GenerateUnlockStateFromProgress", PublicInstance, null, Type.EmptyTypes, null);
        if (method is null)
        {
            source = "GenerateUnlockStateFromProgress missing";
            return null;
        }
        try
        {
            object? result = method.Invoke(save, null);
            source = result is null ? "GenerateUnlockStateFromProgress returned null" : "SaveManager.GenerateUnlockStateFromProgress";
            return result;
        }
        catch (TargetInvocationException ex)
        {
            source = $"GenerateUnlockStateFromProgress failed:{ex.InnerException?.GetType().Name ?? ex.GetType().Name}";
            return null;
        }
    }

    private static object? FindCharacterModel(
        Assembly assembly,
        object unlockState,
        ModelKey characterKey,
        out bool selectedCharacterUnlocked)
    {
        foreach (object item in Enumerate(ReadProperty(unlockState, "Characters")))
        {
            if (TryModelKey(item, out ModelKey key) && key == characterKey)
            {
                selectedCharacterUnlocked = true;
                return item;
            }
        }

        // A locked model may still be present in ModelDb. It can be copied for diagnostic/
        // best-effort projection, but it must never be presented as the current profile's
        // exact unlocked character authority.
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        foreach (object item in Enumerate(modelDb?.GetProperty("AllCharacters", PublicStatic)?.GetValue(null)))
        {
            if (TryModelKey(item, out ModelKey key) && key == characterKey)
            {
                selectedCharacterUnlocked = false;
                return item;
            }
        }

        selectedCharacterUnlocked = false;
        return null;
    }

    private static bool IsAuditedVanillaCharacterModel(object model)
    {
        if (!TryModelKey(model, out ModelKey key) ||
            !AuditedVanillaCharacters.Contains(key.Entry, StringComparer.Ordinal))
        {
            return false;
        }
        Type type = model.GetType();
        return string.Equals(type.Assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(type.Namespace, "MegaCrit.Sts2.Core.Models.Characters", StringComparison.Ordinal);
    }

    private static PoolCapture CaptureCharacterCardPool(
        object character,
        object unlockState,
        ModelKey characterKey,
        string poolId,
        int playersCount)
    {
        return CapturePoolProperty(
            character,
            unlockState,
            new[] { "CardPool", "CharacterCardPool", "Cards" },
            poolId,
            (model, order, id) => ConvertCardModel(model, order, id),
            playersCount);
    }

    private static PoolCapture CaptureStaticCardPool(
        Assembly assembly,
        object unlockState,
        string className,
        string poolId,
        int playersCount)
    {
        object? pool = ResolveModelDbPool(assembly, "CardPool", "MegaCrit.Sts2.Core.Models.CardPools", className);
        return CapturePoolObject(
            pool,
            unlockState,
            poolId,
            (model, order, id) => ConvertCardModel(model, order, id),
            playersCount);
    }

    private static PoolCapture CaptureStaticPotionPool(
        Assembly assembly,
        object unlockState,
        string className,
        string poolId,
        int playersCount)
    {
        object? pool = ResolveModelDbPool(assembly, "PotionPool", "MegaCrit.Sts2.Core.Models.PotionPools", className);
        return CapturePoolObject(pool, unlockState, poolId, ConvertPotionModel, playersCount);
    }

    private static PoolCapture CaptureStaticRelicPool(
        Assembly assembly,
        object unlockState,
        string className,
        string poolId,
        int playersCount)
    {
        object? pool = ResolveModelDbPool(assembly, "RelicPool", "MegaCrit.Sts2.Core.Models.RelicPools", className);
        return CapturePoolObject(pool, unlockState, poolId, ConvertRelicModel, playersCount);
    }

    private static object? ResolveModelDbPool(Assembly assembly, string methodName, string namespaceName, string className)
    {
        Type? poolType = FindType(assembly, $"{namespaceName}.{className}", className);
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        if (poolType is null || modelDb is null)
        {
            return null;
        }
        foreach (MethodInfo method in modelDb.GetMethods(PublicStatic))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1 ||
                method.GetParameters().Length != 0)
            {
                continue;
            }
            try
            {
                return method.MakeGenericMethod(poolType).Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static PoolCapture CapturePoolProperty(
        object owner,
        object unlockState,
        IReadOnlyList<string> propertyNames,
        string poolId,
        Func<object, int, string, object?> converter,
        int playersCount)
    {
        object? pool = propertyNames.Select(name => ReadProperty(owner, name)).FirstOrDefault(value => value is not null);
        return CapturePoolObject(pool, unlockState, poolId, converter, playersCount);
    }

    private static PoolCapture CapturePoolObject(
        object? pool,
        object unlockState,
        string poolId,
        Func<object, int, string, object?> converter,
        int playersCount)
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

    private static PoolCapture StampCardCatalogProfile(PoolCapture capture, RuntimeProfileId profileId)
    {
        if (capture.Cards is null)
        {
            return capture;
        }
        return capture with
        {
            Items = capture.Cards.Select(card => card with { CatalogProfileId = profileId }).ToArray()
        };
    }

    private static IReadOnlyList<CharacterCardPoolSnapshot>? StampCardCatalogProfile(
        IReadOnlyList<CharacterCardPoolSnapshot>? pools,
        RuntimeProfileId profileId) => pools?.Select(pool => pool with
        {
            Cards = pool.Cards.Select(card => card with { CatalogProfileId = profileId }).ToArray()
        }).ToArray();

    private static IReadOnlyList<NeowEffectCardSnapshot>? StampCardCatalogProfile(
        IReadOnlyList<NeowEffectCardSnapshot>? cards,
        RuntimeProfileId profileId) => cards?.Select(card =>
            card with { CatalogProfileId = profileId }).ToArray();

    private static object ToTypedReadOnlyList(List<object> values)
    {
        if (values.All(value => value is NeowEffectCardSnapshot))
        {
            return values.Cast<NeowEffectCardSnapshot>().ToArray();
        }
        if (values.All(value => value is NeowEffectPotionSnapshot))
        {
            return values.Cast<NeowEffectPotionSnapshot>().ToArray();
        }
        if (values.All(value => value is NeowEffectRelicSnapshot))
        {
            return values.Cast<NeowEffectRelicSnapshot>().ToArray();
        }
        return values.ToArray();
    }

    private static IReadOnlyList<CharacterCardPoolSnapshot>? CaptureOtherCharacterPools(
        object unlockState,
        ModelKey selectedCharacter,
        int playersCount,
        ReflectionSnapshotProfileRules rules,
        out bool exact)
    {
        exact = true;
        var output = new List<CharacterCardPoolSnapshot>();
        object? poolsValue = ReadProperty(unlockState, "CharacterCardPools");
        if (poolsValue is null)
        {
            exact = false;
            return null;
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
                CapturePoolObject(
                    pool,
                    unlockState,
                    poolKey.Serialized,
                    (model, order, id) => ConvertCardModel(model, order, id),
                    playersCount),
                playersCount);
            if (capture.Cards is null)
            {
                exact = false;
                continue;
            }
            exact &= capture.Exact;
            output.Add(new CharacterCardPoolSnapshot(characterKey, poolKey.Serialized, poolOrder++, capture.Cards));
        }
        return output;
    }

    private static IReadOnlyList<NeowEffectCardSnapshot>? CaptureStartingDeck(
        object character,
        ModelKey characterKey,
        bool auditedVanilla,
        out bool exact)
    {
        exact = auditedVanilla;
        object? deckValue;
        try
        {
            deckValue = ReadProperty(character, "StartingDeck");
        }
        catch
        {
            exact = false;
            return null;
        }
        if (deckValue is null)
        {
            exact = false;
            return null;
        }
        var output = new List<NeowEffectCardSnapshot>();
        int order = 0;
        foreach (object card in Enumerate(deckValue))
        {
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
            return null;
        }
        return output;
    }

    private static NeowEffectCardSnapshot? ConvertCardModel(object model, int order, string poolId) =>
        ConvertCardModel(model, order, poolId, $"{poolId}:{order}");

    private static NeowEffectCardSnapshot? ConvertCardModel(object model, int order, string poolId, string instanceId)
    {
        if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Card)
        {
            return null;
        }
        string rarityName = ReadEnumName(model, "Rarity");
        string typeName = ReadEnumName(model, "Type");
        if (!TryParseCardRarity(rarityName, out EffectCardRarity rarity) ||
            !TryParseCardType(typeName, out EffectCardType type))
        {
            return null;
        }
        bool? isUpgradable = TryReadBool(model, "IsUpgradable", "CanUpgrade");
        bool? isRemovable = TryReadBool(model, "IsRemovable", "CanRemove");
        int? maxUpgrade = TryReadInt(model, "MaxUpgradeLevel");
        int currentUpgrade = TryReadInt(model, "CurrentUpgradeLevel", "UpgradeLevel") ?? 0;
        bool strike = HasTag(model, "Strike") || (rarity == EffectCardRarity.Basic && key.Entry.Contains("STRIKE", StringComparison.OrdinalIgnoreCase));
        bool defend = HasTag(model, "Defend") || (rarity == EffectCardRarity.Basic && key.Entry.Contains("DEFEND", StringComparison.OrdinalIgnoreCase));

        bool behaviorMetadataExact = isUpgradable.HasValue && isRemovable.HasValue && maxUpgrade.HasValue;
        bool canUpgrade = isUpgradable ?? false;
        bool canRemove = isRemovable ?? false;
        int max = maxUpgrade ?? 0;
        if (max < 0 || (canUpgrade && max == 0) || (!canUpgrade && max != 0))
        {
            behaviorMetadataExact = false;
            canUpgrade = false;
            max = 0;
        }
        return new NeowEffectCardSnapshot(
            instanceId,
            key,
            order,
            rarity,
            type,
            rarity == EffectCardRarity.Basic,
            strike,
            defend,
            canUpgrade && currentUpgrade < max,
            canRemove,
            currentUpgrade,
            max,
            UpgradeTargetKey: null,
            PoolId: poolId,
            SourceAssembly: model.GetType().Assembly.GetName().Name,
            SourceModId: SourceModIdFor(model),
            CanBeGeneratedByModifiers: TryReadBool(model,
                "CanBeGeneratedByModifiers",
                "CanGenerateByModifiers",
                "IsModifierGenerationAllowed"),
            IsMultiplayerOnly: IsMultiplayerOnly(model),
            CanBeGeneratedInCombat: TryReadBool(model, "CanBeGeneratedInCombat"),
            IsUnlockedInCapturedPool: true,
            IsDiscovered: TryReadBool(model, "IsDiscovered", "Discovered"),
            EligibleForPostCombatRewardByPoolMembership:
                rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare,
            EligibilityAuthority: "runtime-unlocked-card-pool-membership",
            BehaviorMetadataExact: behaviorMetadataExact);
    }

    private static object? ConvertPotionModel(object model, int order, string poolId)
    {
        if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Potion ||
            !TryParsePotionRarity(ReadEnumName(model, "Rarity"), out EffectPotionRarity rarity))
        {
            return null;
        }
        bool? allowed = TryReadBool(model, "AllowedOutOfCombat", "CanUseOutOfCombat", "IsAllowedOutOfCombat");
        // Neow potion generation is sourced from the captured character + shared potion
        // pools. AllowedOutOfCombat is optional descriptive metadata only and must not
        // exclude an otherwise authoritative runtime pool member.
        return new NeowEffectPotionSnapshot(
            key,
            order,
            rarity,
            allowed,
            model.GetType().Assembly.GetName().Name,
            SourceModId: SourceModIdFor(model),
            IsMultiplayerOnly: IsMultiplayerOnly(model));
    }

    private static object? ConvertRelicModel(object model, int order, string poolId)
    {
        if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Relic ||
            !TryParseRelicRarity(ReadEnumName(model, "Rarity"), out EffectRelicRarity rarity))
        {
            return null;
        }
        NestedRelicEffectKind kind = ClassifyNestedRelicEffect(model, key, out bool classificationExact);
        bool? isAllowedInShops = TryReadBool(model, "IsAllowedInShops");
        return new NeowEffectRelicSnapshot(
            key,
            order,
            rarity,
            kind,
            classificationExact,
            model.GetType().Assembly.GetName().Name,
            SourceModId: SourceModIdFor(model),
            IsMultiplayerOnly: IsMultiplayerOnly(model),
            RarityCode: ReadEnumName(model, "Rarity"),
            IsAllowedInShops: isAllowedInShops ?? false,
            ShopEligibilityExact: isAllowedInShops.HasValue);
    }

    private static PoolCapture FilterForPlayers(PoolCapture capture, int playersCount)
    {
        if (playersCount > 1)
        {
            return capture;
        }
        if (capture.Cards is not null)
        {
            return capture with { Items = capture.Cards.Where(item => !item.IsMultiplayerOnly).ToArray() };
        }
        if (capture.Potions is not null)
        {
            return capture with { Items = capture.Potions.Where(item => !item.IsMultiplayerOnly).ToArray() };
        }
        if (capture.Relics is not null)
        {
            return capture with { Items = capture.Relics.Where(item => !item.IsMultiplayerOnly).ToArray() };
        }
        return capture;
    }

    private static IReadOnlyList<NeowEffectPotionSnapshot>? MergePotions(PoolCapture character, PoolCapture shared)
    {
        if (character.Potions is null && shared.Potions is null)
        {
            return null;
        }
        var seen = new HashSet<ModelKey>();
        var output = new List<NeowEffectPotionSnapshot>();
        foreach (NeowEffectPotionSnapshot potion in (character.Potions ?? Array.Empty<NeowEffectPotionSnapshot>())
                     .Concat(shared.Potions ?? Array.Empty<NeowEffectPotionSnapshot>()))
        {
            if (seen.Add(potion.PotionKey))
            {
                output.Add(potion with { PoolOrder = output.Count });
            }
        }
        return output;
    }

    private static IReadOnlyList<NeowEffectRelicSnapshot>? BuildOrderedRelicBag(
        IRuntimeProfile profile,
        string canonicalSeed,
        PoolCapture shared,
        PoolCapture character,
        out bool exact,
        out bool nestedHooksExact)
    {
        exact = shared.Exact && character.Exact;
        if (shared.Relics is null || character.Relics is null)
        {
            nestedHooksExact = false;
            exact = false;
            return null;
        }

        // UpFront initialization creates the shared grab bag first, then the player bag,
        // using the same stream. Even though Capsule reads the player bag, every shared-bag
        // shuffle must be consumed first or the player-bag order is wrong.
        ulong root = profile.ComputeRootSeed(canonicalSeed);
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(root, "up_front"));
        if (!ConsumeRelicBagShuffle(shared.Relics, rng, filterPlayerGrabBagRarities: false))
        {
            exact = false;
        }

        var seen = new HashSet<ModelKey>();
        var playerRelics = new List<NeowEffectRelicSnapshot>();
        foreach (NeowEffectRelicSnapshot relic in shared.Relics.Concat(character.Relics))
        {
            if (relic.Rarity is not (EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop))
            {
                continue;
            }
            if (seen.Add(relic.RelicKey))
            {
                playerRelics.Add(relic);
            }
        }

        nestedHooksExact = playerRelics.All(relic => relic.NestedClassificationExact);
        var output = new List<NeowEffectRelicSnapshot>();
        foreach (IGrouping<string, NeowEffectRelicSnapshot> bucket in GroupRelicsInSourceRarityOrder(playerRelics))
        {
            List<NeowEffectRelicSnapshot> shuffled = bucket.ToList();
            rng.UnstableShuffle(shuffled);
            foreach (NeowEffectRelicSnapshot relic in shuffled)
            {
                output.Add(relic with { BagOrder = output.Count });
            }
        }
        return output;
    }

    private static bool ConsumeRelicBagShuffle(
        IReadOnlyList<NeowEffectRelicSnapshot> relics,
        Xoshiro256StarStar rng,
        bool filterPlayerGrabBagRarities)
    {
        bool exact = true;
        IEnumerable<NeowEffectRelicSnapshot> source = filterPlayerGrabBagRarities
            ? relics.Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop)
            : relics;

        foreach (IGrouping<string, NeowEffectRelicSnapshot> bucket in GroupRelicsInSourceRarityOrder(source))
        {
            if (string.IsNullOrWhiteSpace(bucket.Key))
            {
                exact = false;
            }
            List<NeowEffectRelicSnapshot> shuffled = bucket.ToList();
            rng.UnstableShuffle(shuffled);
        }
        return exact;
    }

    private static IEnumerable<IGrouping<string, NeowEffectRelicSnapshot>> GroupRelicsInSourceRarityOrder(
        IEnumerable<NeowEffectRelicSnapshot> relics)
    {
        // Enumerable.GroupBy preserves first-key occurrence order and item source order,
        // matching RelicGrabBag.Populate's dictionary + rarityOrder construction.
        return relics.GroupBy(
            relic => string.IsNullOrWhiteSpace(relic.RarityCode)
                ? relic.Rarity.ToString()
                : relic.RarityCode,
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<ModelKey>? BuildGeneratedCursePool(
        PoolCapture curseCards,
        ReflectionSnapshotProfileRules rules,
        out bool exact)
    {
        exact = curseCards.Exact;
        if (curseCards.Cards is null)
        {
            exact = false;
            return null;
        }
        var output = new List<ModelKey>();
        foreach (NeowEffectCardSnapshot card in curseCards.Cards.OrderBy(card => card.CardKey.Category, StringComparer.Ordinal)
                     .ThenBy(card => card.CardKey.Entry, StringComparer.Ordinal))
        {
            if (!rules.GeneratedCursePoolRequiresModifierFlag)
            {
                output.Add(card.CardKey);
                continue;
            }
            if (!card.CanBeGeneratedByModifiers.HasValue)
            {
                exact = false;
                continue;
            }
            if (card.CanBeGeneratedByModifiers.Value)
            {
                output.Add(card.CardKey);
            }
        }
        exact &= output.Count > 0;
        return output;
    }

    private static bool? CaptureAllCharacterCardPoolsUnlocked(
        Assembly assembly,
        object unlockState,
        out bool exact)
    {
        exact = false;
        object? unlockedPoolsValue = ReadProperty(unlockState, "CharacterCardPools");
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        object? allCharactersValue = modelDb?.GetProperty("AllCharacters", PublicStatic)?.GetValue(null);
        if (unlockedPoolsValue is null || allCharactersValue is null)
        {
            return null;
        }

        ModelKey[] unlockedPools = Enumerate(unlockedPoolsValue)
            .Select(pool => TryModelKey(pool, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .Distinct()
            .ToArray();
        ModelKey[] allCharacters = Enumerate(allCharactersValue)
            .Select(character => TryModelKey(character, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .Distinct()
            .ToArray();
        if (unlockedPools.Length == 0 || allCharacters.Length == 0)
        {
            return null;
        }

        exact = true;
        return unlockedPools.Length == allCharacters.Length;
    }

    private static bool ModelDbContainsAll(
        Assembly assembly,
        string propertyName,
        IEnumerable<ModelKey> requiredKeys)
    {
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        object? value = modelDb?.GetProperty(propertyName, PublicStatic)?.GetValue(null);
        if (value is null) return false;
        var present = Enumerate(value)
            .Select(model => TryModelKey(model, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .ToHashSet(ModelKeyComparer.Instance);
        ModelKey[] required = requiredKeys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        return required.Length > 0 && required.All(present.Contains);
    }

    private static bool IsAuditedBaseGameCardSnapshot(NeowEffectCardSnapshot card) =>
        string.Equals(card.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(card.SourceModId);

    private static bool IsAuditedBaseGamePotionSnapshot(NeowEffectPotionSnapshot potion) =>
        string.Equals(potion.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(potion.SourceModId);

    private static IReadOnlyList<ModelKey>? BuildBonesEligibleRelics(
        RuntimeProfileId profileId,
        int playersCount,
        bool? allCharacterPoolsUnlocked,
        int? unlockedCommonCards,
        int? unlockedUncommonCards,
        out bool exact)
    {
        exact = profileId is RuntimeProfileId.Stable107 or RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111;
        if (!exact)
        {
            return null;
        }
        IEnumerable<ModelKey> values = NeowEffectCoverageRegistry.GetApplicableKeys(profileId)
            .Where(key => key != BaseGameModelKeys.Relics.NeowsBones);
        if (RuntimeProfilePolicies.IsModernCore(profileId))
        {
            bool? scrollBoxesAllowed = unlockedCommonCards.HasValue && unlockedUncommonCards.HasValue
                ? unlockedCommonCards.Value >= 4 && unlockedUncommonCards.Value >= 2 : null;
            exact &= allCharacterPoolsUnlocked.HasValue && scrollBoxesAllowed.HasValue;
            values = values.Where(key => Core.Neow.ModernNeowIdentityPredictor.IsAllowed(
                key, playersCount, allCharacterPoolsUnlocked, scrollBoxesAllowed) != false);
        }
        return values.ToArray();
    }

    private static ModelKey? UniqueDeckKey(IReadOnlyList<NeowEffectCardSnapshot>? deck, Func<NeowEffectCardSnapshot, bool> predicate)
    {
        ModelKey[] keys = (deck ?? Array.Empty<NeowEffectCardSnapshot>())
            .Where(predicate)
            .Select(card => card.CardKey)
            .Distinct()
            .ToArray();
        return keys.Length == 1 ? keys[0] : null;
    }

    private static NestedRelicEffectKind ClassifyNestedRelicEffect(object model, ModelKey key, out bool exact)
    {
        exact = string.Equals(model.GetType().Assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase);
        if (key == BaseGameModelKeys.OrdinaryRelics.Whetstone)
        {
            return NestedRelicEffectKind.Whetstone;
        }
        if (key == BaseGameModelKeys.OrdinaryRelics.WarPaint)
        {
            return NestedRelicEffectKind.WarPaint;
        }
        Type? baseRelicType = model.GetType();
        while (baseRelicType?.BaseType is not null && !string.Equals(baseRelicType.Name, "RelicModel", StringComparison.Ordinal))
        {
            baseRelicType = baseRelicType.BaseType;
        }
        if (baseRelicType is null || !string.Equals(baseRelicType.Name, "RelicModel", StringComparison.Ordinal))
        {
            exact = false;
            return NestedRelicEffectKind.UnknownHook;
        }
        bool overrides = model.GetType().GetMethods(PublicInstance | BindingFlags.NonPublic)
            .Where(method => method.DeclaringType == model.GetType())
            .Any(method => method.Name is "AfterObtained" or "OnObtained" or "OnObtain" or "AfterObtain");
        return overrides ? NestedRelicEffectKind.UnknownHook : NestedRelicEffectKind.NoTrackedImmediateEffect;
    }

    private static IEnumerable? InvokeUnlockedCollection(
        object pool,
        object unlockState,
        string poolId,
        int playersCount,
        out bool usedAuditedMethod)
    {
        usedAuditedMethod = false;
        string exactMethodName = poolId.StartsWith("potion", StringComparison.Ordinal)
            ? "GetUnlockedPotions"
            : poolId.StartsWith("relic", StringComparison.Ordinal)
                ? "GetUnlockedRelics"
                : "GetUnlockedCards";

        MethodInfo[] candidates = pool.GetType().GetMethods(PublicInstance)
            .Where(method =>
                string.Equals(method.Name, exactMethodName, StringComparison.Ordinal) ||
                string.Equals(method.Name, "GetUnlockedModels", StringComparison.Ordinal) ||
                string.Equals(method.Name, "GetUnlocked", StringComparison.Ordinal))
            .OrderBy(method => string.Equals(method.Name, exactMethodName, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(method => method.GetParameters().Length)
            .ToArray();

        foreach (MethodInfo method in candidates)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length is < 1 or > 2 ||
                !parameters[0].ParameterType.IsInstanceOfType(unlockState))
            {
                continue;
            }

            object?[] args = new object?[parameters.Length];
            args[0] = unlockState;
            bool exactInvocation = string.Equals(method.Name, exactMethodName, StringComparison.Ordinal);

            if (parameters.Length == 2)
            {
                if (!TryCreateMultiplayerConstraint(parameters[1], playersCount, out object? constraint))
                {
                    continue;
                }
                args[1] = constraint;
            }

            try
            {
                if (method.Invoke(pool, args) is IEnumerable enumerable)
                {
                    usedAuditedMethod = exactInvocation;
                    return enumerable;
                }
            }
            catch
            {
                // Try another explicitly compatible overload. Failure never falls back to
                // a raw backing collection and never upgrades the pool to Exact.
            }
        }

        return null;
    }

    private static bool TryCreateMultiplayerConstraint(
        ParameterInfo parameter,
        int playersCount,
        out object? value)
    {
        Type type = parameter.ParameterType;
        if (!type.IsEnum)
        {
            if (parameter.HasDefaultValue)
            {
                value = parameter.DefaultValue;
                return true;
            }
            value = null;
            return false;
        }

        string[] preferredNames = playersCount > 1
            ? new[] { "MultiplayerOnly", "MultiPlayerOnly", "Multiplayer" }
            : new[] { "SingleplayerOnly", "SinglePlayerOnly", "Singleplayer" };
        foreach (string name in preferredNames)
        {
            if (Enum.GetNames(type).Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = Enum.Parse(type, name, ignoreCase: true);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static Type? FindType(Assembly assembly, string fullName, string simpleName) =>
        assembly.GetType(fullName, throwOnError: false, ignoreCase: false) ??
        SafeGetTypes(assembly).FirstOrDefault(type => string.Equals(type.Name, simpleName, StringComparison.Ordinal));

    private static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch { return Array.Empty<Type>(); }
    }

    private static object? ReadProperty(object? instance, string name)
    {
        if (instance is null) return null;
        try
        {
            Type type = instance.GetType();
            PropertyInfo? property = type.GetProperty(name, PublicInstance | BindingFlags.NonPublic);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(instance);
            }
            return type.GetField(name, PublicInstance | BindingFlags.NonPublic)?.GetValue(instance);
        }
        catch { return null; }
    }

    private static bool? TryReadBool(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadProperty(instance, name);
            if (value is bool result) return result;
        }
        return null;
    }

    private static int? TryReadInt(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadProperty(instance, name);
            if (value is int result) return result;
            if (value is IConvertible convertible)
            {
                try { return convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture); }
                catch { }
            }
        }
        return null;
    }

    private static string ReadEnumName(object instance, string name) => ReadProperty(instance, name)?.ToString() ?? string.Empty;

    private static bool HasTag(object model, string tagName)
    {
        foreach (object tag in Enumerate(ReadProperty(model, "Tags")))
        {
            if (string.Equals(tag.ToString(), tagName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (MethodInfo method in model.GetType().GetMethods(PublicInstance).Where(method => string.Equals(method.Name, "HasTag", StringComparison.Ordinal)))
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum) continue;
            try
            {
                object tag = Enum.Parse(parameters[0].ParameterType, tagName, ignoreCase: true);
                if (method.Invoke(model, new[] { tag }) is true) return true;
            }
            catch { }
        }
        return false;
    }

    private static IEnumerable<object> Enumerate(object? value)
    {
        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (object? item in enumerable)
            {
                if (item is not null) yield return item;
            }
        }
    }

    private static bool TryModelKey(object? model, out ModelKey key)
    {
        key = default;
        object? id = model;
        if (model is not null && !string.Equals(model.GetType().Name, "ModelId", StringComparison.Ordinal))
        {
            id = ReadProperty(model, "Id");
        }
        if (id is null) return false;
        string category = ReadProperty(id, "Category")?.ToString() ?? string.Empty;
        string entry = ReadProperty(id, "Entry")?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(entry)) return false;
        key = new ModelKey(category, entry);
        return key.IsValid;
    }

    private static IEnumerable<string> EnumerateModelKeys(object unlockState)
    {
        foreach (PropertyInfo property in unlockState.GetType().GetProperties(PublicInstance).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            object? value;
            try { value = property.GetIndexParameters().Length == 0 ? property.GetValue(unlockState) : null; }
            catch { continue; }
            foreach (object item in Enumerate(value))
            {
                if (TryModelKey(item, out ModelKey key)) yield return $"{property.Name}:{key.Serialized}";
            }
        }
    }

    private static IReadOnlyDictionary<ModelKey, ModelKey> CaptureCharacterByCardPool(object unlockState)
    {
        var output = new Dictionary<ModelKey, ModelKey>(ModelKeyComparer.Instance);
        foreach (object character in Enumerate(ReadProperty(unlockState, "Characters")))
        {
            if (!TryModelKey(character, out ModelKey characterKey) ||
                !string.Equals(characterKey.Category, BaseGameModelKeys.Categories.Character, StringComparison.Ordinal))
            {
                continue;
            }
            object? pool = new[] { "CardPool", "CharacterCardPool", "Cards" }
                .Select(name => ReadProperty(character, name))
                .FirstOrDefault(value => value is not null);
            if (pool is null || !TryModelKey(pool, out ModelKey poolKey))
            {
                continue;
            }
            output[poolKey] = characterKey;
        }
        return output;
    }

    private static string CharacterEntryFromPool(string entry)
    {
        if (entry.EndsWith("_CARD_POOL", StringComparison.OrdinalIgnoreCase))
        {
            return entry[..^"_CARD_POOL".Length];
        }
        if (entry.EndsWith("CardPool", StringComparison.Ordinal))
        {
            return entry[..^"CardPool".Length].ToUpperInvariant();
        }
        return entry.ToUpperInvariant();
    }

    private static bool TryParseCardRarity(string value, out EffectCardRarity rarity) =>
        Enum.TryParse(value, ignoreCase: true, out rarity);

    private static bool TryParseCardType(string value, out EffectCardType type)
    {
        if (Enum.TryParse(value, ignoreCase: true, out type)) return true;
        type = EffectCardType.Other;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParsePotionRarity(string value, out EffectPotionRarity rarity)
    {
        if (Enum.TryParse(value, ignoreCase: true, out rarity)) return true;
        rarity = EffectPotionRarity.Other;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseRelicRarity(string value, out EffectRelicRarity rarity)
    {
        if (Enum.TryParse(value, ignoreCase: true, out rarity)) return true;
        rarity = EffectRelicRarity.Other;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsMultiplayerOnly(object model)
    {
        foreach (string name in new[] { "MultiplayerConstraint", "CardMultiplayerConstraint", "PotionMultiplayerConstraint", "RelicMultiplayerConstraint" })
        {
            string value = ReadProperty(model, name)?.ToString() ?? string.Empty;
            if (string.Equals(value, "MultiplayerOnly", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? SourceModIdFor(object model)
    {
        string? assemblyName = model.GetType().Assembly.GetName().Name;
        return string.Equals(assemblyName, "sts2", StringComparison.OrdinalIgnoreCase)
            ? null
            : assemblyName;
    }

    private static string Fingerprint(string prefix, IEnumerable<string>? values)
    {
        string payload = prefix + "\n" + string.Join("\n", values ?? Array.Empty<string>());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static IEnumerable<string> EnumerateCardDescriptors(
        IReadOnlyList<NeowEffectCardSnapshot>? selected,
        IReadOnlyList<NeowEffectCardSnapshot>? colorless,
        IReadOnlyList<CharacterCardPoolSnapshot>? others)
    {
        foreach (NeowEffectCardSnapshot card in selected ?? Array.Empty<NeowEffectCardSnapshot>()) yield return "selected:" + CardDescriptor(card);
        foreach (NeowEffectCardSnapshot card in colorless ?? Array.Empty<NeowEffectCardSnapshot>()) yield return "colorless:" + CardDescriptor(card);
        foreach (CharacterCardPoolSnapshot pool in others ?? Array.Empty<CharacterCardPoolSnapshot>())
        {
            yield return "pool:" + pool.CharacterKey.Serialized + ":" + pool.PoolId;
            foreach (NeowEffectCardSnapshot card in pool.Cards) yield return "other:" + CardDescriptor(card);
        }
    }

    private static string CardDescriptor(NeowEffectCardSnapshot card) =>
        $"{card.PoolOrder}:{card.CardKey.Serialized}:{card.Rarity}:{card.CardType}:{card.IsBasic}:{card.IsStrike}:{card.IsDefend}:{card.CanUpgrade}:{card.CanRemove}:{card.UpgradeLevel}:{card.MaxUpgradeLevel}:{card.PoolId}:{card.CanBeGeneratedByModifiers?.ToString() ?? "unknown"}:{card.IsMultiplayerOnly}:{card.CanBeGeneratedInCombat?.ToString() ?? "unknown"}:{card.IsUnlockedInCapturedPool}:{card.IsDiscovered?.ToString() ?? "unknown"}:{card.EligibleForPostCombatRewardByPoolMembership}:{card.EligibilityAuthority}:{card.CatalogProfileId}:{card.BehaviorMetadataExact}";

    private static string RelicDescriptor(NeowEffectRelicSnapshot relic) =>
        $"{relic.BagOrder}:{relic.RelicKey.Serialized}:{relic.Rarity}:{relic.RarityCode}:{relic.NestedEffectKind}:{relic.NestedClassificationExact}:{relic.SourceAssembly}:{relic.SourceModId}:{relic.IsMultiplayerOnly}:{relic.IsAllowedInShops}:{relic.ShopEligibilityExact}";

    private static string PotionDescriptor(NeowEffectPotionSnapshot potion) =>
        $"{potion.PoolOrder}:{potion.PotionKey.Serialized}:{potion.Rarity}:{potion.AllowedOutOfCombat}:{potion.IsMultiplayerOnly}";
}
