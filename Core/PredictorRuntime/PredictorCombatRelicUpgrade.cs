using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCombatRelicUpgrade
{
    // WarHammer.AfterCombatVictory runs after AfterCombatEnd and player cleanup.
    internal static (PredictorState State, ImmutableArray<long> CardIds) WarHammerVictory(
        PredictorContext context, PredictorState state)
        => PredictorObtainSources.FragrantMushroom(context, state, 4);

    // FishingRod.AfterCombatEnd counts only Monster encounter rooms, including
    // a third room with no upgradable cards. NextItem(empty) consumes no RNG.
    internal static (PredictorState State, ImmutableArray<long> CardIds) FishingRodMonster(
        PredictorContext context, PredictorState entry, long relicId)
    {
        int index = entry.Relics.FindIndex(r => r.Id == relicId && r.Key.Entry == "FISHING_ROD");
        if (index < 0) throw new ArgumentException("PredictorFishingRodInstanceMissing");
        var relic = entry.Relics[index];
        int count = checked(relic.FishingRodCombatsSeen + 1);
        var state = entry with { Relics = entry.Relics.SetItem(index,
            relic with { FishingRodCombatsSeen = count }) };
        if (count % 3 != 0) return (state, []);
        var candidates = state.Deck.Where(id => state.Card(id).UpgradeLevel < state.Card(id).MaxUpgradeLevel).ToArray();
        if (candidates.Length == 0) return (state, []);
        var draw = state.Draw(context.NonCombatStream(PredictorStream.Niche), 0, candidates.Length);
        state = draw.State;
        long selectedId = candidates[draw.Value];
        var selected = state.Card(selectedId);
        return (state.WithCard(PredictorCardLevelEffects.Reconcile(context,
            selected with { UpgradeLevel = selected.UpgradeLevel + 1 })), [selectedId]);
    }

    internal static (PredictorState State, PredictorCard Card) PaelsToothReturn(
        PredictorContext context, PredictorState state, PredictorRelic relic)
    {
        (int selected, state) = state.Draw(PredictorStream.Rewards, 0, relic.StoredCards.Length);
        var stored = relic.StoredCards[selected];
        // SerializableCard equality drives native List.Remove. The card drawn
        // is returned, but an earlier equal copy can be the one removed.
        int removed = relic.StoredCards.FindIndex(card => card.Key == stored.Key &&
            card.UpgradeLevel == stored.UpgradeLevel && card.Enchantment == stored.Enchantment &&
            (!card.Enchantment.HasValue || card.EnchantmentAmount == stored.EnchantmentAmount));
        long returnedId = state.NextInstanceId;
        state = PredictorObtainSources.AddCard(context, state, UpgradeStoredCard(stored));
        int index = state.Relics.FindIndex(candidate => candidate.Id == relic.Id);
        state = state with { Relics = state.Relics.SetItem(index, state.Relics[index] with
        {
            StoredCards = relic.StoredCards.RemoveAt(removed)
        }) };
        return (state, state.Card(returnedId));
    }

    internal static PredictorCard UpgradeStoredCard(PredictorCard card) => card.UpgradeLevel < card.MaxUpgradeLevel
        ? card with { UpgradeLevel = card.UpgradeLevel + 1 } : card;

    internal static PredictorState ToyBoxCombatEnd(PredictorState state, PredictorRelic relic)
    {
        int count = relic.ToyBoxCombatsSeen + 1;
        int index = state.Relics.FindIndex(candidate => candidate.Id == relic.Id);
        state = state with { Relics = state.Relics.SetItem(index, relic with { ToyBoxCombatsSeen = count }) };
        if (count % 3 != 0) return state;
        int wax = state.Relics.FindIndex(candidate => candidate.IsWax && !candidate.Melted);
        return wax < 0 ? state : state with { Relics = state.Relics.SetItem(wax, state.Relics[wax] with { Melted = true }) };
    }
}
