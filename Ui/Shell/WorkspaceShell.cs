using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using Godot;
using MegaCrit.Sts2.Core.Rooms;
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
    private Button _search = null!;
    private Button _analysis = null!;
    private Button _currentPrediction = null!;
    private MegaCrit.Sts2.Core.Runs.IRunState? _liveRun;
    private bool _runPredictionLoaded;
    private bool _openedOnce;
    private Button _encyclopedia = null!;
    private Button _status = null!;
    private Button _notes = null!;
    private Button _settings = null!;
    private Button _close = null!;
    private readonly Control _content = new() { Name = "TaskCanvas", MouseFilter = MouseFilterEnum.Ignore };
    private SearchWorkspace? _persistence;
    private string _persistenceDirectory = "";
    private string _languageCode = string.Empty;
    private double _localePoll;
    private QueryWorkbenchFrame? _references;
    private EncyclopediaCanvas? _encyclopediaCanvas;
    private WorkspacePalette _palette = WorkspacePalette.MainPanel;
    private ColorRect? _background;
    private Label? _title;
    private ModRuntimeSnapshot _runtime = null!;
    private RolltheSpire2.Ui.Pages.Analysis.AnalysisPage? _predictor;
    private RolltheSpire2.Ui.Controllers.AnalysisPageController? _predictorController;


    private enum Workspace { Search, Analysis, Seeds, Encyclopedia, Status, Notes, Settings, Feedback, CurrentPrediction }
    private Workspace _workspace;

    public Button CloseButton => _close;
    public event Action? TopLevelCloseRequested;

    private CrystalSphereAssistantPanel? _crystalScene;
    internal event Action<CrystalSphereAssistantPanel>? CurrentPredictionCreated;
    internal void InitializeRunScene(ModRuntimeSnapshot snapshot, MegaCrit.Sts2.Core.Runs.IRunState run,
        CrystalSphereAssistantPanel? panel = null)
    {
        _crystalScene = panel;_liveRun=run;
        Initialize(snapshot);
    }

    public void Initialize(ModRuntimeSnapshot snapshot) => InitializeWithPersistence(snapshot, OS.GetUserDataDir());

    internal void InitializeWithPersistence(ModRuntimeSnapshot snapshot, string persistenceDirectory) =>
        InitializeWithPersistence(snapshot, persistenceDirectory, null);

    internal void InitializeWithPersistence(ModRuntimeSnapshot snapshot, string persistenceDirectory,
        MenuWorkspaceEntryInputs? prepared)
    {
        _runtime = snapshot;
        if (_liveRun is null) InitializeSearchEvidenceOnMainThread();
        Name = "RolltheSpire2_WorkspaceShell";
        Size = CanvasSize;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        // Read existing preferences without creating a Search cursor for an empty UI.
        _persistence = SearchWorkspace.Open(persistenceDirectory);
        _persistenceDirectory = persistenceDirectory;
        MainPanelTheme.SelectFont(_persistence.Preferences.FontChoice);
        _appliedFontChoice = MainPanelTheme.FontChoice;
        if (_liveRun is null && !_persistence.Runtime.HasSearchInFlight)
            _persistence.EnsureEnvironment(prepared?.Environment ?? SearchEnvironmentSignatureBuilder.Capture(snapshot));
        BuildWorkspaceLayout();
        InitializeIssues();
        if (_liveRun is not null)
        {
            foreach (var navigation in new[] { _search, _seeds, _status })
                navigation.Hide();
            if (_crystalScene is not null)
            {
                _content.AddChild(_crystalScene);
                _crystalScene.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            }
            _close.Pressed += () => TopLevelCloseRequested?.Invoke();
            _analysis.Pressed += ShowLiveSeedPrediction;
            _currentPrediction.Pressed += () => SelectTask(Workspace.CurrentPrediction);
            _encyclopedia.Pressed += () => SelectTask(Workspace.Encyclopedia);
            _notes.Pressed += () => SelectTask(Workspace.Notes);
            _feedback.Pressed += () => SelectTask(Workspace.Feedback);
            _settings.Pressed += OpenSettings;
            InitializeLibraryNavigation();
            RefreshLanguage();
            if (_crystalScene is not null) SelectTask(Workspace.CurrentPrediction);
            else { _workspace = Workspace.Analysis; SetWorkspaceActive(); }
            return;
        }
        _references = new QueryWorkbenchFrame(snapshot, _persistence) { Visible = false };
        if (prepared is not null) _references.AdoptPreparedInputs(prepared.Editor, prepared.Freshness,
            prepared.Solo, prepared.Character, prepared.Ascension);
        _content.AddChild(_references);
        _references.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        InitializeResultsEntry();
        _references.ModalChanged += open =>
        {
            _search.Disabled = _analysis.Disabled = _seeds.Disabled = _encyclopedia.Disabled = _status.Disabled = _notes.Disabled = _feedback.Disabled = _settings.Disabled = _close.Disabled = open;


        };

        _references.Navigate += NavigateSearch;
        _search.Pressed += () => SelectTask(Workspace.Search);
        _analysis.Pressed += () => SelectTask(Workspace.Analysis);
        _encyclopedia.Pressed += () => SelectTask(Workspace.Encyclopedia);
        _status.Pressed += () => SelectTask(Workspace.Status);
        _notes.Pressed += () => SelectTask(Workspace.Notes);
        _feedback.Pressed += () => SelectTask(Workspace.Feedback);
        _settings.Pressed += OpenSettings;
        _close.Pressed += () => TopLevelCloseRequested?.Invoke();
        _references.AttachOverlay(this);
        InitializeLibraryNavigation();
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
    public override void _ExitTree()
    {
        StopPagePreparation(retire: true);
        if (_liveRun is null) FlushSearchEvidenceOnMainThread();
    }

    private void NavigateSearch(string page)
    {
        _references?.CloseModal();
        if (page == "encyclopedia" || page.StartsWith("encyclopedia:", StringComparison.Ordinal))
            SelectTask(Workspace.Encyclopedia, page == "encyclopedia" ? null : page["encyclopedia:".Length..]);
        else SelectTask(Workspace.Search);
    }

    private void SelectTask(Workspace workspace, string? encyclopediaTopic = null,
        string? predictorSeed = null)
    {
        if(_liveRun!=null && workspace==Workspace.Search) workspace=Workspace.Analysis;
        if (DeferWorkspacePage(workspace, () => SelectTask(workspace, encyclopediaTopic, predictorSeed))) return;
        _notesPage?.SetPresentationActive(workspace == Workspace.Notes);
        bool libraryAlreadyCreated = _seedLibrary is not null;
        if (workspace == Workspace.Settings) EnsureSettings();
        if (workspace == Workspace.Feedback) EnsureFeedback();
        if (workspace == Workspace.Seeds) EnsureSeedLibrary();
        if (workspace == Workspace.CurrentPrediction && _crystalScene is null && _liveRun is not null)
        {
            // Preserve the current-event entry, but ordinary run opening does
            // not construct an unrelated Crystal view to encode run identity.
            _crystalScene = new CrystalSphereAssistantPanel();
            _crystalScene.Initialize(_liveRun, _runtime);
            _content.AddChild(_crystalScene);
            _crystalScene.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            CurrentPredictionCreated?.Invoke(_crystalScene);
        }
        if(_crystalScene!=null) _crystalScene.Visible=workspace==Workspace.CurrentPrediction;
        _references?.CloseModal();
        _workspace = workspace;
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
        if (_predictor is not null) _predictor.Visible = workspace == Workspace.Analysis;
        if (workspace == Workspace.Status) OpenStatus();
        if (_statusPage is not null) _statusPage.Visible = workspace == Workspace.Status;
        if (workspace == Workspace.Notes) OpenNotes();
        if (_notesHost is not null) _notesHost.Visible = workspace == Workspace.Notes;
        if (_settingsPage is not null) _settingsPage.Visible = workspace == Workspace.Settings;
        if (_feedbackPage is not null) _feedbackPage.Visible = workspace == Workspace.Feedback;
        if (_seedLibrary is not null)
        {
            _seedLibrary.Visible = workspace == Workspace.Seeds;
            if (workspace == Workspace.Seeds && libraryAlreadyCreated) _seedLibrary.RefreshEntries();
        }
        _content.Name = workspace + "Canvas";

        SetWorkspaceActive();
    }

    private void OpenEncyclopediaSeed(string seed)
    {
        bool predictorCreated = _predictor is null;
        SelectTask(Workspace.Analysis, predictorSeed: seed);
        if (predictorCreated || _predictor is null || _predictorController is null) return;
        _predictorController.SetPartyDraft(null);
        _predictor.SetSeedText(seed, commit: true);
        _predictorController.AnalyzeCurrentDraft();
    }

    private void SetWorkspaceActive()
    {
        _palette.SetActive(_search, _workspace == Workspace.Search, horizontalPadding: 10);
        _palette.SetActive(_analysis, _workspace == Workspace.Analysis, horizontalPadding: 10);
        _palette.SetActive(_currentPrediction, _workspace == Workspace.CurrentPrediction, horizontalPadding: 10);
        _palette.SetActive(_seeds, _workspace == Workspace.Seeds, horizontalPadding: 10);
        _palette.SetActive(_encyclopedia, _workspace == Workspace.Encyclopedia, horizontalPadding: 10);
        _palette.SetActive(_status, _workspace == Workspace.Status, horizontalPadding: 10);
        _palette.SetActive(_notes, _workspace == Workspace.Notes, horizontalPadding: 10);
        _palette.SetActive(_settings, _workspace == Workspace.Settings, horizontalPadding: 10);
        _palette.SetActive(_feedback, _workspace == Workspace.Feedback, horizontalPadding: 10);
        ApplyNotesReminderColor();
    }

    private void RefreshLanguage()
    {
        string language = RolltheSpire2.Ui.Settings.UiLanguagePreferences.Resolve(_persistence!.Preferences.LanguageOverride);
        if (_languageCode == language) return;
        _languageCode = language;
        IUiTextProvider text = JsonUiTextProvider.CreateUi13(language);
        _search.Text = text.Get("shell.search");
        _analysis.Text = text.Get("shell.analysis");
        _currentPrediction.Text=JsonUiTextProvider.CreatePredictorUi13(language).Get("predictor.crystal.current_page");
        _crystalScene?.ApplyLanguage(language);
        _seeds.Text = language == "zh" ? "资料库" : "Library";
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
        MainPanelTheme.SetBackground(_background!, _palette.Canvas);
        _title!.AddThemeColorOverride("font_color", _palette.Color(_palette.Text));
        foreach (var button in HeaderButtons())
        {
            _palette.ApplyButton(button);
            button.CustomMinimumSize = new Vector2(button == _close ? 48 : 0, 48);
            button.AddThemeFontSizeOverride("font_size", 18);
            _palette.SetActive(button, false, horizontalPadding: 10);
            ApplyNavigationIconColor(button);
        }
        SetWorkspaceActive();
        _references?.CloseModal();
        _references?.Refresh(_languageCode, JsonUiTextProvider.CreateUi13(_languageCode));
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
        RefreshIssues();
        PollFeedbackExport();
        TickNotesReminder(delta);
        if(_liveRun==null) FlushSearchEvidenceOnMainThread();
        if (_persistence is { } store && !store.Runtime.IsPolling) store.Tick(delta);
        FitCanvas();
        _localePoll -= delta;
        if (_localePoll <= 0) { _localePoll = 0.25; RefreshLanguage(); if (_workspace == Workspace.Settings) RefreshSettings(); }
        TickPagePreparation();
    }

    private void FitCanvas()
    {
        Vector2 host = GetParent<Control>().Size;
        float scale = Math.Max(.1f, Math.Min(1f, Math.Min(host.X / CanvasSize.X, host.Y / CanvasSize.Y)));
        Scale = Vector2.One * scale;
        float spareY=Math.Max(0,host.Y-CanvasSize.Y*scale);
        Position = new Vector2((host.X - CanvasSize.X * scale) * .5f,
            _liveRun==null?spareY*.5f:Math.Min(spareY,Math.Max(spareY*.5f,112)));
    }

    public void Open() {
        using var editorObservation = _liveRun is null ? _references?.ObserveEnvironmentInputs() : null;
        RefreshWorkspaceFont();
        if (_liveRun is null) _references?.RefreshEnvironmentInputs();
        WorkspacePalette.SetKeyboardFocusVisible(false);
        if (_workspace == Workspace.Status) RefreshStatus();
        Show(); FitCanvas();
        _notesPage?.SetPresentationActive(_workspace == Workspace.Notes);
        DeferIdlePagePreparation();
        BeginNotesReminder();
        bool firstOpen = !_openedOnce;
        _openedOnce = true;
        if (_liveRun == null) return;
        if (_liveRun.CurrentRoom is EventRoom room && room.CanonicalEvent.Id.Entry == "CRYSTAL_SPHERE")
        {
            if (firstOpen) SelectTask(Workspace.CurrentPrediction);
        }
        else ShowLiveSeedPrediction(activate: firstOpen || _workspace == Workspace.Analysis);
        DeferIdlePagePreparation();
    }

    internal void ApplyPreparedEntry(MenuWorkspaceEntryInputs? prepared)
    {
        if (prepared is null || _liveRun is not null) return;
        if (_persistence is not null && !_persistence.Runtime.HasSearchInFlight)
            _persistence.EnsureEnvironment(prepared.Environment);
        _references?.AdoptPreparedInputs(prepared.Editor, prepared.Freshness,
            prepared.Solo, prepared.Character, prepared.Ascension);
        // Consume the verified receipt in the entry turn. The deferred native
        // Push/Open will perform its normal fresh editor observation again.
        _references?.RefreshEnvironmentInputs();
    }
    private void ShowLiveSeedPrediction() => ShowLiveSeedPrediction(activate: true);
    private void ShowLiveSeedPrediction(bool activate)
    {
        try
        {
            // Reuse the controls and result only after a fresh native content
            // observation and the current seed/roster/unlock input comparison.
            RuntimeAuthorityEnvironment.CaptureOnMainThread(_runtime);
            var context = CaptureRunPredictionContext(_liveRun!, _runtime.Detection.NormalizedVersion,
                _runtime.Profile.ProfileId, MegaCrit.Sts2.Core.Context.LocalContext.NetId, _persistence!.PredictorContext);
            string key = System.Text.Json.JsonSerializer.Serialize(new { Seed = _liveRun!.Rng.StringSeed, Context = context,
                RuntimeAuthorityEnvironment.Current.Authority.Fingerprint,
                RuntimeAuthorityEnvironment.Current.Authority.CaptureIssues });
            if (!_runPredictionLoaded || key != _livePredictionBinding || _predictor?.LastDocument is null ||
                _predictor.LastRequest?.OriginalSeed != _liveRun.Rng.StringSeed ||
                !RuntimeAuthorityEnvironment.Current.Authority.Fingerprint.Complete)
            {
                var priorPage = _workspace;
                OpenRunPrediction(_liveRun!, context);
                _livePredictionBinding = key; _runPredictionLoaded = true;
                if (!activate) SelectTask(priorPage);
            }
            else if (activate) SelectTask(Workspace.Analysis);
        }
        catch (Exception ex)
        {
            _runPredictionLoaded = false; _livePredictionBinding = null;
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "LivePredictionCaptureFailed", nameof(WorkspaceShell), "CaptureLiveRun", "issues.prediction_failed");
            SelectTask(Workspace.Analysis);
            _predictor?.ShowUnhandledError();
            _predictor?.ShowLibraryReceipt(JsonUiTextProvider.CreatePredictorUi13(_languageCode).Get("predictor.crystal.context_failed") + "\n" + ex.Message);
        }
    }
    public void CleanupForTopLevelClose() {
        StopPagePreparation();
        CloseIssues();
        while (_seedLibrary?.CloseModal() == true) { }
        _references?.CloseSearch(); _references?.CloseModal(); CloseSettings(); Hide(); }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        DeferIdlePagePreparation();
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
        HandleBack();
        GetViewport().SetInputAsHandled();
    }
    internal void HandleBack()
    {
        if (CloseIssues()) { }
        else if (_workspace == Workspace.Settings) CloseSettings();
        else if (_seedLibrary?.CloseModal() == true) { }
        else if (_references?.CloseModal() == true) { }
        else TopLevelCloseRequested?.Invoke();
    }

}
