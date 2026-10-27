using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRoomKind { Opening, Ancient, Monster, Elite, Boss, Event, Shop, RestSite, Treasure, Victory }
internal sealed record PredictorRoute(int Act, ImmutableArray<MapPosition> Points);
internal sealed record PredictorActMap(int Act, MapPrediction Map, ImmutableArray<MapPosition> Visited);
internal sealed record PredictorVisit(int Act, MapPosition Point, MapPointType PointType, PredictorRoomKind Room,
    ModelKey? Model, int TotalFloor)
{
    public ImmutableArray<PredictorCombatKind> CombatKinds { get; init; } = [];
}
internal sealed record PredictorUnknownOdds(float Monster = .1f, float Elite = -1f, float Treasure = .02f, float Shop = .03f);
internal sealed record DeclareEventEligibility(ModelKey Event, bool Allowed) : PredictorInput;
internal sealed record ResolveCombat(PredictorCombatFacts Facts, bool? EventRanOutOfTime = null) : PredictorInput;
internal sealed record ContinueVictory : PredictorInput;

internal sealed partial class PredictorRun
{
    internal ImmutableArray<PredictorRoute> Routes { get; private set; } = [];
    // A saved route is the player's commitment. A later inventory or map
    // change can make its unvisited suffix illegal without corrupting the
    // checkpoint; the UI must ask for a replacement before travel.
    internal ImmutableArray<int> RouteActsNeedingReplan => Enumerable.Range(Context.Live?.OriginAct ?? 1, 4 - (Context.Live?.OriginAct ?? 1))
        .Where(act => !RouteActCompleted(act))
        .Where(act => Routes.FirstOrDefault(r => r.Act == act) is not { } route ||
            !IsLegalRoute(RouteMap(act), route.Points,
                Working.Maps.FirstOrDefault(m => m.Act == act)?.Visited ?? []))
        .ToImmutableArray();

    internal int? NextRouteAct
    {
        get
        {
            if (Working.Outcome != null) return null;
            for (int act = Context.Live?.OriginAct ?? 1; act <= 3; act++)
                if (!RouteActCompleted(act)) return act;
            return null;
        }
    }

    private bool RouteActCompleted(int act)
    {
        var map = Working.Maps.FirstOrDefault(m => m.Act == act);
        return map != null && !map.Visited.IsEmpty &&
            map.Map.Points.Any(p => p.Position == map.Visited[^1] &&
                p.Type == MapPointType.Boss && p.Children.Count == 0);
    }
    private PredictorCombatKind? _routeCombat;
    private PredictorCombatEntry? _combatEntry;
    private PredictorPosition? _routeNode;
    private ModelKey? _eligibilityCandidate;
    private int _eligibilityExamined;
    private bool _pendingFirstActHooks;
    private (MapPrediction Map, MapPosition? Previous, MapPosition Current)? _pendingEventEntry;
    internal ModelKey? EventEligibilityCandidate => _eligibilityCandidate;

    // Drawing a future route is pure preview. Fresh map hooks are applied only
    // when entering that Act; a changed map invalidates a stale commitment.
    internal MapPrediction RouteMap(int act, CancellationToken token = default)
    {
        RequireRouteSimulation();
        var actual = Working.Maps.FirstOrDefault(m => m.Act == act);
        if (actual != null) return actual.Map;
        return GenerateActualMap(act, token);
    }

    internal void SetRoute(int act, ImmutableArray<MapPosition> points)
    {
        var visited = Working.Maps.FirstOrDefault(m => m.Act == act)?.Visited ?? [];
        if (points.IsDefault || !IsLegalRoute(RouteMap(act), points, visited)) throw new ArgumentException("PredictorRouteInvalid");
        int i = Routes.FindIndex(r => r.Act == act);
        if (i >= 0 && Routes[i].Points.SequenceEqual(points)) return;
        var previous = i >= 0 ? new RouteRedrawUndo(act, Routes, PlannedNodes) : null;
        var route = new PredictorRoute(act, points);
        Routes = i < 0 ? Routes.Add(route) : Routes.SetItem(i, route);
        PlannedNodes = PlansForRoutes(PlannedNodes);
        _routeRedrawUndo = previous;
    }

