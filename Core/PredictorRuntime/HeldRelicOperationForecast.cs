using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum HeldRelicCombat { None, Transition, Monster, Elite, Boss }

internal sealed record HeldRelicOperationObservation(PredictorContext Context, PredictorState State,
    long RelicId, HeldRelicCombat Combat, string Stamp)
{
    public ImmutableArray<PredictorState> PriorSharedStreamPlayers { get; init; } = [];
}

internal sealed record HeldRelicOperationResult(ImmutableArray<PredictorCard> Cards,
    int OrdinaryBattlesRemaining, bool ThisBattle, PredictorCard? FinalReturn = null, bool NoUpgradeTarget = false);

internal static class HeldRelicOperationForecast
{
    internal static HeldRelicOperationResult Calculate(HeldRelicOperationObservation observation)
    {
        var (context, state, sourceId, combat, _) = observation;
        var source = state.Relics.Single(relic => relic.Id == sourceId);
        int remaining = source.Key.Entry == "FISHING_ROD" ? 3 - source.FishingRodCombatsSeen % 3 : 0;
        bool thisBattle = source.Key.Entry switch
        {
            "WAR_HAMMER" => combat == HeldRelicCombat.Elite,
            "FISHING_ROD" => remaining == 1 && combat == HeldRelicCombat.Monster,
            _ => combat is HeldRelicCombat.Monster or HeldRelicCombat.Elite or HeldRelicCombat.Boss
        };
        HeldRelicOperationResult Empty() => new([], remaining, thisBattle);
        if (source.Melted || combat == HeldRelicCombat.Transition) return Empty();

        // Model the trigger on today's inputs. Intervening choices are outside
        // this observation. A Rod forecast before its third fight is conditional.
        if (remaining > 1)
            state = Replace(state, source with { FishingRodCombatsSeen = source.FishingRodCombatsSeen + remaining - 1 });
        bool elite = source.Key.Entry == "WAR_HAMMER";
        bool monster = source.Key.Entry == "FISHING_ROD" ||
            source.Key.Entry == "PAELS_TOOTH" && combat == HeldRelicCombat.Monster;
        // 0.111 shares Niche. Earlier players' hooks consume it first, while
        // their reward streams and deck changes remain personal.
        foreach (var peer in observation.PriorSharedStreamPlayers)
        {
            var shared = state.Streams.Single(stream => stream.Stream == PredictorStream.Niche);
            var peerEntry = peer.WithRng(PredictorStream.Niche, shared.Restore());
            var after = ApplyHooks(context, peerEntry, 0, elite, monster).State;
            var advanced = after.Streams.Single(stream => stream.Stream == PredictorStream.Niche);
            state = state.WithRng(PredictorStream.Niche, advanced.Restore());
        }
        var (_, cards, final) = ApplyHooks(context, state, sourceId, elite, monster);
        return new(cards, remaining, thisBattle, final, remaining > 0 && cards.IsEmpty);
    }

    private static (PredictorState State, ImmutableArray<PredictorCard> Cards, PredictorCard? FinalReturn)
        ApplyHooks(PredictorContext context, PredictorState state, long sourceId, bool elite, bool monster)
    {
        PredictorCard? sourceReturn = null;
        var activeAtEnd = state.Relics.Where(relic => !relic.Melted).Select(relic => relic.Id).ToArray();
        foreach (long relicId in activeAtEnd)
        {
            var relic = state.Relics.Single(candidate => candidate.Id == relicId);
            switch (relic.Key.Entry)
            {
                case "FISHING_ROD" when monster:
                    var upgraded = PredictorCombatRelicUpgrade.FishingRodMonster(context, state, relicId);
                    state = upgraded.State;
                    if (relicId == sourceId) return (state, upgraded.CardIds.Select(state.Card).ToImmutableArray(), null);
                    break;
                case "TOY_BOX" when relic.ToyBoxCombatsSeen < 15:
                    state = PredictorCombatRelicUpgrade.ToyBoxCombatEnd(state, relic);
                    break;
                case "PAELS_TOOTH" when !relic.StoredCards.IsEmpty:
                    var returned = PredictorCombatRelicUpgrade.PaelsToothReturn(context, state, relic);
                    state = returned.State;
                    if (relicId == sourceId) sourceReturn = returned.Card;
                    break;
            }
        }
        if (sourceReturn is not null)
        {
            var storage = state.Relics.Single(relic => relic.Id == sourceId);
            var final = !storage.Melted && storage.StoredCards.Length == 1
                ? PredictorObtainSources.PrepareAddedCard(context, state,
                    PredictorCombatRelicUpgrade.UpgradeStoredCard(storage.StoredCards[0])) : null;
            return (state, [sourceReturn], final);
        }
        if (elite)
            foreach (var relic in state.Relics.Where(relic => !relic.Melted && relic.Key.Entry == "WAR_HAMMER").ToArray())
            {
                var upgraded = PredictorCombatRelicUpgrade.WarHammerVictory(context, state);
                state = upgraded.State;
                if (relic.Id == sourceId) return (state, upgraded.CardIds.Select(state.Card).ToImmutableArray(), null);
            }
        return (state, [], null);
    }

    private static PredictorState Replace(PredictorState state, PredictorRelic relic) =>
        state with { Relics = state.Relics.SetItem(state.Relics.FindIndex(candidate => candidate.Id == relic.Id), relic) };
}
