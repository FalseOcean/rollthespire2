using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorShopEntryKind { CharacterCard, ColorlessCard, Relic, Potion, CardRemoval }
internal sealed record PredictorShopRelicOption(ModelKey Key, string Rarity, int MerchantCost, bool IsAllowedInShops);
internal sealed record PredictorShopEntry(long Id, PredictorShopEntryKind Kind, ModelKey? Key,
    PredictorCard? Card, int BaseCost, bool IsOnSale, bool Stocked, int Revision)
{
    internal PredictorOperationShopRefill? OperationRefill { get; init; }
}
internal sealed record PredictorOperationShopRefill(ImmutableArray<NeowEffectCardSnapshot> Pool,
    EffectCardType? Type, EffectCardRarity? Rarity);
internal sealed record PredictorShopOffer(long EntryId, PredictorShopEntryKind Kind, ModelKey? Key,
    PredictorCard? Card, int Cost, bool IsOnSale, bool Stocked, bool Affordable, bool Selectable);
internal sealed record BuyShopEntry(long EntryId, int InventoryRevision) : PredictorInput;
internal sealed record BeginShopCardRemoval(long EntryId, int InventoryRevision) : PredictorInput;
internal sealed record ChooseShopRemovalCard(long CardId) : PredictorInput;
internal sealed record CancelShopCardRemoval : PredictorInput;
internal sealed record OpenShopInventory : PredictorInput;
internal sealed record CloseShopInventory : PredictorInput;
internal sealed record LeaveShop : PredictorInput;

