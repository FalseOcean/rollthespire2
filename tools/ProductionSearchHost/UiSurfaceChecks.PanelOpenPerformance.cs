using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Rendered, isolated entry measurement. Each invocation measures one scene
    // in a fresh host process. ModelDb/save/scene setup is deliberately outside
    // the click boundary; first-open means shell-cold, resources/scene warm.
    internal static async Task RunPanelOpenPerformanceAsync(Node root, string output, string pck, string scene)
    {
        string[] panelArgs = OS.GetCmdlineUserArgs();
        // Supplementary process-CPU comparisons need equal idle draw work.
        // Uncapped hidden windows were observed at 60 versus >2000 frames/sec.
        if (panelArgs.Contains("--menu-preparation-cpu-60fps")) Engine.MaxFps = 60;
        int preparationAt = Array.IndexOf(panelArgs, "--menu-preparation");
        string? preparationMode = preparationAt < 0 ? null : preparationAt + 1 < panelArgs.Length
            ? panelArgs[preparationAt + 1] : throw new ArgumentException("--menu-preparation needs immediate|partial|ready");
        if (preparationMode is not (null or "immediate" or "partial" or "ready"))
            throw new ArgumentException("--menu-preparation needs immediate|partial|ready");
        if (preparationMode is not null && scene != "main-menu")
            throw new ArgumentException("--menu-preparation measures the main-menu window");
        int fixedWaitAt = Array.IndexOf(panelArgs, "--menu-preparation-baseline-wait-ms");
        double baselineWaitMs = preparationMode == "ready" ? 1500 : preparationMode == "partial" ? 250 : 0;
        if (fixedWaitAt >= 0 && (fixedWaitAt + 1 >= panelArgs.Length || !double.TryParse(panelArgs[fixedWaitAt + 1],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out baselineWaitMs) ||
            !double.IsFinite(baselineWaitMs) || baselineWaitMs < 0 || baselineWaitMs >= 15000))
            throw new ArgumentException("--menu-preparation-baseline-wait-ms needs a finite 0..14999 value");
        bool treeDiagnostic = OS.GetCmdlineUserArgs().Contains("--panel-tree-diagnostic");
        bool navigationPerformance = OS.GetCmdlineUserArgs().Contains("--panel-navigation-performance");
        bool lightPagePerformance = panelArgs.Contains("--light-page-performance");
        if (panelArgs.Contains("--light-page-invalidation") && !lightPagePerformance)
            throw new ArgumentException("--light-page-invalidation requires --light-page-performance");
        if (lightPagePerformance) navigationPerformance = true;
        if (treeDiagnostic && navigationPerformance) throw new ArgumentException("Choose timing/navigation or tree diagnostics separately");
        if (treeDiagnostic && scene == "main-menu") throw new ArgumentException("Tree diagnostic uses one in-run/Crystal first open");
        if (scene is not ("main-menu" or "in-run" or "crystal")) throw new ArgumentException("Unknown panel-open scene");
        if (DisplayServer.GetName() == "headless" || RenderingServer.GetRenderingDevice() is null)
            throw new InvalidOperationException("Panel open performance requires a rendered GPU host; headless is not first-frame evidence");
        if (RunManager.Instance.IsInProgress || NGame.Instance is not null)
            throw new InvalidOperationException("Panel performance requires an isolated host with no player's run");
        Directory.CreateDirectory(output);
        string? expectedUserDataRoot = System.Environment.GetEnvironmentVariable("RT2_PANEL_EXPECT_USER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(expectedUserDataRoot) && !Path.GetFullPath(OS.GetUserDataDir()).StartsWith(
            Path.GetFullPath(expectedUserDataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Panel fixture user data escaped the local evidence directory");
        if (!ProjectSettings.LoadResourcePack(pck, false)) throw new InvalidOperationException("Game PCK missing");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(NGame).Assembly);
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null,
            MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        TestMode.IsOn = true;
        ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
        var save = new SaveManager(new MegaCrit.Sts2.Core.Saves.Test.MockGodotFileIo("user://panel-open-memory"), true);
        SaveManager.MockInstanceForTesting(save);
        save.InitSettingsDataForTest(); save.InitPrefsDataForTest(); save.InitProfileId(1); save.InitProgressData();
        string? progressFixture = System.Environment.GetEnvironmentVariable("RT2_PANEL_PROGRESS_PATH");
        if (!string.IsNullOrWhiteSpace(progressFixture))
        {
            var read = SaveManager.FromJson<SerializableProgress>(File.ReadAllText(progressFixture));
            if (!read.Success || read.SaveData is null) throw new InvalidOperationException("Panel progress fixture could not be read");
            var context = new MegaCrit.Sts2.Core.Saves.Validation.DeserializationContext();
            save.Progress = ProgressState.FromSerializable(read.SaveData, context);
            File.WriteAllText(Path.Combine(output, "progress-fixture-validation.json"), JsonSerializer.Serialize(new {
                status = read.Status.ToString(), context.WarningCount, context.FatalCount, errors = context.Errors
            }, new JsonSerializerOptions { WriteIndented = true }));
            // Preserve all unknown historical Mod-ID warnings; reject fatal or
            // recovered reads. Effective environment equality is checked separately.
            if (context.HasFatal || read.Status != ReadSaveStatus.Success)
                throw new InvalidOperationException("Panel progress fixture was not an exact nonfatal import");
        }
        save.SettingsSave.Language = System.Environment.GetEnvironmentVariable("RT2_PANEL_LOCALE") ?? "eng";
        LocManager.Initialize();
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
            Detection = new("0.111.0", "0.111.0", "beta", "PanelOpenPerformance", true, "") };
        var oldRuntime = ModRuntime.Snapshot;
        var oldLocal = LocalContext.NetId;
        var oldSync = RunManager.Instance.EventSynchronizer;
        var workspace = SearchWorkspace.Open(OS.GetUserDataDir());
        bool oldEntryPreference = workspace.Preferences.ShowInRunPredictionEntry;
        // Isolated host configuration only: no preference file write.
        workspace.Preferences.ShowInRunPredictionEntry = true;
        const string backAction = "mega_pause_and_back";
        bool addedBackAction = !InputMap.HasAction(backAction);
        if (addedBackAction) InputMap.AddAction(backAction);
        typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!.SetValue(null, runtime);
        root.GetWindow().Size = new(1600, 900);
        RenderingServer.SetDefaultClearColor(new Color("18202b"));
        var viewport = new SubViewport { Name = "PanelOpenNativeViewport", Size = new(1600, 900),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = true };
        var display = new TextureRect { Name = "PanelOpenNativeDisplay", Texture = viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(viewport); root.AddChild(display); display.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        viewport.NotifyMouseEntered();
        var game = new PanelOpenGameFixture { Name = "PanelOpenGame", MouseFilter = Control.MouseFilterEnum.Ignore };
        var detached = new List<Node>();
        EventModel? liveEvent = null;
        EventSynchronizer? synchronizer = null;
        RunState? run = null;
        Harmony? diagnosticPatch = null;
        PanelMenuPreparationMeasurement? menuMeasurement = null;
        using var lightProbe = LightPageProbe.Start();
        object? menuBeforeClick = null;
        JsonElement? menuFirstAfterEntry = null;
        string Signature() => run is null ? "outside-run" : JsonSerializer.Serialize(new {
            rng = run.Rng.ToSerializable(), players = run.Players.Select(player => player.ToSerializable()),
            shared = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
            personal = run.Players.Select(player => PredictorLiveRunCapture.Bag(player.RelicGrabBag)),
            eventRng = liveEvent?.Rng.ToSerializable() });
        try
        {
            if (treeDiagnostic)
            {
                _panelTreeObservation = new PanelTreeObservation();
                diagnosticPatch = new Harmony("rt2.panel.tree.diagnostic");
                diagnosticPatch.Patch(AccessTools.Method(typeof(RunPredictionOverlay), "Initialize"),
                    postfix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(PanelTreeInitialized)));
                diagnosticPatch.Patch(AccessTools.Method(typeof(RunPredictionOverlay), "AfterOverlayOpened"),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(PanelTreeAttached)));
            }
            var container = new NSceneContainer(); detached.Add(container);
            PanelOpenSet(game, nameof(NGame.RootSceneContainer), container);
            var input = new NInputManager(); var controller = new NControllerManager(); var hotkeys = new NHotkeyManager();
            detached.AddRange([input, controller, hotkeys]);
            PanelOpenSet(input, nameof(NInputManager.ControllerManager), controller);
            PanelOpenSet(game, nameof(NGame.InputManager), input); PanelOpenSet(game, nameof(NGame.HotkeyManager), hotkeys);
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, game);
            viewport.AddChild(game); game.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            Button launcher;
            Func<WorkspaceShell?> shell;
            if (scene == "main-menu")
            {
                var menu = new PanelOpenMenuFixture(); var stack = new PanelOpenMenuStackFixture();
                var patchNotes = new NPatchNotesScreen(); detached.Add(patchNotes);
                PanelOpenSet(menu, nameof(NMainMenu.SubmenuStack), stack);
                PanelOpenSet(menu, nameof(NMainMenu.PatchNotesScreen), patchNotes);
                PanelOpenSet(container, nameof(NSceneContainer.CurrentScene), menu);
                game.AddChild(menu); menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                menu.AddChild(stack); stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                // Native Push/Pop and RT2's actual deferred push remain intact.
                // The stack has no native main-menu backstop in this fixture.
                if (preparationMode is not null)
                    menuMeasurement = new(root, viewport, game, preparationMode, baselineWaitMs, output);
                AppShellHost.EnsureAttached(menu, runtime);
                var entry = menu.GetNode<AppShellHostRoot>("RolltheSpire2_WorkspaceLauncherRoot");
                menuMeasurement?.AfterAttach(entry);
                launcher = (Button)PanelOpenField(entry, "_launcher")!;
                shell = () => PanelOpenField(entry, "_topLevelSurface") is { } surface
                    ? (WorkspaceShell?)PanelOpenField(surface, "_shell") : null;
            }
            else
            {
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                run = RunState.CreateForTest([player], seed: "000000000000", ascensionLevel: 10);
                RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), false);
                RunManager.Instance.GenerateRooms(); LocalContext.NetId = player.NetId;
                if (scene == "crystal")
                {
                    run.CurrentActIndex = 1; player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
                    liveEvent = ModelDb.Event<CrystalSphere>().ToMutable();
                    await liveEvent.BeginEvent(player, null, false);
                    run.PushRoom(new EventRoom(ModelDb.Event<CrystalSphere>()));
                    synchronizer = RunManager.Instance.EventSynchronizer;
                    var events = (List<EventModel>)PanelOpenField(synchronizer, "_events")!;
                    events.Clear(); events.Add(liveEvent);
                }
                else run.PushRoom(new RestSiteRoom());
                var runNode = new NRun(); detached.Add(runNode);
                var rooms = new NSceneContainer(); detached.Add(rooms);
                PanelOpenSet(runNode, "_roomContainer", rooms);
                PanelOpenSet(runNode, "_state", run);
                var global = new PanelOpenGlobalFixture(); var topBar = new PanelOpenTopBarFixture();
                var stack = new EntryOverlayStackFixture();
                var map = new NMapScreen(); var capstones = new NCapstoneContainer(); detached.AddRange([map, capstones]);
                PanelOpenSet(container, nameof(NSceneContainer.CurrentScene), runNode);
                PanelOpenSet(runNode, nameof(NRun.GlobalUi), global);
                PanelOpenSet(global, nameof(NGlobalUi.Overlays), stack);
                PanelOpenSet(global, nameof(NGlobalUi.MapScreen), map);
                PanelOpenSet(global, nameof(NGlobalUi.CapstoneContainer), capstones);
                PanelOpenSet(global, nameof(NGlobalUi.TopBar), topBar);
                var debug = new Control { Position = new(1500, 90), Size = new(80, 50) };
                PanelOpenSet(global, nameof(NGlobalUi.DebugInfo), debug);
                game.AddChild(global); global.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                global.AddChild(debug); global.AddChild(topBar); global.AddChild(stack);
                stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                InRunPredictionLauncher.EnsureAttached(topBar, run, runtime);
                launcher = global.GetNode<InRunPredictionLauncher>("RolltheSpire2_RunPrediction");
                shell = () => PanelOpenField(launcher, "_surface") is { } surface
                    ? (WorkspaceShell?)PanelOpenField(surface, "_shell") : null;
            }
            TestMode.IsOn = false;
            // In the game, entering a run follows the bootstrap/main-menu
            // authority capture. Supply that existing prerequisite here.
            if (scene != "main-menu") RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
            if (menuMeasurement is null)
                for (int frame = 0; frame < 8; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            string before = Signature();
            var samples = new List<object>();
            var pageInstances = new Dictionary<string, ulong>();
            for (int sample = 0; sample < (treeDiagnostic ? 1 : 4); sample++)
            {
                lightProbe?.SetContext(sample, "Entry");
                if (sample == 0 && menuMeasurement is not null) await menuMeasurement.WaitBeforeClickAsync();
                else for (int frame = 0; frame < 6; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                long launcherWait = Stopwatch.GetTimestamp();
                while ((!launcher.IsVisibleInTree() || launcher.Disabled) && Stopwatch.GetElapsedTime(launcherWait).TotalSeconds < 5)
                    await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!launcher.IsVisibleInTree() || launcher.Disabled) throw new InvalidOperationException("Actual launcher unavailable");
                if (sample == 0 && menuMeasurement is not null) menuBeforeClick = menuMeasurement.SealBeforeClick();
                var frameIntervals = new List<double>();
                var postFirstFrameIntervals = new List<double>();
                long lastFrame = Stopwatch.GetTimestamp(), firstPreDrawAt = 0, firstRenderedAt = 0;
                void OnFrame()
                {
                    long now = Stopwatch.GetTimestamp();
                    double elapsedMs = Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds;
                    frameIntervals.Add(elapsedMs);
                    // Exclude the interval crossing the first rendered frame:
                    // it also contains pre-first-frame work.
                    if (firstRenderedAt != 0 && lastFrame >= firstRenderedAt) postFirstFrameIntervals.Add(elapsedMs);
                    lastFrame = now;
                }
                void OnPreDraw()
                {
                    if (firstPreDrawAt == 0 && shell()?.IsVisibleInTree() == true) firstPreDrawAt = Stopwatch.GetTimestamp();
                }
                root.GetTree().ProcessFrame += OnFrame;
                RenderingServer.FramePreDraw += OnPreDraw;
                bool frameObserverConnected = true;
                long clicked = Stopwatch.GetTimestamp();
                try
                {
                    PanelOpenClick(viewport, launcher);
                    double synchronousDispatchMs = Stopwatch.GetElapsedTime(clicked).TotalMilliseconds;
                    WorkspaceShell? current;
                    do
                    {
                        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        current = shell();
                        if (Stopwatch.GetElapsedTime(clicked).TotalSeconds > 30) throw new TimeoutException("Panel did not render");
                    } while (current?.IsVisibleInTree() != true);
                    firstRenderedAt = Stopwatch.GetTimestamp();
                    double firstRenderedFrameMs = Stopwatch.GetElapsedTime(clicked, firstRenderedAt).TotalMilliseconds;
                    if (firstPreDrawAt == 0) throw new InvalidOperationException("Visible shell first PreDraw was not observed");
                    double firstPreDrawMs = Stopwatch.GetElapsedTime(clicked, firstPreDrawAt).TotalMilliseconds;
                    int crystalTilesAtShellFirstFrame = scene == "crystal" && PanelOpenField(current, "_crystalScene") is Control firstCrystal
                        ? ((System.Collections.IDictionary)PanelOpenField(firstCrystal, "_optionTiles")!).Count : -1;
                    if (treeDiagnostic && _panelTreeObservation is { } treeObservation)
                    {
                        treeObservation.Stages.Add(new("first-post-draw", Stopwatch.GetTimestamp()));
                        treeObservation.AfterAttach = PanelTreeInventory((Node)PanelOpenField(launcher, "_surface")!);
                    }
                    int currentPageReadyRenderedFrames = 0;
                    while (!PanelOpenCurrentPageReady(current, scene))
                    {
                        if (Stopwatch.GetElapsedTime(clicked).TotalSeconds > 30)
                            throw new TimeoutException("Shell rendered but its complete current page did not become usable");
                        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        currentPageReadyRenderedFrames++;
                    }
                    double currentPageUsableMs = Stopwatch.GetElapsedTime(clicked).TotalMilliseconds;
                    JsonElement? menuPreparationAfterEntry = menuMeasurement?.SnapshotCurrentStatus();
                    object? menuPreparationProcessCpu = sample == 0 ? menuMeasurement?.CaptureFirstEntryProcessCpu() : null;
                    if (sample == 0) menuFirstAfterEntry = menuPreparationAfterEntry;
                    double currentPageLongestFrameIntervalMs = frameIntervals.DefaultIfEmpty().Max();
                    if (scene == "in-run" && PanelOpenField(current, "_predictor") is not RolltheSpire2.Ui.Pages.Analysis.AnalysisPage { LastDocument: not null })
                        throw new InvalidOperationException("Ordinary run did not produce its supported initial prediction");
                    Control? crystalPanel = scene == "crystal"
                        ? (Control)PanelOpenField(PanelOpenField(launcher, "_surface")!, "_panel")! : null;
                    if (crystalPanel is not null && PanelOpenField(crystalPanel, "_snapshot") is null)
                        throw new InvalidOperationException("Crystal entry did not capture its current board");
                    var crystalTiles = crystalPanel is null ? null
                        : (System.Collections.IDictionary)PanelOpenField(crystalPanel, "_optionTiles")!;
                    int initialPageTileCountAtFirstFrame = crystalTilesAtShellFirstFrame;
                    var initialPageTileArrivals = new List<object>();
                    if (crystalTiles is not null)
                        initialPageTileArrivals.Add(new { renderedFrameAfterFirst = 0, afterClickMs = firstRenderedFrameMs,
                            tileCount = initialPageTileCountAtFirstFrame });
                    int previousTileCount = initialPageTileCountAtFirstFrame, initialPageRenderedFrames = currentPageReadyRenderedFrames;
                    void ObserveInitialPage()
                    {
                        if (!current.IsVisibleInTree() || (crystalPanel is not null && !crystalPanel.IsVisibleInTree()))
                            throw new InvalidOperationException("Initial page hidden during visible-page observation");
                        if (crystalTiles is not null && crystalTiles.Count != previousTileCount)
                        {
                            previousTileCount = crystalTiles.Count;
                            initialPageTileArrivals.Add(new { renderedFrameAfterFirst = initialPageRenderedFrames,
                                afterClickMs = Stopwatch.GetElapsedTime(clicked).TotalMilliseconds, tileCount = previousTileCount });
                        }
                    }
                    // Ordinary runs preserve the visible-page observation. The
                    // optional navigation pass visits immediately after current
                    // page readiness; Crystal always keeps its result dwell.
                    int requiredDwellMs = navigationPerformance && scene != "crystal" ? 0 : 1800;
                    int requiredDwellFrames = requiredDwellMs == 0 ? 0 : 12;
                    ObserveInitialPage();
                    while (Stopwatch.GetElapsedTime(firstRenderedAt).TotalMilliseconds < requiredDwellMs || initialPageRenderedFrames < requiredDwellFrames)
                    {
                        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        initialPageRenderedFrames++;
                        ObserveInitialPage();
                        if (Stopwatch.GetElapsedTime(firstRenderedAt).TotalSeconds > 10)
                            throw new TimeoutException("Initial page did not complete its bounded rendered dwell");
                    }
                    double initialPageDwellMs = Stopwatch.GetElapsedTime(firstRenderedAt).TotalMilliseconds;
                    int initialPageTileCount = crystalTiles?.Count ?? -1;
                    if (crystalTiles is not null && initialPageTileCount == 0)
                        throw new InvalidOperationException("Crystal results did not arrive during visible-page observation");
                    double initialPageLongestPostFirstFrameIntervalMs = postFirstFrameIntervals.DefaultIfEmpty().Max();
                    int initialPagePostFirstFrameIntervalCount = postFirstFrameIntervals.Count;
                    var pageVisits = new List<object>();
                    var guiResponses = new List<object>();
                    double responseDrawMs = 0, interactionToRenderedMs = 0;
                    async Task<double> Gesture(string name, Control control, Func<bool> responded)
                    {
                        if (!control.IsVisibleInTree() || control is BaseButton { Disabled: true })
                            throw new InvalidOperationException(name + " GUI control unavailable");
                        long start = Stopwatch.GetTimestamp(), gestureLastFrame = start;
                        var intervals = new List<double>();
                        void OnGestureFrame()
                        {
                            long now = Stopwatch.GetTimestamp();
                            intervals.Add(Stopwatch.GetElapsedTime(gestureLastFrame, now).TotalMilliseconds);
                            gestureLastFrame = now;
                        }
                        root.GetTree().ProcessFrame += OnGestureFrame;
                        try
                        {
                            PanelOpenClick(viewport, control);
                            double dispatchMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                            do
                            {
                                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                                if (Stopwatch.GetElapsedTime(start).TotalSeconds > 30) throw new TimeoutException(name + " GUI gesture did not respond");
                            } while (!responded());
                            double renderedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                            guiResponses.Add(new { name, dispatchMs, renderedMs,
                                longestProcessFrameIntervalMs = intervals.DefaultIfEmpty().Max(), frameIntervalsMs = intervals });
                            return renderedMs;
                        }
                        finally { root.GetTree().ProcessFrame -= OnGestureFrame; }
                    }
                    async Task<Control> Visit(string name, string buttonField, string pageField, bool settle)
                    {
                        lightProbe?.SetContext(sample, name);
                        var visit = await MeasureLightPageVisitAsync(root, viewport, current, name, buttonField,
                            pageField, settle, clicked, pageInstances);
                        if (name == "Settings" && pageVisits.Count == 0)
                        {
                            responseDrawMs = visit.EntryToReadyMs;
                            interactionToRenderedMs = visit.UsableMs;
                        }
                        pageVisits.Add(visit.Observation);
                        return visit.Target;
                    }
                    long interaction = Stopwatch.GetTimestamp();
                    var settingsPage = await Visit("Settings", "_settings", "_settingsPage", settle: true);
                    var back = (Button)PanelOpenField(current, "_settingsBack")!;
                    await Gesture("Settings Back", back, () => !settingsPage.IsVisibleInTree());
                    bool libraryNavigationAvailable = ((Button)PanelOpenField(current, "_seeds")!).IsVisibleInTree();
                    if (lightPagePerformance)
                    {
                        await Visit("Feedback", "_feedback", "_feedbackPage", settle: true);
                        await Visit("About", "_notes", "_notesHost", settle: true);
                        await Visit("Settings", "_settings", "_settingsPage", settle: true);
                        await Visit("Feedback", "_feedback", "_feedbackPage", settle: true);
                        await Visit("About", "_notes", "_notesHost", settle: true);
                        if (sample == 0 && panelArgs.Contains("--light-page-invalidation"))
                            await MeasureLightPageInvalidationAsync(current, workspace, Visit, pageVisits);
                        var initialNav = (Button)PanelOpenField(current, scene == "main-menu" ? "_search" : scene == "crystal" ? "_currentPrediction" : "_analysis")!;
                        await Gesture("Return to initial page", initialNav, () => PanelOpenCurrentPageReady(current, scene));
                    }
                    else if (navigationPerformance)
                    {
                        await Visit("Feedback", "_feedback", "_feedbackPage", settle: true);
                        var details = (Control)PanelOpenField(current, "_feedbackDetails")!;
                        var toggle = (Button)PanelOpenField(current, "_feedbackDetailsToggle")!;
                        bool oldDetails = details.Visible;
                        await Gesture("Feedback details", toggle, () => details.Visible != oldDetails);
                        await Gesture("Feedback details restore", toggle, () => details.Visible == oldDetails);
                        if (libraryNavigationAvailable)
                        {
                            var library = await Visit("SeedLibrary", "_seeds", "_seedLibrary", settle: true);
                            var filter = (LineEdit)PanelOpenField(library, "_filter")!;
                            await Gesture("SeedLibrary filter focus", filter, filter.HasFocus);
                        }
                        // One rendered target frame per rapid visit. No deferred
                        // settle delay is added between the successive gestures.
                        await Visit("Settings", "_settings", "_settingsPage", settle: false);
                        await Visit("Feedback", "_feedback", "_feedbackPage", settle: false);
                        if (libraryNavigationAvailable) await Visit("SeedLibrary", "_seeds", "_seedLibrary", settle: false);
                        var initialNav = (Button)PanelOpenField(current, scene == "main-menu" ? "_search" : scene == "crystal" ? "_currentPrediction" : "_analysis")!;
                        await Gesture("Return to initial page", initialNav, () => PanelOpenCurrentPageReady(current, scene));
                    }
                    root.GetTree().ProcessFrame -= OnFrame;
                    RenderingServer.FramePreDraw -= OnPreDraw;
                    frameObserverConnected = false;
                    viewport.GetTexture().GetImage().SavePng(Path.Combine(output, $"{scene}-{sample}-after-navigation.png"));
                    ulong surfaceInstanceId = current.GetInstanceId();
                    // Use the real header close gesture and its native lifecycle.
                    await Gesture("Header close", current.CloseButton,
                        () => !GodotObject.IsInstanceValid(current) || !current.IsVisibleInTree());
                    var observation = new { scene, sample, firstOpen = sample == 0, synchronousDispatchMs,
                        firstPreDrawMs, dispatchToFirstPreDrawMs = firstPreDrawMs - synchronousDispatchMs,
                        firstPreDrawToPostDrawMs = firstRenderedFrameMs - firstPreDrawMs,
                        firstRenderedFrameMs, currentPageUsableMs, currentPageReadyRenderedFrames, currentPageLongestFrameIntervalMs,
                        initialPageDwellMs, initialPageRenderedFrames, requiredDwellMs,
                        initialPageTileCountAtFirstFrame, initialPageTileCount, initialPageTileArrivals,
                        initialPageLongestPostFirstFrameIntervalMs, initialPagePostFirstFrameIntervalCount,
                        clickToOrdinaryInteractionRenderedMs = responseDrawMs, interactionToRenderedMs,
                        pageVisits, guiResponses, libraryNavigationAvailable,
                        interactionDispatchAfterClickMs = Stopwatch.GetElapsedTime(clicked, interaction).TotalMilliseconds,
                        longestProcessFrameIntervalMs = frameIntervals.DefaultIfEmpty().Max(), frameIntervalsMs = frameIntervals,
                        longestPostFirstFrameIntervalMs = postFirstFrameIntervals.DefaultIfEmpty().Max(),
                        postFirstFrameIntervalsMs = postFirstFrameIntervals,
                        ordinaryInteraction = "native GUI pointer Settings -> visible settings page -> FramePostDraw",
                        surfaceInstanceId, menuPreparationBeforeClick = sample == 0 ? menuBeforeClick : null,
                        menuPreparationAfterEntry, menuPreparationProcessCpu };
                    samples.Add(observation);
                    GD.Print("PANEL_OPEN_SAMPLE " + JsonSerializer.Serialize(observation));
                    for (int frame = 0; frame < 6; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (Signature() != before) throw new InvalidOperationException("Panel entry mutated fixture RunState/RNG");
                }
                finally
                {
                    if (frameObserverConnected)
                    {
                        root.GetTree().ProcessFrame -= OnFrame;
                        RenderingServer.FramePreDraw -= OnPreDraw;
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "panel-open-performance.json"), JsonSerializer.Serialize(new {
                passed = true, scene, navigationPerformance, lightPagePerformance,
                lightPageInvalidation = panelArgs.Contains("--light-page-invalidation"),
                lightPageProbe = lightProbe?.Snapshot(), probeEnabled = lightProbe is not null,
                rendered = true, displayServer = DisplayServer.GetName(),
                renderingDriver = RenderingServer.GetCurrentRenderingDriverName(),
                managedRuntime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                fixtureUserDataDirectory = OS.GetUserDataDir(), gameLanguage = save.SettingsSave.Language,
                ownerGameProcessActive = Process.GetProcessesByName("SlayTheSpire2").Length > 0,
                definition = "One fresh rendered host process per scene; scene/ModelDb/resources initialized before clicks; shell first open + three same-scene explicit-close/reopens. Not game process startup cold.",
                fixture = "Mock saves; controlled native singleton graph; real RT2 launcher GUI dispatch + native stack Push/Pop/Remove. Unrelated game startup/native scene startup omitted.",
                timing = "Rendering-server PreDraw/PostDraw are not GPU-exclusive timing. Shell first draw and complete current-page readiness are distinct. Ordinary runs hold the initial page >=1800ms/>=12 draws; navigation mode visits immediately after readiness except Crystal retains its result dwell. Post-first intervals begin wholly after first PostDraw. Page/gesture intervals start at that actual pointer gesture, include synchronous dispatch, and optionally 12 settled process frames. Tile counts establish realized controls, not solver completion. Click-to-Settings includes intentional dwell; interactionToRenderedMs is its response. Library navigation remains hidden in run. Screenshots are captured after navigation, outside measured intervals.",
                readiness = "Visible/ready current controls, initial in-run document or Crystal board with result tiles, and enabled header close. Progressive products additionally expose IsCurrentPageReady; frozen74156b3c pages build synchronously. First visits mean first user visit in this process, which may follow idle preparation.",
                unchangedRunState = Signature() == before, menuPreparationMode = preparationMode, menuPreparationBeforeClick = menuBeforeClick,
                menuPreparationAfterEntry = menuFirstAfterEntry, samples
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "environment-fixture-effective.json"), JsonSerializer.Serialize(workspace.Environment,
                new JsonSerializerOptions { WriteIndented = true }));
            if (treeDiagnostic && _panelTreeObservation is { } observed)
                File.WriteAllText(Path.Combine(output, "panel-tree-diagnostic.json"), JsonSerializer.Serialize(new {
                    note = "Diagnostic instrumentation only; event probes and tree census add overhead. No nodes removed, no GetPicker/GetPopup called; actual native launcher/stack path.",
                    observed.CollectorOverheadMs, observed.BeforeAttach, observed.AfterAttach,
                    stages = observed.Stages.Select(stage => new { stage.Name,
                        millisecondsSinceInitializeReturned = Stopwatch.GetElapsedTime(observed.InitializedAt, stage.Timestamp).TotalMilliseconds })
                }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"PANEL_OPEN_PERFORMANCE_PASS scene={scene};samples={samples.Count};rendered=true;runUnchanged=true");
        }
        finally
        {
            menuMeasurement?.Dispose();
            diagnosticPatch?.UnpatchAll(diagnosticPatch.Id);
            _panelTreeObservation = null;
            TestMode.IsOn = true;
            if (GodotObject.IsInstanceValid(game)) { viewport.RemoveChild(game); game.Free(); }
            foreach (Node node in detached.AsEnumerable().Reverse()) if (GodotObject.IsInstanceValid(node)) node.Free();
            viewport.QueueFree(); display.QueueFree();
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, null);
            liveEvent?.EnsureCleanup(); if (run is not null) RunManager.Instance.CleanUp();
            LocalContext.NetId = oldLocal;
            typeof(RunManager).GetProperty(nameof(RunManager.EventSynchronizer))!.SetValue(RunManager.Instance, oldSync);
            typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!.SetValue(null, oldRuntime);
            workspace.Preferences.ShowInRunPredictionEntry = oldEntryPreference;
            if (addedBackAction) InputMap.EraseAction(backAction);
            TestMode.IsOn = false;
        }
    }

    // Host-only telemetry. Status is deliberately reflected so this exact host
    // binary can load the frozen product, which has no preparation contract.
    private sealed class PanelMenuPreparationMeasurement : IDisposable
    {
        private readonly Node _root;
        private readonly SubViewport _viewport;
        private readonly Button _response;
        private readonly string _mode, _output;
        private readonly double _baselineWaitMs;
        private readonly long _started;
        private readonly TimeSpan _processCpuAtMenuStart;
        private TimeSpan? _processCpuAtClick;
        private long _lastFrame, _probeStarted;
        private int _frames, _probeFrame, _responseFrame = -1;
        private double _attachMilliseconds, _responseMilliseconds;
        private double? _probeElapsedMilliseconds;
        private JsonElement? _probeStatus;
        private object? _entry;
        private PropertyInfo? _statusProperty;
        private readonly List<double> _intervals = [];
        private readonly List<double> _intervalEndMilliseconds = [];
        private readonly List<object> _statuses = [];
        private int? _firstNativeIntervalIndex, _firstNativeObservedFrame;
        private double? _firstNativeObservedMilliseconds;
        private long _lastSlices = -1;
        private bool? _lastReady;
        private bool _sealed, _disposed;
        private string? _probeFailure;
        private object? _receipt;

        internal PanelMenuPreparationMeasurement(Node root, SubViewport viewport, Control game,
            string mode, double baselineWaitMs, string output)
        {
            _root = root; _viewport = viewport; _mode = mode; _baselineWaitMs = baselineWaitMs; _output = output;
            _response = new Button { Name = "PanelMenuResponseProbe", Position = new(1540, 840), Size = new(32, 32),
                FocusMode = Control.FocusModeEnum.None };
            _response.Pressed += () =>
            {
                _responseFrame = _frames;
                _responseMilliseconds = Stopwatch.GetElapsedTime(_probeStarted).TotalMilliseconds;
            };
            game.AddChild(_response);
            _started = _lastFrame = Stopwatch.GetTimestamp();
            _processCpuAtMenuStart = Process.GetCurrentProcess().TotalProcessorTime;
            root.GetTree().ProcessFrame += OnFrame;
        }

        internal void AfterAttach(object entry)
        {
            _attachMilliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            _entry = entry;
            _statusProperty = entry.GetType().GetProperty("MenuPreparationStatus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ReadStatus(record: true);
        }

        private (bool? Ready, long? Slices, JsonElement? Snapshot) ReadStatus(bool record)
        {
            object? status = _statusProperty?.GetValue(_entry);
            if (status is null) return (null, null, null);
            var type = status.GetType();
            object? readyValue = type.GetProperty("IsReady")?.GetValue(status);
            object? sliceValue = type.GetProperty("NativeSlices")?.GetValue(status);
            if (readyValue is not bool ready || sliceValue is null)
                throw new InvalidOperationException("MenuPreparationStatus omitted IsReady/NativeSlices");
            long slices = Convert.ToInt64(sliceValue, System.Globalization.CultureInfo.InvariantCulture);
            JsonElement snapshot = JsonSerializer.SerializeToElement(status, type);
            if (!_sealed && slices > 0 && _firstNativeIntervalIndex is null)
            {
                // The arriving frame interval contains the work whose progress
                // is now observable. Earlier attachment/idle intervals stay out.
                _firstNativeIntervalIndex = Math.Max(0, _intervals.Count - 1);
                _firstNativeObservedFrame = _frames;
                _firstNativeObservedMilliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            }
            if (record && (slices != _lastSlices || ready != _lastReady))
            {
                _statuses.Add(new { elapsedMilliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                    processFrame = _frames, status = snapshot });
                _lastSlices = slices; _lastReady = ready;
            }
            return (ready, slices, snapshot);
        }

        internal JsonElement? SnapshotCurrentStatus() => ReadStatus(record: false).Snapshot;

        internal object CaptureFirstEntryProcessCpu()
        {
            TimeSpan cpuAtReady = Process.GetCurrentProcess().TotalProcessorTime;
            return new { processCpuAtMenuStartMilliseconds = _processCpuAtMenuStart.TotalMilliseconds,
                processCpuAtClickMilliseconds = _processCpuAtClick?.TotalMilliseconds,
                processCpuAtCurrentPageReadyMilliseconds = cpuAtReady.TotalMilliseconds,
                menuStartToClickProcessCpuMilliseconds = _processCpuAtClick is TimeSpan clickCpu
                    ? (clickCpu - _processCpuAtMenuStart).TotalMilliseconds : (double?)null,
                menuStartToCurrentPageReadyProcessCpuMilliseconds = (cpuAtReady - _processCpuAtMenuStart).TotalMilliseconds,
                clickToCurrentPageReadyProcessCpuMilliseconds = _processCpuAtClick is TimeSpan entryCpu
                    ? (cpuAtReady - entryCpu).TotalMilliseconds : (double?)null,
                definition = "TotalProcessorTime for this isolated host process, sampled at menu observation start, immediately before entry and at complete current-page readiness. Includes all host threads (main, worker, render and runtime); not exclusive native-observation CPU, GPU time or system-wide CPU. Separate CPU-host artifact from the frozen latency matrix." };
        }

        private void OnFrame()
        {
            long now = Stopwatch.GetTimestamp();
            _intervals.Add(Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
            _intervalEndMilliseconds.Add(Stopwatch.GetElapsedTime(_started, now).TotalMilliseconds);
            _lastFrame = now; _frames++;
            // Frozen baselines have no preparation status. Their single probe
            // establishes GUI response only, without claiming active preparation.
            if (_frames == 2 && _probeStarted == 0 && _statusProperty is null && _mode != "immediate") ProbeGui();
            if (_probeStarted != 0 && _responseFrame < 0 && _frames - _probeFrame > 2)
                _probeFailure = "Menu GUI response exceeded two process frames";
        }

        private void ProbeGui()
        {
            _probeStatus = ReadStatus(record: true).Snapshot;
            _probeElapsedMilliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            _probeFrame = _frames;
            _probeStarted = Stopwatch.GetTimestamp();
            PanelOpenClick(_viewport, _response);
        }

        internal async Task WaitBeforeClickAsync()
        {
            try
            {
                while (true)
                {
                    if (_probeFailure is not null) throw new InvalidOperationException(_probeFailure);
                    var status = ReadStatus(record: true);
                    // Exercise the real pointer path between completed native
                    // slices, after work has actually begun. Ready mode then
                    // includes the product's normal input-induced idle pause.
                    if (_mode != "immediate" && _statusProperty is not null && _probeStarted == 0 &&
                        status.Slices >= 3 && status.Ready == false) ProbeGui();
                    double elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
                    if (elapsed >= 15000) throw new TimeoutException("Menu preparation did not reach requested " + _mode + " state in 15 seconds");
                    if (_mode == "immediate") break;
                    if (_statusProperty is null)
                    {
                        // The runner can repeat the measured product wait on the
                        // frozen baseline. This is never reported as ready.
                        if (elapsed >= _baselineWaitMs) break;
                    }
                    else if (_mode == "ready" && status.Ready == true) break;
                    else if (_mode == "partial")
                    {
                        if (status.Ready == true) throw new InvalidOperationException("Requested partial preparation phase was missed: already ready");
                        if (status.Slices >= 3 && status.Ready == false) break;
                    }
                    await _root.ToSignal(_root.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            catch (Exception ex)
            {
                _receipt = Receipt(false, ex.GetType().Name + ":" + ex.Message);
                WriteReceipt();
                throw;
            }
        }

        internal object SealBeforeClick()
        {
            if (_probeStarted == 0 && _statusProperty is not null && _mode != "immediate")
                throw new InvalidOperationException("No active preparation phase with at least three native slices was observed for the GUI response probe");
            if (_probeStarted == 0) ProbeGui();
            if (_probeFailure is not null || _responseFrame < 0 || _responseFrame - _probeFrame > 2)
                throw new InvalidOperationException(_probeFailure ?? "Menu GUI pointer did not produce its actual Pressed response before entry");
            _processCpuAtClick = Process.GetCurrentProcess().TotalProcessorTime;
            _receipt = Receipt(true, null);
            _sealed = true;
            _root.GetTree().ProcessFrame -= OnFrame;
            _response.Visible = false;
            return _receipt;
        }

        private object Receipt(bool clicked, string? failure)
        {
            var status = ReadStatus(record: true);
            double durationMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            double finalGapMs = Stopwatch.GetElapsedTime(_lastFrame).TotalMilliseconds;
            object IntervalStats(IEnumerable<int> indices)
            {
                double[] intervals = indices.Select(index => _intervals[index]).ToArray();
                return new { intervalCount = intervals.Length, longestProcessFrameIntervalMilliseconds = intervals.DefaultIfEmpty().Max(),
                    processFrameIntervalsOver33Milliseconds = intervals.Count(value => value > 33),
                    processFrameIntervalsOver50Milliseconds = intervals.Count(value => value > 50),
                    processFrameIntervalsMilliseconds = intervals };
            }
            int[] preparationIndices = _firstNativeIntervalIndex is int firstNative
                ? Enumerable.Range(firstNative, Math.Max(0, _intervals.Count - firstNative)).ToArray() : [];
            object? preparationWindow = _firstNativeIntervalIndex is int start ? new
            {
                firstNativeSliceObservedAtProcessFrame = _firstNativeObservedFrame,
                firstNativeSliceObservedAtMilliseconds = _firstNativeObservedMilliseconds,
                firstIncludedIntervalIndex = start,
                all = IntervalStats(preparationIndices),
                beforeGui = _probeElapsedMilliseconds is double probeAt
                    ? IntervalStats(preparationIndices.Where(index => _intervalEndMilliseconds[index] <= probeAt)) : null,
                afterGui = _probeElapsedMilliseconds is double afterProbeAt
                    ? IntervalStats(preparationIndices.Where(index => _intervalEndMilliseconds[index] > afterProbeAt)) : null,
                definition = "From the interval ending when NativeSlices first becomes >0 through the pre-click seal. Excludes earlier attachment/idle intervals. First interval includes the 0-to-positive transition; afterGui includes the interval crossing the actual pointer gesture. Includes the normal idle pause after that gesture; this is frame responsiveness, not isolated native CPU time."
            } : null;
            return new { requestedMode = _mode, reachedRequestedClickPhase = clicked, failure,
                preparationStatusAvailable = _statusProperty is not null,
                baselineFixedWaitMilliseconds = _statusProperty is null ? _baselineWaitMs : (double?)null,
                baselineWaitRequiresMatchingProductObservation = _statusProperty is null && _mode != "immediate",
                observedReady = status.Ready, nativeSlicesBeforeClick = status.Slices,
                preparationStatusBeforeClick = status.Snapshot, preparationStatusTrace = _statuses.ToArray(),
                attachMilliseconds = _attachMilliseconds, durationMilliseconds = durationMs,
                processCpuAtMenuStartMilliseconds = _processCpuAtMenuStart.TotalMilliseconds,
                processCpuAtClickMilliseconds = _processCpuAtClick?.TotalMilliseconds,
                menuStartToClickProcessCpuMilliseconds = _processCpuAtClick is TimeSpan clickCpu
                    ? (clickCpu - _processCpuAtMenuStart).TotalMilliseconds : (double?)null,
                processFrameCount = _frames, frameIntervalCount = _intervals.Count,
                longestProcessFrameIntervalMilliseconds = _intervals.DefaultIfEmpty().Max(),
                longestObservedWindowGapMilliseconds = Math.Max(finalGapMs, _intervals.DefaultIfEmpty().Max()),
                processFrameIntervalsOver33Milliseconds = _intervals.Count(value => value > 33),
                processFrameIntervalsOver50Milliseconds = _intervals.Count(value => value > 50),
                processFrameIntervalsMilliseconds = _intervals.ToArray(),
                beforeNativePreparationWindow = _firstNativeIntervalIndex is int preparationStart
                    ? IntervalStats(Enumerable.Range(0, Math.Min(preparationStart, _intervals.Count))) : null,
                nativePreparationWindow = preparationWindow,
                finalProcessFrameToClickMilliseconds = finalGapMs,
                guiResponse = new { pointerGestureDispatched = _probeStarted != 0, actualPressedObserved = _responseFrame >= 0,
                    elapsedMillisecondsAtDispatch = _probeElapsedMilliseconds, preparationStatusAtDispatch = _probeStatus,
                    activePreparationProvenAtDispatch = _probeStatus is JsonElement probeStatus &&
                        probeStatus.TryGetProperty("IsReady", out var probeReady) && probeReady.ValueKind == JsonValueKind.False &&
                        probeStatus.TryGetProperty("NativeSlices", out var probeSlices) && probeSlices.GetInt64() >= 3,
                    dispatchedAtProcessFrame = _probeFrame, responseAtProcessFrame = _responseFrame,
                    responseProcessFrames = _responseFrame >= 0 ? _responseFrame - _probeFrame : (int?)null,
                    responseMilliseconds = _responseFrame >= 0 ? _responseMilliseconds : (double?)null },
                definition = "Menu window starts immediately before EnsureAttached, ends immediately before actual launcher click. First interval includes attachment. Partial/ready products receive one tiny native GUI pointer gesture after >=3 native slices while not ready; ready timing includes the ensuing normal idle pause. Immediate probes at entry. Frozen baselines probe at frame two without claiming active preparation or readiness. Periodic input-idle behavior is checked separately." };
        }

        private void WriteReceipt() => File.WriteAllText(Path.Combine(_output, "menu-preparation-performance.json"),
            JsonSerializer.Serialize(_receipt, new JsonSerializerOptions { WriteIndented = true }));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _root.GetTree().ProcessFrame -= OnFrame;
            if (!_sealed && _receipt is null) { _receipt = Receipt(false, "RetiredBeforeFirstClick"); WriteReceipt(); }
            else if (_sealed) WriteReceipt();
            if (GodotObject.IsInstanceValid(_response)) _response.QueueFree();
        }
    }

    private sealed class PanelTreeObservation
    {
        internal long InitializedAt;
        internal double CollectorOverheadMs;
        internal object? BeforeAttach, AfterAttach;
        internal readonly List<(string Name, long Timestamp)> Stages = [];
    }
    private static PanelTreeObservation? _panelTreeObservation;
    private static void PanelTreeInitialized(RunPredictionOverlay __instance)
    {
        if (_panelTreeObservation is not { } observed) return;
        observed.InitializedAt = Stopwatch.GetTimestamp();
        observed.BeforeAttach = PanelTreeInventory(__instance);
        var shell = (WorkspaceShell)PanelOpenField(__instance, "_shell")!;
        void Watch(Node node, string name)
        {
            node.TreeEntered += () => observed.Stages.Add(new(name + ".TreeEntered", Stopwatch.GetTimestamp()));
            node.Ready += () => observed.Stages.Add(new(name + ".Ready", Stopwatch.GetTimestamp()));
        }
        Watch(__instance, "overlay"); Watch(shell, "shell");
        foreach (string field in new[] { "_settingsPage", "_feedbackPage", "_seedLibrary", "_crystalScene", "_content" })
            if (PanelOpenField(shell, field) is Node node) Watch(node, field);
        observed.CollectorOverheadMs = Stopwatch.GetElapsedTime(observed.InitializedAt).TotalMilliseconds;
    }
    private static void PanelTreeAttached() => _panelTreeObservation?.Stages.Add(new("AfterOverlayOpened.prefix", Stopwatch.GetTimestamp()));
    private static object PanelTreeInventory(Node root)
    {
        IEnumerable<Node> All(Node node)
        {
            yield return node;
            foreach (Node child in node.GetChildren(includeInternal: true))
                foreach (Node descendant in All(child)) yield return descendant;
        }
        Node[] nodes = All(root).ToArray();
        return new { nodes = nodes.Length, types = nodes.GroupBy(node => node.GetClass().ToString())
                .OrderByDescending(group => group.Count()).Select(group => new { type = group.Key, count = group.Count() }).ToArray(),
            colorPickerButtons = nodes.OfType<ColorPickerButton>().Select(picker => new {
                name = picker.Name.ToString(), internalChildren = picker.GetChildCount(includeInternal: true),
                subtreeNodes = All(picker).Count() }).ToArray() };
    }

    private static object? PanelOpenField(object owner, string name)
    {
        for (Type? type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly) is { } field)
                return field.GetValue(owner);
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    private static bool PanelOpenActivePageReady(WorkspaceShell shell)
    {
        var failed = shell.GetType().GetProperty("HasCurrentPageFailure", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (failed?.GetValue(shell) is true)
            throw new InvalidOperationException("Requested page reached a presentation failure; it is not a complete usable-content timing sample");
        // Frozen74156b3c builds each page synchronously. Progressive versions
        // expose this read-only readiness fact; a visible shell is insufficient.
        var ready = shell.GetType().GetProperty("IsCurrentPageReady", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return ready is null || ready.GetValue(shell) is true;
    }
    private static bool PanelOpenCurrentPageReady(WorkspaceShell shell, string scene)
    {
        if (!shell.IsVisibleInTree() || !PanelOpenActivePageReady(shell) || shell.CloseButton.Disabled) return false;
        if (scene == "main-menu") return PanelOpenField(shell, "_references") is Control references && references.IsVisibleInTree() && references.IsNodeReady();
        if (scene == "in-run") return PanelOpenField(shell, "_predictor") is RolltheSpire2.Ui.Pages.Analysis.AnalysisPage { LastDocument: not null } predictor && predictor.IsVisibleInTree();
        return PanelOpenField(shell, "_crystalScene") is Control crystal && crystal.IsVisibleInTree() &&
            PanelOpenField(crystal, "_snapshot") is not null &&
            PanelOpenField(crystal, "_optionTiles") is System.Collections.IDictionary { Count: > 0 };
    }
    private static void PanelOpenSet(object owner, string name, object? value)
    {
        for (Type? type = owner.GetType(); type != null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            if (type.GetProperty(name, flags) is { } property) { property.SetValue(owner, value); return; }
            if (type.GetField(name, flags) is { } field) { field.SetValue(owner, value); return; }
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    private static void PanelOpenClick(SubViewport viewport, Control control)
    {
        Vector2 point = control.GetGlobalRect().GetCenter();
        viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool pressed in new[] { true, false })
            viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
    }
}

// Only fixture startup differs. Product methods, GUI input and stack lifecycle
// execute normally; these controls do not pretend to be a complete game scene.
internal partial class PanelOpenGameFixture : NGame
{
    public override void _EnterTree() { }
    public override void _Ready() { }
    public override void _ExitTree() { }
    public override void _Notification(int what) { }
    public override void _Input(InputEvent input) { }
}
internal partial class PanelOpenMenuFixture : NMainMenu { public override void _Ready() { } }
internal partial class PanelOpenMenuStackFixture : NMainMenuSubmenuStack { public override void _Ready() { } }
internal partial class PanelOpenGlobalFixture : NGlobalUi { public override void _Ready() { } }
internal partial class PanelOpenTopBarFixture : NTopBar
{
    public override void _EnterTree() { }
    public override void _Ready() { }
    public override void _ExitTree() { }
    public override void _Input(InputEvent input) { }
}
