using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search.Editors;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

internal static partial class UiSurfaceChecks
{
    // Uses the existing resource bootstrap and controlled session. No seed search,
    // deployed state or release regression is involved in this presentation suite.
    private static async Task RunMainPanelChecks(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, flags)!.GetValue(instance)!;
        void Set(object instance, string name, object? value) => instance.GetType().GetField(name, flags)!.SetValue(instance, value);
        object? Call(object instance, string name, params object?[] args)
        {
            MethodInfo method = instance.GetType().GetMethod(name, flags)!;
            return method.Invoke(instance, args.Concat(method.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray());
        }
        bool centralOnly = OS.GetCmdlineUserArgs().Contains("--central-editor-ui");
        bool frameOnly = centralOnly || OS.GetCmdlineUserArgs().Contains("--workspace-frame-ui");
        int checks = 0;
        var screenshots = new List<string>();
        var fontNames = new HashSet<string>();
        var fontWeights = new HashSet<int>();
        var refreshEvidence = new List<object>();
        void Check(bool value, string description)
        { checks++; if (!value) throw new InvalidOperationException("MainPanelUi:" + description); }
        IEnumerable<Node> Nodes(Node node)
        {
            yield return node;
            foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested;
        }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "mainpanel-ui-state", Guid.NewGuid().ToString("N")));
        host.AddChild(shell);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var references = frame;
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var session = new UiPendingSession();
        bool sessionInstalled = false;
        void Navigate(string name)
        {
            MethodInfo method = shell.GetType().GetMethod("SelectTask", flags)!;
            method.Invoke(shell, [Enum.Parse(method.GetParameters()[0].ParameterType, name), null, null]);
        }
        void RefreshTheme() => UiAppearance.Apply(shell, store.Preferences.Appearance);
        string Draft() => JsonSerializer.Serialize(frame.CaptureDraft().WithoutCapturedAuthority());
        string Preferences() => JsonSerializer.Serialize(store.Preferences);
        bool Fits(Control control)
        {
            Rect2 outer = shell.GetGlobalRect(), inner = control.GetGlobalRect();
            return inner.Position.X >= outer.Position.X - 1 && inner.Position.Y >= outer.Position.Y - 1 &&
                inner.End.X <= outer.End.X + 1 && inner.End.Y <= outer.End.Y + 1;
        }
        void FontCheck(Node page, string scenario)
        {
            bool MainPanelText(Control control)
            {
                for (Node? node = control; node is not null && node != shell; node = node.GetParent())
                {
                    Godot.Theme? theme = node is Control c ? c.Theme : node is Window w ? w.Theme : null;
                    if (theme?.HasMeta("rt2_independent_ui") == true) return false;
                }
                return true;
            }
            var visible = Nodes(page).OfType<Control>().Where(c => c.IsVisibleInTree() && MainPanelText(c) &&
                (c is Label or BaseButton or LineEdit or TextEdit or RichTextLabel)).ToArray();
            Check(visible.Length > 0, "text controls exist " + scenario);
            foreach (Control control in visible)
            {
                Font font = control.GetThemeFont(control is RichTextLabel ? "normal_font" : "font");
                string name = font.GetFontName(); fontNames.Add(name);
                string expectedFamily = MainPanelTheme.FontChoice == "game-serif" ? "Source Han Serif" : "Noto Sans Mono";
                Check(name.Contains(expectedFamily, StringComparison.OrdinalIgnoreCase),
                    "effective selected game font " + scenario + " " + control.GetPath() + " got " + name);
                fontWeights.Add(font.GetFontWeight());
                foreach (char glyph in "涅奥筛种地图先古变牌Aa09")
                    Check(font.HasChar(glyph), "font glyph U+" + ((int)glyph).ToString("X4") + " " + scenario);
            }
        }
        bool IsEncyclopediaDisclosurePair(Node page, Node parent, Control first, Control second)
        {
            // EncyclopediaCanvas.RenderRail deliberately overlays a separately
            // focused disclosure arrow on the row's reserved trailing 28px.
            // Recognize only that authored structure, not arbitrary inset buttons.
            if (page is not EncyclopediaCanvas encyclopedia ||
                parent.GetParent() != Field<ScrollContainer>(encyclopedia, "_railScroll")) return false;
            bool Matches(Control rowControl, Control toggleControl)
            {
                if (rowControl is not Button row || toggleControl is not Button toggle) return false;
                return row.Alignment == HorizontalAlignment.Left && row.ClipContents && row.Text.Length > 0 &&
                    Math.Abs(row.Size.Y - 40) < .01f && Math.Abs(row.Position.X + row.Size.X - 250) < .01f &&
                    toggle.Text.Length == 0 && toggle.FocusMode == Control.FocusModeEnum.All &&
                    toggle.GetThemeStylebox("normal") is StyleBoxEmpty &&
                    Math.Abs(toggle.Position.X - 222) < .01f && Math.Abs(toggle.Position.Y - row.Position.Y - 6) < .01f &&
                    Math.Abs(toggle.Size.X - 28) < .01f && Math.Abs(toggle.Size.Y - 28) < .01f &&
                    row.GetGlobalRect().Encloses(toggle.GetGlobalRect());
            }
            return Matches(first, second) || Matches(second, first);
        }
        void Geometry(Node page, string scenario)
        {
            Check(shell.GetNode<Control>("WorkspaceHeader").GetChildren().OfType<Button>()
                .Where(button => button.IsVisibleInTree()).All(Fits) && Fits(shell.CloseButton), "header reachable " + scenario);
            foreach (Control group in Nodes(page).OfType<Control>().Where(c => c.Name.ToString().StartsWith("SearchConditionGroup") && c is Panel && c.IsVisibleInTree()))
                foreach (Control action in Nodes(group).OfType<Control>().Where(c => c is BaseButton or SpinBox && c.IsVisibleInTree()))
                    Check(group.GetGlobalRect().Grow(1).Encloses(action.GetGlobalRect()),
                        "option contained by its group " + scenario + " " + action.GetPath() + " " + action.GetGlobalRect() + " vs " + group.GetGlobalRect());
            // Check interactive siblings, excluding decorative panels and text.
            // Scrollable content may be below its viewport, but horizontal content
            // must remain reachable when the product disables horizontal scrolling.
            foreach (Node parent in Nodes(page))
            {
                var actions = parent.GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree() &&
                    c is BaseButton or SpinBox or LineEdit && c.Size.X > 0 && c.Size.Y > 0).ToArray();
                for (int i = 0; i < actions.Length; i++)
                    for (int j = i + 1; j < actions.Length; j++)
                    {
                        Rect2 overlap = actions[i].GetGlobalRect().Intersection(actions[j].GetGlobalRect());
                        Check(overlap.Size.X <= 1 || overlap.Size.Y <= 1 ||
                            IsEncyclopediaDisclosurePair(page, parent, actions[i], actions[j]),
                            "interactive overlap " + scenario + " " + actions[i].GetPath() + " / " + actions[j].GetPath() + " " + overlap);
                    }
                foreach (Control action in actions)
                {
                    Node? ancestor = action.GetParent(); ScrollContainer? scroll = null;
                    while (ancestor is not null && ancestor != shell)
                    { if (ancestor is ScrollContainer found) { scroll = found; break; } ancestor = ancestor.GetParent(); }
                    if (scroll is null) Check(Fits(action), "action within workspace " + scenario + " " + action.GetPath());
                    else if (scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled)
                    {
                        Rect2 bounds = scroll.GetGlobalRect(), rect = action.GetGlobalRect();
                        Check(rect.Position.X >= bounds.Position.X - 2 && rect.End.X <= bounds.End.X + 2,
                            "action horizontally reachable " + scenario + " " + action.GetPath() + " " + rect + " vs " + bounds);
                    }
                }
            }
        }
        void SaveScreenshot(string name)
        {
            RenderingServer.ForceDraw();
            Image image = root.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "screenshot " + name);
            screenshots.Add(name + ".png");
            Check(image.GetWidth() == root.GetWindow().Size.X && image.GetHeight() == root.GetWindow().Size.Y,
                "screenshot has requested pixel dimensions " + name);
        }
        void RefreshEvidence(string scenario, string before, string immediate, string after, string? initial = null)
        {
            refreshEvidence.Add(new
            {
                scenario, initialPreferences = initial is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(initial),
                beforePreferences = JsonSerializer.Deserialize<JsonElement>(before),
                immediatelyAfterPreferences = JsonSerializer.Deserialize<JsonElement>(immediate),
                afterFramesPreferences = JsonSerializer.Deserialize<JsonElement>(after),
                presentation = "single-main-panel", domain = Field<string>(frame, "_selectedDomain"),
                lastSavedWorkbenchPreferences = Field<string>(frame, "_lastPreferences"),
                saveTick = Field<double>(frame, "_saveTick"),
                issues = RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Snapshot()
            });
            File.WriteAllText(Path.Combine(output, "mainpanel-refresh-evidence.json"),
                JsonSerializer.Serialize(refreshEvidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        async Task SettleWorkbenchPreferences()
        {
            // Advance the existing scheduled authoring save to the next normal
            // ProcessFrame. Its 0.75s first tick initializes WorkbenchFlags/Page;
            // that unrelated fixture initialization must precede the appearance-refresh baseline.
            Set(frame, "_saveTick", .75d);
            await Frames(3);
        }
        async Task Snapshot(string name, Node page, bool fonts = true)
        {
            await Frames(); SaveScreenshot(name);
            Geometry(page, name); if (fonts) FontCheck(page, name);
        }
        async Task RefreshPreserved(string scenario)
        {
            await SettleWorkbenchPreferences();
            string draft = Draft(), preferences = Preferences(), domain = Field<string>(frame, "_selectedDomain");
            object workspace = Field<object>(shell, "_workspace");
            object editor = Field<object>(frame, "_neowEditor"), editorDraft = Field<object>(editor, "_draft");
            Control[] actions = Nodes(frame).OfType<Control>().Where(c => c is BaseButton or SpinBox).ToArray();
            var inputs = Nodes(shell).OfType<Control>().Where(c => c is LineEdit or TextEdit)
                .Select(c => (Control: c, Text: c is LineEdit line ? line.Text : ((TextEdit)c).Text)).ToArray();
            RefreshTheme();
            string immediate = Preferences(); await Frames();
            RefreshEvidence(scenario, preferences, immediate, Preferences());
            Check(Draft() == draft && Preferences() == preferences, "theme refresh preserves query/preferences " + scenario);
            Check(Field<string>(frame, "_selectedDomain") == domain && Equals(workspace, Field<object>(shell, "_workspace")), "selected page/domain survive " + scenario);
            Check(ReferenceEquals(references, Field<QueryWorkbenchFrame>(shell, "_references")) &&
                ReferenceEquals(editor, Field<object>(frame, "_neowEditor")) && ReferenceEquals(editorDraft, Field<object>(editor, "_draft")), "same authoring instances " + scenario);
            Check(actions.All(GodotObject.IsInstanceValid), "existing input controls survive " + scenario);
            Check(inputs.All(input => GodotObject.IsInstanceValid(input.Control) &&
                (input.Control is LineEdit line ? line.Text : ((TextEdit)input.Control).Text) == input.Text),
                "unsubmitted input text survives theme refresh " + scenario);
        }
        void CheckSearchFrame(bool party)
        {
            Rect2 clear = frame.GetNode<Button>("ClearAllConditions").GetGlobalRect();
            Rect2 categories = frame.GetNode<ScrollContainer>("SearchCategoryScroll").GetGlobalRect();
            Rect2 presets = frame.GetNode<Button>("OpenPresets").GetGlobalRect();
            Rect2 ascension = frame.GetNode<OptionButton>("SearchAscension").GetGlobalRect();
            Check(ascension.End.Y <= clear.Position.Y && clear.End.Y <= categories.Position.Y &&
                categories.End.Y < presets.Position.Y, "context, clear, scrolling categories and fixed presets ordered");
            Check(Fits(frame.GetNode<Button>("OpenPresets")) && Fits(frame.GetNode<Button>("SavePreset")), "bottom presets reachable");
            foreach (Button mode in new[] { frame.GetNode<Button>("SearchMode0"), frame.GetNode<Button>("SearchMode1") })
                Check(mode.GetThemeStylebox("normal") is StyleBoxFlat box && box.BgColor.A == 0 &&
                    box.BorderWidthTop == 0 && box.BorderWidthLeft == 0 && box.BorderWidthRight == 0, "mode is a transparent tab");
            Check(!Nodes(Field<VBoxContainer>(frame, "_familyRows")).OfType<BaseButton>().Any(), "condition estimates are read-only");
            if (party)
            {
                var seats = Field<Dictionary<int, Button>>(frame, "_seatButtons");
                float rowY = seats[0].GetGlobalRect().Position.Y;
                foreach (var (seat, button) in seats)
                    Check(button.Text == $"P{seat + 1}" && button.Icon is not null &&
                        Math.Abs(button.GetGlobalRect().Position.Y - rowY) < 1,
                        "party identity is seat plus native avatar on a single row");
            }
        }
        void CheckSharedFrame(Control analysis)
        {
            Control main = (Control)analysis.FindChild("PredictorMainScroll", true, false);
            Control rail = (Control)analysis.FindChild("PredictorContextInset", true, false);
            Control auxiliaryInset = (Control)analysis.FindChild("PredictorAuxiliaryInset", true, false);
            var editor = Field<Control>(frame, "_neowEditor");
            var searchAuxiliary = frame.GetNode<Control>("SearchInformationRail");
            Check(Math.Abs(main.GetGlobalRect().Position.X - editor.GetGlobalRect().Position.X) < 1 &&
                Math.Abs(auxiliaryInset.GetGlobalRect().Position.X + 8 * shell.GetGlobalTransform().Scale.X - searchAuxiliary.GetGlobalRect().Position.X) < 1 &&
                Math.Abs(rail.Size.X - 224) < 1, "search and analysis share column content origins");
        }
        var empty = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        async Task ResponsivenessChecks()
        {
            var neow = Field<NeowSearchEditor>(frame, "_neowEditor");
            var combatBody = Field<Control>(Field<CombatRewardSearchEditor>(frame, "_combatEditor"), "_body");
            var transformBody = Field<Control>(Field<TransformationSearchEditor>(frame, "_transformationEditor"), "_body");
            int combatRenders = 0, transformRenders = 0, openingChanges = 0;
            void CombatRender(Node _) { if (combatBody.GetChildCount() == 1) combatRenders++; }
            void TransformRender(Node _) { if (transformBody.GetChildCount() == 1) transformRenders++; }
            void OpeningChange() => openingChanges++;
            void Reset() { combatRenders = transformRenders = openingChanges = 0; }
            combatBody.ChildEnteredTree += CombatRender;
            transformBody.ChildEnteredTree += TransformRender;
            neow.OpeningChanged += OpeningChange;
            try
            {
                Reset();
                var help = Nodes(neow).OfType<Button>().Single(b => b.Text is "使用说明 ▾" or "收起说明 ▴" or "Help ▾" or "Hide help ▴");
                help.EmitSignal(BaseButton.SignalName.Pressed);
                Check(openingChanges == 0 && combatRenders == 0 && transformRenders == 0,
                    "Neow guide redraw broadcasts zero semantic changes and renders C/T zero times");
                Reset();
                Call(frame, "RefreshEditorContexts");
                Check(combatRenders == 1 && transformRenders == 1 && openingChanges == 0,
                    "unchanged full editor refresh renders C/T exactly once without an opening broadcast");
                Reset();
                Call(frame, "SelectDomain", "transform"); Reset();
                frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh"));
                Check(combatRenders == 1 && transformRenders == 1,
                    "full workbench refresh on the transformation tab renders C/T exactly once");
                Call(frame, "SelectDomain", "neow");
                Reset();
                Set(frame, "_soloAscension", empty.Ascension + 1);
                Call(frame, "RefreshEditorContexts");
                Check(openingChanges == 1 && combatRenders == 1 && transformRenders == 1,
                    "real ascension context change broadcasts once and refreshes C/T once");
                Reset();
                Call(neow, "OnSourceRelicPressed", BaseGameModelKeys.Relics.LeafyPoultice);
                Check(neow.ExportQuery().OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.LeafyPoultice &&
                    openingChanges == 1 && combatRenders == 1 && transformRenders == 1,
                    "real opening edit retains C/T dependency propagation exactly once");
                Reset();
                var events = Field<EventSearchEditor>(frame, "_eventEditor");
                Call(events, "AddQueueCondition", new ModelKey("EVENT", "MORPHIC_GROVE"));
                Check(combatRenders == 0 && transformRenders == 1,
                    "event queue edits retain their independent transformation dependency");
                frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) } });
                frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh"));
                frame.ClearAllConditions();
                var transform = Field<TransformationSearchEditor>(frame, "_transformationEditor");
                transform.ImportEditorState(new(["N.LeafyPoultice"], 1, 1, [BaseGameModelKeys.Cards.Claw],
                    [new("N.LeafyPoultice", BaseGameModelKeys.Relics.LeafyPoultice, 2)]));
                string incompleteStamp = (string)Call(frame, "ConditionUndoStamp")!;
                Call(frame, "UpdateConditionActions"); frame.UndoClearConditions();
                Check(frame.GetType().GetField("_restoreClearedConditions", flags)!.GetValue(frame) is null &&
                    transform.ExportEditorState().Cards.SequenceEqual([BaseGameModelKeys.Cards.Claw]) &&
                    (string)Call(frame, "ConditionUndoStamp")! == incompleteStamp,
                    "incomplete transformation edits invalidate stale Undo without replacing authored targets");
                frame.RestoreDraft(empty); frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh"));
                await SettleWorkbenchPreferences();
                object savedFlags = Field<object>(frame, "_savedUiFlags");
                Call(frame, "PollProduction", 1d);
                Check(ReferenceEquals(savedFlags, Field<object>(frame, "_savedUiFlags")),
                    "unchanged visible preferences retain their saved snapshot without reserializing");
                // The page itself stays Visible while an ancestor is hidden.
                host.Hide();
                Check(frame.Visible && !frame.IsVisibleInTree(), "ancestor hidden fixture keeps local visibility enabled");
                Set(frame, "_saveTick", 1d);
                Set(frame, "_receiptRemaining", 2d);
                string savedDraft = Field<string>(frame, "_lastSaved"), savedPreferences = Field<string>(frame, "_lastPreferences");
                frame._Process(1d); Call(frame, "PollProduction", 1d); Call(frame, "UpdateDraftStatus");
                Check(Field<double>(frame, "_saveTick") == 1d && Field<double>(frame, "_receiptRemaining") == 2d &&
                    Field<string>(frame, "_lastSaved") == savedDraft && Field<string>(frame, "_lastPreferences") == savedPreferences &&
                    Field<string>(frame, "_analysisKey").Length == 0 && !Field<bool>(frame, "_probabilityPending"),
                    "ancestor-hidden Search does no presentation ticks, draft export or new probability submission");
                host.Show();
                frame.Hide();
                Set(frame, "_saveTick", 1d);
                frame._Process(1d); Call(frame, "PollProduction", 1d); Call(frame, "UpdateDraftStatus");
                Check(Field<double>(frame, "_saveTick") == 1d && Field<string>(frame, "_analysisKey").Length == 0,
                    "locally hidden Search also skips draft presentation and new estimates");
                frame.Show();
                Call(frame, "PollProduction", 1d);
                Check(Field<string>(frame, "_analysisKey").Length > 0,
                    "visible Search resumes its current probability request after hiding");
                GD.Print("RESPONSIVENESS_UI_COUNTS guide:C=0,T=0;full:C=1,T=1;opening:C=1,T=1;event:T=1;hidden:refresh=0");
            }
            finally
            {
                combatBody.ChildEnteredTree -= CombatRender;
                transformBody.ChildEnteredTree -= TransformRender;
                neow.OpeningChanged -= OpeningChange;
                host.Show();
                frame.RestoreDraft(empty); frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh"));
            }
        }
        async Task AuthoringChecks(string locale)
        {
            var text = JsonUiTextProvider.CreateUi13(locale);
            string QueryText() => JsonSerializer.Serialize(frame.CaptureDraft().Query);
            void Restore(SearchQuery query, string domain)
            { frame.RestoreDraft(empty with { Query = query }); frame.Refresh(locale, text); Call(frame, "SelectDomain", domain); }
            var neow = Field<NeowSearchEditor>(frame, "_neowEditor");
            var capsules = SearchQuery.Empty with
            {
                OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                    [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule], BonesRouteOrderMode.AnyOrder),
                StructuredOpeningEffects =
                [new(BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [BaseGameModelKeys.OrdinaryRelics.Whetstone]),
                 new(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
                    NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [new ModelKey("RELIC", "WAR_PAINT")])]
            };
            Restore(capsules, "neow"); string separateQuery = QueryText();
            Call(neow, "SetCapsuleAllocation", true); Call(neow, "RenderEditor");
            Check(neow.ExportEditorState().Slots.Values.Count(k => k.HasValue) == 2,
                "combined allocation keeps the original separate source requirements");
            object neowDraft = Field<object>(neow, "_draft");
            var combined = Field<ModelKey?[]>(neowDraft, "CapsuleTargets");
            Array.Clear(combined); Call(neow, "RenderEditor");
            Call(neow, "SetCapsuleAllocation", false); Call(neow, "RenderEditor");
            Check(QueryText() == separateQuery, "switching back restores separate capsule requirements");
            Call(neow, "SetCapsuleAllocation", true); Call(neow, "RenderEditor");
            Check(combined.All(k => k is null), "an intentionally emptied combined draft stays empty on return");
            await Snapshot("authoring-zh-neow-joint-empty-retained", frame);
            combined[0] = BaseGameModelKeys.OrdinaryRelics.Whetstone;
            Call(neow, "OnSourceRelicPressed", BaseGameModelKeys.Relics.SmallCapsule);
            Check(neow.ExportEditorState().JointCapsules && combined[0] == BaseGameModelKeys.OrdinaryRelics.Whetstone &&
                frame.CaptureDraft().Query.StructuredOpeningEffects.Count == 0,
                "missing capsule sibling preserves combined targets without activating saved separate targets");
            Check(Nodes(neow).OfType<Label>().Any(label => label.Text == text.Get("query.neow.results.combined_source_changed")),
                "changed combined source is identified in the current editor");
            await Snapshot("authoring-zh-neow-source-affected", frame);
            Call(neow, "OnSourceRelicPressed", BaseGameModelKeys.Relics.SmallCapsule);
            Check(frame.CaptureDraft().Query.StructuredOpeningEffects.Single().Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset &&
                frame.CaptureDraft().Query.StructuredOpeningEffects.Single().OutputKeys.Single() == BaseGameModelKeys.OrdinaryRelics.Whetstone,
                "restoring the source pair reactivates the preserved joint requirement only");

            var ancientQuery = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with
                { NeowRelics = new([], [BaseGameModelKeys.Relics.LeafyPoultice], []) } };
            Restore(ancientQuery, "ancient");
            var ancient = Field<AncientSearchEditor>(frame, "_ancientEditor");
            ancient.AdvancedOpen = true; Call(ancient, "Render"); string advancedQuery = QueryText();
            ((Button)ancient.FindChild("AncientAdvancedDisclosure", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            Check(QueryText() == advancedQuery && ancient.FindChild("AncientAdvancedSummary", true, false) is not null &&
                ancient.FindChild("AncientModeGroup", true, false) is null,
                "closing Ancient advanced input preserves active query and shows its mode summary");
            await Snapshot("authoring-zh-ancient-active-collapsed", frame);
            ((Button)ancient.FindChild("AncientAdvancedDisclosure", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            ((Button)ancient.FindChild("AncientModeAny", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            Check(frame.CaptureDraft().Query.LegacyNeow.NeowRelics.IsEmpty && ancient.AdvancedOpen &&
                ancient.FindChild("AncientModeGroup", true, false) is Button,
                "new Ancient mode starts with its own draft and keeps manually expanded advanced configuration visible");
            ((Button)ancient.FindChild("AncientModeGroup", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            Check(QueryText() == advancedQuery && ancient.AdvancedOpen && ancient.FindChild("AncientModeAny", true, false) is Button,
                "switching Ancient modes restores the authored offered-together requirement without collapsing advanced configuration");
            await Snapshot("authoring-zh-ancient-active-expanded", frame);

            Restore(SearchQuery.Empty with { EventSequenceConstraints =
                [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([new ModelKey("EVENT", "TRASH_HEAP")], [], [])),
                 new(2, null, SearchSequenceRangeMode.ExactSlot, 7, new([], [], [new ModelKey("EVENT", "TINKER_TIME")]))] }, "events");
            var events = Field<EventSearchEditor>(frame, "_eventEditor");
            events.AdvancedOpen = true; Call(events, "Render");
            var eventRows = frame.CaptureDraft().Query.EventSequenceConstraints;
            Check(eventRows[0].RangeValue == 3 && eventRows[1].RangeValue == 7 && eventRows[1].RangeMode == SearchSequenceRangeMode.ExactSlot &&
                !Nodes(events.FindChild("EventCondition0", true, false)).OfType<SpinBox>().Any() &&
                !Nodes(events.FindChild("EventCondition1", true, false)).OfType<SpinBox>().Any(),
                "selected event summaries retain independent ranges without editable range controls");
            string eventQuery = QueryText();
            ((Button)events.FindChild("EventQueueAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            Check(QueryText() == eventQuery, "closing event advanced input preserves exact and excluded requirements");
            await Snapshot("authoring-zh-events-independent-collapsed", frame);

            Restore(SearchQuery.Empty with { RelicSequenceConstraints =
                [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 3, new([BaseGameModelKeys.OrdinaryRelics.Whetstone], [], [])),
                 new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 7, new([], [], [new ModelKey("RELIC", "WAR_PAINT")]))] }, "relics");
            var relics = Field<RelicSequenceSearchEditor>(frame, "_relicEditor");
            relics.AdvancedOpen = true; Call(relics, "Render");
            var relicRows = frame.CaptureDraft().Query.RelicSequenceConstraints;
            Check(relicRows[0].RangeValue == 3 && relicRows[1].RangeValue == 7 && relicRows[1].Keys.Ban.Count == 1 &&
                !Nodes(relics.FindChild("RelicCondition0", true, false)).OfType<SpinBox>().Any() &&
                !Nodes(relics.FindChild("RelicCondition1", true, false)).OfType<SpinBox>().Any(),
                "selected relic summaries retain independent ranges and exclusion without editable range controls");
            string relicQuery = QueryText();
            ((Button)relics.FindChild("RelicSequenceAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            Check(QueryText() == relicQuery, "closing relic advanced input preserves its authored requirements");
            await Snapshot("authoring-zh-relics-independent-collapsed", frame);

            Restore(SearchQuery.Empty with
            {
                MerchantColorlessSequenceConditions =
                [new(2, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Uncommon, [new ModelKey("CARD", "FLASH_OF_STEEL"), null]),
                 new(4, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Rare, [null, new ModelKey("CARD", "APOTHEOSIS"), null, null])],
                RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Ordered, [BaseGameModelKeys.OrdinaryRelics.Whetstone, null, null])]
            }, "shop");
            var shopQuery = frame.CaptureDraft().Query;
            Check(shopQuery.MerchantColorlessSequenceConditions.Select(c => c.Count).SequenceEqual([2, 4]) &&
                shopQuery.RelicShopSequenceConditions.Single().Count == 3, "shop blocks retain independent horizons");
            await Snapshot("authoring-zh-shop-independent-ranges", frame);

            Restore(capsules with { RelicSequenceConstraints =
                [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 4, new([new ModelKey("RELIC", "STRAWBERRY")], [], []))] }, "neow");
            var compiled = frame.CaptureDraft().Compile(runtime, out _);
            var preview = WorkbenchProbabilityPreview.Compute(compiled, 30, 1, true);
            frame.DescribeProbability(compiled, preview); Call(frame, "RenderFamilyDashboard");
            var rows = Field<VBoxContainer>(frame, "_familyRows");
            Check(preview.View!.Rows.Any(row => row.Kind == RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.CapsuleRelicJoint) &&
                rows.GetChildren().Count(row => row.Name == "ConditionEstimate_N_R") == 1 &&
                !rows.GetChildren().Any(row => row.Name == "ConditionEstimate_N" || row.Name == "ConditionEstimate_R"),
                "the existing capsule joint projection is shown once without duplicated family probability rows");
            Check(!Nodes(rows).OfType<BaseButton>().Any() && Nodes(rows).OfType<Label>().Any(label =>
                label.Text.Contains(text.Get("workflow.source.neow_capsule"), StringComparison.Ordinal)),
                "family estimates are read-only and connect back to the Capsule source");
            await Snapshot("authoring-zh-family-joint-source", frame);
            Restore(SearchQuery.Empty, "neow");
        }
        var dense = empty with { Query = SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                [BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf], BonesRouteOrderMode.ExactOrder),
            RelicShopSequenceConditions = [new(2, CombatRewardSequenceOrderMode.Ordered,
                [BaseGameModelKeys.OrdinaryRelics.Whetstone, null])],
            CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                [new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.NoDrop, null), new(CombatPotionSlotRequirement.Neutral, null)]),
            RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 5,
                new([BaseGameModelKeys.OrdinaryRelics.Whetstone], [], []))],
            StandardMaps = [new(1, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 2, true)],
            TransformationAggregate = new(TransformationOpening.BonesLeafyNewLeaf, TransformationPickupOrder.LeafyThenNewLeaf,
                false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, [])
        }};
        if (centralOnly)
            dense = dense with { Query = dense.Query with
            {
                CombatCardRewards = new(6, CombatRewardSequenceOrderMode.Ordered,
                    [new ModelKey("CARD", "DARK_EMBRACE"), null, null, new ModelKey("CARD", "OFFERING"), null, null]),
                CombatPotionRewards = new(6, CombatRewardSequenceOrderMode.Ordered,
                    [new(CombatPotionSlotRequirement.DropSpecific, new ModelKey("POTION", "STRENGTH_POTION")),
                     new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.NoDrop, null),
                     new(CombatPotionSlotRequirement.Neutral, null), new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.Neutral, null)]),
                EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([new ModelKey("EVENT", "TRASH_HEAP")], [], []))]
            }};
        try
        {
            shell.Open(); await Frames(12);
            if (OS.GetCmdlineUserArgs().Contains("--product-review-ui"))
            {
                var metrics = new List<object>();
                string previousFont = MainPanelTheme.FontChoice;
                try
                {
                    foreach (string locale in new[] { "en", "zh" })
                    foreach (string fontChoice in new[] { "game-serif", "game-sans" })
                    {
                        MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                        store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                        store.SetFontChoice(fontChoice); Call(shell, "RefreshWorkspaceFont"); RefreshTheme();
                        root.GetWindow().Size = new(1280, 720); host.Size = new(1280, 720);
                        var text = JsonUiTextProvider.CreateUi13(locale);
                        var authored = SearchQuery.Empty with {
                            CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                                [new ModelKey("CARD", "DARK_EMBRACE"), null, null]),
                            CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                                [new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.NoDrop, null),
                                 new(CombatPotionSlotRequirement.Neutral, null)]) };
                        frame.RestoreDraft(empty with { Query = authored }); frame.Refresh(locale, text); Call(frame, "SelectDomain", "combat");
                        await Frames(); await AwaitPageReady(root, shell);
                        var combat = Field<CombatRewardSearchEditor>(frame, "_combatEditor");
                        string baseline = JsonSerializer.Serialize(frame.CaptureDraft().Query);
                        Button Mode(string kind) => (Button)combat.FindChild("Combat" + kind + "SpecificMode", true, false);
                        Button RangeButton(string kind, string value) => Mode(kind).GetParent().GetChildren().OfType<Button>()
                            .Single(button => button.Text == value && Math.Abs(button.Position.Y - Mode(kind).Position.Y) < 1);
                        async Task Settle() { await Frames(); await AwaitPageReady(root, shell); }
                        foreach (string kind in new[] { "Card", "Potion" })
                        {
                            var mode = Mode(kind);
                            var label = mode.GetParent().GetChildren().OfType<Label>().Single(item =>
                                item.Text == text.Get("query.combat.range") && Math.Abs(item.Position.Y - mode.Position.Y - 4) < 1);
                            var font = label.GetThemeFont("font");
                            int fontSize = label.GetThemeFontSize("font_size");
                            float glyphWidth = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, fontSize).X;
                            string scenario = locale + "-1280-" + fontChoice + "-" + kind.ToLowerInvariant();
                            Check(label.IsVisibleInTree() && glyphWidth <= label.Size.X + .5f,
                                "complete actual Combat range glyphs fit " + scenario + ": " + glyphWidth + " <= " + label.Size.X);
                            Check(MainPanelTheme.FontChoice == fontChoice && font.GetFontName().Contains(
                                fontChoice == "game-serif" ? "Source Han Serif" : "Noto Sans Mono", StringComparison.OrdinalIgnoreCase),
                                "range label uses the maintained selected font " + scenario);
                            var minus = RangeButton(kind, "−"); var plus = RangeButton(kind, "+");
                            Check(label.GetGlobalRect().End.X <= minus.GetGlobalRect().Position.X + 1 &&
                                minus.GetGlobalRect().End.X <= plus.GetGlobalRect().Position.X &&
                                plus.GetGlobalRect().End.X <= mode.GetGlobalRect().Position.X,
                                "range text and editing controls remain separately reachable " + scenario);
                            var scroll = Nodes(combat).OfType<ScrollContainer>().Single();
                            // SearchEditorLayout reparents each row into its own
                            // section panel; use the actual content-space position.
                            float rowTop = (mode.GetGlobalRect().Position.Y - scroll.GetGlobalRect().Position.Y) /
                                scroll.GetGlobalTransform().Scale.Y + scroll.ScrollVertical;
                            scroll.ScrollVertical = Math.Max(0, (int)rowTop - 24); await Frames();
                            Check(scroll.GetGlobalRect().Grow(1).Encloses(label.GetGlobalRect()) &&
                                new[] { minus, plus, mode }.All(button => scroll.GetGlobalRect().Grow(1).Encloses(button.GetGlobalRect())),
                                "both range rows can be shown completely in the small editor viewport " + scenario);
                            await Snapshot("product-review-combat-" + scenario, frame);
                            metrics.Add(new { locale, fontChoice, row = kind, text = label.Text, font = font.GetFontName(),
                                fontSize, glyphWidth, availableWidth = label.Size.X, bounds = label.GetGlobalRect().ToString() });
                            mode.EmitSignal(BaseButton.SignalName.Pressed); await Settle();
                            var changedMode = frame.CaptureDraft().Query;
                            Check((kind == "Card" ? changedMode.CombatCardRewards!.OrderMode : changedMode.CombatPotionRewards!.OrderMode)
                                == CombatRewardSequenceOrderMode.Unordered &&
                                (kind == "Card" ? changedMode.CombatPotionRewards!.OrderMode : changedMode.CombatCardRewards!.OrderMode)
                                == CombatRewardSequenceOrderMode.Ordered,
                                "specific switch changes only its existing row matching mode " + scenario);
                            Mode(kind).EmitSignal(BaseButton.SignalName.Pressed); await Settle();
                            RangeButton(kind, "+").EmitSignal(BaseButton.SignalName.Pressed); await Settle();
                            var increased = frame.CaptureDraft().Query;
                            Check((kind == "Card" ? increased.CombatCardRewards!.Count : increased.CombatPotionRewards!.Count) == 4 &&
                                (kind == "Card" ? increased.CombatPotionRewards!.Count : increased.CombatCardRewards!.Count) == 3,
                                "plus changes only its row range " + scenario);
                            RangeButton(kind, "−").EmitSignal(BaseButton.SignalName.Pressed); await Settle();
                            Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == baseline,
                                "minus and specific-mode round trips preserve all authored card/potion targets " + scenario);
                        }
                    }
                    File.WriteAllText(Path.Combine(output, "product-review-combat-checks.json"), JsonSerializer.Serialize(new {
                        status = "PASS", checks, screenshots, metrics, nativeGlyphMeasurements = true,
                        coverage = "en+zh;1280x720;both-maintained-fonts;cards+potions;range+specific-interactions",
                        ownerRuntimeAcceptance = "pending"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    GD.Print($"PRODUCT_REVIEW_COMBAT_UI_PASS checks={checks};screenshots={screenshots.Count};glyphs+rects+interactions=true");
                    return;
                }
                finally { MainPanelTheme.SelectFont(previousFont); }
            }
            if (OS.GetCmdlineUserArgs().Contains("--feedback-fixes-ui"))
            {
                foreach (string locale in new[] { "zh", "en" })
                foreach (var size in new[] { new Vector2I(1600, 900), new Vector2I(1280, 720) })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                    store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    root.GetWindow().Size = size; host.Size = size; await Frames();
                    var text = JsonUiTextProvider.CreateUi13(locale);
                    var authored = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with
                        { NeowRelics = new([], [BaseGameModelKeys.Relics.LeafyPoultice], []) } };
                    frame.RestoreDraft(empty with { Query = authored }); frame.Refresh(locale, text); Call(frame, "SelectDomain", "ancient");
                    var ancient = Field<AncientSearchEditor>(frame, "_ancientEditor");
                    ancient.AdvancedOpen = false; Call(ancient, "Render"); await Frames();
                    ((Button)ancient.FindChild("AncientAdvancedDisclosure", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    Check(ancient.AdvancedOpen, "manual Ancient expansion opens advanced controls");
                    string beforeMode = JsonSerializer.Serialize(frame.CaptureDraft().Query);
                    ((Button)ancient.FindChild("AncientModeAny", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    Check(ancient.AdvancedOpen && ancient.FindChild("AncientModeGroup", true, false) is Button,
                        "offered-any keeps advanced controls open");
                    ((Button)ancient.FindChild("AncientModeGroup", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    Check(ancient.AdvancedOpen && beforeMode == JsonSerializer.Serialize(frame.CaptureDraft().Query),
                        "offered-together restores authored condition and keeps advanced controls open");
                    frame.Refresh(locale, text); Call(frame, "SelectDomain", "ancient"); await Frames();
                    Check(ancient.AdvancedOpen, "full frame refresh retains Ancient expansion");
                    await Snapshot($"ancient-expanded-{locale}-{size.X}", frame);

                    frame.RestoreDraft(empty); frame.Refresh(locale, text); Call(frame, "SelectDomain", "boss"); await Frames();
                    var bosses = Field<Control>(frame, "_actInformationEditor");
                    ((Button)bosses.FindChild("FirstBoss3_0", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    ((Button)bosses.FindChild("SecondBoss3_1", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    var ordered = frame.CaptureDraft();
                    var ordinals = ordered.Query.LegacyWorld.BossOrdinalFilters.Where(f => f.Act == 3).OrderBy(f => f.Ordinal).ToArray();
                    Check(ordinals.Length == 2 && ordinals[0].Ordinal == 1 && ordinals[1].Ordinal == 2 &&
                        ordinals[0].Keys.Any.Single() != ordinals[1].Keys.Any.Single(), "A10 controls export separate ordered Boss targets");
                    frame.RestoreDraft(ordered); frame.Refresh(locale, text); Call(frame, "SelectDomain", "boss"); await Frames();
                    Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == JsonSerializer.Serialize(ordered.Query),
                        "ordered Boss targets survive actual frame restore");
                    Nodes(bosses).OfType<ScrollContainer>().Single().ScrollVertical = int.MaxValue;
                    await Snapshot($"double-boss-{locale}-{size.X}", frame);
                    frame.RestoreDraft(empty with { Ascension = 9 }); frame.Refresh(locale, text); Call(frame, "SelectDomain", "boss"); await Frames();
                    Check(bosses.FindChild("SecondBoss3_0", true, false) is null, "fresh A9 does not invent a second Boss slot");
                }
                File.WriteAllText(Path.Combine(output, "feedback-fixes-ui-checks.json"), JsonSerializer.Serialize(new { checks, screenshots }));
                GD.Print($"FEEDBACK_FIXES_UI_PASS checks={checks};screenshots={screenshots.Count};zh+en+1600+1280=true");
                return;
            }
            // The real Search frame is the Shell child. Localization may rebuild
            // its controls while another page is active, but cannot reveal it.
            string initialLanguage = store.Preferences.LanguageOverride;
            foreach (var destination in new[] { "Seeds", "Analysis" })
            {
                Navigate(destination);
                foreach (var locale in new[] { "zh", "en" })
                {
                    store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    await Frames(2);
                    Check(!frame.Visible && !frame.IsVisibleInTree() &&
                        Field<object>(shell, "_workspace").ToString() == destination &&
                        Field<Control>(shell, destination == "Seeds" ? "_seedLibrary" : "_predictor").Visible,
                        "language refresh keeps Search hidden on " + destination + " " + locale);
                }
            }
            store.SetLanguageOverride(initialLanguage); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
            Navigate("Search"); Call(frame, "SelectDomain", "neow"); await Frames();
            Check(frame.Visible && ReferenceEquals(frame.GetParent(), Field<Control>(shell, "_content")), "Search navigation reveals the direct frame");
            string beforeNavigation = Draft();
            Call(frame, "OpenMultiplayerConfig");
            var configuration = Field<Control>(frame, "_configOverlay");
            Check(configuration.Visible && ReferenceEquals(configuration.GetParent(), shell) && Field<Button>(shell, "_analysis").Disabled,
                "direct frame configuration owns the Shell overlay and modal navigation lock");
            Check(frame.CloseModal() && !configuration.Visible && !Field<Button>(shell, "_analysis").Disabled,
                "closing the direct frame modal restores navigation");
            var guide = Nodes(Field<Control>(frame, "_neowEditor")).OfType<Button>().Single(button => button.Text is "Guide ↗" or "百科 ↗");
            guide.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Check(!frame.Visible && Field<Control>(shell, "_encyclopediaCanvas").Visible,
                "actual Search guide delegates encyclopedia navigation to Shell");
            Navigate("Search"); await Frames();
            Check(frame.Visible && Draft() == beforeNavigation && Field<string>(frame, "_selectedDomain") == "neow",
                "returning from encyclopedia preserves Search authoring state");
            if (OS.GetCmdlineUserArgs().Contains("--item-surfaces-ui"))
            {
                var palette = WorkspacePalette.MainPanel;
                var events = Field<EventSearchEditor>(frame, "_eventEditor");
                var relics = Field<RelicSequenceSearchEditor>(frame, "_relicEditor");
                IUiTextProvider itemText = JsonUiTextProvider.CreateUi13("zh");
                void ItemRestore(SearchQuery query, string domain)
                { frame.RestoreDraft(empty with { Query = query }); frame.Refresh(store.Preferences.LanguageOverride, itemText); Call(frame, "SelectDomain", domain); }
                string ItemQuery() => JsonSerializer.Serialize(frame.CaptureDraft().Query);
                Button[] CandidateButtons(Control page) => Nodes(page).OfType<Button>()
                    .Where(button => button.HasMeta("rt2_candidate_item") && !button.Disabled).ToArray();
                void CheckCandidateSurface(Control page)
                {
                    var idle = CandidateButtons(page).Where(button => button.GetThemeStylebox("normal") is StyleBoxFlat box &&
                        box.BorderWidthLeft == 0 && box.BorderWidthTop == 0 && box.BorderWidthRight == 0 && box.BorderWidthBottom == 0).ToArray();
                    Color fill = palette.Color(palette.CandidateSurface);
                    Check(idle.Length > 0 && idle.All(button => ((StyleBoxFlat)button.GetThemeStylebox("normal")).BgColor == fill) &&
                        fill != palette.Color(palette.ContentSurface) && fill != palette.Color(palette.Canvas),
                        "idle candidates have their own palette surface without bright borders " + page.Name);
                }
                bool IsGoldCandidate(Button button) => button.GetThemeStylebox("normal") is StyleBoxFlat box &&
                    box.BorderColor == palette.Color(palette.Selected) && box.BorderWidthTop > 0;
                Dictionary<string, Rect2> BuilderGeometry(Control editor, string builder, params string[] controls) =>
                    new[] { builder }.Concat(controls).ToDictionary(name => name,
                        name => ((Control)editor.FindChild(name, true, false)).GetGlobalRect());
                void CheckBuilderGeometry(Control editor, Dictionary<string, Rect2> before)
                {
                    foreach (var (name, rect) in before)
                    {
                        Rect2 after = ((Control)editor.FindChild(name, true, false)).GetGlobalRect();
                        Check(rect.Position.DistanceTo(after.Position) < 1 && rect.Size.DistanceTo(after.Size) < 1,
                            "advanced input keeps builder and original controls in place " + name);
                    }
                }
                void ChooseParameter(Control editor, string name, int index)
                {
                    var control = (OptionButton)editor.FindChild(name, true, false);
                    control.Select(index); control.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                }
                foreach ((string locale, Vector2I size) in new[] { ("zh", new Vector2I(1600, 900)), ("en", new Vector2I(1280, 720)) })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                    store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    itemText = JsonUiTextProvider.CreateUi13(locale);
                    Navigate("Search"); root.GetWindow().Size = size; host.Size = size; await Frames();
                    foreach (var navigation in new[] { Field<Button>(shell, "_search"), Field<Button>(shell, "_status") })
                        Check(navigation.GetThemeStylebox("normal") is StyleBoxFlat style && style.BgColor.A == 0,
                            "navigation retains its transparent resting plane");
                    if (locale == "zh")
                    {
                        ItemRestore(SearchQuery.Empty, "events");
                        var catalog = Field<RolltheSpire2.Ui.Pages.Search.Event.EventSequenceSearchUiCatalog>(Field<object>(events, "_draft"), "Catalog");
                        var morphic = new ModelKey("EVENT", "MORPHIC_GROVE");
                        int act = Enumerable.Range(1, 3).First(a => catalog.CandidatesForAct(a).Any(candidate => candidate.EventKey == morphic));
                        ItemRestore(SearchQuery.Empty with { EventSequenceConstraints =
                            [new(act, null, SearchSequenceRangeMode.FirstN, 3, new([morphic], [], []))] }, "events");
                        events.AdvancedOpen = false; events.FocusEvent(morphic); await Frames();
                        Check(events.FindChild("EventQueueRangeMode", true, false) is Label && events.FindChild("EventQueuePolarity", true, false) is Label,
                            "closed event parameters are accurate labels in their original slots");
                        var positions = BuilderGeometry(events, "EventConditionBuilder", "EventQueueRangeMode", "EventQueuePolarity", "EventQueuePositionDecrease", "EventQueuePositionValue", "EventQueuePositionIncrease", "EventQueueChoose");
                        string scope = ItemQuery();
                        ((Button)events.FindChild("EventQueueAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        Check(events.FindChild("EventQueueRangeMode", true, false) is OptionButton && events.FindChild("EventQueuePolarity", true, false) is OptionButton,
                            "open event parameters replace the same labels with selectors");
                        CheckBuilderGeometry(events, positions);
                        ChooseParameter(events, "EventQueueRangeMode", 1); ChooseParameter(events, "EventQueuePolarity", 1); await Frames();
                        Check(ItemQuery() == scope, "editing event builder parameters preserves the existing event scope");
                        CheckCandidateSurface(events);
                        await Snapshot("items-zh-morphic-empty-advanced", frame);
                        var saved = events.ExportEditorState();
                        var results = saved.Results.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
                        ModelKey target = events.TransformationCardCandidates.First();
                        results[EventResultConditionKind.MorphicGroveGroupInitialBasicsContains] = [target, null];
                        events.ImportEditorState(saved with { Results = results }); events.FocusEvent(morphic); await Frames();
                        Check(CandidateButtons(events).Any(IsGoldCandidate), "requested Morphic target retains its gold selection border");
                        CheckCandidateSurface(events);
                        await Snapshot("items-zh-morphic-target-advanced", frame);
                        ((Button)events.FindChild("EventQueueAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        CheckBuilderGeometry(events, positions);
                        Check(events.FindChild("EventQueueRangeMode", true, false) is Label range && range.Text == itemText.Get("query.event.position.exact") &&
                            events.FindChild("EventQueuePolarity", true, false) is Label polarity && polarity.Text == itemText.Get("query.event.match.exclude") &&
                            frame.CaptureDraft().Query.EventSequenceConstraints.Single().RangeValue == 3,
                            "event parameter close retains changed modes and committed scope in place");
                        ItemRestore(SearchQuery.Empty, "boss"); await Frames();
                        var bosses = Field<Control>(frame, "_actInformationEditor");
                        CheckCandidateSurface(bosses);
                        CandidateButtons(bosses).First().EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        Check(CandidateButtons(bosses).Any(IsGoldCandidate), "selected Boss or variant keeps its gold condition semantics");
                        await Snapshot("items-zh-boss-candidates", frame);
                        ItemRestore(SearchQuery.Empty, "ancient"); await Frames();
                        var ancient = Field<AncientSearchEditor>(frame, "_ancientEditor");
                        ancient.AdvancedOpen = false; Call(ancient, "Render"); await Frames();
                        CheckCandidateSurface(ancient);
                        CandidateButtons(ancient).First().EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        Check(CandidateButtons(ancient).Any(IsGoldCandidate), "selected Ancient identity keeps its gold condition semantics");
                        await Snapshot("items-zh-ancient-candidates", frame);
                        Navigate("Status"); await Frames();
                        var status = Field<Control>(shell, "_statusPage");
                        await Snapshot("items-zh-runtime-status-expanded", status);
                        var sections = Field<List<(Button Header, Control Body, string Key)>>(status, "_sections");
                        // Fold a stable diagnostic section; device progress is outside this presentation check.
                        var section = sections.Single(s => s.Key == Ui1TextKey.SaveStatusGameModelTitle);
                        var labels = Nodes(section.Body).OfType<Label>().ToArray();
                        Check(section.Body.Visible && labels.Length > 0, "native status exposes readable diagnostic labels before folding");
                        string[] contents = labels.Select(label => label.Text).ToArray();
                        section.Header.ButtonPressed = false; await Frames();
                        Check(!section.Body.Visible && labels.All(GodotObject.IsInstanceValid), "status disclosure hides its body without discarding readable diagnostic content");
                        await Snapshot("items-zh-runtime-status-collapsed", status);
                        section.Header.ButtonPressed = true; await Frames();
                        Check(section.Body.Visible && labels.Select(label => label.Text).SequenceEqual(contents), "reopening status restores the same available content");
                    }
                    else
                    {
                        ItemRestore(SearchQuery.Empty with { RelicSequenceConstraints =
                            [new(RelicSequenceKind.Uncommon, SearchSequenceRangeMode.ExactSlot, 7, new([], [], [new ModelKey("RELIC", "WAR_PAINT")]))] }, "relics");
                        relics.AdvancedOpen = false; Call(relics, "Render"); await Frames();
                        Check(relics.FindChild("RelicBuilderRangeMode", true, false) is Label && relics.FindChild("RelicBuilderPolarity", true, false) is Label,
                            "closed relic parameters retain their named labels");
                        var positions = BuilderGeometry(relics, "RelicConditionBuilder", "RelicBuilderRangeMode", "RelicBuilderPolarity", "RelicBuilderPositionDecrease", "RelicBuilderPositionValue", "RelicBuilderPositionIncrease", "RelicBuilderChoose");
                        string scope = ItemQuery();
                        ((Button)relics.FindChild("RelicSequenceAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        Check(relics.FindChild("RelicBuilderRangeMode", true, false) is OptionButton && relics.FindChild("RelicBuilderPolarity", true, false) is OptionButton,
                            "open relic parameters replace the original labels with selectors");
                        ChooseParameter(relics, "RelicBuilderRangeMode", 1); ChooseParameter(relics, "RelicBuilderPolarity", 1); await Frames();
                        CheckBuilderGeometry(relics, positions);
                        Check(ItemQuery() == scope, "relic builder parameter changes leave the authored scope intact");
                        await Snapshot("items-en-relic-advanced", frame);
                        ((Button)relics.FindChild("RelicSequenceAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        CheckBuilderGeometry(relics, positions);
                        Check(relics.FindChild("RelicBuilderRangeMode", true, false) is Label range && range.Text == itemText.Get("query.relic.position.exact_n") &&
                            relics.FindChild("RelicBuilderPolarity", true, false) is Label polarity && polarity.Text == itemText.Get("query.relic.polarity.exclude") &&
                            ItemQuery() == scope, "closed relic labels display preserved matching values and independent scope");
                        await Snapshot("items-en-relic-closed", frame);
                    }
                }
                File.WriteAllText(Path.Combine(output, "item-surfaces-ui-checks.json"), JsonSerializer.Serialize(new
                { status = "PASS", checks, screenshots, fontNames, fontWeights, productionDispatch = false,
                    scope = "013 candidate surfaces, inline parameter disclosure and status presentation only", gpuActionsExercised = false,
                    statusAuthorityCoverage = "available native-host labels only", ownerGameAcceptance = "pending in aesthetics chat" },
                    new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"ITEM_SURFACES_UI_PASS checks={checks};screenshots={screenshots.Count};scoped013=true");
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--intent-compact-ui") || OS.GetCmdlineUserArgs().Contains("--intent-remaining-ui"))
            {
                // Focused 012 presentation samples. Reuse this native host and
                // its geometry/font checks; no family pricing or seed dispatch.
                var neow = Field<NeowSearchEditor>(frame, "_neowEditor");
                var ancient = Field<AncientSearchEditor>(frame, "_ancientEditor");
                var events = Field<EventSearchEditor>(frame, "_eventEditor");
                var relics = Field<RelicSequenceSearchEditor>(frame, "_relicEditor");
                var transform = Field<TransformationSearchEditor>(frame, "_transformationEditor");
                bool remainingOnly = OS.GetCmdlineUserArgs().Contains("--intent-remaining-ui");
                IUiTextProvider compactText = JsonUiTextProvider.CreateUi13("zh");
                string CompactQuery() => JsonSerializer.Serialize(frame.CaptureDraft().Query);
                void CompactRestore(SearchQuery query, string domain)
                { frame.RestoreDraft(empty with { Query = query }); frame.Refresh(store.Preferences.LanguageOverride, compactText); Call(frame, "SelectDomain", domain); }
                void CheckNeowBottoms()
                {
                    Panel Group(string name) => ((Control)((ScrollContainer)neow.FindChild(name, true, false)).GetChild(0))
                        .GetChildren().OfType<Panel>().Single();
                    Check(Math.Abs(Group("NeowSources").GetGlobalRect().End.Y - Group("NeowResults").GetGlobalRect().End.Y) < 1,
                        "Neow source and result panel bottoms align using their actual group geometry");
                }
                void CheckSelectedSummaries(Control editor, string prefix)
                {
                    for (int index = 0; index < 2; index++)
                    {
                        Node row = editor.FindChild(prefix + index, true, false);
                        Check(!Nodes(row).OfType<SpinBox>().Any() && !Nodes(row).OfType<OptionButton>().Any(),
                            "selected condition has no editable range " + prefix + index);
                    }
                }
                async Task CompactSnapshot(string name, bool tinyProbability = false, bool illustrativeFamilies = false)
                {
                    await Frames();
                    bool suspended = tinyProbability || illustrativeFamilies;
                    bool wasProcessing = frame.IsProcessing();
                    var entries = (System.Collections.IList)Field<object>(frame, "_familyEntries");
                    object[] oldEntries = entries.Cast<object>().ToArray();
                    string[] oldValues = Field<Label[]>(frame, "_expectationValues").Select(label => label.Text).ToArray();
                    if (suspended)
                    {
                        frame.SetProcess(false); // Keep the injected display sample separate from scheduled live estimates.
                        Field<WorkbenchProbabilityPreviewQueue>(frame, "_probabilityPreview").Invalidate();
                        Set(frame, "_probabilityPending", false);
                        Call(frame, "PresentExpectations", 60000d, 2.26e-27, 1234567d);
                    }
                    try
                    {
                        if (illustrativeFamilies)
                        {
                            // Synthetic display evidence only: these immutable UI
                            // rows do not estimate this fixture's query probability.
                            Type entryType = frame.GetType().GetNestedType("FamilyEntry", BindingFlags.NonPublic)!;
                            entries.Clear();
                            entries.Add(Activator.CreateInstance(entryType, ["N/R", Call(frame, "FamilyTitle", "N/R"), 2.26e-27,
                                compactText.Format("workflow.quote.gpu", "3.2M"), "", compactText.Get("workflow.source.neow_capsule"), false]));
                            entries.Add(Activator.CreateInstance(entryType, ["W", Call(frame, "FamilyTitle", "W"), null,
                                compactText.Format("workflow.quote.cpu", "520K"), "", compactText.Get("workflow.source.acts"), false]));
                            Call(frame, "RenderFamilyDashboard");
                        }
                        await Snapshot(name, frame);
                        if (!suspended) return;
                        var values = Field<Label[]>(frame, "_expectationValues");
                        Check(values[0].Text == "1 / 4.42E+26", "tiny probability retains its readable complete rarity value");
                        var statistics = (Control)frame.FindChild("SearchEstimateStatistics", true, false);
                        foreach (var value in values)
                            Check(!value.ClipText && value.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming &&
                                value.GetVisibleLineCount() == value.GetLineCount() && value.Size.Y >= value.GetMinimumSize().Y - 1 &&
                                statistics.GetGlobalRect().Grow(1).Encloses(value.GetGlobalRect()), "estimate value is fully visible within its statistics panel");
                    }
                    finally
                    {
                        if (illustrativeFamilies)
                        { entries.Clear(); foreach (var old in oldEntries) entries.Add(old); Call(frame, "RenderFamilyDashboard"); }
                        if (suspended)
                        {
                            var values = Field<Label[]>(frame, "_expectationValues");
                            for (int i = 0; i < values.Length; i++) values[i].Text = oldValues[i];
                            frame.SetProcess(wasProcessing);
                        }
                    }
                }
                foreach ((string locale, Vector2I size) in new[] { ("zh", new Vector2I(1600, 900)), ("en", new Vector2I(1280, 720)) })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                    store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    compactText = JsonUiTextProvider.CreateUi13(locale);
                    Navigate("Search"); root.GetWindow().Size = size; host.Size = size; await Frames();
                    if (locale == "zh" && !remainingOnly)
                    {
                        CompactRestore(SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) }, "neow");
                        var saved = neow.ExportEditorState();
                        var slots = new Dictionary<string, ModelKey?>(saved.Slots)
                            { [$"{BaseGameModelKeys.Relics.NewLeaf}/transform/0"] = new ModelKey("CARD", "OFFERING") };
                        neow.ImportEditorState(saved with { Slots = slots }); Call(neow, "RenderEditor");
                        await CompactSnapshot("intent-zh-neow-leafy-tiny-rarity", tinyProbability: true); CheckNeowBottoms();
                        Check(neow.ExportEditorState().Slots.ContainsKey($"{BaseGameModelKeys.Relics.NewLeaf}/transform/0") &&
                            !Nodes(neow).OfType<Label>().Any(label => label.Text == compactText.Get("query.neow.saved.heading")),
                            "inactive New Leaf draft remains stored without a retained-requirements list");
                        CompactRestore(SearchQuery.Empty with
                        {
                            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                                [BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.LeafyPoultice], BonesRouteOrderMode.ExactOrder),
                            StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.Kaleidoscope,
                                NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups,
                                NeowStructuredOutputKind.Card, [new ModelKey("CARD", "DARK_EMBRACE"), new ModelKey("CARD", "OFFERING")], AllowDuplicateOutputs: true)]
                        }, "neow");
                        await CompactSnapshot("intent-zh-neow-bones-complex"); CheckNeowBottoms();
                        CompactRestore(SearchQuery.Empty, "ancient"); ancient.AdvancedOpen = false; Call(ancient, "Render");
                        await CompactSnapshot("intent-zh-ancient-default");
                        var eligibility = Nodes(ancient).OfType<Button>().Single(button => button.Text == compactText.Get("query.ancient.eligibility.edit"));
                        var parameters = (Button)ancient.FindChild("AncientAdvancedDisclosure", true, false);
                        Check(eligibility.GetGlobalRect().Position.X < parameters.GetGlobalRect().Position.X &&
                            Math.Abs(eligibility.GetGlobalRect().Position.Y - parameters.GetGlobalRect().Position.Y) < 1 &&
                            ((Control)ancient.FindChild("AncientMatchingSettings", true, false)).Size.Y <= 96,
                            "Ancient eligibility sits upper-left with a compact upper-right parameter entry");
                    }
                    if (locale != "zh" || !remainingOnly)
                    {
                        CompactRestore(SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with
                            { NeowRelics = new([], [BaseGameModelKeys.Relics.LeafyPoultice], []) } }, "ancient");
                        ancient.AdvancedOpen = true; Call(ancient, "Render"); string authoredAncient = CompactQuery();
                        ((Button)ancient.FindChild("AncientAdvancedDisclosure", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                        Check(CompactQuery() == authoredAncient, "Ancient parameter disclosure leaves its active requirement intact");
                        ((Button)ancient.FindChild("AncientAdvancedDisclosure", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                        Check(new[] { "AncientModeBasic", "AncientModeAny", "AncientModeGroup" }.All(name => ancient.FindChild(name, true, false) is Button),
                            "all three Ancient matching modes remain available");
                        await CompactSnapshot("intent-" + locale + "-ancient-parameters");
                    }
                    if (locale == "zh")
                    {
                        CompactRestore(SearchQuery.Empty, "events");
                        var catalog = Field<RolltheSpire2.Ui.Pages.Search.Event.EventSequenceSearchUiCatalog>(Field<object>(events, "_draft"), "Catalog");
                        if (!remainingOnly)
                        {
                            var candidates = catalog.CandidatesForAct(1).Take(2).ToArray();
                            Check(candidates.Length == 2, "current event catalog supplies two native summary fixtures");
                            CompactRestore(SearchQuery.Empty with { EventSequenceConstraints =
                                [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([candidates[0].EventKey], [], [])),
                                 new(1, null, SearchSequenceRangeMode.ExactSlot, 7, new([], [], [candidates[1].EventKey]))] }, "events");
                            events.AdvancedOpen = true; Call(events, "Render"); string authoredEvents = CompactQuery();
                            ((Button)events.FindChild("EventQueueAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                            Check(CompactQuery() == authoredEvents && frame.CaptureDraft().Query.EventSequenceConstraints.Select(c => c.RangeValue).SequenceEqual([3, 7]) &&
                                frame.CaptureDraft().Query.EventSequenceConstraints[1].RangeMode == SearchSequenceRangeMode.ExactSlot &&
                                frame.CaptureDraft().Query.EventSequenceConstraints[1].Keys.Ban.Single() == candidates[1].EventKey,
                                "collapsed event input preserves independent horizons and exclusion");
                            CheckSelectedSummaries(events, "EventCondition");
                            Check(events.FindChild("EventQueuePositionDecrease", true, false) is Button && events.FindChild("EventQueuePositionIncrease", true, false) is Button,
                                "event builder exposes the shared minus/plus stepper");
                            await CompactSnapshot("intent-zh-events-two-summaries");
                            CompactRestore(SearchQuery.Empty with { RelicSequenceConstraints =
                                [new(RelicSequenceKind.Uncommon, SearchSequenceRangeMode.FirstN, 3, new([BaseGameModelKeys.OrdinaryRelics.Whetstone], [], [])),
                                 new(RelicSequenceKind.Uncommon, SearchSequenceRangeMode.ExactSlot, 7, new([], [], [new ModelKey("RELIC", "WAR_PAINT")]))] }, "relics");
                            relics.AdvancedOpen = true; Call(relics, "Render"); string authoredRelics = CompactQuery();
                            ((Button)relics.FindChild("RelicSequenceAdvanced", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                            Check(CompactQuery() == authoredRelics && frame.CaptureDraft().Query.RelicSequenceConstraints.Select(c => c.RangeValue).SequenceEqual([3, 7]) &&
                                frame.CaptureDraft().Query.RelicSequenceConstraints[1].RangeMode == SearchSequenceRangeMode.ExactSlot &&
                                frame.CaptureDraft().Query.RelicSequenceConstraints[1].Keys.Ban.Single() == new ModelKey("RELIC", "WAR_PAINT"),
                                "collapsed relic input preserves independent horizons and exclusion");
                            CheckSelectedSummaries(relics, "RelicCondition");
                            Check(relics.FindChild("RelicBuilderPositionDecrease", true, false) is Button && relics.FindChild("RelicBuilderPositionIncrease", true, false) is Button,
                                "relic builder exposes the shared minus/plus stepper");
                            await CompactSnapshot("intent-zh-relics-two-summaries");
                        }
                        string[] transformEventIds = ["MORPHIC_GROVE", "AROMA_OF_CHAOS", "WHISPERING_HOLLOW", "SYMBIOTE"];
                        var contributors = Enumerable.Range(1, 3).SelectMany(act => catalog.CandidatesForAct(act)
                            .Where(candidate => transformEventIds.Contains(candidate.EventKey.Entry)).Select(candidate => (Act: act, Key: candidate.EventKey)))
                            .DistinctBy(candidate => candidate.Key).Take(3).ToArray();
                        CompactRestore(SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
                            EventSequenceConstraints = contributors.Select(source => new EventSequenceSearchCondition(source.Act, null,
                                SearchSequenceRangeMode.FirstN, 3, new([source.Key], [], []))).ToArray() }, "transform");
                        var sources = Field<IReadOnlyList<TransformationSearchSource>>(transform, "_sources").ToArray();
                        Check(sources.Length >= 3, "multi-source transformation sample uses available Neow and event sources");
                        foreach (var source in sources) Check(transform.SetSourceTakenOver(source.Id, true), "available transformation source is accepted by its editor API");
                        ModelKey targetCard = neow.TransformationCardCandidates(0).Concat(events.TransformationCardCandidates).First();
                        transform.ImportEditorState(transform.ExportEditorState() with { Objective = 1, Cards = [targetCard] });
                        Call(frame, "RefreshTransformationContext");
                        var authoredTransform = frame.CaptureDraft().Query.TransformationAggregate!;
                        Check(authoredTransform.TargetMultiset.SequenceEqual([targetCard]) && authoredTransform.OpportunityCount == sources.Sum(source => source.ResultCount),
                            "editor export produces the target within its actual source capacity");
                        await CompactSnapshot("intent-zh-transform-multiple-sources", illustrativeFamilies: true);
                        var target = Nodes(transform).OfType<WorkspaceResultTile>().First();
                        var viewport = Nodes(transform).OfType<ScrollContainer>().Single();
                        Check(viewport.GetGlobalRect().Grow(1).Encloses(target.GetGlobalRect()), "multi-source transformation target is visible in the first viewport");
                    }
                    CompactRestore(SearchQuery.Empty with
                    {
                        CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Ordered, [new ModelKey("CARD", "DARK_EMBRACE"), null, null]),
                        CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                            [new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.NoDrop, null), new(CombatPotionSlotRequirement.Neutral, null)])
                    }, "combat");
                    var combat = Field<CombatRewardSearchEditor>(frame, "_combatEditor");
                    var cardSwitch = (Button)combat.FindChild("CombatCardSpecificMode", true, false);
                    var potionSwitch = (Button)combat.FindChild("CombatPotionSpecificMode", true, false);
                    bool SingleMatchingSwitch(Button mode) => mode.GetParent().GetChildren().OfType<Button>()
                        .Count(button => Math.Abs(button.Position.Y - mode.Position.Y) < 1) == 3; // minus, plus, one matching switch
                    Check(cardSwitch.Size.X == potionSwitch.Size.X && cardSwitch.Size.X <= 180 &&
                        cardSwitch.Size.Y == 36 && potionSwitch.Size.Y == 36 && cardSwitch.ToggleMode && potionSwitch.ToggleMode &&
                        SingleMatchingSwitch(cardSwitch) && SingleMatchingSwitch(potionSwitch),
                        "combat specificity uses matching single 180-by-36 switches like Shop");
                    cardSwitch.EmitSignal(BaseButton.SignalName.Pressed);
                    Check(frame.CaptureDraft().Query.CombatCardRewards!.OrderMode == CombatRewardSequenceOrderMode.Unordered &&
                        frame.CaptureDraft().Query.CombatPotionRewards!.OrderMode == CombatRewardSequenceOrderMode.Ordered,
                        "combat switch edits its own existing matching mode");
                    await CompactSnapshot("intent-" + locale + "-combat-switches", illustrativeFamilies: locale == "en");
                }
                File.WriteAllText(Path.Combine(output, remainingOnly ? "intent-remaining-ui-checks.json" : "intent-compact-ui-checks.json"), JsonSerializer.Serialize(new
                { status = "PASS", checks, screenshots, fontNames, fontWeights, projectionFixtureProbability = "2.26e-27",
                    remainingOnly, reusedPriorSixCaptures = remainingOnly, syntheticFamilyDisplayEvidence = new[] { "intent-zh-transform-multiple-sources", "intent-en-combat-switches" },
                    productionDispatch = false, additionalJointEstimateChecks = false, ownerGameAcceptance = "pending in aesthetics chat" },
                    new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"INTENT_COMPACT_UI_PASS checks={checks};screenshots={screenshots.Count};nativeGeometry+font+compactAuthoring=true");
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--mainpanel-preview-only"))
            {
                if (OS.GetCmdlineUserArgs().Contains("--compact-tile-preview"))
                {
                    store.SetLanguageOverride("en"); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    root.GetWindow().Size = new(1280, 720); host.Size = new(1280, 720);
                    frame.RestoreDraft(empty with { Query = SearchQuery.Empty with {
                        CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                            [new ModelKey("CARD", "DARK_EMBRACE"), null, null]) } });
                    frame.Refresh("en", JsonUiTextProvider.CreateUi13("en")); Call(frame, "SelectDomain", "combat");
                    await Frames(8);
                    foreach (var tile in Nodes(frame).OfType<WorkspaceResultTile>().Where(c => c.IsVisibleInTree()))
                        foreach (var label in Nodes(tile).OfType<Label>())
                        {
                            Check(tile.GetGlobalRect().Encloses(label.GetGlobalRect()), "compact target text remains inside its tile");
                            GD.Print($"TARGET_LABEL text={label.Text};size={label.Size};min={label.GetCombinedMinimumSize()};clip={label.ClipText};wrap={label.AutowrapMode}");
                        }
                }
                SaveScreenshot("mainpanel-preview");
                GD.Print("MAINPANEL_PREVIEW_CAPTURED");
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--encyclopedia-ui"))
            {
                foreach ((string locale, Vector2I size) in new[] { ("zh", new Vector2I(1600, 900)), ("en", new Vector2I(1280, 720)) })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                    store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    root.GetWindow().Size = size; host.Size = size;
                    Navigate("Encyclopedia");
                    var encyclopedia = Field<EncyclopediaCanvas>(shell, "_encyclopediaCanvas");
                    encyclopedia.Open(null);
                    await Snapshot($"encyclopedia-{locale}-home", encyclopedia);
                    encyclopedia.Open("seed-history");
                    await Snapshot($"encyclopedia-{locale}-article", encyclopedia);
                    var body = Nodes(Field<Control>(encyclopedia, "_main")).OfType<RichTextLabel>().ToArray();
                    Check(body.Length > 4 && body.All(label => label.GetThemeStylebox("normal") is StyleBoxEmpty),
                        "article text and contents have no inherited paragraph fill " + locale);
                    var scroll = Field<ScrollContainer>(encyclopedia, "_articleScroll");
                    scroll.ScrollVertical = 300; await Frames();
                    int savedPosition = scroll.ScrollVertical;
                    encyclopedia.Open("neow-offer"); await Frames();
                    Nodes(Field<Control>(encyclopedia, "_main")).OfType<RichTextLabel>().First()
                        .EmitSignal(RichTextLabel.SignalName.MetaClicked, "article:seed-history");
                    await Frames();
                    Check(Field<string>(encyclopedia, "_readingId") == "seed-history" &&
                        Field<ScrollContainer>(encyclopedia, "_articleScroll").ScrollVertical == savedPosition,
                        "inline navigation restores article reading position " + locale);
                    var search = Field<LineEdit>(encyclopedia, "_search"); search.Text = "Hash";
                    search.EmitSignal(LineEdit.SignalName.TextChanged, "Hash");
                    await Snapshot($"encyclopedia-{locale}-search", encyclopedia);
                    search.EmitSignal(LineEdit.SignalName.TextSubmitted, "Hash"); await Frames();
                    Check(Field<string>(encyclopedia, "_readingId") == "seed-history", "Hash search retains article navigation " + locale);
                }
                File.WriteAllText(Path.Combine(output, "encyclopedia-ui-checks.json"), JsonSerializer.Serialize(new
                    { status = "PASS", checks, screenshots, fontNames, fontWeights }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"ENCYCLOPEDIA_UI_PASS checks={checks};screenshots={screenshots.Count};zh1600+en1280+fonts+links+readingPosition+search=true");
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--analysis-layout-ui"))
            {
                // Same native card-scene registration as the existing predictor surface checks.
                Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
                Navigate("Analysis");
                var layoutAnalysis = Field<Control>(shell, "_predictor");
                var layoutContext = ProductionSearchReplay.Read<ProductionSearchReplayContext>(ProjectSettings.GlobalizePath("res://Corpus/v1/context.json")).Context;
                var layoutAuthority = layoutContext.Authority.WithWorldAuthority(layoutContext.Authority.WorldAuthority! with { EncounterNumberOfRuns = 1 });
                Check(SeedPredictionRequest.TryCreate("00000103CP3W", layoutAuthority.Character, 10, 1, 0, layoutAuthority,
                    layoutContext.EvaluationAssumptions.AncientEligibilityAssumptions, SeedPredictionDomainSelection.All,
                    SeedPredictionInputLimits.MaximumRelicSequencePreviewCount, false, out var layoutRequest, out _), "layoutAnalysis layout fixture admitted");
                var layoutDocument = RuntimeProfileRegistry.Predict(runtime.Detection, layoutRequest!);
                Call(layoutAnalysis, "ShowDocument", layoutRequest, layoutDocument, null);
                foreach ((string locale, Vector2I size) in new[] { ("zh", new Vector2I(1600, 900)), ("en", new Vector2I(1280, 720)) })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                    store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                    root.GetWindow().Size = size; host.Size = size;
                    Set(layoutAnalysis, "_wbPage", 0); Set(layoutAnalysis, "_openingSubview", true);
                    Set(layoutAnalysis, "_routeSubview", false); Set(layoutAnalysis, "_shopFullQueue", false);
                    Call(layoutAnalysis, "RenderWorkbench"); await Frames(12);
                    var tabs = Field<List<Button>>(layoutAnalysis, "_wbTabs");
                    var navigation = (Control)layoutAnalysis.FindChild("PredictorPages", true, false);
                    var body = (ScrollContainer)layoutAnalysis.FindChild("PredictorMainScroll", true, false);
                    var auxiliary = (Control)layoutAnalysis.FindChild("PredictorAuxiliaryInset", true, false);
                    var divider = (Control)layoutAnalysis.FindChild("PredictorAuxiliaryDivider", true, false);
                    Rect2 fixedNavigation = navigation.GetGlobalRect();
                    Check(tabs.Count == 4 && tabs.All(Fits), "four layoutAnalysis navigation items reachable " + locale);
                    Check(layoutAnalysis.FindChild("RoutePredictionTab", true, false) is null,
                        "seed-information route entry temporarily hidden " + locale);
                    foreach (var tab in tabs)
                    {
                        var group = tab.GetChild<HBoxContainer>(0);
                        var caption = group.GetChild<Label>(0);
                        float textWidth = Mathf.Ceil(caption.GetThemeFont("font").GetStringSize(caption.Text,
                            fontSize: caption.GetThemeFontSize("font_size")).X);
                        float previewWidth = (group.GetChildCount() - 1) * 25;
                        Check(Math.Abs(caption.Size.X - Math.Min(textWidth, Math.Max(0, group.Size.X - previewWidth))) < 1,
                            "navigation caption uses only text width or available space " + locale);
                        if (group.GetChildCount() > 1)
                        {
                            var firstPreview = group.GetChild<Control>(1);
                            var lastPreview = group.GetChild<Control>(group.GetChildCount() - 1);
                            Check(Math.Abs(firstPreview.Position.X - caption.GetRect().End.X - 5) < 1 &&
                                Math.Abs((caption.Position.X + lastPreview.GetRect().End.X) / 2 - group.Size.X / 2) < 1,
                                "navigation title and previews form one centered group " + locale);
                        }
                    }
                    var requestBar = Field<object>(layoutAnalysis, "_requestBar");
                    foreach (string fieldName in new[] { "_railSolo", "_railMulti", "_copySeed", "_randomSeed" })
                    {
                        var button = Field<Button>(requestBar, fieldName);
                        float textWidth = button.GetThemeFont("font").GetStringSize(button.Text,
                            fontSize: button.GetThemeFontSize("font_size")).X;
                        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
                            Check(textWidth + button.GetThemeStylebox(state).GetMinimumSize().X <= button.Size.X + 1,
                                "complete context button text fits " + locale + " " + fieldName + " " + state);
                    }
                    Check(Field<Button>(requestBar, "_railMulti").Text == (locale == "en" ? "Multi" : "多人"),
                        "compact multiplayer caption localized " + locale);
                    if (locale == "en") Check(Field<Button>(requestBar, "_randomSeed").Text == "Random", "complete Random action retained");
                    Check(fixedNavigation.End.X > divider.GetGlobalRect().Position.X &&
                        body.GetGlobalRect().Position.Y >= fixedNavigation.End.Y - 1 &&
                        auxiliary.GetGlobalRect().Position.Y >= fixedNavigation.End.Y - 1,
                        "navigation spans both columns above body/map " + locale);
                    var openingChoices = Field<RolltheSpire2.Presentation.Ui1.SeedAnalysisViewModel>(layoutAnalysis, "_viewModel")
                        .NeowChoices.OrderBy(choice => choice.SlotIndex).ToArray();
                    var openingRow = Field<Control>(layoutAnalysis, "_neowChoiceStrip").GetChild<Control>(0);
                    for (int choiceIndex = 0; choiceIndex < openingChoices.Length; choiceIndex++)
                    {
                        var choice = openingChoices[choiceIndex];
                        var button = openingRow.GetChild<Button>(choiceIndex);
                        bool needsInnerPickup = choice.BonesOutcome?.OutcomeGroups.Count(group => group.RelicResults.Count > 0 &&
                            !string.IsNullOrWhiteSpace(group.RepresentativeRouteId)) > 1;
                        Check(button.Icon is not null && button.Text == (needsInnerPickup ? string.Empty : choice.DisplayName),
                            "only the Bones primary requiring inner pickup hides its caption " + locale);
                        if (needsInnerPickup)
                            Check(button.Disabled && openingRow.GetChildCount() > openingChoices.Length,
                                "disabled Bones icon retains its inner pickup selectors " + locale);
                    }
                    await Snapshot($"analysis-layout-{locale}-act1", layoutAnalysis);
                    tabs[3].EmitSignal(BaseButton.SignalName.Pressed); await Frames(8);
                    Check(!auxiliary.IsVisibleInTree() && !divider.IsVisibleInTree() &&
                        Math.Abs(body.GetGlobalRect().End.X - fixedNavigation.End.X) < 1,
                        "shop preview consumes right rail with no ghost divider " + locale);
                    var shops = (GridContainer)layoutAnalysis.FindChild("ShopPreviews", true, false);
                    Check(shops.GetChildCount() == 5 && shops.Columns >= 2, "all five shops retain responsive grid " + locale);
                    var initialChestGrid = (GridContainer)layoutAnalysis.FindChild("InitialTreasureRelicEntries", true, false);
                    var profile = RuntimeProfileRegistry.Select(runtime.Detection);
                    var chestReference = TreasureRelicPredictor.PredictInitialOrderPreview(profile,
                        profile.ComputeRootSeed(layoutDocument.CanonicalSeed), layoutDocument.Sections
                            .Select(section => section.RelicSequencePrediction).First(sequence => sequence is not null)!);
                    Check(initialChestGrid.Columns == 3 && initialChestGrid.GetChildCount() == 9,
                        "solo initial chest reference shows nine ordered tiles " + locale);
                    for (int chest = 0; chest < chestReference.Entries.Count; chest++)
                    {
                        var tileLabels = Nodes(initialChestGrid.GetChild(chest)).OfType<Label>().Select(label => label.Text).ToArray();
                        Check(tileLabels.Contains((chest + 1).ToString("D2")) && tileLabels.Contains(
                            Field<RolltheSpire2.Presentation.ContentNames.IGameContentNameResolver>(layoutAnalysis, "_contentNames")
                                .Resolve(chestReference.Entries[chest].RelicKey!.Value, RolltheSpire2.Presentation.ContentNames.GameContentKind.Relic)),
                            "initial chest reference retains ordinal and identity " + locale + " " + (chest + 1));
                    }
                    var firstTwoChests = layoutAnalysis.FindChild("FirstTwoTreasurePreview", true, false);
                    Check(firstTwoChests is null, "solo shop preview omits the superseded first-two chest panel " + locale);
                    var playerBag = (VBoxContainer)layoutAnalysis.FindChild("PlayerRelicBagPreview", true, false);
                    var chestBag = (VBoxContainer)layoutAnalysis.FindChild("InitialTreasureRelicPreview", true, false);
                    for (int heading = 0; heading < 2; heading++)
                    {
                        Rect2 left = playerBag.GetChild<Control>(heading).GetGlobalRect();
                        Rect2 right = chestBag.GetChild<Control>(heading).GetGlobalRect();
                        Check(Math.Abs(left.Position.Y - right.Position.Y) < 1 && Math.Abs(left.Size.Y - right.Size.Y) < 1,
                            "paired relic preview title and premise rows align " + locale + " " + heading);
                    }
                    var rarityGroups = playerBag.GetChild<GridContainer>(2).GetChildren().OfType<VBoxContainer>().ToArray();
                    for (int row = 0; row < 3; row++)
                    {
                        var tiles = rarityGroups.Select(group => group.GetChild<VBoxContainer>(1).GetChild<Control>(row))
                            .Concat(initialChestGrid.GetChildren().OfType<Control>().Skip(row * 3).Take(3)).ToArray();
                        Rect2 reference = tiles[0].GetGlobalRect();
                        Check(tiles.All(tile => Math.Abs(tile.GetGlobalRect().Position.Y - reference.Position.Y) < 1 &&
                            Math.Abs(tile.GetGlobalRect().Size.Y - reference.Size.Y) < 1),
                            "paired relic preview item rows share position and height " + locale + " " + (row + 1));
                    }
                    // Native card scenes deliberately retain the game's own typography.
                    await Snapshot($"analysis-layout-{locale}-shops", layoutAnalysis, fonts: false);
                    body.ScrollVertical = int.MaxValue;
                    await Snapshot($"analysis-layout-{locale}-initial-chest-order", layoutAnalysis, fonts: false);
                    body.ScrollVertical = 0;
                    ((Button)layoutAnalysis.FindChild("FullRelicQueueTab", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                    await Frames(8);
                    Check(layoutAnalysis.FindChild("FirstTwoTreasurePreview", true, false) is null,
                        "full relic queue omits the superseded first-two chest panel " + locale);
                    Check(!auxiliary.IsVisibleInTree() && !divider.IsVisibleInTree() &&
                        Math.Abs(body.GetGlobalRect().End.X - fixedNavigation.End.X) < 1,
                        "full relic queue consumes right rail " + locale);
                    bool oldNames = Field<bool>(layoutAnalysis, "_relicNames");
                    ((Button)layoutAnalysis.FindChild("RelicQueueDisplayMode", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                    await Snapshot($"analysis-layout-{locale}-relic-queue", layoutAnalysis, fonts: false);
                    body.ScrollVertical = 200; await Frames();
                    Check(body.ScrollVertical > 0 && navigation.GetGlobalRect() == fixedNavigation,
                        "layoutAnalysis navigation stays fixed while queue scrolls " + locale);
                    tabs[1].EmitSignal(BaseButton.SignalName.Pressed);
                    for (int attempt = 0; attempt < 100 && layoutAnalysis.FindChild("Act2OverviewMap", true, false)?.GetChildCount() == 0; attempt++) await Frames();
                    await Snapshot($"analysis-layout-{locale}-act2-restored", layoutAnalysis);
                    Check(auxiliary.IsVisibleInTree() && divider.IsVisibleInTree() &&
                        layoutAnalysis.FindChild("Act2OverviewMap", true, false)?.GetChildCount() > 0 &&
                        body.GetGlobalRect().End.X < auxiliary.GetGlobalRect().Position.X && navigation.GetGlobalRect() == fixedNavigation,
                        "chapter switch restores split and populated map " + locale);
                    tabs[3].EmitSignal(BaseButton.SignalName.Pressed); await Frames(8);
                    Check(Field<bool>(layoutAnalysis, "_shopFullQueue") && Field<bool>(layoutAnalysis, "_relicNames") != oldNames &&
                        !auxiliary.IsVisibleInTree() && !divider.IsVisibleInTree(), "subpage/display choices retained on return " + locale);
                }
                File.WriteAllText(Path.Combine(output, "analysis-layout-checks.json"), JsonSerializer.Serialize(new
                    { status = "PASS", checks, screenshots, fontNames, fontWeights, fixedAnalysisPredictions = 1 }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"ANALYSIS_LAYOUT_PASS checks={checks};screenshots={screenshots.Count};zh1600+en1280+navigation+shops+fullQueue+restoredMap=true");
                return;
            }
            Check(shell.Theme?.HasMeta("rt2_mainpanel") == true, "main panel theme exists at creation");
            string initialPreferences = Preferences();
            await SettleWorkbenchPreferences();
            string preferences = Preferences(); RefreshTheme();
            string immediatePreferences = Preferences(); await Frames(12);
            RefreshEvidence("initial-theme-refresh", preferences, immediatePreferences, Preferences(), initialPreferences);
            SaveScreenshot("mainpanel-initial");
            Check(Preferences() == preferences, "appearance refresh does not persist preferences");
            Check(shell.FindChild("ArtStylePrototype", true, false) is null && shell.FindChild("OpenArtPrototype", true, false) is null,
                "single main panel has no retired prototype or style switch");
            store.Presets.SaveUserPreset("Main panel UI fixture", "Synthetic presentation fixture", [], frame.CapturePreset(empty));
            SeedLibraryContext seedContext = SeedLibraryContextCapture.ForSearch(empty, runtime);
            Navigate("Seeds");
            var library = Field<SeedLibraryCanvas>(shell, "_seedLibrary");
            Navigate("Search");
            Field<SeedLibraryStore>(library, "_store").Save("000000000111", seedContext, "界面测试 · presentation fixture");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("mainpanel-fixture", empty, seedContext));
            // One complete canvas plus one smaller English density sample.
            foreach ((string locale, Vector2I size) in new[] { ("zh", new Vector2I(1600, 900)), ("en", new Vector2I(1280, 720)) })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                Navigate("Search");
                root.GetWindow().Size = size; host.Size = size; await Frames();
                FontCheck(shell.GetNode<Control>("WorkspaceHeader"), "header-" + locale + "-" + size.X);
                if (locale == "zh")
                {
                    frame.RestoreDraft(empty); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                    Call(frame, "SelectDomain", "neow");
                    if (frameOnly) await ResponsivenessChecks();
                    await RefreshPreserved("simple-neow");
                    await Snapshot($"mainpanel-{locale}-{size.X}-neow-simple", frame);
                }
                if (centralOnly)
                {
                    var openingFixture = empty with { Query = SearchQuery.Empty with
                    {
                        OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                        OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                            [BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.LeafyPoultice], BonesRouteOrderMode.ExactOrder),
                        StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.Kaleidoscope,
                            NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups,
                            NeowStructuredOutputKind.Card, [new ModelKey("CARD", "DARK_EMBRACE"), new ModelKey("CARD", "OFFERING")], AllowDuplicateOutputs: true)]
                    }};
                    frame.RestoreDraft(openingFixture); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                    Call(frame, "SelectDomain", "neow");
                    await Snapshot($"central-{locale}-bones-results", frame);
                    var headerSearch = Field<Button>(shell, "_search");
                    root.GetViewport().PushInput(new InputEventMouseMotion { Position = headerSearch.GetGlobalRect().GetCenter() });
                    await Frames();
                    Check(headerSearch.GetThemeStylebox("hover") is StyleBoxFlat hover && hover.BgColor.A > 0 && hover.BorderWidthBottom == 2,
                        "navigation hover retains blue feedback and active underline " + locale);
                    if (locale == "zh") SaveScreenshot("central-header-hover");
                    root.GetViewport().PushInput(new InputEventMouseMotion { Position = new(1, 1) });
                }
                if (centralOnly)
                {
                    frame.RestoreDraft(empty); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                    foreach (string simpleDomain in new[] { "shop", "relics", "combat", "ancient", "boss", "events" })
                    {
                        if (simpleDomain == "ancient")
                        { var editor = Field<AncientSearchEditor>(frame, "_ancientEditor"); editor.AdvancedOpen = false; Call(editor, "Render"); }
                        if (simpleDomain == "events")
                        { var editor = Field<EventSearchEditor>(frame, "_eventEditor"); editor.AdvancedOpen = false; Call(editor, "Render"); }
                        if (simpleDomain == "relics")
                        { var editor = Field<RelicSequenceSearchEditor>(frame, "_relicEditor"); editor.AdvancedOpen = false; Call(editor, "Render"); }
                        Call(frame, "SelectDomain", simpleDomain); await Frames(8);
                        await Snapshot($"compact-{locale}-{simpleDomain}-default", frame, fonts: false);
                        Control visibleEditor = simpleDomain switch
                        {
                            "shop" => Field<Control>(frame, "_shopEditor"), "relics" => Field<Control>(frame, "_relicEditor"),
                            "combat" => Field<Control>(frame, "_combatEditor"), "ancient" => Field<Control>(frame, "_ancientEditor"),
                            "events" => Field<Control>(frame, "_eventEditor"),
                            _ => Field<Control>(frame, "_actInformationEditor")
                        };
                        Check(Nodes(visibleEditor).OfType<ScrollContainer>().Where(c => c.IsVisibleInTree()).All(c =>
                            c.GetVScrollBar().MaxValue <= c.GetVScrollBar().Page + 1), "default editor fits without scrolling " + locale + " " + simpleDomain);
                    }
                    Call(frame, "SelectDomain", "transform");
                    await Snapshot($"compact-{locale}-transform-default", frame, fonts: false);
                    frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) } });
                    frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale)); Call(frame, "SelectDomain", "neow");
                    await Snapshot($"compact-{locale}-neow-direct-light", frame);
                    if (locale == "zh") await AuthoringChecks(locale);
                }
                frame.RestoreDraft(dense); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                foreach (string domain in locale == "zh"
                    ? new[] { "neow", "ancient", "shop", "combat", "events", "boss", "map", "relics", "transform" }
                    : frameOnly ? new[] { "neow", "ancient", "shop", "combat", "events", "boss", "map", "relics", "transform" } : new[] { "neow", "combat", "map" })
                {
                    var tabs = Field<Dictionary<string, Button>>(frame, "_domainButtons");
                    Check(!tabs[domain].Disabled && Fits(tabs[domain]), "category tab reachable " + domain);
                    tabs[domain].EmitSignal(BaseButton.SignalName.Pressed);
                    Check(Field<string>(frame, "_selectedDomain") == domain, "real domain navigation " + domain);
                    if (locale == "zh" && domain == "neow") await RefreshPreserved(domain);
                    await Snapshot($"mainpanel-{locale}-{size.X}-{domain}", frame);
                    if (centralOnly && locale == "zh" && domain == "events")
                    {
                        var eventEditor = Field<object>(frame, "_eventEditor");
                        var eventDraft = Field<object>(eventEditor, "_draft");
                        Set(eventDraft, "ExpandedCondition", 0); Set(eventDraft, "SelectedEvent", new ModelKey("EVENT", "TRASH_HEAP"));
                        Call(eventEditor, "Render"); await Snapshot("central-zh-event-inline", frame);
                    }
                    if (centralOnly && locale == "zh" && domain == "relics")
                    {
                        var relicEditor = Field<Control>(frame, "_relicEditor");
                        var committed = relicEditor.FindChild("RelicCondition0", true, false);
                        Check(Nodes(committed).OfType<Button>().All(button => button.Text == "×") &&
                            !Nodes(committed).OfType<SpinBox>().Any(), "committed relic is a read-only summary with removal only");
                        Nodes(committed).OfType<Button>().Single(button => button.Text == "×").EmitSignal(BaseButton.SignalName.Pressed);
                        await Frames();
                        Check(frame.CaptureDraft().Query.RelicSequenceConstraints.Count == 0, "shared target removal edits only committed relic condition");
                        frame.RestoreDraft(dense); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                        Call(frame, "SelectDomain", domain);
                    }
                    if (frameOnly && locale == "zh" && domain == "ancient")
                    {
                        var ancient = Field<AncientSearchEditor>(frame, "_ancientEditor");
                        ancient.AdvancedOpen = true;
                        Call(ancient, "SelectMode", 1);
                        await Snapshot($"mainpanel-{locale}-{size.X}-ancient-advanced", frame);
                        Call(ancient, "SelectMode", 0);
                    }
                }
                if (frameOnly)
                {
                    CheckSearchFrame(false);
                    string authored = Draft();
                    frame.GetNode<Button>("ClearAllConditions").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    Field<Button>(frame, "_undoClearConditions").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    Check(Draft() == authored, "moved clear and undo preserve the authored draft");
                }
                ModelKey[] characters = [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
                    BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Regent];
                var party = empty with
                {
                    Version = 4, Mode = WorldGameMode.Multiplayer, GameVersion = runtime.Detection.NormalizedVersion,
                    Profile = runtime.Profile.ProfileId, ObservationVersion = OrderedPartyAuthority.ObservationVersion,
                    Players = characters.Select((character, slot) => new WorkbenchPlayerDraft(slot, character, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray(),
                    Query = SearchQuery.Empty with { Players = characters.Select((_, slot) => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)).ToArray() }
                };
                frame.RestoreDraft(party); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                Check(Field<Dictionary<int, Button>>(frame, "_seatButtons").Count == 4, "four seats are exposed");
                foreach (string domain in locale == "zh" ? new[] { "neow", "ancient", "map" } : new[] { "map" })
                {
                    Call(frame, "SelectDomain", domain);
                    if (domain == "map") await RefreshPreserved("4p-" + domain);
                    await Snapshot($"mainpanel-{locale}-{size.X}-4p-{domain}", frame);
                }
                if (frameOnly) CheckSearchFrame(true);
                frame.RestoreDraft(empty); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                if (frameOnly)
                {
                    Navigate("Analysis");
                    Control predictor = Field<Control>(shell, "_predictor");
                    await Snapshot($"mainpanel-{locale}-{size.X}-analysis-initial", predictor);
                    CheckSharedFrame(predictor);
                    if (centralOnly)
                    {
                        Navigate("Seeds");
                        ((Button)library.FindChild("SeedLibraryFavorites", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                        await AwaitPageReady(root, shell);
                        Check(library.IsPresentationReady, "central library presentation is ready before querying its detail entry");
                        library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>().First()
                            .EmitSignal(BaseButton.SignalName.Pressed);
                        await Snapshot($"central-{locale}-library-surfaces", library);
                    }
                    continue;
                }
                Navigate("Seeds");
                foreach (SeedLibraryCanvas.LibrarySection section in locale == "zh"
                    ? Enum.GetValues<SeedLibraryCanvas.LibrarySection>() : new[] { SeedLibraryCanvas.LibrarySection.Favorites })
                {
                    ((Button)library.FindChild("SeedLibrary" + section, true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                    await AwaitPageReady(root, shell);
                    Check(library.IsPresentationReady, "library presentation is ready before querying its detail entry " + section);
                    Check(library.Section == section, "three peer library section " + section);
                    var item = library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>().FirstOrDefault();
                    Check(item is not null, "library fixture has an actual detail entry " + section);
                    item!.EmitSignal(BaseButton.SignalName.Pressed);
                    if (locale == "zh" && section == SeedLibraryCanvas.LibrarySection.Favorites) await RefreshPreserved("library-" + section);
                    await Snapshot($"mainpanel-{locale}-{size.X}-library-{section}", library);
                }
                if (locale == "zh")
                {
                    Call(shell, "OpenSettings");
                    await AwaitPageReady(root, shell);
                    ((Button)shell.FindChild("SettingsCategory2", true, false)).EmitSignal(Button.SignalName.Pressed);
                    await AwaitPageReady(root, shell);
                    var origin = Field<LineEdit>(shell, "_originInput"); origin.Text = "000000000321"; origin.GrabFocus();
                    string cursor = store.CurrentPersistentOriginSeed; Control settings = Field<Control>(shell, "_settingsPage");
                    await RefreshPreserved("settings");
                    Check(settings.IsVisibleInTree() && origin.Text == "000000000321" && store.CurrentPersistentOriginSeed == cursor,
                        "unsubmitted settings text and cursor preserved");
                    Check(root.GetViewport().GuiGetFocusOwner() == origin, "input focus survives appearance changes");
                    await Snapshot($"mainpanel-{locale}-{size.X}-settings", settings);
                    Call(shell, "CloseSettings");
                    Navigate("Encyclopedia"); await Snapshot($"mainpanel-{locale}-{size.X}-encyclopedia", Field<Control>(shell, "_encyclopediaCanvas"));
                    Navigate("Feedback"); await AwaitPageReady(root, shell);
                    await Snapshot($"mainpanel-{locale}-{size.X}-feedback", Field<Control>(shell, "_feedbackPage"));
                    Navigate("Analysis");
                    Control predictor = Field<Control>(shell, "_predictor");
                    await RefreshPreserved("initial-analysis");
                    await Snapshot($"mainpanel-{locale}-{size.X}-analysis-initial", predictor);
                }
            }
            Navigate("Search"); Call(frame, "SelectDomain", "combat");
            await SettleWorkbenchPreferences();
            var runningDraft = frame.CaptureDraft();
            var runningPlan = ExactSearchExecutionRequestFactory.Compile(runningDraft.Compile(runtime, out _),
                new(store.CurrentNextCursorSeed, 100, 30, 1)).Plan!;
            var prepared = store.PrepareSearchStart(runningDraft, runningPlan, SeedLibraryContextCapture.ForSearch(runningDraft, runtime));
            store.Runtime.InstallStartedSession(session, runningPlan.CompiledSearch.SemanticFingerprint, prepared, root.GetTree());
            sessionInstalled = true;
            Call(frame, "PollProduction", 0d);
            string pendingDraft = Draft(), pendingPreferences = Preferences(); int polls = session.Polls;
            for (int refresh = 0; refresh < 4; refresh++) { RefreshTheme(); await Frames(); }
            Check(store.Runtime.Receiving && store.Runtime.Snapshot.Progress?.State == SearchRunState.Running && session.Polls > polls &&
                session.Cancels == 0 && session.Disposals == 0, "controlled running session continues polling without cancel/dispose");
            Check(Draft() == pendingDraft && Preferences() == pendingPreferences && Field<Button>(frame, "_clearCategory").Disabled,
                "pending session preserves query/preferences/search-state restrictions");
            int hiddenPolls = session.Polls;
            host.Hide(); await Frames();
            Check(frame.Visible && !frame.IsVisibleInTree() && session.Polls > hiddenPolls && store.Runtime.Receiving && session.Cancels == 0,
                "shared runtime continues receiving while Search is hidden by an ancestor");
            session.Finish(); store.Runtime.Tick(); Call(frame, "PollProduction", 0d);
            host.Show(); Call(frame, "PollProduction", 0d);
            Check(!store.Runtime.Receiving && store.Runtime.CleanupCount == 0 && session.Disposals == 1 &&
                store.Runtime.Snapshot.Progress?.State == SearchRunState.Completed,
                "shared owner observes controlled completion and disposes the retained session once");
            Navigate("Analysis");
            Control analysis = Field<Control>(shell, "_predictor");
            using (var mapLoad = new CancellationTokenSource())
            {
                Set(analysis, "_mapLoad", mapLoad); await RefreshPreserved("analysis-map-pending");
                Check(ReferenceEquals(mapLoad, Field<CancellationTokenSource>(analysis, "_mapLoad")) && !mapLoad.IsCancellationRequested,
                    "theme refresh retains pending analysis lifetime");
                Set(analysis, "_mapLoad", null);
            }
            // One fixed existing replay fixture supplies a real analysis document.
            // This checks entry reachability; route transactions are covered by the existing route suite.
            var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(ProjectSettings.GlobalizePath("res://Corpus/v1/context.json")).Context;
            var authority = context.Authority.WithWorldAuthority(context.Authority.WorldAuthority! with { EncounterNumberOfRuns = 1 });
            Check(SeedPredictionRequest.TryCreate("00000103CP3W", authority.Character, 10, 1, 0, authority,
                context.EvaluationAssumptions.AncientEligibilityAssumptions, SeedPredictionDomainSelection.All,
                SeedPredictionInputLimits.MaximumRelicSequencePreviewCount, false, out var request, out _), "fixed analysis fixture admitted");
            var document = RuntimeProfileRegistry.Predict(runtime.Detection, request!);
            Call(analysis, "ShowDocument", request, document, null);
            foreach ((string locale, Vector2I size) in new[] { ("zh", new Vector2I(1600, 900)), ("en", new Vector2I(1280, 720)) })
            {
                store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                root.GetWindow().Size = size; host.Size = size;
                Set(analysis, "_wbPage", 0); Set(analysis, "_openingSubview", true); Set(analysis, "_routeSubview", false); Call(analysis, "RenderWorkbench");
                await Snapshot($"mainpanel-{locale}-{size.X}-analysis-opening", analysis);
                if (frameOnly) CheckSharedFrame(analysis);
                Check(analysis.FindChild("RoutePredictionTab", true, false) is null, "seed-information route entry temporarily hidden");
                var overview = (Button)analysis.FindChild("OverviewTab", true, false);
                Check(!overview.Disabled && Fits(overview), "analysis overview entry remains reachable");
                overview.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(!Field<bool>(analysis, "_routeSubview") && !Field<bool>(analysis, "_openingSubview"), "overview opens its own surface");
                if (locale == "en") await RefreshPreserved("analysis-overview");
                await Snapshot($"mainpanel-{locale}-{size.X}-analysis-overview", analysis);
            }
            Check(session.Cancels == 0 && session.Disposals == 1, "appearance refresh never cancels; shared owner alone disposes after completion");
            File.WriteAllText(Path.Combine(output, "mainpanel-ui-checks.json"), JsonSerializer.Serialize(new
            {
                status = "PASS", checks, screenshots, fontNames, fontWeights, session.Polls, session.Cancels, session.Disposals,
                sizes = new[] { "1600x900", "1280x720" }, languages = new[] { "zh", "en" }, categories = 9,
                coverage = frameOnly ? new[] { "zh1600: 9 categories, 4p, clear/undo and Analysis", "en1280: 9 categories, 4p and Analysis", "shared columns and read-only family estimates",
                    "central: mode/source preservation, input disclosure, independent event/relic/shop ranges and capsule joint provenance" }
                    : new[] { "zh1600: all 9 categories and peer pages", "en1280: Neow/combat/map/4p/library/analysis" },
                productionDispatch = false, fixedAnalysisPredictions = 1,
                excluded = new[] { "full release regression", "result-favorite-analysis persistence transaction", "route simulation parity" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"MAINPANEL_UI_PASS checks={checks};screenshots={screenshots.Count};categories=9;zh+en+1600+1280+font+draft+input+page+session=true");
        }
        finally
        {
            // End the controlled task through its actual owner even if a preceding
            // assertion failed. Never detach it or overwrite a removed view field.
            if (sessionInstalled) { session.Finish(); store.Runtime.Tick(); }
            host.QueueFree();
            await Frames(3);
        }
    }

    private sealed class UiPendingSession : IProductionSearchSession
    {
        private readonly TaskCompletionSource _completion = new();
        public int Polls { get; private set; }
        public int Cancels { get; private set; }
        public int Disposals { get; private set; }
        public Task Completion => _completion.Task;
        private bool _cancelled;
        public void Finish() => _completion.TrySetResult();
        public void Cancel() { Cancels++; _cancelled = true; Finish(); }
        public bool TryReadCandidate(out SearchCandidate? candidate) { candidate = null; return false; }
        public SearchProgressSnapshot GetProgress()
        {
            Polls++;
            var state = !_completion.Task.IsCompleted ? SearchRunState.Running : _cancelled ? SearchRunState.Cancelled : SearchRunState.Completed;
            return new(state, 0, 0, 100, 30, 0, 0, SearchDisposition.NoMatch, "", _cancelled);
        }
        public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => new(
            new Dictionary<string, long>(), new Dictionary<string, long>(), new Dictionary<string, string>(), "", "");
        public ulong GetSafeNextOrdinal() => 0;
        public bool TryGetSafeNextOrdinal(out ulong nextOrdinal) { nextOrdinal = 0; return false; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
