using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

// Same card-obtain handlers as the route research runtime. Crystal witnesses
// use these to validate taking rewards on detached state, never on the live run.
internal static class PredictorObtainSources
{
    internal static PredictorState AddCard(PredictorContext context, PredictorState state, ModelKey key)
    {
        var definition = PredictorCardChanges.Definition(context, key);
        return AddCard(context, state, definition.Prototype);
    }

    internal static PredictorState AddCard(PredictorContext context, PredictorState state, PredictorCard prototype)
        => AddNewCardCore(context, state, prototype, preserveFloorAdded: false, suppressCopies: false);

    internal static PredictorState CloneCardToDeck(PredictorContext context, PredictorState state, PredictorCard source)
        => AddNewCardCore(context, state, source, preserveFloorAdded: true, suppressCopies: false);

    private static PredictorState AddNewCardCore(PredictorContext context, PredictorState state,
        PredictorCard prototype, bool preserveFloorAdded, bool suppressCopies)
    {
        var card = prototype with { Id = state.NextInstanceId,
            FloorAdded = preserveFloorAdded ? prototype.FloorAdded : state.Position.Floor };
        foreach (var relic in state.Relics.Where(r => !r.Melted))
        {
            card = PredictorRewardGeneration.UpgradeOnDeckAddWithEgg(card, relic.Key.Entry);
            if (relic.Key.Entry == "FRESNEL_LENS" && CanEnchant(context, card) &&
                GainsBlockForFresnel(context, card))
                card = card with { Enchantment = new("ENCHANTMENT", "NIMBLE"), EnchantmentAmount = 2 };
        }
        card = PredictorCardLevelEffects.Reconcile(context, card);
        state = state with { NextInstanceId = state.NextInstanceId + 1,
            Cards = state.Cards.Add(card), Deck = state.Deck.Add(card.Id) };
        return AfterCardAddedToDeck(context, state, card.Id, suppressCopies);
    }

    internal static PredictorState AddExistingCard(PredictorContext context, PredictorState state, PredictorCard offered)
    {
        var card = offered with { FloorAdded = state.Position.Floor };
        foreach (var relic in state.Relics.Where(r => !r.Melted))
        {
            var upgraded = PredictorRewardGeneration.UpgradeOnDeckAddWithEgg(card, relic.Key.Entry);
            if (relic.Key.Entry == "FRESNEL_LENS" && CanEnchant(context, upgraded) &&
                GainsBlockForFresnel(context, upgraded))
                upgraded = upgraded with { Enchantment = new("ENCHANTMENT", "NIMBLE"), EnchantmentAmount = 2 };
            if (upgraded != card)
            {
                card = upgraded with { Id = state.NextInstanceId };
                state = state with { NextInstanceId = state.NextInstanceId + 1, Cards = state.Cards.Add(card) };
            }
        }
        card = PredictorCardLevelEffects.Reconcile(context, card);
        state = state.WithCard(card);
        state = state with { Deck = state.Deck.Add(card.Id) };
        return AfterCardAddedToDeck(context, state, card.Id);
    }

    internal static PredictorState AfterCardAddedToDeck(PredictorContext context, PredictorState state, long cardId,
        bool clonedByListener = false)
    {
        var added = state.Card(cardId);
        // Hook listeners run in relic inventory order, then run modifiers.
        // Clones still notify LuckyFysh, while clonedBy suppresses another
        // BingBong/Hoarder copy.
        foreach (var relic in state.Relics.Where(r => !r.Melted).ToArray())
        {
            if (relic.Key.Entry == "BING_BONG" && !clonedByListener)
            {
                int index = state.Relics.FindIndex(r => r.Id == relic.Id);
                var listener = state.Relics[index];
                if (listener.BingBongCardsToSkip.Contains(cardId))
                    state = state with { Relics = state.Relics.SetItem(index,
                        listener with { BingBongCardsToSkip = listener.BingBongCardsToSkip.Remove(cardId) }) };
                else
                {
                    // CloneCard registers the original instance before Add; the
                    // clonedBy callback leaves this skip intact until re-entry.
                    long cloneId = state.NextInstanceId;
                    state = state with { Relics = state.Relics.SetItem(index,
                        listener with { BingBongCardsToSkip = listener.BingBongCardsToSkip.Add(cloneId) }) };
                    state = AddNewCardCore(context, state, added, preserveFloorAdded: true, suppressCopies: true);
                }
            }
            if (relic.Key.Entry == "LUCKY_FYSH")
                state = PredictorSettlementEffects.TakeGold(state, 15);
            if (relic.Key.Entry == "DARKSTONE_PERIAPT" && added.Type == EffectCardType.Curse)
                state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(
                    new(relic.Key, relic.Id, 6, 6)) };
            if (relic.Key.Entry == "BOOK_OF_FIVE_RINGS")
            {
                int index = state.Relics.FindIndex(r => r.Id == relic.Id);
                var listener = state.Relics[index];
                int addedCount = checked(listener.BookOfFiveRingsCardsAdded + 1);
                state = state with { Relics = state.Relics.SetItem(index,
                    listener with { BookOfFiveRingsCardsAdded = addedCount }) };
                if (addedCount % 5 == 0)
                    state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(
                        new(relic.Key, relic.Id, 0, 20)) };
            }
        }
        if (!clonedByListener && context.HasRunModifier("HOARDER"))
        {
            if (state.HoarderCardsToSkip.Contains(cardId))
                state = state with { HoarderCardsToSkip = state.HoarderCardsToSkip.Remove(cardId) };
            else
                for (int i = 0; i < 2; i++)
                {
                    state = state with { HoarderCardsToSkip = state.HoarderCardsToSkip.Add(state.NextInstanceId) };
                    state = AddNewCardCore(context, state, added, preserveFloorAdded: true, suppressCopies: true);
                }
        }
        return state;
    }

    internal static bool GainsBlockForFresnel(PredictorContext context, PredictorCard card) =>
        card.Key.Entry switch
        {
            "SOVEREIGN_BLADE" => false,
            "MAD_SCIENCE" => card.TinkerTimeType == EffectCardType.Skill,
            _ => (context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing"))
                .ShopGainsBlock.Contains(card.Key)
        };

    internal static bool CanEnchant(PredictorContext context, PredictorCard card)
    {
        if (card.Enchantment != null || card.Type is EffectCardType.Status or EffectCardType.Curse)
            return false;
        var definition = PredictorCardChanges.Definition(context, card.Key);
        return !definition.IsQuest && !PredictorCardLevelEffects.Resolve(context, card).Unplayable;
    }
}
