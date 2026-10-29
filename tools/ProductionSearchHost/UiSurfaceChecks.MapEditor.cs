using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunMapEditorChecksAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, members)!.GetValue(owner)!;
        void Call(object owner, string method, params object?[] args) => owner.GetType().GetMethod(method, members)!.Invoke(owner, args);
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("MAP_EDITOR " + why); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        IEnumerable<Node> Nodes(Node node)
        { yield return node; foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested; }
        var previousSize = root.GetWindow().Size;
        var previousScaleSize = root.GetWindow().ContentScaleSize;
        var previousScaleMode = root.GetWindow().ContentScaleMode;
        float previousScaleFactor = root.GetWindow().ContentScaleFactor;
        root.GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.GetWindow().ContentScaleFactor = 1;
        root.GetWindow().ContentScaleSize = new(1600, 1100);
        root.GetWindow().Size = new(1600, 1100);
        var host = new Control { Size = new(1600, 1100) };
        root.AddChild(host);
        var shell = new WorkspaceShell();
        string stateRoot = Path.Combine(output, "map-editor-state");
        shell.InitializeWithPersistence(runtime, stateRoot); host.AddChild(shell);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var editor = Field<ActInformationSearchEditor>(frame, "_actInformationEditor");
        T Control<T>(string name) where T : Node => (T)editor.FindChild(name, true, false)!;
        MapRouteDrawingControl Drawing() => Control<MapRouteDrawingControl>("MapRouteDrawing");
        MapActEditorStateSnapshot Act(int act = 1) => editor.ExportMapState().Acts.Single(a => a.Act == act);
        void InputEvent(InputEvent input) { Input.ParseInputEvent(input); Input.FlushBufferedEvents(); }
        async Task Move(Vector2 point, MouseButtonMask mask = 0)
        { InputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point, ButtonMask = mask }); await Frames(1); }
        async Task Click(Vector2 point, MouseButton button = MouseButton.Left)
        {
            await Move(point);
            InputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = true,
                ButtonMask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right });
            await Frames(1);
            InputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = false });
            await Frames();
        }
        async Task ClickControl(string name)
        {
            var control = Control<Godot.Control>(name);
            foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.EnsureControlVisible(control);
            await Frames();
            Check(control.IsVisibleInTree(), "native control visible " + name);
            await Click(control.GetGlobalRect().GetCenter());
        }
        async Task Drag(MapRouteDrawingControl drawing, Vector2 start, Vector2 end)
        {
            Vector2 origin = drawing.GetGlobalTransformWithCanvas() * start;
            Vector2 target = drawing.GetGlobalTransformWithCanvas() * end;
            await Move(origin);
            InputEvent(new InputEventMouseButton { Position = origin, GlobalPosition = origin, ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left, Pressed = true });
            await Frames(1);
            for (int i = 1; i <= 4; i++) await Move(origin.Lerp(target, i / 4f), MouseButtonMask.Left);
            InputEvent(new InputEventMouseButton { Position = target, GlobalPosition = target, ButtonIndex = MouseButton.Left, Pressed = false });
            await Frames();
        }
        async Task Add(StandardMapRoomType room)
        {
            var drawing = Drawing();
            var nodes = Act().Nodes;
            Vector2 point = nodes.Count == 0 ? drawing.AxisRect.GetCenter() : drawing.NodeRects[nodes[^1].Id].End + new Vector2(8, -20);
            await Drag(drawing, drawing.PaletteRects[room].GetCenter(), point);
            Check(Act().Nodes.Count == nodes.Count + 1 && Act().Nodes[^1].Room == room, "palette drag inserts one authored room " + room);
        }
        async Task Box(Vector2 start, Vector2 end) => await Drag(Drawing(), start, end);
        async Task Screenshot(string name)
        {
            foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.ScrollVertical = 0;
            Call(frame, "UpdateDraftStatus");
            for (int wait = 0; wait < 600 && Field<bool>(frame, "_probabilityPending"); wait++) await Frames(1);
            Check(!Field<bool>(frame, "_probabilityPending"), "current probability presentation settled " + name);
            await Frames(6); RenderingServer.ForceDraw();
            Check(editor.IsVisibleInTree(), "map editor stays visible " + name);
            foreach (var scroll in Nodes(editor).OfType<ScrollContainer>())
                Check(scroll.GetHScrollBar().MaxValue <= scroll.GetHScrollBar().Page + 2, "no horizontal overflow " + name);
            var left = editor.FindChild("MapPropertyNumberGuaranteedElite", true, false) as Godot.Control;
            var right = editor.FindChild("MapPropertyNumberReachableMaxElite", true, false) as Godot.Control;
            if (left is not null && right is not null)
            {
                float contentWidth = ((Godot.Control)left.GetParent()).Size.X;
                if (Math.Abs(left.Position.Y - right.Position.Y) < 1)
                    Check(left.GetRect().End.X <= (contentWidth - 20) / 2 + 1, "native numeric minimum does not enter the other property column " + name);
                Check(right.GetRect().End.X <= contentWidth + 1, "native numeric minimum stays inside property content " + name);
            }
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + ".png"));
            foreach (var scroll in Nodes(editor).OfType<ScrollContainer>())
            {
                if (scroll.GetVScrollBar().MaxValue <= scroll.GetVScrollBar().Page + 2) continue;
                scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
                await Frames(); RenderingServer.ForceDraw();
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + "-tail.png"));
                scroll.ScrollVertical = 0;
                await Frames();
            }
        }
        void Option(string name, int value)
        { var option = Control<OptionButton>(name); option.Select(value); option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)value); }
        async Task TypeNumber(string name, int value)
        {
            var edit = Nodes(Control<Node>(name)).OfType<LineEdit>().Single();
            foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.EnsureControlVisible(edit);
            await Frames(); await Click(edit.GetGlobalRect().GetCenter());
            async Task KeyEvent(Key key, bool pressed, bool ctrl = false, uint unicode = 0)
            {
                root.GetViewport().PushInput(new InputEventKey
                    { Keycode = key, PhysicalKeycode = key, KeyLabel = key, Pressed = pressed, CtrlPressed = ctrl, Unicode = unicode });
                await Frames(1);
            }
            await KeyEvent(Key.A, true, true); await KeyEvent(Key.A, false, true);
            foreach (char digit in value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            { await KeyEvent((Key)digit, true, unicode: digit); await KeyEvent((Key)digit, false); }
            await KeyEvent(Key.Enter, true); await KeyEvent(Key.Enter, false); await Frames();
            bool matches = int.TryParse(edit.Text, out int actual) && actual == value;
            if (!matches) { RenderingServer.ForceDraw(); root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "map-input-failure.png")); }
            Check(matches, "native direct integer input " + name + ";expected=" + value + ";actual=" + edit.Text +
                ";focus=" + root.GetViewport().GuiGetFocusOwner()?.GetPath());
        }
        async Task StepNumber(string name, bool increase)
        {
            var button = Nodes(Control<Node>(name)).OfType<Button>().Single(button => increase ? button.Text == "+" : button.Text is "−" or "-");
            await Click(button.GetGlobalRect().GetCenter());
        }
        try
        {
            shell.Open(); await AwaitPageReady(root, shell); await Frames();
            var initial = frame.CaptureDraft();
            var empty = new WorkbenchSearchDraft(initial.Character, initial.Ascension, SearchQuery.Empty, initial.AncientPremises);
            frame.RestoreDraft(empty);
            Call(frame, "SelectDomain", "map"); await Frames();
            root.GetViewport().NotifyMouseEntered();
            Check(!store.Preferences.MapLossyEnabled && !Control<CheckButton>("MapLossyEnabled").ButtonPressed,
                "lossy map prefilter defaults disabled");
            await ClickControl("MapLossyEnabled");
            Check(store.Preferences.MapLossyEnabled && new SearchWorkspace(stateRoot, false).Preferences.MapLossyEnabled,
                "map toggle persists lossy preference");
            await ClickControl("MapLossyEnabled");
            await ClickControl("MapScope1");
            Check(Enumerable.Range(0, 4).All(i => Control<Button>("MapScope" + i).IsVisibleInTree()), "all four scopes retained");
            Check(Nodes(editor).OfType<OptionButton>().Count(c => c.Name.ToString().StartsWith("MapPropertyComparison")) == 9,
                "daily nine independent properties are directly expanded");
            Option("MapRouteTarget", 1); await Frames(); await TypeNumber("MapRouteTargetValue", 3);
            await StepNumber("MapRouteTargetValue", true);
            Check(frame.CaptureDraft().Query.StandardMaps.Single(c => c.Scope == 1 && c.RouteObjective).Value == 4, "native plus updates active route quantity");
            await StepNumber("MapRouteTargetValue", false);
            Check(frame.CaptureDraft().Query.StandardMaps.Single(c => c.Scope == 1 && c.RouteObjective).Value == 3, "native minus updates active route quantity");
            await TypeNumber("MapRouteTargetValue", 0); await StepNumber("MapRouteTargetValue", false);
            Check(frame.CaptureDraft().Query.StandardMaps.Single(c => c.Scope == 1 && c.RouteObjective).Value == 0, "native minus keeps zero boundary");
            await TypeNumber("MapRouteTargetValue", 100);
            Check(frame.CaptureDraft().Query.StandardMaps.Single(c => c.Scope == 1 && c.RouteObjective).Value == 100, "direct input is not capped at example thresholds");
            await TypeNumber("MapRouteTargetValue", int.MaxValue); await StepNumber("MapRouteTargetValue", true);
            Check(frame.CaptureDraft().Query.StandardMaps.Single(c => c.Scope == 1 && c.RouteObjective).Value == int.MaxValue,
                "native plus keeps integer boundary without overflow");
            await TypeNumber("MapRouteTargetValue", 3);
            Option("MapPropertyComparisonGuaranteedElite", 1); await Frames();
            await ClickControl("MapScope0"); Option("MapRouteTarget", 1); await Frames();
            await TypeNumber("MapRouteTargetValue", 7);
            await ClickControl("MapScope1");
            await ClickControl("MapDrawingMode");
            Check(Act().DrawingMode && Act().Daily.Minimum == 3 && frame.CaptureDraft().Query.StandardMaps.All(c => !c.RouteObjective || c.Scope == 0),
                "drawing suspends daily route and retains total + daily draft");
            Check(Drawing().RoomArt.Count == 5 && Drawing().MissingArtRooms.Count == 0 &&
                Nodes(Drawing()).OfType<TextureRect>().Count(texture => texture.Texture is not null) >= 7,
                "native palette and fixed endpoints use all five real game room textures");
            foreach (var art in Nodes(Drawing()).OfType<TextureRect>())
                Check(art.Size.IsEqualApprox(new Vector2(26, 26)), "room art uses compact 26-pixel layout bound " + art.Name + ":" + art.Size);
            foreach (var room in new[] { StandardMapRoomType.Elite, StandardMapRoomType.RestSite, StandardMapRoomType.Shop, StandardMapRoomType.Unknown }) await Add(room);
            var original = Act().Nodes.Select(n => n.Id).ToArray();
            var drawing = Drawing();
            Rect2 first = drawing.NodeRects[original[0]], second = drawing.NodeRects[original[1]];
            await Box(first.Position - new Vector2(5, 5), second.End + new Vector2(5, 5));
            Check(Act().Nodes.Take(2).Select(n => n.GroupId).Distinct().Count() == 1 && Act().Nodes[0].GroupId != 0,
                "native blank-area box creates unordered group");
            drawing = Drawing();
            Rect2 group = drawing.GroupRects[Act().Nodes[0].GroupId], third = drawing.NodeRects[original[2]];
            await Box(new Vector2(group.GetCenter().X, group.Position.Y - 5), third.End + new Vector2(5, 5));
            Check(Act().Nodes.Take(3).Select(n => n.GroupId).Distinct().Count() == 1 && Act().Nodes[0].GroupId != 0 && Act().Nodes[3].GroupId == 0,
                "box touching part of old group merges its complete membership without nesting");
            drawing = Drawing();
            await Click(drawing.GetGlobalTransformWithCanvas() * drawing.GroupRects[Act().Nodes[0].GroupId].GetCenter(), MouseButton.Right);
            Check(Act().Nodes.All(n => n.GroupId == 0) && Act().Nodes.Select(n => n.Id).SequenceEqual(original), "native right-click ungroups in added order");
            drawing = Drawing(); first = drawing.NodeRects[original[0]]; second = drawing.NodeRects[original[1]];
            await Box(first.Position - new Vector2(5, 5), second.End + new Vector2(5, 5));
            drawing = Drawing();
            await Drag(drawing, drawing.NodeRects[original[0]].GetCenter(), new Vector2(drawing.AxisRect.GetCenter().X, drawing.AxisRect.End.Y + 18));
            Check(Act().Nodes.Count == 3 && Act().Nodes.All(n => n.Id != original[0]) && Act().Nodes.Single(n => n.Id == original[1]).GroupId == 0,
                "dragging one group member out deletes only it and dissolves singleton");
            drawing = Drawing();
            var last = Act().Nodes[^1]; var firstRemaining = drawing.NodeRects[Act().Nodes[0].Id];
            await Drag(drawing, drawing.NodeRects[last.Id].GetCenter(), firstRemaining.Position + new Vector2(-8, 20));
            Check(Act().Nodes[0].Id == last.Id && Act().Nodes.Count == 3, "native node drag moves only the selected node");
            drawing = Drawing();
            first = drawing.NodeRects[Act().Nodes[0].Id]; second = drawing.NodeRects[Act().Nodes[1].Id];
            await Box(first.Position - new Vector2(5, 5), second.End + new Vector2(5, 5));
            drawing = Drawing();
            await Click(drawing.GetGlobalTransformWithCanvas() * drawing.GroupRects[Act().Nodes[0].GroupId].GetCenter(), MouseButton.Right);
            Check(Act().Nodes.Take(2).Select(n => n.AddedOrder).SequenceEqual(new long[] { 2, 4 }),
                "right-click restores added order even after authored nodes were moved");
            drawing = Drawing(); first = drawing.NodeRects[Act().Nodes[0].Id]; second = drawing.NodeRects[Act().Nodes[1].Id];
            await Box(first.Position - new Vector2(5, 5), second.End + new Vector2(5, 5));

            var saved = frame.CaptureDraft();
            string savedJson = JsonSerializer.Serialize(saved, ProductionSearchReplay.JsonOptions);
            File.WriteAllText(Path.Combine(output, "map-editor-draft.json"), savedJson);
            store.SaveWorkbench(saved); store.FlushAll();
            var loaded = new SearchWorkspace(stateRoot, false).LoadWorkbench()!;
            Check(JsonSerializer.Serialize(loaded.MapEditor) == JsonSerializer.Serialize(saved.MapEditor),
                "actual workspace save and reload preserves both map drafts and current mode");
            frame.RestoreDraft(loaded);
            Call(frame, "SelectDomain", "map"); await Frames();
            Check(Act().DrawingMode && Act().Daily.Minimum == 3 && Act().Nodes.Select(n => n.Id).SequenceEqual(saved.MapEditor!.Acts[0].Nodes.Select(n => n.Id)),
                "actual frame JSON restore retains current mode and both drafts including added identity");
            string activeQuery = JsonSerializer.Serialize(frame.CaptureDraft().Query);
            var semanticContext = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
                ProjectSettings.GlobalizePath("res://Corpus/v1/context.json")).Context;
            string activeIdentity = SearchCompiler.Compile(frame.CaptureDraft().Query, semanticContext).SemanticFingerprint;
            var inactiveEdit = saved with { MapEditor = saved.MapEditor! with { Acts = saved.MapEditor.Acts.Select(a => a.Act == 1 ? a with { Daily = a.Daily with { Minimum = 11 } } : a).ToArray() } };
            frame.RestoreDraft(inactiveEdit); Call(frame, "SelectDomain", "map"); await Frames();
            Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == activeQuery, "inactive daily edits do not affect active Search query");
            Check(SearchCompiler.Compile(frame.CaptureDraft().Query, semanticContext).SemanticFingerprint == activeIdentity,
                "inactive draft edits preserve semantic and continuation query identity");
            await ClickControl("MapDailyMode");
            Check(!Act().DrawingMode && frame.CaptureDraft().Query.StandardMaps.Any(c => c.Scope == 1 && c.RouteObjective && c.Value == 11) &&
                frame.CaptureDraft().Query.StandardMapRoutes.Count == 0 && Act().Nodes.Count == 3, "return to daily activates retained draft and preserves drawing");
            await ClickControl("MapDrawingMode");

            // Density is a layout target, never a limit on authored requests.
            var compactDraft = frame.CaptureDraft();
            var twelve = Enumerable.Range(1, 12).Select(index => new MapDrawingNodeSnapshot(index,
                new[] { StandardMapRoomType.Monster, StandardMapRoomType.Elite, StandardMapRoomType.RestSite,
                    StandardMapRoomType.Shop, StandardMapRoomType.Unknown }[(index - 1) % 5], 0, index)).ToArray();
            frame.RestoreDraft(compactDraft with
            {
                Query = compactDraft.Query with
                {
                    StandardMapRoutes = compactDraft.Query.StandardMapRoutes.Where(route => route.Act != 1)
                        .Append(new StandardMapRouteCondition(1, twelve.Select(node => new StandardMapRouteGroup([node.Room])).ToArray())).ToArray()
                },
                MapEditor = compactDraft.MapEditor! with
                { Acts = compactDraft.MapEditor.Acts.Select(act => act.Act == 1 ? act with { Nodes = twelve } : act).ToArray() }
            });
            Call(frame, "SelectDomain", "map"); await Frames();
            Check(Drawing().NodeRects.Count == 12 && Drawing().NodeRects.Values.Select(rect => rect.Position.Y).Distinct().Count() == 1,
                "standard editor places twelve authored nodes on one axis row; width=" + Drawing().Size.X);
            await ClickControl("MapLossyEnabled");

            // Both locales, actual wide/narrow workspace, both accepted modes.
            foreach (string language in new[] { "zh", "en" })
            {
                store.SetLanguageOverride(language); Call(shell, "RefreshLanguage"); frame.Refresh(language, JsonUiTextProvider.CreateUi13(language));
                Call(frame, "SelectDomain", "map"); await Frames();
                foreach (var size in new[] { new Vector2I(1600, 1000), new Vector2I(1000, 800) })
                {
                    root.GetWindow().ContentScaleSize = size; root.GetWindow().Size = size; host.Size = size; await Frames(8);
                    Check(root.GetViewport().GetVisibleRect().Size.IsEqualApprox(size), "real logical viewport exercises requested layout width " + size +
                        ";actual=" + root.GetViewport().GetVisibleRect().Size + ";window=" + root.GetWindow().Size + ";scale=" + root.GetWindow().ContentScaleSize);
                    await Screenshot($"map-drawing-{language}-{size.X}");
                    await ClickControl("MapDailyMode");
                    Check(Nodes(editor).OfType<OptionButton>().Count(c => c.Name.ToString().StartsWith("MapPropertyComparison")) == 9,
                        "all properties remain expanded " + language + " " + size.X);
                    await Screenshot($"map-daily-{language}-{size.X}");
                    await ClickControl("MapDrawingMode");
                }
            }
            await ClickControl("MapLossyEnabled");
            // WorkspaceShell intentionally fits a fixed 1600x900 canvas to a
            // natural narrow window. Exercise the reusable editor's actual
            // narrow layout separately, on the same native node and drafts.
            var editorParent = editor.GetParent();
            Vector2 editorPosition = editor.Position, editorSize = editor.Size;
            var compactHost = new Panel { Size = new(512, 1000), Theme = shell.Theme };
            compactHost.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("091222") });
            root.AddChild(compactHost);
            shell.Hide(); editor.Reparent(compactHost);
            try
            {
                root.GetWindow().ContentScaleSize = new(512, 1000); root.GetWindow().Size = new(512, 1000);
                editor.Position = new(16, 12); editor.Size = new(480, 976); editor.Show();
                foreach (string language in new[] { "zh", "en" })
                {
                    editor.Refresh(language, JsonUiTextProvider.CreateUi13(language), empty.Character, empty.Ascension, 1, 0);
                    await Frames(8);
                    Check(Math.Abs(editor.Size.X - 480) < 1, "component fixture uses actual 480 logical pixels " + language);
                    Check(Drawing().NodeRects.Count == 12 && Drawing().NodeRects.Values.All(rect => rect.Position.X >= 0 && rect.End.X <= Drawing().Size.X),
                        "all twelve compact nodes wrap inside the real narrow component " + language);
                    await Screenshot($"map-component-drawing-{language}-480");
                    foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.EnsureControlVisible(Drawing());
                    await Frames();
                    await Click(Drawing().GetGlobalTransformWithCanvas() * Drawing().PaletteRects[StandardMapRoomType.Shop].GetCenter());
                    Check(Act().Nodes.Count == 13 && Drawing().NodeRects.Count == 13 &&
                        Drawing().NodeRects.Values.All(rect => rect.End.X <= Drawing().Size.X),
                        "thirteenth node remains editable at actual narrow width;ActualActCount=" + Act().Nodes.Count + ";RectCount=" + Drawing().NodeRects.Count);
                    await Screenshot($"map-component-drawing-13-{language}-480");
                    foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.EnsureControlVisible(Drawing());
                    await Frames();
                    var thirteenth = Drawing().NodeRects[Act().Nodes[^1].Id];
                    // The palette-to-axis gap is outside the axis and stays in
                    // view even when the editor's property tail is scrollable.
                    var drop = new Vector2(Drawing().AxisRect.GetCenter().X, Drawing().AxisRect.Position.Y - 12);
                    var transform = Drawing().GetGlobalTransformWithCanvas();
                    Check(root.GetViewport().GetVisibleRect().HasPoint(transform * thirteenth.GetCenter()) &&
                        root.GetViewport().GetVisibleRect().HasPoint(transform * drop), "native overflow deletion endpoints stay within the actual viewport");
                    await Drag(Drawing(), thirteenth.GetCenter(), drop);
                    Check(Act().Nodes.Count == 12, "overflow node retains native drag-out deletion");
                    await ClickControl("MapDailyMode");
                    var minimum = Control<Godot.Control>("MapPropertyNumberGuaranteedElite");
                    var maximum = Control<Godot.Control>("MapPropertyNumberReachableMaxElite");
                    Check(maximum.Position.Y > minimum.Position.Y + 100 &&
                        Nodes(editor).OfType<OptionButton>().Count(c => c.Name.ToString().StartsWith("MapPropertyComparison")) == 9,
                        "real narrow component lays all nine properties in one column " + language);
                    foreach (var control in Nodes(editor).OfType<Godot.Control>().Where(c => c.Name.ToString().StartsWith("MapProperty") ||
                        c.Name.ToString().StartsWith("MapRouteTarget") || c.Name == "MapRouteNode"))
                        Check(control.GetRect().Position.X >= 0 && control.GetRect().End.X <= ((Godot.Control)control.GetParent()).Size.X + 1,
                            "native narrow control fits content " + language + " " + control.Name);
                    await Screenshot($"map-component-daily-{language}-480");
                    await ClickControl("MapDrawingMode");
                }
            }
            finally
            {
                editor.Reparent(editorParent); editor.Position = editorPosition; editor.Size = editorSize;
                compactHost.Free(); shell.Show();
            }
            root.GetWindow().ContentScaleSize = new(1600, 1000); root.GetWindow().Size = new(1600, 1000); host.Size = new(1600, 1000); await Frames();
            frame.RestoreDraft(compactDraft); Call(frame, "SelectDomain", "map"); await Frames();
            Call(shell, "OpenSettings"); await AwaitPageReady(root, shell);
            Call(shell, "SelectSettingsCategory", 2); await AwaitPageReady(root, shell); await Frames();
            var batch = (OptionButton)shell.FindChild("SettingsMapLossyGpuBatchSize", true, false)!;
            Check(batch.ItemCount == 4 && batch.GetItemId(batch.Selected) == 16384, "advanced map setting exposes four real batch capacities and default");
            batch.Select(2); batch.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            Check(store.Preferences.MapLossyGpuBatchSize == 32768 && new SearchWorkspace(stateRoot, false).Preferences.MapLossyGpuBatchSize == 32768,
                "map GPU batch choice persists");
            var mapSettingsScroll = (ScrollContainer)shell.FindChild("SettingsCategoryPage2", true, false)!;
            mapSettingsScroll.EnsureControlVisible(batch); await Frames(); RenderingServer.ForceDraw();
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "map-lossy-settings.png"));
            ((Button)shell.FindChild("SettingsMapLossyReset", true, false)!).EmitSignal(Button.SignalName.Pressed);
            Check(store.Preferences.MapLossyGpuBatchSize == 16384 && !store.Preferences.MapLossyEnabled,
                "map lossy reset restores safe defaults");
            Call(shell, "CloseSettings"); await Frames();
            await ClickControl("MapScope2"); await ClickControl("MapDrawingMode");
            await Click(Drawing().GetGlobalTransformWithCanvas() * Drawing().PaletteRects[StandardMapRoomType.Shop].GetCenter());
            Check(Act(2).Nodes.Count == 1, "other Act owns its own drawing");
            await ClickControl("MapScope1"); await ClickControl("MapDrawingClear");
            var cleared = frame.CaptureDraft();
            Check(Act().Nodes.Count == 0 && Act(2).Nodes.Count == 1 && Act().Daily.Minimum == 11 &&
                cleared.Query.StandardMaps.Any(c => c.Scope == 0 && c.RouteObjective) && cleared.Query.StandardMaps.Any(c => !c.RouteObjective),
                "clear only current Act drawing; preserve fixed structure, other Act, daily, total and independent properties");
            // Reuse this initialized, isolated workspace and existing native captures;
            // no lobby reader or new runtime fixture is needed for the legacy seam.
            var partyDraft = empty with
            {
                Version = 4, Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer,
                GameVersion = runtime.Detection.NormalizedVersion, Profile = runtime.Profile.ProfileId,
                Players = Enumerable.Range(0, 2).Select(slot => new WorkbenchPlayerDraft(slot, empty.Character,
                    MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray(),
                Query = SearchQuery.Empty with { Players = Enumerable.Range(0, 2).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                { Conditions = slot == 1 ? SearchQuery.Empty with { StandardMapRoutes = [new(1, [new([StandardMapRoomType.Shop])])] } : SearchQuery.Empty }).ToArray() }
            };
            store.SaveWorkbench(partyDraft);
            frame.RestoreDraft(new SearchWorkspace(stateRoot, false).LoadWorkbench()!);
            var restoredParty = frame.CaptureDraft();
            Check(restoredParty.Query.StandardMapRoutes.Count == 1 && restoredParty.Query.StandardMapRoutes[0].Groups[0].Nodes.SequenceEqual(new[] { StandardMapRoomType.Shop }) &&
                restoredParty.Query.Players.All(player => player.Conditions.StandardMapRoutes.Count == 0),
                "legacy saved personal map route restores and captures once at shared table level");
            File.WriteAllText(Path.Combine(output, "map-editor.json"), JsonSerializer.Serialize(new { Passed = checks, NativeGestures = true,
                Languages = new[] { "zh", "en" }, Widths = new[] { 1600, 1000 }, ComponentLogicalWidth = 480,
                NaturalWindowUsesWorkspaceFitScale = true, Modes = new[] { "daily", "drawing" } }));
            GD.Print($"MAP_EDITOR_UI_PASS checks={checks};gestures=engine-input;locales=zh,en;widths=1600,1000;modes=daily,drawing;draft=frame-json");
        }
        finally { host.Free(); root.GetWindow().ContentScaleMode = previousScaleMode; root.GetWindow().ContentScaleFactor = previousScaleFactor;
            root.GetWindow().ContentScaleSize = previousScaleSize; root.GetWindow().Size = previousSize; }
    }
}
