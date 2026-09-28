using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Pages.Advanced;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Pages.Events;
using RolltheSpire2.Ui.Pages.DeveloperNotes;
using RolltheSpire2.Ui.Pages.SaveStatus;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Settings;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;
using RolltheSpire2.Ui.Settings;

using RolltheSpire2.Search.Predictability;
namespace RolltheSpire2.Ui.Shell;

internal sealed partial class AppShell : Control
{
    private ModRuntimeSnapshot _runtime = ModRuntimeSnapshot.NotInitialized;
    private PanelContainer? _windowPanel;
    private GlobalHeader? _header;
    private HBoxContainer? _body;
    private NavigationRail? _navigation;
    private PageHost? _pageHost;
    private GlobalStatusBar? _statusBar;
    private ToastHost? _toast;
    private Control? _resizeGrip;
    private IUiTextProvider? _uiText;
    private IGameContentNameResolver? _contentNames;
    private IGameIconResolver? _icons;
    private ICharacterPoolIconProvider? _characterPoolIcons;
    private RuntimeCharacterCatalogSnapshot? _runtimeCharacterCatalog;
    private IRelicTooltipResolver? _relicTooltipResolver;
    private IPotionTooltipResolver? _potionTooltipResolver;
    private ICardTooltipResolver? _cardTooltipResolver;
    private ICardPickerFilterIconProvider? _cardPickerFilterIcons;
    private AnchoredTooltipHost? _tooltipHost;
    private AnalysisPage? _analysisPage;
    private AnalysisPageController? _analysisController;
    private SearchPage? _searchPage;
    private SearchPageController? _searchController;
    private SettingsPage? _settingsPage;
    private string _languageCode = "en";
    private bool _analysisOnEntryPending;
    private readonly RuntimePredictionSettings _predictionSettings = new();
    private SearchWorkspacePersistence? _searchPersistence;
    private IRuntimePredictionDiagnosticSink _diagnosticSink = new NullRuntimePredictionDiagnosticSink();
    private RuntimePredictionDiagnosticLogger? _diagnosticLogger;
    private bool _initialized;
    private bool _dragging;
    private bool _resizing;
    private double _languagePollRemaining;
    private Vector2 _pointerStart;
    private Vector2 _positionStart;
    private Vector2 _sizeStart;

    public event Action? TopLevelCloseRequested;

