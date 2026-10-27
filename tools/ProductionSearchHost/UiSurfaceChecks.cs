using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search.Editors;
using System.Reflection;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Pages.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;

// Exercise the product workspace. Retired windows are not test fixtures.
internal static partial class UiSurfaceChecks
{
    // Share the required native catalogs, isolated saves and thread binding only.
    internal static async Task InitializeNativeWorkspaceAsync(Node root, string output)
    {
        Directory.CreateDirectory(output);
        // The current workspace reads the game's character/unlock catalogs.
        // Use the same isolated initialization as the existing preset UI checks.
        var args = OS.GetCmdlineUserArgs();
        int packAt = Array.IndexOf(args, "--game-pck");
        if (packAt < 0 || packAt + 1 >= args.Length || !ProjectSettings.LoadResourcePack(args[packAt + 1], false))
            throw new InvalidOperationException("UiSurface: pass --game-pck for current workspace resources");
        bool actualRf = args.Contains("--operation-rf-coexistence");
        MegaCrit.Sts2.Core.Saves.SaveManager PrepareSave()
        {
            var save = new MegaCrit.Sts2.Core.Saves.SaveManager(
                new MegaCrit.Sts2.Core.Saves.Test.MockGodotFileIo("user://workspace-surface-memory"), true);
            MegaCrit.Sts2.Core.Saves.SaveManager.MockInstanceForTesting(save);
            save.InitSettingsDataForTest(); save.InitPrefsDataForTest();
            return save;
        }
        MegaCrit.Sts2.Core.Saves.SaveManager? earlySave = null;
        if (actualRf)
        {
            // Native ExecuteVeryEarly initializes save settings before mods.
            // Bind the isolated instance before third-party initializers run.
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = true;
            earlySave = PrepareSave();
            await LoadRfCoexistencePackages(output);
        }
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = true;
        if (!actualRf)
            typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null,
                MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        MegaCrit.Sts2.Core.Models.ModelDb.Init();
        MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();
        MegaCrit.Sts2.Core.Models.ModelDb.InitIds();
        var mockSave = earlySave ?? PrepareSave();
        mockSave.InitProfileId(1); mockSave.InitProgressData();
        mockSave.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        RolltheSpire2.Infrastructure.Snapshots.RuntimeSnapshotThreadGuard.BindCurrentThread();
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        root.GetWindow().Size = new Vector2I(1600, 900);
    }

    internal static async Task RunAsync(Node root, string output)
    {
        await InitializeNativeWorkspaceAsync(root, output);
        var args = OS.GetCmdlineUserArgs();
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
            Detection = new("0.111.0", "0.111.0", "beta", "WorkspaceSurface", true, "") };

