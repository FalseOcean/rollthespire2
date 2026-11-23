using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects;
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
        public EffectCatalogRules Rules { get; init; } = null!;
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
        public IReadOnlyList<CharacterCardPoolSnapshot>? KaleidoscopePools { get; init; }
        public string? KaleidoscopePoolIssue { get; init; }
        public IReadOnlyList<ModelKey>? NativeCharacterPoolOrder { get; init; }
        public bool OtherPoolsExact { get; init; }
        public IReadOnlyList<NeowEffectCardSnapshot>? Deck { get; init; }
        public bool DeckExact { get; init; }
        public bool? AllCharacterCardPoolsUnlocked { get; init; }
        public bool AllCharacterPoolsAuthorityExact { get; init; }
        public Core.Events.EventResultCatalogParts EventCatalogParts { get; init; }
        public bool OtherIdentityExact { get; init; }
        public IReadOnlyList<ModelKey>? ColorfulCandidateColors { get; init; }
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
        var characterPotions = FilterForPlayers(observed.CharacterPotions, playersCount);
        var sharedPotions = FilterForPlayers(observed.SharedPotions, playersCount);
        var sharedRelics = observed.SharedRelics; var characterRelics = observed.CharacterRelics;
        bool? allCharacterCardPoolsUnlocked = observed.AllCharacterCardPoolsUnlocked;
        bool allCharacterPoolsAuthorityExact = observed.AllCharacterPoolsAuthorityExact;
        bool eventResultStaticCatalogExact = observed.EventCatalogParts == Core.Events.EventResultCatalogParts.All;
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
        var kaleidoscopePools = StampCardCatalogProfile(observed.KaleidoscopePools, profile.ProfileId);
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
            RuntimeVersionCompatibility.Resolve(capturedGameVersion).SemanticProfileId,
            out bool relicBagExact,
            out bool nestedRelicHooksExact);

        IReadOnlyList<ModelKey>? generatedCursePool = rules.BuildGeneratedCursePool(
            curseCards.Cards,
            curseCards.Exact,
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
            RuntimeProfilePolicies.SupportsEventResultModel(profile.ProfileId) &&
            selectedCharacterUnlocked &&
            observed.ColorfulCandidateColors is not null;

        IReadOnlyList<NeowEffectCardSnapshot>? merchantColorlessOrderedPool = colorlessCards.Cards?
            .OrderBy(card => card.PoolOrder)
            .ToArray();
        // Replay the standard shop rules over the captured ordered identities.
        // A patch on the game or a card's defining assembly does not remove its
        // observed rarity, type, or position in the stock.
        bool merchantColorlessOrderedPoolExact =
            RuntimeProfilePolicies.SupportsMerchantColorlessModel(profile.ProfileId) &&
            colorlessCards.IdentityExact && merchantColorlessOrderedPool is not null;
        bool merchantColorlessPoolIdentityHooksExact = merchantColorlessOrderedPoolExact;
        bool merchantColorlessRarityIdentityHooksExact = merchantColorlessOrderedPoolExact;

        // Reaching the colorless slots still requires the earlier character slots
        // to be fillable. Modern113 also draws the potions before the cards.
        bool merchantInitialInventoryShapeExact =
            merchantColorlessOrderedPoolExact && playersCount >= 1 &&
            selectedCards.IdentityExact && selectedCards.Cards is not null &&
            selectedCards.Cards.Count(card => card.CardType == EffectCardType.Attack && !card.IsBasic) >= 2 &&
            selectedCards.Cards.Count(card => card.CardType == EffectCardType.Skill && !card.IsBasic) >= 2 &&
            selectedCards.Cards.Count(card => card.CardType == EffectCardType.Power && !card.IsBasic) >= 1 &&
            merchantColorlessOrderedPool is not null &&
            merchantColorlessOrderedPool.Any(card => card.Rarity == EffectCardRarity.Uncommon) &&
            merchantColorlessOrderedPool.Any(card => card.Rarity == EffectCardRarity.Rare);
        bool modernMerchant = RuntimeVersionCompatibility.Resolve(capturedGameVersion).SemanticProfileId == RngSemanticProfileId.Modern113;
        var observedMerchantPotionStock = modernMerchant &&
            observed.CharacterPotions is { Exact: true, Potions: { } personal } &&
            observed.SharedPotions is { Exact: true, Potions: { } shared }
            ? personal.Concat(shared).ToArray() : null;
        NeowEffectPotionSnapshot[]? merchantPotionStock = null;
        if (merchantInitialInventoryShapeExact && modernMerchant)
        {
            // Five different canonical identities are picked before colorless.
            // Disjoint pools mean the full-inventory blacklist cannot shift U/R
            // indexes. PotionFactory samples with one removal per selection.
            var colored = selectedCards.Cards!.Where(c => c.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
                .DistinctBy(c => c.CardKey).ToArray();
            merchantInitialInventoryShapeExact = colored.Count(c => c.CardType == EffectCardType.Attack) >= 2 &&
                colored.Count(c => c.CardType == EffectCardType.Skill) >= 2 &&
                colored.Any(c => c.CardType == EffectCardType.Power) &&
                !colored.Select(c => c.CardKey).Intersect(merchantColorlessOrderedPool!.Select(c => c.CardKey)).Any() &&
                merchantColorlessOrderedPool!.Select(c => c.CardKey).Distinct().Count() == merchantColorlessOrderedPool!.Count;
            if (merchantInitialInventoryShapeExact)
                merchantPotionStock = observedMerchantPotionStock;
            merchantInitialInventoryShapeExact = Core.Merchant.Beta111NormalMerchantShopsContinuation
                .HasUnconditionalPotionStock(merchantPotionStock);
        }

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
            EnumerateCardDescriptors(selectedCards.Cards, colorlessCards.Cards, otherCharacterPools)
                .Concat(EnumerateKaleidoscopeDescriptors(kaleidoscopePools, observed.KaleidoscopePoolIssue))
                .Concat(observed.NativeCharacterPoolOrder is { } nativeOrder
                    ? new[] { "native-character-pool-order=" + string.Join(">", nativeOrder.Select(key => key.Serialized)) }
                    : new[] { "native-character-pool-order=unobserved" }));
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
            observed.EventCatalogParts.ToString(), eventColorfulCharacterPoolsExact.ToString(),
            string.Join(">", observed.ColorfulCandidateColors ?? []),
            selectedCards.IdentityExact.ToString(), colorlessCards.IdentityExact.ToString(), observed.OtherIdentityExact.ToString(),
            merchantColorlessOrderedPoolExact.ToString(), merchantColorlessPoolIdentityHooksExact.ToString(),
            merchantColorlessRarityIdentityHooksExact.ToString(), merchantInitialInventoryShapeExact.ToString(),
            observedMerchantPotionStock == null ? "merchant-potions-unobserved" :
                string.Join("|", observedMerchantPotionStock.Select(p => p.PotionKey.Serialized + ":" + p.Rarity + ":" + p.MerchantCloneRuleExact)),
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
            MerchantInitialInventoryShapeExact: merchantInitialInventoryShapeExact,
            NativeCharacterPoolOrder: observed.NativeCharacterPoolOrder)
        {
            ColorlessRewardIdentityPoolExact = colorlessCards.IdentityExact,
            MerchantPotionStock = merchantPotionStock,
            ObservedMerchantPotionStock = observedMerchantPotionStock,
            OtherCharacterIdentityPoolsExact = observed.OtherIdentityExact,
            KaleidoscopePools = kaleidoscopePools, KaleidoscopePoolIssue = observed.KaleidoscopePoolIssue,
            EventCatalogParts = observed.EventCatalogParts,
            ColorfulCandidateColors = observed.ColorfulCandidateColors
        };

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
        string gameVersion, object? explicitUnlockState, NativeObservationSchedule schedule, bool failSoft = true,
        IReadOnlyList<(ModelKey Character, UnlockState Unlocks)>? partyRoster = null)
    {
        try
        {
            var observed = await ObserveEffectCore(profile, rawSeed, character, ascension, playersCount,
                playerSlotIndex, gameVersion, explicitUnlockState, schedule, RuntimeProfilePolicies.EffectCatalogRules(profile.ProfileId),
                partyRoster).ConfigureAwait(false);
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
        EffectCatalogRules rules, IReadOnlyList<(ModelKey Character, UnlockState Unlocks)>? partyRoster)
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
        var (others, otherExact, otherIdentityExact) = await ObserveOtherCharacterPools(unlockState, character.CharacterKey, playersCount,
            rules, schedule).ConfigureAwait(false);
        var kaleidoscope = await ObserveKaleidoscopePools(model, unlockState, others, playersCount, ascension,
            playerSlotIndex, canonicalSeed, schedule, partyRoster).ConfigureAwait(false);
        var nativeOrder = ObserveNativeCharacterPoolOrder(unlockState);
        var (deck, deckExact) = await ObserveStartingDeck(model, character.CharacterKey, audited, schedule).ConfigureAwait(false);
        var characterPotions = await ObservePoolObject(PropertyPool("PotionPool","CharacterPotionPool","Potions"), unlockState,
            "potion-character", ConvertPotionModel, playersCount, schedule).ConfigureAwait(false);
        var sharedPotions = await ObservePoolObject(StaticPool("PotionPool","PotionPools","SharedPotionPool"), unlockState,
            "potion-shared", ConvertPotionModel, playersCount, schedule).ConfigureAwait(false);
        var sharedRelics = await Pool(StaticPool("RelicPool","RelicPools","SharedRelicPool"), "relic-shared", ConvertRelicModel).ConfigureAwait(false);
        var characterRelics = await Pool(PropertyPool("RelicPool","CharacterRelicPool","Relics"), "relic-character", ConvertRelicModel).ConfigureAwait(false);
        await schedule.Checkpoint("UnlockCoverage").ConfigureAwait(false);
        bool? allUnlocked = CaptureAllCharacterCardPoolsUnlocked(assembly, unlockState, out bool allExact);
        var events = Core.Events.EventResultCatalogParts.None;
        IReadOnlyList<ModelKey>? colorfulCandidates = null;
        if (RuntimeProfilePolicies.SupportsEventResultModel(profile.ProfileId))
        {
            colorfulCandidates = ObserveColorfulCandidates(unlockState, PropertyPool("CardPool"),
                RuntimeVersionCompatibility.Resolve(gameVersion).SemanticProfileId);
            if (ModelDbContainsAll(assembly, "AllCards", Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards))
                events |= Core.Events.EventResultCatalogParts.TrashGrab;
            if (ModelDbContainsAll(assembly, "AllRelics", Core.Events.Beta111EventResultCatalog.TrashHeapDiveRelics))
                events |= Core.Events.EventResultCatalogParts.TrashDive;
            if (ModelDbContainsAll(assembly, "AllRelics", Core.Events.Beta111EventResultCatalog.FakeMerchantRelics))
                events |= Core.Events.EventResultCatalogParts.FakeMerchant;
        }
        var descriptors = new List<string>();
        foreach (string descriptor in EnumerateModelKeys(unlockState))
        {
            await schedule.Checkpoint("UnlockIdentity").ConfigureAwait(false);
            descriptors.Add(descriptor);
        }
        return new() { Rules=rules, ObservedAtUtc=DateTimeOffset.UtcNow, CanonicalSeed=canonicalSeed, IsAuditedVanilla=audited, SelectedCharacterUnlocked=unlocked,
            SelectedCards=selected, ColorlessCards=colorless, CurseCards=curses, OtherCharacterPools=others,
            KaleidoscopePools=kaleidoscope.Pools, KaleidoscopePoolIssue=kaleidoscope.Issue, NativeCharacterPoolOrder=nativeOrder,
            OtherPoolsExact=otherExact, Deck=deck, DeckExact=deckExact, CharacterPotions=characterPotions,
            SharedPotions=sharedPotions, SharedRelics=sharedRelics, CharacterRelics=characterRelics,
            AllCharacterCardPoolsUnlocked=allUnlocked, AllCharacterPoolsAuthorityExact=allExact,
            ColorfulCandidateColors=colorfulCandidates, EventCatalogParts=events, OtherIdentityExact=otherIdentityExact, UnlockDescriptors=descriptors.ToArray(), UnlockSource=unlockSource };
    }
    private static IReadOnlyList<ModelKey>? ObserveColorfulCandidates(object unlockState, object? ownPool,
        RngSemanticProfileId semantics)
    {
        if (!TryModelKey(ownPool, out var own) || ReadProperty(unlockState, "CharacterCardPools") is not IEnumerable pools)
            return null;
        var unlocked = new HashSet<ModelKey>();
        foreach (var pool in pools)
        {
            if (!TryModelKey(pool, out var key)) return null;
            unlocked.Add(key);
        }
        unlocked.Remove(own);
        return Core.Events.Beta111EventResultCatalog.ColorfulOrder(semantics)
            .Where(color => unlocked.Contains(Core.Events.Beta111EventResultCatalog.PoolForColor(color))).ToArray();
    }

    private static IReadOnlyList<ModelKey>? ObserveNativeCharacterPoolOrder(object unlockState)
    {
        if (ReadProperty(unlockState, "Characters") is not IEnumerable characters) return null;
        var keys = new List<ModelKey>();
        foreach (object? character in characters)
        {
            if (!TryModelKey(character, out var key) || keys.Contains(key)) return null;
            keys.Add(key);
        }
        return keys.ToArray();
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
        foreach (object? model in models)
        {
            await schedule.Checkpoint("Pool:" + poolId + ":" + order).ConfigureAwait(false);
            object? converted = model is null ? null : converter(model, order, poolId);
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

    private static async Task<(IReadOnlyList<CharacterCardPoolSnapshot>? Pools, bool Exact, bool IdentityExact)> ObserveOtherCharacterPools(
        object unlockState,
        ModelKey selectedCharacter,
        int playersCount,
        EffectCatalogRules rules,
        NativeObservationSchedule schedule)
    {
        bool exact = true, identityExact = true;
        var output = new List<CharacterCardPoolSnapshot>();
        object? poolsValue = ReadProperty(unlockState, "CharacterCardPools");
        if (poolsValue is not IEnumerable pools) return (null, false, false);
        var poolObjects = pools.Cast<object?>().ToList();
        if (rules.OtherCharacterPoolsUseModelIdOrder)
        {
            poolObjects = poolObjects.OrderBy(pool => TryModelKey(pool, out ModelKey key) ? key.Category : string.Empty, StringComparer.Ordinal)
                .ThenBy(pool => TryModelKey(pool, out ModelKey key) ? key.Entry : string.Empty, StringComparer.Ordinal)
                .ToList();
        }
        var charactersByPool = CaptureCharactersByCardPool(unlockState);
        int poolOrder = 0;
        foreach (object? pool in poolObjects)
        {
            await schedule.Checkpoint("OtherCharacterPool").ConfigureAwait(false);
            if (!TryModelKey(pool, out ModelKey poolKey))
            {
                exact = identityExact = false;
                continue;
            }
            ModelKey characterKey;
            if (!charactersByPool.TryGetValue(poolKey, out var owners) || !owners.TryDequeue(out characterKey))
            {
                // Compatibility fallback for historical/runtime shapes that expose
                // pool identity but not CharacterModel.CardPool. It is deliberately
                // marked non-exact: arbitrary Mod pool naming must not become semantic
                // character authority merely because it resembles vanilla naming.
                string characterEntry = CharacterEntryFromPool(poolKey.Entry);
                characterKey = new ModelKey(BaseGameModelKeys.Categories.Character, characterEntry);
                exact = identityExact = false;
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
                exact = identityExact = false;
                continue;
            }
            exact &= capture.Exact;
            identityExact &= capture.IdentityExact;
            output.Add(new CharacterCardPoolSnapshot(characterKey, poolKey.Serialized, poolOrder++, capture.Cards));
        }
        return (output.ToArray(), exact, identityExact);
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
