using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorSeedCapture
{
    // The analysis page owns the declared unlock context. Account defaults must
    // not replace a search result/favorite's explicitly selected premise.
    internal static PredictorRun CreateFromPrediction(SeedPredictionRequest request, SerializableUnlockState unlocks)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (request.PlayersCount != 1 || request.PlayerSlotIndex != 0)
            throw new InvalidOperationException("PredictorRouteRequiresSingleplayer");
        if (!RolltheSpire2.Compatibility.ProfileSeedRules.TryCanonicalizePredictionSeed(RolltheSpire2.Compatibility.Beta111Profile.Instance, request.OriginalSeed,
                out string canonicalSeed, out string issue))
            throw new InvalidOperationException("PredictorRouteSeedInvalid:" + issue);
        var authority = request.Authority;
        var source = authority.WorldAuthority?.Beta109Generation
            ?? throw new InvalidOperationException("PredictorWorldCatalogMissing");
        var discovered = source.ActSelectionGroups.SelectMany(g => g.Candidates)
            .Where(c => c.DiscoveredInSingleplayer).Select(c => c.ActKey).Distinct().ToImmutableArray();
        var context = new PredictorContext(canonicalSeed, authority.GameVersion, request.Character.CharacterKey,
            request.Ascension, authority.CatalogFingerprint, authority.UnlockSnapshotFingerprint,
            unlocks.UnlockedEpochs.ToImmutableArray(),
            unlocks.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry)).ToImmutableArray(),
            discovered, unlocks.NumberOfRuns)
        { AccountWongoPoints = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.WongoPoints };
        return CreateRun(context, authority);
    }

    internal static PredictorRun CreateFromAccount(string seed, ModelKey character, int ascension, string gameVersion)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var save = MegaCrit.Sts2.Core.Saves.SaveManager.Instance;
        var unlocks = save.GenerateUnlockStateFromProgress();
        var serialized = unlocks.ToSerializable();
        var discovered = save.Progress.DiscoveredActs.Select(k => new ModelKey(k.Category, k.Entry)).ToImmutableArray();
        var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, seed, CharacterIdentity.FromKey(character), ascension, gameVersion,
            predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
            predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
            explicitUnlockState: unlocks, explicitDiscoveredActs: discovered);
        var context = new PredictorContext(seed, gameVersion, character, ascension,
            authority.CatalogFingerprint, authority.UnlockSnapshotFingerprint, serialized.UnlockedEpochs.ToImmutableArray(),
            serialized.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry)).ToImmutableArray(), discovered, serialized.NumberOfRuns)
        { AccountWongoPoints = save.Progress.WongoPoints };
        return CreateRun(context, authority);
    }

    // Product entry for a frozen single-player context. Both authority donors
    // come from the same declared unlock/discovery facts; no live RunState or
    // Search continuation is used to seed the Predictor.
    internal static PredictorRun CreateRun(PredictorContext context)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        PredictorSeedState.ValidateContext(context);
        var unlocks = new UnlockState(context.RevealedEpochs,
            context.SeenEncounters.Select(k => new ModelId(k.Category, k.Entry)), context.NumberOfRuns);
        var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, context.Seed,
            CharacterIdentity.FromKey(context.Character), context.Ascension, context.GameVersion,
            predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
            predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
            explicitUnlockState: unlocks, explicitDiscoveredActs: context.DiscoveredActs);
        if (authority.CatalogFingerprint != context.CatalogFingerprint ||
            authority.UnlockSnapshotFingerprint != context.UnlockFingerprint)
            throw new InvalidOperationException("PredictorDeclaredAuthorityFingerprintMismatch");
        return CreateRun(context, authority);
    }

    internal static ImmutableArray<PredictorCardLevelMetadata> CaptureLevels(CardModel canonical)
    {
        var card = canonical.ToMutable();
        var levels = ImmutableArray.CreateBuilder<PredictorCardLevelMetadata>();
        for (int level = 0; level <= card.MaxUpgradeLevel; level++)
        {
            if (level > 0) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            var keywords = card.GetKeywordsWithSources(KeywordSources.Local).ToHashSet();
            levels.Add(new(level, card.EnergyCost.GetWithModifiers(CostModifiers.Local), card.EnergyCost.CostsX,
                keywords.Contains(CardKeyword.Unplayable), keywords.Contains(CardKeyword.Exhaust),
                keywords.Contains(CardKeyword.Innate), keywords.Contains(CardKeyword.Retain), keywords.Contains(CardKeyword.Eternal)));
        }
        return levels.ToImmutable();
    }
    // Main-thread, canonical metadata only. No Player, RunState, obtain hooks or
    // game RNG are created/mutated, and no captured model reference escapes.
    internal static PredictorRun CreateRun(PredictorContext context, RuntimeContextAuthoritySnapshot authority)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var character = ModelDb.GetById<CharacterModel>(new ModelId(context.Character.Category, context.Character.Entry));
        var relics = character.StartingRelics.Select(r => new ModelKey(r.Id.Category, r.Id.Entry)).ToImmutableArray();
        // From-seed initialization uses the character definition, never a deck
        // snapshot captured from an already-running game/search continuation.
        var deck = character.StartingDeck.Select(c => new PredictorCard(0, new(c.Id.Category, c.Id.Entry),
            Enum.Parse<RolltheSpire2.Core.Effects.Snapshots.EffectCardType>(c.Type.ToString()),
            c.CurrentUpgradeLevel, c.MaxUpgradeLevel, c.IsRemovable, 1)).ToImmutableArray();
        var run = PredictorInitialization.FromSeed(context, authority, deck, relics, character.StartingGold);
        return new(CaptureCatalog(run.Context, authority), run.Working);
    }

    // Metadata only. Current-scene capture must not reconstruct the world or
    // apply starting inventory/Ascension effects to an already-running player.
    internal static PredictorContext CaptureCatalog(PredictorContext context, RuntimeContextAuthoritySnapshot authority)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var character = ModelDb.GetById<CharacterModel>(new ModelId(context.Character.Category, context.Character.Entry));
        var effects = authority.EffectAuthority!;
        context = context with { Catalog = new(effects.CharacterRewardPool!.ToImmutableArray(), effects.ColorlessRewardPool!.ToImmutableArray()),
            PotionPool = effects.PotionPool!.Select(p => new PredictorPotionOption(p.PotionKey,
                p.Rarity.ToString() switch { "Common" => PredictorPotionRarity.Common, "Uncommon" => PredictorPotionRarity.Uncommon, "Rare" => PredictorPotionRarity.Rare, _ => PredictorPotionRarity.Event })).ToImmutableArray() };
        if (effects.UnlockedCharacterCardPoolKeys == null || effects.OtherCharacterPools == null)
            throw new InvalidOperationException("PredictorUnlockedCharacterCatalogMissing");
        var allCards = ModelDb.AllCharacters.Where(c => effects.UnlockedCharacterCardPoolKeys.Contains(new ModelKey(c.Id.Category, c.Id.Entry)))
            .SelectMany(c => c.Id == character.Id ? effects.CharacterRewardPool! :
                effects.OtherCharacterPools.Single(p => p.CharacterKey == new ModelKey(c.Id.Category, c.Id.Entry)).Cards).ToImmutableArray();
        var characterPools = ModelDb.AllCharacters.Where(c => effects.UnlockedCharacterCardPoolKeys.Contains(new ModelKey(c.Id.Category, c.Id.Entry)))
            .Select(c => new PredictorCharacterPool(new(c.Id.Category, c.Id.Entry), (c.Id == character.Id ? effects.CharacterRewardPool! :
                effects.OtherCharacterPools.Single(p => p.CharacterKey == new ModelKey(c.Id.Category, c.Id.Entry)).Cards)
                .Where(card => card.IsUnlockedInCapturedPool && !card.IsMultiplayerOnly).Select(card => card.CardKey).ToImmutableArray())).ToImmutableArray();
        EncounterModel[] eventEncounters = [ModelDb.Encounter<DenseVegetationEventEncounter>(), ModelDb.Encounter<PunchOffEventEncounter>(),
            ModelDb.Encounter<MysteriousKnightEventEncounter>(), ModelDb.Encounter<BattlewornDummyEventV1Encounter>(),
            ModelDb.Encounter<BattlewornDummyEventV2Encounter>(), ModelDb.Encounter<BattlewornDummyEventV3Encounter>(), ModelDb.Encounter<FakeMerchantEventEncounter>()];
        var encounters = ModelDb.AllEncounters.Concat(eventEncounters).Select(e => new ModelKey(e.Id.Category, e.Id.Entry)).Distinct()
            .Select(key =>
            {
                var model = ModelDb.GetById<EncounterModel>(new ModelId(key.Category, key.Entry));
                // EncounterModel's default getters consult the live RunManager
                // ascension. Reconstruct from the requested frozen context instead.
                if (key.Entry == "FAKE_MERCHANT_EVENT_ENCOUNTER") return new PredictorEncounterDefinition(key, 300, 300)
                { IsMonsterEncounter = model.RoomType == MegaCrit.Sts2.Core.Rooms.RoomType.Monster };
                if (model.GetType().GetProperty(nameof(EncounterModel.MinGoldReward))!.DeclaringType != typeof(EncounterModel) ||
                    model.GetType().GetProperty(nameof(EncounterModel.MaxGoldReward))!.DeclaringType != typeof(EncounterModel))
                    throw new NotImplementedException("PredictorEncounterGoldOverridePending:" + key);
                var range = model.RoomType switch { MegaCrit.Sts2.Core.Rooms.RoomType.Monster => (10,20),
                    MegaCrit.Sts2.Core.Rooms.RoomType.Elite => (35,45), MegaCrit.Sts2.Core.Rooms.RoomType.Boss => (100,100), _ => (0,0) };
                double factor = context.Ascension >= 3 ? .75 : 1;
                return new PredictorEncounterDefinition(key, (int)(range.Item1 * factor), (int)(range.Item2 * factor), model.ShouldGiveRewards)
                { EnemyCreationSources = PredictorCombatEnemyFacts.Sources(model.AllPossibleMonsters.Select(m => new ModelKey(m.Id.Category, m.Id.Entry))),
                    CanStealCards = model.AllPossibleMonsters.Any(m => m.Id.Entry == "THIEVING_HOPPER"),
                    IsMonsterEncounter = model.RoomType == MegaCrit.Sts2.Core.Rooms.RoomType.Monster };
            }).ToImmutableArray();
        var potions = context.PotionPool.Select(p => p with
        { CanBeGeneratedInCombat = ModelDb.GetById<PotionModel>(new ModelId(p.Key.Category, p.Key.Entry)).CanBeGeneratedInCombat }).ToImmutableArray();
        var unlocks = new UnlockState(context.RevealedEpochs, context.SeenEncounters.Select(k => new ModelId(k.Category, k.Entry)), context.NumberOfRuns);
        var capturedCharacterCards = effects.CharacterRewardPool!.Where(c => c.IsUnlockedInCapturedPool && !c.IsMultiplayerOnly &&
            c.Rarity is RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common or RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon or RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Rare)
            .Select(c => c.CardKey);
        var declaredCharacterCards = character.CardPool.GetUnlockedCards(unlocks, CardMultiplayerConstraint.SingleplayerOnly)
            .Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).Select(c => new ModelKey(c.Id.Category, c.Id.Entry));
        if (!capturedCharacterCards.SequenceEqual(declaredCharacterCards)) throw new InvalidOperationException("PredictorDeclaredUnlockCatalogMismatch");
        // Freeze the concrete source pool and eligibility, not a Family result pool.
        // No mutable Player or CardFactory RNG is needed to capture these values.
        var definitions = ModelDb.AllCards.Select(c =>
        {
            var pool = c.Type == CardType.Quest || c.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token
                ? ModelDb.CardPool<ColorlessCardPool>() : c.Pool;
            var type = Enum.TryParse<RolltheSpire2.Core.Effects.Snapshots.EffectCardType>(c.Type.ToString(), out var parsed)
                ? parsed : RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Other;
            return new PredictorCardDefinition(new PredictorCard(0, new(c.Id.Category, c.Id.Entry), type, 0, c.MaxUpgradeLevel, c.IsRemovable, 1)
                { SpoilsActIndex = c.Id.Entry == "SPOILS_MAP" ? 1 : -1,
                    HasExhaustKeyword = c.Keywords.Contains(CardKeyword.Exhaust), HasEternalKeyword = c.Keywords.Contains(CardKeyword.Eternal),
                    HasInnateKeyword = c.Keywords.Contains(CardKeyword.Innate), HasRetainKeyword = c.Keywords.Contains(CardKeyword.Retain) },
                c.Rarity.ToString(), new(pool.Id.Category, pool.Id.Entry), c.Type == CardType.Quest,
                c.Tags.Contains(CardTag.Strike), c.Tags.Contains(CardTag.Defend))
                { Unplayable = c.Keywords.Contains(CardKeyword.Unplayable), CostsX = c.EnergyCost.CostsX,
                    HasLocalExhaust = c.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Exhaust), CanonicalCost = c.EnergyCost.Canonical,
                    Levels = CaptureLevels(c) };
        }).ToImmutableArray();
        var transformPools = definitions.Select(d => d.TransformationPool).Distinct().Select(k => new PredictorCardPool(k,
            ModelDb.GetById<CardPoolModel>(new(k.Category, k.Entry)).GetUnlockedCards(unlocks, CardMultiplayerConstraint.SingleplayerOnly)
                .Select(c => new ModelKey(c.Id.Category, c.Id.Entry)).ToImmutableArray())).ToImmutableArray();
        var modifierCurses = ModelDb.CardPool<CurseCardPool>().GetUnlockedCards(unlocks, CardMultiplayerConstraint.SingleplayerOnly)
            .Where(c => c.CanBeGeneratedByModifiers).Select(c => new ModelKey(c.Id.Category, c.Id.Entry)).ToImmutableArray();
        var curses = modifierCurses.OrderBy(c => c.Entry, StringComparer.Ordinal).ToImmutableArray();
        var shopRelics = ModelDb.AllRelics.Select(r => new PredictorShopRelicOption(new(r.Id.Category, r.Id.Entry), r.Rarity.ToString(),
            r.MerchantCost, r.IsAllowedInShops)).ToImmutableArray();
        return context with { AccountWongoPoints = context.AccountWongoPoints ?? MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.WongoPoints,
            PotionPool = potions, OpeningEligibleRelics = effects.BonesEligibleRelics?.ToImmutableArray() ?? [], Catalog = context.Catalog! with
        { Encounters = encounters, AllCharacterCards = allCards, CardDefinitions = definitions, TransformationPools = transformPools,
            UnlockedCurseCards = curses, ModifierCurseCards = modifierCurses, ShopRelics = shopRelics, CharacterPools = characterPools,
            CharacterStrikeKey = effects.CharacterStrikeKey, CharacterDefendKey = effects.CharacterDefendKey,
            TradableRelicTypes = ModelDb.AllRelics.Where(r => !r.HasUponPickupEffect && !r.SpawnsPets &&
                r.Rarity is not (MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Starter or MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Event or MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Ancient))
                .Select(r => new ModelKey(r.Id.Category, r.Id.Entry)).ToImmutableArray(),
            EventPetRelics = ModelDb.AllRelics.Where(r => r.AddsPet).Select(r => new ModelKey(r.Id.Category, r.Id.Entry)).ToImmutableArray(),
            StarterRelics = ModelDb.AllRelics.Where(r => r.Rarity == MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Starter)
                .Select(r => new ModelKey(r.Id.Category, r.Id.Entry)).ToImmutableArray(),
            DustyTomeCards = character.CardPool.GetUnlockedCards(unlocks, MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.SingleplayerOnly)
                .Where(c => c.Rarity == MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Ancient &&
                    !MegaCrit.Sts2.Core.Models.Relics.ArchaicTooth.TranscendenceCards.Contains(c))
                .Select(c => new ModelKey(c.Id.Category, c.Id.Entry)).ToImmutableArray(),
            ShopGainsBlock = ModelDb.AllCards.Where(c => c.Id.Entry is not ("SOVEREIGN_BLADE" or "MAD_SCIENCE") && c.GainsBlock)
                .Select(c => new ModelKey(c.Id.Category, c.Id.Entry)).ToImmutableArray() } };
    }
}
