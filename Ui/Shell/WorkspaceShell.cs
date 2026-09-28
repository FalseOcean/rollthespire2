using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Predictability;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Workspace host for tools, runtime status and bundled developer notes.</summary>
internal sealed partial class WorkspaceShell : Control
{
    private static readonly Vector2 CanvasSize = new(1600, 900);
    private readonly Button _search = MakeButton();
    private readonly Button _analysis = MakeButton();
    private readonly Button _encyclopedia = MakeButton();
    private readonly Button _status = MakeButton();
    private readonly Button _notes = MakeButton();
    private readonly Button _settings = MakeButton();
    private readonly Button _close = MakeButton("×");
    private readonly Control _content = new() { Name = "TaskCanvas", MouseFilter = MouseFilterEnum.Ignore };
    private SearchWorkspacePersistence? _persistence;
    private string _languageCode = string.Empty;
    private double _localePoll;
    private DesignReferenceSurfaces? _references;
    private EncyclopediaCanvas? _encyclopediaCanvas;
    private WorkspacePalette _palette = WorkspacePalette.Canonical;
    private ColorRect? _background;
    private Label? _title;
    private ModRuntimeSnapshot _runtime = null!;
    private RolltheSpire2.Ui.Pages.Analysis.AnalysisPage? _predictor;
    private RolltheSpire2.Ui.Controllers.AnalysisPageController? _predictorController;


    private enum Workspace { Search, Analysis, Seeds, Encyclopedia, Status, Notes, Settings, Feedback }
    private Workspace _workspace;

    public Button CloseButton => _close;
    public event Action? TopLevelCloseRequested;

    public void Initialize(ModRuntimeSnapshot snapshot) => InitializeWithPersistence(snapshot, OS.GetUserDataDir());

