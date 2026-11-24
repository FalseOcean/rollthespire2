using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Tooltips;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class UnknownSequenceUiChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("UnknownSequenceUi");
        var icons = new ReflectionGameIconResolver("unknown-sequence");
        using var characterIcons = new NativeCharacterPoolIconProvider(icons);
        var tooltip = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(), new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
        var page = new AnalysisPage(runtime, icons, characterIcons, [BaseGameModelKeys.Characters.Ironclad], tooltip);
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        tree.Root.AddChild(page);
        page.Size = new(1536, 772);
        try
        {
            page.ApplyLocalization(JsonUiTextProvider.CreatePredictorUi13("zh"), RuntimeGameContentNameResolver.Create("zh"));
            var workspace = new SearchWorkspace(Path.Combine(Path.GetTempPath(), "rt2-unknown-series", Guid.NewGuid().ToString("N")), false);
            var controller = new AnalysisPageController(page, runtime, new RuntimePredictionSettings(),
                new NullRuntimePredictionDiagnosticSink(), workspace, (_, _, _) => { });
            controller.AnalyzeCurrentDraft();
            string seed = page.LastFacts!.CanonicalSeed;
            // Prewarm the same map tasks as the real overview. This synchronous
            // host does not run an engine frame while a test is executing.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            for (int act = 1; act <= 3; act++)
            {
                var key = typeof(AnalysisPage).GetMethod("OverviewActKey", flags)!.Invoke(page, [act]);
                var map = (Task)typeof(AnalysisPage).GetMethod("GetOverviewMap", flags)!.Invoke(page, [act, key, CancellationToken.None])!;
                map.GetAwaiter().GetResult();
            }
            T Find<T>(string name) where T : Godot.Node => page.FindChild(name, true, false) as T ?? throw new Exception(name);
            Find<Godot.Button>("OverviewTab").EmitSignal(Godot.BaseButton.SignalName.Pressed);
            Require(Find<Godot.ScrollContainer>("PredictorMainScroll").VerticalScrollMode == Godot.ScrollContainer.ScrollMode.ShowAlways, "persistent center scrollbar");
            var localize = JsonUiTextProvider.CreatePredictorUi13("zh");
            var marks = new HashSet<int>();
            void Verify()
            {
                int priorDraws = 0;
                for (int act = 1; act <= 3; act++)
                {
                    int count = Find<Godot.OptionButton>("UnknownCount" + act).ItemCount - 2;
                    var selection = Find<Godot.OptionButton>("UnknownCount" + act).Selected;
                    if (selection > 0) count = selection - 1;
                    var expected = Core.World.OrdinaryUnknownRoomPredictor.Baseline(seed, priorDraws, count, act == 1 ? marks : null);
                    foreach (var step in expected.Steps)
                    {
                        var b = Find<Godot.Button>($"Unknown{act}_{step.Ordinal}");
                        var textWidth = b.GetThemeFont("font").GetStringSize(b.Text, fontSize: b.GetThemeFontSize("font_size")).X;
                        Require(b.GetCombinedMinimumSize().X >= textWidth, "room text fits the horizontal strip");
                        var roomType = Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(step.Room.ToString());
                        var point = roomType == MegaCrit.Sts2.Core.Rooms.RoomType.Event
                            ? MegaCrit.Sts2.Core.Map.MapPointType.Unknown : Enum.Parse<MegaCrit.Sts2.Core.Map.MapPointType>(step.Room.ToString());
                        var path = MegaCrit.Sts2.Core.Helpers.ImageHelper.GetRoomIconPath(point, roomType, null);
                        Require(b.Icon?.ResourcePath == path && b.Text == step.Ordinal.ToString(), "native resolved room icon and ordinal");
                        Require(b.ButtonPressed == step.ShopBlacklisted &&
                            b.GetNode<Godot.ColorRect>("ShopMark").Visible == step.ShopBlacklisted &&
                            b.AccessibilityName.Contains(localize.Get("ui1.map.room." + step.Room)), "room name and shop mark");
                    }
                    priorDraws += count;
                }
            }
            Verify();
            var first = Find<Godot.Button>("Unknown1_1");
            first.EmitSignal(Godot.BaseButton.SignalName.Pressed); marks.Add(1); Verify();
            first.EmitSignal(Godot.BaseButton.SignalName.Pressed); marks.Clear(); Verify();
            var countPicker = Find<Godot.OptionButton>("UnknownCount1");
            countPicker.Select(1);
            countPicker.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, 1);
            Require(page.FindChild("Unknown1_1", true, false) is null, "zero visits");
            Verify();
            var card = Find<Godot.Control>("UnknownSequenceCard").GetParent();
            Require(card.GetIndex() == card.GetParent().GetChildCount() - 1, "question marks at the bottom");
            var tabs = Find<Godot.HBoxContainer>("PredictorPages");
            tabs.GetChild<Godot.Button>(1).EmitSignal(Godot.BaseButton.SignalName.Pressed);
            var toggle = Find<Godot.CheckButton>("SpoilsMapToggle");
            toggle.ButtonPressed = true;
            Require(Find<Godot.CheckButton>("SpoilsMapToggle").ButtonPressed, "Act 2 Spoils preview toggled");
            Find<Godot.CheckButton>("SpoilsMapToggle").ButtonPressed = false;
            RouteNotebookChecks.Run(page, seed);
            Console.WriteLine("UNKNOWN_SERIES_UI_PASS rows=3 toggle=on/off priorActCount=0 scrollbar=always-visible");
        }
        finally { page.Free(); tooltip.Free(); }
        return 0;
        static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    }
}
