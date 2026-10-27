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
        var draw = state.Draw(PredictorStream.Niche, 0, candidates.Length);
        state = draw.State;
        long selectedId = candidates[draw.Value];
        var selected = state.Card(selectedId);
        return (state.WithCard(PredictorCardLevelEffects.Reconcile(context,
            selected with { UpgradeLevel = selected.UpgradeLevel + 1 })), [selectedId]);
    }
}
