using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static NGame? _neowFixtureGame;
    private static int _neowSeenCalls;
    private static readonly HashSet<Node> _neowStaticSceneNodes = [];
    private static bool NeowSkipGameStartup(NGame __instance) => !ReferenceEquals(__instance, _neowFixtureGame);
    private static bool NeowSkipStaticSceneStartup(Node __instance) => !_neowStaticSceneNodes.Contains(__instance);
    private static void NeowRecordCardSeen() => _neowSeenCalls++;

    // Uses the existing PCK/ModelDb/MockSave UI host. The native button scenes,
    // focus/unfocus methods, capture, worker projection and renderer are real.
    // The four original Neow options form a controlled current page, not a
    // claim that the native random opening rolls these four together.
    private static async Task RunNeowOperationChecks(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        void Set(object obj, string name, object? value)
        {
            var property = obj.GetType().GetProperty(name, members);
            if (property != null) { property.SetValue(obj, value); return; }
            for (Type? type = obj.GetType(); type != null; type = type.BaseType)
                if (type.GetField(name, members | BindingFlags.DeclaredOnly) is { } field)
                { field.SetValue(obj, value); return; }
            throw new MissingFieldException(obj.GetType().Name, name);
        }
        object? Field(object obj, string name) => PredictorOperationCapture.ReadNullable(obj, name);
        int checks = 0;
        void Check(bool value, string reason)
        { checks++; if (!value) throw new InvalidOperationException("NeowOperationUi: " + reason); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            for (int i = 0; i < 900 && !ready(); i++) await Frames(1);
            Check(ready(), reason);
        }

        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(NEventOptionButton).Assembly);
        Check(!RunManager.Instance.IsInProgress, "isolated host has no existing run to replace");
        Directory.CreateDirectory(output);
        bool layoutOnly = OS.GetCmdlineUserArgs().Contains("--neow-layout-only");
        var receipt = Path.Combine(output, "neow-operation-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        var oldGame = NGame.Instance;
        var oldLocal = LocalContext.NetId;
        var oldRuntime = ModRuntime.Snapshot;
        bool oldTest = TestMode.IsOn;
        string oldLocale = LocManager.Instance.Language;
        var languagePreferences = new RolltheSpire2.Ui.Workspace.SearchWorkspace(OS.GetUserDataDir(), false);
        string oldUiLanguage = languagePreferences.Preferences.LanguageOverride;
        var fixture = new Harmony("rt2.neow.operation.ui.fixture");
        var product = new Harmony("rt2.neow.operation.ui.product");
        bool addedDebugAction = !InputMap.HasAction("debug_hide_event_ui");
        if (addedDebugAction) InputMap.AddAction("debug_hide_event_ui");
        var nodes = new List<Node>();
        NGame? game = null;
        SubViewport? nativeViewport = null;
        TextureRect? nativeDisplay = null;
        Neow? neow = null;
        try
        {
            TestMode.IsOn = true;
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, null);
            typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!.SetValue(null, runtime);
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var run = RunState.CreateForTest([player], seed: "000000067C0S", ascensionLevel: 0);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), false);
            RunManager.Instance.GenerateRooms();
            LocalContext.NetId = player.NetId;
            var room = new EventRoom(ModelDb.Event<Neow>());
            run.PushRoom(room);
            RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<Neow>());
            neow = (Neow)RunManager.Instance.EventSynchronizer.GetLocalEvent();
            await Until(() => neow.CurrentOptions.Count > 0, "original Neow initialization completes");
            string[] sources = ["SCROLL_BOXES", "KALEIDOSCOPE", "LARGE_CAPSULE", "NEOWS_BONES", "LEAD_PAPERWEIGHT"];
            var allOptions = neow.AllPossibleOptions.ToArray();
            var offers = sources.Take(4).Select(key => allOptions.Single(o => o.Relic?.Id.Entry == key)).ToList();
            Set(neow, "_currentOptions", offers);
            TestMode.IsOn = false;
            LocManager.Instance.SetLanguage("zhs");
            languagePreferences.SetLanguageOverride("zh");
            root.GetWindow().Size = new(1920, 1080);
            RenderingServer.SetDefaultClearColor(new Color("18202b"));

            // Same detached singleton wiring used by the native map host. Only
            // NGame's unrelated startup/audio is bypassed, not button lifecycle.
            game = new NGame { Name = "NeowNativeFixture", MouseFilter = Control.MouseFilterEnum.Ignore };
            _neowFixtureGame = game;
            foreach (string method in new[] { "_EnterTree", "_Ready", "_ExitTree", "_Notification", "_Input" })
                fixture.Patch(AccessTools.Method(typeof(NGame), method),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipGameStartup)));
            foreach (var sfx in typeof(MegaCrit.Sts2.Core.Commands.SfxCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name is "Play" or "PlayLoop" or "StopLoop"))
                fixture.Patch(sfx, prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            fixture.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.MarkCardAsSeen)),
                postfix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowRecordCardSeen)));
            _neowSeenCalls = 0;
            var rootScene = new NSceneContainer();
            var runNode = new NRun();
            var global = new NGlobalUi();
            var input = new NInputManager();
            var controller = new NControllerManager();
            var hotkeys = new NHotkeyManager();
            var roomScenes = new NSceneContainer();
            var eventNode = new NEventRoom();
            var layoutScenes = new NSceneContainer();
            var layout = PreloadManager.Cache.GetScene(NAncientEventLayout.ancientScenePath).Instantiate<NAncientEventLayout>();
            nodes.AddRange([rootScene, runNode, global, input, controller, hotkeys, roomScenes, eventNode, layoutScenes]);
            Set(input, nameof(NInputManager.ControllerManager), controller);
            Set(game, nameof(NGame.InputManager), input);
            Set(game, nameof(NGame.HotkeyManager), hotkeys);
            Set(rootScene, nameof(NSceneContainer.CurrentScene), runNode);
            Set(game, nameof(NGame.RootSceneContainer), rootScene);
            Set(runNode, nameof(NRun.GlobalUi), global);
            Set(runNode, "_roomContainer", roomScenes);
            Set(roomScenes, nameof(NSceneContainer.CurrentScene), eventNode);
            Set(eventNode, "_event", neow);
            Set(eventNode, "_eventContainer", layoutScenes);
            Set(layoutScenes, nameof(NSceneContainer.CurrentScene), layout);
            // Real scene constraints: centered 1160-wide bottom content area,
            // final native dialogue, then native options. No test-only scaling
            // or relocation of options into the previously empty upper right.
            Set(layout, "_event", neow);
            Set(layout, "_ancientEvent", neow);
            var buttonHost = layout.GetNode<VBoxContainer>("%OptionsContainer");
            var dialogueHost = layout.GetNode<VBoxContainer>("%DialogueContainer");
            var content = layout.GetNode<VBoxContainer>("%Content");
            var contentContainer = layout.GetNode<Control>("%ContentContainer");
            var topBar = PreloadManager.Cache.GetScene("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
            var nameBanner = NAncientNameBanner.Create(neow)!;
            // Preserve every native top-bar visual/anchor. Its unrelated live
            // inventory/startup callbacks need full NRun infrastructure. Scope
            // these bypasses to this exact static visual tree only.
            foreach (Node node in Descendants(topBar).Concat(Descendants(nameBanner))) _neowStaticSceneNodes.Add(node);
            foreach (var type in _neowStaticSceneNodes.SelectMany(n =>
                {
                    var hierarchy = new List<Type>();
                    for (Type? type = n.GetType(); type?.Namespace?.StartsWith("MegaCrit.", StringComparison.Ordinal) == true; type = type.BaseType)
                        hierarchy.Add(type);
                    return hierarchy;
                }).Distinct())
                foreach (string callback in new[] { "_Ready", "_EnterTree", "_ExitTree", "_Process", "_PhysicsProcess", "_Notification", "_Input", "_UnhandledInput" })
                    if (type.Namespace?.StartsWith("MegaCrit.", StringComparison.Ordinal) == true &&
                        type.GetMethod(callback, members | BindingFlags.DeclaredOnly) is { } method)
                        fixture.Patch(method, prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipStaticSceneStartup)));
            Set(global, nameof(NGlobalUi.TopBar), topBar);
            var hoverRoot = new Control { Name = "NativeHoverTips", MouseFilter = Control.MouseFilterEnum.Ignore };
            Set(game, nameof(NGame.HoverTipsContainer), hoverRoot);
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, game);
            // Main-window GetMousePosition polls the Owner's desktop pointer,
            // even after injected input. A native SubViewport owns its injected
            // pointer, letting real GUI input exercise hover without warping
            // the Owner's physical mouse. Display/capture its native render.
            nativeViewport = new SubViewport { Name = "NeowNativeViewport", Size = new(1920, 1080),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = true };
            root.AddChild(nativeViewport);
            // TextureRect display does not provide SubViewportContainer's
            // automatic pointer-entry notification. Forward that lifecycle as
            // well as the actual motion/button events.
            nativeViewport.NotifyMouseEntered();
            nativeDisplay = new TextureRect { Name = "NeowNativeViewportDisplay", Texture = nativeViewport.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
            root.AddChild(nativeDisplay); nativeDisplay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            nativeViewport.AddChild(game); game.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            game.AddChild(layout);
            layout.AddChild(nameBanner);
            // The source's final settled banner styles, bypassing its unrelated
            // introductory text effect/tween while preserving full-rect roots.
            nameBanner.Position = new(48, -80);
            var bannerTitle = nameBanner.GetNode<RichTextLabel>("%Title");
            var bannerEpithet = nameBanner.GetNode<Label>("%Epithet");
            bannerTitle.Text = neow.Title.GetFormattedText(); bannerTitle.Position = Vector2.Zero;
            bannerTitle.HorizontalAlignment = HorizontalAlignment.Left; bannerTitle.VerticalAlignment = VerticalAlignment.Bottom;
            bannerTitle.AddThemeFontSizeOverride("normal_font_size", 54);
            bannerTitle.AddThemeColorOverride("font_outline_color", Colors.Transparent);
            bannerTitle.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
            bannerTitle.AddThemeColorOverride("default_color", new Color("FFF6E2"));
            bannerEpithet.Text = neow.Epithet.GetFormattedText(); bannerEpithet.Position = new(0, 42);
            bannerEpithet.HorizontalAlignment = HorizontalAlignment.Left; bannerEpithet.VerticalAlignment = VerticalAlignment.Bottom;
            bannerEpithet.AddThemeFontSizeOverride("font_size", 18); bannerEpithet.Modulate = new(1, 1, 1, .5f);
            bannerEpithet.AddThemeColorOverride("font_outline_color", Colors.Transparent);
            bannerEpithet.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
            bannerEpithet.AddThemeColorOverride("font_color", new Color("FFF6E2"));
            foreach (var controlNode in Descendants(nameBanner).OfType<Control>()) controlNode.MouseFilter = Control.MouseFilterEnum.Ignore;
            // Stock Godot lacks the game's Spine extension. Use the original
            // background texture and its original scene rect; the live native
            // AncientBgContainer still applies its aspect-ratio transform.
            layout.GetNode<Control>("%AncientBgContainer").AddChild(new TextureRect
            {
                Name = "NativeStaticNeowBackground", Position = new(-330, -49), Size = new(2582, 1221),
                Texture = ResourceLoader.Load<Texture2D>("res://animations/backgrounds/neow_room/neow_bg.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore
            });
            game.AddChild(topBar); game.AddChild(hoverRoot);
            foreach (var controlNode in Descendants(topBar).OfType<Control>()) controlNode.MouseFilter = Control.MouseFilterEnum.Ignore;
            hoverRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var dialogue = neow.DialogueSet.GetValidDialogues(player.Character.Id, 0, 0, true).First();
            layout.SetDialogue([dialogue.Lines.Last()]);
            var finalDialogue = dialogueHost.GetChild<NAncientDialogueLine>(0);
            finalDialogue.SetTransparency(1); finalDialogue.SetSpeakerIconVisible();
            layout.GetNode<Control>("%DialogueHitbox").Hide();
            layout.GetNode<Control>("%FakeNextButtonContainer").Hide();
            contentContainer.Size = new(contentContainer.Size.X, (float)Field(layout, "_originalContentContainerHeight")!);
            PredictorOperationCapture.Install(product);
            PredictorOperationEventCapture.Install(product);
            NeowOperationPredictionPatch.Install(product);
            foreach (string method in new[] { "OnFocus", "OnUnfocus", "OnRelease", "_ExitTree" })
            {
                var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(NEventOptionButton), method));
                Check(patches != null && patches.Postfixes.Any(p => p.owner == product.Id) &&
                    !patches.Prefixes.Any(p => p.owner == product.Id), "product observes " + method + " only after original");
            }
            var buttons = offers.Select((option, index) => NEventOptionButton.Create(neow, option, index)).ToArray();
            foreach (var button in buttons) { buttonHost.AddChild(button); button.EnableButton(); }
            async Task SettleNativeLayout()
            {
                dialogueHost.ResetSize(); buttonHost.ResetSize(); content.ResetSize();
                await Frames(6);
                // Exact final-line placement from NAncientEventLayout:
                // SetDialogueLineAndAnimate, minus the one-second tween.
                content.Position = new(content.Position.X, contentContainer.Size.Y -
                    (finalDialogue.Position.Y + finalDialogue.Size.Y + buttonHost.Size.Y + 10));
                await Frames(3);
            }
            async Task ReplaceFirstOffer(string key)
            {
                if (offers[0].Relic?.Id.Entry == key) return;
                // Swap one actual authored Neow option into the controlled
                // current page, preserving the native four-button geometry.
                buttonHost.RemoveChild(buttons[0]); buttons[0].Free();
                var offered = allOptions.SingleOrDefault(option => option.Relic?.Id.Entry == key);
                if (offered == null && key is "CALLING_BELL" or "ASTROLABE" or "SEA_GLASS" or "TOY_BOX" or "ALCHEMICAL_COFFER" or "ORRERY")
                {
                    // These belong to other native Ancient offer pools. Mount
                    // an explicit original FromRelic/Obtain option on this
                    // controlled page; do not claim Neow naturally rolls it.
                    var relic = ModelDb.GetById<RelicModel>(new ModelId("RELIC", key)).ToMutable();
                    // Obtain the fully authored relic from Orobas's original
                    // native option factory, including its required character.
                    if (relic is SeaGlass)
                    {
                        var orobas = (Orobas)ModelDb.Event<Orobas>().ToMutable();
                        var glassOptions = (IEnumerable<EventOption>)AccessTools.Property(typeof(Orobas), "SeaGlassOptions").GetValue(orobas)!;
                        relic = glassOptions.Single(option => option.Relic is SeaGlass glass &&
                            glass.CharacterId == ModelDb.Character<Silent>().Id).Relic!;
                    }
                    offered = EventOption.FromRelic(relic, neow,
                        () => MegaCrit.Sts2.Core.Commands.RelicCmd.Obtain(relic, player), "INITIAL");
                }
                offers[0] = offered ?? throw new InvalidOperationException("Native fixture offer missing:" + key);
                buttons[0] = NEventOptionButton.Create(neow, offers[0], 0);
                buttonHost.AddChild(buttons[0]); buttonHost.MoveChild(buttons[0], 0); buttons[0].EnableButton();
                await SettleNativeLayout();
            }
            await SettleNativeLayout();
            if (OS.GetCmdlineUserArgs().Contains("--operation-surfaces-only"))
            {
                if (!OS.GetCmdlineUserArgs().Contains("--operation-rf-coexistence"))
                {
                    RunOperationFreshnessChecks(run, runtime.Detection.NormalizedVersion, game, output);
                    await ReplaceFirstOffer("PRECARIOUS_SHEARS");
                    await RunPrecariousShearsOperationChecks(root, output, runtime, game, run, buttons[0], hoverRoot, Signature, Check);
                    await ReplaceFirstOffer("SCROLL_BOXES");
                }
                File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"not-run\",\"reason\":\"operation-surfaces-only\"}");
                await RunAdditionalOperationChecks(root, output, runtime, game, nativeViewport, hoverRoot, run, player,
                    eventNode, layoutScenes, roomScenes, global, topBar, fixture, Set);
                return;
            }
            string Signature() => JsonSerializer.Serialize(new
            {
                players = run.Players.Select(p => p.ToSerializable()), rng = run.Rng.ToSerializable(),
                bags = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
                personal = PredictorLiveRunCapture.Bag(player.RelicGrabBag), neowRng = neow.Rng.ToSerializable(),
                options = offers.Select(o => new { o.WasChosen, o.IsLocked, key = o.Relic?.Id.ToString() })
            });
            NeowOperationHoverController Controller() => hoverRoot.GetNode<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
            NeowOperationOverlay? View() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName)
                ?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
            void Hover(NEventOptionButton button) => button.EmitSignal(Control.SignalName.MouseEntered);
            void Unhover(NEventOptionButton button) => button.EmitSignal(Control.SignalName.MouseExited);
            async Task MovePointer(Vector2 position, int frames = 3)
            {
                nativeViewport.PushInput(new InputEventMouseMotion
                { Position = position, GlobalPosition = position }, true);
                await Frames(frames);
            }
            async Task LeavePointer()
            {
                await MovePointer(new(4, nativeViewport.GetVisibleRect().Size.Y - 4), 0);
                Check(View() == null && Field(Controller(), "_owner") == null,
                    "original native unfocus clears prediction immediately");
                await Frames(3);
            }
            async Task Screenshot(string name)
            {
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                nativeViewport.GetTexture().GetImage().SavePng(Path.Combine(output, name));
            }
            var layoutEvidence = new List<object>();
            async Task RecordNaturalLayout(NeowOperationOverlay view, NEventOptionButton owner,
                string key, int caseIndex, string language, Vector2I viewportSize)
            {
                await Frames(6);
                object Rect(Rect2 rect) => new { x = rect.Position.X, y = rect.Position.Y,
                    width = rect.Size.X, height = rect.Size.Y };
                var panels = (IEnumerable<(MarginContainer Shell, VBoxContainer Content)>)Field(view, "_summaryPanels")!;
                bool allContentFits = true, allContentBoxesAndScrollFit = true, allTailsVisible = true;
                var panelEvidence = panels.Select(panel =>
                {
                    var scroll = Descendants(panel.Shell).OfType<ScrollContainer>().Single();
                    var bar = scroll.GetVScrollBar();
                    var clip = scroll.GetGlobalRect().Intersection(view.GetViewportRect());
                    if (bar.IsVisibleInTree()) clip.Size = new(Math.Max(0, clip.Size.X - bar.Size.X), clip.Size.Y);
                    for (Node? ancestor = scroll.GetParent(); ancestor is Control control; ancestor = ancestor.GetParent())
                        if (control.ClipContents) clip = clip.Intersection(control.GetGlobalRect());
                    bool fits = bar.MaxValue <= bar.Page + .5 && clip.Grow(.5f).Encloses(panel.Content.GetGlobalRect());
                    bool contentBoxAndScrollFit = fits;
                    allContentBoxesAndScrollFit &= contentBoxAndScrollFit;
                    var text = Descendants(panel.Content).OfType<Label>().Where(label => label.IsVisibleInTree() &&
                        !string.IsNullOrEmpty(label.Text)).ToArray();
                    var textRects = text.Select(label =>
                    {
                        var rect = label.GetGlobalRect();
                        float height = label.GetMinimumSize().Y;
                        float offset = label.VerticalAlignment switch
                        {
                            VerticalAlignment.Center => Math.Max(0, (rect.Size.Y - height) / 2),
                            VerticalAlignment.Bottom => Math.Max(0, rect.Size.Y - height), _ => 0
                        };
                        return new Rect2(rect.Position + new Vector2(0, offset), new(rect.Size.X, height));
                    }).ToArray();
                    fits &= textRects.All(rect => clip.Grow(.5f).Encloses(rect));
                    bool tailVisible = textRects.Length == 0 || clip.Grow(.5f).Encloses(textRects[^1]);
                    allContentFits &= fits; allTailsVisible &= tailVisible;
                    return new { name = panel.Shell.Name.ToString(), shell = Rect(panel.Shell.GetGlobalRect()),
                        content = Rect(panel.Content.GetGlobalRect()),
                        contentMinimumHeight = panel.Content.GetCombinedMinimumSize().Y,
                        scroll = Rect(scroll.GetGlobalRect()), visibleClip = Rect(clip),
                        scrollValue = bar.Value, scrollMax = bar.MaxValue, scrollPage = bar.Page,
                        scrollbarVisible = bar.IsVisibleInTree(), scrollMouseFilter = scroll.MouseFilter.ToString(),
                        scrollbarMouseFilter = bar.MouseFilter.ToString(),
                        contentBoxAndScrollFit, allNaturalContentFits = fits, tailVisible,
                        // No scrolling is injected: a passive preview has no reading interaction to test.
                        tailReachableAsDisplayed = tailVisible,
                        text = text.Select((label, index) => new { label.Text, rect = Rect(label.GetGlobalRect()),
                            naturalTextRect = Rect(textRects[index]), visibleTextRect = Rect(textRects[index].Intersection(clip)),
                            fullyVisible = clip.Grow(.5f).Encloses(textRects[index]),
                            lineCount = label.GetLineCount(), visibleLineCount = label.GetVisibleLineCount(),
                            minimumHeight = label.GetMinimumSize().Y, label.VisibleCharacters, label.VisibleRatio,
                            label.ClipText, fontSize = label.GetThemeFontSize("font_size") }).ToArray() };
                }).ToArray();
                string name = "neow-natural-layout-" + viewportSize.X + "x" + viewportSize.Y + "-" + language +
                    "-case" + caseIndex + "-" + key.ToLowerInvariant() +
                    (caseIndex == 3 ? "-scroll_boxes-winged_boots" : caseIndex == 4 ? "-kaleidoscope-large_capsule" : "");
                var preview = (OperationPreview)Field(view, "_preview")!;
                var evidence = new { caseIndex, key, language, nativeLanguage = LocManager.Instance.Language,
                    containedRelics = preview.ContainedRelics.Select(relic => relic.Entry).ToArray(),
                    branchCount = preview.Branches.Length,
                    logicalViewport = new { width = nativeViewport.Size.X, height = nativeViewport.Size.Y },
                    physicalWindow = new { width = root.GetWindow().Size.X, height = root.GetWindow().Size.Y },
                    summaryDesiredSize = new { width = view.SummaryDesiredSize.X, height = view.SummaryDesiredSize.Y },
                    assignedSummary = Rect((Rect2)Field(view, "_summaryBounds")!),
                    actualSummary = view.GetNodeOrNull<Control>("Rt2NeowSummary") is { } summary ? Rect(summary.GetGlobalRect()) : null,
                    nativeOwner = Rect(owner.GetGlobalRect()),
                    nativeTooltips = hoverRoot.GetChildren().OfType<NHoverTipSet>()
                        .SelectMany(tip => tip.GetChildren().OfType<Control>()).Where(control => control.IsVisibleInTree())
                        .Select(control => Rect(control.GetGlobalRect())).ToArray(),
                    summaryPanelCount = panelEvidence.Length,
                    allContentBoxesAndScrollFit, allNaturalContentFits = allContentFits, allTailsVisible,
                    tailReachableAsDisplayed = allTailsVisible,
                    passivePreview = view.MouseFilter == Control.MouseFilterEnum.Ignore,
                    tailReachabilityBasis = "Visible text geometry at the original owner hover; no injected scrolling or altered hover lifetime. Scroll and scrollbar input modes are recorded per panel.",
                    textBoxMeasurement = "Label global bounds and minimum text height, including font line-box spacing; conservative boxes are not shaped glyph ink bounds. A box crossing the clip does not alone prove lost readable glyphs.",
                    panels = panelEvidence, screenshot = name + ".png",
                    currentPage = "Controlled original native four-option page; native Bones pair selected by cloned-cursor search; no fabricated summary content." };
                File.WriteAllText(Path.Combine(output, name + ".json"),
                    JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                await Screenshot(name + ".png");
                layoutEvidence.Add(evidence);
                GD.Print($"NEOW_NATURAL_LAYOUT case={caseIndex};viewport={viewportSize};language={language};fits={allContentFits};tailVisible={allTailsVisible};evidence={name}.json");
            }
            var originalRewards = player.PlayerRng.Rewards.ToSerializable();
            int complexCursorSteps = -1, sameCursorSteps = -1;
            int expectedFaces = 0, renderedFaces = 0, bonesTextCards = 0;
            int passiveCards = 0, summaryCardRows = 0, summaryCardGroups = 0, directFocusSwitches = 0, naturalFitSummaries = 0;
            var geometry = new List<object>();
            var nativeTooltipSeenCalls = new Dictionary<string, int>();
            var layoutScenarios = layoutOnly
                ? new[] { "zh", "en" }.SelectMany(language => new[] { new Vector2I(1680, 1260), new Vector2I(1920, 1200) }
                    .Select(viewport => (Language: language, Viewport: viewport)))
                : new[] { new Vector2I(1920, 1080), new Vector2I(1680, 1260) }.Select(viewport => (Language: "zh", Viewport: viewport));
            string? previousLanguage = null;
            foreach (var (language, viewportSize) in layoutScenarios)
            {
                if (layoutOnly && language != previousLanguage)
                {
                    languagePreferences.SetLanguageOverride(language);
                    LocManager.Instance.SetLanguage(language == "en" ? "eng" : "zhs");
                    bannerTitle.Text = neow.Title.GetFormattedText(); bannerEpithet.Text = neow.Epithet.GetFormattedText();
                    // Native SetDialogue appends child scenes. Its matching ClearDialogue
                    // is required before a locale rebuild, or stale dialogue pushes options out.
                    layout.ClearDialogue();
                    layout.SetDialogue([dialogue.Lines.Last()]);
                    finalDialogue = dialogueHost.GetChild<NAncientDialogueLine>(0);
                    finalDialogue.SetTransparency(1); finalDialogue.SetSpeakerIconVisible();
                    // Recreate native options so their original Ready path formats the new language.
                    for (int index = 0; index < buttons.Length; index++)
                    {
                        buttonHost.RemoveChild(buttons[index]); buttons[index].Free();
                        buttons[index] = NEventOptionButton.Create(neow, offers[index], index);
                        buttonHost.AddChild(buttons[index]); buttons[index].EnableButton();
                    }
                    previousLanguage = language;
                }
                root.GetWindow().Size = viewportSize;
                nativeViewport.Size = viewportSize;
                await SettleNativeLayout();
                if (layoutOnly)
                {
                    Check(dialogueHost.GetChildCount() == 1,
                        "locale refresh keeps exactly one native final dialogue scene");
                    Check(buttons.All(button => nativeViewport.GetVisibleRect().Grow(.5f).Encloses(button.GetGlobalRect())),
                        "natural scenario starts with all native options inside its logical viewport;viewport=" +
                        nativeViewport.GetVisibleRect() + ";options=" + string.Join(";", buttons.Select(button => button.GetGlobalRect())));
                }
                // Case0 is the ordinary card-only control; case2 supplies an ordinary natural summary.
                foreach (int caseIndex in layoutOnly ? new[] { 0, 2, 3, 4 } : Enumerable.Range(0, 12))
                {
                    player.PlayerRng.Rewards.LoadFromSerializable(originalRewards);
                    string replacement = caseIndex switch
                    {
                        11 => "ORRERY", 10 => "ALCHEMICAL_COFFER", 9 => "TOY_BOX", 8 => "SEA_GLASS", 7 => "ASTROLABE",
                        6 => "CALLING_BELL", 5 => "LEAD_PAPERWEIGHT", _ => "SCROLL_BOXES"
                    };
                    await ReplaceFirstOffer(replacement);
                    string key = caseIndex >= 5 ? replacement : sources[Math.Min(caseIndex, 3)];
                    var button = buttons[caseIndex >= 5 ? 0 : Math.Min(caseIndex, 3)];
                    if (caseIndex is 3 or 4)
                    {
                        // Original native eligibility/order and native Shuffle on
                        // cloned cursors only. Commit one selected cursor to this
                        // isolated fixture, then capture/predict/render normally.
                        var eligible = ((IEnumerable<RelicModel>)AccessTools.Method(typeof(NeowsBones), "GetValidRelics")
                            .Invoke(null, [player])!).ToList();
                        string[] pair = caseIndex == 4 ? ["KALEIDOSCOPE", "LARGE_CAPSULE"] : ["SCROLL_BOXES", "WINGED_BOOTS"];
                        Check(pair.All(entry => eligible.Any(r => r.Id.Entry == entry)),
                            "actual native Bones pool contains both controlled fixture candidates");
                        var cursor = new MegaCrit.Sts2.Core.Random.Rng(originalRewards);
                        int reached = -1;
                        for (int step = 0; step < 20000; step++)
                        {
                            var test = new MegaCrit.Sts2.Core.Random.Rng(cursor.ToSerializable());
                            var shuffled = eligible.ToList(); test.Shuffle(shuffled);
                            if (shuffled.Take(2).Select(r => r.Id.Entry).Order().SequenceEqual(pair.Order()))
                            { reached = step; player.PlayerRng.Rewards.LoadFromSerializable(cursor.ToSerializable()); break; }
                            cursor.NextInt();
                        }
                        Check(reached >= 0, "actual native Bones pool reaches controlled pair through a cloned-cursor search");
                        if (caseIndex == 4) complexCursorSteps = reached; else sameCursorSteps = reached;
                    }
                    string before = Signature();
                    Check(neow.CurrentOptions.Count == 4 && buttonHost.GetChildren().OfType<NEventOptionButton>().Count() == 4,
                        key + " controlled native current page retains exactly four original-style options");
                    var phaseTime = System.Diagnostics.Stopwatch.StartNew();
                    var actual = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion);
                    double captureMs = phaseTime.Elapsed.TotalMilliseconds;
                    long id = PredictorLiveRunCapture.Id(button.Option);
                    Check(actual.Scene is PredictorOperationAncientScene ancient && ancient.Offers.Any(o => o.Id == id && o.Relic.Entry == key),
                        key + " exact native option identity captured");
                    phaseTime.Restart();
                    var expected = await Task.Run(() => RelicOperationPreview.Build(actual, id));
                    double projectionMs = phaseTime.Elapsed.TotalMilliseconds;
                    Check(expected.Faults.IsEmpty && expected.Source.Entry == key, key + " actual detached forecast succeeds;faults=" + string.Join(";", expected.Faults.Select(fault => fault.Stage + ":" + fault.Exception)));
                    int seenBeforeFocus = _neowSeenCalls;
                    await MovePointer(button.GetGlobalRect().GetCenter(), 0);
                    int originalTipSeen = _neowSeenCalls;
                    nativeTooltipSeenCalls[key + (caseIndex == 4 ? "-different" : caseIndex == 3 ? "-same" : "") + "-" + viewportSize] = originalTipSeen - seenBeforeFocus;
                    Check(hoverRoot.GetChildren().OfType<NHoverTipSet>().Any(), key + " original native tooltip remains");
                    await Until(() => View() is { Visible: true }, key + " focus publishes real renderer");
                    var view = View()!;
                    if (key == "ASTROLABE")
                    {
                        // Isolate detached card-node creation from enumeration;
                        // this extra renderer belongs only to the controlled host.
                        var measuredView = new NeowOperationOverlay(); hoverRoot.AddChild(measuredView);
                        phaseTime.Restart(); measuredView.SetPreview(expected);
                        double renderMs = phaseTime.Elapsed.TotalMilliseconds;
                        measuredView.Hide(); hoverRoot.RemoveChild(measuredView); measuredView.QueueFree();
                        GD.Print($"OPERATION_TIMING ASTROLABE;viewport={viewportSize};captureMs={captureMs:F2};projectionMs={projectionMs:F2};cardBuildMs={renderMs:F2};modelExecutions={expected.Branches.Sum(b => b.PathsExamined)}");
                    }
                    var rendered = (OperationPreview)Field(view, "_preview")!;
                    Check(rendered.Source == expected.Source && rendered.Branches.Length == expected.Branches.Length &&
                        rendered.ContainedRelics.SequenceEqual(expected.ContainedRelics), key + " view uses actual operation forecast");
                    if (layoutOnly)
                    {
                        if (caseIndex is 3 or 4)
                            Check(rendered.ContainedRelics.Select(relic => relic.Entry).Order().SequenceEqual(
                                (caseIndex == 3 ? new[] { "SCROLL_BOXES", "WINGED_BOOTS" } : new[] { "KALEIDOSCOPE", "LARGE_CAPSULE" }).Order()) &&
                                rendered.Branches.Length == (caseIndex == 3 ? 1 : 2),
                                "natural Bones layout uses the selected native pair and retained pickup outcomes");
                        await RecordNaturalLayout(view, button, key, caseIndex, language, viewportSize);
                        Check(_neowSeenCalls == originalTipSeen && Signature() == before,
                            key + " natural layout capture preserves native run, RNG and seen-card count");
                        await LeavePointer();
                        continue;
                    }
                    if (key == "KALEIDOSCOPE") Check(rendered.Branches.Single().Sections.Sum(s => s.CardGroups.Length) == 2,
                        "Kaleidoscope retains both three-card groups");
                    if (key == "LEAD_PAPERWEIGHT") Check(rendered.Branches.Single().Sections.SelectMany(section => section.CardGroups)
                        .Any(group => group.Cards.Length == 2 && group.MinimumSelect == 0 && group.MaximumSelect == 1 && group.CanSkip),
                        "actual Lead Paperweight offers both colorless cards as two-select-one");
                    if (key == "NEOWS_BONES") Check(rendered.ContainedRelics.Length == 2,
                        "Bones retains actual two relics and the projection's distinct pickup outcomes");
                    if (caseIndex == 4) Check(rendered.ContainedRelics.Select(r => r.Entry).Order()
                        .SequenceEqual(new[] { "KALEIDOSCOPE", "LARGE_CAPSULE" }), "complex Bones view uses actual selected native RNG cursor");
                    if (caseIndex == 3) Check(rendered.Branches.Length == 1, "same-result native Bones pair folds acquisition orders");
                    if (caseIndex == 4)
                    {
                        Check(rendered.Branches.Length == 2, "different-result native Bones pair retains acquisition orders");
                        Check(rendered.Branches.All(branch =>
                            branch.Sections.Where(section => section.Source.Entry == "KALEIDOSCOPE")
                                .SelectMany(section => section.CardGroups).ToArray() is { Length: 2 } groups &&
                            groups.All(group => group.Cards.Length == 3)),
                            "each complex Bones pickup order retains both complete three-card Kaleidoscope groups");
                    }
                    var cards = Descendants(view).OfType<NCard>().ToArray();
                    bool suppressBranchCards = key == "NEOWS_BONES" && rendered.Branches.Length > 1;
                    int ArtworkCount(OperationPreviewSection section, OperationPreviewRow row) =>
                        !row.ShowCards ? 0 : key == "NEOWS_BONES" && section.Source == rendered.Source && row.Action == OperationPreviewAction.AddCard
                            ? row.Cards.Count(card => !rendered.CurseCandidates.Any(shared =>
                                (shared with { Id = 0 }) == (card with { Id = 0 }))) : row.Cards.Length;
                    int faceCount = suppressBranchCards ? 0 : rendered.Branches.Sum(b => b.Sections.Sum(s => s.CardGroups.Sum(g => g.Cards.Length) +
                        s.Rows.Sum(r => ArtworkCount(s, r))));
                    Check(cards.Length == faceCount && cards.All(c => c.IsVisibleInTree()),
                        key + " every projected artwork slot has one visible native NCard; no silent text fallback");
                    expectedFaces += faceCount; renderedFaces += cards.Length;
                    if (key == "NEOWS_BONES") bonesTextCards += rendered.Branches.Sum(b => b.Sections.Sum(s => s.Rows.Sum(r => r.Cards.Length)));
                    if (key is "SCROLL_BOXES" or "KALEIDOSCOPE") Check(cards.Any(c => c.Visible), key + " real native card faces rendered");
                    Check(cards.All(c => c.Model is { RunState: null, CombatState: null }), key + " renderer models detached");
                    Check(cards.All(c => c.MouseFilter == Control.MouseFilterEnum.Ignore), key + " native prediction card faces do not intercept input");
                    var summary = view.GetNodeOrNull<Control>("Rt2NeowSummary");
                    string[] summaryLines = summary == null ? [] : Descendants(summary).OfType<Label>().Select(label => label.Text).ToArray();
                    if (key is "SCROLL_BOXES" or "KALEIDOSCOPE" or "LEAD_PAPERWEIGHT")
                        Check(summary == null && view.SummaryDesiredSize == Vector2.Zero, key + " D policy has no text panel or reserved summary space");
                    if (suppressBranchCards) Check(cards.Length == 0 && summary != null, "non-equivalent Bones branches retain complete text without ambiguous external cards");
                    string CardCaption(PredictorCard card) => new LocString("cards", card.Key.Entry + ".title").GetFormattedText() +
                        (card.UpgradeLevel > 0 ? "+" + (card.UpgradeLevel > 1 ? card.UpgradeLevel.ToString() : "") : "") +
                        (card.Enchantment is { } enchantment ? " · " + new LocString("enchantments", enchantment.Entry + ".title").GetFormattedText() +
                            " " + card.EnchantmentAmount : "");
                    if (key == "NEOWS_BONES")
                    {
                        var sharedCurse = summary?.FindChild("Rt2BonesSharedCurse", true, false) as Label;
                        Check((sharedCurse != null) == !rendered.CurseCandidates.IsEmpty,
                            "Bones shows a root curse only when projection proves a shared result");
                        if (sharedCurse != null)
                            Check(!rendered.CursesPossible && !sharedCurse.Text.Contains("可能", StringComparison.Ordinal),
                                "shared Bones curse is certain rather than a union of branches");
                        for (int branchIndex = 0; branchIndex < rendered.Branches.Length; branchIndex++)
                        {
                            var branch = rendered.Branches[branchIndex];
                            var branchNode = summary?.FindChild("Rt2OperationBranch_" + branchIndex, true, false);
                            var lines = branchNode == null ? [] : Descendants(branchNode).OfType<Label>().Select(l => l.Text).ToArray();
                            foreach (var row in branch.Sections.Where(s => s.Source == rendered.Source)
                                .SelectMany(s => s.Rows).Where(r => r.Action == OperationPreviewAction.AddCard))
                                foreach (var curse in row.Cards.Where(c => c.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Curse))
                                {
                                    bool shared = rendered.CurseCandidates.Any(c => (c with { Id = 0 }) == (curse with { Id = 0 }));
                                    string caption = CardCaption(curse);
                                    Check(shared ? !lines.Any(l => l.Contains(caption, StringComparison.Ordinal)) :
                                        lines.Any(l => l.Contains(caption, StringComparison.Ordinal) &&
                                            l.StartsWith(row.Possible ? "可能·" : "获得", StringComparison.Ordinal)),
                                        "Bones curse belongs to its own branch with exact certainty, or appears once at root");
                                }
                        }
                    }
                    foreach (var section in rendered.Branches.SelectMany(branch => branch.Sections))
                    {
                        if (OperationDisplayPolicy.Resolve(rendered, section).Text == OperationTextMode.None) continue;
                        foreach (var row in section.Rows.Where(row => !row.Cards.IsEmpty))
                        {
                            Check(row.Cards.All(card => summaryLines.Any(line => line.Contains(CardCaption(card), StringComparison.Ordinal))),
                                key + " independent summary includes every random row card name, upgrade and enchantment");
                            summaryCardRows++;
                        }
                        for (int groupIndex = 0; groupIndex < section.CardGroups.Length; groupIndex++)
                        {
                            var group = section.CardGroups[groupIndex];
                            if (group.Cards.IsEmpty) continue;
                            Check(group.Cards.All(card => summaryLines.Any(line => line.Contains(CardCaption(card), StringComparison.Ordinal))),
                                key + " text policy retains every candidate card name in each forecast group");
                            summaryCardGroups++;
                        }
                    }
                    Check(!summaryLines.Any(line => line.Contains("选一", StringComparison.Ordinal) || line.Contains("全部获得", StringComparison.Ordinal) ||
                        line.Contains("滚轮", StringComparison.Ordinal) || line.Contains("悬停", StringComparison.Ordinal)),
                        key + " summary contains outcomes without selection or browsing teaching");
                    await Frames(4);
                    void Geometry(string phase)
                    {
                        var regions = view.VisibleRegions.ToArray();
                        var viewport = view.GetViewportRect();
                        var topStrip = topBar.GetNode<Control>("BgImage").GetGlobalRect();
                        var tips = hoverRoot.GetChildren().OfType<NHoverTipSet>().SelectMany(t => t.GetChildren().OfType<Control>())
                            .Where(c => c.IsVisibleInTree() && c.Size.X > 0 && c.Size.Y > 0).Select(c => c.GetGlobalRect());
                        var titleBox = bannerTitle.GetGlobalRect();
                        var titleText = new Rect2(new(titleBox.Position.X, titleBox.End.Y - bannerTitle.GetContentHeight()),
                            new(bannerTitle.GetContentWidth(), bannerTitle.GetContentHeight()));
                        var epithetBox = bannerEpithet.GetGlobalRect();
                        var epithetText = new Rect2(new(epithetBox.Position.X, epithetBox.End.Y - bannerEpithet.GetMinimumSize().Y),
                            bannerEpithet.GetMinimumSize());
                        Check(regions.Length > 0 && regions.All(r => r.Size.X > 0 && r.Size.Y > 0 && viewport.Grow(.1f).Encloses(r)),
                            key + " " + phase + " actual prediction surfaces fit " + viewportSize + ";regions=" + string.Join(";", regions) +
                            ";summaryDesired=" + view.SummaryDesiredSize + ";assignedCards=" + Field(view, "_cardsBounds"));
                        Check(!regions.Any(r => r.Intersects(button.GetGlobalRect())),
                            key + " " + phase + " prediction protects its current native owner");
                        if (summary != null && key != "NEOWS_BONES")
                            Check(tips.All(tip => !summary.GetGlobalRect().Intersects(tip)) &&
                                hoverRoot.GetChildren().OfType<NHoverTipSet>()
                                    .Select(t => t.GetNode<Control>("textHoverTipContainer"))
                                    .Where(t => t.IsVisibleInTree() && t.Size.X > 0 && t.Size.Y > 0)
                                    .All(t => summary.GetGlobalRect().End.Y + 10 <= t.GetGlobalRect().Position.Y),
                                key + " ordinary summary sits above native explanation without overlap");
                        Check(buttons.All(b => viewport.Encloses(b.GetGlobalRect())), key + " unscaled native options remain inside viewport");
                        if (key == "NEOWS_BONES" && summary != null)
                            Check(summary.Size.X + .1f >= view.SummaryDesiredSize.X &&
                                summary.GetGlobalRect().End.Y <= button.GetGlobalRect().Position.Y,
                                "Bones C4 fits its complete text width above its actual hovered option;actual=" + summary.GetGlobalRect() +
                                ";desired=" + view.SummaryDesiredSize + ";owner=" + button.GetGlobalRect());
                        geometry.Add(new { key, caseIndex, viewport = viewportSize.ToString(), phase,
                            surfaces = regions.Select(r => r.ToString()).ToArray(), nativeDialogue = finalDialogue.GetGlobalRect().ToString(),
                            nativeName = new[] { titleText.ToString(), epithetText.ToString() },
                            stackCaptions = Descendants(view).OfType<Label>()
                                .Where(label => label.GetParent().Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal))
                                .Select(label => new { text = label.Text, rect = label.GetGlobalRect().ToString(),
                                    stacked = label.GetParent().GetMeta("rt2_stacked").AsBool(),
                                    label.VisibleCharacters, label.VisibleRatio, label.ClipText,
                                    font = label.GetThemeFont("font").ResourcePath,
                                    fontSize = label.GetThemeFontSize("font_size") }).ToArray(),
                            nativeOptions = buttons.Select(b => b.GetGlobalRect().ToString()).ToArray() });
                    }
                    await Screenshot("neow-" + key.ToLowerInvariant() + "-geometry-latest.png");
                    Geometry("default");
                    Check(_neowSeenCalls == originalTipSeen, key + " prediction/renderer adds no MarkCardAsSeen calls beyond native tooltip");
                    Check(Signature() == before, key + " hover leaves actual run, offers and all copied RNG unchanged");
                    await Screenshot("neow-" + key.ToLowerInvariant() +
                        (caseIndex == 4 ? "-different" : caseIndex == 3 ? "-same" : "") + "-" + viewportSize.X + "x" + viewportSize.Y + "-zh.png");
                    var summaryScroll = Descendants(view).OfType<ScrollContainer>().SingleOrDefault(c => c.Name == "Rt2NeowSummaryScroll");
                    var assignedSummary = (Rect2)Field(view, "_summaryBounds")!;
                    if (summaryScroll != null && assignedSummary.Size.Y + .01f >= view.SummaryDesiredSize.Y)
                    {
                        var bar = summaryScroll.GetVScrollBar();
                        Check(!bar.IsVisibleInTree(), key + " naturally fitting summary has no rounding-only scrollbar;max=" +
                            bar.MaxValue + ";page=" + bar.Page + ";desired=" + view.SummaryDesiredSize + ";assigned=" + assignedSummary);
                        naturalFitSummaries++;
                    }
                    var stacks = Descendants(view).OfType<Control>().Where(c => c.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                    Check(stacks.Length > 0 || faceCount == 0, key + " grouped native card stacks exist");
                    var expectedStacks = rendered.Branches.SelectMany(branch => NeowOperationOverlay.DisplaySections(rendered, branch)).SelectMany(section =>
                        section.Rows.Where(row => !suppressBranchCards && ArtworkCount(section, row) > 0)
                            .Select(row => (Source: section.Source.Entry,
                                Stacked: OperationDisplayPolicy.Resolve(rendered, section).Cards is OperationCardLayout.Stack or OperationCardLayout.Groups,
                                Count: ArtworkCount(section, row)))
                            .Concat(section.CardGroups.Where(group => !suppressBranchCards && !group.Cards.IsEmpty).Select(group =>
                                (Source: section.Source.Entry,
                                    Stacked: OperationDisplayPolicy.Resolve(rendered, section).Cards is OperationCardLayout.Stack or OperationCardLayout.Groups or OperationCardLayout.Branches,
                                    Count: group.Cards.Length))))
                        .ToArray();
                    if (key is "SCROLL_BOXES" or "KALEIDOSCOPE")
                        Check(stacks.SelectMany(stack => stack.GetChildren().OfType<Label>()).All(label => string.IsNullOrEmpty(label.Text)),
                            key + " ordinary grouped card art has no group number or caption");
                    Check(stacks.Length == expectedStacks.Length, key + " every artwork row/group retains one native card host");
                    foreach (var stack in stacks)
                    {
                        var nativeMembers = Descendants(stack).OfType<NCard>().ToArray();
                        bool stacked = stack.GetMeta("rt2_stacked").AsBool();
                        var expectedStack = expectedStacks[int.Parse(stack.Name.ToString().Split('_')[1])];
                        Check(stacked == expectedStack.Stacked && nativeMembers.Length == expectedStack.Count,
                            key + " " + expectedStack.Source + " keeps its authored plain/grouped artwork layout");
                        var tiles = nativeMembers.Select(card => card.GetParent<Control>()).ToArray();
                        Check(tiles.All(tile => Math.Abs(tile.Scale.X - view.CardDisplayScale) < .001f),
                            key + " every native face uses viewport-level scale regardless of owner position");
                        if (stacked)
                        {
                            bool earliestFirst = stack.GetMeta("rt2_earliest_first").AsBool();
                            Check(nativeMembers.Zip(nativeMembers.Skip(1)).All(pair => earliestFirst
                                    ? pair.Second.GlobalPosition.X > pair.First.GlobalPosition.X && pair.Second.GlobalPosition.Y < pair.First.GlobalPosition.Y
                                    : pair.Second.GlobalPosition.X < pair.First.GlobalPosition.X && pair.Second.GlobalPosition.Y > pair.First.GlobalPosition.Y),
                                key + " native acquisition order or explicitly authored earliest-first candidates retain their correct diagonal");
                            int front = earliestFirst ? 0 : tiles.Length - 1, back = earliestFirst ? tiles.Length - 1 : 0;
                            Check(tiles.Length == 1 || tiles[back].Modulate.R < tiles[front].Modulate.R &&
                                Math.Abs(tiles[front].Modulate.R - 1) < .001f && tiles[front].ZIndex > tiles[back].ZIndex,
                                key + " stacked back members are darker and actual front remains bright and above the rear cards");
                        }
                        else
                        {
                            Check(nativeMembers.Zip(nativeMembers.Skip(1)).All(pair =>
                                pair.Second.GlobalPosition.X > pair.First.GlobalPosition.X && Math.Abs(pair.Second.GlobalPosition.Y - pair.First.GlobalPosition.Y) < .02f) &&
                                tiles.Zip(tiles.Skip(1)).All(pair => !pair.First.GetGlobalRect().Intersects(pair.Second.GetGlobalRect())),
                                key + " plain native cards share one row with separate non-overlapping faces");
                            Check(tiles.All(tile => Math.Abs(tile.Modulate.R - 1) < .001f), key + " every plain native face stays bright");
                        }
                        if (key == "LEAD_PAPERWEIGHT") Check(!stacked && nativeMembers.Length == 2 &&
                            nativeMembers.All(card => card.Model?.Pool is ColorlessCardPool),
                            "actual Lead Paperweight displays both native colorless candidates plainly side by side");
                    }
                    if (key == "ORRERY")
                    {
                        var orreryGroups = rendered.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).ToArray();
                        Check(orreryGroups.Length == 5 && orreryGroups.All(group => group.Cards.Length == 3) &&
                            summaryLines.Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, "^组[1-5]：")) == 5 &&
                            stacks.Length == 5 && stacks.All(stack => stack.GetMeta("rt2_stacked").AsBool()),
                            "Orrery A3 renders five complete reward groups, one text row and one card stack per reward");
                    }
                    if (key == "SEA_GLASS")
                    {
                        var groups = rendered.Branches.SelectMany(branch => NeowOperationOverlay.DisplaySections(rendered, branch)).SelectMany(section => section.CardGroups).ToArray();
                        Check(groups.Length == 3 && groups.All(group => group.Cards.Length == 5 && group.Cards.Select(card => ModelDb.GetById<CardModel>(new(card.Key.Category, card.Key.Entry)).Rarity).Distinct().Count() == 1) &&
                            groups.Select(group => ModelDb.GetById<CardModel>(new(group.Cards[0].Key.Category, group.Cards[0].Key.Entry)).Rarity).Distinct().Count() == 3 && stacks.All(stack => stack.GetMeta("rt2_stacked").AsBool()),
                            "Sea Glass renders three five-card rarity groups as distinct stacks");
                        Check(stacks.Length == 3 && stacks.All(stack => Descendants(stack).OfType<NCard>().Count() == 5 &&
                                Descendants(stack).OfType<NCard>().Select(card => card.Model!.Rarity).Distinct().Count() == 1) &&
                            stacks.Select(stack => Descendants(stack).OfType<NCard>().First().Model!.Rarity).Distinct().Count() == 3,
                            "actual Sea Glass native faces form exactly three distinct rarity-pure groups of five");
                    }
                    if (key is "TOY_BOX" or "ALCHEMICAL_COFFER")
                    {
                        var identities = Descendants(summary!).OfType<HBoxContainer>().Where(row => row.GetChildren().OfType<TextureRect>()
                            .Any(icon => icon.CustomMinimumSize.IsEqualApprox(new Vector2(24, 24)))).ToArray();
                        int count = key == "TOY_BOX" ? 5 : 4;
                        Check(identities.Length == count && identities.Select(row => row.GlobalPosition.Y).Distinct().Count() == count,
                            key + " B model display puts every result on its own row");
                        if (key == "TOY_BOX") Check(!summaryLines.Any(line => line.Contains("蜡质", StringComparison.Ordinal)),
                            "Toy Box does not repeat the wax property in prediction text");
                    }
                    var cardDock = view.GetNodeOrNull<Control>("Rt2NeowCardDock");
                    Check(!Descendants(view).Any(node => node.Name.ToString().StartsWith("Rt2NeowCardMember_", StringComparison.Ordinal) ||
                        node.Name == "Rt2NeowCardReader" || node.Name == "Rt2NeowInspectedCard"),
                        key + " passive preview creates no reader, member hit strips or inspected card");
                    Check(cardDock == null || Descendants(cardDock).OfType<Control>().All(control =>
                        control.MouseFilter == Control.MouseFilterEnum.Ignore && control.FocusMode == Control.FocusModeEnum.None),
                        key + " entire native card dock is passive for pointer and keyboard input");
                    passiveCards += cards.Length;
                    if (cards.Length > 0)
                    {
                        // The actual native option loses focus as soon as the
                        // pointer reaches the passive card art. Nothing can hold
                        // that preview open or create a secondary reader.
                        var cardPosition = cards[0].GetParent<Control>().GetGlobalRect().GetCenter();
                        var underneath = buttons.FirstOrDefault(candidate => candidate.GetGlobalRect().HasPoint(cardPosition));
                        await MovePointer(cardPosition, 0);
                        Check(View() == null && ReferenceEquals(Field(Controller(), "_owner"), underneath) && !ReferenceEquals(underneath, button),
                            key + " passive art releases its old owner and preserves any original option underneath");
                        int nativeSeenAfterMove = _neowSeenCalls;
                        nativeViewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown,
                            Pressed = true, Position = cardPosition, GlobalPosition = cardPosition }, true);
                        // Pair forwarded wheel events too: an unreleased
                        // button would retain native GUI mouse capture.
                        nativeViewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown,
                            Pressed = false, Position = cardPosition, GlobalPosition = cardPosition }, true);
                        await Frames(3);
                        Check(underneath == null ? View() == null : ReferenceEquals(Field(Controller(), "_owner"), underneath),
                            key + " pointer/wheel never restores the old owner or creates a card reader");
                        Check(_neowSeenCalls == nativeSeenAfterMove && Signature() == before,
                            key + " passive art motion/wheel preserves native state and seen-card count");
                        await MovePointer(button.GetGlobalRect().GetCenter());
                        await Until(() => View() is { Visible: true }, key + " native refocus repopulates actual preview");
                    }
                    else Check(_neowSeenCalls == originalTipSeen && Signature() == before,
                        key + " text-only hover preserves native state and seen-card count");

                    // Even a brief focus on another original option owns the
                    // next prediction immediately; no cross-option dwell.
                    var nextButton = ReferenceEquals(button, buttons[0]) ? buttons[1] : buttons[0];
                    await MovePointer(nextButton.GetGlobalRect().GetCenter(), 0);
                    Check(ReferenceEquals(Field(Controller(), "_owner"), nextButton) && View() == null,
                        key + " original native focus switches owner and removes previous view immediately");
                    Controller()._Process(.05);
                    Check(ReferenceEquals(Field(Controller(), "_owner"), nextButton) && Field(Controller(), "_snapshot") != null,
                        key + " brief cross-option focus captures the next forecast without a dwell gate");
                    await Until(() => View() is { Visible: true }, key + " direct focus switch publishes actual next option");
                    Check(((OperationPreview)Field(View()!, "_preview")!).Source.Entry == nextButton.Option.Relic!.Id.Entry,
                        key + " switched preview belongs to actual newly focused option");
                    Check(Signature() == before, key + " direct switch preserves native run and RNG");
                    directFocusSwitches++;
                    await LeavePointer();
                    Check(View() == null && !hoverRoot.GetChildren().OfType<NHoverTipSet>().Any(),
                        key + " native pointer departure clears both views without a grace interval");
                }
            }
            player.PlayerRng.Rewards.LoadFromSerializable(originalRewards);
            if (layoutOnly)
            {
                File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = true, checks, layoutOnly = true,
                    naturalScenarios = layoutEvidence.Count, evidence = layoutEvidence,
                    complexCursorSteps, sameCursorSteps, actualCaptureProjectionRenderer = true,
                    noContentFitAssertion = true, ownerRuntimeAcceptance = false },
                    new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"NEOW_LAYOUT_UI_PASS checks={checks};naturalScenarios={layoutEvidence.Count};OwnerRuntimeAcceptance=false");
                return;
            }

            var failedPreview = new OperationPreview(new("RELIC", "CALLING_BELL"), [], [], false, [], [])
            { Faults = [new("ControlledFailure", [], new InvalidOperationException("Retained original cause"))] };
            var failureView = new NeowOperationOverlay(); hoverRoot.AddChild(failureView);
            // Exercise the actual renderer with opposite native/RT2 languages.
            // Semantic notice and label tokens stay unchanged in the projection.
            string localizedBefore = Signature();
            var localizedPreview = new OperationPreview(new("RELIC", "ORRERY"), [], [], false,
                [new([], [new(new("RELIC", "ORRERY"),
                    [new(OperationPreviewAction.Restock, [new("RELIC", "SMALL_CAPSULE")], [], false)
                        { Label = "OrreryFinalRerollRestockNoSacrifice" }], [])
                        { Label = "RewardRerollStep:3" }], [], 1, true)],
                ["CourierRestockAwaitingObtain"])
                { Notices = ["Purchase:InsufficientGold", "RewardSacrificeProgress:1"] };
            try
            {
                foreach (string language in new[] { "en", "zh" })
                {
                    languagePreferences.SetLanguageOverride(language);
                    LocManager.Instance.SetLanguage(language == "en" ? "zhs" : "eng");
                    failureView.SetPreview(localizedPreview);
                    failureView.SetLayout(new(new Vector2(200, 300), failureView.SummaryDesiredSize), default);
                    await Frames(3);
                    var lines = Descendants(failureView).OfType<Label>().Select(label => label.Text).ToArray();
                    string[] expected = language == "en"
                        ? ["Not enough gold", "This sacrifice: 1/2 → 0/2", "Reroll 3",
                            "Restock with no further rerolls or sacrifices", "Restocking follows completion of the obtain effect"]
                        : ["金币不足", "本次献祭：1/2 → 0/2", "第3次重掷", "若不再重掷或献祭，结算后补货", "领取效果完成后才会补货"];
                    Check(failureView.GetNode<Control>("Rt2NeowSummary").IsVisibleInTree() &&
                        expected.All(text => lines.Contains(text)) &&
                        lines.Any(line => line.Contains(new LocString("relics", "SMALL_CAPSULE.title").GetFormattedText(), StringComparison.Ordinal)) &&
                        !lines.Any(line => line.Contains("Missing translation", StringComparison.Ordinal)) &&
                        ReferenceEquals(Field(failureView, "_preview"), localizedPreview) && Signature() == localizedBefore,
                        "opposite native/RT2 language uses RT2 notices, parameterized progress and Courier labels while retaining native names and prediction identity " + language);
                }
            }
            finally
            {
                languagePreferences.SetLanguageOverride("zh");
                LocManager.Instance.SetLanguage("zhs");
            }
            failureView.SetPreview(failedPreview);
            failureView.SetLayout(new(new Vector2(200, 300), failureView.SummaryDesiredSize), default);
            await Frames(4);
            Check(Descendants(failureView).OfType<Label>().Any(label => label.Text == "预测失败") &&
                !Descendants(failureView).OfType<Label>().Any(label => label.Text == "部分结果预测失败"),
                "whole failure is visible and does not claim partial results");
            await Screenshot("operation-complete-failure-panel-zh.png");
            failureView.SetPreview(failedPreview with { ContainedRelics = [new("RELIC", "SMALL_CAPSULE")] });
            await Frames(3);
            Check(Descendants(failureView).OfType<Label>().Any(label => label.Text == "部分结果预测失败"),
                "partial failure preserves known outcomes and distinct failure wording");
            // Renderer contract for a generated Whetstone chain. This is an
            // explicit detached projection fixture, not a claimed Shovel roll.
            string chainBefore = Signature(); int chainSeen = _neowSeenCalls;
            var detached = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion);
            var upgrades = detached.State.Cards.Where(card => card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack)
                .Take(2).Select(card => card with { UpgradeLevel = card.UpgradeLevel + 1 }).ToImmutableArray();
            var whetstone = new RolltheSpire2.Core.Identity.ModelKey("RELIC", "WHETSTONE");
            var shovel = new RolltheSpire2.Core.Identity.ModelKey("RELIC", "SHOVEL");
            var chain = new OperationPreview(shovel, [], [], false,
                [new([], [new(shovel, [new(OperationPreviewAction.ObtainRelic, [whetstone], [], false)], []),
                    new(whetstone, [new(OperationPreviewAction.UpgradeCard, [], upgrades, false)], [])], [], 1, true)], []);
            failureView.SetPreview(chain);
            failureView.SetLayout(new(new Vector2(150, 480), failureView.SummaryDesiredSize),
                new(new Vector2(700, 350), failureView.CardsDesiredSize));
            await Frames(4);
            var primaryPanel = failureView.GetNode<Control>("Rt2NeowSummary/Rt2OperationPrimaryPanel");
            var effectPanel = failureView.GetNode<Control>("Rt2NeowSummary/Rt2OperationEffectPanel_WHETSTONE");
            string[] effectLines = Descendants(effectPanel).OfType<Label>().Select(label => label.Text).ToArray();
            Check(!primaryPanel.GetGlobalRect().Intersects(effectPanel.GetGlobalRect()) &&
                effectLines.Any(line => line.StartsWith("升级", StringComparison.Ordinal) && upgrades.All(card => line.Contains(
                    new LocString("cards", card.Key.Entry + ".title").GetFormattedText(), StringComparison.Ordinal))) &&
                Descendants(failureView).OfType<NCard>().Count() == 2,
                "generated Whetstone receives a separate source-effect panel naming both upgraded targets with cards outside");
            Check(Signature() == chainBefore && _neowSeenCalls == chainSeen,
                "detached chain renderer changes no run, RNG or seen-card state");
            await Screenshot("operation-shovel-whetstone-independent-effect-panel-projection-zh.png");
            foreach (int proposalCount in new[] { 8, 9, 16 })
            {
                var proposals = Enumerable.Range(0, proposalCount).Select(index =>
                    detached.State.Cards[index % detached.State.Cards.Length] with { Id = index + 1000 }).ToArray();
                var bridgeSource = new RolltheSpire2.Core.Identity.ModelKey("EVENT", "SLIPPERY_BRIDGE");
                var bridge = new OperationPreview(bridgeSource, [], [], false,
                    [new([], proposals.Select((card, index) => new OperationPreviewSection(bridgeSource,
                        [new(OperationPreviewAction.RemoveCard, [], [card], false)], []) { Label = (index + 1).ToString() }).ToImmutableArray(), [], 1, true)], [])
                    { Notices = ["BridgeAlternativeRemovalProposals"] };
                failureView.SetPreview(bridge);
                var bridgeLayout = NeowHoverPlacement.Plan(failureView.GetViewportRect(), buttons[0].GetGlobalRect(),
                    buttons.Select(button => button.GetGlobalRect()).ToArray(),
                    [topBar.GetNode<Control>("BgImage").GetGlobalRect()], Vector2.Zero, failureView.CardsDesiredSize,
                    false, failureView.CardsSizeForWidth);
                failureView.SetLayout(bridgeLayout.Summary, bridgeLayout.Cards);
                await Frames(4);
                var bridgeGroups = NeowOperationOverlay.DisplaySections(bridge, bridge.Branches.Single()).SelectMany(section => section.CardGroups).ToArray();
                var bridgeHosts = Descendants(failureView).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                // Owner's later complete-sequence correction (e103273f)
                // supersedes the old 15-face cap. Placement may wrap stacks.
                Check(failureView.SummaryDesiredSize == Vector2.Zero &&
                    bridgeGroups.SelectMany(group => group.Cards).SequenceEqual(proposals) &&
                    bridgeHosts.Length == (proposalCount <= 8 ? 1 : (proposalCount + 4) / 5) &&
                    bridgeHosts.All(host => host.GetMeta("rt2_stacked").AsBool() == (proposalCount > 8)) &&
                    bridgeHosts.Zip(bridgeHosts.Skip(1)).All(pair => pair.Second.GlobalPosition.Y > pair.First.GlobalPosition.Y ||
                        Math.Abs(pair.Second.GlobalPosition.Y - pair.First.GlobalPosition.Y) < .1f &&
                        pair.Second.GlobalPosition.X >= pair.First.GetGlobalRect().End.X) &&
                    failureView.VisibleRegions.All(region => failureView.GetViewportRect().Encloses(region) &&
                        !region.Intersects(buttons[0].GetGlobalRect())),
                    "Bridge preserves the full proposal order, shows a single row through eight, then ordered five-card stacks without a generic panel");
                var bridgeFaces = Descendants(failureView).OfType<NCard>().ToArray();
                Check(bridgeFaces.Length == proposalCount &&
                    bridgeFaces.Select(card => card.Model!.Id.Entry).SequenceEqual(proposals.Select(card => card.Key.Entry)) &&
                    bridgeFaces.All(card => Math.Abs(card.GetParent<Control>().Scale.X - failureView.CardDisplayScale) < .001f) &&
                    (proposalCount > 8 || bridgeFaces.Select(card => card.GetParent<Control>().GlobalPosition.Y).Distinct().Count() == 1) &&
                    bridge.Branches.Single().Sections.Length == proposalCount,
                    "Bridge renders every native face in the complete original projection at a fixed card scale");
                await Screenshot($"operation-bridge-{proposalCount}-proposals-projection-zh.png");
            }
            foreach (int rewardGroups in new[] { 1, 2 })
            {
                var dense = new RolltheSpire2.Core.Identity.ModelKey("EVENT", "DENSE_VEGETATION");
                var rewards = Enumerable.Range(0, rewardGroups).Select(index => new OperationPreviewCardGroup(index,
                    detached.State.Cards.Skip(index * 3).Take(3).ToImmutableArray(), 1, 1, true, false)).ToImmutableArray();
                var combat = new OperationPreview(dense, [], [], false,
                    [new([], [new(dense, [], rewards) { Label = "EventCombatRewards" }], [], 1, true)], []);
                failureView.SetPreview(combat);
                failureView.SetLayout(new(new Vector2(150, 600), failureView.SummaryDesiredSize),
                    new(new Vector2(700, 300), failureView.CardsDesiredSize));
                await Frames(4);
                var combatHosts = Descendants(failureView).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                var combatLines = Descendants(failureView.GetNode<Control>("Rt2NeowSummary")).OfType<Label>().Select(label => label.Text).ToArray();
                Check(combatHosts.Length == rewardGroups && combatHosts.All(host => host.GetMeta("rt2_stacked").AsBool() == (rewardGroups > 1)) &&
                    combatLines.Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, "^组[1-2]：")) == rewardGroups &&
                    Descendants(failureView).OfType<NCard>().Count() == rewardGroups * 3,
                    "post-combat multi-reward A3 keeps distinct stacks and one complete text row per reward; ordinary single reward stays flat");
                await Screenshot($"operation-event-combat-{rewardGroups}-groups-projection-zh.png");
            }
            hoverRoot.RemoveChild(failureView); failureView.QueueFree();
            await Frames();

            // Owner's D2 prototype enumerates every real transformation target
            // without choosing a target or mutating the original native deck.
            await ReplaceFirstOffer("NEW_LEAF");
            string leafBefore = Signature();
            int leafSeenBefore = _neowSeenCalls;
            await MovePointer(buttons[0].GetGlobalRect().GetCenter(), 0);
            var leafController = Controller(); leafController.SetProcess(false);
            // Keep a handle to the real worker before publication can consume
            // this very short task. Start capture explicitly before resuming publication.
            AccessTools.Method(typeof(NeowOperationHoverController), "BeginPrediction").Invoke(leafController, null);
            var leafSnapshot = (PredictorOperationSnapshot?)Field(leafController, "_snapshot");
            var leafWork = (Task<OperationPreview>?)Field(leafController, "_work");
            Check(leafSnapshot?.Scene is PredictorOperationAncientScene leafScene && leafScene.Offers.Any(offer =>
                offer.Id == PredictorLiveRunCapture.Id(buttons[0].Option) && offer.Relic.Entry == "NEW_LEAF") && leafWork != null,
                "New Leaf native hover starts its actual snapshot and detached worker");
            await Until(() => leafWork!.IsCompleted, "New Leaf actual detached worker completes");
            Check(leafWork!.IsCompletedSuccessfully, "New Leaf forecast completes without cancellation or hidden failure");
            var leafPreview = leafWork.Result;
            Check(leafPreview.Source.Entry == "NEW_LEAF" && leafPreview.Faults.IsEmpty &&
                leafPreview.Branches.Sum(branch => branch.Sections.Sum(section => section.CardGroups.Length)) == 1 &&
                leafPreview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).Single().Cards.Length > 0 &&
                leafPreview.Branches.Single().PathsExamined > 1,
                "New Leaf actual forecast enumerates all real native target paths into one D2 result group");
            leafController._Process(0); leafController.SetProcess(true); await Frames(5);
            Check(View() is { Visible: true } leafView && leafView.GetNodeOrNull<Control>("Rt2NeowSummary") == null &&
                Descendants(leafView).OfType<NCard>().Any() &&
                Descendants(leafView).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal))
                    .All(stack => stack.GetMeta("rt2_stacked").AsBool()),
                "New Leaf completed actual worker displays a passive D2 stack without a text panel");
            var leafGroups = leafPreview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).ToArray();
            var leafStack = Descendants(View()!).OfType<Control>().Single(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal));
            var leafTiles = leafStack.GetChildren().OfType<Control>().Where(tile => tile.GetChildren().OfType<NCard>().Any()).ToArray();
            Check(leafGroups.Single().EarliestFirst && leafStack.GetMeta("rt2_earliest_first").AsBool() &&
                leafTiles[0].ZIndex == leafTiles.Max(tile => tile.ZIndex) && Math.Abs(leafTiles[0].Modulate.R - 1) < .001f,
                "New Leaf earliest current-deck candidate remains the bright front card at lower-left");
            Check(buttons[0].IsVisibleInTree() && buttons[0].IsEnabled && !buttons[0].Option.WasChosen && neow.CurrentOptions.Count == 4,
                "New Leaf native option remains available and unchosen on its four-option page");
            Check(Signature() == leafBefore && _neowSeenCalls == leafSeenBefore,
                "New Leaf all-target forecast preserves actual state, all RNG and seen-card count");
            await Screenshot("neow-new_leaf-all-targets-d2-1680x1260-zh.png");
            await LeavePointer();
            await ReplaceFirstOffer("SCROLL_BOXES");

            string stable = Signature();
            var featurePreferences = new RolltheSpire2.Ui.Workspace.SearchWorkspace(OS.GetUserDataDir(), false);
            bool operationWasEnabled = featurePreferences.Preferences.OperationPredictionEnabled;
            try
            {
                Hover(buttons[1]);
                var featureController = Controller(); featureController.SetProcess(false);
                AccessTools.Method(typeof(NeowOperationHoverController), "BeginPrediction").Invoke(featureController, null);
                var featureWork = (Task)Field(featureController, "_work")!;
                var featureSnapshot = Field(featureController, "_snapshot");
                var featureCancellation = (CancellationTokenSource)Field(featureController, "_cancellation")!;
                featurePreferences.SetOperationPredictionEnabled(false);
                Check(featureCancellation.IsCancellationRequested && View() == null && !featureController.IsProcessing() &&
                    Field(featureController, "_work") == null && Field(featureController, "_snapshot") == null,
                    "feature disable cancels pending operation work, releases capture and clears its renderer immediately");
                await Until(() => featureWork.IsCompleted, "disabled feature's old detached task settles");
                featureController._Process(0); Hover(buttons[2]); await Frames(4);
                Check(View() == null && Field(featureController, "_work") == null &&
                    !(bool)Field(featureController, "_captureQueued")!,
                    "late worker completion and new native hovers cannot restart a disabled feature");
                featurePreferences.SetOperationPredictionEnabled(true);
                await Until(() => View() is { Visible: true }, "re-enabled feature refreshes the currently focused native option");
                Check(!ReferenceEquals(featureSnapshot, Field(featureController, "_snapshot")) &&
                    ((OperationPreview)Field(View()!, "_preview")!).Source.Entry == buttons[2].Option.Relic!.Id.Entry,
                    "re-enable captures the current option afresh instead of publishing the cancelled option");
                Check(Signature() == stable, "operation feature toggles preserve native state and RNG");
            }
            finally
            {
                Unhover(buttons[1]); Unhover(buttons[2]); await LeavePointer();
                featurePreferences.SetOperationPredictionEnabled(operationWasEnabled);
            }
            Hover(buttons[1]);
            var control = Controller(); control.SetProcess(false);
            // As in the New Leaf fixture, retain the actual task before any
            // publication pass can consume a fast same-frame completion.
            AccessTools.Method(typeof(NeowOperationHoverController), "BeginPrediction").Invoke(control, null);
            var staleWork = (Task?)Field(control, "_work");
            Check(staleWork != null, "stale test starts actual detached work");
            player.Gold++;
            await Until(() => staleWork!.IsCompleted, "stale detached work finishes");
            control._Process(0);
            Check(View() == null && (bool)Field(control, "_captureQueued")!, "changed native state cannot publish stale result");
            player.Gold--; Unhover(buttons[1]); await LeavePointer(); await Frames(15);
            Check(View() == null && Signature() == stable, "stale refresh cannot refill after leaving");
            Hover(buttons[3]); Controller()._Process(.2);
            var cancellation = (CancellationTokenSource?)Field(Controller(), "_cancellation");
            Unhover(buttons[3]); await LeavePointer(); await Frames(20);
            Check(cancellation?.IsCancellationRequested == true && View() == null, "leave cancels actual work without late publication");
            Hover(buttons[0]); buttons[0].Disable(); await Frames(15);
            Check(View() == null && Field(Controller(), "_owner") == null, "native Disable triggers actual unfocus cleanup");
            buttons[0].Enable(); Hover(buttons[0]);
            buttonHost.RemoveChild(buttons[0]); buttons[0].Free(); await Frames(15);
            Check(View() == null && Field(Controller(), "_owner") == null, "native tree exit clears pending hover");
            Check(Signature() == stable, "all prediction/cancellation/lifecycle checks preserve native state");

            // This last isolated callback checks the complete native release ->
            // NEventRoom -> EventSynchronizer path without obtaining a relic or
            // touching a user run. All representative forecasts above used the
            // original Neow option callbacks, never this harmless callback.
            foreach (var button in buttons.Skip(1)) { buttonHost.RemoveChild(button); button.Free(); }
            int clickCalls = 0;
            var clickOption = EventOption.FromRelic(offers[1].Relic!, neow,
                () => { clickCalls++; return Task.CompletedTask; }, "INITIAL");
            Set(neow, "_currentOptions", new List<EventOption> { clickOption });
            var clickButton = NEventOptionButton.Create(neow, clickOption, 0);
            int nativeMousePresses = 0, nativeMouseReleases = 0, nativeReleases = 0;
            clickButton.MousePressed += _ => nativeMousePresses++;
            clickButton.MouseReleased += _ => nativeMouseReleases++;
            clickButton.Released += _ => nativeReleases++;
            buttonHost.AddChild(clickButton); clickButton.EnableButton();
            await SettleNativeLayout();
            await MovePointer(clickButton.GetGlobalRect().GetCenter());
            await Until(() => View() is { Visible: true }, "native release begins with visible actual prediction");
            var clickPosition = clickButton.GetGlobalRect().GetCenter();
            var clickStates = new List<object>();
            void RecordClickState(string stage)
            {
                bool valid = GodotObject.IsInstanceValid(clickButton);
                var hovered = nativeViewport.GuiGetHoveredControl();
                var pointerChain = new List<object>();
                for (Node? ancestor = hovered; ancestor is Control controlNode; ancestor = ancestor.GetParent())
                    pointerChain.Add(new { path = ancestor.GetPath().ToString(), mouseFilter = controlNode.MouseFilter.ToString() });
                clickStates.Add(new { stage, position = clickPosition.ToString(),
                    hovered = hovered?.GetPath().ToString(), pointerChain,
                    nativeMousePresses, nativeMouseReleases, nativeReleases, clickCalls, clickOption.WasChosen,
                    buttonValid = valid, enabled = valid && clickButton.IsEnabled,
                    focused = valid ? Field(clickButton, "<IsFocused>k__BackingField") : null, pressed = valid ? Field(clickButton, "_isPressed") : null,
                    nativeRect = valid ? clickButton.GetGlobalRect().ToString() : null,
                    predictionOwner = Field(Controller(), "_owner") is Node owner ? owner.GetPath().ToString() : null,
                    predictionVisible = View()?.IsVisibleInTree() ?? false,
                    predictionRegions = View()?.VisibleRegions.Select(rect => rect.ToString()).ToArray() ?? [] });
                File.WriteAllText(Path.Combine(output, "neow-native-click-states.json"),
                    JsonSerializer.Serialize(clickStates, new JsonSerializerOptions { WriteIndented = true }));
            }
            RecordClickState("before-press");
            nativeViewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                Pressed = true, ButtonMask = MouseButtonMask.Left, Position = clickPosition, GlobalPosition = clickPosition }, true);
            RecordClickState("after-press");
            Check(nativeMousePresses == 1 && (bool?)Field(clickButton, "_isPressed") == true,
                "actual native GUI press reaches focused enabled button and arms its original release handler");
            await Frames(1);
            nativeViewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                Pressed = false, ButtonMask = 0, Position = clickPosition, GlobalPosition = clickPosition }, true);
            RecordClickState("after-release");
            Check(nativeMouseReleases == 1 && nativeReleases == 1,
                "actual native GUI release invokes its original release handler exactly once");
            Check(View() == null, "native mouse release clears preview immediately");
            await Frames(15);
            Check(clickCalls == 1 && clickOption.WasChosen && View() == null, "original native release/click handler runs and cleans preview");
            if (OS.GetCmdlineUserArgs().Contains("--operation-ui"))
                await RunAdditionalOperationChecks(root, output, runtime, game, nativeViewport, hoverRoot, run, player,
                    eventNode, layoutScenes, roomScenes, global, topBar, fixture, Set);
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = true, checks, nativeButtons = true,
                actualCaptureProjectionRenderer = true, sourceCases = sources, runAndRngUnchangedByPrediction = true,
                complexBonesPair = "KALEIDOSCOPE+LARGE_CAPSULE", sameBonesPair = "SCROLL_BOXES+WINGED_BOOTS",
                complexCursorSteps, sameCursorSteps, expectedFaces, renderedFaces, bonesTextCards, passiveCards, summaryCardRows, summaryCardGroups,
                naturalFitSummaries, directFocusSwitches, geometry,
                nativeTooltipSeenCalls,
                predictionAddsNoSeenCards = true, originalClickDispatch = true, staleAndCancelNoBackfill = true,
                newLeafActualWorkerAllTargetsD2 = true,
                pointer = "native SubViewport.PushInput mouse motion: native option -> passive art/immediate unfocus -> native refocus -> direct other-option focus -> away; wheel on former artwork; native click press/release; lifecycle cancellation additionally uses direct signals; no desktop cursor warp",
                currentPage = "controlled original native Neow options; not a claimed naturally rolled four-option page",
                nativeLayoutScene = NAncientEventLayout.ancientScenePath,
                nativeTopBarScene = "res://scenes/ui/top_bar.tscn",
                nativeDialoguePlacement = "NAncientEventLayout.SetDialogueLineAndAnimate final-line target, with tween settled directly",
                fixture = "mounted native ancient_event_layout.tscn, native final dialogue/options, settled native ancient_name_banner.tscn and top_bar.tscn visuals in real-size SubViewport; FramePostDraw captures; isolated singleton bindings; game startup, FMOD and top-bar/banner live callbacks bypassed; original static Neow background texture, Spine animation unavailable in stock Godot",
                ownerRuntimeAcceptance = false },
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"NEOW_OPERATION_UI_PASS checks={checks};nativeFocus=true;actualCapture=true;renderer=true;runRngUnchanged=true;OwnerRuntimeAcceptance=false");
        }
        finally
        {
            if (game != null && GodotObject.IsInstanceValid(game)) game.Free();
            // Native selection screens return holders, then cards, through
            // deferred pool callbacks on tree exit. Flush that real lifecycle
            // before freeing this process-local fixture's pool registrations.
            if (_operationFixturePools.Count > 0) await Frames(3);
            CleanupOperationFixturePools();
            if (nativeDisplay != null && GodotObject.IsInstanceValid(nativeDisplay)) nativeDisplay.Free();
            if (nativeViewport != null && GodotObject.IsInstanceValid(nativeViewport)) nativeViewport.Free();
            _neowStaticSceneNodes.Clear();
            if (addedDebugAction) InputMap.EraseAction("debug_hide_event_ui");
            _neowFixtureGame = null;
            product.UnpatchAll(product.Id); fixture.UnpatchAll(fixture.Id);
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, oldGame);
            foreach (var node in nodes) if (GodotObject.IsInstanceValid(node)) node.Free();
            neow?.EnsureCleanup(); RunManager.Instance.CleanUp(); LocalContext.NetId = oldLocal;
            typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!.SetValue(null, oldRuntime);
            TestMode.IsOn = oldTest; LocManager.Instance.SetLanguage(oldLocale);
            languagePreferences.SetLanguageOverride(oldUiLanguage);
        }
    }
}
