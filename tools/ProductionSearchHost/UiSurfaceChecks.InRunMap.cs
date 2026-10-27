using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;

internal static partial class UiSurfaceChecks
{
    private static Vector2? _inRunMapFixtureNativeStart;
    private static void InRunMapSupplyNativeStart(ref Vector2 position)
    {
        // The offscreen Window reports DisplayServer pointer (0,0). Replace
        // only the native held-input initial pointer sample; retain its real
        // ready, line, motion, release and Finished lifecycle.
        if (_inRunMapFixtureNativeStart is { } start) position = start;
    }
    // Existing UI source host, mounted native PCK and initialized ModelDb/SaveManager.
    // Native map children, SetMap/point lifecycle and product Harmony patches are
    // real. Full game startup and animated Spine remain outside this receipt.
    internal static async Task RunInRunMapChecks(Node root, string output, ModRuntimeSnapshot runtime, bool drawingOnly = false)
    {
        bool productReviewOnly = OS.GetCmdlineUserArgs().Contains("--product-review-ui");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(NMapScreen).Assembly);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, productReviewOnly ? "product-review-wait-checks.json" :
            OS.GetCmdlineUserArgs().Contains("--event-overview-relics-ui") ? "event-overview-relics-result.json" : "in-run-map-result.json"),
            "{\"passed\":false,\"state\":\"running\"}");
        root.GetWindow().Size = new(1680, 1080);
        RenderingServer.SetDefaultClearColor(new Color("161b20"));
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException("InRunMap: " + message); }
        object Field(object obj, string name) => (obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(obj.GetType().FullName, name)).GetValue(obj)!;
        var oldGame = NGame.Instance;
        var oldLocal = LocalContext.NetId;
        bool fixtureDebugAction = !InputMap.HasAction("debug_hide_top_bar");
        if (fixtureDebugAction) InputMap.AddAction("debug_hide_top_bar");
        TestMode.IsOn = true;
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = RunState.CreateForTest([player], seed: "000000067C0S", ascensionLevel: 10);
        RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), false);
        RunManager.Instance.GenerateRooms();
        foreach (var act in run.Acts)
        {
            act.SetBossEncounter(ModelDb.Encounter<KaiserCrabBoss>());
            act.SetSecondBossEncounter(null);
        }
        run.Map = StandardActMap.CreateFor(run, false);
        run.AddVisitedMapCoord(run.Map.StartingMapPoint.coord);
        // This isolated UI fixture represents departure from the actual first
        // Ancient. Native total floor/history must match its visited coordinate
        // before detached information capture can use it as a real boundary.
        run.AppendToMapPointHistory(run.Map.StartingMapPoint.PointType, RoomType.Event, run.Act.Ancient.Id);
        // Controlled restored-room condition: native unfinished room/history,
        // with no EnterMapCoord receipt. This does not exercise disk loading.
        if (productReviewOnly) run.PushRoom(new EventRoom(run.Act.Ancient));
        LocalContext.NetId = player.NetId;
        TestMode.IsOn = false;

        // No NGame startup or real user run is created. These detached nodes
        // supply only the static screen/controller/cursor lookups native map UI uses.
        var game = new NGame();
        var sceneContainer = new NSceneContainer();
        var runNode = new NRun();
        var global = new NGlobalUi { Name = "GlobalUi" };
        var input = new NInputManager();
        var controller = new NControllerManager();
        var cursor = new NCursorManager();
        var capstone = new NCapstoneContainer();
        var hotkeys = new NHotkeyManager();
        var remoteCursors = new NRemoteMouseCursorContainer();
        typeof(NInputManager).GetProperty("ControllerManager")!.SetValue(input, controller);
        typeof(NGame).GetProperty("InputManager")!.SetValue(game, input);
        typeof(NGame).GetProperty("CursorManager")!.SetValue(game, cursor);
        typeof(NGame).GetProperty("HotkeyManager")!.SetValue(game, hotkeys);
        typeof(NGame).GetProperty("RemoteCursorContainer")!.SetValue(game, remoteCursors);
        typeof(NGame).GetProperty("RootSceneContainer")!.SetValue(game, sceneContainer);
        typeof(NSceneContainer).GetProperty("CurrentScene")!.SetValue(sceneContainer, runNode);
        typeof(NRun).GetProperty("GlobalUi")!.SetValue(runNode, global);
        typeof(NGlobalUi).GetProperty("CapstoneContainer")!.SetValue(global, capstone);
        typeof(NGame).GetProperty("Instance")!.SetValue(null, game);
        var fixtureHarmony = new Harmony("rt2.inrunmap.ui.fixture");
        fixtureHarmony.Patch(AccessTools.Method(typeof(NBossMapPoint), "_Ready"),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapStaticBossReady)));
        // Use the production parent and native packed top-bar geometry. Its
        // unrelated inventory/context setup still requires full game startup.
        foreach (var (type, method) in new[] { (typeof(NGlobalUi), "_Ready"), (typeof(NTopBar), "_Ready"),
                     (typeof(NTopBar), "_EnterTree"), (typeof(NTopBar), "_ExitTree") })
            fixtureHarmony.Patch(AccessTools.Method(type, method),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
        // FMOD is not initialized by this stock-engine UI host.
        fixtureHarmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.SfxCmd), "Play", [typeof(string), typeof(float)]),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
        fixtureHarmony.Patch(AccessTools.Method(typeof(NMapDrawings), "BeginLineLocal"),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSupplyNativeStart)));
        var topBar = PreloadManager.Cache.GetScene("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
        var mapButton = topBar.GetNode<NTopBarMapButton>("%Map");
        // The unused deck button is never initialized; supply only its cleanup
        // reference so its native predelete unsubscribe can run safely.
        typeof(NTopBarDeckButton).GetField("_pile", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(topBar.GetNode<NTopBarDeckButton>("%Deck"), player.Deck);
        foreach (var node in Descendants(mapButton).Where(n => n.Owner == topBar)) node.Owner = null;
        mapButton.GetParent().RemoveChild(mapButton);
        foreach (Node child in topBar.GetChildren())
            if (child.Name != "BgImage") { topBar.RemoveChild(child); child.Free(); }
        topBar.AddChild(mapButton);
        mapButton.Position = new(1420, 0);
        typeof(NTopBar).GetProperty("Map")!.SetValue(topBar, mapButton);
        typeof(NGlobalUi).GetProperty("TopBar")!.SetValue(global, topBar);

        var screen = PreloadManager.Cache.GetScene("res://scenes/screens/map/map_screen.tscn").Instantiate<NMapScreen>();
        screen.Name = "NativeMap"; screen.Visible = false;
        typeof(NGlobalUi).GetProperty("MapScreen")!.SetValue(global, screen);
        global.AddChild(screen); global.AddChild(capstone); global.AddChild(topBar);
        var capstoneBackstop = new ColorRect { Name = "CapstoneBackstop", Color = new(0, 0, 0, .85f), MouseFilter = Control.MouseFilterEnum.Ignore };
        capstone.AddChild(capstoneBackstop);
        root.AddChild(global); global.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        capstone.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        capstoneBackstop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.Initialize(run);
        typeof(NMapBg).GetMethod("OnVisibilityChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(screen.GetNode<NMapBg>("TheMap/MapBg"), null);
        screen.SetMap(run.Map, run.Rng.Seed, false);
        // Ordinary checks start after the first Ancient is completed; the
        // focused waiting check starts inside its unfinished restored room.
        // Publish either premise through the actual native travel API.
        screen.SetTravelEnabled(!productReviewOnly);
        Check(screen.IsTravelEnabled == !productReviewOnly, "fixture establishes actual native departure availability");
        typeof(NMapScreen).GetField("_hasPlayedAnimation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(screen, true);
        var map = screen.GetNode<Control>("TheMap");
        screen.GetNode<Control>("%MapLegend").Position = new(1340, 250);
        InRunMapPredictionPanel.EnsureAttached(screen, run, runtime);
        var panel = screen.GetNode<InRunMapPredictionPanel>(InRunMapPredictionPanel.NodeName);
        var productHarmony = new Harmony("rt2.inrunmap.ui.product");
        string Signature() => JsonSerializer.Serialize(new
        {
            run.CurrentActIndex, run.TotalFloor, run.CurrentMapCoord, run.MapPointHistory,
            map = SerializableActMap.FromActMap(run.Map), run.VisitedMapCoords,
            acts = run.Acts.Select(a => a.ToSave()), rng = run.Rng.ToSerializable(),
            odds = run.Odds.ToSerializable(), extra = run.ExtraFields.ToSerializable(),
            room = run.BaseRoom == null ? null : new { run.BaseRoom.RoomType, run.BaseRoom.ModelId, run.BaseRoom.IsPreFinished },
            players = run.Players.Select(p => p.ToSerializable())
        });
        string before = Signature();
        var graphReference = run.Map;
        var nativePoints = screen.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>()
            .ToDictionary(n => InRunMapSnapshot.Position(n.Point.coord));
        Vector2 At(MapPosition position) => nativePoints[position].GetGlobalRect().GetCenter();
        void Native(string name, params object?[] args) => typeof(NMapScreen).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, args);
        object? NativeDrawingInput() => typeof(NMapScreen).GetField("_drawingInput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(screen);
        async Task Frame(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Stroke(IReadOnlyList<Vector2> positions, MouseButton button = MouseButton.Right)
        {
            var samples = new List<object>();
            void Sample(string stage) => samples.Add(new {
                Stage = stage, Map = map.Position.ToString(), Target = Field(screen, "_targetDragPos").ToString(),
                ScreenOpen = screen.IsOpen, Feature = panel.PredictionEnabled,
                NativeAttached = Field(panel, "_nativeDrawing") != null,
                Points = ((List<Vector2>)Field(panel, "_stroke")).Select(value => value.ToString()).ToArray(),
                Hits = ((HashSet<MapPosition>)Field(panel, "_hits")).ToArray(),
                Selected = panel.DisplayedDraft!.Selected.ToArray() });
            Sample("before");
            _inRunMapFixtureNativeStart = screen.Drawings.GetGlobalTransform().AffineInverse() * positions[0];
            try
            {
                Native("ProcessMouseDrawingEvent", new InputEventMouseButton { ButtonIndex = button, Pressed = true,
                    Position = positions[0], GlobalPosition = positions[0] });
                await Frame(1);
                Check(NativeDrawingInput() is NMouseHeldMapDrawingInput && screen.Drawings.IsLocalDrawing(),
                    "native right/middle dispatch creates held input and starts an ordinary native line");
            }
            finally { _inRunMapFixtureNativeStart = null; }
            Sample("started");
            var held = (NMouseHeldMapDrawingInput)NativeDrawingInput()!;
            foreach (var position in positions.Skip(1))
                held._Input(new InputEventMouseMotion { Position = position, GlobalPosition = position });
            Sample("sampled");
            var last = positions[^1];
            held._Input(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = last, GlobalPosition = last });
            Check(NativeDrawingInput() == null && !screen.Drawings.IsLocalDrawing(),
                "native held release stops its line and releases native drawing input");
            await Frame();
            Sample("released");
            File.AppendAllText(Path.Combine(output, "in-run-map-native-stroke-receipts.jsonl"), JsonSerializer.Serialize(new {
                Button = button.ToString(), Positions = positions.Select(position => position.ToString()).ToArray(), Samples = samples }) + System.Environment.NewLine);
        }
        async Task Transition()
        { await root.ToSignal(root.GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout); await Frame(); }
        void MoveMap(Vector2 position)
        {
            map.Position = position;
            typeof(NMapScreen).GetField("_targetDragPos", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(screen, position);
        }
        bool PanelWithinMap() => screen.GetGlobalRect().Encloses(panel.GetGlobalRect());
        async Task Picture(string name)
        {
            await Frame();
            using var image = root.GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(output, name));
        }
        async Task CheckScrolledInk(IReadOnlyDictionary<MapPosition, Control> nodes, string label)
        {
            var draft = panel.DisplayedDraft!;
            var saved = draft.Selected;
            var points = draft.Map.Points.ToDictionary(p => p.Position);
            var path = new List<MapPosition>();
            var current = draft.Map.Points.Single(p => p.Position.Row == 0);
            while (true)
            {
                path.Add(current.Position);
                if (current.Children.Count == 0) break;
                current = points[current.Children[0]];
            }
            draft.Clear(); draft.ApplyStroke(path, PredictorMapStrokeMode.Draw);
            typeof(InRunMapPredictionPanel).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
            var ink = (InRunMapRouteInk)Field(panel, "_ink");
            foreach (int scroll in new[] { -600, 600, 1800 })
            {
                MoveMap(new(0, scroll)); await Frame();
                using var image = root.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, $"{label}-scroll-{scroll}.png"));
                foreach (var selected in draft.Selected)
                {
                    Vector2 marker = ink.GetGlobalTransform() * ink.Regions[selected].GetCenter();
                    Vector2 visual = nodes[selected].GetGlobalTransform() * (nodes[selected].Size * .5f);
                    Check(marker.DistanceTo(visual) < .5f, $"{label} marker centered after layout/scale/scroll: {selected}; marker={marker}, visual={visual}");
                }
                var visible = draft.Selected.Where(p =>
                {
                    var center = nodes[p].GetGlobalTransform() * (nodes[p].Size * .5f);
                    return center.X > 160 && center.X < 1200 && center.Y > 180 && center.Y < 900;
                }).ToArray();
                Check(visible.Length > 0, $"{label} has selected nodes visible at scroll {scroll}");
                foreach (var selected in visible)
                {
                    var region = ink.Regions[selected];
                    var center = nodes[selected].GetGlobalTransform() * (nodes[selected].Size * .5f);
                    float radius = Math.Min(region.Size.X, region.Size.Y) * .55f;
                    int routePixels = 0;
                    for (int y = (int)(center.Y - radius - 5); y <= center.Y + radius + 5; y++)
                        for (int x = (int)(center.X - radius - 5); x <= center.X + radius + 5; x++)
                        {
                            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) continue;
                            var color = image.GetPixel(x, y);
                            if (Math.Abs(new Vector2(x, y).DistanceTo(center) - radius) <= 3 &&
                                color.G > .5f && color.G > color.R + .035f && color.G > color.B + .01f) routePixels++;
                        }
                    Check(routePixels > 30, $"{label} subtle selected ring rendered at scroll {scroll}: {selected}; pixels={routePixels}");
                }
            }
            draft.Clear(); draft.ApplyStroke(saved, PredictorMapStrokeMode.Draw);
            typeof(InRunMapPredictionPanel).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
            await Frame();
        }
        try
        {
            InRunMapPredictionPatch.Install(productHarmony);
            if (productReviewOnly)
            {
                var room = run.BaseRoom as EventRoom;
                Check(room is { IsPreFinished: false } && !screen.IsTravelEnabled &&
                    PredictorLiveRunCapture.CurrentNodeEntry(run, runtime.Detection.NormalizedVersion) == null,
                    "unfinished native room has no fabricated before-entry observation");
                screen.Open(true); await Transition();
                var session = (InRunMapPredictionSession)Field(panel, "_prediction");
                var status = (Label)Field(panel, "_forecastStatus");
                var reviewFirst = run.Map.StartingMapPoint.Children.First();
                var point = InRunMapSnapshot.Position(reviewFirst.coord);
                Check(panel.DisplayedDraft!.ApplyStroke([point], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed,
                    "compatible authored route remains usable while entry boundary is missing");
                typeof(InRunMapPredictionPanel).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
                await Frame();
                string previousLanguage = InRunFeaturePreferences.LanguageOverride;
                string directory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state");
                var receipts = new List<object>();
                try
                {
                    foreach (string locale in new[] { "en", "zh" })
                    {
                        InRunFeaturePreferences.SetLanguage(directory, locale); await Frame();
                        string expected = JsonUiTextProvider.CreatePredictorUi13(locale).Get("predictor.map.waiting_boundary");
                        Check(expected.Length > 0 && !expected.Contains("predictor.map.waiting_boundary", StringComparison.Ordinal) &&
                            status.IsVisibleInTree() && status.Text == expected,
                            "missing entry explains the real waiting boundary in " + locale);
                        Check(session.WaitingForBoundary && session.Snapshot == null && session.Forecast == null &&
                            session.Nodes.IsEmpty && panel.DisplayedDraft.Selected.SequenceEqual(new[] { point }),
                            "waiting does not manufacture entry, forecast or route observations in " + locale);
                        Check(screen.GetGlobalRect().Grow(1).Encloses(status.GetGlobalRect()) && Signature() == before,
                            "waiting explanation is on screen and prediction preserves serialized native facts in " + locale);
                        Check(!status.GetGlobalRect().Intersects(screen.GetNode<Control>("%DrawingTools").GetGlobalRect()),
                            "waiting explanation stays clear of the native drawing toolbar in " + locale);
                        await Picture("product-review-wait-" + locale + ".png");
                        receipts.Add(new { locale, waiting = status.Text, bounds = status.GetGlobalRect().ToString(),
                            noSnapshot = session.Snapshot == null, noForecast = session.Forecast == null });
                    }
                    room!.MarkPreFinished();
                    screen.SetTravelEnabled(true);
                    string departure = Signature();
                    await Frame(6);
                    Check(session.Snapshot is { CanLeaveCurrentNode: true, EntryPoint: null } && !session.WaitingForBoundary &&
                        PredictorLiveRunCapture.CurrentNodeEntry(run, runtime.Detection.NormalizedVersion) == null,
                        "actual completed-room native departure recovers a detached boundary without inventing an entry receipt");
                    Check(session.Nodes.ContainsKey((0, point)) && session.Forecast != null &&
                        !status.Visible && status.Text.Length == 0 && Field(panel, "_predictionFault") == null,
                        "reliable departure resumes authored-route prediction and removes waiting explanation");
                    Check(Signature() == departure && ReferenceEquals(run.Map, graphReference),
                        "recovered prediction preserves native room, inventory, RNG, graph, history and player serialized facts");
                    await Picture("product-review-wait-recovered.png");
                    File.WriteAllText(Path.Combine(output, "product-review-wait-checks.json"), JsonSerializer.Serialize(new {
                        status = "PASS", checks, receipts, nativeDiskLoad = false,
                        trigger = "native-unfinished-room-without-before-entry-receipt", recovery = "native-completed-room-departure",
                        serializedFactsUnchangedWithinEachBoundary = true, ownerRuntimeAcceptance = "pending"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    GD.Print($"PRODUCT_REVIEW_WAIT_UI_PASS checks={checks};en+zh+missingReceipt+nativeDeparture+readOnly=true");
                    return;
                }
                finally { InRunFeaturePreferences.SetLanguage(directory, previousLanguage); }
            }
            foreach (string method in new[] { "Initialize", "SetMap", "Open", "Close", "ProcessMouseDrawingEvent" })
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NMapScreen), method))?.Owners.Contains(productHarmony.Id) == true,
                    "product Harmony registered native map method " + method);
            foreach (string method in new[] { "BeginLineLocal", "UpdateCurrentLinePositionLocal", "StopLineLocal", "ClearDrawnLinesLocal" })
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NMapDrawings), method))?.Owners.Contains(productHarmony.Id) == true,
                    "product Harmony observes newly local native drawing method " + method);
            foreach (string method in new[] { "HandleDrawingMessage", "HandleClearMapDrawingsMessage", "LoadDrawings", "ClearAllLines" })
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NMapDrawings), method))?.Owners.Contains(productHarmony.Id) != true,
                    "remote, loaded and lifecycle ink cannot author local plans: " + method);
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NMapPoint), "IsInputAllowed"))?.Owners.Contains(productHarmony.Id) != true &&
                Harmony.GetPatchInfo(AccessTools.Method(typeof(NMapScreen), "OnMapPointSelectedLocally"))?.Owners.Contains(productHarmony.Id) != true,
                "product leaves native point admission and travel dispatch under game ownership");
            foreach (string method in new[] { "OnMapDrawingButtonPressed", "OnMapErasingButtonPressed", "OnClearMapDrawingButtonPressed", "UpdateDrawingButtonStates" })
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NMapScreen), method))?.Owners.Contains(productHarmony.Id) != true,
                    "product leaves native tool ownership unchanged: " + method);
            Check(!panel.IsVisibleInTree() && screen.ProcessMode == Node.ProcessModeEnum.Disabled,
                "native closed-map ready lifecycle hides panel and disables inherited processing");
            screen.Open(true); await Transition();
            Check(screen.IsOpen && panel.IsVisibleInTree() && panel.CanProcess(),
                "native Open makes the attached panel visible and processing");
            Check(topBar.Size.IsEqualApprox(screen.Size) && topBar.GetGlobalRect().HasPoint(screen.GetGlobalRect().GetCenter()),
                "native TopBar root covers viewport, reproducing the former exclusion defect");
            Check(panel.FindChild("PredictionToggle", true, false) == null && panel.GetChildCount() == 1 &&
                !((Control)Field(panel, "_forecastStatus")).Visible,
                "default-enabled map has no resident mode/status chrome before Settings;children=" +
                string.Join(',', panel.GetChildren().Select(child => child.Name + ":" + child.GetClass())) +
                ";statusVisible=" + ((Control)Field(panel, "_forecastStatus")).Visible +
                ";status=" + ((Label)Field(panel, "_forecastStatus")).Text);
            await Picture("in-run-map-open.png");
            topBar.AnimHide(); capstone.EnableBackstopInstantly(); await Transition();
            Check(Math.Abs(topBar.Position.Y + 100) < .1f && panel.PredictionEnabled,
                "real Settings top-bar hide transition retains default-enabled forecast");
            await Picture("in-run-map-settings-hidden.png");
            topBar.AnimShow(); capstone.DisableBackstopInstantly(); await Transition();
            Check(Math.Abs(topBar.Position.Y) < .1f && panel.PredictionEnabled,
                "real Settings top-bar show transition does not require forecast re-enabling");
            screen.Close(false); await Frame();
            Check(!panel.IsVisibleInTree() && !panel.CanProcess(), "native Close hides panel with map and disables inherited processing");
            screen.Open(true); await Transition();
            Check(panel.IsVisibleInTree() && panel.CanProcess() && PanelWithinMap(), "native reopen restores panel without opening Settings");
            Check(Signature() == before, "native visibility and top-bar transitions leave run/RNG unchanged");
            var isMapArea = typeof(InRunMapPredictionPanel).GetMethod("IsMapArea", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Check(!(bool)isMapArea.Invoke(panel, [new Vector2(100, 40)])!, "visible top-bar strip excludes route mouse input");
            topBar.Hide();
            Check((bool)isMapArea.Invoke(panel, [new Vector2(100, 40)])!, "hidden top-bar strip does not exclude map input");
            topBar.Show(); await Frame();
            Check(panel.FindChild("ActSwitch", true, false) == null &&
                panel.FindChild("PredictionToggle", true, false) == null &&
                panel.FindChild("DraftStatus", true, false) == null && panel.DisplayedDraft!.Map.ActIndex == run.CurrentActIndex,
                "current actual Act has no separate mode switch or resident status panel");
            Check(panel.PredictionEnabled && panel.DisplayedDraft!.Selected.IsEmpty, "route forecast is enabled on entry; draft starts empty");
            var first = run.Map.StartingMapPoint.Children.First(); var second = first.Children.First();
            var a = InRunMapSnapshot.Position(first.coord); var b = InRunMapSnapshot.Position(second.coord);
            await Stroke([At(b), At(a)]);
            Check(panel.DisplayedDraft!.Selected.SequenceEqual([a, b]), "one reverse native segment selects both crossed native regions");
            if (drawingOnly)
            {
                await RunDrawingForecastUi(root, output, screen, panel, run, runtime.Detection.NormalizedVersion, a, b, Signature, Check);
                Check(Signature() == before, "drawing forecast inputs leave native run and RNG unchanged");
                if (OS.GetCmdlineUserArgs().Contains("--drawing-notices-ui")) return;
                if (OS.GetCmdlineUserArgs().Contains("--shop-gold-ui")) return;
                if (OS.GetCmdlineUserArgs().Contains("--card-collection-overview-ui")) return;
                if (OS.GetCmdlineUserArgs().Contains("--shop-treasure-premises-ui")) return;
                if (OS.GetCmdlineUserArgs().Contains("--rest-ancient-premises-ui")) return;
                if (OS.GetCmdlineUserArgs().Contains("--event-overview-relics-ui")) return;
                if (OS.GetCmdlineUserArgs().Contains("--doors-branch-ui") || OS.GetCmdlineUserArgs().Contains("--event-premises-ui"))
                {
                    File.WriteAllText(Path.Combine(output, "doors-branch-ui-result.json"), JsonSerializer.Serialize(new
                    { passed = true, checks, directBranches = true, optionalTargets = true,
                        noImplicitOutcome = true, independentRewards = true, nativeRunAndRngUnchanged = true }));
                    GD.Print($"DOORS_BRANCH_UI_PASS checks={checks};zh+en;nativeRunAndRngUnchanged=true");
                    return;
                }
                File.WriteAllText(Path.Combine(output, "drawing-ui-result.json"), JsonSerializer.Serialize(new
                { passed = true, checks, currentActOnly = true, independentRewards = true,
                    goldChoice = true, premisesRetained = true, moreAndDiagnosticsRemoved = true,
                    nativeRunAndRngUnchanged = true }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"DRAWING_FORECAST_UI_PASS checks={checks};nativeRunAndRngUnchanged=true");
                return;
            }
            Check(!panel.DisplayedDraft.CanUndo && panel.FindChild("Undo", true, false) == null,
                "native authored strokes add no independent Undo entry or control");
            var old = panel.DisplayedDraft.Selected;
            var alternate = run.Map.GetPointsInRow(first.coord.row).First(p => p.coord != first.coord);
            var other = InRunMapSnapshot.Position(alternate.coord);
            await Stroke([At(a), At(other)]);
            Check(panel.DisplayedDraft.Selected.SequenceEqual(old), "an ambiguous native row landing preserves old plan");
            screen.Drawings.ClearDrawnLinesLocal();
            var offset = new Vector2(18, 0);
            await Stroke([At(a) + offset, At(b) + offset]);
            Check(panel.DisplayedDraft.Selected.SequenceEqual([a, b]),
                "graph recognition accepts a visibly offset native stroke without exact icon intersection");
            await Stroke([At(a)], MouseButton.Middle);
            Check(panel.DisplayedDraft.Selected.SequenceEqual([b]) && panel.DisplayedDraft.ReadyPrefix.IsEmpty,
                "native local eraser removes only its narrow hit and retains future intent");
            var erased = panel.DisplayedDraft.Selected;
            panel.OnMapChanged(); await Frame();
            Check(panel.DisplayedDraft.Selected.SequenceEqual(erased),
                "refresh never rescans old native ink to restore erased intent");
            await Stroke([At(a)]);
            Check(panel.PredictionEnabled && panel.DisplayedDraft.Selected.SequenceEqual([a, b]),
                "default-enabled prediction observes newly authored native local strokes");
            var shared = first;
            var prefix = new List<MapPosition> { a };
            while (shared.Children.Count == 1)
            { shared = shared.Children.First(); prefix.Add(InRunMapSnapshot.Position(shared.coord)); }
            Check(shared.Children.Count > 1, "native map supplies a reachable shared-row branch for rerouting");
            var sharedPosition = InRunMapSnapshot.Position(shared.coord);
            var oldChild = InRunMapSnapshot.Position(shared.Children.First().coord);
            var newChild = InRunMapSnapshot.Position(shared.Children.Skip(1).First().coord);
            screen.Drawings.ClearDrawnLinesLocal();
            await Stroke(prefix.Append(oldChild).Select(At).ToArray());
            await Stroke([At(sharedPosition), At(newChild)]);
            Check(panel.DisplayedDraft.Selected.Contains(newChild) && !panel.DisplayedDraft.Selected.Contains(oldChild) &&
                prefix.All(panel.DisplayedDraft.Selected.Contains),
                "latest same-common-row native branch replaces old branch while retaining its earlier prefix");
            screen.Drawings.ClearDrawnLinesLocal();
            await Stroke([At(a), At(b)]);
            await Picture("in-run-map-current.png");
            await CheckScrolledInk(nativePoints.ToDictionary(p => p.Key, p => (Control)p.Value), "native");

            // Source-native browsing dispatch is unchanged for left/wheel input.
            var processMouse = typeof(NMapScreen).GetMethod("ProcessMouseEvent", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var processScroll = typeof(NMapScreen).GetMethod("ProcessScrollEvent", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var target = typeof(NMapScreen).GetField("_targetDragPos", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Vector2 scrollBefore = (Vector2)target.GetValue(screen)!;
            processMouse.Invoke(screen, [new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }]);
            processMouse.Invoke(screen, [new InputEventMouseMotion { Relative = new(0, 80) }]);
            processMouse.Invoke(screen, [new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }]);
            Check(((Vector2)target.GetValue(screen)!).Y != scrollBefore.Y, "original left dragging remains active");
            scrollBefore = (Vector2)target.GetValue(screen)!;
            processScroll.Invoke(screen, [new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }]);
            Check(((Vector2)target.GetValue(screen)!).Y != scrollBefore.Y, "original wheel browsing remains active");
            var actualSelected = panel.DisplayedDraft.Selected;
            root.GetWindow().Size = new(1900, 1355); await Frame();
            await CheckScrolledInk(nativePoints.ToDictionary(p => p.Key, p => (Control)p.Value), "native-resized");
            root.GetWindow().Size = new(1680, 1080); await Frame();
            Check(panel.PredictionEnabled && panel.DisplayedDraft.Map.ActIndex == run.CurrentActIndex &&
                screen.GetNode<Control>("TheMap/Points").Visible && panel.DisplayedDraft.Selected.SequenceEqual(actualSelected) &&
                map.FindChild("RolltheSpire2_IndependentMap", true, false) == null,
                "forecast retains current-Act native points and authored route intent without a separate mode");
            Native("OnMapDrawingButtonPressed", screen.GetNode<NMapDrawButton>("%DrawButton"));
            Check(NativeDrawingInput() is NMapDrawingInput && screen.Drawings.GetLocalDrawingMode() == DrawingMode.Drawing,
                "native feather owns its original drawing lifecycle with default prediction");
            Native("OnMapDrawingButtonPressed", screen.GetNode<NMapDrawButton>("%DrawButton"));
            Check(NativeDrawingInput() == null && screen.Drawings.GetLocalDrawingMode() == DrawingMode.None,
                "original native feather stops normally with default prediction");
            var allDrafts = (Dictionary<int, PredictorMapRouteDraft>)Field(panel, "_drafts");
            Check(allDrafts.Count == 1 && allDrafts.ContainsKey(run.CurrentActIndex) && !panel.DisplayedDraft.Selected.IsEmpty,
                "only the actual current Act owns an editable route draft");
            Native("OnClearMapDrawingButtonPressed", screen.GetNode<NMapClearButton>("%ClearButton"));
            Check(allDrafts.Values.All(draft => draft.Selected.IsEmpty && !draft.CanUndo && !draft.Undo()) && NativeDrawingInput() == null,
                "native local clear cancels current route intent with no independent Undo restoration");
            panel.OnMapChanged(); await Frame();
            Check(allDrafts.Values.All(draft => draft.Selected.IsEmpty), "refresh after local clear never reinterprets old ink");
            Check(panel.DisplayedDraft!.Map.ActIndex == run.CurrentActIndex && panel.PredictionEnabled,
                "information fixture remains on the actual current Act with prediction enabled");
            Check(Signature() == before && ReferenceEquals(graphReference, run.Map), "all native drawings, tools and visibility changes leave native run/RNG unchanged");
            await RunInRunMapInformationFlow(root, output, screen, panel, run, nativePoints, Signature, Check);
            await CheckMapFeatureLifecycle(root, panel, Check, () => Stroke([At(a)], MouseButton.Middle));

            // Later Acts are actual fixture states with native SetMap geometry,
            // not projections selected while still in Act 1. Prior room facts
            // supply a coherent total floor without inventing old coordinates.
            var managerState = typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var mapVotesProperty = typeof(RunManager).GetProperty(nameof(RunManager.MapSelectionSynchronizer))!;
            var queueSetProperty = typeof(RunManager).GetProperty(nameof(RunManager.ActionQueueSet))!;
            var queueSyncProperty = typeof(RunManager).GetProperty(nameof(RunManager.ActionQueueSynchronizer))!;
            var originalVotes = RunManager.Instance.MapSelectionSynchronizer;
            var originalQueues = RunManager.Instance.ActionQueueSet;
            var originalQueueSync = RunManager.Instance.ActionQueueSynchronizer;
            var onVoteChanged = typeof(NMapScreen).GetMethod("OnPlayerVoteChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate<Action<Player, MapVote?, MapVote?>>(screen);
            var onVoteCancelled = typeof(NMapScreen).GetMethod("OnPlayerVoteCancelled", BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate<Action<Player>>(screen);
            try
            {
                foreach (int actIndex in new[] { 1, 2 })
                {
                    var laterPlayer = Player.CreateForNewRun<Ironclad>(UnlockState.all, player.NetId);
                    var laterRun = RunState.CreateForTest([laterPlayer], seed: "000000067C0S", ascensionLevel: 10);
                    // Votes use player object identity, not merely matching
                    // NetIds. Supply the native vote/queue owners for this Run,
                    // and retain the real screen vote APIs and subscriptions.
                    var laterQueues = new ActionQueueSet(laterRun.Players);
                    var laterBuffer = new RunLocationTargetedMessageBuffer(RunManager.Instance.NetService);
                    var laterQueueSync = new ActionQueueSynchronizer(laterRun, laterQueues, laterBuffer, RunManager.Instance.NetService);
                    var laterVotes = new MapSelectionSynchronizer(RunManager.Instance.NetService, laterQueueSync, laterRun);
                    originalVotes.PlayerVoteChanged -= onVoteChanged;
                    originalVotes.PlayerVoteCancelled -= onVoteCancelled;
                    laterVotes.PlayerVoteChanged += onVoteChanged;
                    laterVotes.PlayerVoteCancelled += onVoteCancelled;
                    queueSetProperty.SetValue(RunManager.Instance, laterQueues);
                    queueSyncProperty.SetValue(RunManager.Instance, laterQueueSync);
                    mapVotesProperty.SetValue(RunManager.Instance, laterVotes);
                    try
                    {
                        managerState.SetValue(RunManager.Instance, laterRun);
                        typeof(RunManager).GetMethod("InitializeNewRun", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(RunManager.Instance, null);
                        RunManager.Instance.GenerateRooms();
                        foreach (var act in laterRun.Acts)
                        { act.SetBossEncounter(ModelDb.Encounter<KaiserCrabBoss>()); act.SetSecondBossEncounter(null); }
                        for (int previous = 0; previous < actIndex; previous++)
                        {
                            laterRun.CurrentActIndex = previous;
                            laterRun.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Boss,
                                RoomType.Boss, ModelDb.Encounter<KaiserCrabBoss>().Id);
                        }
                        laterRun.CurrentActIndex = actIndex;
                        laterRun.Map = StandardActMap.CreateFor(laterRun, false);
                        // Capture at the same before-entry boundary as the product
                        // observer, then publish an observed completed entry task.
                        var start = laterRun.Map.StartingMapPoint.coord;
                        var entry = PredictorLiveRunCapture.CaptureBeforeNodeEntry(laterRun, runtime.Detection.NormalizedVersion, start)!;
                        laterRun.AddVisitedMapCoord(start);
                        laterRun.AppendToMapPointHistory(laterRun.Map.StartingMapPoint.PointType, RoomType.Event, laterRun.Act.Ancient.Id);
                        PredictorLiveRunCapture.RecordNodeEntryTask(laterRun, start, Task.CompletedTask);
                        string LaterFacts() => JsonSerializer.Serialize(new { laterRun.CurrentActIndex, laterRun.TotalFloor,
                            laterRun.VisitedMapCoords, laterRun.MapPointHistory, Map = SerializableActMap.FromActMap(laterRun.Map),
                            Rng = laterRun.Rng.ToSerializable(), Player = laterPlayer.ToSerializable() });
                        string laterBefore = LaterFacts();
                        laterBuffer.OnLocationChanged(laterRun.RunLocation);
                        laterVotes.OnLocationChanged(laterRun.MapLocation);
                        laterVotes.BeforeMapGenerated();
                        Check(laterVotes.GetVote(laterPlayer) == null,
                            $"actual Act {actIndex + 1} native vote owner resolves its own player instance");
                        screen.Initialize(laterRun);
                        screen.SetMap(laterRun.Map, laterRun.Rng.Seed, true);
                        typeof(NMapScreen).GetField("_hasPlayedAnimation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(screen, true);
                        panel.OnMapChanged();
                        typeof(InRunMapPredictionPanel).GetMethod("QueueInformationRefresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
                        await Frame(6);
                        var laterSession = (InRunMapPredictionSession)Field(panel, "_prediction");
                        var startPosition = InRunMapSnapshot.Position(start);
                        Check(panel.DisplayedDraft!.Map.ActIndex == actIndex && screen.GetNode<Control>("TheMap/Points").Visible &&
                            ((Dictionary<int, PredictorMapRouteDraft>)Field(panel, "_drafts")).Keys.SequenceEqual([actIndex]) &&
                            panel.FindChild("ActSwitch", true, false) == null,
                            $"actual Act {actIndex + 1} binds its own native map and current-Act draft");
                        Check(ReferenceEquals(laterSession.Snapshot, entry) && laterSession.IsCurrentNodeReplay &&
                            laterSession.Nodes.TryGetValue((actIndex, startPosition), out var ancient) &&
                            ancient.Ancient is { Offers.IsEmpty: false } && laterSession.Nodes.Keys.All(key => key.ActIndex == actIndex),
                            $"actual Act {actIndex + 1} Ancient options use its observed live entry boundary");
                        Check(LaterFacts() == laterBefore, $"actual Act {actIndex + 1} UI prediction leaves native inventory, history and RNG unchanged");
                        var laterPoints = screen.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>()
                            .Where(point => !point.IsQueuedForDeletion()).ToDictionary(point => InRunMapSnapshot.Position(point.Point.coord), point => (Control)point);
                        await CheckScrolledInk(laterPoints, $"actual-act-{actIndex + 1}");
                    }
                    finally
                    {
                        laterVotes.PlayerVoteChanged -= onVoteChanged;
                        laterVotes.PlayerVoteCancelled -= onVoteCancelled;
                        mapVotesProperty.SetValue(RunManager.Instance, originalVotes);
                        queueSetProperty.SetValue(RunManager.Instance, originalQueues);
                        queueSyncProperty.SetValue(RunManager.Instance, originalQueueSync);
                        originalVotes.PlayerVoteChanged += onVoteChanged;
                        originalVotes.PlayerVoteCancelled += onVoteCancelled;
                        laterQueueSync.Dispose(); laterQueues.Reset();
                    }
                }
            }
            finally { managerState.SetValue(RunManager.Instance, run); }
            var newPlayer = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            var newRun = RunState.CreateForTest([newPlayer], seed: "000000001234", ascensionLevel: 10);
            newRun.Map = StandardActMap.CreateFor(newRun, false);
            InRunMapPredictionPanel.EnsureAttached(screen, newRun, runtime);
            Check(ReferenceEquals(panel, screen.GetNode<InRunMapPredictionPanel>(InRunMapPredictionPanel.NodeName))
                && panel.PredictionEnabled && panel.DisplayedDraft!.Map.Seed == newRun.Rng.StringSeed
                && panel.DisplayedDraft.Selected.IsEmpty && !panel.DisplayedDraft.CanUndo,
                "rebound native screen clears old run draft/history and binds new run");
            Check(Signature() == before, "rebind leaves previous run unchanged");
            File.WriteAllText(Path.Combine(output, "in-run-map-result.json"), JsonSerializer.Serialize(new
            { passed = true, checks, nativeScene = true, nativeSetMap = true, productionPanel = true,
                realRunAndRngUnchanged = true, currentActOnly = true, actualAct2And3AncientEntry = true, nativeMapReadyOpenClose = true, nativeGlobalUiParent = true,
                nativeTopBarGeometry = true, nativeTopBarHideShow = true, visibleStripMouseInput = true,
                residentObservations = true, completeRewardHover = "rendered-with-view-process-suspended;continuous-region-geometry-checked",
                mouseHoverLifecycle = "SubViewport-real-GUI-motion-and-clicks;short-source-to-fixed-main-and-adaptive-result-sides;90ms-exit-without-click;revision-rebind;left/right/bottom-long/prerequisites-long/800x720;blank/Escape/hide/ExitTree;native-script-map-entry-still-Owner-validation",
                adaptiveCardPlacement = "in-run-map-adaptive-card-placement.json;source-relative-main;cards-default-above;child-information-default-below;retained-sides-without-overlap",
                sourceAssociation = "independent-frame;passive-highlight;native-position/parent/Z/ZAsRelative/input-unchanged;replacement/hide-paused/ExitTree",
                readOnlyRewardPreview = "actual-route-copy;take/skip/reroll/sacrifice;revision-expiry;bounded-cache;native-and-session-signatures",
                semanticTakeAndSkip = true,
                persistentNodeEditor = true, detachedSuffixRecompute = true, nativeDepartureFlag = true,
                scrolledRouteInk = "actual-Act1/Act2/Act3/resized;bottom/middle/top;center-alignment-and-rendered-pixels",
                globalUiSetup = "isolated-property-bindings;unrelated-ready-and-context-wiring-bypassed", audio = "uninitialized-host;SfxCmd.Play-bypassed",
                animatedSpine = "unavailable-in-stock-host;static-native-boss-fixture",
                nativeDrawingLifecycle = "screen-right-dispatch;native-held-ready-motion-release;ordinary-native-lines-preserved",
                nativeInitialPointer = "fixture-BeginLineLocal-start-coordinate;offscreen-DisplayServer-pointer-0,0;live-pointer-Owner-validation-required",
                nativeIncrementalRecognition = "reverse;offset;shared-row-reroute;display-off-observation;no-old-ink-rescan;current-act-clear;no-native-Undo",
                harmonyAttachment = "native-screen-attachment-and-local-drawing-observers-installed;native-travel-and-tool-hooks-absent" }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"IN_RUN_MAP_UI_PASS checks={checks};nativeScene=true;currentActOnly=true;actualActs2And3=true;realRunAndRngUnchanged=true;harmonyAttachment=InstalledAndDispatched");
        }
        finally
        {
            _inRunMapFixtureNativeStart = null;
            global.Free();
            productHarmony.UnpatchAll(productHarmony.Id);
            fixtureHarmony.UnpatchAll(fixtureHarmony.Id);
            typeof(NGame).GetProperty("Instance")!.SetValue(null, oldGame);
            foreach (Node node in new Node[] { game, sceneContainer, runNode, input, controller, cursor, hotkeys, remoteCursors }) node.Free();
            RunManager.Instance.CleanUp(); LocalContext.NetId = oldLocal; TestMode.IsOn = false;
            if (fixtureDebugAction) InputMap.EraseAction("debug_hide_top_bar");
        }
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren()) foreach (Node descendant in Descendants(child)) yield return descendant;
    }
    private static bool InRunMapSkipUnrelatedUiSetup() => false;
    private static bool InRunMapStaticBossReady(NBossMapPoint __instance)
    {
        // Stock Godot lacks the game's compiled Spine class. Reproduce only the
        // source static-boss branch in this isolated fixture; product code keeps
        // its original native animated branch and receives no host fallback.
        typeof(NMapPoint).GetMethod("ConnectSignals", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(__instance, null);
        __instance.Disable();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var visuals = __instance.GetNode<Node2D>("SpriteContainer");
        var unavailableSpine = visuals.GetNode("SpineSprite");
        visuals.RemoveChild(unavailableSpine); unavailableSpine.Free();
        visuals.AddChild(new Node2D { Name = "SpineSprite", Visible = false });
        typeof(NBossMapPoint).GetField("_spriteContainer", flags)!.SetValue(__instance, visuals);
        typeof(NBossMapPoint).GetField("_usesSpine", flags)!.SetValue(__instance, false);
        var image = visuals.GetNode<TextureRect>("PlaceholderImage");
        var outline = image.GetNode<TextureRect>("PlaceholderOutline");
        var act = (ActModel)typeof(NBossMapPoint).GetField("_act", flags)!.GetValue(__instance)!;
        var boss = act.BossEncounter;
        image.Texture = PreloadManager.Cache.GetAsset<Texture2D>(boss.BossNodePath + ".png");
        outline.Texture = PreloadManager.Cache.GetAsset<Texture2D>(boss.BossNodePath + "_outline.png");
        image.Show(); outline.Modulate = act.MapBgColor;
        typeof(NBossMapPoint).GetField("_placeholderImage", flags)!.SetValue(__instance, image);
        typeof(NBossMapPoint).GetField("_placeholderOutline", flags)!.SetValue(__instance, outline);
        typeof(NBossMapPoint).GetMethod("RefreshColorInstantly", flags)!.Invoke(__instance, null);
        return false;
    }
}
