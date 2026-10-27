using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // A selected rest action can finish in the drawing projection without
    // inventing its target. Concrete Submit and other preview policies retain
    // their target and health requirements.
    private static bool TryProjectLiveRestTarget(PredictorRun run, PredictorPlannedRestAction step,
        List<RouteDependency> dependencies, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._preview.Partial == null ||
            step.Action is not (PredictorRestAction.Smith or PredictorRestAction.Cook) ||
            run.Phase is not (PredictorPhase.RestSite or PredictorPhase.SmithCards or PredictorPhase.RestCookCards) ||
            !run.RestOptions.Contains(step.Action)) return false;
        token.ThrowIfCancellationRequested();
        var facts = step.Action == PredictorRestAction.Smith
            ? PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other
            : PredictorDeckUncertainFact.Membership;
        var cards = run.Working.Deck.Select(run.Working.Card).ToImmutableArray();
        if (step.Action == PredictorRestAction.Smith && cards.Any(card => PredictorCardChanges.Definition(run.Context, card.Key)
                .Levels.Select(level => level.Eternal).Distinct().Count() > 1)) facts |= PredictorDeckUncertainFact.Removable;
        var domains = PredictorCombatDomain.Deck;
        if (step.Action == PredictorRestAction.Cook)
        {
            domains |= PredictorCombatDomain.Health;
            if (cards.Any(card => card.Key.Entry == "SPOILS_MAP") || RouteCardMayVary(run, dependencies, "SPOILS_MAP"))
                domains |= PredictorCombatDomain.Map;
        }
        string operation = "rest-target:" + step.Action + ":" + run._inputs.Length;
        var cause = PredictorRouteCauseEvidence.CreateExplicit(run._preview.RestDefaultInProgress ? "RestDefaultTargetPending" :
            "PlannedRestTargetPending", run.Working.Position.NodeId,
            domains, PredictorPendingReasonKind.UserChoice,
            "The rest action is retained without choosing a representative card target; independent rewards can continue.",
            ["choice.rest." + step.Action + ".target"],
            CausesFor(dependencies, PredictorCombatDomain.Deck), occurrenceKey: operation);
        AddDependency(dependencies, new(cause, cards, MayChangeExistingCards: true)
        {
            DeckFacts = facts,
            ChangedCardMembershipEntries = step.Action == PredictorRestAction.Smith ? [] : default
        });
        run._preview.Partial.Synchronize(run.Working);
        // Complete the selected action instance, including Tent's remaining
        // actions. No target is submitted and no authored suffix is discarded.
        run.Accept(new(step.Action == PredictorRestAction.Smith ? "RestSmith" : "RestCookRemoveTwoGainMaxHp5",
            0, [], null, null));
        run.CompleteRestAction(step.Action);
        return true;
    }

    private static bool TryProjectLiveRestHeal(PredictorRun run, List<RouteDependency> dependencies,
        CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._preview.Partial == null ||
            run.Phase != PredictorPhase.RestHealFact || !run.Context.HasRunModifier("NIGHT_TERRORS")) return false;
        token.ThrowIfCancellationRequested();
        var cause = PredictorRouteCauseEvidence.CreateExplicit("RestMaximumHp", run.Working.Position.NodeId,
            PredictorCombatDomain.Health, PredictorPendingReasonKind.MissingObservation,
            "Maximum HP determines the rest's health consequence, but not its card or potion rewards.",
            ["health.rest.maximum"], occurrenceKey: "rest-heal:" + run._inputs.Length);
        AddDependency(dependencies, new(cause));
        run._restMaxHpFact = null;
        run.Phase = PredictorPhase.RestSite;
        run.Accept(new("RestHeal", 0, [], null, null));
        run._restModelTrace?.ReadRestHealHooks(run);
        var plans = run.RestHealRewardPlans();
        if (plans.Count > 0 && !run.BeginRestHealRewards(plans, 0, token))
        {
            run._restHealRewardsPending = true;
            return true;
        }
        run.CompleteRestAction(PredictorRestAction.Heal);
        return true;
    }
}
