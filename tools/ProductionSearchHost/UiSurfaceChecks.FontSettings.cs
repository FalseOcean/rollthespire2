using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    internal static async Task AwaitPageReady(Node root, WorkspaceShell shell)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!shell.IsCurrentPageReady)
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(60)) throw new InvalidOperationException("Requested UI page did not become ready within 60 seconds");
            await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    internal static void CheckSharedGameFonts(Action<bool, string> check)
    {
        bool serif = MainPanelTheme.FontChoice == "game-serif";
        foreach (var (shown, file) in new[]
        {
            (MainPanelTheme.Regular, serif ? "source_han_serif_sc_medium_shared.tres" : "noto_sans_mono_cjksc_regular_shared.tres"),
            (MainPanelTheme.Medium, serif ? "source_han_serif_sc_bold_shared.tres" : "noto_sans_mono_cjksc_regular_shared.tres")
        })
        {
            string path = "res://themes/fonts/zhs/" + file;
            var native = ResourceLoader.Load<FontVariation>(path)!;
            check(shown.ResourcePath == path && shown.GetInstanceId() == native.GetInstanceId(),
                "selected font is the game's cached FontVariation: " + file);
            check(shown.GetRids().Count > 0 && shown.GetRids().SequenceEqual(native.GetRids()),
                "selected font shares the native rendering RIDs: " + file);
            var source = (FontFile)native.BaseFont;
            check(source.MultichannelSignedDistanceField && source.MsdfSize == 48 && source.MsdfPixelRange == 24,
                "shared FontFile retains native MSDF import settings: " + file);
            check(Math.Abs(native.VariationTransform.X.X - .95f) < .0001f &&
                Math.Abs(native.VariationTransform.Y.Y - .95f) < .0001f &&
                native.VariationTransform.X.Y == 0 && native.VariationTransform.Y.X == 0 &&
                native.VariationTransform.Origin == Vector2.Zero && Math.Abs(native.BaselineOffset + .05f) < .0001f,
                "shared FontVariation retains native transform and baseline: " + file);
        }
    }

    private static async Task RunFontSettingsChecks(Node root, Control host, WorkspaceShell shell,
        SearchWorkspace store, QueryWorkbenchFrame frame, string state, string output)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(string name) => typeof(WorkspaceShell).GetField(name, flags)!.GetValue(shell)!;
        void Call(string name, params object[] args) => typeof(WorkspaceShell).GetMethod(name, flags)!.Invoke(shell, args);
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("FontSettings: " + message); }
        async Task Frames() { for (int i = 0; i < 5; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        bool HiddenPagesAbsent() => new[] { "_settingsPage", "_feedbackPage", "_seedLibrary", "_language", "_fontChoice" }
            .All(name => Field(name) is null);
        bool UnrequestedPagesAbsent() => new[] { "_feedbackPage", "_seedLibrary", "_language", "_fontChoice" }
            .All(name => Field(name) is null);
        Check(HiddenPagesAbsent(), "unvisited Settings, Feedback and Library have no page or settings controls");
        shell.Open();
        Check(HiddenPagesAbsent(), "opening Search does not construct hidden pages");
        ((Button)Field("_settings")).EmitSignal(Button.SignalName.Pressed);
        Check(!shell.IsCurrentPageReady && UnrequestedPagesAbsent(), "first Settings request prepares only the requested view and awaits its actual draw");
        object? requestedSettings = Field("_settingsPage");
        ((Button)Field("_search")).EmitSignal(Button.SignalName.Pressed);
        Check(shell.IsCurrentPageReady && frame.IsVisibleInTree() && UnrequestedPagesAbsent() &&
            ReferenceEquals(requestedSettings, Field("_settingsPage")),
            "real Search navigation cancels delivery while the requested view retains its own unfinished construction");
        ((Button)Field("_settings")).EmitSignal(Button.SignalName.Pressed);
        shell.TopLevelCloseRequested += shell.CleanupForTopLevelClose;
        try { shell.CloseButton.EmitSignal(Button.SignalName.Pressed); }
        finally { shell.TopLevelCloseRequested -= shell.CleanupForTopLevelClose; }
        Check(!shell.Visible && shell.IsCurrentPageReady && UnrequestedPagesAbsent() &&
            ReferenceEquals(requestedSettings, Field("_settingsPage")),
            "real close cancels delivery without constructing unrequested pages or replacing the retained partial view");
        shell.Open(); ((Button)Field("_settings")).EmitSignal(Button.SignalName.Pressed);
        await AwaitPageReady(root, shell);
        var retainedSettings = (Control)Field("_settingsPage");
        Check(retainedSettings.IsVisibleInTree() && Field("_feedbackPage") is null && Field("_seedLibrary") is null,
            "first Settings navigation constructs only its requested page");
        Check(shell.FindChild("SettingsCategoryPage0", true, false) is not null &&
            new[] { 1, 2, 3 }.All(i => shell.FindChild("SettingsCategoryPage" + i, true, false) is null) &&
            Field("_language") is null && Field("_originInput") is null && Field("_logPath") is null,
            "first Settings visit builds only its Features category");
        ((Button)shell.FindChild("SettingsCategory1", true, false)).EmitSignal(Button.SignalName.Pressed);
        Check(!shell.IsCurrentPageReady, "first General visit awaits its actual controls, text and draw");
        object? suspendedLanguage = Field("_language"), suspendedFontChoice = Field("_fontChoice");
        ((Button)shell.FindChild("SettingsCategory0", true, false)).EmitSignal(Button.SignalName.Pressed);
        await Frames();
        Check(shell.IsCurrentPageReady && (int)Field("_settingsCategory") == 0 &&
            ReferenceEquals(suspendedLanguage, Field("_language")) && ReferenceEquals(suspendedFontChoice, Field("_fontChoice")),
            "choosing Features cancels General delivery and leaves its own partial controls suspended");
        ((Button)shell.FindChild("SettingsCategory1", true, false)).EmitSignal(Button.SignalName.Pressed);
        await AwaitPageReady(root, shell);
        var choice = (OptionButton)Field("_fontChoice"); var language = (OptionButton)Field("_language");
        var sample = (Label)Field("_fontFeedback");
        Check(choice.ItemCount == 2 && choice.Selected == 1 && store.Preferences.FontChoice == "game-serif", "two choices, default game Source Han Serif");
        using (var probe = new Godot.Theme())
        {
            int changed = 0; probe.Changed += () => changed++;
            foreach (bool initiallyBlocked in new[] { false, true })
            {
                probe.SetBlockSignals(initiallyBlocked); changed = 0;
                var expected = new InvalidOperationException("theme batch exception fixture");
                Color expectedColor = initiallyBlocked ? Colors.Red : Colors.Blue;
                Exception? caught = null;
                try
                {
                    using var batch = new ThemeChangeBatch([probe]);
                    batch.MarkChanged(); probe.SetColor("font_color", "Label", expectedColor);
                    throw expected;
                }
                catch (Exception ex) { caught = ex; }
                Check(ReferenceEquals(caught, expected) && probe.IsBlockingSignals() == initiallyBlocked &&
                    changed == (initiallyBlocked ? 0 : 1) && probe.GetColor("font_color", "Label") == expectedColor,
                    "exception restores the prior signal state and emits the required final change");
            }
            probe.SetBlockSignals(false); changed = 0;
            using (var outer = new ThemeChangeBatch([probe]))
            {
                outer.MarkChanged();
                using (var inner = new ThemeChangeBatch([probe]))
                {
                    inner.MarkChanged(); probe.SetColor("font_color", "Label", Colors.Green);
                }
                Check(probe.IsBlockingSignals() && changed == 0, "nested batch preserves its outer signal block");
            }
            Check(!probe.IsBlockingSignals() && changed == 1, "nested mutation emits one final outer change");
            using (var unchanged = new ThemeChangeBatch([probe])) { }
            Check(changed == 1 && !probe.IsBlockingSignals(), "unchanged batch stays silent and restores signals");
        }
        ((Button)shell.FindChild("SettingsCategory2", true, false)).EmitSignal(Button.SignalName.Pressed);
        await AwaitPageReady(root, shell);
        var origin = (LineEdit)Field("_originInput"); origin.Text = "000000000789";
        ((Button)shell.FindChild("SettingsCategory1", true, false)).EmitSignal(Button.SignalName.Pressed);
        await AwaitPageReady(root, shell);
        Check(shell.FindChild("SettingsCategoryPage3", true, false) is null && Field("_logPath") is null,
            "visiting General and Advanced leaves Diagnostics unconstructed");
        var runtime = store.Runtime; string initialCursor = store.CurrentPersistentOriginSeed;
        var independent = new PanelContainer { Theme = MainPanelTheme.CreateClassicBoundary(), Visible = false };
        var independentLabel = new Label { Text = "Native font boundary" }; independent.AddChild(independentLabel); shell.AddChild(independent);
        Font nativeFont = independentLabel.GetThemeFont("font");
        var independentRids = nativeFont.GetRids().ToArray();
        Color independentColor = independentLabel.GetThemeColor("font_color");
        // Use the same detached native card scene and normal visual update as
        // Analysis, without constructing the lazy Analysis page or predicting.
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(NCard).Assembly);
        var nativeCard = new Control { Visible = false, ProcessMode = Node.ProcessModeEnum.Disabled };
        var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        card.Model = ModelDb.AllCards.First().ToMutable();
        card.Ready += () =>
        {
            card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            card.GetNode<Control>("%Highlight").Hide();
        };
        nativeCard.AddChild(card); shell.AddChild(nativeCard); await Frames();
        Check(card.IsNodeReady() && card.Model is { IsMutable: true, RunState: null },
            "font fixture contains a real ready native card with a detached model");
        IEnumerable<Node> Nodes(Node node)
        {
            yield return node;
            foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested;
        }
        var nativeText = Nodes(nativeCard).OfType<Control>().Where(c => c is Label or RichTextLabel)
            .Select(c => (Control: c, Key: c is RichTextLabel ? "normal_font" : "font"))
            .Select(t => (t.Control, t.Key, Font: t.Control.GetThemeFont(t.Key)))
            .Select(t => (t.Control, t.Key, t.Font, Rids: t.Font.GetRids().ToArray(),
                ColorKey: t.Control is RichTextLabel ? "default_color" : "font_color"))
            .Select(t => (t.Control, t.Key, t.Font, t.Rids, t.ColorKey, Color: t.Control.GetThemeColor(t.ColorKey))).ToArray();
        Check(nativeText.Length > 0, "native card has actual text surfaces to preserve");
        void CheckRetainedState()
        {
            Check(ReferenceEquals(Field("_references"), frame) && ReferenceEquals(store.Runtime, runtime) &&
                origin.Text == "000000000789" && store.CurrentPersistentOriginSeed == initialCursor,
                "appearance switches preserve editor, runtime and unsubmitted settings");
            Check(independentLabel.GetThemeFont("font").GetInstanceId() == nativeFont.GetInstanceId() &&
                independentLabel.GetThemeFont("font").GetRids().SequenceEqual(independentRids) &&
                independentLabel.GetThemeColor("font_color") == independentColor,
                "independent map/game theme keeps its font and color resources");
            Check(nativeText.All(t => t.Control.GetThemeFont(t.Key).GetInstanceId() == t.Font.GetInstanceId() &&
                t.Control.GetThemeFont(t.Key).GetRids().SequenceEqual(t.Rids) && t.Control.GetThemeColor(t.ColorKey) == t.Color),
                "native card text keeps its original fonts, rendering RIDs and colors");
        }
        var themes = Nodes(shell).Select(n => n is Control c ? c.Theme : n is Window w ? w.Theme : null)
            .Where(t => t?.HasMeta("rt2_mainpanel") == true).Select(t => t!)
            .DistinctBy(t => t.GetInstanceId()).ToArray();
        Check(themes.Length > 0, "notification checks observe actual main-panel themes");
        var notifications = themes.ToDictionary(t => t.GetInstanceId(), _ => 0);
        var subscriptions = themes.Select(t => (Theme: t, Handler: (Action)(() => notifications[t.GetInstanceId()]++))).ToArray();
        foreach (var subscription in subscriptions) subscription.Theme.Changed += subscription.Handler;
        void ResetNotifications() { foreach (var id in notifications.Keys) notifications[id] = 0; }
        void CheckNotifications(bool unchanged, string operation) => Check(notifications.Values.All(n => unchanged ? n == 0 : n <= 1) &&
            (unchanged || notifications.Values.Any(n => n == 1)),
            operation + (unchanged ? " leaves unchanged themes silent" : " emits at most one change per theme"));
        var firstFonts = new Dictionary<int, ulong>();
        string[] names = ["Noto Sans Mono", "Source Han Serif"];
        string[] ids = ["game-sans", "game-serif"];
        foreach (var locale in new[] { ("zh", 1), ("en", 2) })
        {
            language.Select(locale.Item2); language.EmitSignal(OptionButton.SignalName.ItemSelected, (long)locale.Item2);
            Check(choice.GetItemText(0) == (locale.Item1 == "zh" ? "游戏等宽黑体" : "Game mono sans") &&
                choice.GetItemText(1) == (locale.Item1 == "zh" ? "游戏思源宋体（默认）" : "Game Source Han Serif (default)"),
                "localized font names omit experiment letters and identify the new default");
            foreach (int width in new[] { 1600, 1280, 2560 })
            {
                root.GetWindow().Size = new Vector2I(width, width * 9 / 16); host.Size = new Vector2(width, width * 9 / 16);
                foreach (int index in new[] { 0, 1, 0 })
                {
                    bool unchanged = store.Preferences.FontChoice == ids[index]; ResetNotifications();
                    choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                    CheckNotifications(unchanged, "actual font selection"); await Frames();
                    Font shown = sample.GetThemeFont("font");
                    CheckSharedGameFonts(Check);
                    Check(shown.GetFontName().Contains(names[index]) && choice.GetThemeFont("font").GetFontName().Contains(names[index]),
                        "live labels and controls use selected font family");
                    Check(shown.GetInstanceId() == MainPanelTheme.Regular.GetInstanceId() &&
                        choice.GetThemeFont("font").GetInstanceId() == MainPanelTheme.Medium.GetInstanceId(),
                        "existing live labels and buttons use the selected shared regular and bold resources");
                    Check(store.Preferences.FontChoice == ids[index] && new SearchWorkspace(state, false).Preferences.FontChoice == ids[index],
                        "selection persists without rebuilding workspace");
                    CheckRetainedState();
                    if (firstFonts.TryGetValue(index, out ulong previous)) Check(shown.GetInstanceId() == previous, "font resources are reused");
                    else firstFonts.Add(index, shown.GetInstanceId());
                    var page = (ScrollContainer)shell.FindChild("SettingsCategoryPage1", true, false);
                    Check(page.GetHScrollBar().MaxValue <= page.GetHScrollBar().Page + 2 &&
                        shell.GetGlobalRect().End.X <= width + 1, "font choice fits the actual viewport");
                    Check(shown.HasChar('涅'), "both choices provide Chinese glyphs without a system fallback");
                    await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"font-{index}-{locale.Item1}-{width}.png"));
                }
            }
        }
        var colorChoice = (OptionButton)Field("_colorScheme");
        UiColorPreferences savedAppearance = store.Preferences.Appearance;
        var custom = new UiColorPreferences { Scheme = "custom", Background = "17202A", Panel = "243448", Accent = "D4AB63", Text = "F0E2C8" };
        try
        {
            foreach (var appearance in new[] { new UiColorPreferences(), new UiColorPreferences { Scheme = "stars" }, custom })
            {
                bool unchanged = store.Preferences.Appearance == appearance; ResetNotifications();
                if (appearance.Scheme == "custom")
                {
                    var pickers = (List<ColorPickerButton>)Field("_colorPickers");
                    string[] values = [appearance.Background, appearance.Panel, appearance.Accent, appearance.Text];
                    for (int i = 0; i < pickers.Count; i++)
                    {
                        pickers[i].Color = new Color(values[i]);
                        pickers[i].EmitSignal(ColorPickerButton.SignalName.ColorChanged, pickers[i].Color);
                    }
                    pickers[0].EmitSignal(ColorPickerButton.SignalName.PopupClosed);
                }
                else
                {
                    int index = appearance.Scheme == "ink" ? 0 : 1;
                    colorChoice.Select(index); colorChoice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                }
                CheckNotifications(unchanged, "actual color selection"); await Frames();
                Color expectedText = new(appearance.Scheme == "ink" ? "E5EDF8" : appearance.Text);
                Color expectedBackground = new(appearance.Scheme == "ink" ? "070D1A" : appearance.Background);
                Check(sample.GetThemeColor("font_color") == expectedText && ((ColorRect)Field("_background")).Color == expectedBackground,
                    "actual color selection updates existing text and background: " + appearance.Scheme);
                Check(new SearchWorkspace(state, false).Preferences.Appearance == appearance,
                    "actual color selection persists all saved colors: " + appearance.Scheme);
                CheckRetainedState();
                ResetNotifications(); UiAppearance.Apply(shell, store.Preferences.Appearance);
                CheckNotifications(true, "repeated appearance application");
                ResetNotifications(); MainPanelTheme.RefreshFonts(shell);
                CheckNotifications(true, "repeated font application");
                if (appearance.Scheme == "custom")
                {
                    ((Button)Field("_seeds")).EmitSignal(Button.SignalName.Pressed);
                    await AwaitPageReady(root, shell);
                    var library = (SeedLibraryCanvas)Field("_seedLibrary");
                    var libraryTitle = (Label)typeof(SeedLibraryCanvas).GetField("_title", flags)!.GetValue(library)!;
                    var libraryFilter = (LineEdit)typeof(SeedLibraryCanvas).GetField("_filter", flags)!.GetValue(library)!;
                    Check(library.IsVisibleInTree() && libraryTitle.Text == "Library" &&
                        libraryTitle.GetThemeFont("font").GetInstanceId() == MainPanelTheme.Regular.GetInstanceId() &&
                        libraryTitle.GetThemeColor("font_color") == expectedText && Field("_feedbackPage") is null,
                        "first Library visit uses the current English, sans and custom palette without constructing Feedback");
                    libraryFilter.Text = "retained-library-filter";
                    ((Button)Field("_settings")).EmitSignal(Button.SignalName.Pressed);
                    await AwaitPageReady(root, shell);
                    ((Button)Field("_seeds")).EmitSignal(Button.SignalName.Pressed);
                    Check(ReferenceEquals(library, Field("_seedLibrary")) && libraryFilter.Text == "retained-library-filter" &&
                        Nodes(shell).Count(n => n.Name == "SeedLibraryCanvas") == 1,
                        "repeated Library navigation retains its page and filter without duplicates");
                    ((Button)Field("_settings")).EmitSignal(Button.SignalName.Pressed);
                    await AwaitPageReady(root, shell);
                }
            }
        }
        finally { Call("ChangeAppearance", savedAppearance); }
        choice.Select(1); choice.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Call("CloseSettings"); shell.Open(); Call("OpenSettings"); await AwaitPageReady(root, shell); await Frames();
        Check(choice.Selected == 1 && sample.GetThemeFont("font").GetFontName().Contains(names[1]), "closing and reopening retains the chosen font");
        Check(ReferenceEquals(retainedSettings, Field("_settingsPage")) && Nodes(shell).Count(n => n.Name == "SettingsPage") == 1 &&
            choice.ItemCount == 2, "close and reopen retain one Settings page and its existing controls");
        choice.Select(1); choice.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        // Reuse this native host for the companion feedback-layout delivery.
        Control? retainedFeedback = null;
        foreach (var locale in new[] { ("zh", 1), ("en", 2) })
        foreach (int width in new[] { 1600, 1280 })
        {
            root.GetWindow().Size = new Vector2I(width, width * 9 / 16); host.Size = new Vector2(width, width * 9 / 16);
            language.Select(locale.Item2); language.EmitSignal(OptionButton.SignalName.ItemSelected, (long)locale.Item2);
            ((Button)Field("_feedback")).EmitSignal(Button.SignalName.Pressed);
            await AwaitPageReady(root, shell);
            retainedFeedback ??= (Control)Field("_feedbackPage");
            Check(ReferenceEquals(retainedFeedback, Field("_feedbackPage")) && retainedFeedback.IsVisibleInTree() &&
                Nodes(shell).Count(n => n.Name == "FeedbackPage") == 1,
                "first and repeated Feedback navigation retain one actual page");
            foreach (bool ready in new[] { false, true })
            {
                var issue = new RolltheSpire2.Infrastructure.Diagnostics.FeedbackIssueText("Test", "Test");
                typeof(WorkspaceShell).GetField("_feedbackResult", flags)!.SetValue(shell, ready
                    ? new RolltheSpire2.Infrastructure.Diagnostics.FeedbackBundleResult(
                        Path.Combine(output, "RT2-feedback-preview.zip"), 302264, 10, 0, false, issue, issue) : null);
                typeof(WorkspaceShell).GetField("_feedbackMessage", flags)!.SetValue(shell, ready ? "feedback.issue_opened" : "");
                Call("RefreshFeedback"); await Frames();
                var export = (Button)Field("_feedbackExport"); var details = (Button)Field("_feedbackDetailsToggle");
                Check(export.IsVisibleInTree() && export.GetGlobalRect().End.Y < host.Size.Y && details.GetGlobalRect().End.Y < host.Size.Y,
                    "feedback primary actions and disclosure fit viewport");
                Check(((Button)Field("_feedbackCopy")).Disabled == !ready, "copy remains gated by completed bundle");
                Check(export.GetThemeFont("font").GetInstanceId() == MainPanelTheme.Medium.GetInstanceId() &&
                    export.Text == RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(locale.Item1)
                        .Get(ready ? "feedback.export.again" : "feedback.export"),
                    "deferred Feedback uses the current shared font and language");
                details.EmitSignal(Button.SignalName.Pressed); await Frames();
                Check(((Control)Field("_feedbackDetails")).Visible, "details remain available");
                details.EmitSignal(Button.SignalName.Pressed); await Frames();
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"feedback-{locale.Item1}-{width}-{ready}.png"));
            }
        }
        foreach (var subscription in subscriptions) subscription.Theme.Changed -= subscription.Handler;
        // Saved configuration must be correct during construction; Open must not
        // be required to repair a theme built with a different global palette.
        foreach (var (font, appearance) in new[] { ("game-serif", new UiColorPreferences()),
            ("game-sans", new UiColorPreferences { Scheme = "stars" }), ("game-serif", custom) })
        {
            string savedState = Path.Combine(output, "saved-theme-" + appearance.Scheme);
            var saved = new SearchWorkspace(savedState, false); saved.SetFontChoice(font); saved.SetAppearance(appearance);
            string savedLanguage = appearance.Scheme == "stars" ? "zh" : "en";
            string expectedFinalFont = font;
            saved.SetLanguageOverride(savedLanguage);
            var initialized = new WorkspaceShell();
            try
            {
                initialized.InitializeWithPersistence((RolltheSpire2.Bootstrap.ModRuntimeSnapshot)Field("_runtime"), savedState);
                var initializedTitle = (Label)typeof(WorkspaceShell).GetField("_title", flags)!.GetValue(initialized)!;
                var initializedBackground = (ColorRect)typeof(WorkspaceShell).GetField("_background", flags)!.GetValue(initialized)!;
                Color expectedText = new(appearance.Scheme == "ink" ? "E5EDF8" : appearance.Text);
                Color expectedBackground = new(appearance.Scheme == "ink" ? "070D1A" : appearance.Background);
                Check(MainPanelTheme.FontChoice == font && initializedTitle.GetThemeFont("font").GetInstanceId() == MainPanelTheme.Medium.GetInstanceId() &&
                    initialized.CloseButton.GetThemeFont("font").GetInstanceId() == MainPanelTheme.Medium.GetInstanceId(),
                    "saved font is applied before the first Open: " + font);
                Check(initializedTitle.GetThemeColor("font_color") == expectedText && initializedBackground.Color == expectedBackground &&
                    initialized.Theme.GetColor("font_color", "Label") == expectedText,
                    "saved palette is applied before the first Open: " + appearance.Scheme);
                Check(typeof(WorkspaceShell).GetField("_settingsPage", flags)!.GetValue(initialized) is null,
                    "saved preferences do not eagerly create Settings");
                host.AddChild(initialized); initialized.Open();
                if (appearance.Scheme == "stars")
                {
                    var idle = System.Diagnostics.Stopwatch.StartNew();
                    while (idle.Elapsed < TimeSpan.FromMilliseconds(1200))
                        await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Check(initialized.IsCurrentPageReady &&
                        typeof(WorkspaceShell).GetField("_pageTextPreparation", flags)!.GetValue(initialized) is null &&
                        ((HashSet<int>)typeof(WorkspaceShell).GetField("_preparedPageText", flags)!.GetValue(initialized)!).Count == 0 &&
                        typeof(WorkspaceShell).GetField("_settingsPage", flags)!.GetValue(initialized) is null &&
                        typeof(WorkspaceShell).GetField("_feedbackPage", flags)!.GetValue(initialized) is null,
                        "idle keeps the current page ready without constructing or speculatively warming unvisited pages");
                }
                if (appearance.Scheme == "ink")
                {
                    var initializedWorkbench = (QueryWorkbenchFrame)typeof(WorkspaceShell).GetField("_references", flags)!.GetValue(initialized)!;
                    ((Button)initializedWorkbench.FindChild("OpenPresets", true, false)).EmitSignal(Button.SignalName.Pressed);
                    var presetLibrary = (SeedLibraryCanvas)typeof(WorkspaceShell).GetField("_seedLibrary", flags)!.GetValue(initialized)!;
                    Check(presetLibrary.IsVisibleInTree() && presetLibrary.Section == SeedLibraryCanvas.LibrarySection.Presets &&
                        typeof(WorkspaceShell).GetField("_settingsPage", flags)!.GetValue(initialized) is null,
                        "an early workbench Presets action creates the requested Library before any Settings visit");
                }
                ((Button)typeof(WorkspaceShell).GetField("_settings", flags)!.GetValue(initialized)!).EmitSignal(Button.SignalName.Pressed);
                if (appearance.Scheme == "custom")
                {
                    Check(!initialized.IsCurrentPageReady, "saved custom fixture changes preferences during unfinished Settings preparation");
                    expectedFinalFont = "game-sans"; savedLanguage = "zh";
                    var activeStore = (SearchWorkspace)typeof(WorkspaceShell).GetField("_persistence", flags)!.GetValue(initialized)!;
                    activeStore.SetFontChoice(expectedFinalFont); activeStore.SetLanguageOverride(savedLanguage);
                    initialized.Open();
                }
                await AwaitPageReady(root, initialized);
                ((Button)initialized.FindChild("SettingsCategory1", true, false)).EmitSignal(Button.SignalName.Pressed);
                await AwaitPageReady(root, initialized);
                var initializedSample = (Label)typeof(WorkspaceShell).GetField("_fontFeedback", flags)!.GetValue(initialized)!;
                var initializedLanguage = (OptionButton)typeof(WorkspaceShell).GetField("_language", flags)!.GetValue(initialized)!;
                await Frames();
                Check(MainPanelTheme.FontChoice == expectedFinalFont &&
                    initializedSample.GetThemeFont("font").GetInstanceId() == MainPanelTheme.Regular.GetInstanceId() &&
                    initializedSample.GetThemeColor("font_color") == expectedText && initializedLanguage.Selected == (savedLanguage == "zh" ? 1 : 2),
                    "deferred Settings first visit uses the saved font, palette and language");
            }
            finally { initialized.Free(); }
        }
        MainPanelTheme.SelectFont(store.Preferences.FontChoice); UiAppearance.Initialize(savedAppearance);
        independent.QueueFree(); nativeCard.QueueFree();
        GD.Print($"FONT_SETTINGS_PASS checks={checks};fonts=2;languages=zh,en;sizes=1600,1280,2560;live+persist+sharedResource+nativeRid+msdf+nativeCard+independentBoundary+savedPalettes+batchedChanges+silentNoOps+lazyPages");
    }
}
