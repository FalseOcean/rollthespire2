using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorRouteEditChecks
{
    private static readonly ImmutableArray<MapPosition> Original = [new(3, 0), new(3, 1), new(1, 2), new(3, 3), new(3, 4)];
    private static readonly ImmutableArray<MapPosition> Replacement = [new(3, 0), new(3, 1), new(5, 2), new(3, 3), new(3, 4)];
    private const string Current = "map:1:1:3:2", Abandoned = "map:1:2:1:3", Rejoined = "map:1:3:3:4";

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Route redraw: " + message); }

    internal static void Run(PredictorRun captured)
    {
        ForkRejoinAndUndo(captured);
        InvalidAndNoOpEdits(captured);
        PersistenceAndAcceptedOperations(captured);
        AdjustCombatAfterRedraw(captured);
        PlanEditsAndReplacementWindow(captured);
        CompletedFactsAndMapReplacement(captured);
        Console.WriteLine("RUNTIME_ROUTE_EDIT_PASS fork/rejoin; temporary undo; current-node/checkpoint purity; recomputed preview; invalid/no-op preservation; accepted-operation expiry; combat revision preserves redraw; persistence excludes abandoned intent; map replacement cleanup");
    }

    // Real source catalogs, initialization and rest settlement remain in use;
    // only map geometry fixes a branch and rejoin for deterministic assertions.
    private static PredictorRun Fixture(PredictorRun captured, bool goldenCompass = false, bool combatCurrent = false)
    {
        var positions = new[] { new MapPosition(3, 0), new(3, 1), new(1, 2), new(5, 2), new(3, 3), new(3, 4) };
        var points = positions.Select(position => new MapPredictionPoint(position,
            position.Row == 0 ? MapPointType.Ancient : position.Row == 4 ? MapPointType.Boss :
                combatCurrent && position.Row == 1 ? MapPointType.Monster : MapPointType.RestSite, true,
            position.Row switch
            {
                0 => [new(3, 1)], 1 => [new(1, 2), new(5, 2)],
                2 => [new(3, 3)], 3 => [new(3, 4)], _ => Array.Empty<MapPosition>()
            })).ToArray();
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], Position = new(1, 1, "map:1:0:3:1"),
            Maps = [new(1, captured.RouteMap(1) with { Points = points }, [positions[0]])],
            Visits = [new(1, positions[0], MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = captured.Working.Acts.SetItem(0, captured.Working.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 })
        };
        if (goldenCompass) state = state with
        {
            Relics = [new(state.NextInstanceId, new("RELIC", "GOLDEN_COMPASS"), 1)],
            NextInstanceId = state.NextInstanceId + 1
        };
        var run = new PredictorRun(captured.Context, state);
        run.SetRoute(1, Original);
        Check(!run.CanUndoRouteRedraw, "initial commitment has no prior redraw");
        run.SetPlannedNode(new(Current) { LeaveRest = !combatCurrent, LeaveRewards = combatCurrent });
        run.SetPlannedNode(new(Abandoned) { LeaveRest = true });
        run.SetPlannedNode(new(Rejoined) { LeaveRest = true });
        run.SetPlannedNode(new("future:unbound") { LeaveRewards = true });
        run.AdvanceRoute();
        Check(run.Phase == (combatCurrent ? PredictorPhase.CombatFacts : PredictorPhase.RestSite) &&
            run.Working.Position.NodeId == Current, "fixture enters current route node");
        return run;
    }

    private static void ForkRejoinAndUndo(PredictorRun captured)
    {
        var run = Fixture(captured);
        string working = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints),
            plans = JsonSerializer.Serialize(run.PlannedNodes);
        var request = run.Request;
        var oldPreview = run.PreviewRouteContinuation(run.PlannedNodes, 2);
        Check(oldPreview.Nodes.Length == 2 && oldPreview.Nodes[1].Position.NodeId == Abandoned,
            "original preview follows authored old branch");
        run.SetRoute(1, Replacement);
        Check(run.CanUndoRouteRedraw && run.PlannedNodes.All(p => p.NodeId != Abandoned) &&
            run.PlannedNodes.Any(p => p.NodeId == Current) && run.PlannedNodes.Any(p => p.NodeId == Rejoined) &&
            run.PlannedNodes.Any(p => p.NodeId == "future:unbound"),
            "redraw removes abandoned operations and retains current, rejoined and unrelated intent");
        var preview = run.PreviewRouteContinuation(run.PlannedNodes, 2);
        Check(preview.Nodes.Length == 2 && preview.Nodes[1].Position.NodeId == "map:1:2:5:3" &&
            !preview.Nodes[1].Advanced, "preview recomputes the new branch without borrowing old branch's leave action");
        Check(working == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            request == run.Request && run.CanUndoRouteRedraw, "redraw and observation preserve factual current node and requests");
        Check(run.UndoRouteRedraw() && run.Routes.Single().Points.SequenceEqual(Original) &&
            plans == JsonSerializer.Serialize(run.PlannedNodes), "immediate undo restores original route and operations exactly");
        Check(working == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            !run.CanUndoRouteRedraw && !run.UndoRouteRedraw(), "undo changes no facts and has no redo/second undo window");
    }

    private static void InvalidAndNoOpEdits(PredictorRun captured)
    {
        var run = Fixture(captured);
        run.SetRoute(1, Replacement);
        run.SetRoute(1, Replacement.ToArray().ToImmutableArray());
        var plan = run.PlannedNodes.Single(p => p.NodeId == Current);
        run.SetPlannedNode(plan with { });
        run.RemovePlannedNode("missing:plan");
        run.RestoreRoutes([new(1, Replacement.ToArray().ToImmutableArray())]);
        run.RestorePlannedNodes(run.PlannedNodes.ToArray().ToImmutableArray());
        Check(run.CanUndoRouteRedraw, "equivalent route/plan setters and absent removal preserve undo");
        try { run.SetRoute(1, [new(3, 0), new(5, 1), new(5, 2), new(3, 3), new(3, 4)]); }
        catch (ArgumentException) { }
        Check(run.Routes.Single().Points.SequenceEqual(Replacement) && run.CanUndoRouteRedraw,
            "redraw cannot move factual current node and does not destroy the previous window");
        try { run.SetPlannedNode(new(" ")); }
        catch (InvalidDataException) { }
        Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Invalid && run.CanUndoRouteRedraw,
            "invalid input and malformed plan preserve undo");
        var stale = run.Request! with { Revision = run.Revision + 1 };
        Check(run.Submit(stale, new LeaveRestSite()) == PredictorInputResult.Stale && run.CanUndoRouteRedraw,
            "stale input preserves undo");
        try { run.AdvanceRoute(); }
        catch (InvalidOperationException) { }
        Check(run.CanUndoRouteRedraw && run.UndoRouteRedraw(), "failed travel preserves undo");
    }

    private static void PersistenceAndAcceptedOperations(PredictorRun captured)
    {
        var run = Fixture(captured);
        run.SetRoute(1, Replacement);
        string serialized = PredictorRunStore.Serialize(run);
        var restored = PredictorRunStore.Deserialize(serialized, run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
        Check(run.CanUndoRouteRedraw && !restored.CanUndoRouteRedraw && !restored.UndoRouteRedraw(),
            "saving observes the run while reload has no temporary undo snapshot");
        Check(restored.Routes.Single().Points.SequenceEqual(Replacement) && restored.PlannedNodes.All(p => p.NodeId != Abandoned) &&
            restored.PlannedNodes.Any(p => p.NodeId == Rejoined) && restored.Working.Position.NodeId == Current,
            "active draft replay preserves current route/current plans and factual node");
        Check(run.Submit(run.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted && !run.CanUndoRouteRedraw &&
            !run.UndoRouteRedraw(), "next accepted operation expires the old branch opportunity");
        run.SetRoute(1, Original);
        Check(run.CanUndoRouteRedraw, "later redraw creates a new independent window");
        run.AdvanceRoute();
        Check(!run.CanUndoRouteRedraw, "successful travel expires redraw undo");

        var cancelled = Fixture(captured);
        Check(cancelled.Submit(cancelled.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted, "cancellation fixture commits rest");
        cancelled.SetRoute(1, Replacement);
        using var token = new CancellationTokenSource(); token.Cancel();
        try { cancelled.AdvanceRoute(token.Token); }
        catch (OperationCanceledException) { }
        Check(cancelled.CanUndoRouteRedraw && cancelled.UndoRouteRedraw(), "cancelled travel preserves undo and route boundary");
    }

    private static void AdjustCombatAfterRedraw(PredictorRun captured)
    {
        var run = Fixture(captured, combatCurrent: true);
        string entry = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var input = run.DefaultCombatInput();
        Check(input != null && run.Submit(run.Request!, input) == PredictorInputResult.Accepted &&
            run.Phase == PredictorPhase.Rewards && run.CanAdjustCurrentCombat,
            "combat fixture resolves into revisable rewards");
        run.SetRoute(1, Replacement);
        string plans = JsonSerializer.Serialize(run.PlannedNodes);
        var previousRequest = run.Request!;
        Check(run.CanUndoRouteRedraw && run.PlannedNodes.All(p => p.NodeId != Abandoned),
            "combat-reward redraw removes old branch before adjustment");
        Check(run.Submit(previousRequest, new AdjustCurrentCombat()) == PredictorInputResult.Accepted &&
            run.Phase == PredictorPhase.CombatFacts && run.Working.Position.NodeId == Current,
            "combat adjustment replays the current factual node to its combat boundary");
        Check(run.Routes.Single().Points.SequenceEqual(Replacement) &&
            plans == JsonSerializer.Serialize(run.PlannedNodes) && run.PlannedNodes.Any(p => p.NodeId == Rejoined) &&
            run.PlannedNodes.All(p => p.NodeId != Abandoned),
            "historical entry replay preserves redrawn route/common plans without resurrecting old branch operations");
        Check(!run.CanUndoRouteRedraw && !run.UndoRouteRedraw() && entry == JsonSerializer.Serialize(run.Working) &&
            checkpoints == JsonSerializer.Serialize(run.Checkpoints),
            "meaningful combat adjustment expires redraw undo and restores exact current combat entry facts");
        Check(run.Submit(previousRequest, new LeaveRewards()) == PredictorInputResult.Stale,
            "pre-adjustment rewards request remains stale");
    }

    private static void PlanEditsAndReplacementWindow(PredictorRun captured)
    {
        var run = Fixture(captured);
        run.SetRoute(1, Replacement);
        run.SetPlannedNode(run.PlannedNodes.Single(p => p.NodeId == Current) with { LeaveRest = false });
        Check(!run.CanUndoRouteRedraw && !run.UndoRouteRedraw(), "meaningful plan edit expires undo");
        run.SetRoute(1, Original);
        Check(run.CanUndoRouteRedraw && run.PlannedNodes.All(p => p.NodeId != Abandoned),
            "returning to discarded branch does not resurrect its obsolete operations");
        run.SetRoute(1, Replacement);
        Check(run.UndoRouteRedraw() && run.Routes.Single().Points.SequenceEqual(Original) &&
            run.PlannedNodes.All(p => p.NodeId != Abandoned), "second redraw replaces the previous window instead of stacking history");
        run.SetRoute(1, Replacement);
        run.RemovePlannedNode(Rejoined);
        Check(!run.CanUndoRouteRedraw, "successful plan removal expires undo");

        var defaults = Fixture(captured);
        defaults.SetRoute(1, Replacement);
        defaults.AutomaticRewardPickup = defaults.AutomaticRewardPickup;
        defaults.CombatDefaults = defaults.CombatDefaults;
        Check(defaults.CanUndoRouteRedraw, "unchanged prediction preferences preserve undo");
        defaults.AutomaticRewardPickup = !defaults.AutomaticRewardPickup;
        Check(!defaults.CanUndoRouteRedraw, "changed prediction preference expires undo");
    }

    private static void CompletedFactsAndMapReplacement(PredictorRun captured)
    {
        var run = Fixture(captured);
        Check(run.Submit(run.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted, "completed fixture commits rest");
        string checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        run.SetRoute(1, Replacement);
        Check(checkpoints == JsonSerializer.Serialize(run.Checkpoints) && run.Checkpoints[^1].CompletedNode!.NodeId == Current,
            "redraw preserves completed checkpoint facts");
        run.Rewind(0);
        Check(!run.CanUndoRouteRedraw && !run.UndoRouteRedraw(), "rewind expires redraw opportunity");

        var mapChange = Fixture(captured, goldenCompass: true);
        mapChange.SetRoute(1, Replacement);
        var beforeMapChange = mapChange.Working.Maps.Single().Map;
        mapChange.ApplyGoldenCompassObtained(mapChange.Working.Relics.Single().Id);
        Check(!mapChange.CanUndoRouteRedraw && mapChange.Routes.IsEmpty && mapChange.PlannedNodes.Any(p => p.NodeId == Current) &&
            mapChange.PlannedNodes.All(p => p.NodeId != Abandoned && p.NodeId != Rejoined) &&
            !ReferenceEquals(beforeMapChange, mapChange.Working.Maps.Single().Map),
            "Golden Compass map replacement discards route/future operations and retains factual active node intent");
    }
}