    public void Initialize(ModRuntimeSnapshot runtime)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        _runtime = runtime;
        Name = "RolltheSpire2_UI1B0_AppShell";
        MouseFilter = Control.MouseFilterEnum.Stop;
        ZIndex = UiZLayers.ShellSurface;
        ClipContents = false;
        _searchPersistence = new SearchWorkspacePersistence(OS.GetUserDataDir(), runtime.Profile.ProfileId);
        _searchPersistence.ApplyPreferencesToRuntimeSettings(_predictionSettings);
        SearchEnvironmentSignature currentEnvironment = SearchEnvironmentSignatureBuilder.Capture(runtime);
        _searchPersistence.EnsureEnvironment(currentEnvironment);
        // Follow game by default; only a new explicit override pins the UI language.
        // Historical last-observed Language metadata is not an explicit choice.
        _languageCode = PreferredUiLanguage();
        string diagnosticDirectory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "logs");
        try
        {
            var logger = new RuntimePredictionDiagnosticLogger(
                diagnosticDirectory,
                _predictionSettings.EnableDiagnosticLogging,
                _predictionSettings.DiagnosticLogLevel);
            _diagnosticLogger = logger;
            _diagnosticSink = logger;
        }
        catch (Exception ex)
        {
            _diagnosticSink = new NullRuntimePredictionDiagnosticSink(
                diagnosticDirectory,
                "DiagnosticInitializationFailed:" + ex.GetType().Name);
            RuntimeLog.Error($"runtimePredictionDiagnosticInitializationFailed={ex.GetType().Name}:{ex.Message}");
        }
        _icons = new ReflectionGameIconResolver($"{runtime.Profile.ProfileId}:{runtime.Detection.DisplayVersion}");
        _runtimeCharacterCatalog = RuntimeCharacterCatalogCapture.Capture();
        RuntimeLog.Detail(
            $"runtimeCharacterCatalogCaptured=true;count={_runtimeCharacterCatalog.EffectiveCharacters.Count};" +
            $"complete={_runtimeCharacterCatalog.IdentityCaptureComplete.ToString().ToLowerInvariant()};" +
            $"evidence={_runtimeCharacterCatalog.EvidenceCode};");
        _characterPoolIcons = new NativeCharacterPoolIconProvider(_icons);
        try
        {
            // The main RenderingServer already knows the active adapter before any RT2
            // compute backend is created. Prime Hardware identity here so the very first
            // Planner in a cold process can address durable device-specific evidence.
            // This is identity-only: no RenderingDevice creation, dispatch, or warm-up.
            string renderingBackend = RenderingServer.GetCurrentRenderingDriverName()?.Trim() ?? string.Empty;
            string gpuDevice = RenderingServer.GetVideoAdapterName()?.Trim() ?? string.Empty;
            SearchPerformanceProfileFoundation.ObserveRenderingBackend(renderingBackend);
            RolltheSpire2.Search.FamilyExecution.FamilyDeviceProfileFoundation.CaptureAvailabilityOnMainThread();
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(gpuDevice, renderingBackend);
            bool analyticalIdentityPrimed = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().HasKnownGpu;
            RuntimeLog.Detail(
                $"planningHardwareIdentityPrimed={analyticalIdentityPrimed.ToString().ToLowerInvariant()};" +
                $"gpuDevice={gpuDevice};renderingBackend={renderingBackend};source=RenderingServerMainAdapter;workloadCreated=false;");
        }
        catch (Exception ex)
        {
            // Hardware identity is performance compatibility metadata only. Missing identity
            // remains a CalibrationBootstrap condition, never Search admission authority.
            RuntimeLog.Detail($"planningHardwareIdentityPrimed=false;reason={ex.GetType().Name};source=RenderingServerMainAdapter;");
        }
        SearchPredictabilityVerificationStore.InitializeOnMainThread();
        GpuCostCalibration.InitializeOnMainThread();
        CpuCostCalibration.InitializeOnMainThread();
        _relicTooltipResolver = new RuntimeRelicTooltipResolver();
        _potionTooltipResolver = new RuntimePotionTooltipResolver();
        _cardTooltipResolver = new RuntimeCardTooltipResolver();
        _cardPickerFilterIcons = new CardPickerFilterIconProvider();
        BuildShell();
        RuntimeLog.Detail(
            $"uiZIndexAudit=true;maxAssignedZIndex={UiZLayers.HighestAssigned};withinCanvasItemLimit=true;");
        SetLanguage(_languageCode);
        AppPageKey initialPage = ResolvePersistedPage();
        Navigate(initialPage);
        SetRuntimeAwareIdleStatus();
        RuntimeLog.Ui($"App shell initialized: restoredPage={initialPage}; fallbackDefault=Analysis; readableLog={RuntimeLog.CurrentLogPath}");
    }

    public override void _Ready()
    {
        ApplyDefaultGeometry();
        ApplyPersistedGeometry();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
        {
            return;
        }

        // Phase A2 central backstop. Page-owned surfaces get the first chance
        // to consume exactly one internal layer. Only a Page-main Escape reaches
        // the already-guarded top-level close authority. Child pickers may also
        // consume the event earlier in the same Godot unhandled-key stage; both
        // paths converge on the same Page-owned ordering and SetInputAsHandled.
        if (_pageHost?.ActivePage is IPageSurfaceLifecycle lifecycle && lifecycle.TryHandleEscape())
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        RuntimeLog.Detail($"uiSurfaceEscapeTopLevelCloseRequested=true;page={_pageHost?.ActivePageKey?.ToString() ?? "None"}");
        TopLevelCloseRequested?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        _tooltipHost?.Dismiss();
        if (_diagnosticLogger is not null)
        {
            _ = _diagnosticLogger.DisposeAsync();
        }

        // Godot exits child pages before their AppShell parent. Search and
        // Analysis share one provider for source-audited Card Library Texture2D
        // resources. Dispose only releases our managed Resource references; it
        // never Free()s native Card Library nodes or game-owned textures.
        _characterPoolIcons?.Dispose();
        _characterPoolIcons = null;

        // Cancel any active Production Search before joining the persistent GPU
        // owner. The owner stop item is serialized behind the active job, so the
        // backend reaches its existing cancellation/cleanup boundary before the
        // Local RenderingDevice is destroyed on its owning thread.
        _searchController?.CancelForAppShutdown();
        CaptureAndFlushPersistentUiState();
        RuntimeLog.PumpOnMainThread();
    }

    public override void _Process(double delta)
    {
        RuntimeLog.PumpOnMainThread();
        SearchPredictabilityVerificationStore.TryFlushPendingOnMainThread();
        GpuCostCalibration.TryFlushPendingOnMainThread();
        CpuCostCalibration.TryFlushPendingOnMainThread();
        _languagePollRemaining -= delta;
        if (_languagePollRemaining <= 0d)
        {
            _languagePollRemaining = 0.25d;
            RefreshLanguageFromGameLocale();
        }
        if (_searchPersistence is not null)
        {
            _searchPersistence.CapturePreferences(_languageCode, Size, _predictionSettings);
            _searchPersistence.Tick(delta);
        }
        if (_dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _dragging = false;
            }
            else
            {
                Vector2 current = GetViewport().GetMousePosition();
                Position = _positionStart + current - _pointerStart;
                ClampToViewport();
            }
        }
        if (_resizing)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _resizing = false;
            }
            else
            {
                Vector2 current = GetViewport().GetMousePosition();
                Vector2 deltaSize = current - _pointerStart;
                SetLogicalSize(_sizeStart + deltaSize);
            }
        }
    }

    public void Open()
    {
        RefreshLanguageFromGameLocale();
        Visible = true;
        ClampToViewport();
        QueueAnalysisOnEntry();
        RuntimeLog.Ui("App shell opened.");
    }

    private void BuildShell()
    {
        _tooltipHost = new AnchoredTooltipHost(_relicTooltipResolver!, _potionTooltipResolver!, _cardTooltipResolver!);
        _windowPanel = new PanelContainer();
        _windowPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Ui1Theme.ApplyPanel(_windowPanel, Ui1SurfaceRole.Window, 5f, 1);
        var rootColumn = new VBoxContainer();
        rootColumn.AddThemeConstantOverride("separation", 0);
        _header = new GlobalHeader();
        _header.BindRuntime(_runtime);
        _header.DragInput += HandleDragInput;
        _header.CloseRequested += () => TopLevelCloseRequested?.Invoke();

        var topSeparator = new HSeparator();
        Ui1Theme.ApplySeparator(topSeparator);
        _body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 0);
        _navigation = new NavigationRail();
        _navigation.NavigateRequested += Navigate;
        var verticalSeparator = new VSeparator();
        verticalSeparator.AddThemeStyleboxOverride("separator", Ui1Theme.Surface(Ui1SurfaceRole.Navigation, 0f, 1));
        _pageHost = new PageHost();
        RegisterPages();
        _body.AddChild(_navigation);
        _body.AddChild(verticalSeparator);
        _body.AddChild(_pageHost);

        _statusBar = new GlobalStatusBar();
        var statusSeparator = new HSeparator();
        Ui1Theme.ApplySeparator(statusSeparator);
        rootColumn.AddChild(_header);
        rootColumn.AddChild(topSeparator);
        rootColumn.AddChild(_statusBar);
        rootColumn.AddChild(statusSeparator);
        rootColumn.AddChild(_body);
        _windowPanel.AddChild(rootColumn);
        AddChild(_windowPanel);
        AddChild(_tooltipHost);
        _tooltipHost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _toast = new ToastHost();
        _toast.AnchorLeft = 1f;
        _toast.AnchorTop = 1f;
        _toast.AnchorRight = 1f;
        _toast.AnchorBottom = 1f;
        _toast.OffsetLeft = -300f;
        _toast.OffsetTop = -94f;
        _toast.OffsetRight = -18f;
        _toast.OffsetBottom = -44f;
        _toast.ZIndex = UiZLayers.ToastModal;
        AddChild(_toast);

        _resizeGrip = new Label
        {
            Text = "◢",
            TooltipText = "Resize",
            MouseFilter = Control.MouseFilterEnum.Stop,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ZIndex = UiZLayers.ShellChrome
        };
        Ui1Theme.ApplyLabel((Label)_resizeGrip, Ui1TextRole.Muted);
        _resizeGrip.AnchorLeft = 1f;
        _resizeGrip.AnchorTop = 1f;
        _resizeGrip.AnchorRight = 1f;
        _resizeGrip.AnchorBottom = 1f;
        _resizeGrip.OffsetLeft = -26f;
        _resizeGrip.OffsetTop = -26f;
        _resizeGrip.OffsetRight = 0f;
        _resizeGrip.OffsetBottom = 0f;
        _resizeGrip.GuiInput += HandleResizeInput;
        AddChild(_resizeGrip);
        Resized += OnShellResized;
    }

    private void RegisterPages()
    {
        _pageHost!.Register(AppPageKey.Analysis, CreateAnalysisPage);
        _pageHost.Register(AppPageKey.Search, CreateSearchPage);
        _pageHost.Register(AppPageKey.Events, () => new EventBrowserPage());
        _pageHost.Register(AppPageKey.SaveStatus, () => new SaveStatusPage(_runtime));
        _pageHost.Register(AppPageKey.DeveloperNotes, () => new DeveloperNotesPage(new DeveloperNotesDocumentProvider()));
        _pageHost.Register(AppPageKey.Advanced, () =>
        {
            var page = new AdvancedPage(_predictionSettings, _searchPersistence!);
            page.OpenLogDirectoryRequested += OpenOperationalLogDirectory;
            return page;
        });
        _pageHost.Register(AppPageKey.Settings, CreateSettingsPage);
    }

    private IAppPage CreateSearchPage()
    {
        _searchPage = new SearchPage(
            _icons!,
            _characterPoolIcons!,
            _runtimeCharacterCatalog?.EffectiveCharacters ?? BaseGameModelKeys.Characters.All,
            _cardPickerFilterIcons!,
            _tooltipHost!,
            _predictionSettings);
        _searchPage.CandidateCopyRequested += CopyText;
        _searchController = new SearchPageController(
            _searchPage,
            _runtime,
            _predictionSettings,
            _diagnosticSink,
            _searchPersistence!,
            SetGlobalStatus,
            OpenSearchCandidate,
            OpenPersistedSearchResult,
            text => _toast?.ShowToast(text));
        return _searchPage;
    }

    private void OpenSearchCandidate(RolltheSpire2.Search.Contracts.SearchCandidate candidate)
    {
        Navigate(AppPageKey.Analysis, analyzeOnEntry: false);
        if (_analysisPage is null)
        {
            return;
        }
        var character = candidate.Authority.Character;
        SeedPredictionRequest request = candidate.PredictionRequest;
        SeedPredictionDocument document = candidate.Document;
        if (SeedPredictionRequest.TryCreate(
                candidate.Seed,
                character,
                candidate.Ascension,
                candidate.Authority.PlayersCount,
                candidate.Authority.PlayerSlotIndex,
                candidate.Authority,
                candidate.PredictionRequest.AncientOptionConditions,
                SeedPredictionDomainSelection.All,
                SeedPredictionInputLimits.MaximumRelicSequencePreviewCount,
                includeDiagnostics: true,
                out SeedPredictionRequest? fullRequest,
                out SeedPredictionRequestError requestError))
        {
            request = fullRequest!.WithComplexBonesDeckInteractions(
                candidate.PredictionRequest.EnableComplexBonesDeckInteractions);
            document = RuntimeProfileRegistry.Predict(_runtime.Detection, request);
        }
        else
        {
            RuntimeLog.Error($"searchWitnessFullAnalysisRequestRejected={requestError};seed={candidate.Seed}");
        }

        SearchMatchWitness? routeWitness = candidate.PrimaryWitness is { OpeningRouteId.Length: > 0 } primaryRouteWitness
            ? primaryRouteWitness
            : candidate.Witnesses.FirstOrDefault(witness => !string.IsNullOrWhiteSpace(witness.OpeningRouteId));
        string witnessOpeningRoute = routeWitness?.OpeningRouteId ?? string.Empty;
        (int? preferredChoiceSlotIndex, string preferredOpeningRoute) =
            NormalizeWitnessOpeningRouteForAnalysis(witnessOpeningRoute);
        string preferredRewardRoute = routeWitness?.RewardRouteGroupId ?? string.Empty;
        // Search -> Predictor is a one-way hard handoff. Exact/Witness route identity is
        // global (`choice.{slot}.{route}`); Predictor presentation is choice-local. Normalize
        // only at this UI handoff seam so the accepted SameRoute witness becomes the visible route.
        _analysisPage.ImportSearchContext(request, preferredOpeningRoute, preferredChoiceSlotIndex);
        _analysisPage.ShowDocument(request, document);
        SetGlobalStatus(GlobalStatusKind.Idle, _uiText!.Get(Ui1TextKey.AnalysisComplete), candidate.Seed);
        RuntimeLog.Detail($"rewriteR3SearchCandidateOpened={candidate.Seed};snapshot={candidate.SnapshotFingerprint};witnessRoute={witnessOpeningRoute};analysisRoute={preferredOpeningRoute};analysisChoiceSlot={preferredChoiceSlotIndex?.ToString() ?? "none"};rewardGroup={preferredRewardRoute}");
    }

    private void OpenPersistedSearchResult(PersistedSearchResult result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Seed))
            return;
        Navigate(AppPageKey.Analysis, analyzeOnEntry: false);
        if (_analysisPage is null || _analysisController is null)
            return;
        ModelKey character = BaseGameModelKeys.Characters.Silent;
        if (!string.IsNullOrWhiteSpace(result.CharacterKey) &&
            ModelKey.TryParseExact(result.CharacterKey, out ModelKey restoredCharacter) &&
            restoredCharacter.IsValid)
        {
            character = restoredCharacter;
        }
        // Persisted Search rows keep only lightweight handoff identity, never a serialized
        // Exact/Witness object. Preserve the accepted opening route when the row was saved;
        // older rows simply fall back to an unselected Predictor route.
        (int? persistedChoiceSlot, string persistedOpeningRoute) =
            NormalizeWitnessOpeningRouteForAnalysis(result.WitnessOpeningRouteId);
        _analysisPage.SetPredictorContext(
            character,
            result.Ascension,
            SeedPredictionInputLimits.MinimumPlayers,
            playerSlotIndex: 0,
            AncientOptionConditionProfile.BroadDefault,
            preferredOpeningRouteId: persistedOpeningRoute,
            preferredOpeningChoiceSlotIndex: persistedChoiceSlot,
            preferredRewardRouteGroupId: string.Empty,
            notify: false);
        _analysisPage.SetSeedText(result.Seed, commit: true);
        _analysisController.AnalyzeCurrentDraft();
        RuntimeLog.Detail(
            $"persistedSearchResultAnalyzeRequested=true;seed={result.Seed};character={character.Serialized};" +
            $"ascension={result.Ascension};queryFingerprint={result.QueryFingerprint};" +
            $"analysisRoute={persistedOpeningRoute};analysisChoiceSlot={persistedChoiceSlot?.ToString() ?? "none"}");
    }

    private IAppPage CreateAnalysisPage()
    {
        _analysisPage = new AnalysisPage(
            _runtime,
            _icons!,
            _characterPoolIcons!,
            _runtimeCharacterCatalog?.EffectiveCharacters ?? BaseGameModelKeys.Characters.All,
            _tooltipHost!);
        _analysisPage.CopySeedRequested += CopyText;
        _analysisController = new AnalysisPageController(
            _analysisPage,
            _runtime,
            _predictionSettings,
            _diagnosticSink,
            _searchPersistence!,
            SetGlobalStatus);

        PredictorContextDocument persisted = _searchPersistence!.PredictorContext;
        ModelKey persistedCharacter = BaseGameModelKeys.Characters.Silent;
        if (!string.IsNullOrWhiteSpace(persisted.CharacterKey) &&
            ModelKey.TryParseExact(persisted.CharacterKey, out ModelKey restored) &&
            restored.IsValid)
        {
            persistedCharacter = restored;
        }
        _analysisPage.SetUnlockState(persisted.AllCharacterCardPoolsUnlocked);
        _analysisPage.SetPredictorContext(
            persistedCharacter,
            persisted.Ascension,
            persisted.PlayersCount,
            persisted.PlayerSlotIndex,
            persisted.AncientOptionConditions,
            persisted.PreferredOpeningRouteId,
            persisted.PreferredOpeningChoiceSlotIndex >= 0 ? persisted.PreferredOpeningChoiceSlotIndex : null,
            persisted.PreferredRewardRouteGroupId,
            notify: false);
        if (!string.IsNullOrWhiteSpace(persisted.Seed))
        {
            _analysisPage.SetSeedText(persisted.Seed, commit: true);
        }
        return _analysisPage;
    }

    private IAppPage CreateSettingsPage()
    {
        _settingsPage = new SettingsPage();
        _settingsPage.LanguageRequested += language => {
            _searchPersistence!.SetLanguageOverride(language);
            SetLanguage(PreferredUiLanguage());
        };
        _settingsPage.SyncLanguage(_searchPersistence!.Preferences.LanguageOverride);
        _settingsPage.CenterWindowRequested += CenterWindow;
        _settingsPage.ResetWindowRequested += () => ApplyDefaultGeometry();
        _settingsPage.DeleteHistoricalDataRequested += () => {
            var result = Infrastructure.Persistence.HistoricalRuntimeData.DeleteArchived(
                Path.Combine(OS.GetUserDataDir(), "RolltheSpire2"));
            if (_uiText is not null) _toast?.ShowToast(_uiText.Format(Ui1TextKey.SettingsHistoricalResult, result.Files, result.Failures));
        };
        SyncSettingsPage();
        return _settingsPage;
    }

    private void Navigate(AppPageKey key) => Navigate(key, analyzeOnEntry: true);

    private void Navigate(AppPageKey key, bool analyzeOnEntry)
    {
        if (_pageHost is not null && _pageHost.ActivePageKey != key)
        {
            if (_pageHost.ActivePage is IPageSurfaceLifecycle outgoingLifecycle)
            {
                outgoingLifecycle.CancelTransientSurfacesForPageSwitch();
            }
            // Never carry keyboard focus into a hidden Page. Mouse-first Pages
            // may legitimately have no focus until their next explicit action.
            _pageHost.ReleaseFocusFromActivePage();
        }

        _tooltipHost?.Dismiss();
        IAppPage page = _pageHost!.Activate(key);
        page.ApplyLocalization(_uiText!, _contentNames!);
        if (key == AppPageKey.Search)
        {
            // Search workspace restoration must happen after localization. The
            // Neow structured editor tree is created by ApplyLocalization, so
            // restoring before this point can persist a valid Draft while the
            // visible picker/editor state is lost.
            _searchController?.EnsurePersistedWorkspaceRestored();
        }
        page.ApplyDisplayMode(AppDisplayMode.Normal);
        if (page is IResponsiveAppPage responsive)
        {
            responsive.SetCompact(Size.X < Ui1Metrics.CompactThreshold);
        }
        _navigation!.Select(key);
        _header!.SetCurrentPage(key);
        _searchPersistence?.SetLastPage(key.ToString());
        if (key == AppPageKey.Settings)
        {
            SyncSettingsPage();
        }
        RuntimeLog.Ui($"Page activated: {key}");
        if (analyzeOnEntry && key == AppPageKey.Analysis) QueueAnalysisOnEntry();
        else _analysisOnEntryPending = false;
    }

    private void QueueAnalysisOnEntry()
    {
        if (_analysisOnEntryPending || _pageHost?.ActivePageKey != AppPageKey.Analysis) return;
        _analysisOnEntryPending = true;
        // Run after restored controls/localization and top-level visibility settle.
        // Search-result handoff supplies its own document/request and cancels this.
        Callable.From(() => {
            if (!_analysisOnEntryPending) return;
            _analysisOnEntryPending = false;
            if (!IsVisibleInTree() || _pageHost?.ActivePageKey != AppPageKey.Analysis || _analysisPage is null) return;
            string seed = _analysisPage.CurrentDraft.RawSeed;
            if (string.IsNullOrWhiteSpace(seed) || !_runtime.Profile.TryCanonicalizeSeed(seed, out _, out _)) return;
            RuntimeLog.Ui($"analysisAutoOnEntry=true;seed={seed};source=PageActivation");
            _analysisController?.AnalyzeCurrentDraft();
        }).CallDeferred();
    }

    private AppPageKey ResolvePersistedPage()
    {
        string raw = _searchPersistence?.Preferences.LastPage ?? string.Empty;
        return Enum.TryParse(raw, ignoreCase: false, out AppPageKey restored) &&
               Enum.IsDefined(restored) && restored != AppPageKey.Events
            ? restored
            : AppPageKey.Analysis;
    }

    private static (int? ChoiceSlotIndex, string LocalRouteId) NormalizeWitnessOpeningRouteForAnalysis(string? witnessRouteId)
    {
        string route = witnessRouteId?.Trim() ?? string.Empty;
        const string prefix = "choice.";
        if (!route.StartsWith(prefix, StringComparison.Ordinal))
        {
            return (null, route);
        }

        int slotSeparator = route.IndexOf('.', prefix.Length);
        if (slotSeparator <= prefix.Length || slotSeparator + 1 >= route.Length ||
            !int.TryParse(route[prefix.Length..slotSeparator], out int slotIndex) || slotIndex < 0)
        {
            return (null, route);
        }

        return (slotIndex, route[(slotSeparator + 1)..]);
    }

    private static string ResolveUiLanguageCode()
    {
        string locale = TranslationServer.GetLocale();
        return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
    }

    private string PreferredUiLanguage() => _searchPersistence?.Preferences.LanguageOverride is "zh" or "en"
        ? _searchPersistence.Preferences.LanguageOverride : ResolveUiLanguageCode();

    private void RefreshLanguageFromGameLocale()
    {
        string liveLanguage = PreferredUiLanguage();
        if (string.Equals(liveLanguage, _languageCode, StringComparison.Ordinal))
        {
            return;
        }

        string previous = _languageCode;
        SetLanguage(liveLanguage);
        RuntimeLog.Detail($"ui1LanguageHotReloaded=true;from={previous};to={liveLanguage};source=TranslationServer");
    }

    private void SetLanguage(string languageCode)
    {
        _languageCode = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        _uiText = JsonUiTextProvider.Create(_languageCode);
        _contentNames = RuntimeGameContentNameResolver.Create(_languageCode);
        _header!.ApplyLocalization(_uiText);
        _navigation!.ApplyLocalization(_uiText);
        _pageHost!.ApplyLocalization(_uiText, _contentNames);
        SyncSettingsPage();
        _searchPersistence?.CapturePreferences(_languageCode, Size, _predictionSettings);
        if (_analysisPage?.LastDocument is null)
        {
            SetRuntimeAwareIdleStatus();
        }
        else
        {
            SetGlobalStatus(GlobalStatusKind.Idle, _uiText.Get(Ui1TextKey.AnalysisComplete), _analysisPage.LastDocument.CanonicalSeed);
        }
    }

    private void SetRuntimeAwareIdleStatus()
    {
        if (_uiText is null || _statusBar is null)
        {
            return;
        }

        if (_runtime.IsCompatibilityFallback)
        {
            SetGlobalStatus(
                GlobalStatusKind.Warning,
                _uiText.Format(Ui1TextKey.RuntimeFallbackStatus, _runtime.Compatibility.ReferenceVersion),
                _runtime.Detection.DisplayVersion);
            return;
        }
        if (_runtime.IsPendingRuntimeValidation)
        {
            SetGlobalStatus(
                GlobalStatusKind.Warning,
                _uiText.Get(Ui1TextKey.RuntimePendingValidationStatus),
                _runtime.Detection.DisplayVersion);
            return;
        }

        SetGlobalStatus(GlobalStatusKind.Idle, _uiText.Get(Ui1TextKey.Ready), string.Empty);
    }

    private void OpenOperationalLogDirectory()
    {
        try
        {
            // RuntimeLog is the existing logging-path authority. The UI does not construct
            // or persist its own RolltheSpire2 log path.
            string directory = RuntimeLog.LogDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                RuntimeLog.Warn("openOperationalLogDirectoryFailed=true;reason=MissingLogDirectoryAuthority");
                return;
            }

            Directory.CreateDirectory(directory);
            string absoluteDirectory = Path.GetFullPath(directory);
            string directoryUri = new Uri(absoluteDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
            Error shellOpenResult = OS.ShellOpen(directoryUri);
            if (shellOpenResult != Error.Ok)
            {
                RuntimeLog.Warn($"openOperationalLogDirectoryFailed=true;reason=ShellOpen:{shellOpenResult}");
                return;
            }

            RuntimeLog.Detail("openOperationalLogDirectory=true");
        }
        catch (Exception ex)
        {
            // Product convenience only: opening Explorer/Finder must never affect the Mod.
            RuntimeLog.Warn($"openOperationalLogDirectoryFailed=true;reason={ex.GetType().Name}:{ex.Message}");
        }
    }

    private void CopyRecentDiagnosticLogPath()
    {
        string value = string.IsNullOrWhiteSpace(RuntimeLog.CurrentLogPath)
            ? RuntimeLog.LogDirectory
            : RuntimeLog.CurrentLogPath;
        CopyText(value);
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        DisplayServer.ClipboardSet(text);
        _toast!.ShowToast(_uiText!.Get(Ui1TextKey.Copied));
    }

    private void SetGlobalStatus(GlobalStatusKind kind, string text, string context)
    {
        _statusBar?.SetStatus(kind, text, context);
    }

    public void CleanupForTopLevelClose()
    {
        _tooltipHost?.Dismiss();
        _dragging = false;
        _resizing = false;
        if (_pageHost is not null)
        {
            foreach (IPageSurfaceLifecycle lifecycle in _pageHost.CachedPages.OfType<IPageSurfaceLifecycle>())
            {
                lifecycle.ResetSurfacesForTopLevelClose();
            }
            _pageHost.ReleaseFocusFromActivePage();
        }
        _searchController?.CancelForPanelClose();
        CaptureAndFlushPersistentUiState();
        RuntimeLog.Detail("ui1AppShellTopLevelCleanup=true;searchCancellationRequested=true;mainThreadWait=false");
    }

    private void CaptureAndFlushPersistentUiState()
    {
        if (_searchPersistence is null)
            return;
        _searchPersistence.CapturePreferences(_languageCode, Size, _predictionSettings);
        _searchPersistence.FlushAll();
    }

    private void ApplyPersistedGeometry()
    {
        if (!IsInsideTree() || _searchPersistence is null)
            return;
        float savedWidth = _searchPersistence.Preferences.PanelWidth;
        float savedHeight = _searchPersistence.Preferences.PanelHeight;
        if (savedWidth <= 0f || savedHeight <= 0f)
            return;
        Vector2 center = Position + Size * 0.5f;
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 maximumLogical = viewport * 0.92f;
        Vector2 minimum = new(
            Math.Min(Ui1Metrics.MinimumWidth, maximumLogical.X),
            Math.Min(Ui1Metrics.MinimumHeight, maximumLogical.Y));
        CustomMinimumSize = minimum;
        Size = new Vector2(
            Math.Clamp(savedWidth, minimum.X, Math.Max(minimum.X, maximumLogical.X)),
            Math.Clamp(savedHeight, minimum.Y, Math.Max(minimum.Y, maximumLogical.Y)));
        Position = center - Size * 0.5f;
        ClampToViewport();
        RuntimeLog.Detail($"uiPanelSizeRestored=true;width={Size.X:0.0};height={Size.Y:0.0};savedWidth={savedWidth:0.0};savedHeight={savedHeight:0.0}");
    }

    private void HandleDragInput(InputEvent input)
    {
        if (input is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }
        if (mouse.Pressed)
        {
            _dragging = true;
            _pointerStart = GetViewport().GetMousePosition();
            _positionStart = Position;
        }
        else
        {
            _dragging = false;
        }
    }

    private void HandleResizeInput(InputEvent input)
    {
        if (input is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }
        if (mouse.Pressed)
        {
            _resizing = true;
            _pointerStart = GetViewport().GetMousePosition();
            _sizeStart = Size;
        }
        else
        {
            _resizing = false;
        }
    }

    private void OnShellResized()
    {
        bool compact = Size.X < Ui1Metrics.CompactThreshold;
        _navigation?.SetCompact(compact);
        if (_pageHost is not null)
        {
            foreach (IAppPage page in _pageHost.CachedPages)
            {
                if (page is IResponsiveAppPage responsive)
                {
                    responsive.SetCompact(compact);
                }
            }
        }
        SyncSettingsPage();
    }

    private void CenterWindow()
    {
        if (!IsInsideTree())
        {
            return;
        }
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Position = (viewport - Size) * 0.5f;
        ClampToViewport();
        SyncSettingsPage();
    }

    private void SyncSettingsPage()
    {
        _settingsPage?.Sync(Position, Size, _uiText);
    }

    private void ApplyDefaultGeometry(Vector2? requestedPhysicalCenter = null)
    {
        if (!IsInsideTree())
        {
            return;
        }
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 physicalCenter = requestedPhysicalCenter ?? viewport * 0.5f;
        Vector2 maximumLogical = viewport * 0.92f;
        float width = Math.Min(Ui1Metrics.DefaultWidth, maximumLogical.X);
        float height = Math.Min(Ui1Metrics.DefaultHeight, maximumLogical.Y);
        Size = new Vector2(Math.Max(760f, width), Math.Max(520f, height));
        CustomMinimumSize = new Vector2(
            Math.Min(Ui1Metrics.MinimumWidth, maximumLogical.X),
            Math.Min(Ui1Metrics.MinimumHeight, maximumLogical.Y));
        Position = physicalCenter - Size * 0.5f;
        ClampToViewport();
    }

    private void SetLogicalSize(Vector2 requested)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 maximum = viewport - Position;
        Vector2 minimum = CustomMinimumSize;
        Size = new Vector2(
            Math.Clamp(requested.X, minimum.X, Math.Max(minimum.X, maximum.X)),
            Math.Clamp(requested.Y, minimum.Y, Math.Max(minimum.Y, maximum.Y)));
        ClampToViewport();
    }

    private void ClampToViewport()
    {
        if (!IsInsideTree())
        {
            return;
        }
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 physicalSize = Size;
        float maxX = Math.Max(0f, viewport.X - physicalSize.X);
        float maxY = Math.Max(0f, viewport.Y - physicalSize.Y);
        Position = new Vector2(Math.Clamp(Position.X, 0f, maxX), Math.Clamp(Position.Y, 0f, maxY));
        SyncSettingsPage();
    }
}
