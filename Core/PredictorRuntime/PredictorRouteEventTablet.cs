using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static bool ProjectDrawingTabletDecipher(PredictorRun run, List<RouteDependency> dependencies)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            run._eventKey?.Entry != "TABLET_OF_TRUTH" || run._pendingHealthOption != "DECIPHER" ||
            run._eventHealth != null) return false;

        // Beta111 checks max HP before deciding whether to upgrade. A lethal
        // attempt skips that draw and may spend a death-prevention item. All
        // upgrade draws belong to the discarded event-local stream, so those
        // unknown effects need not block independent later public RNG.
        var cards = run.Working.Deck.Select(run.Working.Card).Concat(run._preview.Partial?.Deck.Choices
            .SelectMany(choice => choice.Effects).SelectMany(effect => effect.AddedCards) ?? [])
            .Distinct().ToImmutableArray();
        var domains = PredictorCombatDomain.Health | PredictorCombatDomain.Deck;
        if (run.EventPotionSlots.Any(potion => potion?.Key.Entry == "FAIRY_IN_A_BOTTLE") ||
            !CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty)
            domains |= PredictorCombatDomain.PotionInventory;
        if (run.Working.Relics.Any(relic => !relic.Melted && relic.Key.Entry == "LIZARD_TAIL" && !relic.LizardTailUsed) ||
            !CausesFor(dependencies, PredictorCombatDomain.Relics).IsEmpty)
            domains |= PredictorCombatDomain.Relics;
        var facts = PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other;
        if (cards.Any(card => PredictorCardChanges.Definition(run.Context, card.Key).Levels
            .Select(level => level.Eternal).Distinct().Count() > 1)) facts |= PredictorDeckUncertainFact.Removable;
        var cause = PredictorRouteCauseEvidence.CreateExplicit("EventTabletDecipherOutcomePending",
            run.Working.Position.NodeId, domains, PredictorPendingReasonKind.MissingObservation,
            "Tablet Decipher loses max HP and upgrades one random eligible card (all eligible cards on the fifth attempt) only on its nonlethal source path. Actual max HP, death prevention and upgrade targets remain unresolved; no public RNG stream is consumed.",
            ["health.maxHp", "deck.Upgradable.candidates"], CausesFor(dependencies, domains),
            occurrenceKey: run._eventKey.Value.Serialized + ":DECIPHER:" + run._tabletCount);
        var effect = new PendingRouteEventCardEffect(cards, cards, domains, facts, false, false, 0, 0)
        { TargetRead = new(PredictorDeckProjectionScope.Upgradable, PredictorDeckUncertainFact.Upgrade),
            MayEditExistingCards = true };
        run._preview.Event.PendingCardEffect = effect;
        run._preview.Partial?.PendingEventCards(run, cause, effect);
        AddDependency(dependencies, EventCardEffectDependency(cause, effect));
        run.EventTrace()?.PartialLocalDeckOperation(cause);

        run._pendingHealthOption = null;
        run._pendingHealthOptions = [];
        run._eventBranch = "DECIPHER";
        run.Accept(new("EventOption:DECIPHER", 0, [], null, null));
        run._tabletCount++;
        if (run._tabletCount == 5) run.FinishEvent();
        else
        {
            // The final price is current max HP - 1. Keep it unknown rather
            // than manufacture a numerical health command or a survivor HP.
            int? cost = run._tabletCount switch { 1 => 6, 2 => 12, 3 => 24, 4 => null,
                _ => throw new InvalidOperationException("PredictorTabletCountInvalid") };
            run.EventOptions = [new("DECIPHER", MaxHpCost: cost), new("GIVE_UP")];
            run._requestId++;
        }
        run.EventTrace()?.Stage(run, dependencies);
        return true;
    }
}
