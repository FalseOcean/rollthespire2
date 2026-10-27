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

    internal static (PredictorCatalog Catalog, ImmutableArray<ModelKey> Unmodeled) Capture(IRunState run, Player player,
        CapturePools? pools = null, Func<CardModel, ImmutableArray<PredictorCardLevelMetadata>>? captureLevels = null)
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
        var unlocked = player.UnlockState.CharacterCardPools.ToArray();
        var characters = ModelDb.AllCharacters.Append(player.Character).DistinctBy(c => c.Id)
            .Where(c => unlocked.Contains(c.CardPool) || c.Id == player.Character.Id).ToArray();
        var characterPools = characters.Select(c => (Character: Key(c), Cards: Pool(c.CardPool))).ToArray();
        var own = Pool(player.Character.CardPool);
        var colorless = Pool(ModelDb.CardPool<ColorlessCardPool>());
        var all = unlocked.SelectMany(p => Pool(p)).ToImmutableArray();
        var cardModels = ModelDb.AllCards.Concat(own.Concat(colorless).Concat(all).Select(c=>ModelDb.GetById<CardModel>(new(c.CardKey.Category,c.CardKey.Entry))))
            .Concat(player.Deck.Cards.Select(c => ModelDb.GetById<CardModel>(c.Id)))
            .DistinctBy(c => c.Id).ToArray();
        var definitions = cardModels.Select(c =>
        {
            bool modded = Modded(c);
            if (modded) unmodeled.Add(Key(c));
            var type = Enum.TryParse<EffectCardType>(c.Type.ToString(), out var t) ? t : EffectCardType.Other;
            var prototype = new PredictorCard(0, Key(c), type, c.CurrentUpgradeLevel, c.MaxUpgradeLevel, c.IsRemovable, 1);
            // Mod upgrade implementations may require a live Owner. Preserve
            // readable base metadata without calling arbitrary lifecycle code.
            var levels = modded
                ? Enumerable.Range(0, c.MaxUpgradeLevel + 1).Select(level => new PredictorCardLevelMetadata(level,
                    c.EnergyCost.Canonical, c.EnergyCost.CostsX, c.Keywords.Contains(CardKeyword.Unplayable),
                    c.Keywords.Contains(CardKeyword.Exhaust), c.Keywords.Contains(CardKeyword.Innate),
                    c.Keywords.Contains(CardKeyword.Retain), c.Keywords.Contains(CardKeyword.Eternal))).ToImmutableArray()
                : (captureLevels ?? PredictorSeedCapture.CaptureLevels)(c);
            var pool = c.Type == CardType.Quest || c.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token
                ? ModelDb.CardPool<ColorlessCardPool>() : c.Pool;
            return new PredictorCardDefinition(prototype, c.Rarity.ToString(), Key(pool),
                c.Type == CardType.Quest, c.Tags.Contains(CardTag.Strike), c.Tags.Contains(CardTag.Defend))
            {
                Unplayable = c.Keywords.Contains(CardKeyword.Unplayable), CostsX = c.EnergyCost.CostsX,
                HasLocalExhaust = c.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Exhaust),
                CanonicalCost = c.EnergyCost.Canonical, Levels = levels
            };
        }).ToImmutableArray();
        var catalog = new PredictorCatalog(own, colorless)
        {
            AllCharacterCards = all, CardDefinitions = definitions,
            CharacterPools = characterPools.Select(p => new PredictorCharacterPool(p.Character, p.Cards.Select(c => c.CardKey).ToImmutableArray())).ToImmutableArray(),
            ShopGainsBlock = cardModels.Where(c => c.Id.Entry is not ("SOVEREIGN_BLADE" or "MAD_SCIENCE") && c.GainsBlock).Select(Key).ToImmutableArray()
        };
        return (catalog, unmodeled.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToImmutableArray());
    }
}
