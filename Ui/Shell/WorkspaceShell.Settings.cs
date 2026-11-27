using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private Control _settingsPage = null!;
    private Workspace _settingsReturn = Workspace.Search;
    private OptionButton _language = null!;
    private CheckButton? _mainMenuEntry;
    private CheckButton? _persistentSearch;
    private Button? _launcherShortcut;
    private bool _listeningForLauncherShortcut;
    private CheckButton _runPredictionEntry = null!;
    private LineEdit _logPath = null!;
    private Label _logFeedback = null!;
    private Button _settingsBack = null!;
    private readonly List<(Label Label, string Key)> _settingsLabels = [];
    private readonly List<(Button Button, string Key)> _settingsButtons = [];
    private IUiTextProvider? _settingsText;
    private string _logReceiptKey = "";
    private CheckButton _skipExact = null!;
    private CheckButton _gpuPowerDiagnostics = null!;
    private SpinBox _queryHistoryLimit = null!;
    private bool SettingsSearchBusy => _references?.HasActiveSearch == true || _persistence?.Runtime.HasSearchInFlight == true;
    private CheckButton _consoleEnabled = null!;
    private SpinBox _consolePosition = null!;
    private SpinBox _consoleScale = null!;

    private CheckButton _operationPrediction = null!;
    private CheckButton _mapPrediction = null!;
    private CheckButton _deadSeedResearch = null!;
    private readonly List<(Button Button, ScrollContainer? Page)> _settingsCategories = [];
    private VBoxContainer _settingsPages = null!;
    private int _settingsCategory;

    private void EnsureSettings()
    {
        EnsurePageViewSynchronously(_settingsCategory);
        SelectSettingsCategory(_settingsCategory);
    }

    private void BuildSettings()
    {
        _settingsPage = new Control { Name = "SettingsPage", Visible = false };
        AttachPageBuildGroup(_content, _settingsPage); _settingsPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 24);
        _settingsPage.AddChild(body); body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var heading = new HBoxContainer(); body.AddChild(heading);
        var title = SettingsLabel("shell.settings", 30); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        _settingsBack = SettingsButton("settings.back", CloseSettings); heading.AddChild(_settingsBack);
        _settingsBack.Name = "SettingsBack";
        body.AddChild(SettingsLabel("settings.subtitle", 18, true));
        var workspace = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        workspace.AddThemeConstantOverride("separation", 28); body.AddChild(workspace);
        var navigation = new VBoxContainer { Name = "SettingsCategories", CustomMinimumSize = new(224, 0) };
        navigation.AddThemeConstantOverride("separation", 10); workspace.AddChild(navigation);
        var divider = new ColorRect { Color = _palette.Color(_palette.Line), CustomMinimumSize = new(1, 0),
            MouseFilter = MouseFilterEnum.Ignore }; workspace.AddChild(divider);
        _settingsPages = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; workspace.AddChild(_settingsPages);
        foreach (string key in new[] { "settings.group.features", "settings.group.general", "settings.group.search", "settings.group.diagnostics" })
        {
            int index = _settingsCategories.Count;
            var button = SettingsButton(key, () => SelectSettingsCategory(index));
            button.Name = "SettingsCategory" + index; button.ToggleMode = true;
            button.Alignment = HorizontalAlignment.Left; navigation.AddChild(button);
            _settingsCategories.Add((button, null));
        }
    }

    private VBoxContainer SettingsSection(VBoxContainer parent, string titleKey, string helpKey)
    {
        return SettingsRow(CreateSettingsPanel(parent), titleKey, helpKey);
    }

    private VBoxContainer CreateSettingsPanel(VBoxContainer parent)
    {
        var panel = new PanelContainer();
        var style = _palette.Box(_palette.Surface);
        style.ContentMarginLeft = style.ContentMarginRight = 24;
        style.ContentMarginTop = style.ContentMarginBottom = 22;
        panel.AddThemeStyleboxOverride("panel", style); AttachPageBuildGroup(parent, panel);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 16);
        panel.AddChild(rows);
        return rows;
    }

    private VBoxContainer SettingsRow(VBoxContainer rows, string titleKey, string helpKey)
    {
        if (rows.GetChildCount() > 0) rows.AddChild(new HSeparator());
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 32); AttachPageBuildGroup(rows, row);
        var intro = new VBoxContainer { CustomMinimumSize = new(350, 0) }; intro.AddThemeConstantOverride("separation", 10); row.AddChild(intro);
        intro.AddChild(SettingsLabel(titleKey, 22, wrap: true));
        var help = SettingsLabel(helpKey, 17, true, wrap: true); help.CustomMinimumSize = new(350, 0); intro.AddChild(help);
        var controls = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; controls.AddThemeConstantOverride("separation", 10); row.AddChild(controls);
        return controls;
    }

    private void BindSettingsToggle(VBoxContainer controls, CheckButton toggle, string label, Action<bool> change)
    {
        toggle.AddThemeFontSizeOverride("font_size", 20);
        toggle.CustomMinimumSize = new(360, 44);
        toggle.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        controls.AddChild(toggle); _settingsButtons.Add((toggle, label));
        toggle.Toggled += enabled => change(enabled);
    }

    private IEnumerable<object?> BuildSettingsFeatures(VBoxContainer features)
    {
        _operationPrediction = new() { Name = "SettingsOperationPrediction" };
        _mapPrediction = new() { Name = "SettingsMapPrediction" };
        _runPredictionEntry = new() { Name = "SettingsRunPredictionEntry", CustomMinimumSize = new(360, 44),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _consoleEnabled = new() { Name = "SettingsConsoleEnabled" };
        _consolePosition = new() { Name = "SettingsConsolePosition", MinValue = 10, MaxValue = 90, Step = 5, Suffix = "%", CustomMinimumSize = new(180, 42) };
        _consoleScale = new() { Name = "SettingsConsoleScale", MinValue = 75, MaxValue = 125, Step = 5, Suffix = "%", CustomMinimumSize = new(180, 42) };
        var operation = SettingsSection(features, "settings.operation.title", "settings.operation.help");
        BindSettingsToggle(operation, _operationPrediction,
            "settings.operation.enable", enabled => _persistence!.SetOperationPredictionEnabled(enabled));
        BuildOperationPredictionSettings(operation);
        yield return null;
        BindSettingsToggle(SettingsSection(features, "settings.map.title", "settings.map.help"), _mapPrediction,
            "settings.map.enable", enabled => _persistence!.SetMapPredictionEnabled(enabled));
        yield return null;
        var prediction = SettingsSection(features, "settings.run_prediction.title", "settings.run_prediction.help");
        _runPredictionEntry.AddThemeFontSizeOverride("font_size", 20);
        _runPredictionEntry.AddThemeColorOverride("font_color", _palette.Color(_palette.Text));
        prediction.AddChild(_runPredictionEntry);
        _runPredictionEntry.Toggled += enabled =>
        {
            _persistence!.SetShowInRunPredictionEntry(enabled);
            InRunPredictionLauncher.ApplyPreference(GetTree(), enabled || _persistence.Preferences.PersistentSearch);
        };
        yield return null;

        var console = SettingsSection(features, "settings.console.title", "settings.console.help");
        _consoleEnabled.AddThemeFontSizeOverride("font_size", 20);
        _consoleEnabled.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _consoleEnabled.CustomMinimumSize = new(360, 44);
        console.AddChild(_consoleEnabled);
        var consoleParameters = new HFlowContainer(); consoleParameters.AddThemeConstantOverride("h_separation", 20);
        consoleParameters.AddThemeConstantOverride("v_separation", 10); console.AddChild(consoleParameters);
        foreach (var (key, input) in new[] { ("settings.console.position", _consolePosition), ("settings.console.scale", _consoleScale) })
        {
            var column = new VBoxContainer(); consoleParameters.AddChild(column);
            column.AddChild(SettingsLabel(key, 16, true)); column.AddChild(input);
        }
        _consoleEnabled.Toggled += _ => SaveConsoleSettings();
        _consolePosition.ValueChanged += _ => SaveConsoleSettings();
        _consoleScale.ValueChanged += _ => SaveConsoleSettings();
        yield return null;
    }

    private IEnumerable<object?> BuildSettingsGeneral(VBoxContainer general)
    {
        _language = new() { Name = "SettingsLanguage", CustomMinimumSize = new(360, 44), FitToLongestItem = false };
        var language = SettingsSection(general, "settings.language", "settings.language.help");
        for (int i = 0; i < 3; i++) _language.AddItem("");
        _language.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        foreach (var (state, fill) in new[] { ("normal", _palette.Canvas), ("hover", _palette.Hover), ("pressed", _palette.Line), ("disabled", _palette.Canvas) })
            _language.AddThemeStyleboxOverride(state, _palette.Box(fill, _palette.Line, 1));
        _language.AddThemeStyleboxOverride("focus", _palette.FocusRing());
        _language.AddThemeFontSizeOverride("font_size", 20); language.AddChild(_language);
        _language.ItemSelected += index =>
        {
            _persistence!.SetLanguageOverride(index == 1 ? "zh" : index == 2 ? "en" : "");
            _persistence.FlushPreferences();
            _languageCode = ""; RefreshLanguage();
        };
        yield return null;

        BuildFontSettings(SettingsSection(general, "settings.font.title", "settings.font.help"));
        yield return null;
        var launcher = SettingsSection(general, "settings.launcher.title", "settings.launcher.help");
        _mainMenuEntry = new CheckButton { Name = "SettingsMainMenuEntry", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _mainMenuEntry.AddThemeFontSizeOverride("font_size", 20);
        launcher.AddChild(_mainMenuEntry);
        _settingsButtons.Add((_mainMenuEntry, "settings.launcher.show"));
        _mainMenuEntry.Toggled += show => _persistence!.SetMainMenuLauncherHidden(!show);
        var shortcuts = new HFlowContainer();
        launcher.AddChild(shortcuts);
        _launcherShortcut = _palette.Button("");
        _launcherShortcut.Name = "SettingsLauncherShortcut";
        _launcherShortcut.CustomMinimumSize = new(220, 44);
        shortcuts.AddChild(_launcherShortcut);
        _launcherShortcut.Pressed += () =>
        {
            _launcherShortcut.GrabFocus();
            _listeningForLauncherShortcut = true;
            RefreshLauncherShortcut();
        };
        _launcherShortcut.FocusExited += StopLauncherShortcutCapture;
        _launcherShortcut.VisibilityChanged += () =>
        {
            if (!_launcherShortcut.IsVisibleInTree()) StopLauncherShortcutCapture();
        };
        var reset = SettingsButton("settings.launcher.reset", () =>
        {
            _persistence!.SetMainMenuLauncherShortcut(SearchWorkspace.DefaultMainMenuLauncherShortcut);
            StopLauncherShortcutCapture();
        });
        reset.Name = "SettingsLauncherShortcutReset";
        shortcuts.AddChild(reset);
        yield return null;
    }

    private IEnumerable<object?> BuildSettingsSearch(VBoxContainer search)
    {
        var rows = CreateSettingsPanel(search);
        BuildMapLossySettings(rows);
        yield return null;

        var experimental = SettingsRow(rows, "settings.candidates.title", "settings.candidates.help");
        _skipExact = new() { Name = "SettingsSkipExact", CustomMinimumSize = new(360, 44), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _skipExact.AddThemeFontSizeOverride("font_size", 20); experimental.AddChild(_skipExact);
        _skipExact.Toggled += enabled =>
        {
            if (SettingsSearchBusy) { RefreshSettings(); return; }
            _persistence!.SetSkipExactValidation(enabled);
            _references?.SyncSearchPreferences();
        };
        yield return null;

        _queryHistoryLimit = new() { Name = "SettingsQueryHistoryLimit", MinValue = 1, MaxValue = SearchWorkspace.MaximumQueryHistoryLimit, Step = 1, CustomMinimumSize = new(180, 42) };
        var history = SettingsRow(rows, "settings.history.title", "settings.history.help");
        history.AddChild(_queryHistoryLimit);
        _queryHistoryLimit.ValueChanged += value =>
        {
            _persistence!.SetQueryHistoryLimit((int)value);
        };
        yield return null;

        _deadSeedResearch = new() { Name = "SettingsDeadSeedResearch" };
        BindSettingsToggle(SettingsRow(rows, "settings.dead_seed.title", "settings.dead_seed.help"), _deadSeedResearch,
            "settings.dead_seed.show", enabled =>
            {
                _persistence!.SetShowDeadSeedResearch(enabled);
                _references?.RefreshResearchVisibility();
            });
        yield return null;

        _persistentSearch = new CheckButton { Name = "SettingsPersistentSearch" };
        BindSettingsToggle(SettingsRow(rows, "settings.persistent_search.title", "settings.persistent_search.help"), _persistentSearch,
            "settings.persistent_search.enable", enabled =>
            {
                _persistence!.SetPersistentSearch(enabled);
                InRunPredictionLauncher.ApplyPreference(GetTree(), enabled || _persistence.Preferences.ShowInRunPredictionEntry);
                if (_liveRun is not null) _search.Visible = enabled || _persistence.Runtime.Receiving;
            });
        yield return null;

        var gpuPower = SettingsRow(rows, "settings.gpu_power.title", "settings.gpu_power.help");
        _gpuPowerDiagnostics = new() { Name = "SettingsGpuPowerDiagnostics", CustomMinimumSize = new(360, 44),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _gpuPowerDiagnostics.AddThemeFontSizeOverride("font_size", 20);
        gpuPower.AddChild(_gpuPowerDiagnostics);
        _settingsButtons.Add((_gpuPowerDiagnostics, "settings.gpu_power.enable"));
        _gpuPowerDiagnostics.Toggled += enabled =>
        {
            _persistence!.SetGpuPowerDiagnosticsEnabled(enabled);
        };
        yield return null;
    }

    private IEnumerable<object?> BuildSettingsDiagnostics(VBoxContainer diagnostics)
    {
        _logPath = new() { Name = "SettingsLogPath", Editable = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 40), ExpandToTextLength = false };
        var logs = SettingsSection(diagnostics, "settings.logs.title", "settings.logs.help");
        StyleSettingsInput(_logPath); logs.AddChild(_logPath);
        var logActions = new HFlowContainer(); logActions.AddThemeConstantOverride("h_separation", 12); logs.AddChild(logActions);
        logActions.AddChild(SettingsButton("settings.logs.open", OpenSettingsLogDirectory));
        logActions.AddChild(SettingsButton("settings.logs.copy", () =>
        {
            string path = RuntimeLog.CurrentLogPath;
            if (string.IsNullOrWhiteSpace(path)) path = RuntimeLog.LogDirectory;
            DisplayServer.ClipboardSet(path); _logReceiptKey = "settings.logs.copied"; RefreshSettings();
        }));
        _logFeedback = _palette.Label("", 17, true); _logFeedback.AutowrapMode = TextServer.AutowrapMode.WordSmart; logs.AddChild(_logFeedback);
        yield return null;
        if (_liveRun is null)
            diagnostics.AddChild(SettingsButton("settings.diagnostics.status", () => SelectTask(Workspace.Status)));
        yield return null;
    }

    private IEnumerable<object?> BuildSettingsCategory(int index)
    {
        var scroll = new ScrollContainer { Name = "SettingsCategoryPage" + index,
            SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 16); scroll.AddChild(content);
        _settingsPages.AddChild(scroll);
        _settingsCategories[index] = (_settingsCategories[index].Button, scroll);
        IEnumerable<object?> steps = index switch
        {
            0 => BuildSettingsFeatures(content), 1 => BuildSettingsGeneral(content),
            2 => BuildSettingsSearch(content), 3 => BuildSettingsDiagnostics(content),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        foreach (var step in steps) yield return step;
    }

    private void SelectSettingsCategory(int index)
    {
        CloseOperationPredictionConfiguration();
        if (index < 0 || index >= _settingsCategories.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (DeferSettingsCategory(index)) return;
        EnsurePageViewSynchronously(index);
        _settingsCategory = index;
        for (int i = 0; i < _settingsCategories.Count; i++)
        {
            if (_settingsCategories[i].Page is { } page) page.Visible = i == index;
            _settingsCategories[i].Button.SetPressedNoSignal(i == index);
        }
        LocalizeSettings(JsonUiTextProvider.CreateUi13(_languageCode));
    }

    private bool CaptureLauncherShortcut(InputEvent input)
    {
        if (!_listeningForLauncherShortcut || input is not InputEventKey key) return false;
        GetViewport().SetInputAsHandled();
        if (!key.Pressed || key.Echo) return true;
        if (key.Keycode is Key.None or Key.Unknown or Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return true;
        if (key.Keycode != Key.Escape)
            _persistence!.SetMainMenuLauncherShortcut(key.GetKeycodeWithModifiers());
        StopLauncherShortcutCapture();
        return true;
    }

    private void StopLauncherShortcutCapture()
    {
        _listeningForLauncherShortcut = false;
        RefreshLauncherShortcut();
    }

    private void RefreshLauncherShortcut()
    {
        if (_launcherShortcut is null || _settingsText is null || _persistence is null) return;
        _launcherShortcut.Text = _listeningForLauncherShortcut
            ? _settingsText.Get("settings.launcher.listen")
            : _settingsText.Format("settings.launcher.shortcut", OS.GetKeycodeString(_persistence.Preferences.MainMenuLauncherShortcut));
    }

    private Label SettingsLabel(string key, int size, bool secondary = false, bool wrap = false)
    {
        var label = _palette.Label("", size, secondary);
        if (wrap) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.Text = _settingsText!.Get(key);
        _settingsLabels.Add((label, key)); return label;
    }

    private Button SettingsButton(string key, Action action, bool primary = false)
    {
        var button = _palette.Button(_settingsText!.Get(key), primary); button.CustomMinimumSize = new(112, 44);
        button.AddThemeFontSizeOverride("font_size", 18); button.Pressed += action;
        _settingsButtons.Add((button, key)); return button;
    }

    private void StyleSettingsInput(LineEdit input)
    {
        input.AddThemeFontSizeOverride("font_size", 18);
        input.AddThemeColorOverride("font_color", _palette.Color(_palette.Text));
        input.AddThemeColorOverride("font_uneditable_color", _palette.Color(_palette.Secondary));
        input.AddThemeStyleboxOverride("normal", _palette.Box(_palette.Canvas, _palette.Line, 1));
        input.AddThemeStyleboxOverride("read_only", _palette.Box(_palette.Canvas));
        input.AddThemeStyleboxOverride("focus", _palette.FocusRing());
    }

    private void SaveConsoleSettings()
    {
        if (_persistence is null) return;
        _persistence.SetInRunConsole(_consoleEnabled.ButtonPressed, _consolePosition.Value, _consoleScale.Value);
        InRunConsole.InRunConsoleLauncher.ApplyPreferences(GetTree(), _consoleEnabled.ButtonPressed,
            _consolePosition.Value, _consoleScale.Value);
        RefreshSettings();
    }

    private void OpenSettings()
    {
        if (_workspace != Workspace.Settings) _settingsReturn = _workspace;
        SelectTask(Workspace.Settings);
        if (IsCurrentPageReady) _settingsCategories[_settingsCategory].Button.GrabFocus();
    }

    private void CloseSettings()
    {
        if (_workspace != Workspace.Settings) return;
        CloseOperationPredictionConfiguration();
        SelectTask(_settingsReturn); _settings.GrabFocus();
    }

    private void LocalizeSettings(IUiTextProvider text)
    {
        if (_settingsPage is null) return;
        _settingsText = text;
        RefreshFontSettings();
        foreach (var (label, key) in _settingsLabels) label.Text = text.Get(key);
        foreach (var (button, key) in _settingsButtons) button.Text = text.Get(key);
        if (_language is not null)
        {
            _language.SetItemText(0, text.Get("settings.language.follow_game"));
            _language.SetItemText(1, text.Get("settings.language.zh")); _language.SetItemText(2, text.Get("settings.language.en"));
            _language.Select(_persistence!.Preferences.LanguageOverride switch { "zh" => 1, "en" => 2, _ => 0 });
        }
        if (_runPredictionEntry is not null) _runPredictionEntry.Text = text.Get("settings.run_prediction.show");
        if (_consoleEnabled is not null) _consoleEnabled.Text = text.Get("settings.console.show");
        if (_skipExact is not null) _skipExact.Text = text.Get("settings.candidates.enable");
        LocalizeOperationPredictionSettings(text);
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        if (_settingsText is not { } text || _persistence is not { } persistence) return;
        if (_builtPageViews.Contains(0))
        {
            persistence.SyncFeaturePreferences();
            _operationPrediction.SetPressedNoSignal(persistence.Preferences.OperationPredictionEnabled);
            _mapPrediction.SetPressedNoSignal(persistence.Preferences.MapPredictionEnabled);
            _runPredictionEntry.SetPressedNoSignal(persistence.Preferences.ShowInRunPredictionEntry);
            _consoleEnabled.SetPressedNoSignal(persistence.Preferences.ShowInRunConsole);
            _consolePosition.Editable = _consoleScale.Editable = persistence.Preferences.ShowInRunConsole;
            _consolePosition.SetValueNoSignal(persistence.Preferences.InRunConsoleVerticalPercent);
            _consoleScale.SetValueNoSignal(persistence.Preferences.InRunConsoleScalePercent);
            RefreshOperationPredictionSettings(text, persistence);
        }
        if (_builtPageViews.Contains(2))
        {
            _deadSeedResearch.SetPressedNoSignal(persistence.Preferences.ShowDeadSeedResearch);
            _gpuPowerDiagnostics.SetPressedNoSignal(persistence.Preferences.EnableGpuPowerDiagnostics);
            _skipExact.SetPressedNoSignal(persistence.Preferences.SkipExactValidation);
            _skipExact.Disabled = SettingsSearchBusy;
            RefreshMapLossySettings(persistence);
        }
        _queryHistoryLimit?.SetValueNoSignal(persistence.Preferences.QueryHistoryLimit);
        _persistentSearch?.SetPressedNoSignal(persistence.Preferences.PersistentSearch);
        _mainMenuEntry?.SetPressedNoSignal(!persistence.Preferences.MainMenuLauncherHidden);
        RefreshLauncherShortcut();
        if (!_builtPageViews.Contains(3)) return;
        string path = RuntimeLog.CurrentLogPath;
        if (string.IsNullOrWhiteSpace(path)) path = RuntimeLog.LogDirectory;
        if (_logPath.Text != path) _logPath.Text = path;
        _logPath.TooltipText = path;
        _logFeedback.Text = RuntimeLog.LastFailureCode.Length > 0 ? text.Get("settings.logs.unavailable")
            : _logReceiptKey.Length > 0 ? text.Get(_logReceiptKey)
            : text.Get(RuntimeLog.DetailEnabled ? "settings.logs.detail" : "settings.logs.standard");
    }

    private void OpenSettingsLogDirectory()
    {
        try
        {
            OperationalFileLog.Flush();
            string directory = RuntimeLog.LogDirectory;
            Directory.CreateDirectory(directory);
            var uri = new Uri(Path.GetFullPath(directory) + Path.DirectorySeparatorChar).AbsoluteUri;
            if (OS.ShellOpen(uri) != Error.Ok) throw new IOException("ShellOpen failed");
            _logReceiptKey = "settings.logs.opened";
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn("openOperationalLogDirectoryFailed=" + ex.Message);
            _logReceiptKey = "settings.logs.open_failed";
        }
        RefreshSettings();
    }
}
