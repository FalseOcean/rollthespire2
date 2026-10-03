using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorSeedCapture
{
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
        return context with { AccountWongoPoints = context.AccountWongoPoints ?? MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.WongoPoints,
            PotionPool = potions, OpeningEligibleRelics = effects.BonesEligibleRelics?.ToImmutableArray() ?? [], Catalog = context.Catalog! with
        { AllCharacterCards = allCards, CardDefinitions = definitions, TransformationPools = transformPools,
            UnlockedCurseCards = curses, ModifierCurseCards = modifierCurses, CharacterPools = characterPools,
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