        if (args.Contains("--launcher-icon-ui"))
        {
            await RunLauncherIconChecks(root, output, runtime);
            return;
        }
        if (args.Contains("--menu-preparation-only"))
        {
            int checks = 0;
            void Check(bool ok, string reason)
            { checks++; if (!ok) throw new InvalidOperationException("MenuPreparation:" + reason); }
            await CheckMenuObservationAsync(root, runtime, Check);
            await RunMenuPreparationChecksAsync(root, runtime, Check);
            File.WriteAllText(Path.Combine(output, "menu-preparation.json"),
                System.Text.Json.JsonSerializer.Serialize(new { Passed = checks }));
            return;
        }
        if (args.Contains("--ui-lifecycle-only"))
        {
            await RunPageLifecycleChecksAsync(root, output, runtime);
            await RunLibraryLifecycleChecksAsync(root, output);
            await RunDeveloperNotesLifecycleChecksAsync(root, output);
            return;
        }
        if (args.Contains("--content-names-ui"))
        {
            RunContentNamesChecks(root, output);
            return;
        }
        if (args.Contains("--runtime-issues-ui"))
        {
            await RunRuntimeIssuesChecks(root, output, runtime);
            return;
        }
        if (args.Contains("--in-run-console-ui"))
        {
            await RunInRunConsoleChecks(root, output, runtime);
            return;
        }
        if (args.Contains("--neow-operation-ui") || args.Contains("--operation-ui") || args.Contains("--operation-surfaces-only"))
        {
            await RunNeowOperationChecks(root, output, runtime);
            return;
        }
        if (args.Contains("--mainpanel-ui"))
        {
            await RunMainPanelChecks(root, output, runtime);
            return;
        }
        if (args.Contains("--treasure-unknown-ui"))
        {
            await RunTreasureUnknownChecks(root, output, runtime);
            return;
        }
        if (args.Contains("--in-run-map-ui") || args.Contains("--in-run-map-drawing-ui"))
        {
            await RunInRunMapChecks(root, output, runtime, args.Contains("--in-run-map-drawing-ui"));
            return;
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(object value, string name) => value.GetType().GetField(name, flags)!.GetValue(value)!;
        object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("UiSurface:" + message); }
        IEnumerable<Node> EncyclopediaNodes(Node node)
        {
            yield return node;
            foreach (Node child in node.GetChildren())
                foreach (Node descendant in EncyclopediaNodes(child)) yield return descendant;
        }
        string state = Path.Combine(output, "ui-state");
        var host = new Control { Size = new Vector2(1600, 900) };
        root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, state);
        host.AddChild(shell);
        try
        {
            var persistence = (SearchWorkspace)Field(shell, "_persistence");
            var workbench = (QueryWorkbenchFrame)Field(shell, "_references");
            if (args.Contains("--font-settings-only"))
            {
                await RunFontSettingsChecks(root, host, shell, persistence, workbench, state, output);
                return;
            }
            if (args.Contains("--settings-only"))
            {
                int checks = 0;
                void Check(bool ok, string reason) { checks++; Require(ok, reason); }
                shell.Open(); Call(shell, "OpenSettings");
                await AwaitPageReady(root, shell);
                Call(shell, "SelectSettingsCategory", 1);
                await AwaitPageReady(root, shell);
                var language = (OptionButton)Field(shell, "_language");
                Call(shell, "SelectSettingsCategory", 2);
                await AwaitPageReady(root, shell);
                var worker = (SpinBox)Field(shell, "_workerBudget");
                var operation = (CheckButton)Field(shell, "_operationPrediction");
                var map = (CheckButton)Field(shell, "_mapPrediction");
                Check(operation.ButtonPressed && map.ButtonPressed, "existing feature behavior defaults enabled");
                operation.ButtonPressed = false; map.ButtonPressed = false;
                Check(!persistence.Preferences.OperationPredictionEnabled && !persistence.Preferences.MapPredictionEnabled,
                    "feature buttons update preferences");
                var readback = new SearchWorkspace(state, false);
                Check(!readback.Preferences.OperationPredictionEnabled && !readback.Preferences.MapPredictionEnabled,
                    "feature preferences survive reload");
                operation.ButtonPressed = true; map.ButtonPressed = true;
                worker.Value = 3;
                Check(persistence.Preferences.SearchWorkerBudget == 3 &&
                    (int)workbench.GetType().GetProperty("SearchWorkers", flags)!.GetValue(workbench)! == 3,
                    "worker edit reaches the actual search input");
                ((Button)shell.FindChild("SettingsWorkerReset", true, false)).EmitSignal(Button.SignalName.Pressed);
                Check(persistence.Preferences.SearchWorkerBudget is null && worker.Value ==
                    SearchWorkspace.ResolveSearchWorkers(null, System.Environment.ProcessorCount), "automatic half-CPU reset");
                var originalCursor = persistence.CurrentPersistentOriginSeed;
                var origin = (LineEdit)Field(shell, "_originInput"); origin.Text = "000000000456";
                foreach (var locale in new[] { ("zh", 1), ("en", 2) })
                {
                    language.Select(locale.Item2); language.EmitSignal(OptionButton.SignalName.ItemSelected, (long)locale.Item2);
                    foreach (int width in new[] { 1600, 1280 })
                    {
                        root.GetWindow().Size = new Vector2I(width, width * 9 / 16);
                        host.Size = new Vector2(width, width * 9 / 16);
                        for (int category = 0; category < 4; category++)
                        {
                            ((Button)shell.FindChild("SettingsCategory" + category, true, false)).EmitSignal(Button.SignalName.Pressed);
                            await AwaitPageReady(root, shell);
                            for (int frame = 0; frame < 5; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                            var page = (ScrollContainer)shell.FindChild("SettingsCategoryPage" + category, true, false);
                            Check(page.IsVisibleInTree(), "selected category visible");
                            Check(shell.GetGlobalRect().End.X <= width + 1 && shell.GetGlobalRect().End.Y <= width * 9 / 16 + 1,
                                "settings canvas fits actual viewport");
                            for (int other = 0; other < 4; other++)
                                Check(((shell.FindChild("SettingsCategoryPage" + other, true, false) as Control)?.Visible ?? false) == (other == category),
                                    "only selected settings category shown");
                            Check(page.GetHScrollBar().MaxValue <= page.GetHScrollBar().Page + 2,
                                "settings have no horizontal overflow");
                            Check(EncyclopediaNodes(page).OfType<Label>().Where(l => l.IsVisibleInTree())
                                .All(l => !l.Text.StartsWith("settings.")), "category text localized");
                            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,
                                $"settings-{locale.Item1}-{width}-{category}.png"));
                        }
                    }
                }
                Check(origin.Text == "000000000456" && persistence.CurrentPersistentOriginSeed == originalCursor,
                    "category and language changes preserve unsubmitted origin");
                ((Button)Field(shell, "_originApply")).EmitSignal(Button.SignalName.Pressed);
                Check(persistence.CurrentPersistentOriginSeed == "000000000456", "existing origin action retained");
                var skip = (CheckButton)Field(shell, "_skipExact"); skip.ButtonPressed = true;
                Check(persistence.Preferences.SkipExactValidation, "advanced candidate control retained"); skip.ButtonPressed = false;
                Call(shell, "CloseSettings");
                Check(workbench.IsVisibleInTree(), "settings returns to original workspace");
                GD.Print($"SETTINGS_PASS checks={checks};categories=4;languages=zh,en;resolutions=1600,1280;workers+features+origin");
                return;
            }
            RunLiveEtaChecks(workbench);
            Require(!persistence.Preferences.SkipExactValidation, "candidate mode defaults off");
            shell.Open();
            Require(((Control)Field(shell, "_references")).Visible && !(bool)Field(workbench, "_multiplayer"),
                "new reader starts on single-player Search without a redirect");
            var about = (Button)Field(shell, "_notes");
            Require(about.Text.Contains('•') && (bool)Field(shell, "_notesPending"), "unread chapters mark About");
            Call(shell, "TickNotesReminder", 5.0);
            Require((double)Field(shell, "_notesPulseElapsed") == 4.8 && about.GetThemeColor("font_color") == new Color("DCC58D"),
                "reminder stops pulsing and retains steady gold");
            about.EmitSignal(Button.SignalName.Pressed);
            var notes = (DeveloperNotesPage)Field(shell, "_notesPage");
            Require(((Dictionary<string, Button>)Field(notes, "_chapterButtons")).Keys.SequenceEqual(
                new[] { "release", "next", "positioning", "devlog" }), "notes retain four peer chapters after FAQ migration");
            await AwaitPageReady(root, shell);
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "notes-unread.png"));
            ((Button)Field(shell, "_search")).EmitSignal(Button.SignalName.Pressed);
            shell.Open();
            Require(((Control)Field(shell, "_references")).Visible && persistence.Preferences.ReadNotesVersions.Count == 0,
                "dismissal neither acknowledges nor redirects on reopening");
            about.EmitSignal(Button.SignalName.Pressed);
            await AwaitPageReady(root, shell);
            ((Button)Field(notes, "_acknowledge")).EmitSignal(Button.SignalName.Pressed);
            Require(persistence.Preferences.ReadNotesVersions.ContainsKey("release") && !persistence.Preferences.ReadNotesVersions.ContainsKey("positioning") && about.Text.Contains('•'),
                "release acknowledgement leaves positioning pending");
            notes.OpenChapter(DeveloperNotesChapterIds.Positioning);
            await AwaitPageReady(root, shell);
            Require(((Button)Field(notes, "_acknowledge")).Visible, "positioning has its own article-end acknowledgement");
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "notes-positioning.png"));
            ((Button)Field(notes, "_acknowledge")).EmitSignal(Button.SignalName.Pressed);
            Require(!about.Text.Contains('•') && !((Button)Field(notes, "_acknowledge")).Visible, "last acknowledgement clears reminders");
            ((Button)Field(shell, "_search")).EmitSignal(Button.SignalName.Pressed);
            shell.Open();
            Require(((Control)Field(shell, "_references")).Visible, "acknowledged release does not interrupt reopening");
            foreach (var (locale, index) in new[] { ("en", 2), ("zh", 1) })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                Call(shell, "OpenSettings");
                await AwaitPageReady(root, shell);
                Call(shell, "SelectSettingsCategory", 1);
                await AwaitPageReady(root, shell);
                var language = (OptionButton)Field(shell, "_language");
                language.Select(index);
                language.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(persistence.Preferences.LanguageOverride == locale, "current settings language signal");
                Require(((Control)Field(shell, "_settingsPage")).Visible, "full settings page is visible");
                Require(!((Button)Field(shell, "_settingsBack")).Text.StartsWith("settings."), "settings translation");
                Call(shell, "SelectSettingsCategory", 2);
                await AwaitPageReady(root, shell);
                var skipExact = (CheckButton)Field(shell, "_skipExact");
                Require(!skipExact.Text.StartsWith("settings."), "candidate switch localization");
                skipExact.ButtonPressed = true;
                Require(persistence.Preferences.SkipExactValidation, "candidate switch saves explicit opt-in");
                skipExact.ButtonPressed = false;
                var origin = (LineEdit)Field(shell, "_originInput");
                origin.Text = "000000000123";
                ((Button)Field(shell, "_originApply")).EmitSignal(Button.SignalName.Pressed);
                Require(persistence.CurrentPersistentOriginSeed == "000000000123", "origin is saved");
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "settings-" + locale + ".png"));
                Call(shell, "CloseSettings");
                Require(((Control)Field(shell, "_references")).Visible, "settings returns to Search");
                ((Button)Field(shell, "_feedback")).EmitSignal(Button.SignalName.Pressed);
                await AwaitPageReady(root, shell);
                for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(((Control)Field(shell, "_feedbackPage")).Visible, "top-level feedback page");
                Require(!((Button)Field(shell, "_feedbackExport")).Text.StartsWith("feedback."), "feedback translation");
                Require(!((Button)Field(shell, "_feedbackCopyIssue")).Text.StartsWith("feedback."), "issue copy translation");
                Require(((Button)Field(shell, "_feedbackCopy")).Disabled, "copy requires completed ZIP");
                Require(shell.CloseButton.GetGlobalRect().End.X <= host.GetGlobalRect().End.X, "expanded top navigation fits");
                var capture = (RolltheSpire2.Infrastructure.Diagnostics.FeedbackBundleRequest)Call(shell, "CaptureFeedbackRequest")!;
                Require(capture.EnvironmentJson.Contains("RenderingDriver") && capture.EditorJson is not null, "main-thread feedback snapshot");
                // Reproduce the actual desktop override: base=false, pc=true. Also check log-path overrides.
                const string logging = "debug/file_logging/enable_file_logging", logPath = "debug/file_logging/log_path";
                var oldLogging = ProjectSettings.GetSetting(logging);
                var oldDesktop = ProjectSettings.GetSetting(logging + ".pc");
                var oldPathOverride = ProjectSettings.GetSetting(logPath + ".pc");
                try
                {
                    ProjectSettings.SetSetting(logging, false); ProjectSettings.SetSetting(logging + ".pc", true);
                    ProjectSettings.SetSetting(logPath + ".pc", "user://feedback-test/desktop.log");
                    var enabled = (RolltheSpire2.Infrastructure.Diagnostics.FeedbackBundleRequest)Call(shell, "CaptureFeedbackRequest")!;
                    Require(!enabled.CaptureWarnings.Any(w => w.StartsWith("Godot file logging is disabled")), "desktop logging override prevents false warning");
                    Require(enabled.GodotLog == ProjectSettings.GlobalizePath("user://feedback-test/desktop.log"), "effective desktop log path");
                    ProjectSettings.SetSetting(logging + ".pc", false);
                    var disabled = (RolltheSpire2.Infrastructure.Diagnostics.FeedbackBundleRequest)Call(shell, "CaptureFeedbackRequest")!;
                    Require(disabled.CaptureWarnings.Any(w => w.StartsWith("Godot file logging is disabled")), "actually disabled logging still warns");
                }
                finally
                {
                    ProjectSettings.SetSetting(logging, oldLogging); ProjectSettings.SetSetting(logging + ".pc", oldDesktop);
                    ProjectSettings.SetSetting(logPath + ".pc", oldPathOverride);
                }
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "feedback-" + locale + ".png"));
                var issue = new RolltheSpire2.Infrastructure.Diagnostics.FeedbackIssueText("Test", "Test");
                typeof(WorkspaceShell).GetField("_feedbackResult", flags)!.SetValue(shell,
                    new RolltheSpire2.Infrastructure.Diagnostics.FeedbackBundleResult(
                        Path.Combine(output, "RT2-feedback-20260928-080214-09f7001b713544988a12a0835cb71ee6.zip"),
                        302264, 10, 0, false, issue, issue));
                typeof(WorkspaceShell).GetField("_feedbackMessage", flags)!.SetValue(shell, "feedback.issue_opened");
                Call(shell, "RefreshFeedback");
                for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(((Control)Field(shell, "_feedbackArtifact")).Visible && !((Button)Field(shell, "_feedbackCopy")).Disabled,
                    "completed bundle has file actions");
                Require(!((Control)Field(shell, "_feedbackPath")).IsVisibleInTree(), "full path stays in collapsed details");
                Require(((Control)Field(shell, "_feedbackNotice")).Visible && !((Control)Field(shell, "_feedbackWarnings")).Visible,
                    "receipt is separate from clean export summary");
                Require(((Button)Field(shell, "_feedbackExport")).GetGlobalRect().End.Y < host.GetGlobalRect().End.Y &&
                    ((Button)Field(shell, "_feedbackDetailsToggle")).GetGlobalRect().End.Y < host.GetGlobalRect().End.Y,
                    "completed feedback actions and disclosure remain in the viewport");
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "feedback-ready-" + locale + ".png"));
                ((Button)Field(shell, "_feedbackDetailsToggle")).EmitSignal(Button.SignalName.Pressed);
                Require(((Control)Field(shell, "_feedbackPath")).IsVisibleInTree(), "path available through details");
                ((Button)Field(shell, "_feedbackDetailsToggle")).EmitSignal(Button.SignalName.Pressed);
                typeof(WorkspaceShell).GetField("_feedbackResult", flags)!.SetValue(shell, null);
                typeof(WorkspaceShell).GetField("_feedbackMessage", flags)!.SetValue(shell, "");
                Call(shell, "RefreshFeedback");
                ((Button)Field(shell, "_search")).EmitSignal(Button.SignalName.Pressed);

                // Render the event picker and shared-event article for manual layout review.
                async Task Snapshot(string name)
                {
                    for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                    RenderingServer.ForceDraw();
                    root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + "-" + locale + ".png"));
                }
                Call(workbench, "SelectDomain", "events");
                var events = (EventSearchEditor)Field(workbench, "_eventEditor");
                var eventDraft = Field(events, "_draft");
                eventDraft.GetType().GetField("QueueAct")!.SetValue(eventDraft, 1);
                Call(events, "OpenQueuePicker");
                await Snapshot("event-picker-all");
                foreach (string key in new[] { "query.event.picker.results_supported", "query.event.picker.appearance_only" })
                {
                    var filters = (VBoxContainer)Field(events, "_queuePickerFilters");
                    string label = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(locale).Get(key);
                    filters.GetChildren().OfType<Button>().Single(b => b.Text == label).EmitSignal(Button.SignalName.Pressed);
                    await Snapshot(key.EndsWith("results_supported") ? "event-picker-results" : "event-picker-appearance");
                }
                events.CloseQueuePicker();
                ((Button)Field(shell, "_encyclopedia")).EmitSignal(Button.SignalName.Pressed);
                var encyclopedia = (EncyclopediaCanvas)Field(shell, "_encyclopediaCanvas");
                encyclopedia.Open(null);
                await Snapshot("encyclopedia-home");
                var homeLabels = EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<Label>().ToArray();
                string[] rootTitles = locale == "zh" ? ["阅读引导", "默认环境", "多人", "模组", "种子问题快查"]
                    : ["Reading guide", "Default environment", "Multiplayer", "Mods", "Seed FAQ"];
                Require(rootTitles.All(title => homeLabels.Any(l => l.Text == title)), "home exposes five peer encyclopedia sections");
                EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<Button>()
                    .Single(b => b.GetChildren().OfType<Label>().Any(l => l.Text == rootTitles[1]))
                    .EmitSignal(Button.SignalName.Pressed);
                Require((string)Field(encyclopedia, "_readingId") == "default-environment", "default section has its own article");
                await Snapshot("encyclopedia-default-environment");
                EncyclopediaNodes((ScrollContainer)Field(encyclopedia, "_railScroll")).OfType<Button>()
                    .Single(b => b.Text == (locale == "zh" ? "涅奥" : "Neow")).EmitSignal(Button.SignalName.Pressed);
                Require((string)Field(encyclopedia, "_readingId") == "neow", "default environment opens the Neow search-page group");
                await Snapshot("encyclopedia-neow-group");
                EncyclopediaNodes((ScrollContainer)Field(encyclopedia, "_railScroll")).OfType<Button>()
                    .Single(b => b.Text == (locale == "zh" ? "骨骰的领取顺序" : "Bones pickup order"))
                    .EmitSignal(Button.SignalName.Pressed);
                Require((string)Field(encyclopedia, "_readingId") == "neow-offer", "third-level convention opens from its search-page group");
                await Snapshot("encyclopedia-neow-order");
                encyclopedia.Open(null);
                EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<Button>()
                    .Single(b => b.GetChildren().OfType<Label>().Any(l => l.Text == rootTitles[0]))
                    .EmitSignal(Button.SignalName.Pressed);
                Require((string)Field(encyclopedia, "_readingId") == "reading-guide", "home guide opens reading hierarchy");
                EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<RichTextLabel>().First()
                    .EmitSignal(RichTextLabel.SignalName.MetaClicked, "article:seed-history");
                Require((string)Field(encyclopedia, "_readingId") == "seed-history", "seed background remains accessible through reading guide");
                string seedText = string.Join("\n", EncyclopediaNodes((Control)Field(encyclopedia, "_main"))
                    .OfType<RichTextLabel>().Select(l => l.GetParsedText()));
                Require(seedText.Contains("0.109.0") && seedText.Contains("GPU") &&
                    seedText.Contains(locale == "zh" ? "多人" : "multiplayer", StringComparison.OrdinalIgnoreCase),
                    "seed history retains version and developer maintenance reasons");
                await Snapshot("encyclopedia-seed-history");
                ((ScrollContainer)Field(encyclopedia, "_articleScroll")).ScrollVertical = 100000;
                await Snapshot("encyclopedia-seed-history-bottom");
                var seedSearch = (LineEdit)Field(encyclopedia, "_search");
                seedSearch.Text = "Hash";
                seedSearch.EmitSignal(LineEdit.SignalName.TextChanged, "Hash");
                await Snapshot("encyclopedia-seed-search");
                seedSearch.EmitSignal(LineEdit.SignalName.TextSubmitted, "Hash");
                Require((string)Field(encyclopedia, "_readingId") == "seed-history", "Hash search resolves to the seed article");
                encyclopedia.Open(null);
                string startTitle = locale == "zh" ? "阅读引导" : "Reading guide";
                EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<Button>()
                    .Single(b => b.GetChildren().OfType<Label>().Any(l => l.Text == startTitle))
                    .EmitSignal(Button.SignalName.Pressed);
                Require((string)Field(encyclopedia, "_readingId") == "reading-guide", "home entry opens scope article");
                await Snapshot("encyclopedia-scope");
                var articleMain = (Control)Field(encyclopedia, "_main");
                var backButton = articleMain.GetChildren().OfType<Button>().Single(b => b.Text.StartsWith("‹"));
                var breadcrumb = articleMain.GetChildren().OfType<Label>().Single(l => l.Position.Y == 9);
                Require(backButton.GetGlobalRect().End.X <= breadcrumb.GetGlobalRect().Position.X,
                    "article back button and breadcrumb do not overlap");
                ((ScrollContainer)Field(encyclopedia, "_articleScroll")).ScrollVertical = 10000;
                await Snapshot("encyclopedia-scope-bottom");
                encyclopedia.Open(null);
                string faqTitle = locale == "zh" ? "种子问题快查" : "Seed FAQ";
                EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<Button>()
                    .Single(b => b.GetChildren().OfType<Label>().Any(l => l.Text == faqTitle))
                    .EmitSignal(Button.SignalName.Pressed);
                Require((string)Field(encyclopedia, "_readingId") == "faq", "home mismatch entry opens migrated FAQ");
                Require(EncyclopediaNodes(encyclopedia).OfType<RichTextLabel>().Any(l =>
                    l.GetParsedText().Contains(locale == "zh" ? "开发者笔记" : "Developer Notes")), "migrated notes question is readable");
                await Snapshot("encyclopedia-faq");
                encyclopedia.Open("crystal-sphere");
                Require(EncyclopediaNodes(encyclopedia).OfType<RichTextLabel>().Any(l => l.GetParsedText().Contains("1.3.3")),
                    "Crystal article retains its introduction version");
                await Snapshot("encyclopedia-crystal");
                foreach (string topic in new[] { "neow-offer", "ancient-pael", "neow-transform", "shop", "relic-chests", "event-appearance" })
                {
                    encyclopedia.Open(topic);
                    await Snapshot("encyclopedia-" + topic);
                }
                // Exercise real link callbacks, including a destination already visible in the rail.
                encyclopedia.Open("neow-offer");
                await Snapshot("encyclopedia-navigation-before");
                var railScroll = (ScrollContainer)Field(encyclopedia, "_railScroll");
                railScroll.ScrollVertical = 20;
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                int railPosition = railScroll.ScrollVertical;
                var body = EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<RichTextLabel>().ToArray();
                Require(body.All(l => !l.SelectionEnabled), "article clicks cannot select or highlight text");
                body.First(l => l.GetParsedText().Contains(locale == "zh" ? "涅奥" : "Neow"))
                    .EmitSignal(RichTextLabel.SignalName.MetaClicked, "article:neow");
                await Snapshot("encyclopedia-navigation-visible");
                railScroll = (ScrollContainer)Field(encyclopedia, "_railScroll");
                Require(railScroll.ScrollVertical == railPosition, "inline navigation preserves rail scroll for a visible destination");
                Require((string)Field(encyclopedia, "_readingId") == "neow", "text links still navigate without selection");
                railScroll.ScrollVertical = 10000;
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                EncyclopediaNodes((Control)Field(encyclopedia, "_main")).OfType<RichTextLabel>().First()
                    .EmitSignal(RichTextLabel.SignalName.MetaClicked, "article:ancient-pael");
                await Snapshot("encyclopedia-navigation-outside");
                railScroll = (ScrollContainer)Field(encyclopedia, "_railScroll");
                var selectedPael = EncyclopediaNodes(railScroll).OfType<Button>()
                    .Single(b => b.Text == (locale == "zh" ? "佩尔" : "Pael"));
                Require(selectedPael.GetGlobalRect().Position.Y >= railScroll.GetGlobalRect().Position.Y &&
                    selectedPael.GetGlobalRect().End.Y <= railScroll.GetGlobalRect().End.Y,
                    "inline navigation reveals an offscreen destination in the rail");
                if (locale == "zh")
                {
                    string paelText = string.Join("\n", EncyclopediaNodes((Control)Field(encyclopedia, "_main"))
                        .OfType<RichTextLabel>().Select(l => l.GetParsedText()));
                    Require(paelText.Contains("黏糊") && paelText.Contains("佩尔的增生组织") && !paelText.Contains("Goopy"),
                        "Pael uses official Chinese enchantment and relic names");
                }
                encyclopedia.Open("multiplayer-shared");
                await Snapshot("multiplayer-shared");
                ((ScrollContainer)Field(encyclopedia, "_articleScroll")).ScrollVertical = 650;
                await Snapshot("multiplayer-shared-morphic");
                ((Button)Field(shell, "_search")).EmitSignal(Button.SignalName.Pressed);
            }
            await RunClearConditionChecks(root, workbench, persistence, output);
            persistence.FlushWorkspace();
            var reloaded = new SearchWorkspace(state);
            Require(reloaded.Preferences.LanguageOverride == "zh", "language persists");
            Require(reloaded.CurrentPersistentOriginSeed == "000000000123", "origin persists");
            Require(typeof(WorkspaceShell).Assembly.GetType("RolltheSpire2.Ui.Shell.AppShell") is null,
                "retired shell is absent from the product assembly");
            shell.CleanupForTopLevelClose();
            Require(!shell.Visible, "workspace closes");
            GD.Print("UI_WORKSPACE_PASS settings=en,zh;origin+persistence+liveEta+close=true;legacyShell=false");
        }
        finally { host.QueueFree(); }
        await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
