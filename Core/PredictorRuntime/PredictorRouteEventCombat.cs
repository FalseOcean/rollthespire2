using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{

    private static ImmutableArray<PredictorCombatPendingCause> RouteEventCombatContinuationCauses(PredictorRun run,
        PredictorCombatPreview? combat) => combat == null ? [] :
        run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults
            ? combat.PendingCauses.Where(cause => (cause.AffectedDomains & PredictorCombatDomain.Continuation) != 0).ToImmutableArray()
            : combat.PendingCauses;

    // Continue the same event parent with the ordinary combat projection's
    // masked settlement. Only its private reward transaction is transferred:
    // replay inputs, the event branch, deferred return and obtain parents stay
    // owned by this run. No missing combat fact becomes an accepted input.
    private static bool ProjectDrawingEventCombat(PredictorRun run, PredictorPlannedEventAction step,
        List<RouteDependency> dependencies, List<PredictorCombatPendingCause> local, CancellationToken token,
        bool requireKnownContinuation = false)
    {
        var afterGeneration = new List<RouteDependency>();
        PropagateDeckCombatReaders(run, dependencies, afterGeneration);
        PredictorCombatProjection? projection;
        try { projection = run.ProjectCurrentCombat(step.Combat, token); }
        catch (ArgumentException ex) when (IsRouteEventCombatBindingError(ex))
        {
            local.Add(RouteCause("PlannedEventCombatFactsPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            return false;
        }
        if (projection == null) return false;
        if (requireKnownContinuation && !RouteEventCombatContinuationCauses(run, projection.Knowledge).IsEmpty)
            return false;
        // Timeout selects a different event return, unlike an unknown health
        // amount or inventory. Its actual branch must be supplied before return.
        bool dummy = run._eventCombatEntry!.Encounter.Key.Entry.StartsWith("BATTLEWORN_DUMMY_", StringComparison.Ordinal);
        if (dummy != step.EventRanOutOfTime.HasValue)
        {
            local.Add(RouteCause(dummy ? "EventCombatDeclarationRequired" : "PlannedEventTimeoutChanged",
                run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                dummy ? projection.Knowledge.PendingCauses.Where(cause => cause.Code == "EventTimeout").ToImmutableArray() : []));
            return false;
        }
        foreach (var cause in projection.Knowledge.PendingCauses.Where(cause => cause.Code != "EventTimeout"))
            AddDependency(dependencies, new(cause,
                cause.Code == "StolenCardIdentity" ? run.Working.Deck.Select(run.Working.Card).ToImmutableArray() : default,
                (cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 && cause.Code != "PaelsToothReturnedCardPending"));
        var settled = projection.Witness;
        run.AcceptSettledEventCombat(settled, step, projection.Knowledge.Rewards);
        run._preview.Event.CombatAfterGeneration.Clear();
        foreach (var dependency in afterGeneration)
        {
            var existing = dependencies.FirstOrDefault(value => value.Cause.Identity == dependency.Cause.Identity);
            var addedDomains = dependency.Cause.AffectedDomains & ~(existing?.Cause.AffectedDomains ?? PredictorCombatDomain.None);
            if (addedDomains != PredictorCombatDomain.None)
                run._preview.Event.CombatAfterGeneration.Add(dependency with
                { Cause = dependency.Cause with { AffectedDomains = addedDomains } });
            AddDependency(dependencies, dependency);
        }
        run._preview.Partial?.Synchronize(run.Working);
        run.Phase = PredictorPhase.Rewards;
        run._requestId++;
        run._preview.Partial?.Facts.RecordResult("event.combat.projectedSettlement", run.Working.Position.NodeId,
            run._eventCombatEntry.Encounter.Key.Serialized, projection.Knowledge.PendingCauses,
            operation: "Continue the actual nested encounter with the existing combat projection; missing facts retain their dependency masks.");
        if (run.Rewards.IsEmpty) run.TryFinishEventCombatRewards();
        return true;
    }

    private static void ContinueDrawingEventCombat(PredictorRun run, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || local.Count != 0 ||
            run.Phase != PredictorPhase.CombatFacts || run._eventCombatEntry is not { } entry ||
            entry.Encounter.Key.Entry.StartsWith("BATTLEWORN_DUMMY_", StringComparison.Ordinal)) return;
        // The adopted business branch owns this actual encounter. Reuse its
        // masked projection without saving an invented Combat action or facts.
        // A real continuation declaration (including timeout) still stays open.
        if (ProjectDrawingEventCombat(run, new(PredictorEventActionKind.Combat)
            { ExpectedEncounter = entry.Encounter.Key }, dependencies, local, token, requireKnownContinuation: true))
            run._preview.Partial?.Facts.RecordResult("event.default.combatProjection", run.Working.Position.NodeId,
                entry.Encounter.Key.Serialized, origin: PredictorKnowledgeOrigin.ApprovedDefault,
                operation: "Continue the adopted encounter through its known continuation; unresolved settlement facts retain their actual domains.");
    }

    private static List<RouteDependency> EventCombatRewardDependencies(PredictorRun run, PredictorReward reward,
        List<RouteDependency> dependencies)
    {
        string? source = run._preview.Partial?.CombatRewardSourceIdentity(run.Working.Position.NodeId, reward);
        if (source == null ||
            !run._preview.Event.CombatRewardKnowledge.Any(knowledge => knowledge.SourceIdentity == source) ||
            run._preview.Event.CombatAfterGeneration.Count == 0) return dependencies;
        // These exact offers precede Hunt/Royalties' contribution to the next
        // cursor. A reroll or newly generated obtain reward has a new source and
        // reads that cursor normally. Never remove a domain already unresolved
        // on entry, or a separate extra-reward identity dependency.
        return dependencies.Select(dependency => dependency with { Cause = dependency.Cause with
        { AffectedDomains = dependency.Cause.AffectedDomains & ~run._preview.Event.CombatAfterGeneration
            .Where(after => after.Cause.Identity == dependency.Cause.Identity)
            .Aggregate(PredictorCombatDomain.None, (domains, after) => domains | after.Cause.AffectedDomains) } })
            .Where(dependency => dependency.Cause.AffectedDomains != PredictorCombatDomain.None).ToList();
    }
}
