using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

// Crystal needs the actual player's reward pools, not opening/route authority.
// Unknown mod hooks are deliberately not invoked by detached prediction.
internal static class CrystalSphereCatalogCapture
{
    private static ModelKey Key(AbstractModel model) => new(model.Id.Category, model.Id.Entry);
    private static bool Modded(AbstractModel model) => model.GetType().Assembly != typeof(CardModel).Assembly;

    // Owned by one synchronous capture. A pool is copied in its actual native
    // order once for this player's unlocks and multiplayer constraint; nothing
    // here is retained by a snapshot or shared with a later capture/worker.
    internal sealed class CapturePools(IRunState run, Player player)
    {
        private readonly CardMultiplayerConstraint _constraint = run.CardMultiplayerConstraint;
        private readonly Dictionary<CardPoolModel, ImmutableArray<CardModel>> _cards = new(ReferenceEqualityComparer.Instance);
        internal int Reads { get; private set; }
        internal int Reuses { get; private set; }
        internal int ProjectionReuses { get; set; }

        internal ImmutableArray<CardModel> Read(CardPoolModel pool)
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (_cards.TryGetValue(pool, out var cards)) { Reuses++; return cards; }
            cards = pool.GetUnlockedCards(player.UnlockState, _constraint).ToImmutableArray();
            _cards.Add(pool, cards); Reads++; return cards;
        }
    }

    internal sealed record CardInputs(CardPoolModel Own, CardPoolModel Colorless,
        CardPoolModel[] Unlocked, (ModelKey Character, CardPoolModel Pool)[] Characters, CardModel[] Definitions);

    internal static CardInputs ReadCardInputs(Player player, CapturePools pools)
    {
        var own = player.Character.CardPool;
        var colorless = ModelDb.CardPool<ColorlessCardPool>();
        var unlocked = player.UnlockState.CharacterCardPools.ToArray();
        var characters = ModelDb.AllCharacters.Append(player.Character).DistinctBy(c => c.Id)
            .Where(c => unlocked.Contains(c.CardPool) || c.Id == player.Character.Id)
            .Select(c => (Character: Key(c), Pool: c.CardPool)).ToArray();
        foreach (var character in characters) pools.Read(character.Pool);
        var poolCards = pools.Read(own).Concat(pools.Read(colorless)).Concat(unlocked.SelectMany(p => pools.Read(p))).ToArray();
        var definitions = ModelDb.AllCards.Concat(poolCards
                .Select(c => ModelDb.GetById<CardModel>(c.Id)))
            .Concat(player.Deck.Cards.Select(c => ModelDb.GetById<CardModel>(c.Id)))
            .DistinctBy(c => c.Id).ToArray();
        return new(own, colorless, unlocked, characters, definitions);
    }

    internal static (PredictorCatalog Catalog, ImmutableArray<ModelKey> Unmodeled) Capture(IRunState run, Player player,
        CapturePools? pools = null, Func<CardModel, ImmutableArray<PredictorCardLevelMetadata>>? captureLevels = null,
        CardInputs? inputs = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        pools ??= new(run, player);
        var unmodeled = new HashSet<ModelKey>();
        if (Modded(player.Character)) unmodeled.Add(Key(player.Character));
        foreach (var relic in player.Relics)
        {
            try { PredictorSettlementEffects.RequireImplementedRelic(Key(relic)); }
            catch (NotImplementedException) { unmodeled.Add(Key(relic)); }
            if (Modded(relic)) unmodeled.Add(Key(relic));
        }
        foreach (var modifier in run.Modifiers)
            if (Modded(modifier) || !PredictorSeedState.KnownModifier(modifier.Id.Entry)) unmodeled.Add(Key(modifier));
        foreach (var card in player.Deck.Cards)
            if (card.Enchantment is { } enchantment && Modded(enchantment)) unmodeled.Add(Key(enchantment));

        var projected = new Dictionary<CardPoolModel, ImmutableArray<NeowEffectCardSnapshot>>(ReferenceEqualityComparer.Instance);
        ImmutableArray<NeowEffectCardSnapshot> Pool(CardPoolModel pool)
        {
            if (projected.TryGetValue(pool, out var cards)) { pools.ProjectionReuses++; return cards; }
            cards = pools.Read(pool)
            .Select((c, index) => new NeowEffectCardSnapshot(Key(c).Serialized, Key(c), index,
                Enum.TryParse<EffectCardRarity>(c.Rarity.ToString(), out var rarity) ? rarity : EffectCardRarity.Special,
                Enum.TryParse<EffectCardType>(c.Type.ToString(), out var type) ? type : EffectCardType.Other,
                c.Rarity == CardRarity.Basic, c.Tags.Contains(CardTag.Strike), c.Tags.Contains(CardTag.Defend),
                c.IsUpgradable, c.IsRemovable, c.CurrentUpgradeLevel, c.MaxUpgradeLevel, PoolId: pool.Id.ToString(),
                SourceAssembly: c.GetType().Assembly.GetName().Name,
                CanBeGeneratedByModifiers: c.CanBeGeneratedByModifiers,
                IsMultiplayerOnly: c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly,
                CanBeGeneratedInCombat: c.CanBeGeneratedInCombat,
                EligibleForPostCombatRewardByPoolMembership: c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare,
                BehaviorMetadataExact: !Modded(c))).ToImmutableArray();
            projected.Add(pool, cards); return cards;
        }
        var unlocked = inputs?.Unlocked ?? player.UnlockState.CharacterCardPools.ToArray();
        var characters = inputs?.Characters ?? ModelDb.AllCharacters.Append(player.Character).DistinctBy(c => c.Id)
            .Where(c => unlocked.Contains(c.CardPool) || c.Id == player.Character.Id)
            .Select(c => (Character: Key(c), Pool: c.CardPool)).ToArray();
        var characterPools = characters.Select(c => (c.Character, Cards: Pool(c.Pool), PoolKey: Key(c.Pool))).ToArray();
        var own = Pool(inputs?.Own ?? player.Character.CardPool);
        var colorless = Pool(inputs?.Colorless ?? ModelDb.CardPool<ColorlessCardPool>());
        var all = unlocked.SelectMany(p => Pool(p)).ToImmutableArray();
        var cardModels = inputs?.Definitions ?? ModelDb.AllCards.Concat(own.Concat(colorless).Concat(all)
                .Select(c => ModelDb.GetById<CardModel>(new(c.CardKey.Category, c.CardKey.Entry))))
            .Concat(player.Deck.Cards.Select(c => ModelDb.GetById<CardModel>(c.Id)))
            .DistinctBy(c => c.Id).ToArray();
        var definitions = cardModels.Select(c =>
        {
            bool modded = Modded(c);
            if (modded) unmodeled.Add(Key(c));
            return CaptureCardDefinition(c, captureLevels ?? PredictorSeedCapture.CaptureLevels);
        }).ToImmutableArray();
        var catalog = new PredictorCatalog(own, colorless)
        {
            AllCharacterCards = all, CardDefinitions = definitions,
            CharacterPools = characterPools.Select(p => new PredictorCharacterPool(p.Character, p.Cards.Select(c => c.CardKey).ToImmutableArray(), p.PoolKey)).ToImmutableArray(),
            ShopGainsBlock = cardModels.Where(c => c.Id.Entry is not ("SOVEREIGN_BLADE" or "MAD_SCIENCE") && c.GainsBlock).Select(Key).ToImmutableArray()
        };
        return (catalog, unmodeled.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToImmutableArray());
    }

    internal static PredictorCardDefinition CaptureCardDefinition(CardModel card,
        Func<CardModel, ImmutableArray<PredictorCardLevelMetadata>> captureLevels)
    {
        var type = Enum.TryParse<EffectCardType>(card.Type.ToString(), out var parsed) ? parsed : EffectCardType.Other;
        var prototype = new PredictorCard(0, Key(card), type, card.CurrentUpgradeLevel, card.MaxUpgradeLevel, card.IsRemovable, 1);
        // Custom upgrade implementations can require a live Owner.
        var levels = Modded(card)
            ? Enumerable.Range(0, card.MaxUpgradeLevel + 1).Select(level => new PredictorCardLevelMetadata(level,
                card.EnergyCost.Canonical, card.EnergyCost.CostsX, card.Keywords.Contains(CardKeyword.Unplayable),
                card.Keywords.Contains(CardKeyword.Exhaust), card.Keywords.Contains(CardKeyword.Innate),
                card.Keywords.Contains(CardKeyword.Retain), card.Keywords.Contains(CardKeyword.Eternal))).ToImmutableArray()
            : captureLevels(card);
        var pool = card.Type == CardType.Quest || card.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token
            ? ModelDb.CardPool<ColorlessCardPool>() : card.Pool;
        return new(prototype, card.Rarity.ToString(), Key(pool), card.Type == CardType.Quest,
            card.Tags.Contains(CardTag.Strike), card.Tags.Contains(CardTag.Defend))
        {
            Unplayable = card.Keywords.Contains(CardKeyword.Unplayable), CostsX = card.EnergyCost.CostsX,
            HasLocalExhaust = card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Exhaust),
            CanonicalCost = card.EnergyCost.Canonical, Levels = levels
        };
    }
}
