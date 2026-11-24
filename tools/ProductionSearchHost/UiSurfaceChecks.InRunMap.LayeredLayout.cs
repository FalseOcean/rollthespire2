using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Actual Godot input/rendering in an injected-pointer viewport. Only the
    // texture is borrowed; native map point/travel semantics belong to the outer fixture.
    private static async Task CheckInRunMapLayeredLayout(Node root, string output,
        InRunMapNodeDetails product, NMapPoint nativePoint, InRunMapNodeDetailData combat,
        InRunMapNodeDetailData eventData, InRunMapNodeDetailData alternativeData,
        InRunMapNodeDetailData stagedEvent, InRunMapNodeDetailData triggeredCombat, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var icons = (IGameIconResolver)typeof(InRunMapNodeDetails).GetField("_icons", members)!.GetValue(product)!;
        var names = (IGameContentNameResolver)typeof(InRunMapNodeDetails).GetField("_names", members)!.GetValue(product)!;
        var viewport = new SubViewport { Name = "MapLayeredPointerViewport", Size = new(1680, 1080),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = true };
        root.AddChild(viewport); viewport.NotifyMouseEntered();
        int blankClicks = 0, sourceClicks = 0;
        var paper = new ColorRect { Color = new("a9a28e"), Size = new(1680, 1080), MouseFilter = Control.MouseFilterEnum.Stop };
        paper.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) blankClicks++; };
        viewport.AddChild(paper);
        var source = new Control { Name = "NativeTextureSource", Position = new(140, 230), Size = new(56, 56),
            ZIndex = 3, ZAsRelative = true, MouseFilter = Control.MouseFilterEnum.Stop };
        var nativeIcon = Descendants(nativePoint).OfType<TextureRect>().First(node => node.Name == "Icon");
        source.AddChild(new TextureRect { Texture = nativeIcon.Texture, Position = new(-18, -18), Size = new(92, 92),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore });
        viewport.AddChild(source);
        var view = new InRunMapNodeDetails(icons, names); viewport.AddChild(view);
        InRunMapNodeDetailData data = combat;
        var bounds = new Rect2(Vector2.Zero, new(1680, 1080));
        source.MouseEntered += () => { if (view.CanOpenNode(data.NodeId)) view.ShowNode(data, source.GetGlobalRect(), bounds, source); };
        source.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) sourceClicks++; };
        var requested = new List<(string Node, string Choice)>();
        long fixtureRevision = 0;
        bool publishRevisionOnClick = false;
        view.ChoiceRequested += (node, choice) =>
        {
            requested.Add((node, choice));
            if (!publishRevisionOnClick) return;
            long revision = ++fixtureRevision;
            // A view-only receipt fixture: the outer Information flow exercises
            // actual Presenter/Session.Take recomputation. Here a real pointer
            // choice publishes a fresh revision synchronously, like that caller.
            data = data with { IsCurrent = () => fixtureRevision == revision,
                Sections = data.Sections.Select(section => section with {
                    Choices = section.Choices.Select(item => item with { Id = item.Id + "@" + revision }).ToArray()
                }).ToArray() };
            view.ShowNode(data, source.GetGlobalRect(), bounds, source);
        };
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Move(Vector2 position) => viewport.PushInput(new InputEventMouseMotion
            { Position = position, GlobalPosition = position }, true);
        void Click(Vector2 position, MouseButton button = MouseButton.Left)
        {
            Move(position);
            viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = false }, true);
        }
        async Task AwaitPointerExit(Vector2 position)
        {
            Move(position);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            do { await Frames(1); }
            while (view.HoverOpen && System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 400);
        }
        Button Choice(string id) => Descendants(view).OfType<Button>().Single(button => button.IsVisibleInTree() &&
            button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == id);
        Control Named(string name) => (Control)view.FindChild(name, true, false)!;
        float NaturalTextWidth(Button button) => button.GetThemeFont("font").GetStringSize(button.Text,
            fontSize: button.GetThemeFontSize("font_size")).X + button.GetThemeStylebox("normal").GetMinimumSize().X;
        bool UntrimmedTextFits(Button button) => !string.IsNullOrWhiteSpace(button.Text) && !button.ClipText &&
            button.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming && button.Size.X + .5f >= NaturalTextWidth(button);
        async Task Picture(string name)
        {
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = viewport.GetTexture().GetImage(); image.SavePng(Path.Combine(output, name));
        }
        async Task<string> DragHorizontal(ScrollContainer scroll, string scenario)
        {
            // Keep real native input, including the original same-frame drag.
            // Capture both container settling and GUI delivery before judging a
            // failure: a range alone does not prove the bar can receive input.
            var bar = scroll.GetHScrollBar();
            var samples = new List<object>(); var delivered = new List<object>();
            var values = new List<double>();
            void Input(InputEvent input)
            {
                delivered.Add(new { Type = input.GetClass(), Text = input.AsText(),
                    Position = input is InputEventMouse mouse ? mouse.Position.ToString() : null,
                    Value = bar.Value });
            }
            void Value(double value) => values.Add(value);
            object ControlGeometry(Control control) => new {
                Path = control.GetPath().ToString(), Rect = control.GetGlobalRect().ToString(),
                Minimum = control.GetCombinedMinimumSize().ToString(), CustomMinimum = control.CustomMinimumSize.ToString(),
                Visible = control.Visible, TreeVisible = control.IsVisibleInTree(),
                MouseFilter = control.MouseFilter.ToString(), Clip = control.ClipContents,
                Scale = control.GetGlobalTransform().Scale.ToString() };
            object Snapshot(string stage) => new {
                Stage = stage, Processing = view.IsProcessing(), Open = view.HoverOpen,
                Requested = requested.Count, Pointer = viewport.GetMousePosition().ToString(),
                Hovered = viewport.GuiGetHoveredControl()?.GetPath().ToString(),
                Scroll = ControlGeometry(scroll), scroll.ScrollHorizontal, scroll.ScrollVertical,
                HorizontalMode = scroll.HorizontalScrollMode.ToString(), VerticalMode = scroll.VerticalScrollMode.ToString(),
                Bar = ControlGeometry(bar), bar.Value, bar.MinValue, bar.MaxValue, bar.Page,
                VerticalBar = ControlGeometry(scroll.GetVScrollBar()),
                VerticalRange = new { scroll.GetVScrollBar().Value, scroll.GetVScrollBar().MaxValue, scroll.GetVScrollBar().Page },
                Contents = scroll.GetChildren().OfType<Control>().Where(control => control is not ScrollBar).Select(ControlGeometry).ToArray(),
                Faces = Descendants(scroll).OfType<NCard>().Select(card => new {
                    Key = card.Model?.Id.Entry, Geometry = ControlGeometry(card) }).ToArray() };
            samples.Add(Snapshot("initial"));
            // This viewport deliberately leaves production _Process enabled.
            // Observe another eight native frames rather than supplying a
            // programmatic scroll or assuming container minimums have settled.
            for (int frame = 1; frame <= 8; frame++)
            { await Frames(1); samples.Add(Snapshot("settle-" + frame)); }
            var rect = bar.GetGlobalRect();
            float thumb = (float)Math.Clamp(rect.Size.X * bar.Page / (bar.MaxValue - bar.MinValue), 16, rect.Size.X);
            var start = new Vector2(rect.Position.X + thumb / 2, rect.GetCenter().Y);
            var end = new Vector2(rect.End.X - 2, rect.GetCenter().Y);
            string stem = "in-run-map-horizontal-drag-" + new string(scenario.Select(character =>
                char.IsLetterOrDigit(character) ? character : '_').ToArray());
            await Picture(stem + "-before.png");
            bar.GuiInput += Input; bar.ValueChanged += Value;
            try
            {
                Move(start); samples.Add(Snapshot("move"));
                viewport.PushInput(new InputEventMouseButton { Position = start, GlobalPosition = start,
                    ButtonIndex = MouseButton.Left, Pressed = true }, true);
                samples.Add(Snapshot("press"));
                viewport.PushInput(new InputEventMouseMotion { Position = end, GlobalPosition = end,
                    Relative = end - start, ButtonMask = MouseButtonMask.Left }, true);
                samples.Add(Snapshot("motion"));
                viewport.PushInput(new InputEventMouseButton { Position = end, GlobalPosition = end,
                    ButtonIndex = MouseButton.Left, Pressed = false }, true);
                samples.Add(Snapshot("release"));
                await Frames(); samples.Add(Snapshot("settled"));
                await Picture(stem + "-after.png");
                File.WriteAllText(Path.Combine(output, stem + ".json"), System.Text.Json.JsonSerializer.Serialize(new {
                    Scenario = scenario, EstimatedThumb = thumb, Start = start.ToString(), End = end.ToString(),
                    Samples = samples, Delivered = delivered, Values = values },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return $"receipt={stem};scrollX={scroll.ScrollHorizontal};bar={bar.GetGlobalRect()};" +
                    $"visible={bar.IsVisibleInTree()};value={bar.Value};page={bar.Page};max={bar.MaxValue};" +
                    $"guiEvents={delivered.Count};valueChanges={values.Count};hovered={viewport.GuiGetHoveredControl()?.GetPath()};open={view.HoverOpen}";
            }
            finally { bar.GuiInput -= Input; bar.ValueChanged -= Value; }
        }
        async Task Transit(Rect2 from, Rect2 to, string scenario)
        {
            // Use facing edges, with neutral panel padding as the waypoint.
            // Crossing through a reward action would deliberately replace the
            // side dock with its operation preview and test a different path.
            Vector2 start, destination;
            if (from.End.X <= to.Position.X || to.End.X <= from.Position.X)
            {
                bool right = from.End.X <= to.Position.X;
                float low = Math.Max(from.Position.Y + 2, to.Position.Y + 2), high = Math.Min(from.End.Y - 2, to.End.Y - 2);
                float y = low <= high ? Math.Clamp(from.GetCenter().Y, low, high) : from.GetCenter().Y;
                start = new(right ? from.End.X - 2 : from.Position.X + 2, y);
                destination = new(right ? to.Position.X + 2 : to.End.X - 2,
                    Math.Clamp(y, to.Position.Y + 2, to.End.Y - 2));
            }
            else
            {
                bool down = from.GetCenter().Y <= to.GetCenter().Y;
                float x = Math.Clamp(from.GetCenter().X, Math.Max(from.Position.X + 2, to.Position.X + 2), Math.Min(from.End.X - 2, to.End.X - 2));
                start = new(x, down ? from.End.Y - 2 : from.Position.Y + 2);
                destination = new(x, down ? to.Position.Y + 2 : to.End.Y - 2);
            }
            for (int step = 0; step <= 12; step++)
            {
                Move(start.Lerp(destination, step / 12f)); await Frames(1);
                check(view.HoverOpen, "continuous short source/panel/dock pointer transit: " + scenario + "/" + step);
            }
        }
        async Task CheckGroupDisplay(string scenario)
        {
            Move(Named("NodeHeading").GetGlobalRect().GetCenter()); await Frames();
            var rewardScroll = (ScrollContainer)Named("NodeDetailScroll");
            rewardScroll.ScrollVertical = 0;
            foreach (var rowScroll in Descendants(Named("CompactNodeHover")).OfType<ScrollContainer>().Where(scroll => scroll.Name == "CardRewardScroll"))
                rowScroll.ScrollHorizontal = 0;
            await Frames();
            var groupHeading = Descendants(Named("CompactNodeHover")).OfType<Control>().First(control =>
                control.Name.ToString().StartsWith("CardRewardHeading", StringComparison.Ordinal) &&
                rewardScroll.GetGlobalRect().HasPoint(control.GetGlobalRect().GetCenter()));
            Move(groupHeading.GetGlobalRect().GetCenter()); await Frames();
            var dock = (ScrollContainer)Named("MapRewardCardDock");
            dock.ScrollHorizontal = 0;
            var groups = Descendants(dock).OfType<Control>().Where(control => control.HasMeta("reward_group")).ToArray();
            var sections = data.Sections.Where(section => section.Choices.Any(choice => choice.Object?.CardSnapshot != null) ||
                section.Objects.Any(item => item.CardSnapshot != null)).ToArray();
            check(view.HasRewardCardDock && dock.IsVisibleInTree() && bounds.Encloses(dock.GetGlobalRect()) &&
                groups.Length == sections.Length && groups.Select(group => group.GetMeta("reward_group").AsString())
                    .SequenceEqual(sections.Select(section => section.StableId)),
                "side dock automatically retains every actual opportunity as a separately identified stack in source order: " + scenario);
            check(!Descendants(view).OfType<Control>().Any(control => control.IsVisibleInTree() &&
                control.Name.ToString() is "NodePanelClose" or "NodeOperationPreviewClose" or "RewardGroupPreview"),
                "main surface and automatic side stacks need no X or separate group-view window: " + scenario);
            for (int index = 0; index < sections.Length; index++)
            {
                var section = sections[index];
                var offered = section.Choices.Where(choice => choice.Object?.CardSnapshot != null).Select(choice => choice.Object!).ToArray();
                var expected = offered.Concat(section.Objects.Where(item => item.CardSnapshot != null && !offered.Contains(item)))
                    .Select(item => item.CardSnapshot!).ToArray();
                var overlay = Descendants(groups[index]).OfType<NeowOperationOverlay>().Single();
                var displayed = (OperationPreview?)typeof(NeowOperationOverlay).GetField("_preview", members)!.GetValue(overlay);
                var projected = displayed?.Branches.SelectMany(branch => branch.Sections).SelectMany(item => item.CardGroups)
                    .SelectMany(group => group.Cards).ToArray();
                var faces = Descendants(overlay).OfType<NCard>().ToArray();
                check(projected != null && projected.SequenceEqual(expected) && faces.Length == expected.Length &&
                    faces.Select(card => card.Model!.Id.Entry).SequenceEqual(expected.Select(card => card.Key.Entry)) &&
                    faces.All(card => Math.Abs(card.GetGlobalTransform().Scale.X - .42f) < .001f),
                    "one opportunity preserves exact ordered snapshots/native cards at the same .42 scale for every group count: " + scenario + "/" + section.StableId);
                check(index == 0 || groups[index].GetGlobalRect().Position.X >= groups[index - 1].GetGlobalRect().End.X &&
                    Math.Abs(groups[index].GetGlobalRect().Position.Y - groups[index - 1].GetGlobalRect().Position.Y) < 1,
                    "opportunity stacks stay side by side rather than merging or wrapping 2+1: " + scenario + "/" + index);
                check(!overlay.GetChildren().OfType<NinePatchRect>().Any(control => control.IsVisibleInTree()),
                    "native card stacks add no enclosing night-blue backing frame: " + scenario + "/" + index);
            }
            int before = requested.Count;
            var main = Named("CompactNodeHover");
            await Transit(main.GetGlobalRect(), dock.GetGlobalRect(), scenario + "/main-to-dock");
            Move(dock.GetGlobalRect().GetCenter()); await Frames();
            check(view.RewardCardDockContains(dock.GetGlobalRect().GetCenter()) && view.HoverOpen && requested.Count == before,
                "actual pointer can read side stacks without authoring a choice or closing main controls: " + scenario);
            await Picture("in-run-map-layered-side-stacks-" + scenario + "-" + groups.Length + "groups-start.png");
            if (dock.GetHScrollBar().MaxValue > dock.GetHScrollBar().Page)
            {
                check(dock.GetHScrollBar().IsVisibleInTree() && dock.GetGlobalRect().Encloses(dock.GetHScrollBar().GetGlobalRect()),
                    "horizontal overflow exposes a reachable scrollbar even when vertical scrolling also consumes width: " + scenario);
                string receipt = await DragHorizontal(dock, "dock-" + scenario + "-" + groups.Length + "groups");
                check(dock.ScrollHorizontal > 0 && view.HoverOpen && requested.Count == before,
                    "actual horizontal scrollbar drag reaches overflow stacks without changing their scale or authoring a reward: " + scenario +
                    ";before=" + before + ";requested=" + requested.Count + ";" + receipt);
            }
            await Picture("in-run-map-layered-side-stacks-" + scenario + "-" + groups.Length + "groups.png");
            await Transit(dock.GetGlobalRect(), main.GetGlobalRect(), scenario + "/dock-to-main");
            Move(Named("NodeHeading").GetGlobalRect().GetCenter()); await Frames();
            if (view.FindChild("EventBranchRow", true, false) is HBoxContainer branchRow)
            {
                var branchScroll = (ScrollContainer)Named("EventBranchScroll");
                branchScroll.ScrollHorizontal = (int)branchScroll.GetHScrollBar().MaxValue; await Frames();
                var finalBranch = branchRow.GetChildren().OfType<Button>().Last();
                before = requested.Count;
                Click(finalBranch.GetGlobalRect().GetCenter()); await Frames();
                check(requested.Count == before + 1 && view.HoverOpen,
                    "final branch remains clickable after returning from side stacks without any X: " + scenario);
            }
        }
        async Task CheckRewardRows(string scenario, bool exerciseOverflow)
        {
            var body = Named("CompactNodeHover"); var outcomeScroll = (ScrollContainer)Named("NodeDetailScroll");
            check(!Descendants(body).OfType<NCard>().Any(), "main reward rows use small icons/text rather than full native card scenes: " + scenario);
            foreach (var section in data.Sections.Where(section => section.Choices.Any(choice => choice.Object?.CardSnapshot != null)))
            {
                var candidates = section.Choices.Where(choice => choice.Object?.CardSnapshot != null).ToArray();
                var buttons = candidates.Select(choice => Choice(choice.Id)).ToArray();
                var row = buttons[0].GetParent() as HBoxContainer;
                var horizontal = row?.GetParent() as ScrollContainer;
                check(row?.Name == "CardRewardRow" && horizontal?.Name == "CardRewardScroll" &&
                    section.Choices.All(choice => ReferenceEquals(Choice(choice.Id).GetParent(), row)) &&
                    row!.GetChildren().OfType<Button>().Where(button => button.HasMeta("choice_id"))
                        .Select(button => button.GetMeta("choice_id").AsString()).SequenceEqual(section.Choices.Select(choice => choice.Id)),
                    "each opportunity keeps every candidate then its actual actions in one horizontal row: " + scenario + "/" + section.StableId);
                check(buttons.All(button => Math.Abs(button.GetGlobalRect().GetCenter().Y - buttons[0].GetGlobalRect().GetCenter().Y) < 1),
                    "three/four and greater-than-four candidates never wrap or truncate their opportunity: " + scenario + "/" + candidates.Length);
                foreach (var choice in candidates)
                {
                    var button = Choice(choice.Id); var item = choice.Object!;
                    string expectedName = (item.DisplayName ?? names.Resolve(item.Key, item.Kind)) +
                        (item.UpgradeLevel > 0 ? "+" + (item.UpgradeLevel > 1 ? item.UpgradeLevel.ToString() : "") : "");
                    check(button.Disabled == !choice.Enabled && Descendants(button).OfType<TextureRect>().Any(icon => icon.Texture != null && icon.Size.X <= 40) &&
                        Descendants(button).OfType<Label>().Any(label => label.Text.StartsWith(expectedName, StringComparison.Ordinal) && !label.ClipText),
                        "small actual card icon and complete name/upgrade retain choice identity and enabled state: " + scenario + "/" + choice.Id);
                }
                foreach (var action in section.Choices.Where(choice => choice.Object == null))
                {
                    var button = Choice(action.Id);
                    check(button.Text == action.Label && UntrimmedTextFits(button),
                        "inline reward action keeps its actual label readable at measured native font width: " + scenario + "/" + action.Id +
                        ";width=" + button.Size.X + ";natural=" + NaturalTextWidth(button) + ";text=" + button.Text);
                }
                var selected = candidates.FirstOrDefault(choice => choice.Selected);
                var ordinary = candidates.FirstOrDefault(choice => !choice.Selected && choice.Enabled);
                if (selected != null && ordinary != null)
                    check(Choice(selected.Id).GetThemeStylebox("normal") is StyleBoxFlat selectedStyle &&
                        Choice(ordinary.Id).GetThemeStylebox("normal") is StyleBoxFlat ordinaryStyle && selectedStyle.BorderColor != ordinaryStyle.BorderColor,
                        "icon/text row retains the selected card's visible state: " + scenario);
                if (scenario == "right" && section.StableId == "geometry.reward.0")
                {
                    var six = section.Choices.Select(choice => Choice(choice.Id)).ToArray();
                    check(candidates.Length == 5 && section.Choices.Count(choice => choice.Object == null) == 1 && six.Length == 6 &&
                        horizontal!.ScrollHorizontal == 0,
                        "five-card geometry opportunity retains its one supplied real Reroll as six controls without inventing a Skip action");
                    if (!horizontal!.GetGlobalRect().HasPoint(six[^1].GetGlobalRect().GetCenter()))
                    { horizontal.ScrollHorizontal = (int)horizontal.GetHScrollBar().MaxValue; await Frames(); }
                    check(horizontal.GetGlobalRect().HasPoint(six[^1].GetGlobalRect().GetCenter()),
                        "side-dock width reservation still exposes the sixth actual reward action through its native row scrollbar");
                    var final = section.Choices.Last(); int before = requested.Count;
                    Click(Choice(final.Id).GetGlobalRect().GetCenter()); await Frames();
                    check(requested.Count == before + 1 && requested[^1].Choice == final.Id && view.HoverOpen,
                        "normal-width sixth control dispatches the supplied actual alternative without a reserved second alternative slot");
                }
                if (exerciseOverflow)
                {
                    outcomeScroll.ScrollVertical += (int)(row!.GetGlobalRect().Position.Y - outcomeScroll.GetGlobalRect().Position.Y);
                    await Frames();
                    var bar = horizontal!.GetHScrollBar();
                    check(bar.IsVisibleInTree() && bar.MaxValue > bar.Page,
                        "long/narrow opportunity provides a native horizontal scrollbar: " + scenario);
                    horizontal.ScrollHorizontal = 0; await Frames();
                    int beforeDrag = requested.Count;
                    string dragReceipt = await DragHorizontal(horizontal, "row-" + scenario + "-" + section.StableId);
                    check(horizontal.ScrollHorizontal > 0 && view.HoverOpen,
                        "actual native scrollbar drag navigates horizontal cards while main wheel input stays vertical: " + scenario + ";" + dragReceipt);
                    check(requested.Count == beforeDrag, "native row scrollbar drag authors no reward choice: " + scenario);
                    horizontal!.ScrollHorizontal = (int)horizontal.GetHScrollBar().MaxValue; await Frames();
                    var final = section.Choices.Last(choice => choice.Enabled);
                    var finalButton = Choice(final.Id);
                    // With no redundant Skip at the end, the last enabled
                    // control can precede a disabled card. Bring that actual
                    // target into view after exercising the native bar drag.
                    horizontal.EnsureControlVisible(finalButton); await Frames();
                    check(horizontal.GetGlobalRect().HasPoint(finalButton.GetGlobalRect().GetCenter()) &&
                        outcomeScroll.GetGlobalRect().HasPoint(finalButton.GetGlobalRect().GetCenter()),
                        "long/narrow horizontal reward row reaches its final applicable action: " + scenario + "/" + final.Id);
                    int before = requested.Count;
                    var clickPoint = finalButton.GetGlobalRect().GetCenter();
                    int pressed = 0, guiDown = 0, guiUp = 0;
                    finalButton.Pressed += () => pressed++;
                    finalButton.GuiInput += input =>
                    {
                        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
                        { if (mouse.Pressed) guiDown++; else guiUp++; }
                    };
                    string ActionInput()
                    {
                        var preview = Named("MapEventOptionPreview");
                        var previewViewport = (ScrollContainer)Named("NodeOptionPreviewViewport");
                        var previewFrame = Named("NodeOptionNightFrame");
                        var anchor = (Control?)typeof(InRunMapNodeDetails).GetField("_previewAnchor", members)!.GetValue(view);
                        string anchorInfo = anchor != null && GodotObject.IsInstanceValid(anchor)
                            ? anchor.GetPath() + ";rect=" + anchor.GetGlobalRect() : "none";
                        return $"count={requested.Count};before={before};button={finalButton.GetGlobalRect()};disabled={finalButton.Disabled};" +
                            $"point={clickPoint};pointer={viewport.GetMousePosition()};hovered={viewport.GuiGetHoveredControl()?.GetPath()};" +
                            $"horizontal={horizontal.GetGlobalRect()};scrollX={horizontal.ScrollHorizontal};outcome={outcomeScroll.GetGlobalRect()};scrollY={outcomeScroll.ScrollVertical};" +
                            $"preview={preview.Visible};viewportVisible={previewViewport.Visible};viewport={previewViewport.GetGlobalRect()};viewportHits={previewViewport.GetGlobalRect().HasPoint(clickPoint)};" +
                            $"previewScroll={previewViewport.ScrollHorizontal},{previewViewport.ScrollVertical};frameVisible={previewFrame.Visible};frame={previewFrame.GetGlobalRect()};frameHits={previewFrame.GetGlobalRect().HasPoint(clickPoint)};" +
                            $"previewAnchor={anchorInfo};panel={body.GetGlobalRect()};open={view.HoverOpen}";
                    }
                    string beforeMove = ActionInput();
                    using var beforeImage = viewport.GetTexture().GetImage();
                    Move(clickPoint);
                    string afterMove = ActionInput();
                    Click(clickPoint); await Frames();
                    bool dispatched = requested.Count == before + 1 && requested[^1].Choice == final.Id && view.HoverOpen;
                    string diagnostic = "";
                    if (!dispatched)
                    {
                        string stem = "in-run-map-reward-action-" + scenario + "-" + new string((section.StableId ?? "group")
                            .Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
                        beforeImage.SavePng(Path.Combine(output, stem + "-before.png"));
                        await Picture(stem + "-after.png");
                        diagnostic = $"scenario={scenario};section={section.StableId};final={final.Id};beforeMove[{beforeMove}];" +
                            $"afterMove[{afterMove}];afterClick[{ActionInput()}];pressed={pressed};guiDown={guiDown};guiUp={guiUp};" +
                            $"newChoices={string.Join(",", requested.Skip(before).Select(item => item.Choice))};pictures={stem}";
                        File.AppendAllText(Path.Combine(output, "in-run-map-reward-action-input.txt"), diagnostic + System.Environment.NewLine);
                    }
                    check(dispatched, "actual GUI click dispatches final action from its own reward opportunity: " + scenario + ";" + diagnostic);
                    horizontal.ScrollHorizontal = 0; await Frames();
                }
            }
            outcomeScroll.ScrollVertical = 0; await Frames();
        }

        async Task CheckOperationCardPreview(string scenario)
        {
            var first = data.Sections.SelectMany(section => section.Choices).First(choice =>
                choice.Object?.CardSnapshot != null && choice.Enabled && (choice.Preview != null || choice.PreviewFactory != null));
            Move(Choice(first.Id).GetGlobalRect().GetCenter()); await Frames(8);
            var preview = (NeowOperationOverlay)Named("MapEventOptionCards");
            var previewViewport = Named("NodeOptionCardsViewport");
            check(preview.IsVisibleInTree() && previewViewport.IsVisibleInTree() &&
                !Named("NodeOptionNightFrame").Visible && preview.SummaryDesiredSize == Vector2.Zero,
                "ordinary Take keeps only its real card result without a source/title/reward-scope backing frame: " + scenario);
            check(Descendants(preview).OfType<NCard>().All(card => Math.Abs(card.GetGlobalTransform().Scale.X - .42f) < .001f),
                "operation result cards share the opportunity dock scale: " + scenario);
            var main = Named("CompactNodeHover");
            await Transit(main.GetGlobalRect(), previewViewport.GetGlobalRect(), scenario + "/main-to-operation");
            check(preview.IsVisibleInTree(), "operation preview remains present across its actual short side gap: " + scenario);
            await Picture("in-run-map-layered-operation-stack-" + scenario + ".png");
            await Transit(previewViewport.GetGlobalRect(), main.GetGlobalRect(), scenario + "/operation-to-main");
            Move(Named("NodeHeading").GetGlobalRect().GetCenter()); await Frames();
            check(view.HoverOpen && preview.IsVisibleInTree(), "return to main controls preserves continuous operation without X: " + scenario);
        }

        // Explicit presentation stress fixture. Duplicate groups of supplied
        // real snapshots only to test geometry; this never asserts a second predicted
        // reward. Presenter/event semantics have independent checks.
        var nativeRewards = combat.Sections.First(section => section.Choices.Any(choice => choice.Object?.CardSnapshot != null));
        var actualAlternative = alternativeData.Sections.First(section => section.RewardActionsRow &&
            section.Choices.Any(choice => choice.Object?.CardSnapshot != null) && section.Choices.Any(choice => choice.Object == null && choice.PreviewFactory != null));
        check(actualAlternative.Choices.Count(choice => choice.Object == null) == 1,
            "the supplied actual Colorful/Driftwood opportunity contributes exactly its real Reroll, with no old Skip or invented second alternative");
        var branches = eventData.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices).ToArray();
        if (branches.Length == 0) branches = eventData.Sections.Where(section => section.HeaderChoice != null).Select(section => section.HeaderChoice!).ToArray();
        if (branches.Length < 2) throw new InvalidOperationException("MapPanelGeometryFixtureNeedsTwoEventBranches");
        InRunMapNodeDetailData Geometry(bool longText, bool longPremises, int groupCount = 2)
        {
            var branchRow = new InRunMapDetailSection("", "", [], branches.Take(2).Select((choice, index) => choice with
            {
                Id = "geometry.branch." + index, Preview = null, PreviewFactory = null,
                Label = longText ? choice.Label + " · " + string.Concat(Enumerable.Repeat("较长的本地化分支 Long localized branch ", 3)) : choice.Label
            }).ToArray()) { BranchRow = true };
            var offered = nativeRewards.Choices.Where(choice => choice.Object?.CardSnapshot != null).ToArray();
            var groups = Enumerable.Range(0, groupCount).Select(index =>
            {
                // Preserve the six-control horizontal stress after H07 removed
                // Skip: five labelled geometry cards plus the actual Reroll.
                int count = index == 0 ? 5 : 6;
                var candidates = Enumerable.Range(0, count).Select(ordinal =>
                {
                    var original = offered[ordinal % offered.Length]; var item = original.Object!;
                    int upgrade = ordinal == 0 ? Math.Max(1, item.UpgradeLevel) : item.UpgradeLevel;
                    return original with { Id = $"geometry.reward.{index}.card.{ordinal}", Selected = ordinal == 0,
                        Enabled = ordinal != count - 1, Object = item with { UpgradeLevel = upgrade,
                            CardSnapshot = item.CardSnapshot! with { UpgradeLevel = upgrade },
                            DisplayName = longText ? names.Resolve(item.Key, item.Kind) + " · 很长的卡牌名称 Long localized card name " + ordinal : item.DisplayName } };
                });
                var actions = (index == 0 ? actualAlternative : nativeRewards).Choices.Where(choice => choice.Object == null);
                return nativeRewards with { Title = index == 0 ? "Presentation fixture · five cards with actual Reroll" :
                        "Presentation overflow fixture · six candidates", StableId = "geometry.reward." + index,
                    HeaderChoice = null, OutcomePreview = null, Objects = [],
                    Choices = candidates.Concat(actions
                        .Select(choice => choice with { Id = "geometry.reward." + index + "." + choice.Id })).ToArray() };
            });
            return combat with { NodeId = "geometry.groups", Title = "Presentation fixture · grouped rewards",
                Sections = new[] { branchRow }.Concat(groups).Concat(longText ? new[] {
                    new InRunMapDetailSection("Long localized content", string.Concat(Enumerable.Repeat(
                        "预测保留当前节点的真实结果。Long localized names and descriptions remain readable. ", 70)), [], []) } : []).ToArray(),
                Information = longPremises ? Enumerable.Range(0, 8).Select(index => "来源 " + (index + 1) + " · " +
                    string.Concat(Enumerable.Repeat("前置选择影响此收益 Pending prior choice affects this reward. ", 5))).ToArray() : combat.Information,
                EditorActions = [], Premises = null, IsCurrent = null };
        }
        try
        {
            foreach (var scenario in new[] { (Name: "left", Position: new Vector2(140, 230), GroupCount: 0, Long: false, Premises: false),
                (Name: "one-group", Position: new Vector2(140, 230), GroupCount: 1, Long: false, Premises: false),
                (Name: "right", Position: new Vector2(1480, 230), GroupCount: 2, Long: false, Premises: false),
                (Name: "three-groups", Position: new Vector2(140, 230), GroupCount: 3, Long: false, Premises: false),
                (Name: "long-content", Position: new Vector2(1480, 230), GroupCount: 2, Long: true, Premises: false),
                (Name: "prerequisites-long", Position: new Vector2(140, 230), GroupCount: 2, Long: true, Premises: true) })
            {
                view.CloseHover(); Move(new(4, 4)); await Frames();
                data = scenario.GroupCount > 0 ? Geometry(scenario.Long, scenario.Premises, scenario.GroupCount) : combat;
                source.Position = scenario.Position;
                var original = source.GetGlobalRect(); Move(original.GetCenter()); await Frames();
                var body = Named("CompactNodeHover"); var frame = Named("NodeNightFrame"); var marker = Named("NodeSourceHighlight");
                check(view.HoverOpen && source.GetGlobalRect() == original && source.GetParent() == viewport && source.ZIndex == 3 && source.ZAsRelative,
                    "pointer opens independent panel without changing native source geometry or draw ownership: " + scenario.Name);
                check(bounds.Encloses(body.GetGlobalRect()) && bounds.Encloses(frame.GetGlobalRect()) &&
                    frame.GetGlobalRect().Encloses(body.GetGlobalRect()) && !frame.GetGlobalRect().Encloses(original),
                    "bounded compact night frame follows its panel rather than enclosing source: " + scenario.Name + ";body=" + body.GetGlobalRect());
                check(scenario.Position.X < 800 ? body.GetGlobalRect().Position.X > original.End.X : body.GetGlobalRect().End.X < original.Position.X,
                    "panel chooses available side while retaining internal reading order: " + scenario.Name);
                check(marker.IsVisibleInTree() && marker.GetGlobalRect().Encloses(original) && marker.MouseFilter == Control.MouseFilterEnum.Ignore,
                    "passive marker associates panel with original native node: " + scenario.Name);
                check(!Descendants(view).OfType<Button>().Any(button => button.IsVisibleInTree() && button.HasMeta("choice_id") &&
                    button.GetMeta("choice_id").AsString() == "diagnostics"), "diagnostics remain secondary: " + scenario.Name);
                await AwaitPointerExit(new(4, 4));
                check(!view.HoverOpen && !marker.Visible,
                    "leaving the whole connected region closes main content and source marker without a click or X: " + scenario.Name);
                Move(original.GetCenter()); await Frames();
                check(view.HoverOpen, "source re-entry opens the current content after pointer-only exit: " + scenario.Name);
                var oldEnvelope = original.Grow(12).Merge(body.GetGlobalRect());
                Vector2? emptyEnvelopePoint = null;
                var envelopeXs = new[] { original.GetCenter().X, original.Position.X + 2, original.End.X - 2 }
                    .Concat(Enumerable.Range(1, 9).Select(x => oldEnvelope.Position.X + oldEnvelope.Size.X * x / 10f));
                foreach (float x in envelopeXs)
                    for (int y = 1; y < 10 && emptyEnvelopePoint is null; y++)
                    {
                        var point = new Vector2(x, oldEnvelope.Position.Y + oldEnvelope.Size.Y * y / 10f);
                        if (!original.Grow(14).HasPoint(point) && !body.GetGlobalRect().Grow(14).HasPoint(point) &&
                            !Named("MapRewardCardDock").GetGlobalRect().Grow(14).HasPoint(point) && !view.ContainsPointer(point))
                            emptyEnvelopePoint = point;
                    }
                check(emptyEnvelopePoint != null,
                    "old source/panel Merge contains demonstrable blank map space outside the new short connected region: " + scenario.Name);
                int beforeEnvelopeClick = blankClicks;
                await AwaitPointerExit(emptyEnvelopePoint!.Value);
                check(!view.HoverOpen && !view.BlocksMapInput(emptyEnvelopePoint.Value),
                    "old large-envelope blank space does not keep a hover or acquire map input ownership: " + scenario.Name);
                Click(emptyEnvelopePoint.Value); await Frames();
                check(blankClicks == beforeEnvelopeClick + 1,
                    "actual blank click in former Merge envelope reaches the underlying map: " + scenario.Name);
                Move(original.GetCenter()); await Frames();
                var scroll = (ScrollContainer)Named("NodeDetailScroll");
                var rewards = data.Sections.SelectMany(section => section.Choices).Where(choice => choice.Object?.CardSnapshot != null).ToArray();
                var target = rewards.Select(choice => Choice(choice.Id)).First(button => scroll.GetGlobalRect().HasPoint(button.GetGlobalRect().GetCenter()));
                var targetRect = target.GetGlobalRect();
                await Transit(original, body.GetGlobalRect(), scenario.Name + "/source-to-main");
                if (scenario.Name == "left") await CheckOperationCardPreview("ordinary");
                Move(targetRect.GetCenter()); await Frames();
                int beforeChoice = requested.Count; Click(targetRect.GetCenter()); await Frames();
                check(requested.Count == beforeChoice + 1 && view.HoverOpen && target.GetGlobalRect() == targetRect && source.GetGlobalRect() == original,
                    "actual reward click keeps stable panel and source geometry: " + scenario.Name);
                await CheckRewardRows(scenario.Name, exerciseOverflow: scenario.Long);
                if (scenario.GroupCount > 0)
                {
                    var groups = Descendants(body).OfType<Control>().Where(control => control.HasMeta("reward_group") &&
                        control.GetMeta("reward_group").AsString().StartsWith("geometry.reward.", StringComparison.Ordinal)).ToArray();
                    check(groups.Length == scenario.GroupCount && groups.All(group => Descendants(group).OfType<HBoxContainer>().Any(row => row.Name == "CardRewardRow")) &&
                        groups.Zip(groups.Skip(1)).All(pair => pair.Second.GetGlobalRect().Position.Y >= pair.First.GetGlobalRect().End.Y),
                        "each presentation opportunity retains its own small-icon reward row: " + scenario.Name);
                    var row = (HBoxContainer)Named("EventBranchRow"); var branchButtons = row.GetChildren().OfType<Button>().ToArray();
                    var branchScroll = (ScrollContainer)Named("EventBranchScroll");
                    check(branchButtons.Length == 2 && branchButtons.Select(button => Math.Round(button.GetGlobalRect().Position.Y)).Distinct().Count() == 1 &&
                        !scroll.IsAncestorOf(row) && bounds.Encloses(branchScroll.GetGlobalRect()) && bounds.Encloses(scroll.GetGlobalRect()),
                        "one horizontal branch row and outcomes are reachable inside viewport: " + scenario.Name);
                    var branchRect = row.GetGlobalRect(); var headingRect = Named("NodeHeading").GetGlobalRect();
                    int beforeScroll = scroll.ScrollVertical;
                    for (int wheel = 0; wheel < 4; wheel++) Click(scroll.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
                    await Frames();
                    check((!scenario.Long || scroll.ScrollVertical > beforeScroll) && row.GetGlobalRect() == branchRect &&
                        Named("NodeHeading").GetGlobalRect() == headingRect && view.HoverOpen,
                        "native wheel scrolls overflowing outcomes while branch controls and heading stay in place: " + scenario.Name);
                    scroll.ScrollVertical = 0; await Frames();
                    if (scenario.Premises)
                    {
                        var prerequisites = (ScrollContainer)Named("NodePrerequisiteScroll");
                        int beforePrerequisite = prerequisites.ScrollVertical;
                        for (int wheel = 0; wheel < 4; wheel++) Click(prerequisites.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
                        await Frames();
                        check(prerequisites.ScrollVertical > beforePrerequisite && bounds.Encloses(prerequisites.GetGlobalRect()) &&
                            row.GetGlobalRect() == branchRect && Named("NodeHeading").GetGlobalRect() == headingRect && scroll.ScrollVertical == 0,
                            "multiple long prerequisite causes scroll independently without displacing branch or reward controls");
                    }
                    beforeChoice = requested.Count; Click(branchButtons[0].GetGlobalRect().GetCenter()); await Frames();
                    check(requested.Count == beforeChoice + 1 && view.HoverOpen, "branch click remains reachable after outcome scrolling: " + scenario.Name);
                    if (scenario.Long)
                    {
                        branchScroll.ScrollHorizontal = (int)branchScroll.GetHScrollBar().MaxValue; await Frames();
                        check(branchScroll.GetGlobalRect().Encloses(branchButtons[^1].GetGlobalRect()),
                            "long localized branch row fits or scrolls to the final complete button: " + scenario.Name);
                    }
                }
                await Picture("in-run-map-layered-" + scenario.Name + ".png");
                await CheckGroupDisplay(scenario.Name);
                // Replace generation synchronously before the old deferred signal runs.
                var oldButton = Choice(rewards[0].Id); beforeChoice = requested.Count;
                oldButton.EmitSignal(BaseButton.SignalName.Pressed);
                data = data with { Subtitle = data.Subtitle + " · new revision" };
                view.ShowNode(data, original, bounds, source); await Frames();
                check(requested.Count == beforeChoice && view.HoverOpen && view.HoverNodeId == data.NodeId && !ReferenceEquals(Choice(rewards[0].Id), oldButton),
                    "same-node replacement stays open and rejects stale-generation reward callback: " + scenario.Name);
                var fresh = Choice(rewards[0].Id);
                var freshRect = fresh.GetGlobalRect(); var freshPoint = freshRect.GetCenter();
                var freshScroll = (ScrollContainer)Named("NodeDetailScroll");
                int freshPressed = 0, freshGuiDown = 0, freshGuiUp = 0;
                fresh.Pressed += () => freshPressed++;
                fresh.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
                    { if (mouse.Pressed) freshGuiDown++; else freshGuiUp++; }
                };
                string BeforeFreshClick() => $"count={requested.Count};before={beforeChoice};id={rewards[0].Id};" +
                    $"rect={fresh.GetGlobalRect()};initialRect={freshRect};scroll={freshScroll.GetGlobalRect()};scrollY={freshScroll.ScrollVertical};" +
                    $"panel={Named("CompactNodeHover").GetGlobalRect()};hoverOpen={view.HoverOpen};" +
                    $"preview={Named("MapEventOptionPreview").Visible};previewRect={Named("NodeOptionPreviewViewport").GetGlobalRect()};" +
                    $"point={freshPoint};pointer={viewport.GetMousePosition()};hovered={viewport.GuiGetHoveredControl()?.GetPath()};" +
                    $"disabled={fresh.Disabled};generation={typeof(InRunMapNodeDetails).GetField("_hoverGeneration", members)!.GetValue(view)}";
                string beforeFresh = BeforeFreshClick();
                Move(freshPoint);
                string afterFreshMove = BeforeFreshClick();
                Click(freshPoint); await Frames();
                string freshReceipt = $"scenario={scenario.Name};beforeMove[{beforeFresh}];afterMove[{afterFreshMove}];" +
                    $"afterClick[{BeforeFreshClick()}];pressed={freshPressed};guiDown={freshGuiDown};guiUp={freshGuiUp};" +
                    $"newChoices={string.Join(",", requested.Skip(beforeChoice).Select(item => item.Choice))}";
                if (requested.Count != beforeChoice + 1)
                    File.AppendAllText(Path.Combine(output, "in-run-map-layered-input.txt"), freshReceipt + System.Environment.NewLine);
                check(requested.Count == beforeChoice + 1, "same-node replacement binds a fresh actionable reward: " + scenario.Name + ";" + freshReceipt);
                // Real pointer dispatch through the view's deferred callback,
                // followed by a controlled caller revision publication. This is
                // a view lifecycle assertion, not another predictor algorithm.
                long currentRevision = fixtureRevision;
                data = data with { IsCurrent = () => fixtureRevision == currentRevision };
                view.ShowNode(data, original, bounds, source); await Frames();
                var previousData = data;
                var revisionChoice = data.Sections.SelectMany(section => section.Choices).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                var revisionButton = Choice(revisionChoice.Id);
                var revisionScroll = (ScrollContainer)Named("NodeDetailScroll");
                revisionScroll.ScrollVertical = 0; await Frames();
                publishRevisionOnClick = true;
                try
                {
                    beforeChoice = requested.Count;
                    Click(revisionButton.GetGlobalRect().GetCenter()); await Frames();
                    var newChoice = data.Sections.SelectMany(section => section.Choices).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                    check(requested.Count == beforeChoice + 1 && fixtureRevision == currentRevision + 1 &&
                        previousData.IsCurrent?.Invoke() == false && data.IsCurrent?.Invoke() == true &&
                        view.HoverOpen && view.HoverNodeId == data.NodeId && newChoice.Id != revisionChoice.Id &&
                        !ReferenceEquals(Choice(newChoice.Id), revisionButton),
                        "actual pointer choice accepts the caller's fresh revision while the old observation and old controls expire: " + scenario.Name);
                    beforeChoice = requested.Count;
                    Click(Choice(newChoice.Id).GetGlobalRect().GetCenter()); await Frames();
                    check(requested.Count == beforeChoice + 1 && fixtureRevision == currentRevision + 2 && view.HoverOpen,
                        "new-revision controls remain continuously actionable inside the hover region: " + scenario.Name);
                }
                finally { publishRevisionOnClick = false; }
                await AwaitPointerExit(new(4, 4));
                check(!view.HoverOpen && !marker.Visible,
                    "clicking and publishing new revisions never pins the main content after pointer exit: " + scenario.Name);
                Move(original.GetCenter()); await Frames();
                int beforeSource = sourceClicks; Click(original.GetCenter()); await Frames();
                check(sourceClicks == beforeSource + 1 && source.ZIndex == 3 && source.ZAsRelative,
                    "original source GUI input remains available: " + scenario.Name);
                int beforeBlank = blankClicks; Click(new(4, 4)); await Frames();
                check(!view.HoverOpen && !marker.Visible && blankClicks == beforeBlank + 1,
                    "blank click dismisses panel and passes through to underlying map surface: " + scenario.Name);
                Move(original.GetCenter()); await Frames();
                check(view.HoverOpen, "source re-entry reopens explicitly dismissed panel: " + scenario.Name);
                viewport.PushInput(new InputEventKey { Pressed = true, Keycode = Key.Escape }, true); await Frames();
                check(!view.HoverOpen && !marker.Visible, "Escape dismisses panel and association marker: " + scenario.Name);
                Move(new(4, 4)); await Frames(1); Move(original.GetCenter()); await Frames();
                check(view.HoverOpen && !Descendants(view).OfType<Button>().Any(button => button.IsVisibleInTree() &&
                    button.Name.ToString() is "NodePanelClose" or "NodeOperationPreviewClose"),
                    "source re-entry remains available after Escape without restoring visible close controls: " + scenario.Name);
            }
            // The independent narrow interaction fixture starts a fresh hover
            // near the upper-left, with room for its main to grow rightward.
            // The preceding same-node generation retained its right-side offset;
            // moving that live source to the right edge would intentionally
            // carry its main offscreen, as the separate anchor cases verify.
            view.CloseHover(); Move(new(4, 4)); await Frames();
            viewport.Size = new(800, 720); paper.Size = new(800, 720);
            bounds = new(Vector2.Zero, new(800, 720)); source.Position = new(12, 12);
            data = Geometry(longText: true, longPremises: true, groupCount: 3);
            Move(source.GetGlobalRect().GetCenter());
            view.ShowNode(data, source.GetGlobalRect(), bounds, source); await Frames(8);
            var narrowFrame = Named("NodeNightFrame");
            var narrowBranches = (ScrollContainer)Named("EventBranchScroll");
            var narrowPrerequisites = (ScrollContainer)Named("NodePrerequisiteScroll");
            var narrowRewards = (ScrollContainer)Named("NodeDetailScroll");
            var narrowInputGeometry = new {
                Source = source.GetGlobalRect().ToString(), Main = narrowFrame.GetGlobalRect().ToString(),
                Offset = (narrowFrame.GetGlobalRect().Position - source.GetGlobalRect().Position).ToString(),
                Branches = narrowBranches.GetGlobalRect().ToString(), Prerequisites = narrowPrerequisites.GetGlobalRect().ToString(),
                Rewards = narrowRewards.GetGlobalRect().ToString(), Bounds = bounds.ToString() };
            File.WriteAllText(Path.Combine(output, "in-run-map-narrow-input-geometry.json"),
                System.Text.Json.JsonSerializer.Serialize(narrowInputGeometry));
            await Picture("in-run-map-narrow-input-geometry.png");
            check(view.HoverOpen && bounds.Encloses(narrowFrame.GetGlobalRect()) &&
                new[] { narrowBranches, narrowPrerequisites, narrowRewards }.All(control =>
                    bounds.Encloses(control.GetGlobalRect()) && control.Size.X > 0 && control.Size.Y > 0),
                "800x720 viewport bounds long branch, prerequisite and reward controls inside independent panel; geometry=" +
                System.Text.Json.JsonSerializer.Serialize(narrowInputGeometry));
            var narrowBranchRow = (HBoxContainer)Named("EventBranchRow");
            var narrowBranchRect = narrowBranchRow.GetGlobalRect();
            int narrowBeforePrerequisites = narrowPrerequisites.ScrollVertical;
            for (int wheel = 0; wheel < 4; wheel++) Click(narrowPrerequisites.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
            await Frames();
            check(narrowPrerequisites.ScrollVertical > narrowBeforePrerequisites && narrowBranchRow.GetGlobalRect() == narrowBranchRect,
                "narrow-window actual wheel scrolls prerequisite causes while preserving branch entry");
            int narrowBeforeRewards = narrowRewards.ScrollVertical;
            for (int wheel = 0; wheel < 4; wheel++) Click(narrowRewards.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
            await Frames();
            if (narrowRewards.ScrollVertical <= narrowBeforeRewards)
            {
                Console.WriteLine("IN_RUN_MAP_NARROW_WHEEL " +
                    $"point={narrowRewards.GetGlobalRect().GetCenter()};hovered={viewport.GuiGetHoveredControl()?.GetPath()};" +
                    $"value={narrowRewards.ScrollVertical};before={narrowBeforeRewards};" +
                    $"max={narrowRewards.GetVScrollBar().MaxValue};page={narrowRewards.GetVScrollBar().Page};" +
                    $"preview={Named("NodeOptionPreviewViewport").IsVisibleInTree()};region={narrowRewards.GetGlobalRect()}");
                await Picture("in-run-map-narrow-wheel-failure.png");
            }
            check(narrowRewards.ScrollVertical > narrowBeforeRewards && narrowBranchRow.GetGlobalRect() == narrowBranchRect,
                "narrow-window actual wheel reaches overflowing rewards without scrolling branch controls");
            narrowBranches.ScrollHorizontal = (int)narrowBranches.GetHScrollBar().MaxValue; await Frames();
            var lastNarrowBranch = narrowBranchRow.GetChildren().OfType<Button>().Last();
            int beforeNarrowBranch = requested.Count;
            Click(lastNarrowBranch.GetGlobalRect().GetCenter()); await Frames();
            check(narrowBranches.GetHScrollBar().MaxValue > narrowBranches.Size.X &&
                narrowBranches.GetGlobalRect().HasPoint(lastNarrowBranch.GetGlobalRect().GetCenter()) &&
                requested.Count == beforeNarrowBranch + 1 && view.HoverOpen,
                "narrow-window horizontal branch scrolling exposes a fully clickable final localized branch");
            await CheckRewardRows("narrow-window", exerciseOverflow: true);
            narrowRewards.ScrollVertical = 0; await Frames();
            var firstNarrowReward = data.Sections.SelectMany(section => section.Choices)
                .Where(choice => choice.Object?.CardSnapshot != null).Select(choice => Choice(choice.Id))
                .First(button => narrowRewards.GetGlobalRect().HasPoint(button.GetGlobalRect().GetCenter()));
            int beforeNarrowReward = requested.Count;
            Click(firstNarrowReward.GetGlobalRect().GetCenter()); await Frames();
            check(requested.Count == beforeNarrowReward + 1 && view.HoverOpen,
                "narrow-window icon/text card remains clickable after scrolling back to its group");
            await Picture("in-run-map-layered-narrow-window.png");
            await CheckGroupDisplay("narrow-window");
            // A top-center source has insufficient space above. Cards must
            // move below while the main stays aligned with the source, and
            // the real pointer must traverse the resulting connection.
            view.CloseHover(); Move(new(4, 4)); await Frames();
            source.Position = new(400, 100); data = Geometry(longText: false, longPremises: false, groupCount: 3);
            Move(source.GetGlobalRect().GetCenter()); await Frames(8);
            var centerDock = Named("MapRewardCardDock").GetGlobalRect();
            var centerMain = Named("CompactNodeHover").GetGlobalRect();
            bool coversSource = centerDock.Intersects(source.GetGlobalRect().Grow(4));
            File.WriteAllText(Path.Combine(output, "in-run-map-adaptive-card-placement.json"),
                System.Text.Json.JsonSerializer.Serialize(new {
                    State = "edge-adaptive", CoversSource = coversSource,
                    Source = source.GetGlobalRect().ToString(), Cards = centerDock.ToString(),
                    Main = centerMain.ToString() }));
            await Picture("in-run-map-adaptive-card-placement.png");
            check(centerDock.Position.Y >= centerMain.End.Y + 9 && bounds.Encloses(centerDock) && !coversSource &&
                Math.Abs(centerMain.Position.Y - source.GetGlobalRect().Position.Y) < 1,
                "narrow top-center cards flip below without moving the source-relative main or covering the source");
            await Transit(source.GetGlobalRect(), Named("CompactNodeHover").GetGlobalRect(), "narrow-source-avoidance/source-to-main");
            await CheckGroupDisplay("narrow-source-avoidance");
            view.CloseHover(); Move(new(4, 4)); await Frames();
            var settledCard = nativeRewards.Choices.First(choice => choice.Object?.CardSnapshot != null).Object!.CardSnapshot!;
            var settled = new OperationPreview(settledCard.Key, [], [], false,
                [new([], [new(settledCard.Key, [new(OperationPreviewAction.UpgradeCard, [], [settledCard], false)], [])], [], 1, true)], [])
                { Style = new(OperationTextMode.GroupRows, OperationCardLayout.Stack), Notices = ["Linked effect remains visible"] };
            data = combat with { NodeId = "geometry.outcome", Title = "Settled outcome display fixture", Information = [],
                Sections = [new("", "", [], []) { OutcomePreview = settled, StableId = "settled-scope", LargeCardResults = true }], IsCurrent = null };
            Move(source.GetGlobalRect().GetCenter()); await Frames(8);
            var outcomeDock = Named("MapRewardCardDock");
            check(Descendants(outcomeDock).OfType<Control>().Any(control => control.HasMeta("outcome_scope") &&
                    control.GetMeta("outcome_scope").AsString() == "settled-scope") &&
                !Descendants(Named("CompactNodeHover")).OfType<NCard>().Any() &&
                Descendants(outcomeDock).OfType<NCard>().Single().Model!.Id.Entry == settledCard.Key.Entry,
                "settled outcome card art shares the side dock with its own scope, without inventing a reward opportunity or duplicating main card scenes");
            check(Descendants(Named("CompactNodeHover")).OfType<Label>().Any(label => label.Text == "Linked effect remains visible"),
                "moving settled card art outside keeps its linked effect/unknown explanation in the main summary");
            await Transit(source.GetGlobalRect(), Named("CompactNodeHover").GetGlobalRect(), "settled-outcome/source-to-main");
            await Transit(Named("CompactNodeHover").GetGlobalRect(), outcomeDock.GetGlobalRect(), "settled-outcome/main-to-dock");
            await Transit(outcomeDock.GetGlobalRect(), Named("CompactNodeHover").GetGlobalRect(), "settled-outcome/dock-to-main");
            await Picture("in-run-map-layered-settled-outcome-side-stack.png");
            view.ShowNode(data, source.GetGlobalRect(), bounds, source); await Frames();
            foreach (var displaySize in new[] { new Vector2I(1680, 1080), new Vector2I(800, 720) })
            {
                view.CloseHover(); Move(new(4, 4)); await Frames();
                viewport.Size = displaySize; paper.Size = displaySize; bounds = new(Vector2.Zero, displaySize);
                source.Position = new(80, 140); data = stagedEvent;
                Move(source.GetGlobalRect().GetCenter()); await Frames(10);
                var stageRows = Descendants(Named("CompactNodeHover")).OfType<HBoxContainer>()
                    .Where(row => row.Name == "EventBranchRow").OrderBy(row => row.GetGlobalRect().Position.Y).ToArray();
                check(stageRows.Length == 3 && stageRows.Select(row => row.GetGlobalRect().Position.Y).Distinct().Count() == 3 &&
                    stageRows.All(row => row.GetGlobalRect().Size.Y > 0), "three event stage rows remain separate at " + displaySize);
                await Transit(source.GetGlobalRect(), Named("CompactNodeHover").GetGlobalRect(), "stage/source-to-main");
                await Picture("in-run-map-ui04-stages-" + displaySize.X + ".png");

                view.CloseHover(); Move(new(4, 4)); await Frames();
                var stockObjects = nativeRewards.Choices.Where(choice => choice.Object != null).Select(choice => choice.Object!).ToArray();
                var stock = Enumerable.Range(0, 8).Select(index => new InRunMapDetailChoice("shop-fixture-" + index, "Buy",
                    stockObjects[index % stockObjects.Length], Enabled: index != 2) { Price = 100 + index,
                        Status = index == 2 ? "Insufficient gold" : null }).ToArray();
                data = combat with { NodeId = "geometry.shop-grid", Title = "Shop grid display fixture", Information = [],
                    Sections = [new("", "", [], stock) { ItemFlow = true }], EditorActions = [], IsCurrent = null };
                Move(source.GetGlobalRect().GetCenter()); await Frames(12);
                var flow = Named("ShopOfferRows"); var tiles = flow.GetChildren().OfType<Button>().ToArray();
                check(tiles.Length == 8 && tiles.Select(tile => tile.GetGlobalRect().Position.Y).Distinct().Count() > 1 &&
                    tiles.All(tile => tile.GetGlobalRect().Position.X >= flow.GetGlobalRect().Position.X - 1 &&
                        tile.GetGlobalRect().End.X <= flow.GetGlobalRect().End.X + 1), "shop items wrap in reachable rows at " + displaySize);
                check(Descendants(flow).OfType<Label>().Count(label => label.Text.EndsWith("G", StringComparison.Ordinal)) == 8 &&
                    tiles[2].Disabled && Descendants(tiles[2]).OfType<Label>().Any(label => label.Text == "Insufficient gold"),
                    "shop tiles retain every price and unavailable reason");
                check(Descendants(flow).OfType<Label>().Where(label => label.Text.EndsWith("G", StringComparison.Ordinal))
                    .All(label => label.GetLineCount() == 1), "shop prices stay on one readable line");
                await Transit(source.GetGlobalRect(), Named("CompactNodeHover").GetGlobalRect(), "shop/source-to-main");
                int beforePurchase = requested.Count; Click(tiles[0].GetGlobalRect().GetCenter()); await Frames();
                check(requested.Count == beforePurchase + 1, "shop tile sends one existing action on actual click");
                await Picture("in-run-map-ui04-shop-" + displaySize.X + ".png");

                view.CloseHover(); Move(new(4, 4)); await Frames();
                data = triggeredCombat with { IsCurrent = null, EditorActions = [] };
                Move(source.GetGlobalRect().GetCenter()); await Frames(12);
                var main = Named("CompactNodeHover").GetGlobalRect();
                var battle = Named("MapRewardCardDock").GetGlobalRect();
                check(!triggeredCombat.Sections.Any(section => section.CombatPanel) &&
                    view.FindChild("EventCombatPanel", true, false) == null &&
                    new Rect2(Vector2.Zero, displaySize).Encloses(main) && new Rect2(Vector2.Zero, displaySize).Encloses(battle) &&
                    Descendants(Named("MapRewardCardDock")).OfType<Control>().Any(control => control.HasMeta("reward_group_has_stable_identity")),
                    "actual completed event combat retains reachable main results and its real card groups without an obsolete partial panel at " + displaySize);
                await Transit(source.GetGlobalRect(), main, "battle/source-to-main");
                await Transit(main, battle, "battle/main-to-reward-dock");
                await Transit(battle, main, "battle/reward-dock-to-main");
                await Picture("in-run-map-ui04-battle-" + displaySize.X + ".png");
            }
            var finalMarker = Named("NodeSourceHighlight"); view.SetProcess(false); view.Hide();
            check(!view.HoverOpen && !finalMarker.Visible && source.ZIndex == 3 && source.ZAsRelative,
                "Hide retires marker and panel even with automatic processing paused");
            view.Show(); view.SetProcess(true);
            var replacement = new Control { Position = source.Position, Size = source.Size, ZIndex = 5,
                ZAsRelative = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            viewport.AddChild(replacement); view.ShowNode(data, replacement.GetGlobalRect(), bounds, replacement); await Frames();
            check(source.ZIndex == 3 && source.ZAsRelative && replacement.ZIndex == 5 && replacement.ZAsRelative &&
                finalMarker.GetGlobalRect().Encloses(replacement.GetGlobalRect()),
                "replacement source updates passive association without borrowing source draw ownership");
            viewport.RemoveChild(view);
            check(!view.HoverOpen && !finalMarker.Visible && source.ZIndex == 3 && source.ZAsRelative && replacement.ZIndex == 5 && replacement.ZAsRelative,
                "ExitTree retires panel association without native-source restoration leases");
            view.QueueFree();
        }
        finally { if (GodotObject.IsInstanceValid(view) && view.IsInsideTree()) view.CloseHover(); viewport.QueueFree(); }
    }
}
