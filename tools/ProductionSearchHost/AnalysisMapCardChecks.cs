using Godot;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;

internal static class AnalysisMapCardChecks
{
    internal static async Task Run(Node host, string output)
    {
        var assembly = typeof(SeedPredictionRequest).Assembly;
        object New(string name, params object?[] args) => Activator.CreateInstance(assembly.GetType("RolltheSpire2." + name, true)!, args)!;
        object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, args);
        object Resolver(string n) => New("Infrastructure.ContentNames." + n);
        var icons = New("Ui.Icons.ReflectionGameIconResolver", "map-preview-checks");
        var tooltip = (Control)New("Ui.Tooltips.AnchoredTooltipHost", Resolver("RuntimeRelicTooltipResolver"),
            Resolver("RuntimePotionTooltipResolver"), Resolver("RuntimeCardTooltipResolver"));
        Func<IGameContentNameResolver?> names = () => null;
        var card = (Control)New("Ui.Components.AnalysisActPredictionCard", 1, icons,
            New("Ui.Icons.EventThumbnailProvider"), tooltip, names);
        host.GetWindow().Size = new Vector2I(1120, 1000);
        host.AddChild(card); host.AddChild(tooltip);
        var workspace = (Control)New("Ui.Components.MapWorkspace");
        host.AddChild(workspace);
        workspace.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        card.Position = new Vector2(20, 20); card.Size = new Vector2(1080, 930);
        var display = new GameContentDisplayViewModel(new(new ModelKey("ENCOUNTER", "KAISER_CRAB_BOSS"), GameContentKind.Encounter), "Kaiser Crab", "fixture");
        var boss = new BossPredictionViewModel(1, 1, display, PredictionPrecision.Exact, "", SourceAuthority.AuditedStaticExact,
            SnapshotCompleteness.Complete, "", 0, "fixture");
        var domain = new SeedDomainViewModel<BossPredictionViewModel>(SeedDomainEvaluationStatus.Evaluated, "", new[] { boss });
        var events = new SeedDomainViewModel<EventPoolActSequenceViewModel>(SeedDomainEvaluationStatus.Unknown, "fixture", Array.Empty<EventPoolActSequenceViewModel>());
        var predictor = assembly.GetType("RolltheSpire2.Core.Prediction.Maps.Beta111MapPredictor", true)!;
        object[] maps = new[] { "OVERGROWTH", "HIVE", "GLORY" }.Select((act, index) =>
            predictor.GetMethod("Predict", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                new object[] { "000000067C0S", RuntimeProfileId.Beta111, new ModelKey("ACT", act), index, 10, 1, index == 2, CancellationToken.None })!).ToArray();
        var button = (Button)card.FindChild("MapPreviewButton", true, false);
        // This isolated host initializes only the two fixture models, not the complete
        // Act catalogue used by the production resolver. Load their official visuals directly.
        var bossModel = MegaCrit.Sts2.Core.Models.ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.KaiserCrabBoss>();
        var bossIcon = ResourceLoader.Load<Texture2D>(MegaCrit.Sts2.Core.Helpers.ImageHelper.GetRoomIconPath(
            MegaCrit.Sts2.Core.Map.MapPointType.Boss, MegaCrit.Sts2.Core.Rooms.RoomType.Boss, bossModel.Id));
        var ancientIcon = MegaCrit.Sts2.Core.Models.ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().RunHistoryIcon;
        if (bossIcon is null || ancientIcon is null) throw new Exception("Official special-node fixture assets missing");
        IUiTextProvider text = JsonUiTextProvider.Create("en");
        Action<int> open = act => { card.Visible = false; Call(workspace, "Open", act, "000000067C0S", text, button); };
        Action closed = () => card.Visible = true;
        workspace.GetType().GetEvent("Closed")!.AddEventHandler(workspace, closed);
        int requestedAct = 0;
        Action<int> load = act => requestedAct = act;
        workspace.GetType().GetEvent("ActRequested")!.AddEventHandler(workspace, load);
        card.GetType().GetEvent("MapRequested")!.AddEventHandler(card, open);
        foreach (Vector2I windowSize in new[] { new Vector2I(1440, 900), new Vector2I(1120, 760) })
        foreach (string language in new[] { "en", "zh" })
        {
            host.GetWindow().Size = windowSize;
            card.Size = new Vector2(windowSize.X - 40, windowSize.Y - 40);
            for (int i = 0; i < 3; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            text = JsonUiTextProvider.Create(language);
            Call(card, "ApplyLocalization", text);
            Call(card, "Bind", domain, events, RuntimeProfileId.Beta111, 1, null, "", "?");
            ((Control)card.FindChild("EventSubcard", true, false)).Visible = false;
            Call(card, "SetMapAvailable", true);
            if (button.Icon is null) throw new Exception("Official map entry icon missing");
            Vector2 beforeSize = card.GetCombinedMinimumSize();
            button.EmitSignal(Button.SignalName.Pressed);
            if (!workspace.Visible) throw new Exception("Map subpage did not open");
            for (int selectedAct = 1; selectedAct <= 3; selectedAct++)
            {
                var selectedTab = (Button)workspace.FindChild("MapAct" + selectedAct, true, false);
                selectedTab.GrabFocus();
                selectedTab.EmitSignal(Button.SignalName.Pressed);
                if (requestedAct != selectedAct) throw new Exception("Act tab requested the wrong map");
                var mapView = (Control)workspace.FindChild("MapView", true, false);
                if (mapView.GetChildCount() != 0) throw new Exception("Previous Act remained visible during loading");
                Call(workspace, "ShowMap", maps[selectedAct % 3], new Texture2D?[] { bossIcon }, ancientIcon);
                if (mapView.GetChildCount() != 0) throw new Exception("Stale Act result published");
                Call(workspace, "ShowMap", maps[selectedAct - 1], new Texture2D?[] { bossIcon, bossIcon }, ancientIcon);
                for (int i = 0; i < 4; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (button.GetGlobalRect().End.X < card.GetGlobalRect().End.X - 40 || button.GetGlobalRect().End.X > card.GetGlobalRect().End.X)
                    throw new Exception("Map button not aligned to card right edge");
                using (var image = host.GetViewport().GetTexture().GetImage()) image.SavePng(Path.Combine(output, "map-workspace-" + language + "-" + windowSize.X + "-act" + selectedAct + ".png"));
                if (card.GetCombinedMinimumSize() != beforeSize) throw new Exception("Map expanded the Act card");
                if (selectedAct == 1 && ((language == "zh" && windowSize.X == 1440) || (language == "en" && windowSize.X == 1120)))
                    await MapRouteInteractionChecks.Run(host, workspace, maps, bossIcon, ancientIcon, output, language);
                var dialog = workspace;
                var view = (Control)workspace.FindChild("MapView", true, false);
                var normal = view.GetChildren().OfType<TextureRect>().First(t => t.Material is ShaderMaterial);
                Color initial = ((ShaderMaterial)normal.Material).GetShaderParameter("initial_color").AsColor();
                if (initial.R < .6f || initial.G < .5f) throw new Exception("Official material mask lost");
                var info = (Control)workspace.FindChild("MapInformation", true, false);
                if (view.GlobalPosition.X < dialog.GlobalPosition.X + dialog.Size.X * .36f ||
                    Math.Abs(info.Size.X / ((Control)workspace.FindChild("MapRegion", true, false)).Size.X - 2f / 3f) > .03f) throw new Exception("Workspace panes must retain 40/60 space");
                if (view.Size.X > 430.1f) throw new Exception("Map paper too wide");
                foreach (var icon in view.GetChildren().OfType<TextureRect>())
                    if (!new Rect2(Vector2.Zero, view.Size).Encloses(icon.GetRect())) throw new Exception("Map node clipped");
                if (Math.Abs(workspace.Size.X - windowSize.X) > 2) throw new Exception("Workspace width constrained like a modal");
                for (int tab = 1; tab <= 3; tab++)
                    if (((Button)workspace.FindChild("MapAct" + tab, true, false)).ButtonPressed != (tab == selectedAct)) throw new Exception("Incorrect selected-tab appearance");
            }
            var closedView = (Control)workspace.FindChild("MapView", true, false);
            host.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (workspace.Visible || !card.Visible) throw new Exception("Escape failed to restore prediction");
            if (closedView.GetChildCount() != 0) throw new Exception("Map retained stale nodes");
            button.EmitSignal(Button.SignalName.Pressed);
            ((Button)workspace.FindChild("BackToPrediction", true, false)).EmitSignal(Button.SignalName.Pressed);
            if (workspace.Visible || !card.Visible) throw new Exception("Back button failed");
            for (int directAct = 2; directAct <= 3; directAct++)
            {
                open(directAct);
                if (requestedAct != directAct) throw new Exception("Deep link selected incorrect Act");
                Call(workspace, "Close");
            }
            Call(workspace, "ShowMap", maps[2], new Texture2D?[] { bossIcon }, ancientIcon);
            if (closedView.GetChildCount() != 0) throw new Exception("Closed workspace accepted stale map");
        }
        workspace.Free(); card.Free(); tooltip.Free();
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Passed = true,
            Checks = "actual AnalysisActPredictionCard; en/zh; official map entry; right alignment; real seed standard graph; unified full-page workspace; three Act switches/deep links; 40/60 regions; compact map paper; all nodes fit; no inline expansion; stale result rejection; Escape/back restores report; clear" }));
    }
}