// The live inventory belongs to one open MerchantRoom. Its RNG and bag draws
// have already been committed to Working when the room is entered.
internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorShopEntry> _shopEntries = [];
    private long? _shopRemovalEntry;
    private int _shopRemovalQuotedCost;
    private int _shopRevision;
    private long? _shopPendingRelicEntry;
    private int _shopPendingGoldBefore;
    private bool _shopPendingRelicIgnoreCost;
    private bool _parasolActive;
    private int _parasolCursor;
    private bool _advancingParasol;
    private bool _shopInventoryOpen;

    internal ImmutableArray<PredictorShopEntry> ShopEntries => _shopEntries;
    internal bool ShopInventoryOpen => _shopInventoryOpen;
    internal bool CanUseFoulPotionInShop => Phase == PredictorPhase.Shop && !_shopInventoryOpen &&
        !_parasolActive && _shopRemovalEntry == null;
    internal void ApplyFoulPotionInShop(long potionId)
    {
        if (!CanUseFoulPotionInShop) throw new InvalidOperationException("PredictorFoulPotionShopTargetUnavailable");
        int before = Working.GoldBudget;
        Working = PredictorSettlementEffects.TakeGold(Working, 100);
        Accept(new("FoulPotionShopGold", potionId, [], before, Working.GoldBudget));
    }
    internal ImmutableArray<PredictorShopOffer> ShopOffers => _shopEntries.Select(e =>
    {
        int cost = ShopCost(e);
        bool selectable = Phase == PredictorPhase.Shop && _shopInventoryOpen && !_parasolActive &&
            _shopRemovalEntry == null && e.Stocked &&
            Working.GoldBudget >= cost && (e.Kind != PredictorShopEntryKind.CardRemoval ||
                Working.Deck.Any(id => Working.Card(id).Removable));
        return new PredictorShopOffer(e.Id, e.Kind, e.Key, e.Card, cost, e.IsOnSale,
            e.Stocked, Working.GoldBudget >= cost, selectable);
    }).ToImmutableArray();
    internal int ShopInventoryRevision => _shopRevision;
    internal bool ShopParasolActive => _parasolActive;
    internal int ShopParasolCursor => _parasolCursor;
    internal ImmutableArray<long> ShopRemovalCandidates => _shopRemovalEntry == null ? [] :
        Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();

    internal void ResetShop()
    {
        _shopEntries = [];
        _shopRemovalEntry = null;
        _shopRemovalQuotedCost = 0;
        _shopRevision = 0;
        _shopPendingRelicEntry = null;
        _shopPendingGoldBefore = 0;
        _shopPendingRelicIgnoreCost = false;
        _parasolActive = false;
        _parasolCursor = 0;
        _advancingParasol = false;
        _shopInventoryOpen = false;
    }

    internal void EnterShop(PredictorPosition position)
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete || !Working.OpeningCompleted)
            throw new InvalidOperationException("PredictorShopEntryInvalid");
        if (Context.Catalog?.ShopRelics.IsDefaultOrEmpty != false)
            throw new InvalidOperationException("PredictorShopRelicCatalogMissing");
        var state = Working with { Position = position };
        var entries = ImmutableArray.CreateBuilder<PredictorShopEntry>();
        (int sale, state) = state.Draw(PredictorStream.Shops, 0, 5);
        for (int i = 0; i < 5; i++)
        {
            var type = i switch { 0 or 1 => EffectCardType.Attack, 2 or 3 => EffectCardType.Skill, _ => EffectCardType.Power };
            (var card, state) = ShopCard(state, Context.Catalog.CharacterCards, entries, type, null);
            (int cost, state) = PriceCard(state, card, false, i == sale);
            entries.Add(new(state.NextInstanceId, PredictorShopEntryKind.CharacterCard, card.Key, card, cost, i == sale, true, 0));
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        foreach (var rarity in new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare })
        {
            (var card, state) = ShopCard(state, Context.Catalog.ColorlessCards, entries, null, rarity);
            (int cost, state) = PriceCard(state, card, true, false);
            entries.Add(new(state.NextInstanceId, PredictorShopEntryKind.ColorlessCard, card.Key, card, cost, false, true, 0));
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        string[] rarities = new string[3];
        for (int i = 0; i < 2; i++) (rarities[i], state) = RollRelicRarity(state);
        rarities[2] = "Shop";
        foreach (string rarity in rarities)
        {
            (var key, state) = ShopRelic(state, rarity, []);
            (int cost, state) = PriceRelic(state, key);
            entries.Add(new(state.NextInstanceId, PredictorShopEntryKind.Relic, key, null, cost, false, true, 0));
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        var potions = PredictorRewardGeneration.GeneratePotions(Context, ref state, 3, PredictorStream.Shops);
        foreach (var potion in potions)
        {
            (int cost, state) = PricePotion(state, potion);
            entries.Add(new(state.NextInstanceId, PredictorShopEntryKind.Potion, potion, null, cost, false, true, 0));
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        int removeCost = RemovalBaseCost + RemovalIncrease * state.CardShopRemovalsUsed;
        entries.Add(new(state.NextInstanceId, PredictorShopEntryKind.CardRemoval, null, null, removeCost, false,
            !Context.HasRunModifier("HOARDER"), 0));
        state = state with { NextInstanceId = state.NextInstanceId + 1 };
        state.Validate();
        Working = state;
        ResetShop();
        _shopEntries = entries.ToImmutable();
        _inputs = [];
        Rewards = [];
        Phase = PredictorPhase.Shop;
        _requestId++;
    }

    private int RemovalBaseCost => Context.Ascension >= 6 ? 100 : 75;
    private int RemovalIncrease => Context.Ascension >= 6 ? 50 : 25;

    // Called by the room-entry hook traversal after its synchronous listeners
    // have returned. Vanilla starts this task, yields for a UI frame, then
    // purchases entries in inventory order; the caller owns that placement.
    internal void BeginShopParasol(CancellationToken token = default)
    {
        if (Phase != PredictorPhase.Shop || _parasolActive ||
            !PredictorSettlementEffects.Has(Working, "LORDS_PARASOL"))
            throw new InvalidOperationException("PredictorShopParasolEntryInvalid");
        _parasolActive = true;
        _parasolCursor = 0;
        ContinueShopParasol(token);
    }

    private void ContinueShopParasol(CancellationToken token)
    {
        if (!_parasolActive || _advancingParasol) return;
        _advancingParasol = true;
        try
        {
            while (_parasolCursor < _shopEntries.Length - 1)
            {
                token.ThrowIfCancellationRequested();
                int slot = _parasolCursor;
                if (_preview.ShopAutomaticPurchaseAllowed?.Invoke(slot) == false) return;
                _parasolCursor++;
                if (!_shopEntries[slot].Stocked) continue;
                var result = BuyShop(slot, ShopCost(_shopEntries[slot]), token, ignoreCost: true);
                if (Phase != PredictorPhase.Shop || result == PredictorInputResult.Invalid)
                    return; // A relic's nested obtain owns the next choice.
                // Vanilla continues after failed potion procurement too.
            }
            var removal = _shopEntries[^1];
            if (_preview.ShopAutomaticPurchaseAllowed?.Invoke(_shopEntries.Length - 1) == false) return;
            if (!removal.Stocked)
            {
                _parasolActive = false;
                return;
            }
            if (Working.Deck.All(id => !Working.Card(id).Removable))
            {
                // FromDeckForRemoval returns an empty selection before opening
                // a picker. The merchant removal wrapper then returns false;
                // the entry remains stocked and Parasol's task ends.
                _parasolActive = false;
                return;
            }
            _shopRemovalEntry = removal.Id;
            _shopRemovalQuotedCost = 0;
            Accept(new("ParasolBeginRemoval", removal.Id, [], Working.GoldBudget, null));
        }
        finally { _advancingParasol = false; }
    }

    internal PredictorInputResult TrySubmitShop(PredictorInput input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Phase != PredictorPhase.Shop) return PredictorInputResult.Invalid;
        if (input is OpenShopInventory && !_shopInventoryOpen && !_parasolActive && _shopRemovalEntry == null)
        {
            _shopInventoryOpen = true;
            Accept(new("OpenShopInventory", 0, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is CloseShopInventory && _shopInventoryOpen && !_parasolActive && _shopRemovalEntry == null)
        {
            _shopInventoryOpen = false;
            Accept(new("CloseShopInventory", 0, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (_shopRemovalEntry != null)
        {
            if (input is CancelShopCardRemoval && !_parasolActive)
            {
                _shopRemovalEntry = null;
                Accept(new("CancelShopCardRemoval", 0, [], null, null));
                return PredictorInputResult.Accepted;
            }
            if (input is not ChooseShopRemovalCard choose || !ShopRemovalCandidates.Contains(choose.CardId))
                return PredictorInputResult.Invalid;
            int before = Working.GoldBudget;
            if (before < _shopRemovalQuotedCost && Context.Operation == null) return PredictorInputResult.Invalid;
            Working = Working with { GoldBudget = Math.Max(0, before - _shopRemovalQuotedCost) };
            ApplySpoilsMapBeforeRemoved(choose.CardId);
            RemoveCards([choose.CardId]);
            Working = Working with { CardShopRemovalsUsed = Working.CardShopRemovalsUsed + 1 };
            int index = _shopEntries.FindIndex(e => e.Id == _shopRemovalEntry.Value);
            _shopEntries = _shopEntries.SetItem(index, _shopEntries[index] with { Stocked = false, Revision = ++_shopRevision });
            _shopRemovalEntry = null;
            ShopAfterPurchase(_shopRemovalQuotedCost);
            RefreshShopCards();
            Accept(new(_parasolActive ? "ParasolChooseRemovalCard" : "ChooseShopRemovalCard",
                choose.CardId, [], before, Working.GoldBudget));
            if (_parasolActive) _parasolActive = false;
            return PredictorInputResult.Accepted;
        }
        if (_parasolActive) return PredictorInputResult.Invalid;
        if (input is LeaveShop)
        {
            if (_shopInventoryOpen) return PredictorInputResult.NotAllowed;
            Accept(new("LeaveShop", 0, [], null, null));
            ResetShop();
            CompleteNode();
            return PredictorInputResult.Accepted;
        }
        if (!_shopInventoryOpen) return PredictorInputResult.NotAllowed;
        long id; int revision;
        if (input is BeginShopCardRemoval begin) { id = begin.EntryId; revision = begin.InventoryRevision; }
        else if (input is BuyShopEntry buy) { id = buy.EntryId; revision = buy.InventoryRevision; }
        else return PredictorInputResult.Invalid;
        if (revision != _shopRevision) return PredictorInputResult.Stale;
        int slot = _shopEntries.FindIndex(e => e.Id == id);
        if (slot < 0 || !_shopEntries[slot].Stocked) return PredictorInputResult.Invalid;
        var entry = _shopEntries[slot];
        int price = ShopCost(entry);
        if (Working.GoldBudget < price) return PredictorInputResult.Invalid;
        if (entry.Kind == PredictorShopEntryKind.CardRemoval)
        {
            if (input is not BeginShopCardRemoval || Working.Deck.All(cardId => !Working.Card(cardId).Removable)) return PredictorInputResult.Invalid;
            _shopRemovalEntry = id;
            _shopRemovalQuotedCost = price;
            Accept(new("BeginShopCardRemoval", id, [], Working.GoldBudget, null));
            return PredictorInputResult.Accepted;
        }
        if (input is not BuyShopEntry) return PredictorInputResult.Invalid;
        return BuyShop(slot, price, token, ignoreCost: false);
    }

    private PredictorInputResult BuyShop(int slot, int quotedPrice, CancellationToken token, bool ignoreCost)
    {
        var entry = _shopEntries[slot];
        int before = Working.GoldBudget;
        switch (entry.Kind)
        {
            case PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard:
                Working = AcquireShopCard(Working, entry.Card!);
                if (!ignoreCost) Working = Working with { GoldBudget = Working.GoldBudget - ShopCost(entry) };
                break;
            case PredictorShopEntryKind.Relic:
                if (!ignoreCost) Working = Working with { GoldBudget = Working.GoldBudget - quotedPrice };
                _shopPendingRelicEntry = entry.Id;
                _shopPendingGoldBefore = before;
                _shopPendingRelicIgnoreCost = ignoreCost;
                if (!BeginObtainRelic(entry.Key!.Value, null, PredictorObtainReturn.Shop, entry.Id, token))
                {
                    Accept(new(ignoreCost ? "ParasolBeginRelicObtain" : "BeginShopRelicObtain", entry.Id,
                        [], before, Working.GoldBudget));
                    return PredictorInputResult.Accepted;
                }
                return ResumeShopAfterRelicObtain(entry.Id);
            case PredictorShopEntryKind.Potion:
                if (PredictorSettlementEffects.Has(Working, "SOZU")) return PredictorInputResult.NotAllowed;
                int empty = Working.PotionSlots.FindIndex(p => p == null);
                if (empty < 0) return PredictorInputResult.TooFull;
                long instanceId = Working.NextInstanceId;
                Working = Working with { NextInstanceId = instanceId + 1,
                    PotionSlots = Working.PotionSlots.SetItem(empty, new(instanceId, entry.Key!.Value)) };
                if (!ignoreCost) Working = Working with { GoldBudget = Working.GoldBudget - ShopCost(entry) };
                break;
            default: return PredictorInputResult.Invalid;
        }
        FinishShopPurchase(slot, before, entry.Id, ignoreCost);
        return PredictorInputResult.Accepted;
    }

    internal PredictorInputResult ResumeShopAfterRelicObtain(long entryId)
    {
        if (_shopPendingRelicEntry != entryId) throw new InvalidOperationException("PredictorShopRelicContinuationInvalid");
        int slot = _shopEntries.FindIndex(e => e.Id == entryId);
        if (slot < 0) throw new InvalidOperationException("PredictorShopRelicEntryMissing");
        int before = _shopPendingGoldBefore;
        bool ignoreCost = _shopPendingRelicIgnoreCost;
        _shopPendingRelicEntry = null;
        _shopPendingRelicIgnoreCost = false;
        // Direct obtain pickers return through Rewards. The suspended merchant
        // transaction owns the return phase, including Parasol's next purchase.
        Phase = PredictorPhase.Shop;
        FinishShopPurchase(slot, before, entryId, ignoreCost);
        if (_parasolActive) ContinueShopParasol(default);
        return PredictorInputResult.Accepted;
    }

    private void FinishShopPurchase(int slot, int before, long entryId, bool ignoreCost)
    {
        var entry = _shopEntries[slot];
        // Vanilla returns a fresh Cost read after acquisition. For a newly
        // obtained discount relic this may differ from the amount just paid.
        int goldSpent = ignoreCost ? 0 : ShopCost(entry);
        bool restock = OperationShopPriceHooksApply && PredictorSettlementEffects.Has(Working, "THE_COURIER");
        if (restock) RestockShop(slot);
        else _shopEntries = _shopEntries.SetItem(slot, entry with { Stocked = false, Card = null, Key = null, Revision = _shopRevision + 1 });
        _shopRevision++;
        ShopAfterPurchase(goldSpent);
        // Each purchased entry notifies the inventory. Existing card results are
        // modified again, but their canonical identity and base price stay put.
        RefreshShopCards();
        Working.Validate();
        Accept(new(ignoreCost ? "ParasolBuyShopEntry" : "BuyShopEntry", entryId, [], before, Working.GoldBudget));
    }

    private PredictorState AcquireShopCard(PredictorState state, PredictorCard prototype)
    {
        var card = prototype;
        // CardPileCmd.Add runs TryModifyCardBeingAddedToDeck after the
        // merchant creation-result pass. An Egg may upgrade the same card
        // again at this distinct hook site.
        foreach (var relic in state.Relics.Where(r => !r.Melted))
        {
            card = PredictorRewardGeneration.UpgradeOnDeckAddWithEgg(card, relic.Key.Entry);
            if (relic.Key.Entry == "FRESNEL_LENS" && card.Enchantment == null &&
                card.Type is EffectCardType.Attack or EffectCardType.Skill or EffectCardType.Power &&
                ShopGainsBlock(card))
                card = card with { Enchantment = new("ENCHANTMENT", "NIMBLE"), EnchantmentAmount = 2 };
        }
        card = card with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
        return state with { NextInstanceId = state.NextInstanceId + 1,
            Cards = state.Cards.Add(card), Deck = state.Deck.Add(card.Id) };
    }

    private void ShopAfterPurchase(int spent)
    {
        if (spent > 0)
        {
            int maw = Working.Relics.FindIndex(r => !r.Melted && !r.MawBankSpent && r.Key.Entry == "MAW_BANK");
            if (maw >= 0) Working = Working with { Relics = Working.Relics.SetItem(maw, Working.Relics[maw] with { MawBankSpent = true }) };
        }
    }

    private int ShopCost(PredictorShopEntry entry)
    {
        if (!OperationShopPriceHooksApply) return entry.BaseCost;
        decimal price = entry.BaseCost;
        if (PredictorSettlementEffects.Has(Working, "MEMBERSHIP_CARD")) price *= .5m;
        if (PredictorSettlementEffects.Has(Working, "THE_COURIER")) price *= .8m;
        return (int)price;
    }

    private void RestockShop(int slot)
    {
        var old = _shopEntries[slot];
        var state = Working;
        PredictorShopEntry replacement;
        switch (old.Kind)
        {
            case PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard:
                var pool = old.Kind == PredictorShopEntryKind.CharacterCard ? Context.Catalog!.CharacterCards : Context.Catalog!.ColorlessCards;
                EffectCardType? type = old.Kind == PredictorShopEntryKind.CharacterCard ? old.Card!.Type : null;
                EffectCardRarity? rarity = old.Kind == PredictorShopEntryKind.ColorlessCard ?
                    Context.Catalog!.ColorlessCards.Single(c => c.CardKey == old.Key).Rarity : null;
                if (Context.Operation != null && old.OperationRefill is { } actual)
                {
                    pool = actual.Pool;
                    type = actual.Type;
                    rarity = actual.Rarity;
                }
                (var card, state) = ShopCard(state, pool, _shopEntries, type, rarity);
                (int cardPrice, state) = PriceCard(state, card, old.Kind == PredictorShopEntryKind.ColorlessCard, false,
                    Context.Operation != null && old.OperationRefill != null ? pool.FirstOrDefault(c => c.CardKey == card.Key)?.Rarity : null);
                replacement = old with { Key = card.Key, Card = card, BaseCost = cardPrice, IsOnSale = false, Revision = _shopRevision + 1 };
                break;
            case PredictorShopEntryKind.Relic:
                (string relicRarity, state) = RollRelicRarity(state);
                var blacklist = _shopEntries.Where(e => e.Kind == PredictorShopEntryKind.Relic && e.Key != null).Select(e => e.Key!.Value).ToImmutableHashSet();
                (var key, state) = ShopRelic(state, relicRarity, blacklist);
                (int relicPrice, state) = PriceRelic(state, key);
                replacement = old with { Key = key, BaseCost = relicPrice, Revision = _shopRevision + 1 };
                break;
            case PredictorShopEntryKind.Potion:
                var potion = PredictorRewardGeneration.GeneratePotions(Context, ref state, 1, PredictorStream.Shops)[0];
                (int potionPrice, state) = PricePotion(state, potion);
                replacement = old with { Key = potion, BaseCost = potionPrice, Revision = _shopRevision + 1 };
                break;
            default: throw new InvalidOperationException("PredictorShopRestockInvalid");
        }
        Working = state;
        _shopEntries = _shopEntries.SetItem(slot, replacement);
    }

    private void RefreshShopCards()
    {
        for (int i = 0; i < _shopEntries.Length; i++)
        {
            var entry = _shopEntries[i];
            if (!entry.Stocked || entry.Card == null) continue;
            // Reapplying result hooks does not replace this stock occurrence.
            // The card payload still records any real hook change; inventory
            // command freshness belongs to the separate global revision.
            _shopEntries = _shopEntries.SetItem(i, entry with { Card = ApplyMerchantCardResults(entry.Card) });
        }
    }

    private PredictorCard ApplyMerchantCardResults(PredictorCard card)
    {
        foreach (var relic in Working.Relics.Where(r => !r.Melted))
        {
            card = PredictorRewardGeneration.UpgradeWithEgg(card, relic.Key.Entry);
            if (relic.Key.Entry == "FRESNEL_LENS" && card.Enchantment == null &&
                card.Type is EffectCardType.Attack or EffectCardType.Skill or EffectCardType.Power &&
                ShopGainsBlock(card))
                card = card with { Enchantment = new("ENCHANTMENT", "NIMBLE"), EnchantmentAmount = 2 };
        }
        return card;
    }

    private (PredictorCard Card, PredictorState State) ShopCard(PredictorState state,
        ImmutableArray<NeowEffectCardSnapshot> initialPool, IEnumerable<PredictorShopEntry> current,
        EffectCardType? type, EffectCardRarity? fixedRarity)
    {
        var excluded = current.Where(e => e.Card != null).Select(e => e.Card!.Key).ToImmutableHashSet();
        var pool = initialPool.Where(c => !excluded.Contains(c.CardKey)).ToList();
        var currentCharacterCards = Context.Catalog!.CharacterCards.Select(c => c.CardKey).ToImmutableHashSet();
        foreach (var modifier in Context.Modifiers.Where(m => m.Key.Entry == "CHARACTER_CARDS"))
        {
            if (modifier.Character is not { } character)
                throw new InvalidOperationException("PredictorCharacterCardsModifierTargetMissing");
            if (pool.Any(c => !currentCharacterCards.Contains(c.CardKey))) continue;
            var target = Context.Catalog.CharacterPools.Single(p => p.Character == character).Cards;
            foreach (var key in target)
                pool.Add(Context.Catalog.AllCharacterCards.First(c => c.CardKey == key));
        }
        pool = pool.Where(c => c.Rarity is not (EffectCardRarity.Basic or EffectCardRarity.Ancient) &&
            c.IsUnlockedInCapturedPool && !c.IsMultiplayerOnly).ToList();
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Shops).Restore();
        EffectCardRarity rarity;
        if (fixedRarity is { } fixedValue) rarity = fixedValue;
        else
        {
            var rarityRng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
            float roll = rarityRng.NextFloat();
            state = state.WithRng(PredictorStream.Rewards, rarityRng);
            rarity = roll < (Context.Ascension >= 7 ? .045f : .09f) + state.Odds.CardRarityOffset ? EffectCardRarity.Rare :
                roll < (Context.Ascension >= 7 ? .045f : .09f) + state.Odds.CardRarityOffset + .37f ? EffectCardRarity.Uncommon : EffectCardRarity.Common;
            var first = rarity;
            while (!pool.Any(c => c.Rarity == rarity && c.CardType == type))
            {
                rarity = rarity switch { EffectCardRarity.Common => EffectCardRarity.Uncommon,
                    EffectCardRarity.Uncommon => EffectCardRarity.Rare, _ => EffectCardRarity.Common };
                if (rarity == first) throw new InvalidOperationException("PredictorShopCardPoolDepleted");
            }
        }
        var options = pool.Where(c => c.Rarity == rarity && (type == null || c.CardType == type)).ToArray();
        if (options.Length == 0) throw new InvalidOperationException("PredictorShopCardPoolDepleted");
        var selected = options[rng.NextInt(options.Length)];
        state = state.WithRng(PredictorStream.Shops, rng);
        var rewards = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        rewards.NextFloat();
        state = state.WithRng(PredictorStream.Rewards, rewards);
        var card = ApplyMerchantCardResults(PredictorInitialization.ToCard(selected));
        return (card, state);
    }

    private (int Cost, PredictorState State) PriceCard(PredictorState state, PredictorCard card, bool colorless, bool sale,
        EffectCardRarity? capturedRarity = null)
    {
        var rarity = capturedRarity ?? (colorless ? Context.Catalog!.ColorlessCards : Context.Catalog!.AllCharacterCards)
            .First(c => c.CardKey == card.Key).Rarity;
        int basePrice = rarity switch
        {
            EffectCardRarity.Rare => 150,
            EffectCardRarity.Uncommon => 75,
            _ => 50
        };
        if (colorless) basePrice = (int)MathF.Round(basePrice * 1.15f);
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Shops).Restore();
        int price = (int)MathF.Round(basePrice * NextRange(rng, .95f, 1.05f));
        if (sale)
        {
            price = (int)MathF.Round(basePrice * NextRange(rng, .95f, 1.05f)) / 2;
        }
        return (price, state.WithRng(PredictorStream.Shops, rng));
    }

    private static (string Rarity, PredictorState State) RollRelicRarity(PredictorState state)
    {
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        float roll = rng.NextFloat();
        return (roll < .5f ? "Common" : roll < .83f ? "Uncommon" : "Rare", state.WithRng(PredictorStream.Rewards, rng));
    }

    private (ModelKey Key, PredictorState State) ShopRelic(PredictorState state, string rarity, ImmutableHashSet<ModelKey> blacklist)
    {
        var catalog = Context.Catalog!.ShopRelics;
        bool Allowed(ModelKey k) => catalog.Any(r => r.Key == k && r.IsAllowedInShops) &&
            PredictorRewardGeneration.RelicAllowed(Context, state, k) &&
            !state.Relics.Any(r => !r.Melted && r.Key == k);
        var pulled = state.PersonalBag.Pull(rarity, true, Allowed, k => !blacklist.Contains(k));
        var key = pulled.Key ?? new ModelKey("RELIC", "CIRCLET");
        return (key, state with { PersonalBag = pulled.Bag, SharedBag = state.SharedBag.Remove(key) });
    }

    internal (int Cost, PredictorState State) PriceMerchantRelic(PredictorState state, ModelKey key)
    {
        var option = Context.Catalog!.ShopRelics.Single(r => r.Key == key);
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Shops).Restore();
        int cost = (int)Math.Round(option.MerchantCost * NextRange(rng, .85f, 1.15f));
        return (cost, state.WithRng(PredictorStream.Shops, rng));
    }

    private (int Cost, PredictorState State) PriceRelic(PredictorState state, ModelKey key) =>
        PriceMerchantRelic(state, key);

    private (int Cost, PredictorState State) PricePotion(PredictorState state, ModelKey key)
    {
        var option = Context.PotionPool.Single(p => p.Key == key);
        int baseCost = option.Rarity switch { PredictorPotionRarity.Rare => 100, PredictorPotionRarity.Uncommon => 75, _ => 50 };
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Shops).Restore();
        int cost = (int)MathF.Round(baseCost * NextRange(rng, .95f, 1.05f));
        return (cost, state.WithRng(PredictorStream.Shops, rng));
    }

    private static float NextRange(Xoshiro256StarStar rng, float min, float max) =>
        (float)(rng.NextDouble() * (double)(max - min) + (double)min);

    private bool ShopGainsBlock(PredictorCard card) =>
        PredictorObtainSources.GainsBlockForFresnel(Context, card);
}