    internal void InitializeWithPersistence(ModRuntimeSnapshot snapshot, string persistenceDirectory)
    {
        _runtime = snapshot;
        InitializeSearchEvidenceOnMainThread();
        Name = "RolltheSpire2_WorkspaceShell";
        Size = CanvasSize;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        // Read existing preferences without creating a Search cursor for an empty UI.
        _persistence = new SearchWorkspacePersistence(persistenceDirectory, snapshot.Profile.ProfileId, initializeSearchCursor: false);
        var background = _background = new ColorRect { Color = _palette.Color(_palette.Canvas), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var header = new HBoxContainer { Position = new Vector2(32, 20), Size = new Vector2(1536, 48) };
        header.AddThemeConstantOverride("separation", 12);
        AddChild(header);
        var title = _title = new Label { Text = "RolltheSpire2", CustomMinimumSize = new Vector2(216, 0), VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        header.AddChild(title);
        header.AddChild(_search);
        header.AddChild(_analysis);
        header.AddChild(_seeds);
        header.AddChild(_encyclopedia);
        header.AddChild(_status);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        header.AddChild(_notes);
        header.AddChild(_feedback);
        header.AddChild(_settings);
        _close.CustomMinimumSize = new Vector2(48, 48);
        header.AddChild(_close);
        _content.Position = new Vector2(32, 96);
        _content.Size = new Vector2(1536, 772);
        AddChild(_content);
        _references = new DesignReferenceSurfaces(snapshot, _persistence);
        _content.AddChild(_references);
        _references.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _references.ModalChanged += open =>
        {
            _search.Disabled = _analysis.Disabled = _seeds.Disabled = _encyclopedia.Disabled = _status.Disabled = _notes.Disabled = _feedback.Disabled = _settings.Disabled = _close.Disabled = open;

        };

        _references.EncyclopediaRequested += topic => SelectTask(Workspace.Encyclopedia, topic);
        _references.OpenPartyInformation += ShowPartyResult;
        _references.OpenSeedInformation += ShowSingleResult;
        _search.Pressed += () => SelectTask(Workspace.Search);
        _analysis.Pressed += () => SelectTask(Workspace.Analysis);
        _encyclopedia.Pressed += () => SelectTask(Workspace.Encyclopedia);
        _status.Pressed += () => SelectTask(Workspace.Status);
        _notes.Pressed += () => SelectTask(Workspace.Notes);
        _feedback.Pressed += () => SelectTask(Workspace.Feedback);
        _settings.Pressed += OpenSettings;
        _close.Pressed += () => TopLevelCloseRequested?.Invoke();
        BuildSettings();
        BuildFeedback();
        _references.AttachOverlay(this);
        InitializeSeedLibrary(persistenceDirectory);
        RefreshLanguage();
        SelectTask(Workspace.Search);
    }

    // Restore the old shell's evidence lifecycle without restoring its UI/controller.
    // Identity capture only: no compute device creation, dispatch or benchmark.
    internal static void InitializeSearchEvidenceOnMainThread()
    {
        try {
            string backend=RenderingServer.GetCurrentRenderingDriverName()?.Trim()??"";
            string gpu=RenderingServer.GetVideoAdapterName()?.Trim()??"";
            SearchPerformanceProfileFoundation.ObserveRenderingBackend(backend);
            FamilyDeviceProfileFoundation.CaptureAvailabilityOnMainThread();
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(gpu,backend);
            RuntimeLog.Info($"workbenchHardwareIdentity=true;{SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().Summary};computeCreated=false");
        } catch(Exception ex) {RuntimeLog.Warn("workbenchHardwareIdentityUnavailable="+ex.GetType().Name);}
        SearchPredictabilityVerificationStore.InitializeOnMainThread();
        GpuCostCalibration.InitializeOnMainThread();
        CpuCostCalibration.InitializeOnMainThread();
    }
    private static void FlushSearchEvidenceOnMainThread()
    {
        SearchPredictabilityVerificationStore.TryFlushPendingOnMainThread();
        GpuCostCalibration.TryFlushPendingOnMainThread();
        CpuCostCalibration.TryFlushPendingOnMainThread();
    }
    public override void _ExitTree() => FlushSearchEvidenceOnMainThread();

    private void SelectTask(Workspace workspace, string? encyclopediaTopic = null,
        string? predictorSeed = null)
    {
        _references?.CloseModal();
        _workspace = workspace;
        if (workspace == Workspace.Search) { _partyExpected = null; _partyResultDraft = null; }
        if (workspace == Workspace.Search) _references?.Home();
        if (_references is not null) _references.Visible = workspace == Workspace.Search;
        if (workspace == Workspace.Encyclopedia)
        {
            if (_encyclopediaCanvas is null)
            {
                _encyclopediaCanvas = new EncyclopediaCanvas();
                _encyclopediaCanvas.ReturnToSearchRequested += () => SelectTask(Workspace.Search);
                _encyclopediaCanvas.OpenSeedRequested += OpenEncyclopediaSeed;
                _content.AddChild(_encyclopediaCanvas);
                _encyclopediaCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                _encyclopediaCanvas.Refresh(_languageCode);
            }
            _encyclopediaCanvas.Open(encyclopediaTopic);
        }
        if (_encyclopediaCanvas is not null) _encyclopediaCanvas.Visible = workspace == Workspace.Encyclopedia;
        if (workspace == Workspace.Analysis && _predictor is null) CreatePredictor(predictorSeed);
        if (workspace == Workspace.Analysis && _partyResultDraft is not null)
        {
            _predictorController?.SetPartyDraft(_partyResultDraft, _partyExpected?.Seed, _partyExpected?.Document.Party);
            _partyResultDraft = null; _partyExpected = null;
        }
        if (_predictor is not null) _predictor.Visible = workspace == Workspace.Analysis;
        if (workspace == Workspace.Status) OpenStatus();
        if (_statusPage is not null) _statusPage.Visible = workspace == Workspace.Status;
        if (workspace == Workspace.Notes) OpenNotes();
        if (_notesPage is not null) _notesPage.Visible = workspace == Workspace.Notes;
        _settingsPage.Visible = workspace == Workspace.Settings;
        _feedbackPage.Visible = workspace == Workspace.Feedback;
        if (workspace == Workspace.Feedback) RefreshFeedback();
        if (workspace == Workspace.Settings) RefreshSettings(resetInput: true);
        if (_seedLibrary is not null)
        {
            _seedLibrary.Visible = workspace == Workspace.Seeds;
            if (workspace == Workspace.Seeds) _seedLibrary.RefreshEntries();
        }
        _content.Name = workspace + "Canvas";
        SetWorkspaceActive();
    }

    private void OpenEncyclopediaSeed(string seed)
    {
        bool predictorCreated = _predictor is null;
        _partyExpected = null;
        _partyResultDraft = null;
        SelectTask(Workspace.Analysis, predictorSeed: seed);
        if (predictorCreated || _predictor is null || _predictorController is null) return;
        _predictorController.SetPartyDraft(null);
        _predictor.SetSeedText(seed, commit: true);
        _predictorController.AnalyzeCurrentDraft();
    }

    private void SetWorkspaceActive()
    {
        _palette.SetActive(_search, _workspace == Workspace.Search);
        _palette.SetActive(_analysis, _workspace == Workspace.Analysis);
        _palette.SetActive(_seeds, _workspace == Workspace.Seeds);
        _palette.SetActive(_encyclopedia, _workspace == Workspace.Encyclopedia);
        _palette.SetActive(_status, _workspace == Workspace.Status);
        _palette.SetActive(_notes, _workspace == Workspace.Notes);
        _palette.SetActive(_settings, _workspace == Workspace.Settings);
        _palette.SetActive(_feedback, _workspace == Workspace.Feedback);
    }

    private void RefreshLanguage()
    {
        string language = _persistence!.Preferences.LanguageOverride;
        if (language is not ("en" or "zh"))
            language = TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        if (_languageCode == language) return;
        _languageCode = language;
        IUiTextProvider text = JsonUiTextProvider.CreateUi13(language);
        _search.Text = text.Get("shell.search");
        _analysis.Text = text.Get("shell.analysis");
        _seeds.Text = language == "zh" ? "种子库" : "Seed library";
        _seedLibrary?.Refresh(language);
        _encyclopedia.Text = text.Get("shell.encyclopedia");
        _status.Text = text.Get("shell.status");
        _notes.Text = text.Get("shell.about");
        _notes.TooltipText = text.Get("shell.about_hint");
        _settings.Text = text.Get("shell.settings");
        _feedback.Text = text.Get("feedback.title");
        LocalizeSettings(text);
        RefreshFeedback();
        ApplyPalette();
    }

    private void ApplyPalette()
    {
        _background!.Color = _palette.Color(_palette.Canvas);
        _title!.AddThemeColorOverride("font_color", _palette.Color(_palette.Text));
        foreach (var b in new[] { _search, _analysis, _seeds, _encyclopedia, _status, _notes, _feedback, _settings, _close })
        {
            var donor = _palette.Button(b.Text);
            foreach (string style in new[] { "normal", "hover", "pressed", "disabled", "focus" }) b.AddThemeStyleboxOverride(style, donor.GetThemeStylebox(style));
            foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color", "font_focus_color" }) b.AddThemeColorOverride(color, donor.GetThemeColor(color));
            donor.Free();
        }
        SetWorkspaceActive();
        _references?.Refresh(_palette, _languageCode, JsonUiTextProvider.CreateUi13(_languageCode));
        _encyclopediaCanvas?.Refresh(_languageCode);
        _predictor?.ApplyLocalization(JsonUiTextProvider.CreatePredictorUi13(_languageCode),
            RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver.Create(_languageCode));
        RefreshStatus();
        RefreshNotes();
    }

    public override void _Ready() => FitCanvas();
    public override void _Process(double delta)
    {
        RuntimeLog.PumpOnMainThread();
        PollFeedbackExport();
        FlushSearchEvidenceOnMainThread();
        _persistence?.Tick(delta);
        FitCanvas();
        _localePoll -= delta;
        if (_localePoll <= 0) { _localePoll = 0.25; RefreshLanguage(); if (_workspace == Workspace.Settings) RefreshSettings(); }
    }

    private void FitCanvas()
    {
        Vector2 host = GetParent<Control>().Size;
        float scale = Math.Min(1f, Math.Min(host.X / CanvasSize.X, host.Y / CanvasSize.Y));
        Scale = Vector2.One * scale;
        Position = (host - CanvasSize * scale) * 0.5f;
    }

    public void Open() {
        WorkspacePalette.SetKeyboardFocusVisible(false);
        if (_workspace == Workspace.Status) RefreshStatus();
        Show(); FitCanvas();
    }
    public void CleanupForTopLevelClose() {
        while (_seedLibrary?.CloseModal() == true) { }
        _references?.CloseSearch(); _references?.CloseModal(); CloseSettings(); Hide(); }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventMouseButton { Pressed: true } || input is InputEventScreenTouch { Pressed: true })
            WorkspacePalette.SetKeyboardFocusVisible(false);
        else if (input is InputEventKey { Pressed: true } key &&
                 key.Keycode is Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End
                     or Key.Pageup or Key.Pagedown or Key.Enter or Key.KpEnter or Key.Space or Key.Escape)
            WorkspacePalette.SetKeyboardFocusVisible(true);
        else if (input is InputEventJoypadButton { Pressed: true })
            WorkspacePalette.SetKeyboardFocusVisible(true);
        // Mouse motion alone must not erase an existing keyboard navigation indicator.
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!IsVisibleInTree() || input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_workspace == Workspace.Settings) CloseSettings();
        else if (_seedLibrary?.CloseModal() == true) { }
        else if (_references?.CloseModal() == true) { }
        else TopLevelCloseRequested?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    private static StyleBoxFlat ButtonStyle(bool active) => new()
    {
        BgColor = active ? new Color("26374b") : new Color(0, 0, 0, 0),
        ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5
    };

    private static Button MakeButton(string text = "")
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(112, 48) };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(false));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(true));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(true));
        return button;
    }
}
