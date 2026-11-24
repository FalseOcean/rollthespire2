using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.DeveloperNotes;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunOwnerFollowupChecksAsync(Node root, WorkspaceShell shell, string output, bool aboutOnly = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Set(object value, string name, object? data) => value.GetType().GetField(name, flags)!.SetValue(value, data);
        void Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        int checks = 0;
        var screenshots = new List<string>();
        void Check(bool valid, string why) { checks++; if (!valid) throw new InvalidOperationException("OwnerFollowup:" + why); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Shot(string name)
        {
            await Frames(); RenderingServer.ForceDraw();
            string path = Path.Combine(output, "owner-followup-" + name + ".png");
            Check(root.GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "save " + name);
            screenshots.Add(path);
        }
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        async Task RenderAbout(string[] chapters)
        {
            Field<Button>(shell, "_notes").EmitSignal(BaseButton.SignalName.Pressed);
            await AwaitPageReady(root, shell);
            var notes = Field<DeveloperNotesPage>(shell, "_notesPage");
            foreach (string locale in new[] { "zh", "en" })
            {
                store.SetLanguageOverride(locale); Call(shell, "RefreshLanguage");
                foreach (string chapter in chapters)
                {
                    notes.OpenChapter(chapter);
                    for (int i = 0; i < 600 && !notes.IsPresentationReady && !notes.HasPresentationFailure; i++) await Frames(1);
                    Check(notes.IsPresentationReady && !notes.HasPresentationFailure && notes.SelectedChapterId == chapter,
                        "actual About chapter renders " + locale + "/" + chapter);
                    await Shot("about-" + chapter + "-" + locale);
                    if (!aboutOnly) continue;
                    var renderer = Field<DeveloperNotesArticleRenderer>(notes, "_renderer");
                    var buttons = Field<List<Button>>(notes, "_sectionButtons");
                    int manual = renderer.Sections.ToList().FindIndex(section => section.Title.Contains("家族", StringComparison.Ordinal) ||
                        section.Title.Contains("family", StringComparison.OrdinalIgnoreCase));
                    Check(manual >= 0, "manual order has its own reachable section " + locale);
                    buttons[manual].EmitSignal(BaseButton.SignalName.Pressed);
                    await Shot("about-release-manual-" + locale);
                    buttons[^1].EmitSignal(BaseButton.SignalName.Pressed);
                    await Shot("about-release-credits-" + locale);
                    var scroll = Field<ScrollContainer>(notes, "_articleScroll");
                    scroll.ScrollVertical = (int)Math.Max(0, scroll.GetVScrollBar().MaxValue - scroll.GetVScrollBar().Page);
                    await Shot("about-release-end-" + locale);
                }
            }
        }
        if (aboutOnly)
        {
            await RenderAbout(["release"]);
            File.WriteAllText(Path.Combine(output, "release-copy-ui.json"), JsonSerializer.Serialize(
                new { Passed = true, Checks = checks, Screenshots = screenshots }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("RELEASE_COPY_UI_PASS checks=" + checks);
            return;
        }
        var transform = Field<TransformationSearchEditor>(frame, "_transformationEditor");
        var original = frame.CaptureDraft();
        string state = Field<string>(store, "_directory");
        string userData = Directory.GetParent(Directory.GetParent(state)!.FullName)!.FullName;
        string history = JsonSerializer.Serialize(store.Workspace.QueryHistory);
        foreach (string locale in new[] { "zh", "en" })
        {
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
            store.SetLanguageOverride(locale); Call(shell, "RefreshLanguage");
            frame.RestoreDraft(original, render: false); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            transform.ImportEditorState(new(["N.OWNER_CAPTURE_UNAVAILABLE"], 0, 1, []));
            Call(frame, "UpdateDraftStatus");
            Check(store.Workspace.AuxiliaryIssues.Any(issue => issue.StartsWith("DraftCapture: ", StringComparison.Ordinal)),
                "actual failed capture retains its own unresolved stage " + locale);
            Check(Field<Button>(frame, "_start").Disabled, "failed current capture still blocks Start " + locale);
            Call(frame, "UpdateDraftStatus");
            Check(store.Workspace.AuxiliaryIssues.Any(issue => issue.StartsWith("DraftCapture: ", StringComparison.Ordinal)),
                "another unsuccessful capture cannot clear the error " + locale);
            Call(frame, "SaveDraft");
            Check(!store.Workspace.AuxiliaryIssues.Any(issue => issue.StartsWith("DraftSave: ", StringComparison.Ordinal)),
                "capture before panel closure is not reported as a write failure " + locale);
            const string legacy = "QueryAdmission: System.InvalidOperationException: TransformationDeclaredSourcesUnavailable:N.LeafyPoultice\r\n" +
                "   at RolltheSpire2.Ui.Pages.Search.Editors.TransformationSearchEditor.ExportCondition(SearchQuery q)\r\n" +
                "   at RolltheSpire2.Ui.Pages.Search.QueryWorkbenchFrame.CaptureDraft()\r\n" +
                "   at RolltheSpire2.Ui.Pages.Search.QueryWorkbenchFrame.UpdateDraftStatus()";
            store.Workspace.AuxiliaryIssues.Add(legacy);
            store.Workspace.AuxiliaryIssues.Add(legacy.Replace("QueryAdmission:", "DraftSave:")
                .Replace("UpdateDraftStatus()", "SaveDraft()"));
            store.FlushAll();
            Check(new SearchWorkspace(userData, false).Workspace.AuxiliaryIssues.Contains(legacy),
                "original saved legacy notice is replayed, including its actual origin stack " + locale);
            frame.RestoreDraft(original, render: false); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            Call(frame, "UpdateDraftStatus");
            Check(!store.Runtime.HasSearchInFlight && store.Workspace.AuxiliaryIssues.Count == 0,
                "successful editor capture recovers current and legacy notices without starting a search " + locale);
            Check(store.AuxiliaryFailures.Contains(legacy), "recovery retains original diagnostic evidence " + locale);
            Check(!Field<Label>(frame, "_status").Text.Contains("TransformationDeclaredSourcesUnavailable", StringComparison.Ordinal),
                "visible footer withdraws the recovered error " + locale);
            store.FlushAll();
            Check(new SearchWorkspace(userData, false).Workspace.AuxiliaryIssues.Count == 0,
                "reopen cannot revive recovered notices " + locale);
            await Shot("capture-recovered-" + locale);
        }

        store.RecordPresentationFailure(new InvalidOperationException("genuine-start-admission-failure"), "QueryAdmission");
        store.RecordPresentationFailure(new IOException("unrelated-result-display-failure"), "ResultPresentation");
        Call(frame, "UpdateDraftStatus");
        Check(store.Workspace.AuxiliaryIssues.Count == 2, "successful capture cannot resolve genuine admission or result-display failures");
        string path = Path.Combine(state, "query_workbench.json");
        using (var locked = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
        {
            Set(frame, "_lastSaved", ""); Set(frame, "_restoredCaptureStamp", null);
            Call(frame, "UpdateDraftStatus");
            Check(store.Workspace.AuxiliaryIssues.Any(issue => issue.StartsWith("DraftSave: ", StringComparison.Ordinal)) &&
                store.LastPersistenceFailure is IOException or UnauthorizedAccessException,
                "a real failed write retains its original cause after successful capture");
        }
        Call(frame, "UpdateDraftStatus");
        Check(!store.Workspace.AuxiliaryIssues.Any(issue => issue.StartsWith("DraftSave: ", StringComparison.Ordinal)) &&
            store.Workspace.AuxiliaryIssues.Count == 2, "successful save recovers its own stage only");
        Check(JsonSerializer.Serialize(store.Workspace.QueryHistory) == history, "no search/history mutation during recovery");

        await RenderAbout(["release", "positioning", "next"]);
        await RunLauncherIconChecks(root, output, Field<RolltheSpire2.Bootstrap.ModRuntimeSnapshot>(frame, "_runtime"));
        File.WriteAllText(Path.Combine(output, "owner-followup.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks,
            Screenshots = screenshots, HistoryChanged = false, SearchStarted = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("OWNER_FOLLOWUP_PASS checks=" + checks);
    }

    private static async Task RunCandidateDefaultsChecksAsync(Node root, WorkspaceShell shell, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(string name) => (T)shell.GetType().GetField(name, flags)!.GetValue(shell)!;
        void Refresh()
        {
            shell.GetType().GetField("_languageCode", flags)!.SetValue(shell, "");
            shell.GetType().GetMethod("RefreshLanguage", flags)!.Invoke(shell, null);
        }
        IEnumerable<Node> Nodes(Node node)
        {
            yield return node;
            foreach (var child in node.GetChildren()) foreach (var nested in Nodes(child)) yield return nested;
        }
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("CandidateDefaults:" + label); }
        var screenshots = new List<string>();
        async Task Shot(string name)
        {
            for (int i = 0; i < 4; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string path = Path.Combine(output, "candidate-defaults-" + name + ".png");
            Check(root.GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "snapshot " + name);
            screenshots.Add(path);
        }
        var store = Field<SearchWorkspace>("_persistence");
        Check(!store.Preferences.OperationPredictionEnabled && !store.Preferences.MapPredictionEnabled &&
            !store.Preferences.ShowInRunPredictionEntry && !store.Preferences.ShowInRunConsole, "fresh persisted defaults are all off");
        Field<Button>("_settings").EmitSignal(BaseButton.SignalName.Pressed);
        await AwaitPageReady(root, shell);
        string[] toggles = ["_operationPrediction", "_mapPrediction", "_runPredictionEntry", "_consoleEnabled"];
        foreach (string locale in new[] { "zh", "en" })
        {
            store.SetLanguageOverride(locale); Refresh(); await AwaitPageReady(root, shell);
            Check(toggles.All(name => !Field<CheckButton>(name).ButtonPressed), "all four displayed switches are off " + locale);
            ((Button)shell.FindChild("SettingsCategory0", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            await Shot(locale + "-features");
            ((Button)shell.FindChild("SettingsCategory1", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            await AwaitPageReady(root, shell);
            Check(shell.FindChild("SettingsColorScheme", true, false) is null &&
                !Nodes(Field<Control>("_settingsPage")).OfType<ColorPickerButton>().Any(), "no retired color editor " + locale);
            var font = Field<OptionButton>("_fontChoice");
            Check(font.ItemCount == 2 && font.GetItemText(1).Contains(locale == "zh" ? "宋体" : "Serif", StringComparison.Ordinal),
                "font choices still localize independently " + locale);
            var palette = RolltheSpire2.Ui.Theme.WorkspacePalette.MainPanel;
            Check(palette.Color(palette.Canvas).ToHtml(false).Equals("070d1a", StringComparison.OrdinalIgnoreCase) &&
                palette.Color(palette.Selected).ToHtml(false).Equals("c7b07b", StringComparison.OrdinalIgnoreCase), "current blue-ink theme is active " + locale);
            await Shot(locale + "-general");
        }
        store.SetOperationPredictionEnabled(true); store.SetMapPredictionEnabled(true);
        store.SetShowInRunPredictionEntry(true); store.SetInRunConsole(true, 50, 100); Refresh();
        Check(toggles.All(name => Field<CheckButton>(name).ButtonPressed), "explicit existing opt-ins still display enabled");
        Check(store.Preferences.Appearance is null, "no old appearance remains in preferences");
        File.WriteAllText(Path.Combine(output, "candidate-defaults.json"), JsonSerializer.Serialize(
            new { Passed = true, Checks = checks, Screenshots = screenshots, SearchStarted = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("CANDIDATE_DEFAULTS_PASS checks=" + checks);
    }
}