    internal void RestoreRoutes(ImmutableArray<PredictorRoute> routes, bool allowSelectedPrefixes = false)
    {
        RequireRouteSimulation();
        if (routes.IsDefault) return;
        if (routes.Any(r => r is null) || routes.Select(r => r.Act).Distinct().Count() != routes.Length ||
            routes.Any(r => r.Act is < 1 or > 3 || r.Points.IsDefaultOrEmpty || r.Points.Length < (allowSelectedPrefixes ? 1 : 2) ||
                r.Points[0].Row != 0 || r.Points.Any(p => p.Column < 0 || p.Row < 0) ||
                r.Points.Skip(1).Where((p, i) => p.Row != r.Points[i].Row + 1).Any()))
            throw new InvalidDataException("PredictorStoredRouteMalformed");
        if (SameRoutes(Routes, routes)) return;
        Routes = routes;
        CompleteRouteRedrawUndo();
    }

    // Session-local prefix intent for detached observations. AdvanceRoute on
    // the concrete run still requires a complete route; only an explicitly
    // opted-in preview can execute this selected prefix.
    internal void SetRoutePreviewPrefix(int act, ImmutableArray<MapPosition> points)
    {
        var visited = Working.Maps.FirstOrDefault(m => m.Act == act)?.Visited ?? [];
        if (points.IsDefault || !IsLegalRoute(RouteMap(act), points, visited, requireBoss: false))
            throw new ArgumentException("PredictorRoutePreviewPrefixInvalid");
        int index = Routes.FindIndex(r => r.Act == act);
        if (index >= 0 && Routes[index].Points.SequenceEqual(points)) return;
        var previous = index >= 0 ? new RouteRedrawUndo(act, Routes, PlannedNodes) : null;
        var route = new PredictorRoute(act, points);
        Routes = index < 0 ? Routes.Add(route) : Routes.SetItem(index, route);
        PlannedNodes = PlansForRoutes(PlannedNodes);
        _routeRedrawUndo = previous;
    }

