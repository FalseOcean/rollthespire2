using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunMap;

internal static class PredictorLiveCaptureChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string why)
    { _checks++; if (!value) throw new InvalidOperationException("LIVE_CAPTURE " + why); }
    private static string NativeFacts(RunState run) => JsonSerializer.Serialize(new
    {
        Player = run.Players[0].ToSerializable(), Rng = run.Rng.ToSerializable(), Odds = run.Odds.ToSerializable(),
        Acts = run.Acts.Select(a => a.ToSave()).ToArray(), History = run.MapPointHistory,
        Visited = run.VisitedMapCoords, Map = InRunMapSnapshotForChecks(run), run.CurrentActIndex
    }, new JsonSerializerOptions { IncludeFields = true });
    private static object InRunMapSnapshotForChecks(RunState run) => run.Map.GetAllMapPoints()
        .Append(run.Map.StartingMapPoint).Append(run.Map.BossMapPoint).DistinctBy(p => p.coord)
        .Select(p => new { p.coord, p.PointType, Children = p.Children.Select(c => c.coord).ToArray() }).ToArray();

    internal static void Run(PredictorRun baseline)
    {
        _checks = 0;
        if (int.TryParse(Environment.GetEnvironmentVariable("RT2_LIVE_CAPTURE_COST_SAMPLES"), out int costSamples))
        {
            CaptureCost(baseline, Math.Clamp(costSamples, 1, 30));
            return;
        }
        NodeEntryHookBinding();
        SameRunHistoryIdentity();
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", Members)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", Members)!;
        var priorState = stateProperty.GetValue(manager);
        var priorAscension = ascensionProperty.GetValue(manager);
        ulong? priorLocal = LocalContext.NetId;
        try
        {
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var native = RunState.CreateForTest([player], baseline.Working.Acts
                .Select(a => ModelDb.GetById<ActModel>(new(a.Key.Category, a.Key.Entry))).ToArray(),
                seed: baseline.Context.Seed, ascensionLevel: baseline.Context.Ascension);
            LocalContext.NetId = player.NetId;
            stateProperty.SetValue(manager, native);
            ascensionProperty.SetValue(manager, new AscensionManager(native.AscensionLevel));
            native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), native.Rng.UpFront);
            player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
            manager.GenerateRooms();
            native.ExtraFields.StartedWithNeow = true;
            native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, RoomType.Event, new("EVENT", "MORPHIC_GROVE"));
            native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, RoomType.Monster, native.Act.BossEncounter.Id);
            native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Treasure, RoomType.Treasure, null);
            native.CurrentActIndex = 1;
            native.Map = new GoldenPathActMap(native);
            native.AddVisitedMapCoord(native.Map.StartingMapPoint.coord);
            native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, native.Act.Ancient.Id);
            player.Gold = 413;
            player.ExtraFields.CardShopRemovalsUsed = 2;
            player.ExtraFields.WongoPoints = 71;
            var dowsing = (Dowsing)ModelDb.Card<Dowsing>().ToMutable();
            dowsing.RoomsEntered = 4;
            native.AddCard(dowsing, player); player.Deck.AddInternal(dowsing);
            var algorithm = (GeneticAlgorithm)ModelDb.Card<GeneticAlgorithm>().ToMutable();
            algorithm.IncreasedBlock = 29;
            native.AddCard(algorithm, player); player.Deck.AddInternal(algorithm);
            var spoils = (SpoilsMap)ModelDb.Card<SpoilsMap>().ToMutable();
            spoils.SpoilsActIndex = 1;
            native.AddCard(spoils, player); player.Deck.AddInternal(spoils);
            typeof(SpoilsMap).GetProperty("SpoilsCoord", Members)!.SetValue(spoils, native.Map.BossMapPoint.coord);
            var silver = (SilverCrucible)ModelDb.Relic<SilverCrucible>().ToMutable();
            silver.TimesUsed = 2; silver.TreasureRoomsEntered = 1; player.AddRelicInternal(silver);
            var boots = (WingedBoots)ModelDb.Relic<WingedBoots>().ToMutable();
            boots.TimesUsed = 2; player.AddRelicInternal(boots);
            var candy = (LastingCandy)ModelDb.Relic<LastingCandy>().ToMutable();
            candy.CombatRewardsSeen = 5; player.AddRelicInternal(candy);
            var fish = (FishingRod)ModelDb.Relic<FishingRod>().ToMutable();
            fish.CombatsSeen = 7; player.AddRelicInternal(fish);
            var wing = (PaelsWing)ModelDb.Relic<PaelsWing>().ToMutable();
            wing.RewardsSacrificed = 3; player.AddRelicInternal(wing);
            var silken = (SilkenTress)ModelDb.Relic<SilkenTress>().ToMutable();
            typeof(SilkenTress).GetField("_isUsed", Members)!.SetValue(silken, true); player.AddRelicInternal(silken);
            var potion = ModelDb.Potion<BlockPotion>().ToMutable(); player.AddPotionInternal(potion, 1);
            var roomSet = (RoomSet)typeof(ActModel).GetField("_rooms", Members)!.GetValue(native.Act)!;
            roomSet.normalEncountersVisited = 2; roomSet.eliteEncountersVisited = 1; roomSet.eventsVisited = 3;
            foreach (var rng in new[] { player.PlayerRng.Rewards, player.PlayerRng.Shops, player.PlayerRng.Transformations,
                native.Rng.Niche, native.Rng.UpFront, native.Rng.UnknownMapPoint,
                native.Rng.TreasureRoomRelics, native.Rng.CombatPotionGeneration }) rng.NextInt(123);
            native.Odds.UnknownMapPoint.MonsterOdds = .4f;
            string before = NativeFacts(native);
            string fingerprint = PredictorLiveRunCapture.Fingerprint(native, true);
            var captured = PredictorLiveRunCapture.Capture(native, baseline.Context.GameVersion, true);
            var state = captured.Run.Working;
            Check(NativeFacts(native) == before && PredictorLiveRunCapture.Fingerprint(native, true) == fingerprint,
                "capture leaves serialized native inventory, queues, history, maps, odds and all RNG unchanged");
            Check(captured.Fingerprint == fingerprint && captured.CanLeaveCurrentNode, "departure boundary and stable fingerprint");
            Check(state.Position.Act == 2 && state.Position.Floor == native.TotalFloor && captured.Run.NextRouteAct == 2,
                "actual Act2 and total floor retained without past map reconstruction");
            Check(state.GoldBudget == 413 && state.CardShopRemovalsUsed == 2 && state.WongoPoints == 71, "actual economy copied");
            Check(state.Deck.Length == player.Deck.Cards.Count && state.Cards.Single(c => c.Key.Entry == "DOWSING").RoomsEntered == 4 &&
                state.Cards.Single(c => c.Key.Entry == "GENETIC_ALGORITHM").IncreasedBlock == 29,
                "modified actual deck and persistent card fields, never starting deck replacement");
            Check(state.Cards.Single(c => c.Key.Entry == "SPOILS_MAP") is { SpoilsActIndex: 1, SpoilsCoord: { } } &&
                state.Cards.Single(c => c.Key.Entry == "SPOILS_MAP").SpoilsCoord == new MapPosition(native.Map.BossMapPoint.coord.col, native.Map.BossMapPoint.coord.row),
                "actual Spoils attached coordinate copied without regenerated assignment");
            Check(state.Relics.Single(r => r.Key.Entry == "SILVER_CRUCIBLE") is { RewardUpgradeUses: 2, TreasureRoomsEntered: 1 } &&
                state.Relics.Single(r => r.Key.Entry == "WINGED_BOOTS").TimesUsed == 2 &&
                state.Relics.Single(r => r.Key.Entry == "LASTING_CANDY").CombatRewardsSeen == 5 &&
                state.Relics.Single(r => r.Key.Entry == "FISHING_ROD").FishingRodCombatsSeen == 7 &&
                state.Relics.Single(r => r.Key.Entry == "PAELS_WING").RewardsSacrificed == 3 &&
                state.Relics.Single(r => r.Key.Entry == "SILKEN_TRESS").SilkenUsed, "live relic continuation counters copied");
            Check(state.PotionSlots[1]?.Key.Entry == "BLOCK_POTION" && state.PotionSlots[0] == null, "potion slot identities retained");
            Check(state.UnknownOdds.Monster == .4f && state.Acts[1] is { NormalCursor: 2, EliteCursor: 1, EventCursor: 3 }, "actual room queues/cursors and unknown odds");
            Check(state.Acts[1].NormalEncounters.Select(k => k.Entry).SequenceEqual(roomSet.normalEncounters.Select(e => e.Id.Entry)),
                "actual generated encounter order copied");
            Check(captured.Run.Context.Live!.PreviousVisits.Length == 3 && state.Visits.Length == 1 && state.Maps.Length == 1,
                "prior room facts retained without fabricated previous coordinates/maps");
            var session = new InRunMapPredictionSession();
            var emptyDrafts = new Dictionary<int, PredictorMapRouteDraft>();
            session.Update(native, baseline.Context.GameVersion, false, emptyDrafts);
            Check(session.WaitingForBoundary && session.Snapshot == null && session.Forecast == null,
                "session waits for initial departure boundary without capturing midpoint");
            session.Update(native, baseline.Context.GameVersion, true, emptyDrafts);
            Check(!session.WaitingForBoundary && session.Snapshot != null && session.Forecast is { Nodes.IsEmpty: true },
                "departure captures actual boundary with no inferred undrawn path");
            long initialRevision = session.Revision;
            var initialSnapshot = session.Snapshot;
            session.Update(native, baseline.Context.GameVersion, true, emptyDrafts);
            Check(session.Revision == initialRevision && ReferenceEquals(session.Snapshot, initialSnapshot),
                "unchanged departure and draft do not recapture/recompute");
            var map = state.Maps[0].Map;
            var draft = new PredictorMapRouteDraft(map, state.Maps[0].Visited[^1]);
            var prefix = map.Points.Where(p => p.Position.Row is 1 or 2).OrderBy(p => p.Position.Row).Select(p => p.Position).ToArray();
            draft.ApplyStroke(prefix, PredictorMapStrokeMode.Draw);
            var drafts = new Dictionary<int, PredictorMapRouteDraft> { [1] = draft };
            session.Update(native, baseline.Context.GameVersion, true, drafts);
            Check(session.Nodes.Count >= 1 && session.Nodes.Keys.All(k => k.ActIndex == 1 && prefix.Contains(k.Point)),
                "session forecast uses selected ready prefix only");
            var firstNode = session.Nodes.Values.OrderBy(n => n.Position.Floor).First();
            PredictorMapNodeIdentity.TryParse(firstNode.Position.NodeId, out var firstIdentity);
            Check(session.SetPlan(1, firstIdentity.Point, new(firstNode.Position.NodeId) { LeaveRewards = true }),
                "future node semantic plan accepted");
            var retainedForecast = session.Forecast;
            var retainedPlans = session.Plans;
            session.Update(native, baseline.Context.GameVersion, false, drafts);
            Check(session.WaitingForBoundary && ReferenceEquals(retainedForecast, session.Forecast) && session.Plans.SequenceEqual(retainedPlans),
                "midpoint retains prior forecast and authored plans");
            session.Update(native, baseline.Context.GameVersion, true, drafts);
            var gap = new PredictorMapRouteDraft(map, state.Maps[0].Visited[^1]);
            gap.ApplyStroke([prefix[1]], PredictorMapStrokeMode.Draw);
            session.Update(native, baseline.Context.GameVersion, true, new Dictionary<int, PredictorMapRouteDraft> { [1] = gap });
            Check(gap.ReadyPrefix.IsEmpty && session.Nodes.IsEmpty, "gap is not filled or projected silently");
            session.ForgetRedrawUndo();
            session.Update(native, baseline.Context.GameVersion, true, drafts);
            Check(session.GetPlan(1, firstIdentity.Point)?.LeaveRewards == true && session.HistoryCount == 1,
                "returning after redraw undo expires restores same-run authored choice");
            session.Update(native, baseline.Context.GameVersion, true, new Dictionary<int, PredictorMapRouteDraft>());
            session.ForgetRedrawUndo();
            session.Update(native, baseline.Context.GameVersion, true, new Dictionary<int, PredictorMapRouteDraft> { [1] = gap });
            session.ForgetRedrawUndo();
            session.Update(native, baseline.Context.GameVersion, true, drafts);
            Check(session.GetPlan(1, firstIdentity.Point)?.LeaveRewards == true,
                "history survives multiple unrelated redraws rather than only the last undo");
            var currentActDrafts = new Dictionary<int, PredictorMapRouteDraft>(drafts) { [2] = draft };
            session.Update(native, baseline.Context.GameVersion, true, currentActDrafts);
            Check(session.Nodes.Keys.All(key => key.ActIndex == native.CurrentActIndex),
                "future-Act authored input cannot extend current actual Act scope");
            WaitingSessionEdits(native, baseline, session, draft, drafts, roomSet);
            foreach (var stream in state.Streams)
            {
                Rng source = stream.Stream switch
                {
                    PredictorStream.Rewards => player.PlayerRng.Rewards, PredictorStream.Shops => player.PlayerRng.Shops,
                    PredictorStream.Transformations => player.PlayerRng.Transformations, PredictorStream.UpFront => native.Rng.UpFront,
                    PredictorStream.Niche => native.Rng.Niche, PredictorStream.UnknownMapPoint => native.Rng.UnknownMapPoint,
                    PredictorStream.TreasureRoomRelics => native.Rng.TreasureRoomRelics, _ => native.Rng.CombatPotionGeneration
                };
                Check(stream == CrystalSphereLiveCapture.RngState(stream.Stream, source), "actual RNG state " + stream.Stream);
            }
            long dowsingId = state.Cards.Single(c => c.Key.Entry == "DOWSING").Id;
            var inserted = ModelDb.Card<Anger>().ToMutable(); native.AddCard(inserted, player); player.Deck.AddInternal(inserted, 0);
            var refreshed = PredictorLiveRunCapture.Capture(native, baseline.Context.GameVersion, true);
            Check(refreshed.Fingerprint != captured.Fingerprint && refreshed.Run.Working.Card(dowsingId).Key.Entry == "DOWSING",
                "real deck insertion changes fingerprint while preserving old persistent target identity");
            var waiting = PredictorLiveRunCapture.Capture(native, baseline.Context.GameVersion, false);
            Check(!waiting.CanLeaveCurrentNode && waiting.PendingCauses.Any(c => c.Code == "LiveCurrentNodePending"), "midpoint capture explicitly pending");
            Check(waiting.Run.PreviewRouteContinuation([], scope: PredictorRoutePreviewScope.AllModeledNodes).Nodes.IsEmpty,
                "pending native current node cannot be treated as completed travel boundary");
            typeof(RelicGrabBag).GetField("_originalRelics", Members)!.SetValue(native.SharedRelicGrabBag, null);
            typeof(RelicGrabBag).GetField("_refreshAllowed", Members)!.SetValue(native.SharedRelicGrabBag, false);
            var loaded = PredictorLiveRunCapture.Capture(native, baseline.Context.GameVersion, true);
            Check(!loaded.Run.Working.SharedBag.HasOriginalCatalog && !loaded.Run.Working.SharedBag.RefreshAllowed,
                "restored bag missing original catalog remains missing, with its real refresh policy");
            var route = loaded.Run.Working.Maps[0].Map.Points.OrderBy(p => p.Position.Row).Select(p => p.Position).ToImmutableArray();
            loaded.Run.SetRoute(2, route);
            before = NativeFacts(native);
            loaded.Run.AdvanceRoute();
            Check(loaded.Run.Working.Position.Floor == native.TotalFloor + 1 && loaded.Run.Working.Position.Act == 2,
                "future node starts from live total floor rather than current-act visit count");
            Check(NativeFacts(native) == before, "detached route entry leaves real game state/RNG untouched");
            NodeEntrySessionLifecycle(native, baseline, roomSet);
            EventExclusionSessionLifecycle(baseline);
            OpeningEntrySessionLifecycle(baseline);
            NodeEntryFeaturePreferenceLifecycle(baseline);
            BingBongSkipState(native, baseline.Context.GameVersion);
            Console.WriteLine($"RUNTIME_LIVE_CAPTURE_PASS checks={_checks};native=true;actualInventory=true;actualRng=true;actualQueues=true;noGameMutation=true");
        }
        finally
        {
            stateProperty.SetValue(manager, priorState); ascensionProperty.SetValue(manager, priorAscension);
            LocalContext.NetId = priorLocal;
        }
    }

    private sealed class SkipSetFixture(object? value)
    {
        public readonly object? _cardsToSkip = value;
    }

    private static void BingBongSkipState(RunState native, string version)
    {
        var player = native.Players[0];
        var relic = (BingBong)ModelDb.Relic<BingBong>().ToMutable();
        var field = typeof(BingBong).GetField("_cardsToSkip", Members)!;
        player.AddRelicInternal(relic);
        try
        {
            field.SetValue(relic, null);
            string before = NativeFacts(native);
            string emptyFingerprint = PredictorLiveRunCapture.Fingerprint(native, true);
            var empty = PredictorLiveRunCapture.Capture(native, version, true);
            Check(empty.Run.Working.Relics.Single(r => r.Id == PredictorLiveRunCapture.Id(relic)).BingBongCardsToSkip.IsEmpty &&
                field.GetValue(relic) == null && NativeFacts(native) == before &&
                PredictorLiveRunCapture.Fingerprint(native, true) == emptyFingerprint,
                "BingBong null backing set captures empty without invoking initializing getter or changing native state");

            var card = player.Deck.Cards[0];
            var skip = new HashSet<CardModel> { card };
            field.SetValue(relic, skip);
            string skipFingerprint = PredictorLiveRunCapture.Fingerprint(native, true);
            Check(skipFingerprint != emptyFingerprint, "BingBong skip membership participates in native purity fingerprint");
            var populated = PredictorLiveRunCapture.Capture(native, version, true);
            Check(populated.Run.Working.Relics.Single(r => r.Id == PredictorLiveRunCapture.Id(relic)).BingBongCardsToSkip
                .SequenceEqual([PredictorLiveRunCapture.Id(card)]) && ReferenceEquals(field.GetValue(relic), skip) &&
                skip.SetEquals([card]) && NativeFacts(native) == before &&
                PredictorLiveRunCapture.Fingerprint(native, true) == skipFingerprint,
                "BingBong nonempty skip state is copied with identity and leaves actual native backing state unchanged");

            Check(PredictorLiveRunCapture.BingBongSkipCards(new SkipSetFixture(null)) == null,
                "shared BingBong field adapter preserves legitimate null");
            try
            {
                PredictorLiveRunCapture.BingBongSkipCards(new SkipSetFixture(new List<CardModel> { card }));
                throw new InvalidOperationException("BingBong field type mismatch silently became empty");
            }
            catch (InvalidCastException error)
            {
                Check(error.Message.Contains("_cardsToSkip", StringComparison.Ordinal) &&
                    error.Message.Contains(typeof(List<CardModel>).FullName!, StringComparison.Ordinal),
                    "unsupported nonnull BingBong state fails with field and actual type at shared capture/fingerprint adapter");
            }
        }
        finally { player.RemoveRelicInternal(relic, silent: true); }
    }

    private static void WaitingSessionEdits(RunState native, PredictorRun baseline,
        InRunMapPredictionSession session, PredictorMapRouteDraft draft,
        Dictionary<int, PredictorMapRouteDraft> drafts, RoomSet roomSet)
    {
        var snapshot = session.Snapshot!;
        var first = session.Nodes.Values.OrderBy(n => n.Position.Floor).First();
        PredictorMapNodeIdentity.TryParse(first.Position.NodeId, out var identity);
        session.Update(native, baseline.Context.GameVersion, false, drafts);
        var beforeEdit = session.Forecast;
        Check(session.SetPlan(1, identity.Point, new(first.Position.NodeId) { LeaveRewards = false }) &&
            session.WaitingForBoundary && !ReferenceEquals(beforeEdit, session.Forecast) &&
            ReferenceEquals(snapshot, session.Snapshot), "waiting semantic edit recomputes purely from old completed snapshot");
        beforeEdit = session.Forecast;
        Check(session.RemovePlan(1, identity.Point) && !ReferenceEquals(beforeEdit, session.Forecast) &&
            ReferenceEquals(snapshot, session.Snapshot), "waiting semantic plan removal also recomputes detached suffix");
        Check(session.SetPlan(1, identity.Point, new(first.Position.NodeId) { LeaveRewards = true }), "restore future plan before native travel observation");
        // Controlled native fixture enters a room, but does not declare its
        // effects settled. The UI draft immediately rebases to that coordinate.
        native.AddVisitedMapCoord(new(identity.Point.Column, identity.Point.Row));
        native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster, RoomType.Monster,
            roomSet.normalEncounters[roomSet.normalEncountersVisited % roomSet.normalEncounters.Count].Id);
        roomSet.normalEncountersVisited++;
        native.Players[0].Gold += 17;
        draft.Rebase(draft.Map, identity.Point);
        string before = NativeFacts(native);
        session.Update(native, baseline.Context.GameVersion, false, drafts);
        Check(ReferenceEquals(snapshot, session.Snapshot) && snapshot.Run.Working.Maps[0].Visited.Length == 1 &&
            snapshot.Run.Working.Position.Floor < native.TotalFloor && snapshot.Run.Working.GoldBudget != native.Players[0].Gold,
            "entered native coordinate and midpoint inventory do not promote old snapshot to a completed node");
        Check(session.Nodes.ContainsKey((1, identity.Point)) && session.GetPlan(1, identity.Point)?.LeaveRewards == true &&
            session.Nodes[(1, identity.Point)].Position.Floor == snapshot.Run.Working.Position.Floor + 1,
            "old snapshot to entered node bridge is replayed and retains its semantic plan");
        Check(NativeFacts(native) == before, "waiting bridge observation leaves native state and RNG untouched");
        session.Update(native, baseline.Context.GameVersion, false, new Dictionary<int, PredictorMapRouteDraft>());
        Check(session.Nodes.ContainsKey((1, identity.Point)) && session.GetPlan(1, identity.Point)?.LeaveRewards == true &&
            session.WaitingForBoundary && ReferenceEquals(snapshot, session.Snapshot),
            "entered bridge cannot be erased by absent future intent while waiting");
        session.Update(native, baseline.Context.GameVersion, false, drafts);
        var row3 = draft.Map.Points.Single(p => p.Position.Row == 3).Position;
        draft.ApplyStroke([row3], PredictorMapStrokeMode.Draw);
        beforeEdit = session.Forecast;
        session.Update(native, baseline.Context.GameVersion, false, drafts);
        Check(session.WaitingForBoundary && !ReferenceEquals(beforeEdit, session.Forecast) && ReferenceEquals(snapshot, session.Snapshot),
            "waiting route edit triggers old-snapshot recomputation without capturing midpoint");
        var waitingGap = new PredictorMapRouteDraft(draft.Map, identity.Point);
        waitingGap.ApplyStroke([row3], PredictorMapStrokeMode.Draw);
        session.Update(native, baseline.Context.GameVersion, false, new Dictionary<int, PredictorMapRouteDraft> { [1] = waitingGap });
        Check(session.Nodes.Count > 0 && session.Nodes.Keys.All(k => k.Point.Row <= identity.Point.Row),
            "waiting gap retains actual entered bridge without inventing the missing future row");
        var cold = new InRunMapPredictionSession();
        cold.Update(native, baseline.Context.GameVersion, false, drafts);
        Check(cold.Snapshot == null && cold.Forecast == null && cold.Nodes.IsEmpty && cold.Plans.IsEmpty,
            "drawings before first departure are retained as intent without invented snapshot/forecast");
        session.Update(native, baseline.Context.GameVersion, true, drafts);
        Check(!session.WaitingForBoundary && !ReferenceEquals(snapshot, session.Snapshot) &&
            session.Snapshot!.Run.Working.Position.Floor == native.TotalFloor && session.GetPlan(1, identity.Point) == null,
            "real departure replaces old basis and trims the now-completed node plan");
        Check(session.HistoryCount > 0 && session.Plans.All(plan =>
            PredictorMapNodeIdentity.TryParse(plan.NodeId, out var planned) && planned.Floor > native.TotalFloor),
            "actual outcomes replace past simulation while past choices remain inactive history");
        var nextNode = session.Nodes.Values.FirstOrDefault();
        if (nextNode != null && PredictorMapNodeIdentity.TryParse(nextNode.Position.NodeId, out var nextIdentity))
            session.SetPlan(1, nextIdentity.Point, new(nextNode.Position.NodeId) { LeaveRewards = true });
        var otherPlayer = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var otherRun = RunState.CreateForTest([otherPlayer], baseline.Working.Acts
            .Select(a => ModelDb.GetById<ActModel>(new(a.Key.Category, a.Key.Entry))).ToArray(),
            seed: baseline.Context.Seed, ascensionLevel: baseline.Context.Ascension);
        session.Update(otherRun, baseline.Context.GameVersion, false, new Dictionary<int, PredictorMapRouteDraft>());
        Check(session.WaitingForBoundary && session.Snapshot == null && session.Forecast == null &&
            session.Nodes.IsEmpty && session.Plans.IsEmpty && session.HistoryCount == 0, "new Run clears prior Run forecast and choices before its first departure");
    }

    private static void NodeEntrySessionLifecycle(RunState native, PredictorRun baseline, RoomSet rooms)
    {
        string version = baseline.Context.GameVersion;
        // Keep this lifecycle check independent of unknown-room branch content.
        native.Map.GetAllMapPoints().Single(p => p.coord.row == 2).PointType = MegaCrit.Sts2.Core.Map.MapPointType.Monster;
        var captured = PredictorLiveRunCapture.Capture(native, version, true);
        var map = captured.Run.Working.Maps[0].Map;
        var points = map.Points.Where(p => p.Position.Row is 2 or 3 or 4)
            .OrderBy(p => p.Position.Row).Select(p => p.Position).ToArray();
        var draft = new PredictorMapRouteDraft(map, captured.Run.Working.Maps[0].Visited[^1]);
        draft.ApplyStroke(points, PredictorMapStrokeMode.Draw);
        var drafts = new Dictionary<int, PredictorMapRouteDraft> { [1] = draft };
        var session = new InRunMapPredictionSession();
        session.Update(native, version, true, drafts);
        var current = session.Nodes[(1, points[0])];
        var future = session.Nodes[(1, points[1])];
        var currentPlan = new PredictorPlannedNode(current.Position.NodeId) { LeaveRewards = true };
        var futurePlan = new PredictorPlannedNode(future.Position.NodeId) { LeaveRewards = true };
        Check(session.SetPlan(1, points[0], currentPlan) && session.SetPlan(1, points[1], futurePlan),
            "entry fixture authors current and future semantic choices");
        string before = NativeFacts(native);
        var entryWork = new PredictorOperationCapture.CaptureTiming();
        var entry = PredictorLiveRunCapture.CaptureBeforeNodeEntry(native, version, new(points[0].Column, points[0].Row), entryWork)!;
        Check(entryWork.CatalogLevelReads > 0 && entryWork.CatalogLevelReuses == 0 && entryWork.PoolReads > 0 && entryWork.PoolReuses > 0,
            "first true entry reads native definitions and shares actual pool copies only within that capture");
        Check(NativeFacts(native) == before && entry.EntryPoint == points[0] && entry.EntryActIndex == 1 &&
            PredictorLiveRunCapture.CurrentNodeEntry(native, version) == null,
            "before-entry capture is read-only and cannot claim its not-yet-entered target");
        EnterFixture(points[0]);
        Check(PredictorLiveRunCapture.CurrentNodeEntry(native, version) == null,
            "mutated coordinate and history alone cannot establish successful native entry");
        var entryTask = new TaskCompletionSource();
        PredictorLiveRunCapture.RecordNodeEntryTask(native, new(points[0].Column, points[0].Row), entryTask.Task);
        Check(PredictorLiveRunCapture.CurrentNodeEntry(native, version) == null,
            "pending native entry task cannot publish the current-node premise");
        entryTask.SetResult();
        native.Players[0].Gold += 41;
        native.Rng.Niche.NextInt(123);
        draft.Rebase(map, points[0]);
        before = NativeFacts(native);
        session.Update(native, version, false, drafts);
        Check(ReferenceEquals(entry, session.Snapshot) && session.IsCurrentNodeReplay && !session.WaitingForBoundary &&
            session.Nodes.ContainsKey((1, points[0])) && session.GetPlan(1, points[0]) == currentPlan &&
            session.GetPlan(1, points[1]) == futurePlan,
            "observed entry supersedes old basis, replays current node and preserves future intent");
        Check(entry.Run.Working.GoldBudget != native.Players[0].Gold &&
            entry.Run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) != CrystalSphereLiveCapture.RngState(PredictorStream.Niche, native.Rng.Niche),
            "mid-node inventory and RNG are not recaptured or passed off as entry");
        var reopened = new InRunMapPredictionSession();
        reopened.Update(native, version, false, new Dictionary<int, PredictorMapRouteDraft>());
        Check(ReferenceEquals(entry, reopened.Snapshot) && reopened.Nodes.ContainsKey((1, points[0])) &&
            reopened.Nodes.Count == 1 && !reopened.WaitingForBoundary,
            "opening prediction after entry needs no prior map session or drawn future route");
        Check(PredictorLiveRunCapture.CaptureBeforeNodeEntry(native, version, new(points[0].Column, points[0].Row)) == null &&
            ReferenceEquals(entry, PredictorLiveRunCapture.CurrentNodeEntry(native, version)),
            "duplicate native coordinate entry neither recaptures midpoint nor destroys entry basis");
        session.Update(native, version, true, drafts);
        Check(ReferenceEquals(entry, session.Snapshot) && session.GetPlan(1, points[0]) == currentPlan,
            "travel availability alone cannot settle shop or pending reward choices");
        Check(session.SetPlan(1, points[0], currentPlan with { LeaveRewards = false }) &&
            session.RemovePlan(1, points[0]) && ReferenceEquals(entry, session.Snapshot),
            "current-node edit and removal recompute from the stable entry premise");
        Check(NativeFacts(native) == before, "current-node replay, edits and duplicate observation leave native state unchanged");
        var nextWork = new PredictorOperationCapture.CaptureTiming();
        var nextEntry = PredictorLiveRunCapture.CaptureBeforeNodeEntry(native, version, new(points[1].Column, points[1].Row), nextWork)!;
        Check(nextWork.CatalogLevelReads == 0 && nextWork.CatalogLevelReuses == entryWork.CatalogLevelReads && nextWork.PoolReads > 0,
            "next true entry reuses only same-reference native canonical definitions while rereading actual pools");
        var independentlyCaptured = PredictorLiveRunCapture.Capture(native, version, true);
        Check(JsonSerializer.Serialize(nextEntry.Run.Context) == JsonSerializer.Serialize(independentlyCaptured.Run.Context) &&
            JsonSerializer.Serialize(nextEntry.Run.Working) == JsonSerializer.Serialize(independentlyCaptured.Run.Working),
            "definition reuse produces the same context and dynamic entry state as a fresh independent capture");
        Console.WriteLine($"NODE_ENTRY_CAPTURE_REUSE firstLevels={entryWork.CatalogLevelReads};nextLevels={nextWork.CatalogLevelReads};reusedLevels={nextWork.CatalogLevelReuses};firstPools={entryWork.PoolReads};nextPools={nextWork.PoolReads};trueEntryRetained=true");
        Check(nextEntry.Run.Working.GoldBudget == native.Players[0].Gold &&
            nextEntry.Run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) == CrystalSphereLiveCapture.RngState(PredictorStream.Niche, native.Rng.Niche),
            "next real departure captures actual outcomes and RNG rather than earlier projected results");
        EnterFixture(points[1]);
        PredictorLiveRunCapture.RecordNodeEntryTask(native, new(points[1].Column, points[1].Row), Task.CompletedTask);
        draft.Rebase(map, points[1]);
        before = NativeFacts(native);
        session.Update(native, version, false, drafts);
        Check(ReferenceEquals(nextEntry, session.Snapshot) && session.GetPlan(1, points[0]) == null &&
            session.GetPlan(1, points[1]) == futurePlan && session.Nodes.ContainsKey((1, points[1])),
            "actual progression retires departed plans and preserves the now-current future plan");
        Check(PredictorLiveRunCapture.CurrentNodeEntry(native, "unverified-other-version") == null && NativeFacts(native) == before,
            "entry lookup preserves version identity and actual progression replay is read-only");

        void EnterFixture(MapPosition point)
        {
            Check(native.AddVisitedMapCoord(new(point.Column, point.Row)), "controlled native fixture enters next coordinate once");
            native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster, RoomType.Monster,
                rooms.normalEncounters[rooms.normalEncountersVisited % rooms.normalEncounters.Count].Id);
            rooms.normalEncountersVisited++;
        }
    }

    private static RunState EntryFixture(PredictorRun baseline, int act)
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var native = RunState.CreateForTest([player], baseline.Working.Acts
            .Select(a => ModelDb.GetById<ActModel>(new(a.Key.Category, a.Key.Entry))).ToArray(),
            seed: baseline.Context.Seed, ascensionLevel: baseline.Context.Ascension);
        typeof(RunManager).GetProperty("State", Members)!.SetValue(RunManager.Instance, native);
        LocalContext.NetId = player.NetId;
        native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), native.Rng.UpFront);
        player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
        RunManager.Instance.GenerateRooms();
        native.ExtraFields.StartedWithNeow = true;
        native.CurrentActIndex = act - 1;
        native.Map = new GoldenPathActMap(native);
        return native;
    }

    private static void EventExclusionSessionLifecycle(PredictorRun baseline)
    {
        var native = EntryFixture(baseline, 2);
        native.AddVisitedMapCoord(native.Map.StartingMapPoint.coord);
        native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, native.Act.Ancient.Id);
        native.Players[0].Gold = 300;
        var rooms = (RoomSet)typeof(ActModel).GetField("_rooms", Members)!.GetValue(native.Act)!;
        rooms.events.Clear();
        rooms.events.AddRange(new EventModel[] { ModelDb.Event<MorphicGrove>(), ModelDb.Event<ZenWeaver>(), ModelDb.Event<AromaOfChaos>() });
        rooms.eventsVisited = 0;
        native.Odds.UnknownMapPoint.MonsterOdds = 0;
        native.Odds.UnknownMapPoint.TreasureOdds = 0;
        native.Odds.UnknownMapPoint.ShopOdds = 0;
        native.Map.GetAllMapPoints().Single(p => p.coord.row == 1).PointType = MegaCrit.Sts2.Core.Map.MapPointType.Unknown;
        var map = PredictorLiveRunCapture.Map(native);
        var point = map.Points.Single(p => p.Position.Row == 1).Position;
        var draft = new PredictorMapRouteDraft(map, new(native.Map.StartingMapPoint.coord.col, native.Map.StartingMapPoint.coord.row));
        draft.ApplyStroke([point], PredictorMapStrokeMode.Draw);
        var drafts = new Dictionary<int, PredictorMapRouteDraft> { [1] = draft };
        var session = new InRunMapPredictionSession();
        string before = NativeFacts(native);
        session.Update(native, baseline.Context.GameVersion, true, drafts);
        PredictorRouteNodeKnowledge Node() => session.Nodes[(1, point)];
        PredictorPlannedNode Option(string key)
        {
            var node = Node();
            return new(node.Position.NodeId) { EventOccurrenceExclusions = session.GetPlan(1, point)?.EventOccurrenceExclusions ?? [],
                Event = new(node.Event!.Event!.Value) { Actions = [new(PredictorEventActionKind.Option)
                    { ExpectedOption = node.Event.EntryOptions.Single(option => option.Key == key).Value! }] } };
        }
        bool Exclude(PredictorEventOccurrenceExclusion exclusion, bool excluded)
        {
            var plan = session.GetPlan(1, point)!;
            var values = plan.EventOccurrenceExclusions.Remove(exclusion);
            return session.SetPlan(1, point, plan with { EventOccurrenceExclusions = excluded ? values.Add(exclusion) : values,
                Event = plan.Event is { } authored ? authored with { Occurrence = null } : null });
        }
        Check(Node().Event?.Event?.Entry == "MORPHIC_GROVE", "conditional queue starts with automatic A");
        Check(session.SetPlan(1, point, Option("LONER")), "author A operations before appearance edits");
        var candidate = Node().Event!.SelectedOccurrence!;
        var excludeA = new PredictorEventOccurrenceExclusion(candidate.QueueEvent, candidate.Cursor);
        Check(Exclude(excludeA, true) && Node().Event?.Event?.Entry == "ZEN_WEAVER", "exclude A selects automatic B");
        Check(session.SetPlan(1, point, Option("BREATHING_TECHNIQUES")), "author B's independent operations");
        Check(Exclude(excludeA, false) && Node().Event?.Event?.Entry == "MORPHIC_GROVE" &&
            session.GetPlan(1, point)!.Event!.Actions.Single().ExpectedOption!.Key == "LONER" &&
            session.GetPlan(1, point)!.EventOccurrenceExclusions.IsEmpty,
            "restore A converges and rebinds A actions without resurrecting old exclusions");
        Check(Exclude(excludeA, true) && Node().Event?.Event?.Entry == "ZEN_WEAVER" &&
            session.GetPlan(1, point)!.Event!.Actions.Single().ExpectedOption!.Key == "BREATHING_TECHNIQUES",
            "excluding A again rebinds B actions without oscillating to A");
        session.Update(native, baseline.Context.GameVersion, true, new Dictionary<int, PredictorMapRouteDraft>());
        session.ForgetRedrawUndo();
        session.Update(native, baseline.Context.GameVersion, true, drafts);
        Check(Node().Event?.Event?.Entry == "ZEN_WEAVER" &&
            session.GetPlan(1, point)!.EventOccurrenceExclusions.SequenceEqual([excludeA]),
            "route return after undo expires keeps latest exclusions and event-specific actions");
        // An ordinary malformed edit is rejected at its producer. The retained
        // node remains available to an explicit corrected edit, with no reset.
        try
        {
            session.SetPlan(1, point, session.GetPlan(1, point)! with { EventOccurrenceExclusions = [excludeA, excludeA] });
            Check(false, "malformed exclusion edit must fail");
        }
        catch (InvalidDataException ex) { Check(ex.Message == "PredictorPlannedNodesMalformed", "failure retains its actual cause"); }
        // Inject one detached source failure after valid observations exist.
        // Recompute must retain those editing locations, so correcting intent
        // can recompute normally once the source premise is trustworthy again.
        var snapshot = session.Snapshot!;
        var brokenMap = map with { Points = map.Points.Select(value => value.Position == point
            ? value with { Type = RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster } : value).ToArray() };
        var brokenState = snapshot.Run.Working with
            { OpeningCompleted = false, Maps = [snapshot.Run.Working.Maps[0] with { Map = brokenMap }] };
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), Members)!
            .SetValue(session, snapshot with { Run = new PredictorRun(snapshot.Run.Context, brokenState) });
        InvalidOperationException? failure = null;
        try { session.Recompute(); }
        catch (InvalidOperationException ex) { failure = ex; }
        finally { typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), Members)!.SetValue(session, snapshot); }
        Check(failure?.Message == "PredictorCombatBoundaryInvalid",
            "recompute preserves its source failure; actual=" + (failure?.ToString() ?? "no exception"));
        Check(Exclude(excludeA, false) && Node().Event?.Event?.Entry == "MORPHIC_GROVE" && NativeFacts(native) == before,
            "explicit correction after rejected edit and recompute failure restores A without mutating native state/RNG");
    }

    private static void OpeningEntrySessionLifecycle(PredictorRun baseline)
    {
        var native = EntryFixture(baseline, 1);
        string version = baseline.Context.GameVersion;
        var start = native.Map.StartingMapPoint.coord;
        string before = NativeFacts(native);
        var entry = PredictorLiveRunCapture.CaptureBeforeNodeEntry(native, version, start)!;
        Check(entry.Run.Working is { OpeningCompleted: false, OpeningOptions.Length: 3 } && NativeFacts(native) == before,
            "first Neow entry carries genuine generated offers without advancing native RNG");
        native.AddVisitedMapCoord(start);
        native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, ModelDb.Event<Neow>().Id);
        var room = new EventRoom(ModelDb.Event<Neow>());
        native.PushRoom(room);
        var neow = (Neow)ModelDb.Event<Neow>().ToMutable();
        neow.BeginEvent(native.Players[0], null, false).GetAwaiter().GetResult();
        Check(neow.CurrentOptions.Select(option => PredictorLiveRunCapture.Key(option.Relic!))
            .SequenceEqual(entry.Run.Working.OpeningOptions), "copied first-entry offers match native Neow generation exactly");
        PredictorLiveRunCapture.RecordNodeEntryTask(native, start, Task.CompletedTask);
        var session = new InRunMapPredictionSession();
        session.Update(native, version, false, new Dictionary<int, PredictorMapRouteDraft>());
        Check(session.IsCurrentNodeReplay && session.Nodes.Single().Value.Room == PredictorRoomKind.Opening,
            "unfinished Neow retains a usable true opening-entry premise");
        // Native option generation and Chosen settle the actual relic. This
        // model-only host mirrors EventRoom's one-player completion notification;
        // it does not start room assets, animations or write a game save.
        neow.StateChanged += model => { if (model.IsFinished) room.MarkPreFinished(); };
        var option = neow.CurrentOptions.First(value => value.Relic is { HasUponPickupEffect: false });
        // RelicCmd's local-player branch writes discovery progress. Keep native
        // obtain/settlement but omit that unrelated account-side display branch.
        LocalContext.NetId = null;
        try { option.Chosen().GetAwaiter().GetResult(); }
        finally { LocalContext.NetId = native.Players[0].NetId; }
        Check(neow.IsFinished && room.IsPreFinished && native.Players[0].Relics.Contains(option.Relic!),
            "natural Neow option completion publishes the actual obtained relic and completion fact");
        before = NativeFacts(native);
        var map = PredictorLiveRunCapture.Map(native);
        var point = map.Points.Single(p => p.Position.Row == 1).Position;
        var draft = new PredictorMapRouteDraft(map, new(start.col, start.row));
        draft.ApplyStroke([point], PredictorMapStrokeMode.Draw);
        var drafts = new Dictionary<int, PredictorMapRouteDraft> { [0] = draft };
        session.Update(native, version, true, drafts);
        Check(PredictorLiveRunCapture.CurrentNodeEntry(native, version) == null && !session.IsCurrentNodeReplay &&
            session.Snapshot!.Run.Working.OpeningCompleted && session.Snapshot.Run.Working.Position.Floor == native.TotalFloor &&
            session.Snapshot.Run.Working.Relics.Any(relic => relic.Key == PredictorLiveRunCapture.Key(option.Relic!)) &&
            session.Nodes.Keys.All(location => location.Point != new MapPosition(start.col, start.row)),
            "completed Neow retires old opening replay and predicts the future from its actual settled state");
        var reopened = new InRunMapPredictionSession();
        reopened.Update(native, version, true, drafts);
        Check(reopened.Nodes.ContainsKey((0, point)) && reopened.Snapshot!.Run.Working.OpeningCompleted && NativeFacts(native) == before,
            "opening the map again after natural Neow completion leaves inventory and RNG unchanged");
        var save = new SerializableRun
        {
            Players = native.Players.Select(player => player.ToSerializable()).ToList(),
            Acts = native.Acts.Select(act => act.ToSave()).ToList(), Modifiers = [], GameMode = native.GameMode,
            CurrentActIndex = native.CurrentActIndex, Ascension = native.AscensionLevel,
            SerializableRng = native.Rng.ToSerializable(), SerializableOdds = native.Odds.ToSerializable(),
            SerializableSharedRelicGrabBag = native.SharedRelicGrabBag.ToSerializable(),
            VisitedMapCoords = native.VisitedMapCoords.ToList(), EventsSeen = native.VisitedEventIds.ToList(),
            MapPointHistory = native.MapPointHistory.Select(history => history.ToList()).ToList(),
            ExtraFields = native.ExtraFields.ToSerializable()
        };
        var loaded = RunState.FromSerializable(save);
        loaded.Map = new SavedActMap(SerializableActMap.FromActMap(native.Map));
        loaded.PushRoom(new EventRoom(room.ToSerializable()));
        typeof(RunManager).GetProperty("State", Members)!.SetValue(RunManager.Instance, loaded);
        var restored = new InRunMapPredictionSession();
        restored.Update(loaded, version, true, drafts);
        Check(!restored.IsCurrentNodeReplay && restored.Snapshot!.Run.Working.OpeningCompleted &&
            restored.Nodes.ContainsKey((0, point)) && restored.Snapshot.Run.Working.Streams.SequenceEqual(session.Snapshot!.Run.Working.Streams),
            "saved/reloaded completed Neow continues from actual native streams without inventing an entry receipt");
        // The no-Neow opening is already complete before its first monster.
        var withoutNeow = EntryFixture(baseline, 1);
        withoutNeow.ExtraFields.StartedWithNeow = false;
        withoutNeow.Map.StartingMapPoint.PointType = MegaCrit.Sts2.Core.Map.MapPointType.Monster;
        Check(PredictorLiveRunCapture.CaptureBeforeNodeEntry(withoutNeow, version, withoutNeow.Map.StartingMapPoint.coord)!
            .Run.Working is { OpeningCompleted: true, OpeningOptions.IsEmpty: true },
            "first non-Neow native entry does not replay an absent opening");
    }

    private static void SameRunHistoryIdentity()
    {
        var history = new InRunMapPlanHistory();
        var eventKey = new RolltheSpire2.Core.Identity.ModelKey("EVENT", "COLORFUL_PHILOSOPHERS");
        var otherEvent = new RolltheSpire2.Core.Identity.ModelKey("EVENT", "DENSE_VEGETATION");
        PredictorRouteNodeKnowledge Node(RolltheSpire2.Core.Identity.ModelKey key, int cursor, string id = "map:2:3:1:19")
        {
            var candidate = new PredictorEventOccurrenceCandidate(key, key, cursor + 1, cursor,
                RolltheSpire2.Core.Events.EventCandidateEligibilityKind.StaticEligible, true, false);
            return new(new(2, 19, id), PredictorRoomKind.Event, key, PredictorPhase.EventChoice, [], [], [], [], [])
            {
                Event = new(key, [], [], [], [], [], [])
                {
                    SelectedOccurrence = candidate,
                    Occurrence = new(id, 2, cursor, [candidate], false)
                }
            };
        }
        var originalNode = Node(eventKey, 3);
        var target = new PredictorPlannedCard(472, new("CARD", "STRIKE_IRONCLAD"), 0);
        var original = history.Remember(new(originalNode.Position.NodeId)
        {
            Event = new(eventKey) { Actions = [new(PredictorEventActionKind.Cards) { DeckCards = [target] }] }
        }, originalNode);
        Check(original.Event!.Occurrence == null && original.Event.ExpectedOccurrence is { ExpectedCursor: 3, SelectedCursor: 3 },
            "automatic event acquires observed instance receipt without forcing its appearance");
        Check(history.Matching(Node(eventKey, 4), null) == null && history.Count == 1,
            "same named event at a changed queue occurrence does not inherit old choice");
        var exclusionEdit = history.Remember(original with
            { EventOccurrenceExclusions = [new(eventKey, 3)] }, originalNode);
        Check(exclusionEdit.Event!.Occurrence == null && exclusionEdit.Event.ExpectedOccurrence == original.Event.ExpectedOccurrence,
            "appearance exclusion keeps automatic selection separate from the retained action receipt");
        history.Remember(original, originalNode);
        Check(history.Matching(Node(eventKey, 3, "map:2:4:1:20"), null) == null,
            "same named event at a different node is a different instance");
        var replacementNode = Node(otherEvent, 4);
        history.Remember(new(replacementNode.Position.NodeId) { Event = new(otherEvent) }, replacementNode);
        Check(history.Count == 2 && history.Matching(originalNode, null) == original &&
            history.Matching(originalNode, null)!.Event!.Actions[0].DeckCards.Single() == target,
            "return to an earlier event instance restores its original branch/target intent without substituting identity");
        var excludingA = history.Remember(history.Matching(replacementNode, null)! with
            { EventOccurrenceExclusions = [new(eventKey, 3)] }, replacementNode);
        var restoredA = history.Matching(originalNode, null)!;
        Check(restoredA.EventOccurrenceExclusions.SequenceEqual(excludingA.EventOccurrenceExclusions) &&
            restoredA.Event!.Actions[0].DeckCards.Single() == target,
            "matching event-instance actions keeps the latest coordinate exclusion policy");
        history.Remember(excludingA with { EventOccurrenceExclusions = [] }, replacementNode);
        Check(history.Matching(originalNode, null)!.EventOccurrenceExclusions.IsEmpty &&
            history.LatestPlans.Single().EventOccurrenceExclusions.IsEmpty,
            "restore updates active and route-return history without erasing either event's operations");
        history.Remove(1, new(1, 3));
        Check(history.Count == 0 && history.Matching(originalNode, null) == null,
            "explicit plan removal clears remembered choices at that coordinate");
        history.Remember(original, originalNode); history.Clear();
        Check(history.Count == 0, "explicit global clear forgets same-run choice history");
    }

    private delegate void EntryObserver(RunManager manager, MapCoord target, out bool captured);

    private static void NodeEntryFeaturePreferenceLifecycle(PredictorRun baseline)
    {
        var manager = RunManager.Instance;
        var runtimeProperty = typeof(RolltheSpire2.Bootstrap.ModRuntime).GetProperty("Snapshot",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var runtimeDirectory = typeof(RolltheSpire2.Ui.Settings.InRunFeaturePreferences).GetField("_runtimeDirectory",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldRuntime = RolltheSpire2.Bootstrap.ModRuntime.Snapshot;
        var oldDirectory = runtimeDirectory.GetValue(null);
        string directory = Path.Combine(Godot.OS.GetUserDataDir(), "RolltheSpire2", "state");
        RolltheSpire2.Ui.Settings.InRunFeaturePreferences.Initialize(directory, new());
        bool oldMap = RolltheSpire2.Ui.Settings.InRunFeaturePreferences.MapPredictionEnabled;
        var activeDirectory = runtimeDirectory.GetValue(null);
        string version = baseline.Context.GameVersion;
        runtimeProperty.SetValue(null, oldRuntime with { Detection = oldRuntime.Detection with
            { NormalizedVersion = version } });
        var observer = typeof(PredictorLiveRunCapture).GetMethod("BeforeNativeNodeEntry",
            BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<EntryObserver>();
        try
        {
            var native = CostFixture(baseline, false);
            var mapPoints = native.Map.GetAllMapPoints().Where(p => p.coord.row > 0)
                .OrderBy(p => p.coord.row).Take(3).ToArray();
            foreach (var point in mapPoints) point.PointType = MegaCrit.Sts2.Core.Map.MapPointType.Monster;
            var points = mapPoints.Select(point => point.coord).ToArray();
            var rooms = (RoomSet)typeof(ActModel).GetField("_rooms", Members)!.GetValue(native.Act)!;
            string before = NativeFacts(native);
            runtimeDirectory.SetValue(null, null);
            Check(RolltheSpire2.Ui.Settings.InRunFeaturePreferences.CachedMapPredictionEnabled == null,
                "uninitialized preference cache is explicitly unknown");
            observer(manager, points[0], out bool captured);
            Check(captured && runtimeDirectory.GetValue(null) == null && NativeFacts(native) == before,
                "unknown cache preserves capture without lazy runtime preference initialization or native mutation");
            runtimeDirectory.SetValue(null, activeDirectory);
            Enter(points[0]);
            var entry = PredictorLiveRunCapture.CurrentNodeEntry(native, version);
            Check(entry != null, "unknown-cache observer produces a valid completed entry receipt");
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, false);
            before = NativeFacts(native);
            observer(manager, points[0], out captured);
            Check(!captured && ReferenceEquals(entry, PredictorLiveRunCapture.CurrentNodeEntry(native, version)) &&
                NativeFacts(native) == before, "off-feature duplicate no-op preserves the current valid receipt");
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, true);
            Check(ReferenceEquals(entry, PredictorLiveRunCapture.CurrentNodeEntry(native, version)),
                "off-on in the same room keeps the genuine before-entry premise");
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, false);
            observer(manager, points[1], out captured);
            Check(!captured && PredictorLiveRunCapture.CurrentNodeEntry(native, version) == null && NativeFacts(native) == before,
                "off-feature new entry clears stale receipt without capture or native mutation");
            Enter(points[1]);
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, true);
            var session = new InRunMapPredictionSession();
            session.Update(native, version, false, new Dictionary<int, PredictorMapRouteDraft>());
            Check(session.WaitingForBoundary && session.Snapshot == null,
                "re-enable after an off-feature node move waits rather than inventing entry state");
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, false);
            before = NativeFacts(native);
            Check(PredictorLiveRunCapture.CaptureBeforeNodeEntry(native, version, points[2]) != null && NativeFacts(native) == before,
                "explicit direct capture remains independent of the native feature gate");
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, true);
            observer(manager, points[2], out captured);
            Check(captured && NativeFacts(native) == before, "enabled observer resumes read-only capture at the next genuine entry");
            Enter(points[2]);
            Check(PredictorLiveRunCapture.CurrentNodeEntry(native, version) != null,
                "resumed observer receipt validates after the next native entry completes");

            void Enter(MapCoord point)
            {
                Check(native.AddVisitedMapCoord(point), "feature lifecycle controlled entry advances once");
                native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster, RoomType.Monster,
                    rooms.normalEncounters[rooms.normalEncountersVisited % rooms.normalEncounters.Count].Id);
                rooms.normalEncountersVisited++;
                PredictorLiveRunCapture.RecordNodeEntryTask(native, point, Task.CompletedTask);
            }
        }
        finally
        {
            runtimeDirectory.SetValue(null, activeDirectory);
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, oldMap);
            runtimeDirectory.SetValue(null, oldDirectory);
            runtimeProperty.SetValue(null, oldRuntime);
        }
    }

    // Opt-in measurements use the existing native host. Setup, assertions and
    // reporting are outside the measured interval. "Cold" means no previous
    // snapshot for this RunState, not a process-cold/JIT-free measurement.
    private static void CaptureCost(PredictorRun baseline, int samples)
    {
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", Members)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", Members)!;
        var runtimeProperty = typeof(RolltheSpire2.Bootstrap.ModRuntime).GetProperty("Snapshot",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var oldRuntime = RolltheSpire2.Bootstrap.ModRuntime.Snapshot;
        var oldState = stateProperty.GetValue(manager);
        var oldAscension = ascensionProperty.GetValue(manager);
        ulong? oldLocal = LocalContext.NetId;
        string version = baseline.Context.GameVersion;
        var observer = typeof(PredictorLiveRunCapture).GetMethod("BeforeNativeNodeEntry",
            BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<EntryObserver>();
        // Seed only in-memory preferences; never construct a SearchWorkspace
        // here or attribute its disk recovery cost to a native entry capture.
        string directory = Path.Combine(Godot.OS.GetUserDataDir(), "RolltheSpire2", "state");
        RolltheSpire2.Ui.Settings.InRunFeaturePreferences.Initialize(directory, new());
        bool oldMap = RolltheSpire2.Ui.Settings.InRunFeaturePreferences.MapPredictionEnabled;
        runtimeProperty.SetValue(null, oldRuntime with { Detection = oldRuntime.Detection with
            { NormalizedVersion = version } });
        Console.WriteLine($"LIVE_CAPTURE_COST_SETUP samples={samples};clock=Stopwatch;allocation=currentThread;" +
            "cold=perRunNoPreviousSnapshot;nativeLifecycle=false;sceneRendering=false;timingFields=unused");
        try
        {
            foreach (bool enabled in new[] { false, true })
            {
                RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, enabled);
                for (int sample = 0; sample < samples; sample++)
                {
                    var native = CostFixture(baseline, false);
                    var target = native.Map.GetAllMapPoints().Single(p => p.coord.row == 1).coord;
                    string before = NativeFacts(native);
                    bool captured = false;
                    Cost($"observer.feature-{enabled.ToString().ToLowerInvariant()}.cold", sample,
                        () => observer(manager, target, out captured));
                    Check(NativeFacts(native) == before, "cost observer leaves native state unchanged");
                    Console.WriteLine($"LIVE_CAPTURE_COST_OBSERVATION featureEnabled={enabled};sample={sample};captured={captured}");
                    // Same unentered target deliberately isolates snapshot reuse
                    // from actual native room construction and game RNG draws.
                    Cost($"observer.feature-{enabled.ToString().ToLowerInvariant()}.warm", sample,
                        () => observer(manager, target, out captured));
                    Check(!enabled || captured, "enabled observer supplies a usable before-entry capture");
                    Check(NativeFacts(native) == before, "warm cost observer leaves native state unchanged");
                }
            }
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, true);
            foreach (bool complex in new[] { false, true })
            {
                string kind = complex ? "mixed10" : "ordinary2";
                for (int sample = 0; sample < samples; sample++)
                {
                    var native = CostFixture(baseline, complex);
                    string before = NativeFacts(native);
                    var map = PredictorLiveRunCapture.Map(native);
                    var points = map.Points.Where(p => p.Position.Row > 0)
                        .OrderBy(p => p.Position.Row).Take(complex ? 10 : 2).Select(p => p.Position).ToArray();
                    var draft = new PredictorMapRouteDraft(map,
                        new(native.Map.StartingMapPoint.coord.col, native.Map.StartingMapPoint.coord.row));
                    Check(draft.ApplyStroke(points, PredictorMapStrokeMode.Draw) != PredictorMapDraftEditResult.Invalid,
                        "cost selected route is connected");
                    var drafts = new Dictionary<int, PredictorMapRouteDraft> { [1] = draft };
                    var session = new InRunMapPredictionSession();
                    Cost($"session.{kind}.update-initial", sample, () => session.Update(native, version, true, drafts));
                    Check(session.Nodes.Count >= 1, "cost route reaches a real editable node");
                    Cost($"session.{kind}.update-unchanged", sample, () => session.Update(native, version, true, drafts));
                    var first = session.Nodes.OrderBy(pair => pair.Value.Position.Floor).First();
                    bool accepted = false;
                    Cost($"session.{kind}.edit", sample, () => accepted = session.SetPlan(first.Key.ActIndex,
                        first.Key.Point, new(first.Value.Position.NodeId) { LeaveRewards = true }));
                    Check(accepted, "cost edit accepted by actual session");
                    Cost($"session.{kind}.recompute", sample, session.Recompute);
                    Console.WriteLine($"LIVE_CAPTURE_COST_ROUTE kind={kind};sample={sample};selected={points.Length};" +
                        $"nodes={session.Nodes.Count};plans={session.Plans.Length};" +
                        $"terminalCauses={string.Join(',', session.Forecast?.TerminalBoundary?.PendingCauses.Select(c => c.Code) ?? [])};" +
                        $"rooms={string.Join(',', session.Nodes.Values.OrderBy(n => n.Position.Floor).Select(n => n.Room))}");
                    Check(NativeFacts(native) == before, "cost session update/edit/recompute leaves native state unchanged");
                }
            }
            Console.WriteLine($"LIVE_CAPTURE_COST_PASS checks={_checks};noGameMutation=true");
        }
        finally
        {
            RolltheSpire2.Ui.Settings.InRunFeaturePreferences.SetMap(directory, oldMap);
            runtimeProperty.SetValue(null, oldRuntime);
            stateProperty.SetValue(manager, oldState);
            ascensionProperty.SetValue(manager, oldAscension);
            LocalContext.NetId = oldLocal;
        }
    }

    private static void Cost(string operation, int sample, Action action)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        action();
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Console.WriteLine(FormattableString.Invariant($"LIVE_CAPTURE_COST operation={operation};sample={sample};ms={ms:F4};allocatedBytes={bytes}"));
    }

    private static RunState CostFixture(PredictorRun baseline, bool complex)
    {
        typeof(RunManager).GetProperty("AscensionManager", Members)!.SetValue(RunManager.Instance, new AscensionManager(baseline.Context.Ascension));
        var native = EntryFixture(baseline, 2);
        native.AddVisitedMapCoord(native.Map.StartingMapPoint.coord);
        native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, native.Act.Ancient.Id);
        native.Players[0].Gold = 300;
        MegaCrit.Sts2.Core.Map.MapPointType[] types = complex ?
            [MegaCrit.Sts2.Core.Map.MapPointType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Treasure,
             MegaCrit.Sts2.Core.Map.MapPointType.Shop, MegaCrit.Sts2.Core.Map.MapPointType.RestSite,
             MegaCrit.Sts2.Core.Map.MapPointType.Unknown, MegaCrit.Sts2.Core.Map.MapPointType.Monster,
             MegaCrit.Sts2.Core.Map.MapPointType.RestSite, MegaCrit.Sts2.Core.Map.MapPointType.Treasure,
             MegaCrit.Sts2.Core.Map.MapPointType.Elite, MegaCrit.Sts2.Core.Map.MapPointType.Monster] :
            [MegaCrit.Sts2.Core.Map.MapPointType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Monster];
        var points = native.Map.GetAllMapPoints().Where(p => p.coord.row > 0).OrderBy(p => p.coord.row).Take(types.Length).ToArray();
        Check(points.Length == types.Length, "cost map supplies selected length");
        for (int i = 0; i < types.Length; i++) points[i].PointType = types[i];
        return native;
    }

    private static void NodeEntryHookBinding()
    {
        var harmony = new Harmony("rt2.predictor.live.capture.entry.fixture");
        try
        {
            PredictorLiveRunCapture.InstallNodeEntryObserver(harmony);
            var original = AccessTools.Method(typeof(RunManager), nameof(RunManager.EnterMapCoord));
            var patches = Harmony.GetPatchInfo(original);
            Check(patches != null && patches.Prefixes.Any(p => p.owner == harmony.Id) &&
                patches.Postfixes.Any(p => p.owner == harmony.Id),
                "installed native EnterMapCoord prefix/postfix bind target, Task result and shared receipt state");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

}
