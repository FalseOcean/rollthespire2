using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Theme;

internal static partial class UiSurfaceChecks
{
    private static async Task RunPageLifecycleChecksAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Call(object value, string name, params object?[] args)
        {
            var method = value.GetType().GetMethod(name, flags)!;
            method.Invoke(value, args.Concat(method.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray());
        }
        int checks = 0;
        void Check(bool ok, string reason) { checks++; if (!ok) throw new InvalidOperationException("PAGE_LIFECYCLE " + reason); }
        async Task Frames(int count = 1)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        IEnumerable<Node> Nodes(Node node)
        { yield return node; foreach (var child in node.GetChildren()) foreach (var nested in Nodes(child)) yield return nested; }
        var observations = new List<object>();
        void Observe(string phase) => observations.Add(new {
            phase, nodes = Nodes(root).Count(), controls = Nodes(root).OfType<Control>().Count(),
            managedBytes = GC.GetTotalMemory(false), workingSetBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,
            textureBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed),
            godotObjects = Performance.GetMonitor(Performance.Monitor.ObjectCount),
            godotResources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)
        });
        var surface = new Control { Size = new(1600, 900) }; root.AddChild(surface);
        string state = Path.Combine(output, "page-lifecycle-state", Guid.NewGuid().ToString("N"));
        var store = SearchWorkspace.Open(state); store.SetFontChoice("game-sans"); store.SetLanguageOverride("zh");
        var shell = new WorkspaceShell(); shell.InitializeWithPersistence(runtime, state); surface.AddChild(shell); shell.Open();
        async Task Ready()
        {
            for (int i = 0; i < 1800 && !shell.IsCurrentPageReady; i++) await Frames();
            Check(shell.IsCurrentPageReady, "requested complete page, including native layout, becomes usable");
            Check(Field<Dictionary<int, Exception>>(shell, "_pageBuildFailures").Count == 0,
                "usable page was not manufactured from a construction failure");
        }
        void Navigate(string page)
        {
            var method = typeof(WorkspaceShell).GetMethod("SelectTask", flags)!;
            method.Invoke(shell, [Enum.Parse(method.GetParameters()[0].ParameterType, page), null, null]);
        }
        try
        {
            shell._Process(3);
            Check(Field<object?>(shell, "_pageTextPreparation") is null,
                "a long startup frame delta does not count as idle time on a newly opened view");
            await Frames(3); Observe("first-current-page");
            var detached = new VBoxContainer { Theme = shell.Theme };
            var inheritedToggle = new CheckButton { Text = "分组字体" };
            var inheritedInput = new LineEdit { Text = "123" };
            detached.AddChild(inheritedToggle); detached.AddChild(inheritedInput);
            var fontMethod = typeof(WorkspaceShell).GetMethod("PageControlFont", flags)!;
            var plannedToggleFont = (Font)fontMethod.Invoke(shell, [inheritedToggle])!;
            var plannedInputFont = (Font)fontMethod.Invoke(shell, [inheritedInput])!;
            shell.AddChild(detached);
            Check(plannedToggleFont.GetRids().Select(r => r.Id).SequenceEqual(inheritedToggle.GetThemeFont("font").GetRids().Select(r => r.Id)) &&
                plannedInputFont.GetRids().Select(r => r.Id).SequenceEqual(inheritedInput.GetThemeFont("font").GetRids().Select(r => r.Id)),
                "detached group prepares the exact fonts later inherited from its real mounted owner");
            detached.Free();
            var pendingPanel = new PanelContainer();
            var palette = WorkspacePalette.MainPanel;
            var pendingStyle = palette.Box(palette.Surface);
            pendingPanel.AddThemeStyleboxOverride("panel", pendingStyle);
            var priorAppearance = UiAppearance.Current;
            try
            {
                UiAppearance.Apply(shell, priorAppearance with { Scheme = "custom", Panel = "21384C" }, [pendingPanel]);
                Check(pendingStyle.BgColor == palette.Color(palette.Surface),
                    "an explicitly owned detached group receives the same actual palette change as mounted controls");
            }
            finally { UiAppearance.Apply(shell, priorAppearance, [pendingPanel]); pendingPanel.Free(); }
            // Actual native cache identity, independent of a page's scheduling.
            string[] corpus = ["问题反馈", "导出诊断包", "Report a problem", "Export diagnostics"];
            using (var prepare = new MainPanelFontPreparation(MainPanelTheme.Regular, MainPanelTheme.Medium, corpus))
            {
                for (int i = 0; i < 1800 && !prepare.IsCompleted; i++) { prepare.Advance(); await Frames(); }
                Check(prepare.IsCompleted, "native fixed-page preparation completes its actual remaining glyph work");
            }
            using (var warm = new MainPanelFontPreparation(MainPanelTheme.Regular, MainPanelTheme.Medium, corpus))
                Check(warm.Advance(TimeSpan.FromSeconds(1)) && warm.RenderedGlyphCount == 0,
                    "a fully warm corpus is exhausted within its deadline without a character-count frame quota");
            var server = TextServerManager.GetPrimaryInterface();
            var rid = MainPanelTheme.Regular.GetRids()[0];
            var size = new Vector2I((int)server.FontGetMsdfSize(rid), 0);
            var cached = server.FontGetGlyphList(rid, size).Select(glyph => (long)glyph).ToHashSet();
            string coldText = string.Concat(Enumerable.Range(0x4e00, 0xa000 - 0x4e00)
                .Where(code => server.FontGetGlyphIndex(rid, size.X, code, 0) is var glyph && glyph != 0 && !cached.Contains(glyph))
                .Take(2).Select(char.ConvertFromUtf32));
            Check(coldText.Length == 2, "fixture has genuine supported native cache misses");
            using (var cold = new MainPanelFontPreparation(MainPanelTheme.Regular, MainPanelTheme.Medium, [coldText]))
            {
                Check(!cold.Advance(TimeSpan.FromTicks(1)) && cold.RenderedGlyphCount > 0,
                    "cold native work advances but yields with actual glyph work still pending");
                int rendered = cold.RenderedGlyphCount;
                cold.Cancel();
                Check(cold.IsCancelled && cold.Advance() && cold.RenderedGlyphCount == rendered,
                    "cancelled native preparation cannot resume rendering");
            }
            // Isolate control construction from the separately measured native
            // font preparation: these are existing production cache identities.
            Call(shell, "RefreshPageTextContext");
            Field<HashSet<int>>(shell, "_preparedPageText").UnionWith([0, 1, 2, 3, 4]);
            Call(shell, "OpenSettings");
            Check(!shell.IsCurrentPageReady && Field<Control?>(shell, "_settingsPage") is not null,
                "font-ready navigation still waits for actual construction and native draw delivery");
            Navigate("Feedback"); await Frames(2); Navigate("Search"); await Frames(12);
            Check(Field<object>(shell, "_workspace").ToString() == "Search" && shell.IsCurrentPageReady,
                "abandoned construction never completes an old navigation");
            bool witnessedMounted = false;
            void ObserveMount()
            {
                if (!Field<bool>(shell, "_pageNavigationApplied")) return;
                witnessedMounted = true;
                Check(!shell.IsCurrentPageReady, "mounted controls remain pending before their native draw");
            }
            RenderingServer.FramePreDraw += ObserveMount;
            try { Call(shell, "OpenSettings"); await Ready(); }
            finally { RenderingServer.FramePreDraw -= ObserveMount; }
            Check(witnessedMounted && Field<Action?>(shell, "_pageLayoutPreDraw") is null && Field<Action?>(shell, "_pageLayoutPostDraw") is null,
                "native draw delivers readiness and releases both request-owned callbacks");
            var settings = Field<Control>(shell, "_settingsPage");
            Call(shell, "SelectSettingsCategory", 2); await Ready();
            var origin = Field<LineEdit>(shell, "_originInput"); origin.Text = "000000000789";
            Navigate("Feedback"); await Ready();
            var feedback = Field<Control>(shell, "_feedbackPage");
            Call(shell, "OpenSettings"); await Ready();
            Check(ReferenceEquals(settings, Field<Control>(shell, "_settingsPage")) && origin.Text == "000000000789",
                "cross-page reuse preserves the actual uncommitted input");
            Call(shell, "SelectSettingsCategory", 1); await Frames(2);
            store.SetLanguageOverride("en"); Call(shell, "RefreshLanguage");
            var changedText = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en");
            Check(Field<List<(Label Label, string Key)>>(shell, "_settingsLabels").All(item => item.Label.Text == changedText.Get(item.Key)),
                "language invalidation updates every already-authored label, including queued controls, before resuming construction");
            Call(shell, "SelectSettingsCategory", 3); await Ready();
            Check(Field<int>(shell, "_settingsCategory") == 3 &&
                Field<Button>(shell, "_settingsBack").Text == "Back",
                "context change and newer category supersede the original delivery");
            Call(shell, "SelectSettingsCategory", 1); await Ready();
            Check(Field<OptionButton>(shell, "_fontChoice").GetItemText(0) == changedText.Get("settings.font.game-sans"),
                "resumed General construction binds its new-language option text before full delivery");
            Navigate("Seeds");
            Call(shell, "StopPagePreparation");
            Field<HashSet<int>>(shell, "_preparedPageText").Clear();
            typeof(WorkspaceShell).GetField("_pagePreparationIdleAt", flags)!.SetValue(shell,
                System.Diagnostics.Stopwatch.GetTimestamp() - 3 * System.Diagnostics.Stopwatch.Frequency);
            shell._Process(0);
            Check(!shell.IsCurrentPageReady && Field<object?>(shell, "_pageTextPreparation") is null,
                "background font work yields to the actual current Library presentation");
            await Ready();
            Call(shell, "OpenSettings"); await Ready();
            Observe("all-fixed-pages-created");
            int warmNodes = Nodes(shell).Count();
            var initialRuntime = store.Runtime;
            for (int i = 0; i < 8; i++)
            {
                Navigate("Feedback"); await Ready();
                Call(shell, "OpenSettings"); await Ready();
                Call(shell, "SelectSettingsCategory", i % 4); await Ready();
                shell.CleanupForTopLevelClose(); shell.Open(); await Frames(2);
            }
            await Frames(4); Observe("after-eight-reuse-cycles");
            Check(Nodes(shell).Count() == warmNodes && ReferenceEquals(feedback, Field<Control>(shell, "_feedbackPage")) &&
                ReferenceEquals(initialRuntime, store.Runtime) && origin.Text == "000000000789",
                "repeated navigation and real closes preserve instances, drafts and business owner without node growth");
            GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(4); Observe("after-eight-reuse-cycles-collected");
            Check(Field<Dictionary<int, IEnumerator<object?>>>(shell, "_pageViewBuilds").Count == 0,
                "completed views release all construction iterators");
            Navigate("Search");
            shell.CleanupForTopLevelClose();
            Check(!shell.Visible && shell.IsCurrentPageReady, "real close revokes pending page delivery");
        }
        finally
        {
            surface.Free(); await Frames(4); Observe("after-shell-retirement");
        }
        Check(Field<Dictionary<int, IEnumerator<object?>>>(shell, "_pageViewBuilds").Count == 0 &&
            Field<List<(Control, Control)>>(shell, "_pageBuildGroups").Count == 0,
            "retirement releases queued construction and unmounted resource owners");
        var abandonedSurface = new Control { Size = new(1600, 900) }; root.AddChild(abandonedSurface);
        var abandoned = new WorkspaceShell();
        SearchWorkspace.Open(state + "-partial").SetFontChoice("game-sans");
        abandoned.InitializeWithPersistence(runtime, state + "-partial"); abandonedSurface.AddChild(abandoned); abandoned.Open();
        Call(abandoned, "RefreshPageTextContext");
        Field<HashSet<int>>(abandoned, "_preparedPageText").Add(4);
        var task = typeof(WorkspaceShell).GetMethod("SelectTask", flags)!;
        task.Invoke(abandoned, [Enum.Parse(task.GetParameters()[0].ParameterType, "Feedback"), null, null]);
        Check(!abandoned.IsCurrentPageReady && Field<Action?>(abandoned, "_pendingPageNavigation") is not null,
            "retirement fixture has actual unfinished construction or native layout delivery");
        abandonedSurface.Free(); await Frames(4);
        Check(Field<Dictionary<int, IEnumerator<object?>>>(abandoned, "_pageViewBuilds").Count == 0,
            "retirement disposes unfinished construction without a late navigation");
        Check(Field<Action?>(abandoned, "_pageLayoutPreDraw") is null && Field<Action?>(abandoned, "_pageLayoutPostDraw") is null,
            "retirement releases draw callbacks before a later render can deliver an abandoned request");

        var boundarySurface = new Control { Size = new(1600, 900) }; root.AddChild(boundarySurface);
        var boundary = new WorkspaceShell();
        var boundaryStore = SearchWorkspace.Open(state + "-boundary"); boundaryStore.SetFontChoice("game-sans");
        boundaryStore.SetLanguageOverride("zh");
        boundary.InitializeWithPersistence(runtime, state + "-boundary"); boundarySurface.AddChild(boundary); boundary.Open();
        Call(boundary, "RefreshPageTextContext");
        Field<HashSet<int>>(boundary, "_preparedPageText").UnionWith([0, 1, 2, 3, 4]);
        void BoundaryNavigate(string page) => task.Invoke(boundary, [Enum.Parse(task.GetParameters()[0].ParameterType, page), null, null]);
        async Task BoundaryReady()
        { for (int i = 0; i < 1800 && !boundary.IsCurrentPageReady; i++) await Frames(); Check(boundary.IsCurrentPageReady, "boundary fixture completes real rendering"); }
        bool cancelledAtDraw = false;
        void CancelAtDraw()
        {
            if (cancelledAtDraw || !Field<bool>(boundary, "_pageNavigationApplied")) return;
            cancelledAtDraw = true; BoundaryNavigate("Search");
        }
        RenderingServer.FramePreDraw += CancelAtDraw;
        try
        {
            BoundaryNavigate("Feedback");
            for (int i = 0; i < 1800 && !cancelledAtDraw; i++) await Frames();
        }
        finally { RenderingServer.FramePreDraw -= CancelAtDraw; }
        await Frames(2);
        Check(cancelledAtDraw && boundary.IsCurrentPageReady && Field<object>(boundary, "_workspace").ToString() == "Search" &&
            Field<Action?>(boundary, "_pageLayoutPreDraw") is null && Field<Action?>(boundary, "_pageLayoutPostDraw") is null,
            "cancellation at native layout invalidates both callbacks and cannot replay the abandoned navigation");
        bool changedAtDraw = false, withheldOldContext = false;
        void ChangeAtDraw()
        {
            if (changedAtDraw || !Field<bool>(boundary, "_pageNavigationApplied")) return;
            changedAtDraw = true; boundaryStore.SetLanguageOverride("en"); Call(boundary, "RefreshLanguage");
        }
        void ObserveChangedDraw()
        { if (changedAtDraw && !withheldOldContext) withheldOldContext = !boundary.IsCurrentPageReady && !Field<bool>(boundary, "_pageNavigationApplied"); }
        RenderingServer.FramePreDraw += ChangeAtDraw; RenderingServer.FramePostDraw += ObserveChangedDraw;
        try
        {
            BoundaryNavigate("Feedback");
            Check(!boundary.IsCurrentPageReady, "a built but never rendered view still awaits its own draw on revisit");
            await BoundaryReady();
        }
        finally { RenderingServer.FramePreDraw -= ChangeAtDraw; RenderingServer.FramePostDraw -= ObserveChangedDraw; }
        Check(changedAtDraw && withheldOldContext && Field<string>(boundary, "_pageTextContext").EndsWith(":en") &&
            Field<Action?>(boundary, "_pageLayoutPostDraw") is null,
            "font/language context changed before draw cannot complete the old context's delivery");
        BoundaryNavigate("Search");
        Field<HashSet<int>>(boundary, "_presentedPageViews").Remove(4);
        BoundaryNavigate("Feedback");
        boundary.SetProcess(false); boundary.Hide(); await Frames(2);
        Check(boundary.IsCurrentPageReady && Field<Action?>(boundary, "_pageLayoutPreDraw") is null &&
            Field<Action?>(boundary, "_pageLayoutPostDraw") is null,
            "hidden unfinished presentation releases global draw callbacks even if node processing is paused");
        boundary.SetProcess(true); boundary.Open(); BoundaryNavigate("Search");
        var buildFailure = new InvalidOperationException("Page lifecycle fixture original construction failure");
        IEnumerable<object?> FailingBuild() { yield return null; throw buildFailure; }
        Field<HashSet<int>>(boundary, "_presentedPageViews").Remove(0);
        Field<Dictionary<int, IEnumerator<object?>>>(boundary, "_pageViewBuilds")[0] = FailingBuild().GetEnumerator();
        Field<HashSet<int>>(boundary, "_preparedPageText").Add(0);
        Call(boundary, "OpenSettings");
        for (int i = 0; i < 120 && !boundary.HasCurrentPageFailure; i++) await Frames();
        Check(boundary.HasCurrentPageFailure && !boundary.IsCurrentPageReady &&
            ReferenceEquals(Field<Dictionary<int, Exception>>(boundary, "_pageBuildFailures")[0], buildFailure) &&
            Field<Action?>(boundary, "_pageLayoutPostDraw") is null,
            "construction failure retains its original cause and cannot become successful draw readiness");
        BoundaryNavigate("Search");
        Check(boundary.IsCurrentPageReady, "independent navigation remains available after construction failure");
        boundarySurface.Free(); await Frames(4);
        GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(4); Observe("after-partial-retirement-collected");
        File.WriteAllText(Path.Combine(output, "page-lifecycle.json"), JsonSerializer.Serialize(new {
            passed = true, checks, observations,
            scope = "native isolated host; node plateau and measured residency observations are not an Owner-game memory guarantee"
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PAGE_LIFECYCLE_PASS checks={checks}");
    }
}
