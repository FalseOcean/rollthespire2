using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunOwnerPolishEditorChecksAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        IEnumerable<Node> Nodes(Node node)
        { yield return node; foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested; }
        int checks = 0;
        void Check(bool valid, string why)
        { checks++; if (!valid) throw new InvalidOperationException("OwnerPolishEditors:" + why); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        var screenshots = new List<string>();
        async Task Capture(string name)
        {
            await Frames(); RenderingServer.ForceDraw();
            string path = Path.Combine(output, "owner-editors-" + name + ".png");
            root.GetViewport().GetTexture().GetImage().SavePng(path); screenshots.Add(path);
        }
        Vector2I previousSize = root.GetWindow().Size, previousScaleSize = root.GetWindow().ContentScaleSize;
        var previousScaleMode = root.GetWindow().ContentScaleMode;
        float previousScale = root.GetWindow().ContentScaleFactor;
        root.GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.GetWindow().ContentScaleFactor = 1;
        root.GetWindow().ContentScaleSize = new(1600, 900); root.GetWindow().Size = new(1600, 900);
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        string stateRoot = Path.Combine(output, "owner-editors-state", Guid.NewGuid().ToString("N"));
        shell.InitializeWithPersistence(runtime, stateRoot); host.AddChild(shell);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var editor = Field<ActInformationSearchEditor>(frame, "_actInformationEditor");
        var relics = Field<RelicSequenceSearchEditor>(frame, "_relicEditor");
        var empty = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        ModelKey first = new("RELIC", "WHETSTONE"), second = new("RELIC", "WAR_PAINT");
        T Find<T>(Node owner, string name) where T : Node => (T)owner.FindChild(name, true, false)!;
        async Task Press(Node owner, string name)
        { Find<Button>(owner, name).EmitSignal(BaseButton.SignalName.Pressed); await Frames(); }
        void EngineInput(InputEvent input) { Input.ParseInputEvent(input); Input.FlushBufferedEvents(); }
        try
        {
            shell.Open(); await AwaitPageReady(root, shell); await Frames(8);
            root.GetViewport().NotifyMouseEntered();
            foreach (string locale in new[] { "zh", "en" })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(locale); Call(shell, "RefreshLanguage");
                var text = JsonUiTextProvider.CreateUi13(locale);
                frame.RestoreDraft(empty, render: false); frame.Refresh(locale, text); Call(frame, "SelectDomain", "boss"); await Frames();
                Button[] secondBosses = Nodes(editor).OfType<Button>().Where(button => button.Name.ToString().StartsWith("SecondBoss3_", StringComparison.Ordinal)).ToArray();
                Check(secondBosses.Length > 0, "actual A10 catalog exposes third-act second Boss");
                var identityScroll = Nodes(editor).OfType<ScrollContainer>().Single();
                Check(identityScroll.GetVScrollBar().MaxValue <= identityScroll.GetVScrollBar().Page + 2,
                    "normal Boss/variant surface fits without scrolling " + locale);
                Check(secondBosses.All(button => identityScroll.GetGlobalRect().Encloses(button.GetGlobalRect())),
                    "normal third-act second Boss fully visible " + locale);
                await Capture("boss-" + locale);

                Call(frame, "SelectDomain", "map"); await Frames(); await Press(editor, "MapScope1");
                var quantity = Find<MapQuantityInput>(editor, "MapRouteTargetValue");
                var node = Find<OptionButton>(editor, "MapRouteNode");
                Check(quantity.Size.X < 220 && quantity.GetRect().End.X < editor.Size.X - 100 && node.Size.X < 260,
                    "single-route type and quantity remain compact " + locale);
                await Capture("route-" + locale);

                var twelve = Enumerable.Range(1, 12).Select(index => new MapDrawingNodeSnapshot(index,
                    new[] { StandardMapRoomType.Monster, StandardMapRoomType.Elite, StandardMapRoomType.RestSite,
                        StandardMapRoomType.Shop, StandardMapRoomType.Unknown }[(index - 1) % 5], 0, index)).ToArray();
                frame.RestoreDraft(empty with
                {
                    Query = SearchQuery.Empty with { StandardMapRoutes = [new(1, twelve.Select(n => new StandardMapRouteGroup([n.Room])).ToArray())] },
                    MapEditor = new(1, [new(1, true, new(StandardMapMetric.ReachableMaxElite, null), twelve)])
                }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "map"); await Frames();
                var drawing = Find<MapRouteDrawingControl>(editor, "MapRouteDrawing");
                Check(drawing.NodeRects.Count == 12 && drawing.NodeRects.Values.Select(rect => rect.Position.Y).Distinct().Count() == 1,
                    "twelve authored nodes still fit one axis " + locale);
                foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.EnsureControlVisible(drawing);
                await Frames();
                Vector2 start = drawing.GetGlobalTransformWithCanvas() * drawing.PaletteRects[StandardMapRoomType.RestSite].GetCenter();
                Vector2 end = drawing.GetGlobalTransformWithCanvas() * drawing.AxisRect.GetCenter();
                EngineInput(new InputEventMouseMotion { Position = start, GlobalPosition = start }); await Frames(1);
                EngineInput(new InputEventMouseButton { Position = start, GlobalPosition = start, ButtonIndex = MouseButton.Left,
                    ButtonMask = MouseButtonMask.Left, Pressed = true }); await Frames(1);
                EngineInput(new InputEventMouseMotion { Position = end, GlobalPosition = end, ButtonMask = MouseButtonMask.Left }); await Frames();
                var ghost = Find<TextureRect>(drawing, "MapNodeDragPreview");
                Check(ghost.Visible && ghost.Texture == drawing.RoomArt[StandardMapRoomType.RestSite] && ghost.Size == new Vector2(26, 26),
                    "native mouse drag shows actual node icon " + locale);
                await Capture("drag-" + locale);
                EngineInput(new InputEventMouseButton { Position = end, GlobalPosition = end, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
                Check(editor.ExportMapState().Acts.Single(act => act.Act == 1).Nodes.Count == 13,
                    "icon drag still inserts one node " + locale);

                frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Ordered, [first, second]) } }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "relics"); await Frames();
                await Capture("treasure-" + locale);
                await Press(relics, "TreasureWindowFirst");
                Check(relics.FindChild("TreasureRetainedTargets", true, false) is null && relics.FindChild("TreasureTargetSlot2", true, false) is null,
                    "out-of-range draft text and tile are hidden " + locale);
                var dormant = frame.CaptureDraft(); store.SaveWorkbench(dormant); store.FlushAll();
                var loaded = new SearchWorkspace(stateRoot, false).LoadWorkbench()!;
                Check(loaded.Query.TreasureRelics!.Slots.SequenceEqual(new ModelKey?[] { first, second }) && loaded.Query.TreasureRelics.Count == 1,
                    "hidden target survives actual save/reload " + locale);
                await Capture("treasure-hidden-" + locale);
                await Press(relics, "TreasureClearTarget1"); await Press(relics, "TreasureChooseTarget1");
                var picker = Field<ObjectPickerPanel>(relics, "_picker");
                var names = RuntimeGameContentNameResolver.Create(locale);
                string firstName = names.Resolve(first, GameContentKind.Relic), secondName = names.Resolve(second, GameContentKind.Relic);
                Button? Candidate(string name) => Nodes(picker).OfType<Button>().FirstOrDefault(button => button.GetChildren().OfType<Label>().Any(label => label.Text == name));
                Check(picker.Visible && Candidate(secondName) is null && Candidate(firstName) is not null,
                    "picker excludes the other hidden slot's relic " + locale);
                Candidate(firstName)!.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(frame.CaptureDraft().Query.TreasureRelics!.Slots.SequenceEqual(new ModelKey?[] { first, second }),
                    "explicit new choice preserves the independent hidden target " + locale);
                await Press(relics, "TreasureWindowBoth"); await Press(relics, "TreasureOrderMode");
                var active = frame.CaptureDraft().Query.TreasureRelics!;
                Check(active.Count == 2 && active.OrderMode == TreasureRelicOrderMode.Unordered && active.Slots[1] == second,
                    "range restore and independent Ordered toggle preserve targets " + locale);
                frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Ordered, [first, first]) } }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "relics"); await Frames();
                Check(Find<Label>(relics, "TreasureDuplicateTargets").Text == text.Get("query.treasure.duplicate_targets") &&
                    frame.CaptureDraft().Query.TreasureRelics!.Slots.SequenceEqual(new ModelKey?[] { first, first }),
                    "existing duplicate is visible and preserved without auxiliary failure " + locale);
                await Capture("treasure-duplicate-" + locale);
                await Press(relics, "TreasureClearTarget2");
                Check(relics.FindChild("TreasureDuplicateTargets", true, false) is null && frame.CaptureDraft().Query.TreasureRelics!.Slots[0] == first,
                    "explicit edit resolves duplicate without deleting independent target " + locale);

                // Exercise actual narrow logical dimensions, rather than the shell's fit scale.
                frame.RestoreDraft(empty, render: false); frame.Refresh(locale, text); Call(frame, "SelectDomain", "boss"); await Frames();
                Node parent = editor.GetParent(); Vector2 position = editor.Position, size = editor.Size;
                var compactHost = new Panel { Size = new(512, 720), Theme = shell.Theme };
                root.AddChild(compactHost); shell.Hide(); editor.Reparent(compactHost);
                try
                {
                    root.GetWindow().Size = new(512, 720); root.GetWindow().ContentScaleSize = new(512, 720);
                    editor.Position = new(16, 12); editor.Size = new(480, 696); editor.Show();
                    editor.Refresh(locale, text, empty.Character, 10, 1, 0); await Frames();
                    Button last = Nodes(editor).OfType<Button>().Last(button => button.Name.ToString().StartsWith("SecondBoss3_", StringComparison.Ordinal));
                    var scroll = Nodes(editor).OfType<ScrollContainer>().Single(); scroll.EnsureControlVisible(last); await Frames();
                    Check(scroll.GetGlobalRect().Encloses(last.GetGlobalRect()), "narrow window can scroll to final second Boss " + locale);
                    await Capture("boss-narrow-" + locale);
                }
                finally
                {
                    editor.Reparent(parent); editor.Position = position; editor.Size = size; compactHost.Free(); shell.Show();
                    root.GetWindow().Size = new(1600, 900); root.GetWindow().ContentScaleSize = new(1600, 900); await Frames();
                }
            }
            File.WriteAllText(Path.Combine(output, "owner-polish-editors.json"), JsonSerializer.Serialize(new { checks, screenshots }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"OWNER_POLISH_EDITORS_PASS checks={checks};screenshots={screenshots.Count}");
        }
        finally
        {
            host.Free(); root.GetWindow().ContentScaleMode = previousScaleMode; root.GetWindow().ContentScaleFactor = previousScale;
            root.GetWindow().ContentScaleSize = previousScaleSize; root.GetWindow().Size = previousSize;
        }
    }
}