    internal void AdvanceRoute(CancellationToken token = default)
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete || Working.Outcome != null) throw new InvalidOperationException("PredictorRouteNotAtBoundary");
        ResetCombatWorkflow();
        _nodeEntryRoutes = Routes;
        _nodeReplayAvailable = true;
        var boundary = Working;
        try { AdvanceRouteCore(token); CompleteRouteRedrawUndo(); }
        catch
        {
            // Failed entry has no accepted player input. It cannot leave a
            // half-visited map node or consume a fresh-map/room RNG draw.
            Working = boundary; Rewards = []; _inputs = []; GenerationStages = [];
            ResetEvent(); ResetObtain(); ResetShop(); ResetRest(); ResetAncient(); ResetRouteTransaction();
            Phase = PredictorPhase.Complete;
            _requestId++;
            throw;
        }
    }

    private void AdvanceRouteCore(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Phase != PredictorPhase.Complete || Working.Outcome != null) throw new InvalidOperationException("PredictorRouteNotAtBoundary");
        _inputs = []; Rewards = []; GenerationStages = [];
        int act = Working.Maps.IsEmpty ? 1 : Working.Maps[^1].Act;
        var map = Working.Maps.FirstOrDefault(m => m.Act == act);
        if (map != null && map.Visited.Length > 0 && map.Map.Points.Single(p => p.Position == map.Visited[^1]).Children.Count == 0)
        {
            if (act == 3)
            {
                Working = Working with { CurrentRoomIsCombat = false, Position = new(3, Working.Position.Floor, "victory") };
                _inputs = []; Rewards = [];
                Phase = PredictorPhase.Victory;
                Accept(new("TheArchitectContinueAvailable", 0, [], null, null));
                return;
            }
            act++;
            map = null;
        }
        bool enteringAct = map == null;
        if (map == null)
        {
            map = new(act, GenerateActualMap(act, token, applyStartingPoint: false), []);
            Working = Working with { Maps = Working.Maps.Add(map), UnknownOdds = new(
                Elite: Context.HasRunModifier("DEADLY_EVENTS") ? .1f : -1f) };
            ApplySpoilsMapAfterMapGenerated(act, map.Map);
            ApplyFurCoatAfterActualMapGenerated(act, map.Map);
            map = map with { Map = ApplyStartingPoint(map.Map) };
            Working = Working with { Maps = Working.Maps.SetItem(Working.Maps.Length - 1, map) };
            // Vanilla enters a MapRoom, then fires AfterActEntered before the
            // player chooses a map point. Act 1 with Neow instead enters its
            // opening Ancient first and fires this hook after room entry.
            if (act > 1 || !Working.StartedWithNeow)
            {
                // CardPileCmd.Add stamps RunState.TotalFloor, which has not
                // advanced for the first map point of this act yet.
                Working = Working with { Position = new(act, Working.Position.Floor, $"map:{act}:entry") };
                AfterRuntimeActEntered();
            }
        }
        var route = Routes.SingleOrDefault(r => r.Act == act) ?? throw new InvalidOperationException("PredictorRouteNotSelected");
        if (!IsLegalRoute(map.Map, route.Points, map.Visited, requireBoss: !_preview.Settings.AcceptsPrefixes))
            throw new InvalidOperationException("PredictorRouteNeedsReplan:Act" + act);
        int ordinal = map.Visited.IsEmpty ? 0 : route.Points.IndexOf(map.Visited[^1]) + 1;
        if (!map.Visited.IsEmpty && ordinal == 0) throw new InvalidOperationException("PredictorRouteMissingCurrentPoint");
        if (ordinal >= route.Points.Length) throw new InvalidOperationException("PredictorRouteExhaustedBeforeBoss");
        var point = map.Map.Points.Single(p => p.Position == route.Points[ordinal]);
        if (!CanTravel(map.Map, ordinal > 0 ? map.Visited[^1] : null, point.Position))
            throw new InvalidOperationException("PredictorRouteEdgeNoLongerLegal");
        // Node entry checkpoint is the preceding completed boundary. All draws
        // below live in Working and are discarded if that node is rewound.
        int floor = Working.Position.Floor + 1;
        var position = new PredictorPosition(act, floor, $"map:{act}:{point.Position.Row}:{point.Position.Column}:{floor}");
        var room = ResolveRoom(point, map);
        int mapIndex = Working.Maps.FindIndex(m => m.Act == act);
        Working = Working with { Position = position, CurrentRoomIsCombat = false, Maps = Working.Maps.SetItem(mapIndex, map with { Visited = map.Visited.Add(point.Position) }),
            Visits = Working.Visits.Add(new(act, point.Position, point.Type, room, null, position.Floor)) };
        _routeNode = position;
        // An Ancient is an EventRoom at the base of the room stack. Vanilla
        // MarkRoomVisited advances the ordinary event cursor for this too.
        if (room is PredictorRoomKind.Opening or PredictorRoomKind.Ancient)
        {
            int actIndex = Working.Acts.FindIndex(a => a.Act == act);
            var actState = Working.Acts[actIndex];
            Working = Working with { Acts = Working.Acts.SetItem(actIndex, actState with { EventCursor = actState.EventCursor + 1 }) };
        }
        _pendingFirstActHooks = enteringAct && act == 1 && Working.StartedWithNeow;
        BeforeRouteRoomEntered(point);
        bool travelApplied = false;
        switch (room)
        {
            case PredictorRoomKind.Opening:
                EnterOpening();
                Working = Working with { Position = position };
                break;
            case PredictorRoomKind.Monster:
            case PredictorRoomKind.Elite:
            case PredictorRoomKind.Boss:
                AfterRouteRoomEntered(room);
                _routeCombat = room switch { PredictorRoomKind.Monster => PredictorCombatKind.Monster, PredictorRoomKind.Elite => PredictorCombatKind.Elite, _ => PredictorCombatKind.Boss };
                _combatEntry = PrepareCombatEntry(_routeCombat.Value, act, position.Floor, token);
                Working = Working with { Position = position,
                    Visits = Working.Visits.SetItem(Working.Visits.Length - 1, Working.Visits[^1] with { Model = _combatEntry.Encounter.Key }) };
                Phase = PredictorPhase.CombatFacts;
                _requestId++;
                break;
            case PredictorRoomKind.RestSite: EnterRestSite(act, position.Floor); Working = Working with { Position = position }; break;
            case PredictorRoomKind.Shop:
                EnterShop(position);
                AfterRouteRoomEntered(room);
                // Winged Boots observes the entry before Parasol's deferred
                // purchases. A Boots instance bought here cannot pay for the
                // jump that reached this merchant.
                ApplyTravelAfterRoomEntered(map.Map, ordinal > 0 ? map.Visited[^1] : null, point.Position);
                travelApplied = true;
                if (PredictorSettlementEffects.Has(Working, "LORDS_PARASOL")) BeginShopParasol(token);
                break;
            case PredictorRoomKind.Event:
                _pendingEventEntry = (map.Map, ordinal > 0 ? map.Visited[^1] : null, point.Position);
                BeginEventOccurrence();
                return;
            case PredictorRoomKind.Treasure: AfterRouteRoomEntered(room); EnterTreasure(position, token); break;
            case PredictorRoomKind.Ancient: EnterAncient(position, token); break;
            default: throw new InvalidOperationException("PredictorRoomDispatchInvalid");
        }
        if (room is PredictorRoomKind.Opening or PredictorRoomKind.Ancient or PredictorRoomKind.Event or PredictorRoomKind.RestSite)
            AfterRouteRoomEntered(room);
        if (!travelApplied)
            ApplyTravelAfterRoomEntered(map.Map, ordinal > 0 ? map.Visited[^1] : null, point.Position);
        if (_pendingFirstActHooks) { _pendingFirstActHooks = false; AfterRuntimeActEntered(); }
        PauseForTerminalEntry();
    }

    private void BeforeRouteRoomEntered(MapPredictionPoint point)
    {
        if (point.Type != MapPointType.Unknown) return;
        foreach (long id in Working.Deck)
        {
            var card = Working.Card(id);
            if (card.Key.Entry != "DOWSING") continue;
            card = card with { RoomsEntered = card.RoomsEntered + 1 };
            Working = Working.WithCard(card);
            if (card.RoomsEntered >= 5) Working = PredictorCardChanges.TransformTo(Context, Working, id, new("CARD", "ABUNDANCE"));
        }
    }

    private void AfterRouteRoomEntered(PredictorRoomKind room)
    {
        // Parasol's normal-game callback yields before purchases. Its concrete
        // shop continuation starts after this synchronous entry traversal.
        foreach (var relic in Working.Relics.Where(r => !r.Melted))
        {
            if (relic.Key.Entry == "LAVA_LAMP")
            {
                int index = Working.Relics.FindIndex(r => r.Id == relic.Id);
                Working = Working with { Relics = Working.Relics.SetItem(index, relic with { TookDamageThisCombat = false }) };
            }
            if (relic.Key.Entry == "MAW_BANK" && !relic.MawBankSpent)
                Working = PredictorSettlementEffects.TakeGold(Working, 12);
            int heal = relic.Key.Entry switch
            {
                "ETERNAL_FEATHER" when room == PredictorRoomKind.RestSite => Working.Deck.Length / 5 * 3,
                "MEAL_TICKET" when room == PredictorRoomKind.Shop => 15,
                "PLANISPHERE" when Working.Visits.LastOrDefault()?.PointType == MapPointType.Unknown => 5,
                _ => 0
            };
            if (heal > 0) Working = Working with { CurrentHealthEffects = Working.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 0, heal)) };
        }
    }

    private PredictorRoomKind ResolveRoom(MapPredictionPoint point, PredictorActMap map)
    {
        var trace = _preview.Partial?.BeginRoomResolution(this, point, map);
        PredictorRoomKind Done(PredictorRoomKind room, string branch) => trace?.Complete(room, branch) ?? room;
        if (point.Type == MapPointType.Ancient) return Done(map.Act == 1 ? PredictorRoomKind.Opening : PredictorRoomKind.Ancient, "Ancient map point");
        if (point.Type != MapPointType.Unknown) return Done(point.Type switch
        {
            MapPointType.Monster => PredictorRoomKind.Monster, MapPointType.Elite => PredictorRoomKind.Elite,
            MapPointType.Boss => PredictorRoomKind.Boss, MapPointType.Shop => PredictorRoomKind.Shop,
            MapPointType.RestSite => PredictorRoomKind.RestSite, MapPointType.Treasure => PredictorRoomKind.Treasure,
            _ => throw new InvalidOperationException("PredictorUnassignedMapPoint")
        }, "typed map point");
        int unknowns = RoomHistory.Count(v => v.PointType == MapPointType.Unknown);
        trace?.ReadTutorial(unknowns);
        if (Context.NumberOfRuns == 0 && unknowns <= 2) return Done(unknowns < 2 ? PredictorRoomKind.Event : PredictorRoomKind.Monster, "tutorial fixed room");
        bool forcedEvent = UnknownRoomMustBeEvent(map.Act);
        trace?.ReadForced(forcedEvent);
        if (forcedEvent)
        {
            trace?.ReadCursor();
            var forced = Working.Streams.Single(s => s.Stream == PredictorStream.UnknownMapPoint).Restore();
            _ = forced.NextFloat();
            Working = Working.WithRng(PredictorStream.UnknownMapPoint, forced);
            return Done(PredictorRoomKind.Event, "forced Event");
        }
        bool blockShop = Working.Visits.LastOrDefault()?.Room == PredictorRoomKind.Shop ||
            point.Children.Count > 0 && point.Children.All(c => map.Map.Points.Single(p => p.Position == c).Type == MapPointType.Shop);
        bool blockMonster = PredictorSettlementEffects.Has(Working, "JUZU_BRACELET");
        var odds = Working.UnknownOdds;
        trace?.ReadRandom(blockShop, blockMonster, odds);
        trace?.ReadCursor();
        var rng = Working.Streams.Single(s => s.Stream == PredictorStream.UnknownMapPoint).Restore();
        float roll = rng.NextFloat();
        float cumulative = 0;
        var selected = PredictorRoomKind.Event;
        foreach (var entry in new[] { (PredictorRoomKind.Monster, odds.Monster, blockMonster), (PredictorRoomKind.Elite, odds.Elite, false),
            (PredictorRoomKind.Treasure, odds.Treasure, false), (PredictorRoomKind.Shop, odds.Shop, blockShop) })
        {
            if (entry.Item3 || entry.Item2 < 0) continue;
            cumulative += entry.Item2;
            if (roll <= cumulative) { selected = entry.Item1; break; }
        }
        Working = Working.WithRng(PredictorStream.UnknownMapPoint, rng) with { UnknownOdds = new(
            selected == PredictorRoomKind.Monster ? .1f : odds.Monster + (blockMonster ? 0 : .1f),
            selected == PredictorRoomKind.Elite ? (Context.HasRunModifier("DEADLY_EVENTS") ? .1f : -1f) :
                odds.Elite + (Context.HasRunModifier("DEADLY_EVENTS") ? .1f : -1f),
            selected == PredictorRoomKind.Treasure ? .02f : odds.Treasure +
                (Context.HasRunModifier("DEADLY_EVENTS") ? .04f : .02f),
            selected == PredictorRoomKind.Shop ? .03f : odds.Shop + (blockShop ? 0 : .03f)) };
        return Done(selected, "random room");
    }

    private void BeginEventOccurrence()
    {
        var act = Working.Acts.Single(a => a.Act == Working.Position.Act);
        if (act.RawEvents.IsEmpty) throw new InvalidOperationException("PredictorEventQueueEmpty");
        _eligibilityExamined = 0;
        _eligibilityCandidate = act.RawEvents[act.EventCursor % act.RawEvents.Length];
        Phase = PredictorPhase.EventEligibility;
        _requestId++;
        AdvanceKnownEventEligibility();
    }

    private void AdvanceKnownEventEligibility()
    {
        int index = Working.Acts.FindIndex(a => a.Act == Working.Position.Act);
        var act = Working.Acts[index];
        while (_eligibilityExamined < act.RawEvents.Length && _eligibilityCandidate is { } candidate &&
            (EventOccurrenceStaticEligibility(candidate).ShouldReject || Working.VisitedEvents.Contains(candidate)))
        {
            string reason = EventOccurrenceStaticEligibility(candidate).ShouldReject ? "Static" : "Visited";
            act = act with { EventCursor = act.EventCursor + 1 };
            Working = Working with { Acts = Working.Acts.SetItem(index, act) };
            _eligibilityExamined++;
            _eligibilityCandidate = act.RawEvents[act.EventCursor % act.RawEvents.Length];
            Accept(new("EventEligibilityAutoRejected:" + reason + ":" + candidate.Entry, 0, [], null, null));
        }
        if (_eligibilityExamined >= act.RawEvents.Length && !_preview.RequiresEventChoice)
            FinishEventOccurrence(_eligibilityCandidate!.Value);
    }

    private PredictorInputResult SubmitEventEligibility(DeclareEventEligibility input)
    {
        if (_eligibilityCandidate != input.Event) return PredictorInputResult.Invalid;
        // IsAllowed is consulted while searching the queue, but RoomSet may
        // still use NextEvent after a full exhausted cycle. Reject an
        // impossible positive declaration here; do not prevent that vanilla
        // fallback from opening the event's normal initial page.
        if (input.Allowed && !EventActEligibility(input.Event, Working.Position.Act))
            return PredictorInputResult.Invalid;
        int index = Working.Acts.FindIndex(a => a.Act == Working.Position.Act);
        var act = Working.Acts[index];
        if (!input.Allowed)
        {
            act = act with { EventCursor = act.EventCursor + 1 };
            Working = Working with { Acts = Working.Acts.SetItem(index, act) };
            _eligibilityExamined++;
            _eligibilityCandidate = act.RawEvents[act.EventCursor % act.RawEvents.Length];
            Accept(new("EventEligibilityRejected:" + input.Event.Entry, 0, [], null, null));
            AdvanceKnownEventEligibility();
            return PredictorInputResult.Accepted;
        }
        FinishEventOccurrence(input.Event);
        return PredictorInputResult.Accepted;
    }

    private void FinishEventOccurrence(ModelKey selected)
    {
        int index = Working.Acts.FindIndex(a => a.Act == Working.Position.Act);
        var act = Working.Acts[index];
        // RoomSet.EnsureNextEventIsValid stops after one complete cycle and
        // opens NextEvent even when its IsAllowed predicate remains false.
        if (LanternKeyReplacesEvent(Working.Position.Act)) selected = new("EVENT", "WAR_HISTORIAN_REPY");
        Working = Working with { VisitedEvents = Working.VisitedEvents.Add(selected),
            Acts = Working.Acts.SetItem(index, act with { EventCursor = act.EventCursor + 1 }),
            Visits = Working.Visits.SetItem(Working.Visits.Length - 1, Working.Visits[^1] with { Model = selected }) };
        var position = Working.Position;
        _eligibilityCandidate = null;
        Phase = PredictorPhase.Complete;
        var eligibilityInputs = _inputs;
        if (!TryBeginDrawingBridgeSummary(position, selected)) BeginEvent(position, selected);
        _inputs = eligibilityInputs;
        Accept(new((_eligibilityExamined >= act.RawEvents.Length ? "EventQueueCycleFallback:" : "EventEligibilityAccepted:") + selected.Entry, 0, [], null, null));
        if (_pendingEventEntry is { } entry)
        {
            _pendingEventEntry = null;
            AfterRouteRoomEntered(PredictorRoomKind.Event);
            ApplyTravelAfterRoomEntered(entry.Map, entry.Previous, entry.Current);
            if (_pendingFirstActHooks) { _pendingFirstActHooks = false; AfterRuntimeActEntered(); }
            PauseForTerminalEntry();
        }
    }

    private static bool EventActEligibility(ModelKey key, int act) => key.Entry switch
    {
        "SYMBIOTE" or "POTION_COURIER" or "RANWID_THE_ELDER" or "RELIC_TRADER" or
        "FAKE_MERCHANT" or "CRYSTAL_SPHERE" => act > 1,
        "TEA_MASTER" or "ROOM_FULL_OF_CHEESE" or "BRAIN_LEECH" => act < 3,
        "STONE_OF_ALL_TIME" or "DOLL_ROOM" or "WELCOME_TO_WONGOS" => act == 2,
        "THE_LEGENDS_WERE_TRUE" => act == 1,
        _ => true
    };

    private PredictorInputResult SubmitRouteCombat(ResolveCombat input, CancellationToken token)
    {
        if (_combatEntry is not { } entry || _routeNode is not { } position) return PredictorInputResult.Invalid;
        if (input.Facts == null || input.Facts.Potions == null) return PredictorInputResult.Invalid;
        if (!PredictorCombatRewardFacts.ValidGremlin(entry.Encounter, input.Facts.GremlinGold)) return PredictorInputResult.Invalid;
        if (!PredictorCombatEnemyFacts.Valid(entry.Encounter, input.Facts.EnemyCreations)) return PredictorInputResult.Invalid;
        if (entry.Encounter.CanStealCards != (input.Facts.StolenCards != null)) return PredictorInputResult.Invalid;
        if (Working.Relics.Any(r => !r.Melted && r.Key.Entry == "BONE_TEA" && r.CombatsLeft > 0) && !input.Facts.FirstPlayerTurnOccurred.HasValue)
            return PredictorInputResult.Invalid;
        Phase = PredictorPhase.Complete;
        try { SettleCombat(entry, 1f, input.Facts, token, preserveInputs: true); }
        catch (ArgumentException) { Phase = PredictorPhase.CombatFacts; return PredictorInputResult.Invalid; }
        catch { Phase = PredictorPhase.CombatFacts; throw; }
        Working = Working with { Position = position };
        _routeCombat = null;
        _combatEntry = null;
        return PredictorInputResult.Accepted;
    }

    private void ResetRouteTransaction()
    {
        _routeCombat = null; _combatEntry = null; _routeNode = null;
        _eligibilityCandidate = null; _eligibilityExamined = 0;
        _pendingFirstActHooks = false;
        _pendingEventEntry = null; _terminalResumePhase = null;
        TreasureRelic = null; _treasureAllowed = false; _treasureObtaining = false;
    }
}
