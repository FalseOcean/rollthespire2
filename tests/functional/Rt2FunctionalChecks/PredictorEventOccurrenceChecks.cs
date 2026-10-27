using System.Collections.Immutable;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorEventOccurrenceChecks
{
    private static ModelKey E(string entry) => new("EVENT", entry);
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Event occurrence: " + message); }

    internal static void Run(PredictorRun captured)
    {
        NativeCursorAndHypothesis(captured);
        StaticCleaningAndUnsupported(captured);
        ExhaustionAndForcedIdentity(captured);
        BindingAndLegacy(captured);
        InheritedUnknownRoomReaders(captured);
        Console.WriteLine("RUNTIME_EVENT_OCCURRENCE_PASS native RoomSet cursor; conditional window; explicit hypothesis; static/visited cleaning; all-conditional/exhausted cycles; LanternKey; stale binding; pure observation; legacy declarations; inherited theft before unknown-room resolution; unrelated deck continuation");
    }

    private static PredictorRun Fixture(PredictorRun captured, int act, string[] entries,
        int cursor = 0, string[]? visited = null, bool lantern = false, PredictorContext? context = null)
    {
        context ??= captured.Context;
        var points = new[]
        {
            new MapPredictionPoint(new(3, 0), MapPointType.Monster, true, [new(3, 1)]),
            new MapPredictionPoint(new(3, 1), MapPointType.Unknown, true, [new(3, 2)]),
            new MapPredictionPoint(new(3, 2), MapPointType.Boss, true, [])
        };
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [],
            Position = new(act, 1, "event-occurrence:entry"),
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Monster, PredictorRoomKind.Monster, null, 1)],
            VisitedEvents = (visited ?? []).Select(E).ToImmutableArray(),
            UnknownOdds = new(0, -1, 0, 0),
            Acts = captured.Working.Acts.Select(a => a.Act == act ? a with
            { RawEvents = entries.Select(E).ToImmutableArray(), EventCursor = cursor } : a).ToImmutableArray(),
            GoldBudget = 0
        };
        if (lantern) state = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", "LANTERN_KEY"));
        var run = new PredictorRun(context, state);
        run.SetRoute(act, points.Select(p => p.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static PredictorEventOccurrenceObservation Observe(PredictorRun run)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request;
        var observation = run.ObserveEventOccurrence()!;
        Check(observation != null && state == JsonSerializer.Serialize(run.Working) &&
            checkpoints == JsonSerializer.Serialize(run.Checkpoints) && request == run.Request,
            "observation preserves concrete state, RNG, checkpoints and request");
        return observation!;
    }

    private static ChooseEventOccurrence Choose(PredictorEventOccurrenceObservation observation,
        PredictorEventOccurrenceCandidate candidate) => new(candidate.Event, candidate.QueueEvent,
            candidate.RawOrdinal, observation.Cursor, candidate.Cursor);

    private static (RunState Run, RoomSet Rooms, Player Player) Native(PredictorRun captured,
        int act, string[] entries, int cursor = 0, string[]? visited = null)
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var native = RunState.CreateForTest([player], seed: captured.Context.Seed);
        native.CurrentActIndex = act - 1;
        player.Gold = 0;
        foreach (string key in visited ?? []) native.AddVisitedEvent(ModelDb.GetById<EventModel>(new("EVENT", key)));
        var rooms = new RoomSet { eventsVisited = cursor };
        rooms.events.AddRange(entries.Select(key => ModelDb.GetById<EventModel>(new("EVENT", key))));
        return (native, rooms, player);
    }

    private static void NativeCursorAndHypothesis(PredictorRun captured)
    {
        string[] entries = ["SYMBIOTE", "MORPHIC_GROVE", "ZEN_WEAVER", "AROMA_OF_CHAOS", "SAPPHIRE_SEED"];
        var run = Fixture(captured, 2, entries, visited: ["SYMBIOTE"]);
        var observation = Observe(run);
        Check(observation.Cursor == 1 && observation.Candidates.Select(c => c.Event.Entry)
            .SequenceEqual(["MORPHIC_GROVE", "ZEN_WEAVER", "AROMA_OF_CHAOS"]) &&
            !observation.Candidates[0].IsUnconditional && observation.Candidates[^1].IsUnconditional &&
            !observation.ExhaustionFallbackAvailable, "conditional prefix ends at first unconditional inclusive");
        var native = Native(captured, 2, entries, visited: ["SYMBIOTE"]);
        string nativeRng = JsonSerializer.Serialize(native.Run.Rng.ToSerializable());
        native.Rooms.EnsureNextEventIsValid(native.Run);
        Check(native.Rooms.NextEvent.Id.Entry == "AROMA_OF_CHAOS", "native dynamic predicates choose the expected comparison event");
        var streams = run.Working.Streams;
        Check(run.Submit(run.Request!, Choose(observation, observation.Candidates[^1])) == PredictorInputResult.Accepted,
            "choose later actual event");
        native.Rooms.MarkVisited(RoomType.Event);
        Check(run.Working.Acts.Single(a => a.Act == 2).EventCursor == native.Rooms.eventsVisited &&
            run.Working.Visits[^1].Model == E("AROMA_OF_CHAOS") && streams.SequenceEqual(run.Working.Streams) &&
            nativeRng == JsonSerializer.Serialize(native.Run.Rng.ToSerializable()),
            "source and runtime advance skipped+selected cursors without RNG calls");
        var draft = run.CaptureNodeDraft()!;
        Check(draft.Inputs is [ChooseEventOccurrence], "active replay records the actual semantic occurrence input");
        var replay = new PredictorRun(run.Context, run.Checkpoints, run.Revision);
        replay.RestoreRoutes(run.Routes);
        replay.RestoreNodeDraft(draft);
        Check(JsonSerializer.Serialize(run.Working) == JsonSerializer.Serialize(replay.Working) &&
            run.EventOptions.SequenceEqual(replay.EventOptions), "appearance replay restores source cursor and actual event begin");

        var hypothesis = Fixture(captured, 2, entries, visited: ["SYMBIOTE"]);
        var hypothetical = Observe(hypothesis);
        Check(!native.Rooms.events[1].IsAllowed(native.Run) && hypothesis.Working.GoldBudget == 0 &&
            hypothesis.Submit(hypothesis.Request!, Choose(hypothetical, hypothetical.Candidates[0])) == PredictorInputResult.Accepted &&
            hypothesis.Working.Visits[^1].Model == E("MORPHIC_GROVE"),
            "appearance is an explicit player hypothesis, never auto-adjudicated by current gold/deck");
    }

    private static void StaticCleaningAndUnsupported(PredictorRun captured)
    {
        var authority = captured.Context.AncientAuthority!;
        var context = captured.Context with { AncientAuthority = authority with
        { EventAuthority = authority.EventAuthority with { CharacterCardPoolCount = 1, CharacterCardPoolCountExact = true } } };
        var run = Fixture(captured, 1, ["POTION_COURIER", "WAR_HISTORIAN_REPY", "COLORFUL_PHILOSOPHERS", "AROMA_OF_CHAOS"], context: context);
        var observation = Observe(run);
        Check(observation.Cursor == 3 && observation.Candidates is [{ Event.Entry: "AROMA_OF_CHAOS", RawOrdinal: 4, IsUnconditional: true }],
            "static cleaning removes act-ineligible, replacement-only and immutable unlock-impossible entries");
        var unknown = Observe(Fixture(captured, 2, ["CUSTOM_UNAUDITED_EVENT", "AROMA_OF_CHAOS", "SAPPHIRE_SEED"]));
        Check(unknown.Candidates.Length == 2 && !unknown.Candidates[0].IsUnconditional && unknown.Candidates[^1].IsUnconditional,
            "unsupported/custom classification never closes the window as unconditional");
        var noAuthority = captured.Context with { AncientAuthority = null };
        var conservative = Observe(Fixture(captured, 2, ["DENSE_VEGETATION", "AROMA_OF_CHAOS"], context: noAuthority));
        Check(!conservative.Candidates[0].IsUnconditional, "missing immutable mode authority retains a conditional candidate");
    }

    private static void ExhaustionAndForcedIdentity(PredictorRun captured)
    {
        string[] entries = ["MORPHIC_GROVE", "ZEN_WEAVER"];
        var run = Fixture(captured, 2, entries, 5);
        var observation = Observe(run);
        Check(observation.Candidates.Length == 3 && observation.ExhaustionFallbackAvailable &&
            observation.Candidates[^1] is { QueueEvent.Entry: "ZEN_WEAVER", Cursor: 7, RawOrdinal: 2, IsCycleFallback: true },
            "all-conditional queue exposes explicit full raw-cycle fallback");
        var native = Native(captured, 2, entries, 5);
        native.Rooms.EnsureNextEventIsValid(native.Run);
        string repeated = native.Rooms.NextEvent.Id.Entry;
        Check(run.Submit(run.Request!, Choose(observation, observation.Candidates[^1])) == PredictorInputResult.Accepted,
            "explicit exhausted-cycle choice");
        native.Rooms.MarkVisited(RoomType.Event);
        Check(run.Working.Acts.Single(a => a.Act == 2).EventCursor == native.Rooms.eventsVisited &&
            run.Working.Visits[^1].Model!.Value.Entry == repeated,
            "full-cycle cursor equals native exhausted repeat semantics");

        var allVisited = Fixture(captured, 2, ["AROMA_OF_CHAOS", "SAPPHIRE_SEED"], 4, ["AROMA_OF_CHAOS", "SAPPHIRE_SEED"]);
        var exhausted = Native(captured, 2, ["AROMA_OF_CHAOS", "SAPPHIRE_SEED"], 4, ["AROMA_OF_CHAOS", "SAPPHIRE_SEED"]);
        exhausted.Rooms.EnsureNextEventIsValid(exhausted.Run);
        exhausted.Rooms.MarkVisited(RoomType.Event);
        Check(allVisited.Phase == PredictorPhase.EventChoice && allVisited.ObserveEventOccurrence() == null &&
            allVisited.Working.Visits[^1].Model == E("AROMA_OF_CHAOS") &&
            allVisited.Working.Acts.Single(a => a.Act == 2).EventCursor == exhausted.Rooms.eventsVisited,
            "a fully statically exhausted queue keeps the source automatic repeat");

        var forced = Fixture(captured, 3, ["MORPHIC_GROVE", "AROMA_OF_CHAOS"], lantern: true);
        var options = Observe(forced);
        Check(options.Candidates.All(c => c.Event == E("WAR_HISTORIAN_REPY")) &&
            options.Candidates.Select(c => c.QueueEvent.Entry).SequenceEqual(["MORPHIC_GROVE", "AROMA_OF_CHAOS"]),
            "LanternKey displays actual replacement while preserving queue candidates");
        var nativeForced = Native(captured, 3, ["MORPHIC_GROVE", "AROMA_OF_CHAOS"]);
        nativeForced.Rooms.EnsureNextEventIsValid(nativeForced.Run);
        var actual = nativeForced.Run.CreateCard<LanternKey>(nativeForced.Player).ModifyNextEvent(nativeForced.Rooms.NextEvent);
        Check(forced.Submit(forced.Request!, Choose(options, options.Candidates[^1])) == PredictorInputResult.Accepted &&
            forced.Working.Visits[^1].Model!.Value.Entry == actual.Id.Entry &&
            forced.Working.VisitedEvents.Contains(E("WAR_HISTORIAN_REPY")) &&
            !forced.Working.VisitedEvents.Contains(E("AROMA_OF_CHAOS")),
            "selection follows native hook identity and visited-event ownership");
    }

    private static void BindingAndLegacy(PredictorRun captured)
    {
        var run = Fixture(captured, 2, ["MORPHIC_GROVE", "AROMA_OF_CHAOS", "SAPPHIRE_SEED"]);
        var observation = Observe(run);
        var choice = Choose(observation, observation.Candidates[^1]);
        string before = JsonSerializer.Serialize(run.Working);
        foreach (var wrong in new[] { choice with { Event = E("SAPPHIRE_SEED") },
            choice with { QueueEvent = E("SAPPHIRE_SEED") }, choice with { RawOrdinal = 1 },
            choice with { ExpectedCursor = 1 }, choice with { SelectedCursor = 4 } })
            Check(run.Submit(run.Request!, wrong) == PredictorInputResult.Invalid && before == JsonSerializer.Serialize(run.Working),
                "changed queue/identity/cursor preserves authored intent without replacement");
        var oldRequest = run.Request!;
        Check(run.Submit(run.Request!, new DeclareEventEligibility(E("MORPHIC_GROVE"), false)) == PredictorInputResult.Accepted &&
            run.EventEligibilityCandidate == E("AROMA_OF_CHAOS") &&
            run.Submit(oldRequest, choice) == PredictorInputResult.Stale &&
            run.Submit(run.Request!, choice) == PredictorInputResult.Invalid,
            "legacy rejection advances cursor and invalidates both request and candidate snapshot");
        Check(run.Submit(run.Request!, new DeclareEventEligibility(E("AROMA_OF_CHAOS"), true)) == PredictorInputResult.Accepted &&
            run.Working.Visits[^1].Model == E("AROMA_OF_CHAOS"), "existing one-by-one UI input remains functional");
    }

    private static void InheritedUnknownRoomReaders(PredictorRun captured)
    {
        PredictorRun CombatBeforeUnknown(string encounter, bool lantern)
        {
            var points = new[]
            {
                new MapPredictionPoint(new(3, 0), MapPointType.Ancient, true, [new(3, 1)]),
                new MapPredictionPoint(new(3, 1), MapPointType.Monster, true, [new(3, 2)]),
                new MapPredictionPoint(new(3, 2), MapPointType.Unknown, true, [new(3, 3)]),
                new MapPredictionPoint(new(3, 3), MapPointType.Boss, true, [])
            };
            var context = captured.Context with { NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns) };
            var state = captured.Working with
            {
                OpeningCompleted = true, Relics = [], PotionSlots = [null, null],
                Position = new(3, 1, "event-occurrence:combat-entry"),
                Maps = [new(3, captured.RouteMap(3) with { Points = points }, [points[0].Position])],
                Visits = [new(3, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
                VisitedEvents = [], UnknownOdds = new(1, -1, 0, 0),
                Acts = captured.Working.Acts.Select(a => a.Act == 3 ? a with
                {
                    RawEvents = [E("AROMA_OF_CHAOS")], EventCursor = 0,
                    NormalEncounters = [new("ENCOUNTER", encounter), new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")],
                    NormalCursor = 0
                } : a).ToImmutableArray()
            };
            if (lantern) state = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", "LANTERN_KEY"));
            var run = new PredictorRun(context, state);
            run.SetRoute(3, points.Select(p => p.Position).ToImmutableArray());
            run.AdvanceRoute();
            Check(run.Phase == PredictorPhase.CombatFacts, "route fixture enters an actual combat before the unknown point");
            return run;
        }

        PredictorRouteContinuation Preview(PredictorRun run)
        {
            string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
            var request = run.Request;
            var result = run.PreviewRouteContinuation([], maximumNodes: 2, scope: PredictorRoutePreviewScope.AllModeledNodes);
            Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
                request == run.Request, "unknown-room dependency projection preserves the actual active combat");
            return result;
        }

        // Native ThievingHopper's third steal-priority tier includes Quest
        // cards. Missing theft can therefore remove LanternKey legitimately;
        // an ordinary card-removal reward cannot remove this quest card.
        var theft = CombatBeforeUnknown("THIEVING_HOPPER_WEAK", true);
        var blocked = Preview(theft);
        Check(blocked.Nodes is [{ Room: PredictorRoomKind.Monster, Advanced: true }] &&
            blocked.PendingCauses.Any(c => c.Code == "StolenCardIdentity") &&
            blocked.PendingCauses.Any(c => c.Code == "UnknownRoomLanternKeyPending" && c.NodeId == "map:3:2:3:3"),
            "unresolved source-valid Lantern theft pauses before resolving or observing the next unknown room");

        void ResolveTheft(PredictorRun run, bool stealLantern)
        {
            long lanternId = run.Working.Deck.Single(id => run.Working.Card(id).Key.Entry == "LANTERN_KEY");
            var input = run.ResolveCombatDefaults(new()
            { StolenCards = new(stealLantern ? [new(lanternId, false)] : []) })!.Input!;
            Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted &&
                run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
                "concrete theft witness settles the actual combat and reward boundary");
            run.AdvanceRoute();
        }
        var lost = CombatBeforeUnknown("THIEVING_HOPPER_WEAK", true);
        ResolveTheft(lost, true);
        var retained = CombatBeforeUnknown("THIEVING_HOPPER_WEAK", true);
        ResolveTheft(retained, false);
        Check(lost.Working.Visits[^1].Room == PredictorRoomKind.Monster &&
            retained.Working.Visits[^1].Room == PredictorRoomKind.Event &&
            retained.ObserveEventOccurrence()!.Candidates.All(c => c.Event == E("WAR_HISTORIAN_REPY")),
            "the two real theft declarations demonstrate different unknown-room type and event identity");

        var unrelatedTheft = Preview(CombatBeforeUnknown("THIEVING_HOPPER_WEAK", false));
        Check(unrelatedTheft.Nodes.Length == 2 && unrelatedTheft.Nodes[1].Room == PredictorRoomKind.Monster &&
            unrelatedTheft.PendingCauses.Any(c => c.Code == "StolenCardIdentity") &&
            !unrelatedTheft.PendingCauses.Any(c => c.Code is "UnknownRoomLanternKeyPending" or "UnknownRoomDowsingPending"),
            "theft of unrelated ordinary cards leaves unknown-room resolution independent");
        var unpickedCard = Preview(CombatBeforeUnknown("FUZZY_WURM_CRAWLER_WEAK", true));
        Check(unpickedCard.Nodes.Length == 2 && unpickedCard.Nodes[1].Room == PredictorRoomKind.Event &&
            unpickedCard.Nodes[1].Event!.Occurrence!.Candidates.All(c => c.Event == E("WAR_HISTORIAN_REPY")) &&
            unpickedCard.PendingCauses.Any(c => c.Code == "PendingCardChoice") &&
            !unpickedCard.PendingCauses.Any(c => c.Code == "UnknownRoomLanternKeyPending"),
            "an unrelated unpicked normal card does not hide the guaranteed Lantern-forced event");
    }
}
