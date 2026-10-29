using System.Reflection;
using System.Text.Json;
using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckRunEntry(Node host, GameVersionDetection detection, string output)
    {
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
        if(System.Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING") is { Length:>0 })
        {
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
            if(PredictorCrystalAssistantChecks.Run("000000000000",10)!=0)
                throw new InvalidOperationException("Focused native Wing checks incomplete or failed; inspect their receipts");
            return;
        }
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("Run entry: " + message); }
        CheckAuthorityCandidateReuse(Check);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var crystalPreferences = RolltheSpire2.Ui.Workspace.SearchWorkspace.Open(OS.GetUserDataDir());
        var originalCrystalOptions = (crystalPreferences.Preferences.CrystalSphereAvoidCurse,
            crystalPreferences.Preferences.CrystalSphereIncludeDriftwood,
            crystalPreferences.Preferences.CrystalSphereIncludeEnchantments,
            crystalPreferences.Preferences.CrystalSphereTransparentMask);
        bool originalMaskAppearance = CrystalSphereMaskAppearance.Enabled;
        crystalPreferences.SetCrystalSphereOptions(true, true, false, false);
        CrystalSphereMaskAppearance.SetEnabled(false);
        try
        {
        // Exercise the actual vanilla mask/material and production postfix body.
        // The editor host rolls forward to .NET 10, unsupported by the game's
        // bundled Harmony; patch attachment remains normal-game validation.
        {
            var applyMask=typeof(CrystalSphereMaskAppearance).GetMethod("ReadyPostfix",BindingFlags.Static|BindingFlags.NonPublic)!;
            var nativeScene = GD.Load<PackedScene>("res://scenes/events/custom/crystal_sphere/crystal_sphere_screen.tscn").Instantiate();
            var mask = nativeScene.GetNode<MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere.NCrystalSphereMask>("Bg/Sphere/ScryMask");
            mask.GetParent().RemoveChild(mask); nativeScene.Free();
            var material=mask.Material; var mouseFilter=mask.MouseFilter;
            mask.SelfModulate=new Color(.8f,.9f,1,1);
            host.AddChild(mask);
            applyMask.Invoke(null,[mask]);
            Check(!CrystalSphereMaskAppearance.Enabled && mask.SelfModulate==new Color(.8f,.9f,1,1),
                "mask is disabled by default and Ready preserves the complete modulation");
            crystalPreferences.SetCrystalSphereOptions(transparentMask:true);
            typeof(CrystalSphereMaskAppearance).GetField("_enabled",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,null);
            applyMask.Invoke(null,[mask]);
            Check(CrystalSphereMaskAppearance.Enabled && Math.Abs(mask.SelfModulate.A-.35f)<.001,
                "Native mask Ready restores the persisted appearance before any assistant panel opens");
            crystalPreferences.SetCrystalSphereOptions(transparentMask:false);
            CrystalSphereMaskAppearance.SetEnabled(false);
            CrystalSphereMaskAppearance.SetEnabled(true);
            Check(Math.Abs(mask.SelfModulate.A-.35f)<.001 && Math.Abs(mask.SelfModulate.R-.8f)<.001,
                "native mask becomes translucent without replacing RGB tint");
            CrystalSphereMaskAppearance.SetEnabled(false);
            Check(mask.SelfModulate==new Color(.8f,.9f,1,1),"disabling restores pre-enable alpha");
            CrystalSphereMaskAppearance.SetEnabled(true);
            var cell=new MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereCell(0,0);
            mask.UpdateMat(cell);
            Check(cell.IsHidden && ReferenceEquals(mask.Material,material) && mask.MouseFilter==mouseFilter,
                "mask opacity preserves hidden cell, shader material and input policy");
            mask.SelfModulate=new Color(.6f,.7f,.8f,.25f);
            CrystalSphereMaskAppearance.SetEnabled(false);
            Check(mask.SelfModulate==new Color(.6f,.7f,.8f,.25f),"disable preserves a later third-party modulation change");
            CrystalSphereMaskAppearance.SetEnabled(true); applyMask.Invoke(null,[mask]);
            CrystalSphereMaskAppearance.SetEnabled(false);
            Check(Math.Abs(mask.SelfModulate.A-.25f)<.001,"already-lower third-party opacity is never replaced");
            mask.Free();
            CrystalSphereMaskAppearance.SetEnabled(true);CrystalSphereMaskAppearance.SetEnabled(false);
            Console.WriteLine("CRYSTAL_MASK_PASS nativeMaterial=true;postfixBody=true;patchAttachment=notTested;defaultOff=true;restore=true;externalChangesPreserved=true;freedNodeSafe=true;hiddenStateUnchanged=true");
        }
        var service = DispatchProxy.Create<INetHostGameService, EntryNetProxy>();
        var proxy = (EntryNetProxy)(object)service;
        using var standard = new NCharacterSelectScreen();
        using var custom = new NCustomRunScreen();
        var standardLobby = new StartRunLobby(GameMode.Standard, service, standard, 4);
        var customLobby = new StartRunLobby(GameMode.Custom, service, custom, 4);
        typeof(NCharacterSelectScreen).GetField("_lobby",flags)!.SetValue(standard,standardLobby);
        typeof(NCustomRunScreen).GetField("_lobby",flags)!.SetValue(custom,customLobby);
        foreach (var type in new[] { NetGameType.Host, NetGameType.Client })
        {
            proxy.Type = type;
            Check(!LobbyUnlockReadout.IsCustomMultiplayerLobby(standard), "standard lobby hidden");
            Check(LobbyUnlockReadout.IsCustomMultiplayerLobby(custom), "custom lobby shown");
            Check(ReferenceEquals(LobbyUnlockReadout.Find(custom),customLobby), "custom lobby context readable");
        }
        proxy.Connected = false;
        Check(!LobbyUnlockReadout.IsCustomMultiplayerLobby(custom), "disconnected lobby hidden");
        proxy.Connected = true; proxy.Type = NetGameType.Singleplayer;
        Check(!LobbyUnlockReadout.IsCustomMultiplayerLobby(custom), "solo custom lobby remains unchanged");

        var profile = Beta111Profile.Instance;
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = profile, Detection = detection,
            Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
        await MeasureMenuShellComponents(host, output, runtime, Check);
        var players = new[] { "DEFECT", "IRONCLAD" }.Select((name,slot) => Player.CreateForNewRun(
            ModelDb.AllCharacters.Single(c => c.Id.Entry == name), slot == 0 ? UnlockState.all : UnlockState.none, (ulong)(slot+10))).ToArray();
        var run = RunState.CreateForTest(players, gameMode:GameMode.Custom, ascensionLevel:10, seed:"R93FXJN77VRC");
        LocalContext.NetId = players[1].NetId;
        var saved = new PredictorContextDocument { Seed = "R93FXT6SM6K5", PartySeed = "R93FXT6SM6K5" };
        string Signature() => JsonSerializer.Serialize(new { run.Rng.StringSeed, rng = run.Rng.ToSerializable(),
            players = run.Players.Select(p => new { cards = p.Deck.Cards.Select(c => c.Id.ToString()).ToArray(),
                rng = p.PlayerRng.ToSerializable(), unlocks = p.UnlockState.ToSerializable() }).ToArray() });
        string before = Signature();
        var context = WorkspaceShell.CaptureRunPredictionContext(run,"0.111.0",profile.ProfileId,LocalContext.NetId,saved);
        Check(context.SelectedSlot == 1 && context.Ascension == 10 && context.Players.Select(p => p.Character.Entry).SequenceEqual(["DEFECT","IRONCLAD"]), "live roster/local slot/ascension");
        Check(context.Players.All(p => p.Selection is null), "different seed cannot carry old selections");
        Check(JsonSerializer.Serialize(context.Players[1].Unlocks) == JsonSerializer.Serialize(players[1].UnlockState.ToSerializable()), "remote partial unlocks retained");
        var chosen = context with { Players = context.Players.Select(p => p with { Selection = new(1,"",true) }).ToArray() };
        saved.Party = chosen; saved.PartySeed = run.Rng.StringSeed;
        Check(WorkspaceShell.CaptureRunPredictionContext(run,"0.111.0",profile.ProfileId,LocalContext.NetId,saved)
            .Players.All(p => p.Selection?.ChoiceSlot == 1), "same-context choices retained");

        // Native EventRoom is visible in RunState before async asset preload
        // finishes and EventSynchronizer.BeginEvent fills its indexed list.
        var eventSyncProperty = typeof(RunManager).GetProperty(nameof(RunManager.EventSynchronizer))!;
        var previousEventSync = eventSyncProperty.GetValue(RunManager.Instance);
        var readinessNet = new NetSingleplayerGameService();
        using (var readinessSync = new EventSynchronizer(new RunLocationTargetedMessageBuffer(readinessNet), readinessNet,
            run, run, players[1].NetId, run.Rng.Seed))
        {
            run.PushRoom(new MegaCrit.Sts2.Core.Rooms.EventRoom(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>()));
            try
            {
                eventSyncProperty.SetValue(RunManager.Instance, readinessSync);
                Check(!InRunPredictionLauncher.TryGetCurrentEvent(run,out _), "event preload with an empty synchronizer is not ready and does not index it");
                var firstEvent = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>().ToMutable();
                var localEvent = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>().ToMutable();
                await firstEvent.BeginEvent(players[0], null, false);
                await localEvent.BeginEvent(players[1], null, false);
                var nativeEvents = (List<EventModel>)typeof(EventSynchronizer).GetField("_events",flags)!.GetValue(readinessSync)!;
                nativeEvents.Add(firstEvent);
                Check(!InRunPredictionLauncher.TryGetCurrentEvent(run,out _), "partial multiplayer event initialization waits for the local slot");
                nativeEvents.Add(firstEvent);
                Check(!InRunPredictionLauncher.TryGetCurrentEvent(run,out _), "an old or different owner's local slot cannot bind a retained scene");
                nativeEvents[1] = localEvent;
                Check(InRunPredictionLauncher.TryGetCurrentEvent(run,out var readyEvent) && ReferenceEquals(readyEvent,localEvent),
                    "a populated local slot with the actual player and canonical event is ready");
            }
            finally { run.PopCurrentRoom(); eventSyncProperty.SetValue(RunManager.Instance, previousEventSync); }
        }

        var stack = new EntryOverlayStackFixture { Size = new Vector2(1600,900) }; host.AddChild(stack);
        var below = new EntryUnderlyingOverlay(); stack.Push(below);
        var sharedStore = RolltheSpire2.Ui.Workspace.SearchWorkspace.Open(OS.GetUserDataDir());
        string originalLanguage = sharedStore.Preferences.LanguageOverride;
        sharedStore.Preferences.LanguageOverride = "zh";
        var overlay = new RunPredictionOverlay();
        typeof(RuntimeAuthorityEnvironment).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            RuntimeAuthorityEnvironmentSnapshot.Unavailable(runtime.Detection.DisplayVersion, "MenuNotOpenedFixture"));
        typeof(RuntimeAuthorityEnvironment).GetField("_interpretationKey", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, "");
        try
        {
            overlay.Initialize(runtime,run,stack,null);
            var initializedShell = (WorkspaceShell)typeof(RunPredictionOverlay).GetField("_shell", flags)!.GetValue(overlay)!;
            Check(typeof(RunPredictionOverlay).GetField("_panel",flags)!.GetValue(overlay) is null &&
                (string)typeof(WorkspaceShell).GetField("_languageCode",flags)!.GetValue(initializedShell)! == "zh" &&
                ReferenceEquals(typeof(WorkspaceShell).GetField("_liveRun",flags)!.GetValue(initializedShell), run),
                "ordinary in-run context uses current shared preferences without constructing Crystal controls");
            crystalPreferences.SetCrystalSphereOptions(false, false, true, true);
            CrystalSphereMaskAppearance.SetEnabled(true);
            var freshPanel = new CrystalSphereAssistantPanel();
            try
            {
                freshPanel.Initialize(run, runtime);host.AddChild(freshPanel);
                var options = new[] { "_avoidCurse", "_includeDriftwood", "_includeEnchantments", "_transparentMask" }
                    .Select(name => (CheckButton)typeof(CrystalSphereAssistantPanel).GetField(name, flags)!.GetValue(freshPanel)!).ToArray();
                Check(options.Select(option => option.ButtonPressed).SequenceEqual([false, false, true, true]) &&
                    freshPanel.SuspendSession() is null,
                    "Fresh Crystal controls restore all four saved choices without starting a search");
                Check(options[1].Disabled && !options[1].ButtonPressed,
                    "An unavailable Driftwood control retains the saved preference");
                freshPanel.ApplyLanguage("en");freshPanel.ApplyLanguage("zh");
                Check(options.Select(option => option.ButtonPressed).SequenceEqual([false, false, true, true]) &&
                    freshPanel.SuspendSession() is null &&
                    options.All(option => option.TooltipText.Length == 0 && RolltheSpire2.Ui.Tooltips.SearchTooltip.IsScoped(option)),
                    "Language refresh preserves the options and uses the existing custom tooltip scope for all four");
            }
            finally
            {
                host.RemoveChild(freshPanel);freshPanel.Free();
                crystalPreferences.SetCrystalSphereOptions(true, true, false, false);
                CrystalSphereMaskAppearance.SetEnabled(false);
            }
        }
        finally
        {
            sharedStore.Preferences.LanguageOverride = originalLanguage;
            if (typeof(RunPredictionOverlay).GetField("_shell", flags)!.GetValue(overlay) is WorkspaceShell initializedShell)
                typeof(WorkspaceShell).GetMethod("RefreshLanguage", flags)!.Invoke(initializedShell, null);
        }
        try
        {
            overlay.Open();
            Check(RuntimeAuthorityEnvironment.Current.Authority.Fingerprint.Complete,
                "ordinary run entry captures fresh Current even when RT2 was never opened in the menu");
            Check(RuntimeAuthorityEnvironment.Current.Interpretation.EnvironmentReason != "MenuNotOpenedFixture",
                "fresh run entry replaces the initial unavailable interpretation as well as its authority");
            CrystalSphereAssistantPanel panel = null!;
            void SettleCrystalPresentation()
            {
                // This part of the existing fixture injects a detached board
                // into an ordinary-run panel. Settle structural controls only;
                // real scene validation and frame timing are checked separately.
                for (int i = 0; i < 1000; i++)
                {
                    typeof(CrystalSphereAssistantPanel).GetMethod("ApplyPresentationSlice",flags)!.Invoke(panel,null);
                    if ((int)typeof(CrystalSphereAssistantPanel).GetField("_targetPresentationIndex",flags)!.GetValue(panel)! >=
                        ((System.Collections.Immutable.ImmutableArray<CrystalRewardOption>)typeof(CrystalSphereAssistantPanel)
                            .GetField("_selected",flags)!.GetValue(panel)!).Length &&
                        !((bool)typeof(CrystalSphereAssistantPanel).GetField("_presentationRequested",flags)!.GetValue(panel)!) &&
                        ((Queue<CrystalRewardOption>)typeof(CrystalSphereAssistantPanel).GetField("_optionPresentationQueue",flags)!.GetValue(panel)!).Count == 0)
                        return;
                }
                throw new InvalidOperationException("Crystal structural presentation did not settle");
            }
            void ApplyPanelUpdate(CrystalSphereAssistantPanel.Update update)
            {
                panel.ApplyUpdate(update);
                if(!panel.IsVisibleInTree()) SettleCrystalPresentation();
            }
            var firstEntry = stack.Peek() ?? throw new InvalidOperationException("Run entry did not enter the native overlay stack");
            Check(firstEntry is RunPredictionOverlayStackEntry && overlay.IsOpen && overlay.IsVisibleInTree() && !below.Visible,
                "native stack entry covers the previous screen with the retained surface");
            var shell = (WorkspaceShell)typeof(RunPredictionOverlay).GetField("_shell", flags)!.GetValue(overlay)!;
            Check(new[] { "_settingsPage", "_feedbackPage", "_seedLibrary", "_fontChoice", "_language" }
                .All(name => typeof(WorkspaceShell).GetField(name, flags)!.GetValue(shell) is null),
                "ordinary in-run first entry constructs no unvisited Settings, Feedback or Library controls");
            Check(ReferenceEquals(firstEntry.DefaultFocusedControl, shell.CloseButton), "native entry delegates the retained close focus");
            var covering = new EntryUnderlyingOverlay(); stack.Push(covering);
            Check(!overlay.Visible && overlay.IsOpen && overlay.ProcessMode == Node.ProcessModeEnum.Inherit,
                "native temporary hide retains the open session's processing lifecycle");
            stack.Remove(covering);
            Check(ReferenceEquals(stack.Peek(), firstEntry) && overlay.Visible,
                "closing a covering overlay shows the same native entry and surface");
            var ordinaryPage = (AnalysisPage)typeof(WorkspaceShell).GetField("_predictor",flags)!.GetValue(shell)!;
            var ordinaryDocument = ordinaryPage.LastDocument;
            Check(ordinaryDocument is not null && ordinaryPage.Visible, "ordinary run opens a real Analysis document");
            void CloseOrdinary() => typeof(RunPredictionOverlay).GetMethod("Close",flags)!.Invoke(overlay,null);
            var reuseBefore = CaptureRunEntryReuseProbe(shell, run, runtime);
            CloseOrdinary(); overlay.Open();
            var reuseAfter = CaptureRunEntryReuseProbe(shell, run, runtime);
            LogRunEntryReuseProbe(reuseBefore, reuseAfter, output);
            Check(reuseBefore.EffectiveContext is not null && System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(reuseBefore.EffectiveContext),
                System.Text.Json.Nodes.JsonNode.Parse(reuseAfter.RecomputedKey)!["Context"]),
                "initial default party selections match the context actually used by the first document");
            // An empty-state first open saves its implicit party defaults after
            // stamping the old key. The installed baseline has the same one-time
            // redundant rebind (run-entry-baseline-r4); keep that limitation visible.
            GD.Print($"RUN_ENTRY_INITIAL_DEFAULT_REBIND documentReplaced={!ReferenceEquals(ordinaryDocument, ordinaryPage.LastDocument)};baselineReproduced=true");
            ordinaryDocument = ordinaryPage.LastDocument;
            CloseOrdinary(); overlay.Open();
            Check(ReferenceEquals(ordinaryPage,typeof(WorkspaceShell).GetField("_predictor",flags)!.GetValue(shell)) &&
                ReferenceEquals(ordinaryDocument,ordinaryPage.LastDocument) && ordinaryPage.Visible,
                "unchanged ordinary close and reopen after initial defaults retain the Analysis page and exact result document");
            var roster = (List<Player>)typeof(RunState).GetField("_players",flags)!.GetValue(run)!;
            var ascensionProperty = typeof(RunState).GetProperty(nameof(RunState.AscensionLevel))!;
            int originalAscension = run.AscensionLevel;
            try
            {
                CloseOrdinary(); ascensionProperty.SetValue(run, originalAscension - 1); overlay.Open();
                Check(ordinaryPage.LastDocument is not null && !ReferenceEquals(ordinaryDocument,ordinaryPage.LastDocument) &&
                    ordinaryPage.LastRequest?.Ascension == originalAscension - 1,
                    "a changed actual run context replaces the cached ordinary answer before display");
            }
            finally
            {
                CloseOrdinary(); roster.Clear(); roster.AddRange(players);
                ascensionProperty.SetValue(run,originalAscension); overlay.Open();
            }
            Check(ordinaryPage.LastDocument is not null && ordinaryPage.LastRequest is { } restoredOrdinaryRequest &&
                restoredOrdinaryRequest.Ascension == originalAscension && restoredOrdinaryRequest.OriginalSeed == run.Rng.StringSeed &&
                (bool)typeof(AnalysisPage).GetField("_wbHasResult",flags)!.GetValue(ordinaryPage)!,
                "restored live inputs recover their current ordinary prediction context");
            Check(typeof(RunPredictionOverlay).GetField("_panel",flags)!.GetValue(overlay) is null,
                "ordinary opening and retained reopens never create unrelated Crystal controls");
            if (OS.GetCmdlineUserArgs().Contains("--entry-boundary-only"))
            {
                CloseOrdinary();
                typeof(RuntimeAuthorityEnvironment).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
                    RuntimeAuthorityEnvironmentSnapshot.Unavailable(runtime.Detection.DisplayVersion, "MenuNotOpenedFixture"));
                typeof(RuntimeAuthorityEnvironment).GetField("_interpretationKey", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, "");
                run.PushRoom(new MegaCrit.Sts2.Core.Rooms.EventRoom(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>()));
                var crystalEntry = new RunPredictionOverlay();
                try
                {
                    crystalEntry.Initialize(runtime, run, stack, null);
                    host.AddChild(crystalEntry);
                    Check(RuntimeAuthorityEnvironment.Current.Authority.Fingerprint.Complete &&
                        RuntimeAuthorityEnvironment.Current.Interpretation.EnvironmentReason != "MenuNotOpenedFixture",
                        "Crystal entry also establishes fresh authority and interpretation without a prior RT2 menu open");
                }
                finally { crystalEntry.Retire(); run.PopCurrentRoom(); }
                overlay.Retire();
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(Signature() == before, "both entry boundaries preserve the real fixture RNG, deck and unlocks");
                GD.Print($"RUN_ENTRY_BOUNDARY_PASS checks={checks};initialDefaultRebindDisclosed=true;ordinaryAndCrystalFresh=true;readOnly=true");
                return;
            }
            ((Button)typeof(WorkspaceShell).GetField("_currentPrediction",flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
            panel = (CrystalSphereAssistantPanel)typeof(RunPredictionOverlay).GetField("_panel",flags)!.GetValue(overlay)!;
            ((Button)typeof(WorkspaceShell).GetField("_analysis",flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
            Check(panel != null && new[] { "_search", "_seeds", "_status" }
                .All(name => !((Control)typeof(WorkspaceShell).GetField(name, flags)!.GetValue(shell)!).Visible),
                "in-run workspace hides Search/library/status navigation");
            Check(ReferenceEquals(panel, typeof(WorkspaceShell).GetField("_crystalScene",flags)!.GetValue(shell)),
                "the preserved current-event navigation lazily creates its one session-owned view");
            Check(new[]{"_analysis","_currentPrediction","_notes","_settings","_encyclopedia"}.All(name=>
                ((Control)typeof(WorkspaceShell).GetField(name,flags)!.GetValue(shell)!).Visible),"shared seed prediction and common navigation remain available");
            // Actual native event + saved-player bags feed the production UI
            // projection. The host has no running EventRoom/synchronizer.
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = true;
            if(System.Environment.GetEnvironmentVariable("RT2_RUN_ENTRY_UI_ONLY")!="1")
                Check(PredictorCrystalAssistantChecks.Run("000000000000", 10) == 0, "native Crystal assistant checks pass");
            else GD.Print("RUN_ENTRY_UI_ONLY native algorithm suite skipped; receipt is UI scope only");
            var crystalPlayer = Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(UnlockState.all, 1);
            var crystalRun = RunState.CreateForTest([crystalPlayer], seed: "000000000000", ascensionLevel: 10);
            crystalRun.CurrentActIndex = 1;
            bool driftwoodUi=System.Environment.GetEnvironmentVariable("RT2_CRYSTAL_DRIFTWOOD_UI")=="1";
            if(driftwoodUi) crystalPlayer.AddRelicInternal(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.Driftwood>().ToMutable(),silent:true);
            crystalPlayer.PopulateRelicGrabBagIfNecessary(crystalRun.Rng.UpFront);
            var liveEvent = (MegaCrit.Sts2.Core.Models.Events.CrystalSphere)ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>().ToMutable();
            await liveEvent.BeginEvent(crystalPlayer, null, false);
            var capture = RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.CaptureScene(crystalRun, crystalPlayer, liveEvent, null, "0.111.0");
            // Exercise the existing panel against a deliberately different live
            // run. Any actual scene observation must reject this snapshot, so
            // remaining unstale while hidden proves the display poll was skipped.
            var observationHost = new Control(); host.AddChild(observationHost); observationHost.Hide();
            var observationPanel = new CrystalSphereAssistantPanel(); observationPanel.Initialize(run, runtime);
            observationHost.AddChild(observationPanel);
            try
            {
                observationPanel.ShowSnapshot(capture, startDiscovery: true);
                var observationWorker = (Task)typeof(CrystalSphereAssistantPanel).GetField("_worker", flags)!.GetValue(observationPanel)!;
                var observationCancel = (CancellationTokenSource)typeof(CrystalSphereAssistantPanel).GetField("_cancel", flags)!.GetValue(observationPanel)!;
                typeof(CrystalSphereAssistantPanel).GetField("_poll", flags)!.SetValue(observationPanel, .123);
                observationPanel._Process(1);
                Check(observationPanel.Visible && !observationPanel.IsVisibleInTree() && !observationCancel.IsCancellationRequested &&
                    ReferenceEquals(observationWorker, typeof(CrystalSphereAssistantPanel).GetField("_worker", flags)!.GetValue(observationPanel)) &&
                    !(bool)typeof(CrystalSphereAssistantPanel).GetField("_stale", flags)!.GetValue(observationPanel)! &&
                    (double)typeof(CrystalSphereAssistantPanel).GetField("_poll", flags)!.GetValue(observationPanel)! == .123,
                    "ancestor-hidden Crystal skips scene observation and retains its authorized frozen worker");
                var closedObservation = observationPanel.SuspendSession()!;
                Check(observationCancel.IsCancellationRequested && ReferenceEquals(closedObservation.Worker, observationWorker),
                    "explicit close still cancels and retains the existing Crystal session");
                await observationWorker;
                var lateQueue = new System.Collections.Concurrent.ConcurrentQueue<CrystalSphereAssistantPanel.Update>();
                lateQueue.Enqueue(new([], CrystalSphereAssistantPanel.DiscoveryStop.UserPaused));
                observationPanel.RestoreSession(closedObservation with { Resume = false, Worker = Task.CompletedTask, Updates = lateQueue });
                observationPanel._Process(1);
                Check(lateQueue.IsEmpty && !(bool)typeof(CrystalSphereAssistantPanel).GetField("_stale", flags)!.GetValue(observationPanel)!,
                    "hidden Crystal consumes late completed-worker updates without certifying the native scene");
                observationHost.Show();
                Check((bool)typeof(CrystalSphereAssistantPanel).GetField("_stale", flags)!.GetValue(observationPanel)! &&
                    ((Button)typeof(CrystalSphereAssistantPanel).GetField("_use", flags)!.GetValue(observationPanel)!).Disabled,
                    "restoring ancestor visibility immediately rejects a changed board before any process frame, including completed-worker results");
                GD.Print("CRYSTAL_HIDDEN_OBSERVATION_PASS hiddenPolls=0;workerPreserved=true;closeCancels=true;lateUpdatesConsumed=true;showImmediatelyValidated=true");
            }
            finally { observationHost.Free(); }
            // Native payment changes the scene fingerprint, but entering the
            // selected, untouched board must preserve the same search session.
            foreach(bool debt in new[]{false,true})
            {
                var entryPlayer=Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(UnlockState.all,1);
                var entryRun=RunState.CreateForTest([entryPlayer],seed:"000000000000",ascensionLevel:10);entryRun.CurrentActIndex=1;
                entryPlayer.PopulateRelicGrabBagIfNecessary(entryRun.Rng.UpFront);
                var entryEvent=(MegaCrit.Sts2.Core.Models.Events.CrystalSphere)ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>().ToMutable();
                await entryEvent.BeginEvent(entryPlayer,null,false);
                var beforeEntry=RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.CaptureScene(entryRun,entryPlayer,entryEvent,null,"0.111.0");
                var selectedBranch=beforeEntry.Branches[debt?1:0];
                panel!.ShowSnapshot(beforeEntry with {Branches=[selectedBranch]},startDiscovery:false);
                var preserved=panel.SuspendSession()!;
                if(debt) await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddCurseToDeck<MegaCrit.Sts2.Core.Models.Cards.Debt>(entryPlayer);
                else await MegaCrit.Sts2.Core.Commands.PlayerCmd.LoseGold(entryEvent.DynamicVars["UncoverFutureCost"].BaseValue,entryPlayer,MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Spent);
                var entryGame=new MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame(entryPlayer,entryEvent.Rng,debt?6:3);
                var entered=RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.CaptureScene(entryRun,entryPlayer,entryEvent,entryGame,"0.111.0");
                Check(entered.Fingerprint!=beforeEntry.Fingerprint && panel.AcceptEnteredBoard(entered),"Native branch entry reuses preview instead of reporting stale: "+debt);
                var continued=panel.SuspendSession()!;
                Check(ReferenceEquals(preserved.Reachability,continued.Reachability) && !continued.Stale && continued.Snapshot.Branches.Single().Mode=="Remaining",
                    "Payment transition preserves solver and restricts branch selector to the real board");
                panel.ShowSnapshot(beforeEntry with {Branches=[beforeEntry.Branches[debt?0:1]]},startDiscovery:false);
                Check(!panel.AcceptEnteredBoard(entered),"Entering the other payment branch cannot reuse this plan");
                panel.ShowSnapshot(beforeEntry with {Branches=[selectedBranch]},startDiscovery:false);
                var cell=entryGame.cells.Cast<MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereCell>().First(c=>c.IsHidden);
                entryGame.SetTool(MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame.CrystalSphereToolType.Small);
                await entryGame.CellClicked(cell);
                var progressed=RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.CaptureScene(entryRun,entryPlayer,entryEvent,entryGame,"0.111.0");
                Check(!panel.AcceptEnteredBoard(progressed),"A consumed reveal is not an untouched payment entry");
            }
            GD.Print("CRYSTAL_EXPECTED_ENTRY_PASS nativeThreeAndSix=true;sameSolver=true;wrongBranchAndClickRejected=true");
            panel!.ShowSnapshot(capture, startDiscovery: false);
            var includeEnchantments=(CheckButton)typeof(CrystalSphereAssistantPanel).GetField("_includeEnchantments",flags)!.GetValue(panel)!;
            Check(!includeEnchantments.ButtonPressed,"Enchantment prediction defaults off");
            var plainEnchantmentSession=panel.SuspendSession()!;
            includeEnchantments.ButtonPressed=true;
            var exactEnchantmentSession=panel.SuspendSession()!;
            if(exactEnchantmentSession.Worker!=null) await exactEnchantmentSession.Worker;
            Check(exactEnchantmentSession.IncludeEnchantments && exactEnchantmentSession.Selected.IsEmpty &&
                !ReferenceEquals(plainEnchantmentSession.Reachability,exactEnchantmentSession.Reachability),
                "Enchantment toggle clears targets and constructs an isolated query session");
            panel.RestoreSession(exactEnchantmentSession);
            Check(includeEnchantments.ButtonPressed && panel.SuspendSession()!.IncludeEnchantments,
                "Enchantment preference survives panel session restoration");
            includeEnchantments.ButtonPressed=false;
            var plainAgain=panel.SuspendSession()!;
            if(plainAgain.Worker!=null) await plainAgain.Worker;
            Check(!plainAgain.IncludeEnchantments && !ReferenceEquals(plainAgain.Reachability,exactEnchantmentSession.Reachability),
                "Disabling enchantments starts a fresh wildcard search without sharing exact caches");
            panel.ShowSnapshot(capture,startDiscovery:false);
            Check(!panel.FindChildren("*","CheckButton",true,false).OfType<CheckButton>().Any(b=>b.Text is "显示棋盘辅助" or "Show board guidance"),
                "Board guidance no longer has a visibility toggle");
            var includeDriftwood=(CheckButton)typeof(CrystalSphereAssistantPanel).GetField("_includeDriftwood",flags)!.GetValue(panel)!;
            Check(includeDriftwood.ButtonPressed && includeDriftwood.Disabled==!driftwoodUi,
                "Driftwood prediction defaults on and requires the owned relic");
            includeDriftwood.ButtonPressed=false;
            var noRerollSession=panel.SuspendSession()!;
            if(noRerollSession.Worker!=null) await noRerollSession.Worker;
            panel.RestoreSession(noRerollSession);
            Check(!includeDriftwood.ButtonPressed && !panel.SuspendSession()!.IncludeDriftwood,
                "Disabled Driftwood preference survives session restoration");
            await Task.Run(()=>noRerollSession.Reachability!.Advance([],TimeSpan.Zero));
            var noRerollTable=(RolltheSpire2.Core.PredictorRuntime.CrystalReachabilityTable)
                typeof(RolltheSpire2.Core.PredictorRuntime.PredictorCrystalReachability).GetField("_table",flags)!.GetValue(noRerollSession.Reachability)!;
            Check(!noRerollTable.IncludesRerolls,"Disabling Driftwood actually removes rerolls from the search domain");
            includeDriftwood.ButtonPressed=true;
            var rerollSession=panel.SuspendSession()!;
            if(rerollSession.Worker!=null) await rerollSession.Worker;
            await Task.Run(()=>rerollSession.Reachability!.Advance([],TimeSpan.Zero));
            var rerollTable=(RolltheSpire2.Core.PredictorRuntime.CrystalReachabilityTable)
                typeof(RolltheSpire2.Core.PredictorRuntime.PredictorCrystalReachability).GetField("_table",flags)!.GetValue(rerollSession.Reachability)!;
            Check(rerollTable.IncludesRerolls==driftwoodUi && !ReferenceEquals(rerollSession.Reachability,noRerollSession.Reachability),
                "Re-enabling Driftwood constructs a fresh search with the matching action domain");
            panel.ShowSnapshot(capture,startDiscovery:false);
            var transparentMask=(CheckButton)typeof(CrystalSphereAssistantPanel).GetField("_transparentMask",flags)!.GetValue(panel)!;
            Check(!transparentMask.ButtonPressed && !CrystalSphereMaskAppearance.Enabled,"panel mask switch defaults off");
            transparentMask.ButtonPressed=true;
            Check(CrystalSphereMaskAppearance.Enabled,"mask switch still opts in independently");
            transparentMask.ButtonPressed=false;
            Check(!CrystalSphereMaskAppearance.Enabled,"mask switch still disables independently");
            // A real exact gold receipt on a tiny detached board exercises the
            // same panel controls without waiting for a native whole-board proof.
            var goldBase=capture.Branches[0].Snapshot;
            var goldFog=new bool[121];goldFog[5*11+5]=goldFog[6*11+5]=goldFog[10*11]=true;
            var goldOccupancy=Enumerable.Repeat(-1,121).ToArray();goldOccupancy[5*11+5]=goldOccupancy[6*11+5]=0;
            var goldBoard=goldBase with { Remaining=2,Hidden=goldFog.ToImmutableArray(),Occupancy=goldOccupancy.ToImmutableArray(),
                Items=[new("GOLD_BIG",2,1,5,5,1,false)],Revealed=[] };
            var goldReceipt=await Task.Run(()=>new PredictorCrystalGoldSearch(goldBoard,[],true).Advance(TimeSpan.FromSeconds(1)));
            Check(goldReceipt.GoldComplete && goldReceipt.Gold==30 && goldReceipt.SelectedPlan!=null,"UI gold receipt comes from exact typed replay");
            panel.ShowSnapshot(capture with { Branches=[("Remaining",goldBoard)] },startDiscovery:false);
            var guideButton=(Button)typeof(CrystalSphereAssistantPanel).GetField("_use",flags)!.GetValue(panel)!;
            var goldSummary=(Label)typeof(CrystalSphereAssistantPanel).GetField("_summary",flags)!.GetValue(panel)!;
            ApplyPanelUpdate(new([],Projection:goldReceipt with { GoldComplete=false,GoldUpperBound=40 },CandidatesComplete:true));
            Check(guideButton.Disabled && panel.SuspendSession()!.Projection.SelectedPlan is not null &&
                !panel.SuspendSession()!.Complete && panel.SuspendSession()!.Selected.IsEmpty,
                "A zero-target incumbent survives unfinished gold proof; an unvalidated synthetic board cannot enable its action");
            string incumbentSummary=goldSummary.Text;
            ApplyPanelUpdate(new([],Projection:goldReceipt,CandidatesComplete:false,GoldProgress:new(7,3,1,2,.25)));
            var goldSession=panel.SuspendSession()!;
            Check(goldSummary.Text!=incumbentSummary && goldSession.Projection.GoldComplete && !goldSession.CandidatesComplete &&
                !goldSession.Complete && goldSession.GoldProgress.Examined==7,
                "Gold optimum, candidate closure and their counters remain separate UI receipts");
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.Exhausted,
                Projection:new(System.Collections.Immutable.ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0),
                CandidatesComplete:true));
            Check(panel.SuspendSession()!.Complete && panel.SuspendSession()!.Projection.GoldComplete &&
                panel.SuspendSession()!.Projection.SelectedPlan is not null && guideButton.Disabled,
                "Later candidate publication preserves the completed money guide and closes both lanes");
            panel.RestoreSession(goldSession);
            Check(panel.SuspendSession()!.Selected.IsEmpty && panel.SuspendSession()!.Projection.GoldComplete &&
                !panel.SuspendSession()!.CandidatesComplete && panel.SuspendSession()!.GoldProgress==goldSession.GoldProgress,
                "No-target money guide and proof state survive panel reopening");
            int staleMoneyCallbacks=0;
            void ObserveMoneyGuide(RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveSnapshot scene,string mode,PredictorCrystalSolution plan)=>staleMoneyCallbacks++;
            panel.GuideRequested+=ObserveMoneyGuide;
            typeof(CrystalSphereAssistantPanel).GetMethod("UsePlan",flags)!.Invoke(panel,null);
            panel.GuideRequested-=ObserveMoneyGuide;
            Check(staleMoneyCallbacks==0 && guideButton.Disabled,"No-target money callback still respects the current-scene guard");
            GD.Print("CRYSTAL_GOLD_UI_PASS noTargetGuide=true;incumbentAndOptimalDistinct=true;candidateAndGoldClosureSeparate=true;receiptRetained=true;reopen=true;staleCallbackBlocked=true");
            panel.ShowSnapshot(capture,startDiscovery:false);
            panel.SetProcess(false); // no host EventRoom: projection/layout is the subject here
            var state = capture.Branches[0].Snapshot;
            var replay = RolltheSpire2.Core.PredictorRuntime.PredictorRun.FromCrystal(state);
            foreach (var item in state.Items.Where(i => i.X >= 0 && i.Kind.StartsWith("CARD_")))
            {
                if (replay.Phase != RolltheSpire2.Core.PredictorRuntime.PredictorPhase.EventChoice) break;
                if (!replay.ExportCrystal().Hidden[item.X * 11 + item.Y]) continue;
                replay.Submit(replay.Request!, new RolltheSpire2.Core.PredictorRuntime.SelectCrystalTool(RolltheSpire2.Core.PredictorRuntime.PredictorCrystalTool.Big));
                replay.Submit(replay.Request!, new RolltheSpire2.Core.PredictorRuntime.RevealCrystalCell(item.X, item.Y));
            }
            while (replay.Phase == RolltheSpire2.Core.PredictorRuntime.PredictorPhase.EventChoice)
            {
                int next = replay.ExportCrystal().Hidden.IndexOf(true);
                replay.Submit(replay.Request!, new RolltheSpire2.Core.PredictorRuntime.SelectCrystalTool(RolltheSpire2.Core.PredictorRuntime.PredictorCrystalTool.Small));
                replay.Submit(replay.Request!, new RolltheSpire2.Core.PredictorRuntime.RevealCrystalCell(next / 11, next % 11));
            }
            var target = replay.Rewards.First(r => r.Kind == RolltheSpire2.Core.PredictorRuntime.PredictorRewardKind.Card).Cards[0].Key;
            var solved = await Task.Run(() => RolltheSpire2.Core.PredictorRuntime.PredictorCrystalSearch.Solve(state, [target], TimeSpan.FromSeconds(10)));
            Check(solved.Status == "Found", "render uses an actual executable solution");
            CheckCrystalLocalization(host,runtime,run,crystalRun,liveEvent,capture,solved,Check);
            async Task FinishQuery()
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
                    var queue=(System.Collections.Concurrent.ConcurrentQueue<CrystalSphereAssistantPanel.Update>)
                        typeof(CrystalSphereAssistantPanel).GetField("_updates",flags)!.GetValue(panel)!;
                    while(queue.TryDequeue(out var update)) ApplyPanelUpdate(update);
                } while(typeof(CrystalSphereAssistantPanel).GetField("_worker",flags)!.GetValue(panel) is Task { IsCompleted:false }
                    && timer.Elapsed<TimeSpan.FromSeconds(15));
                Check(timer.Elapsed<TimeSpan.FromSeconds(15),"UI query worker yields within one discovery round");
                if(panel.IsVisibleInTree()) await UiSurfaceChecks.AwaitPageReady(host,shell);
                else SettleCrystalPresentation();
            }
            typeof(CrystalSphereAssistantPanel).GetMethod("StartDiscovery",flags)!.Invoke(panel,null);
            await FinishQuery();
            var projection = (RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection", flags)!.GetValue(panel)!;
            Check(projection.Available.Count>0,"production conditional query supplies the UI without prefilled rows");
            UiSurfaceChecks.RunCrystalLifecycleChecks(host, run, runtime, capture, projection, Check);
            var chosenCard = projection.Available.Keys.First(o => o.Kind == RolltheSpire2.Core.PredictorRuntime.PredictorRewardKind.Card);
            // Same identity/level must remain visually distinct when enchanted.
            var variants=new[] {chosenCard with { Enchantment=new(null,0) },
                chosenCard with { Enchantment=new(new ModelKey("ENCHANTMENT","SWIFT"),1) },
                chosenCard with { Enchantment=new(new ModelKey("ENCHANTMENT","NIMBLE"),2) }};
            var variantNames=variants.Select(v=>(string)typeof(CrystalSphereAssistantPanel).GetMethod("NameOf",flags)!.Invoke(panel,[v])!).ToArray();
            Check(variantNames.Distinct().Count()==3,"exact-none, Swift and Nimble card variants have distinct UI names");
            foreach(var variant in variants)
            {
                var variantTile=(Control)typeof(CrystalSphereAssistantPanel).GetMethod("Tile",flags)!.Invoke(panel,[variant,false])!;
                Check(variantTile.GetChildren().OfType<Label>().Any(label=>!string.IsNullOrWhiteSpace(label.Text)),
                    "enchanted candidate tile includes a visible variant badge");
                variantTile.Free();
            }
            var shiftedNiche=state.State.Streams.Single(s=>s.Stream==PredictorStream.Niche).Restore();shiftedNiche.Advance(1);
            var shifted=state with { State=state.State.WithRng(PredictorStream.Niche,shiftedNiche) };
            Check(CrystalSphereGuidance.Matches(state,shifted,false) && !CrystalSphereGuidance.Matches(state,shifted,false,observeNiche:true),
                "ordinary board guidance keeps local snapshot scope while Niche-dependent enchanted guidance detects stream changes");
            var tiles=(Dictionary<RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption,Control>)
                typeof(CrystalSphereAssistantPanel).GetField("_optionTiles",flags)!.GetValue(panel)!;
            var chosenTile=tiles[chosenCard];
            var chosenButton=chosenTile.GetChildren().OfType<Button>().First();
            var chosenId=chosenButton.GetInstanceId();
            ApplyPanelUpdate(new([],Progress:new(1,0,0,1,.15),Projection:projection));
            Check(ReferenceEquals(tiles[chosenCard],chosenTile) && chosenButton.GetInstanceId()==chosenId && !chosenButton.IsQueuedForDeletion(),
                "progress updates retain the actual clickable candidate button");
            chosenButton.EmitSignal(Button.SignalName.Pressed);
            var pendingTiles=tiles.Where(p=>p.Key!=chosenCard).Select(p=>p.Value).ToArray();
            Check(pendingTiles.Length>0 && pendingTiles.All(t=>!t.IsQueuedForDeletion() && !t.GetChildren().OfType<Button>().First().Disabled),
                "first selection keeps pending candidates clickable for focused verification");
            // Real same-board root certificates, published before traversal.
            System.Collections.Immutable.ImmutableArray<CrystalRewardOption> rootSlotPublication=[];
            var earlyExclusions=await Task.Run(()=>new RolltheSpire2.Core.PredictorRuntime.PredictorCrystalReachability(
                state,true,potionScenarioBound:true,workers:4,includeRerolls:driftwoodUi).Advance([chosenCard],TimeSpan.Zero,
                    publishExcluded:denials=>rootSlotPublication=denials));
            var excludedTiles=earlyExclusions.ExcludedCandidates.Where(tiles.ContainsKey).ToArray();
            Check(!earlyExclusions.Complete && excludedTiles.Length>0,"Root bounds can retire visible candidates before full completion");
            var earlyTiles=rootSlotPublication.Where(tiles.ContainsKey).ToArray();
            Check(earlyTiles.Length>0,"Root-slot callback supplies actual visible candidates before the complete initial projection");
            ApplyPanelUpdate(new([],ExcludedCandidates:rootSlotPublication));
            Check(earlyTiles.All(o=>!tiles.ContainsKey(o)) &&
                !(bool)typeof(CrystalSphereAssistantPanel).GetField("_complete",flags)!.GetValue(panel)!,
                "An exclusion-only early publication removes tiles without marking the query complete");
            int beforeExclusions=tiles.Count;
            ApplyPanelUpdate(new([],Progress:new(1,0,0,1,.15)));
            Check(tiles.Count==beforeExclusions,"Unfinished slice without negative evidence preserves grey candidates");
            ApplyPanelUpdate(new([],ExcludedCandidates:earlyExclusions.ExcludedCandidates));
            Check(excludedTiles.All(o=>!tiles.ContainsKey(o)) &&
                ((System.Collections.Immutable.ImmutableArray<RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption>)typeof(CrystalSphereAssistantPanel).GetField("_selected",flags)!.GetValue(panel)!).Contains(chosenCard) &&
                !(bool)typeof(CrystalSphereAssistantPanel).GetField("_complete",flags)!.GetValue(panel)!,
                "Certified exclusions disappear immediately while selected plan and unfinished query survive");
            Console.WriteLine($"CRYSTAL_UI_EARLY_EXCLUSION_PASS removed={excludedTiles.Length};queryComplete=false");
            Check(((RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!).SelectedPlan!=null,
                "click immediately retains the verified selected plan before further search");
            await FinishQuery();
            Check(((RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!).SelectedPlan != null,
                "selected reward has an executable joint plan");
            // Restore one pending tile before its already-proven exclusion is
            // delivered, as can happen while the background queue is in flight.
            var pending=(HashSet<CrystalRewardOption>)typeof(CrystalSphereAssistantPanel).GetField("_pendingOptions",flags)!.GetValue(panel)!;
            var denied=excludedTiles[0];pending.Add(denied);
            typeof(CrystalSphereAssistantPanel).GetField("_complete",flags)!.SetValue(panel,false);
            typeof(CrystalSphereAssistantPanel).GetField("_candidatesComplete",flags)!.SetValue(panel,false);
            typeof(CrystalSphereAssistantPanel).GetMethod("Render",flags)!.Invoke(panel,null);
            SettleCrystalPresentation();
            var oldQueue=typeof(CrystalSphereAssistantPanel).GetField("_updates",flags)!.GetValue(panel);
            tiles[denied].GetChildren().OfType<Button>().First().EmitSignal(Button.SignalName.Pressed);
            Check(Equals(typeof(CrystalSphereAssistantPanel).GetField("_focusTarget",flags)!.GetValue(panel),denied) &&
                !ReferenceEquals(oldQueue,typeof(CrystalSphereAssistantPanel).GetField("_updates",flags)!.GetValue(panel)),
                "Grey click switches to an exclusive focused queue without accepting stale publications");
            Check(((System.Collections.Immutable.ImmutableArray<CrystalRewardOption>)typeof(CrystalSphereAssistantPanel).GetField("_selected",flags)!.GetValue(panel)!).SequenceEqual([chosenCard]),
                "Grey click requests verification without selecting an unproven reward");
            await FinishQuery();
            Check((CrystalFocusVerdict)typeof(CrystalSphereAssistantPanel).GetField("_focusVerdict",flags)!.GetValue(panel)! == CrystalFocusVerdict.Unreachable &&
                !tiles.ContainsKey(denied) && !(bool)typeof(CrystalSphereAssistantPanel).GetField("_complete",flags)!.GetValue(panel)!,
                "Focused negative removes only its candidate, not a global exhaustion claim");
            var focusedSession=panel.SuspendSession()!;
            Check(!focusedSession.Resume && focusedSession.FocusTarget==denied,"Finished focus remains a readable result on reopening");
            panel.RestoreSession(focusedSession with { Resume=true,FocusVerdict=CrystalFocusVerdict.Checking });
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.UserPaused,
                Focus:new(denied,CrystalFocusVerdict.Unreachable,focusedSession.Projection,default)));
            panel._Process(0);
            Check(Equals(typeof(CrystalSphereAssistantPanel).GetField("_focusTarget",flags)!.GetValue(panel),denied) &&
                !(bool)typeof(CrystalSphereAssistantPanel).GetField("_resumeOnOpen",flags)!.GetValue(panel)!,
                "Focus completed during closing stays a verdict instead of silently resuming broad search");
            panel.RestoreSession(focusedSession);
            var focusedProjection=(CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!;
            var accepted=focusedProjection.Available.Keys.First();
            typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.SetValue(panel,
                focusedProjection with { Available=focusedProjection.Available.Remove(accepted) });
            pending.Add(accepted);typeof(CrystalSphereAssistantPanel).GetMethod("Render",flags)!.Invoke(panel,null);
            SettleCrystalPresentation();
            tiles[accepted].GetChildren().OfType<Button>().First().EmitSignal(Button.SignalName.Pressed);
            await FinishQuery();
            Check((CrystalFocusVerdict)typeof(CrystalSphereAssistantPanel).GetField("_focusVerdict",flags)!.GetValue(panel)! == CrystalFocusVerdict.Reachable &&
                ((CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!).SelectedPlan!=null,
                "Focused positive is published while the existing selected plan remains usable");
            ((Button)typeof(CrystalSphereAssistantPanel).GetField("_allCandidates",flags)!.GetValue(panel)!).EmitSignal(Button.SignalName.Pressed);
            await FinishQuery();
            Check(typeof(CrystalSphereAssistantPanel).GetField("_focusTarget",flags)!.GetValue(panel)==null,"Return to all candidates resumes ordinary scheduling");
            Console.WriteLine("CRYSTAL_FOCUS_UI_PASS greyClick=true;positiveAndNegative=true;selectedPlanRetained=true;reopen=true;backToBroad=true");
            var statusReceipt = (Label)typeof(CrystalSphereAssistantPanel).GetField("_status",flags)!.GetValue(panel)!;
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.TimeLimit, Progress:new(12345,6789,0,42,10.2)));
            string timedOut=statusReceipt.Text;
            Check(statusReceipt.TooltipText.Contains(12345.ToString("N0")) && statusReceipt.TooltipText.Contains(6789.ToString("N0")),
                "detailed counters live in tooltip instead of the main status");
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.UserPaused));
            Check(statusReceipt.Text!=timedOut,"manual pause and time limit have different visible explanations");
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.Exhausted,RolltheSpire2.Core.PredictorRuntime.CrystalDiscoveryPhase.Complete,
                CandidatesComplete:true));
            Check(statusReceipt.Text!=timedOut && panel.SuspendSession()!.CandidatesComplete &&
                panel.SuspendSession()!.Complete==panel.SuspendSession()!.Projection.GoldComplete,
                "candidate exhaustion remains separate from the gold proof and visible stop reason");
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.Failed));
            Check(!(bool)typeof(CrystalSphereAssistantPanel).GetField("_complete",flags)!.GetValue(panel)! && statusReceipt.Text!=timedOut,
                "failure does not display an exhaustion proof");
            ApplyPanelUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.TimeLimit, Progress:new(12345,6789,0,42,10.2)));
            Control canvas = shell;
            var status = (Label)typeof(CrystalSphereAssistantPanel).GetField("_status", flags)!.GetValue(panel)!;
            async Task ShowCapturedCrystalPage(Func<Task> observe)
            {
                // Layout/retargeting uses a board captured from crystalRun;
                // ordinary shell navigation uses the outside multiplayer run.
                // Keep the real event boundary through all awaited presentation
                // frames, then restore it before leaving this Crystal sequence.
                var panelRun = typeof(CrystalSphereAssistantPanel).GetField("_run", flags)!;
                var previousPanelRun = panelRun.GetValue(panel);
                bool previousProcessing = panel.IsProcessing();
                var previousLocal = LocalContext.NetId;
                var syncProperty = typeof(RunManager).GetProperty(nameof(RunManager.EventSynchronizer))!;
                var previousSync = syncProperty.GetValue(RunManager.Instance);
                var net = new NetSingleplayerGameService();
                using var sync = new EventSynchronizer(new RunLocationTargetedMessageBuffer(net), net, crystalRun, crystalRun,
                    crystalPlayer.NetId, crystalRun.Rng.Seed);
                bool addedRoom = crystalRun.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.EventRoom { CanonicalEvent.Id.Entry: "CRYSTAL_SPHERE" };
                bool pushedRoom=false;
                try
                {
                    ((List<EventModel>)typeof(EventSynchronizer).GetField("_events", flags)!.GetValue(sync)!).Add(liveEvent);
                    syncProperty.SetValue(RunManager.Instance, sync);
                    LocalContext.NetId = crystalPlayer.NetId; panelRun.SetValue(panel, crystalRun);panel.SetProcess(true);
                    if (addedRoom)
                    {
                        crystalRun.PushRoom(new MegaCrit.Sts2.Core.Rooms.EventRoom(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>()));
                        pushedRoom=true;
                    }
                    Check(RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.Fingerprint(crystalRun, host.GetTree().Root) == capture.Fingerprint,
                        "navigation fixture supplies the real native event and exact captured board before Show freshness");
                    ((Button)typeof(WorkspaceShell).GetField("_currentPrediction", flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
                    Check(!(bool)typeof(CrystalSphereAssistantPanel).GetField("_stale", flags)!.GetValue(panel)!,
                        "showing the unchanged actual Crystal context retains its active query");
                    await UiSurfaceChecks.AwaitPageReady(host,shell);
                    await observe();
                    Check(RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.Fingerprint(crystalRun,host.GetTree().Root)==capture.Fingerprint,
                        "awaited Crystal presentation and retargeting preserve the real captured board");
                }
                finally
                {
                    try
                    {
                        panel.SetProcess(previousProcessing);
                        if (pushedRoom) crystalRun.PopCurrentRoom();
                    }
                    finally
                    {
                        panelRun.SetValue(panel, previousPanelRun); LocalContext.NetId = previousLocal;
                        syncProperty.SetValue(RunManager.Instance, previousSync);
                    }
                }
            }
            // The launcher initially opens seed analysis for the outside run.
            var usage=panel.FindChild("CrystalUsage",true,false) as Control
                ?? throw new InvalidOperationException("Crystal usage surface missing");
            var optionControls = new[] { "_avoidCurse", "_includeDriftwood", "_includeEnchantments", "_transparentMask" }
                .Select(name => (CheckButton)typeof(CrystalSphereAssistantPanel).GetField(name, flags)!.GetValue(panel)!).ToArray();
            await ShowCapturedCrystalPage(async ()=>
            {
            Check(panel.FindChild("CrystalMoreOptions",true,false) is null && !usage.Visible &&
                optionControls.All(option => option.IsVisibleInTree() && ReferenceEquals(option.GetParent(), optionControls[0].GetParent())),
                "Four Crystal switches are always visible in the left column while instructions start collapsed");
            panel.ApplyLanguage("en");
            await UiSurfaceChecks.AwaitPageReady(host,shell);
            foreach (var size in new[] { new Vector2I(1280,720), new Vector2I(2560,1340) })
            {
                host.GetWindow().Size = size; stack.Size = size;
                for (int frame = 0; frame < 5; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var bounds = canvas.GetGlobalRect();
                Check(bounds.Position.X >= 0 && bounds.Position.Y >= 0 && bounds.End.X <= size.X+1 && bounds.End.Y <= size.Y+1,
                    "shared-size dialog stays in viewport without in-run-only shrinking");
                Check(bounds.Encloses(shell.CloseButton.GetGlobalRect()) && bounds.Encloses(status.GetGlobalRect()),
                    "title/status/close remain inside dialog");
                Check(status.GetGlobalRect().Position.Y > shell.CloseButton.GetGlobalRect().Position.Y, "status is below header");
                var targetRow = (Control)typeof(CrystalSphereAssistantPanel).GetField("_targetRow", flags)!.GetValue(panel)!;
                Check(bounds.Encloses(targetRow.GetGlobalRect()), "card tiles fit inside dialog");
                float expectedScale=Math.Min(1f,Math.Min(size.X/1600f,size.Y/900f));
                Check(Math.Abs(shell.Scale.X-expectedScale)<.001,"in-run and external shells use identical canvas scale");
                Check(!tiles.ContainsKey(RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption.AnyRelic),"generic any-relic option is absent from UI");
                var currentTab = (Button)typeof(WorkspaceShell).GetField("_currentPrediction", flags)!.GetValue(shell)!;
                Check(currentTab.Visible && currentTab.Text.Length > 0, "current prediction top-level tab visible");
                if (DisplayServer.GetName() != "headless")
                {
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"crystal-panel-{size.X}.png"));
                }
            }

            });

            var savedSelection=(System.Collections.Immutable.ImmutableArray<RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption>)
                typeof(CrystalSphereAssistantPanel).GetField("_selected",flags)!.GetValue(panel)!;
            var savedSession=typeof(CrystalSphereAssistantPanel).GetField("_reachability",flags)!.GetValue(panel);
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=false;
            ((Button)typeof(WorkspaceShell).GetField("_analysis",flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
            var seedPage=(AnalysisPage)typeof(WorkspaceShell).GetField("_predictor",flags)!.GetValue(shell)!;
            Check(seedPage.Visible && !panel.Visible && seedPage.CurrentDraft.RawSeed==run.Rng.StringSeed,"seed prediction opens with current real seed");
            ((Button)typeof(WorkspaceShell).GetField("_notes",flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
            Check(((Control)typeof(WorkspaceShell).GetField("_notesPage",flags)!.GetValue(shell)!).Visible,"shared About page opens in-run");
            if (DisplayServer.GetName() != "headless")
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "notes-navigation.png"));
            }
            ((Button)typeof(WorkspaceShell).GetField("_settings",flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
            await UiSurfaceChecks.AwaitPageReady(host, shell);
            var retainedSettingsPage = (Control)typeof(WorkspaceShell).GetField("_settingsPage",flags)!.GetValue(shell)!;
            Check(retainedSettingsPage.Visible && typeof(WorkspaceShell).GetField("_feedbackPage", flags)!.GetValue(shell) is null &&
                typeof(WorkspaceShell).GetField("_seedLibrary", flags)!.GetValue(shell) is null,
                "first shared Settings visit constructs only Settings in-run");
            Check(shell.FindChild("SettingsCategoryPage1", true, false) is null &&
                shell.FindChild("SettingsCategoryPage2", true, false) is null && shell.FindChild("SettingsCategoryPage3", true, false) is null,
                "in-run first Settings visit constructs only its Features category");
            ((Button)shell.FindChild("SettingsCategory1", true, false)).EmitSignal(Button.SignalName.Pressed);
            await UiSurfaceChecks.AwaitPageReady(host, shell);
            var fontChoice = (OptionButton)typeof(WorkspaceShell).GetField("_fontChoice", flags)!.GetValue(shell)!;
            var fontStore = (RolltheSpire2.Ui.Workspace.SearchWorkspace)typeof(WorkspaceShell).GetField("_persistence", flags)!.GetValue(shell)!;
            int originalFont = fontStore.Preferences.FontChoice == "game-sans" ? 0 : 1;
            var retainedProjection = typeof(CrystalSphereAssistantPanel).GetField("_projection", flags)!.GetValue(panel);
            try
            {
                foreach (int index in new[] { 0, 1 })
                {
                    fontChoice.Select(index); fontChoice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                    UiSurfaceChecks.CheckSharedGameFonts(Check);
                    Check(fontStore.Preferences.FontChoice == (index == 0 ? "game-sans" : "game-serif") &&
                        ReferenceEquals(savedSession, typeof(CrystalSphereAssistantPanel).GetField("_reachability", flags)!.GetValue(panel)) &&
                        ReferenceEquals(retainedProjection, typeof(CrystalSphereAssistantPanel).GetField("_projection", flags)!.GetValue(panel)) &&
                        savedSelection.SequenceEqual((ImmutableArray<CrystalRewardOption>)typeof(CrystalSphereAssistantPanel).GetField("_selected", flags)!.GetValue(panel)!),
                        "actual in-run font selection preserves the current query, selection and exact projection");
                    Check(ReferenceEquals(status, typeof(CrystalSphereAssistantPanel).GetField("_status", flags)!.GetValue(panel)),
                        "font selection retains the live Crystal status label instance");
                    Check(ReferenceEquals(guideButton, typeof(CrystalSphereAssistantPanel).GetField("_use", flags)!.GetValue(panel)),
                        "font selection retains the live Crystal guide button instance");
                    GD.Print($"RUN_ENTRY_FONT_OBSERVATION choice={MainPanelTheme.FontChoice};phase=synchronous;status={status.GetThemeFont("font").ResourcePath};guide={guideButton.GetThemeFont("font").ResourcePath}");
                    // Theme resource changes propagate through Godot's deferred
                    // control theme caches. Inspect their effective fonts after
                    // a rendered frame, as the font-settings checks already do.
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Font statusFont = status.GetThemeFont("font"), guideFont = guideButton.GetThemeFont("font");
                    Check(statusFont.GetInstanceId() == MainPanelTheme.Regular.GetInstanceId(),
                        $"rendered Crystal status uses selected Regular: choice={MainPanelTheme.FontChoice};actual={statusFont.ResourcePath}#{statusFont.GetInstanceId()};expected={MainPanelTheme.Regular.ResourcePath}#{MainPanelTheme.Regular.GetInstanceId()}");
                    Check(guideFont.GetInstanceId() == MainPanelTheme.Medium.GetInstanceId(),
                        $"rendered Crystal guide uses selected Medium: choice={MainPanelTheme.FontChoice};actual={guideFont.ResourcePath}#{guideFont.GetInstanceId()};expected={MainPanelTheme.Medium.ResourcePath}#{MainPanelTheme.Medium.GetInstanceId()}");
                }
            }
            finally
            {
                fontChoice.Select(originalFont); fontChoice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)originalFont);
            }
            GD.Print("RUN_ENTRY_FONT_PASS choices=2;sharedResources=true;liveControlsRetained=true;queryAndProjectionRetained=true;preferenceRestored=true");
            CrystalRewardOption liveChoice=null!;
            await ShowCapturedCrystalPage(async ()=>
            {
            Check(panel.Visible && ReferenceEquals(savedSession,typeof(CrystalSphereAssistantPanel).GetField("_reachability",flags)!.GetValue(panel)) &&
                savedSelection.SequenceEqual((System.Collections.Immutable.ImmutableArray<RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption>)
                    typeof(CrystalSphereAssistantPanel).GetField("_selected",flags)!.GetValue(panel)!),"navigation retains selected rewards and search session");
            var languageChoice=(OptionButton)typeof(WorkspaceShell).GetField("_language",flags)!.GetValue(shell)!;
            int oldLanguage=languageChoice.Selected;
            try
            {
                languageChoice.Select(1);languageChoice.EmitSignal(OptionButton.SignalName.ItemSelected,1L);
                await UiSurfaceChecks.AwaitPageReady(host,shell);
                Check(((Button)typeof(WorkspaceShell).GetField("_currentPrediction",flags)!.GetValue(shell)!).Text=="局内预测" &&
                    ReferenceEquals(savedSession,typeof(CrystalSphereAssistantPanel).GetField("_reachability",flags)!.GetValue(panel)),
                    "shared language setting updates scene navigation without resetting search");
                foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(2560,1340)})
                {
                    host.GetWindow().Size=size;stack.Size=size;
                    for(int frame=0;frame<5;frame++) await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
                    var selectedHost=(Control)typeof(CrystalSphereAssistantPanel).GetField("_targetRow",flags)!.GetValue(panel)!;
                    var selectedButton=selectedHost.GetChildren().OfType<Control>().First();
                    GD.Print($"CRYSTAL_SELECTED_LAYOUT size={size};host={selectedHost.GetGlobalRect()};button={selectedButton.GetGlobalRect()};visible={selectedButton.IsVisibleInTree()};scroll={((Control)selectedHost.GetParent()).GetGlobalRect()}");
                    Check(selectedButton.IsVisibleInTree() && selectedButton.Size.Y>0 && ((Control)selectedHost.GetParent()).GetGlobalRect().Encloses(selectedButton.GetGlobalRect()),"localized selected reward remains visible in target panel");
                    if(DisplayServer.GetName()!="headless")
                    {
                        await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,$"crystal-panel-zh-{size.X}.png"));
                    }
                    usage.Show();
                    for(int frame=0;frame<5;frame++) await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
                    Check(canvas.GetGlobalRect().Encloses(status.GetGlobalRect()) &&
                        canvas.GetGlobalRect().Encloses(guideButton.GetGlobalRect()),
                        "expanded instructions retain status and guidance inside the shared canvas");
                    if(DisplayServer.GetName()!="headless")
                    {
                        await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,$"crystal-expanded-zh-{size.X}.png"));
                    }
                    usage.Hide();
                }
            }
            finally { languageChoice.Select(oldLanguage);languageChoice.EmitSignal(OptionButton.SignalName.ItemSelected,oldLanguage); }
            panel.SelectOption(chosenCard);
            Check(((RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!).Available.Count>0,
                "return to a cached selection restores verified candidates synchronously");
            await FinishQuery();
            // Retarget while the worker is still starting. Its old queue must not
            // overwrite the restored empty-selection query.
            panel.SelectOption(chosenCard);
            panel.SelectOption(chosenCard);
            await FinishQuery();
            Check(((System.Collections.Immutable.ImmutableArray<RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption>)
                typeof(CrystalSphereAssistantPanel).GetField("_selected",flags)!.GetValue(panel)!).IsEmpty,"deselect retains its own query revision");
            Check(typeof(CrystalSphereAssistantPanel).GetMethod("PickTarget",flags)==null &&
                typeof(CrystalSphereAssistantPanel).GetField("_picker",flags)==null,"Crystal uses candidate tiles without a separate card picker");
            var groups=(Dictionary<string,(Label Count,HFlowContainer Flow)>)typeof(CrystalSphereAssistantPanel).GetField("_groups",flags)!.GetValue(panel)!;
            Check(groups.Keys.SequenceEqual(new[]{"Relic","Rare","Uncommon","Common","Potion"}),"candidate group order is relic, rare, uncommon, common, potion");
            var upgradedChoice=chosenCard with { UpgradeLevel=1 };
            var upgradeTile=(Control)typeof(CrystalSphereAssistantPanel).GetMethod("Tile",flags)!.Invoke(panel,[upgradedChoice,false])!;
            Check(upgradeTile.GetChildren().OfType<Button>().Single().AccessibilityName.ToString().EndsWith("+"),"upgraded tile has distinct accessible name");
            Check(upgradeTile.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.EndsWith("+")),"upgraded tile visibly carries its exact level");
            upgradeTile.Free();

            var branchSelector = (OptionButton)typeof(CrystalSphereAssistantPanel).GetField("_branch",flags)!.GetValue(panel)!;
            Check(branchSelector.ItemCount == 2, "initial paid and Debt budgets remain distinct");
            branchSelector.Select(1); branchSelector.EmitSignal(OptionButton.SignalName.ItemSelected,1L);
            Check(((RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!).Available.IsEmpty,
                "switching budget drops the previous branch's certified options");
            var liveTimer=System.Diagnostics.Stopwatch.StartNew();
            RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection liveProjection;
            do
            {
                await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
                var queue=(System.Collections.Concurrent.ConcurrentQueue<CrystalSphereAssistantPanel.Update>)
                    typeof(CrystalSphereAssistantPanel).GetField("_updates",flags)!.GetValue(panel)!;
                while(queue.TryDequeue(out var update)) ApplyPanelUpdate(update);
                liveProjection=(RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)
                    typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!;
            } while(liveProjection.Available.IsEmpty && liveTimer.Elapsed<TimeSpan.FromSeconds(5));
            Check(!liveProjection.Available.IsEmpty && typeof(CrystalSphereAssistantPanel).GetField("_worker",flags)!.GetValue(panel) is Task { IsCompleted:false },
                "six-reveal candidates are interactive while the actual worker is running");
            liveChoice=liveProjection.Available.Where(p=>p.Key.Kind==RolltheSpire2.Core.PredictorRuntime.PredictorRewardKind.Card)
                .OrderByDescending(p=>p.Value.RerollRewardIndices.Length).First().Key;
            if(driftwoodUi) Check(liveProjection.Available[liveChoice].RerollRewardIndices.Length>0,
                "Actual asynchronous panel automatically includes Driftwood suffix witnesses");
            var tilePresentationTimer=System.Diagnostics.Stopwatch.StartNew();
            while(!tiles.TryGetValue(liveChoice,out var liveTile) || !liveTile.IsVisibleInTree())
            {
                Check(tilePresentationTimer.Elapsed<TimeSpan.FromSeconds(5),"live candidate reaches visible deferred presentation promptly");
                await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            var liveButton=tiles[liveChoice].GetChildren().OfType<Button>().First();
            Check(liveButton.IsVisibleInTree() && typeof(CrystalSphereAssistantPanel).GetField("_worker",flags)!.GetValue(panel) is Task { IsCompleted:false },
                "deferred candidate control is interactive while the actual worker is still running");
            ApplyPanelUpdate(new([],Projection:liveProjection));
            Check(!liveButton.IsQueuedForDeletion(),"running refresh preserves a pressed target");
            var retargetTimer=System.Diagnostics.Stopwatch.StartNew();
            liveButton.EmitSignal(Button.SignalName.Pressed);
            double clickMs=retargetTimer.Elapsed.TotalMilliseconds;
            Check(((RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!).SelectedPlan!=null,
                "mid-search click switches query and keeps its executable witness immediately");
            double firstReceiptMs=-1,firstVisibleMs=-1,maxRetargetFrameMs=0,lastRetargetFrameMs=retargetTimer.Elapsed.TotalMilliseconds;
            int retargetFrames=0;
            while(firstVisibleMs<0 && retargetTimer.Elapsed<TimeSpan.FromSeconds(5))
            {
                await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
                double now=retargetTimer.Elapsed.TotalMilliseconds;
                maxRetargetFrameMs=Math.Max(maxRetargetFrameMs,now-lastRetargetFrameMs);lastRetargetFrameMs=now;retargetFrames++;
                var actual=(CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!;
                if(!actual.Available.IsEmpty && firstReceiptMs<0) firstReceiptMs=now;
                // Observe the real bounded consumer and rendered tile state;
                // manually draining its queue would hide presentation latency.
                if(actual.Available.Keys.Any(option=>option!=liveChoice && tiles.TryGetValue(option,out var visible) &&
                    visible.IsVisibleInTree() && visible.Modulate.A>.99f)) firstVisibleMs=now;
            }
            var firstCompatible=(RolltheSpire2.Core.PredictorRuntime.CrystalOptionProjection)typeof(CrystalSphereAssistantPanel).GetField("_projection",flags)!.GetValue(panel)!;
            Check(!firstCompatible.Available.IsEmpty && firstVisibleMs>=0,"active retarget visibly presents compatible candidates through the production queue promptly");
            GD.Print($"CRYSTAL_UI_RETARGET clickMs={clickMs:F2};firstReceiptMs={firstReceiptMs:F2};firstVisibleMs={firstVisibleMs:F2};maxFrameMs={maxRetargetFrameMs:F2};frames={retargetFrames};options={firstCompatible.Available.Count};actualWorker=true;manualDrain=false");
            while(retargetTimer.Elapsed<TimeSpan.FromSeconds(11))
            {
                await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
                var continuousQueue=(System.Collections.Concurrent.ConcurrentQueue<CrystalSphereAssistantPanel.Update>)typeof(CrystalSphereAssistantPanel).GetField("_updates",flags)!.GetValue(panel)!;
                while(continuousQueue.TryDequeue(out var update)) ApplyPanelUpdate(update);
            }
            bool stillRunning=(bool)typeof(CrystalSphereAssistantPanel).GetField("_running",flags)!.GetValue(panel)!;
            Check(stillRunning || (bool)typeof(CrystalSphereAssistantPanel).GetField("_complete",flags)!.GetValue(panel)!,
                "search continues beyond ten seconds unless actually complete");
            GD.Print($"CRYSTAL_CONTINUOUS_SEARCH elapsed={retargetTimer.Elapsed.TotalSeconds:F2};running={stillRunning}");
            if(stillRunning) ((Button)typeof(CrystalSphereAssistantPanel).GetField("_stop",flags)!.GetValue(panel)!).EmitSignal(Button.SignalName.Pressed);
            await FinishQuery();
            Check(!(bool)typeof(CrystalSphereAssistantPanel).GetField("_running",flags)!.GetValue(panel)!,"manual pause joins the continuous worker");
            if(stillRunning)
            {
                typeof(CrystalSphereAssistantPanel).GetMethod("StartDiscovery",flags)!.Invoke(panel,null);
                Check((bool)typeof(CrystalSphereAssistantPanel).GetField("_running",flags)!.GetValue(panel)!,"continuous search can resume after manual pause");
            }
            Check(((System.Collections.Immutable.ImmutableArray<RolltheSpire2.Core.PredictorRuntime.CrystalRewardOption>)
                typeof(CrystalSphereAssistantPanel).GetField("_selected",flags)!.GetValue(panel)!).SequenceEqual([liveChoice]),
                "superseded worker cannot overwrite the mid-search selection");
            });
            // Restore the ordinary run/event boundary before intentional stale
            // close/reopen assertions; their mismatched context is the subject.
            var closeCancel = (CancellationTokenSource)typeof(CrystalSphereAssistantPanel).GetField("_cancel",flags)!.GetValue(panel)!;
            typeof(RunPredictionOverlay).GetMethod("Close", flags)!.Invoke(overlay, null);
            var retainedSession = (CrystalSphereAssistantPanel.Session?)typeof(RunPredictionOverlay).GetField("_session",flags)!.GetValue(overlay);
            Check(ReferenceEquals(stack.Peek(),below) && below.Visible, "close restores previous screen");
            Check(closeCancel.IsCancellationRequested && !overlay.IsOpen && !overlay.Visible && overlay.IsInsideTree() &&
                overlay.ProcessMode == Node.ProcessModeEnum.Disabled &&
                !(bool)typeof(CrystalSphereAssistantPanel).GetField("_exited",flags)!.GetValue(panel)!,
                "explicit close cancels work and disables the retained tree without exiting its panel");
            Check(retainedSession!=null && retainedSession.Selected.SequenceEqual([liveChoice]) && retainedSession.Projection.SelectedPlan!=null,
                "Closing the overlay retains the selected plan and frozen search session");
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=false;overlay.Open();MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
            var reopenedPanel=(CrystalSphereAssistantPanel)typeof(RunPredictionOverlay).GetField("_panel",flags)!.GetValue(overlay)!;
            Check(ReferenceEquals(reopenedPanel,panel) &&
                ReferenceEquals(shell,typeof(RunPredictionOverlay).GetField("_shell",flags)!.GetValue(overlay)) &&
                stack.Peek() is RunPredictionOverlayStackEntry && !ReferenceEquals(stack.Peek(),firstEntry) &&
                overlay.ProcessMode == Node.ProcessModeEnum.Inherit,
                "reopening retains the exact panel and shell and creates only a fresh native stack entry");
            Check(ReferenceEquals(retainedSettingsPage, typeof(WorkspaceShell).GetField("_settingsPage", flags)!.GetValue(shell)) &&
                ReferenceEquals(fontChoice, typeof(WorkspaceShell).GetField("_fontChoice", flags)!.GetValue(shell)) &&
                shell.FindChildren("SettingsPage", "Control", true, false).Count == 1,
                "close and reopen retain the same lazily created Settings page and controls without duplicates");
            ((Button)typeof(WorkspaceShell).GetField("_currentPrediction",flags)!.GetValue(shell)!).EmitSignal(Button.SignalName.Pressed);
            var restoredSession=reopenedPanel.SuspendSession();
            Check(restoredSession!=null && ReferenceEquals(restoredSession.Reachability,retainedSession!.Reachability) &&
                restoredSession.Selected.SequenceEqual(retainedSession.Selected) && restoredSession.Projection.SelectedPlan==retainedSession.Projection.SelectedPlan &&
                restoredSession.IncludeDriftwood==retainedSession.IncludeDriftwood && restoredSession.IncludeEnchantments==retainedSession.IncludeEnchantments,
                "Reopening reuses controls while preserving the same query, goals and witness");
            // This fixture's live run differs from the captured Crystal run.
            // A changed fingerprint must retain the plan read-only, not erase it.
            Check(restoredSession!.Stale && ((Button)typeof(CrystalSphereAssistantPanel).GetField("_use",flags)!.GetValue(reopenedPanel)!).Disabled,
                "Changed live context keeps the old plan visible but disables stale execution");
            reopenedPanel.Refresh();
            Check(reopenedPanel.SuspendSession()?.Projection.SelectedPlan==restoredSession.Projection.SelectedPlan,
                "A failed new capture does not destroy the retained plan");
            typeof(RunPredictionOverlay).GetMethod("Close",flags)!.Invoke(overlay,null);
            Check(overlay.Matches(run,runtime,stack,null), "unchanged run and room retain their surface");
            run.PushRoom(new MegaCrit.Sts2.Core.Rooms.EventRoom(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>()));
            try { Check(!overlay.Matches(run,runtime,stack,null), "a new room invalidates the retained surface before showing it"); }
            finally { run.PopCurrentRoom(); }
            Check(!overlay.Matches(crystalRun,runtime,stack,null), "a new run cannot reuse old answers or UI binding");
            // This intentional failure writes a real process-wide issue notice.
            // Keep it after the normal no-notice layout checks: those assert the
            // authored canvas, while notices use the shell's outer scroll area.
            bool priorOrdinaryTestMode = MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn;
            try
            {
                MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
                try
                {
                    roster.Clear(); overlay.Open();
                    Check(!(bool)typeof(AnalysisPage).GetField("_wbHasResult",flags)!.GetValue(ordinaryPage)! &&
                        !(bool)typeof(WorkspaceShell).GetField("_runPredictionLoaded",flags)!.GetValue(shell)!,
                        "a failed current roster capture clears visible old answers and retires the cached binding");
                }
                finally { CloseOrdinary(); roster.Clear(); roster.AddRange(players); overlay.Open(); }
                Check(ordinaryPage.LastDocument is not null && ordinaryPage.LastRequest is { } recoveredOrdinaryRequest &&
                    recoveredOrdinaryRequest.Ascension == originalAscension && recoveredOrdinaryRequest.OriginalSeed == run.Rng.StringSeed &&
                    (bool)typeof(AnalysisPage).GetField("_wbHasResult",flags)!.GetValue(ordinaryPage)!,
                    "restored live inputs can recover a current ordinary prediction after capture failure");
                CloseOrdinary();
            }
            finally { MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = priorOrdinaryTestMode; }
            overlay.Retire();
            Check(!overlay.Matches(run,runtime,stack,null) && overlay.IsQueuedForDeletion(), "retirement releases the room-owned surface");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(retainedSettingsPage) && !GodotObject.IsInstanceValid(fontChoice),
                "retirement frees the lazily created Settings subtree and its controls");
            GD.Print("CRYSTAL_SESSION_REOPEN_PASS controlsRetained=true;nativeEntryReplaced=true;closeCancels=true;queryRetained=true;selectedPlanRetained=true;changedContextReadOnly=true;failedRefreshRetained=true;newRoomAndRunInvalidate=true");
            Check(Signature() == before, "prediction did not mutate run RNG, deck or unlocks");
        }
        finally { stack.Clear(); host.RemoveChild(stack); stack.QueueFree(); }
        GD.Print($"RUN_ENTRY_PASS checks={checks};customLobby=true;contextReadOnly=true;nativeOverlayLifecycle=true");
        }
        finally
        {
            crystalPreferences.SetCrystalSphereOptions(originalCrystalOptions.Item1, originalCrystalOptions.Item2,
                originalCrystalOptions.Item3, originalCrystalOptions.Item4);
            CrystalSphereMaskAppearance.SetEnabled(originalMaskAppearance);
        }
    }

    private static async Task MeasureMenuShellComponents(Node host, string output, ModRuntimeSnapshot runtime, Action<bool, string> check)
    {
        // This host has initialized game metadata already. Measure actual Shell
        // initialization/reopen components, not a cold game or native menu Push.
        var canvas = new Control { Size = new Vector2(1600, 900) };
        host.AddChild(canvas);
        var shell = new WorkspaceShell();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "responsiveness-menu-state", Guid.NewGuid().ToString("N")));
        double initializeMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        canvas.AddChild(shell); shell.Open();
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        double firstDrawMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        check(shell.IsVisibleInTree() && shell.CloseButton.IsVisibleInTree() && !shell.CloseButton.Disabled,
            "first Shell draw exposes its working close action");
        shell.CleanupForTopLevelClose();
        started = System.Diagnostics.Stopwatch.GetTimestamp();
        shell.Open();
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        double reopenDrawMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        check(shell.IsVisibleInTree() && !shell.CloseButton.Disabled, "same Shell reopens with controls available");
        shell.CleanupForTopLevelClose(); host.RemoveChild(canvas); canvas.QueueFree();
        GD.Print(FormattableString.Invariant($"PANEL_COMPONENT_TIMING kind=main-menu-shell;metadataAlreadyInitialized=true;initializeMs={initializeMs:F3};firstDrawMs={firstDrawMs:F3};reopenDrawMs={reopenDrawMs:F3};nativeMenuPushMeasured=false"));
    }

    private static void CheckAuthorityCandidateReuse(Action<bool, string> check)
    {
        string path = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "authority", "runtime_authority_baseline_candidate.json");
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        DateTime originalWriteTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        var snapshot = RuntimeAuthoritySnapshot.Unavailable("candidate-export-fixture", "FixtureIncomplete") with
        {
            UnlockAuthority = [new("Cards", ["A"], false, "fixture")]
        };
        try
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            check(RuntimeAuthorityBaselineCandidateWriter.TryWrite(snapshot) == path, "candidate export writes the initial observation");
            double initialMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            byte[] first = File.ReadAllBytes(path); DateTime firstWrite = File.GetLastWriteTimeUtc(path);
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 20; i++)
                RuntimeAuthorityBaselineCandidateWriter.TryWrite(snapshot with
                {
                    CapturedAtUtc = snapshot.CapturedAtUtc.AddSeconds(i + 1),
                    CurrentUnlockState = [new("Cards", ["A"], true, "current player state is not in this export")]
                });
            double unchangedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            check(File.ReadAllBytes(path).SequenceEqual(first) && File.GetLastWriteTimeUtc(path) == firstWrite,
                "unchanged candidate export retains bytes and actual export timestamp across twenty captures");
            var changed = snapshot with { UnlockAuthority = [new("Cards", ["A", "B"], false, "fixture")] };
            check(RuntimeAuthorityBaselineCandidateWriter.TryWrite(changed) == path && !File.ReadAllBytes(path).SequenceEqual(first),
                "incomplete equal hashes do not hide changed exported unlock facts");
            first = File.ReadAllBytes(path);
            var issueChanged = changed with { CaptureIssues = ["ChangedFixtureIssue"] };
            check(RuntimeAuthorityBaselineCandidateWriter.TryWrite(issueChanged) == path && !File.ReadAllBytes(path).SequenceEqual(first),
                "changed capture issue is exported even when semantic hashes match");
            first = File.ReadAllBytes(path);
            File.Delete(path);
            check(RuntimeAuthorityBaselineCandidateWriter.TryWrite(issueChanged) == path && File.ReadAllBytes(path).SequenceEqual(first),
                "deleted candidate export is recreated");
            if (OperatingSystem.IsWindows())
            {
                var retry = issueChanged with { GameVersion = "candidate-export-retry" };
                using (File.Open(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
                    check(RuntimeAuthorityBaselineCandidateWriter.TryWrite(retry) == string.Empty, "failed export is reported");
                check(RuntimeAuthorityBaselineCandidateWriter.TryWrite(retry) == path && !File.ReadAllBytes(path).SequenceEqual(first),
                    "failed export is retried after the file is released");
            }
            GD.Print(FormattableString.Invariant($"AUTHORITY_EXPORT_REUSE_PASS unchangedCaptures=20;extraWrites=0;initialMs={initialMs:F3};unchangedTotalMs={unchangedMs:F3};incompleteFactsAndIssuesInvalidate=true;deletedFileRestored=true"));
        }
        finally
        {
            if (original is null) File.Delete(path);
            else { File.WriteAllBytes(path, original); File.SetLastWriteTimeUtc(path, originalWriteTime); }
        }
    }

    public class EntryNetProxy : DispatchProxy
    {
        public NetGameType Type = NetGameType.Host;
        public bool Connected = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_Type" => Type, "get_IsConnected" => Connected, "get_NetId" => 1UL,
            _ => method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
        };
    }
}

// Keep the native Push/Remove implementation; only replace its main-game scene lookups.
internal partial class EntryOverlayStackFixture : NOverlayStack
{
    public override void _EnterTree() { }
    public override void _Ready()
    {
        var backstop = new Control { Name = "OverlayBackstop" }; AddChild(backstop);
        typeof(NOverlayStack).GetField("_backstop",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(this,backstop);
    }
    public override void _ExitTree() => Clear();
}
internal partial class EntryUnderlyingOverlay : Control, IOverlayScreen
{
    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => null;
    public void AfterOverlayOpened() { }
    public void AfterOverlayClosed() => QueueFree();
    public void AfterOverlayShown() => Show();
    public void AfterOverlayHidden() => Hide();
}
