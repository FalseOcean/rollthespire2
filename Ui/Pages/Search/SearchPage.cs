using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Pages.Search.Ancient;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class SearchPage : MarginContainer, IAppPage, IResponsiveAppPage, IPageSurfaceLifecycle
{
    private const long UnlimitedScanCount = long.MaxValue;
    // Runtime rollback only. Phase A2 uses the page-local Control transaction
    // surface by default, but the previously accepted Window suspend/resume
    // workaround remains intact until the replacement path is Owner runtime-proven.
    private static readonly bool UseLegacyPresetWindowFallback = false;
    private readonly RuntimePredictionSettings _settings;
    private ModRuntimeSnapshot _runtime = ModRuntimeSnapshot.NotInitialized;
    private readonly SearchContextBar _contextBar;
    private readonly SearchPresetSaveTransactionOverlay _presetSaveSurface;
    private readonly SearchConfirmationTransactionOverlay _presetOverwriteSurface;
    private readonly SearchConfirmationTransactionOverlay _presetDeleteSurface;
    private readonly ConfirmationDialog _presetSaveDialog;
    private readonly LineEdit _presetNameInput;
    private readonly Label _presetVisualMarkLabel;
    private readonly RelicSequencePickerSlot _presetVisualIconSlot;
    private readonly RelicPickerPanel _presetVisualIconPicker;
    private readonly ConfirmationDialog _presetOverwriteDialog;
    private readonly ConfirmationDialog _presetDeleteDialog;
    private readonly SearchPresetLibraryOverlay _presetLibrary;
    private IReadOnlyList<SearchPresetDefinition> _knownPresets = Array.Empty<SearchPresetDefinition>();
    private IReadOnlyList<ModelKey> _presetVisualRelicCandidates = Array.Empty<ModelKey>();
    private string _pendingPresetDeleteId = string.Empty;
    private string _pendingPresetSaveName = string.Empty; // legacy Window fallback only
    private ModelKey? _pendingPresetSaveIcon; // legacy Window fallback only
    private SearchPresetSaveCommit? _pendingPresetSaveCommit;
    private SearchPresetSaveIntentKind _activePresetSaveKind = SearchPresetSaveIntentKind.CreateCurrentQuery;
    private string _activePresetSaveSourceId = string.Empty;
    private int _activePresetVisualIconSlot;
    private bool _resumePresetSaveDialogAfterVisualPicker;
    private bool _suppressPresetChildReturn;
    private readonly SearchCategoryNavigationBar _categoryNavigation;
    private readonly SearchCategoryHost _categoryHost;
    private readonly Label _pageTitle;
    private readonly Button _savePresetAction;
    private readonly Button _loadPresetAction;
    private readonly WarningCallout _runtimeCompatibilityWarning;
    private readonly WarningCallout _searchAuthorityWarning;
    private readonly Label _filtersTitle;
    private readonly LineEdit _startSeed;
    private readonly SpinBox _scanCount;
    private readonly SpinBox _targetCount;
    private readonly Label _startSeedLabel;
    private readonly Label _scanCountLabel;
    private readonly LineEdit _neowAny;
    private readonly LineEdit _neowAll;
    private readonly LineEdit _neowBan;
    private readonly Label _neowAnyLabel;
    private readonly Label _neowAllLabel;
    private readonly Label _neowBanLabel;
    private readonly CheckBox _requireBones;
    private readonly LineEdit _bonesAny;
    private readonly LineEdit _bonesAll;
    private readonly LineEdit _bonesBan;
    private readonly Label _bonesAnyLabel;
    private readonly Label _bonesAllLabel;
    private readonly Label _bonesBanLabel;
    private readonly CheckBox _smallCapsule;
    private readonly CheckBox _largeCapsule;
    private readonly LineEdit _capsuleAny;
    private readonly LineEdit _capsuleAll;
    private readonly LineEdit _capsuleBan;
    private readonly Label _capsuleAnyLabel;
    private readonly Label _capsuleAllLabel;
    private readonly Label _capsuleBanLabel;
    private readonly CheckBox _whetstone;
    private readonly CheckBox _warPaint;
    private readonly LineEdit _finalCurse;
    private readonly LineEdit _banCurses;
    private readonly Label _finalCurseLabel;
    private readonly Label _banCursesLabel;
    private readonly CheckBox _validationPreset;
    private readonly SpinBox _bossAct;
    private readonly LineEdit _bossAny;
    private readonly LineEdit _bossBan;
    private readonly Label _bossActLabel;
    private readonly Label _bossAnyLabel;
    private readonly Label _bossBanLabel;
    private readonly SpinBox _ancientAct;
    private readonly LineEdit _ancientAny;
    private readonly LineEdit _ancientBan;
    private readonly LineEdit _ancientOptionAny;
    private readonly LineEdit _ancientOptionBan;
    private readonly Label _ancientActLabel;
    private readonly Label _ancientAnyLabel;
    private readonly Label _ancientBanLabel;
    private readonly Label _ancientOptionAnyLabel;
    private readonly Label _ancientOptionBanLabel;
    private readonly Button _advancedSearchToggle;
    private readonly VBoxContainer _advancedSearchBody;
    private readonly Label _advancedSearchHint;
    private readonly Button _compatibilityToggle;
    private readonly VBoxContainer _compatibilityBody;
    private readonly Label _compatibilityHint;
    private readonly SearchIntegratedFilterEditor _integratedFilters;
    private readonly PanelContainer _controlPane;
    private readonly Label _controlTitle;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Label _targetCountTitle;
    private readonly Label _gpuBackendTitle;
    private readonly OptionButton _gpuBackendSelect;
    private readonly Button _probabilityToggle;
    private readonly VBoxContainer _probabilityDetailsBody;
    private readonly Label _probabilityValue;
    private readonly VBoxContainer _probabilityRows;
    private readonly Button _etaToggle;
    private readonly VBoxContainer _etaDetailsBody;
    private readonly Label _etaValue;
    private readonly VBoxContainer _etaRows;
    private readonly Button _progressToggle;
    private readonly VBoxContainer _progressDetailsBody;
    private readonly Label _scannedValue;
    private readonly Label _speedTitle;
    private readonly Label _speedValue;
    private readonly Label _elapsedTitle;
    private readonly Label _elapsedValue;
    private readonly Label _issue;
    private readonly Label _recentTitle;
    private readonly Label _resultsStaleNotice;
    private readonly VBoxContainer _results;
    private readonly Label _emptyResults;
    private readonly Button _viewAll;
    private IUiTextProvider? _uiText;
    private IGameContentNameResolver? _contentNames;
    private bool _compact;
    private bool _suppressQueryDraftChanged;
    private SearchProbabilityQuickView _probabilityView = SearchProbabilityQuickView.Unavailable;
    private SearchEtaQuickView _etaView = SearchEtaQuickView.Unavailable(30);
    private SearchEtaQuickView _predictedEtaView = SearchEtaQuickView.Unavailable(30);
    private double _lastLiveEtaSeconds = -1;
    private readonly List<PersistedSearchResult> _persistedResultDtos = new();

    public SearchPage(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        IReadOnlyList<ModelKey> characterKeys,
        ICardPickerFilterIconProvider cardPickerFilterIcons,
        AnchoredTooltipHost tooltipHost,
        RuntimePredictionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        PageKey = AppPageKey.Search;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var pageLayout = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        pageLayout.AddThemeConstantOverride("separation", 14);

        var center = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        center.AddThemeConstantOverride("separation", 12);

        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _pageTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _savePresetAction = new Button { CustomMinimumSize = new Vector2(126, 38) };
        _loadPresetAction = new Button { CustomMinimumSize = new Vector2(112, 38) };
        Ui1Theme.ApplyButton(_savePresetAction, Ui1ButtonRole.Secondary);
        Ui1Theme.ApplyButton(_loadPresetAction, Ui1ButtonRole.Secondary);
        _savePresetAction.Pressed += () => ShowSavePresetDialog(_knownPresets);
        _loadPresetAction.Pressed += () => PresetWorkspaceRequested?.Invoke();
        var titleRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        titleRow.AddThemeConstantOverride("separation", 10);
        titleRow.AddChild(_pageTitle);
        titleRow.AddChild(_savePresetAction);
        titleRow.AddChild(_loadPresetAction);

        _runtimeCompatibilityWarning = new WarningCallout();
        _searchAuthorityWarning = new WarningCallout();
        _contextBar = new SearchContextBar(characterPoolIcons, characterKeys);
        _presetLibrary = new SearchPresetLibraryOverlay(icons, tooltipHost);

        _presetSaveSurface = new SearchPresetSaveTransactionOverlay(icons, tooltipHost);
        _presetSaveSurface.VisualIconRequested += OpenPresetVisualIconPicker;
        _presetSaveSurface.SaveRequested += ConfirmPresetSave;
        _presetSaveSurface.Cancelled += () =>
        {
            if (!_suppressPresetChildReturn && _presetLibrary.IsOpen)
                _presetLibrary.GrabFocus();
        };

        _presetOverwriteSurface = new SearchConfirmationTransactionOverlay();
        _presetOverwriteSurface.Confirmed += () =>
        {
            if (_pendingPresetSaveCommit is not null)
                PresetSaveConfirmed?.Invoke(_pendingPresetSaveCommit);
            _pendingPresetSaveCommit = null;
            _presetSaveSurface.CloseCommitted();
        };
        _presetOverwriteSurface.Cancelled += () =>
        {
            _pendingPresetSaveCommit = null;
            if (!_suppressPresetChildReturn && _presetSaveSurface.IsOpen)
                _presetSaveSurface.FocusAfterChildPicker(_activePresetVisualIconSlot);
        };

        _presetDeleteSurface = new SearchConfirmationTransactionOverlay();
        _presetDeleteSurface.Confirmed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_pendingPresetDeleteId))
                PresetDeleteRequested?.Invoke(_pendingPresetDeleteId);
            _pendingPresetDeleteId = string.Empty;
        };
        _presetDeleteSurface.Cancelled += () =>
        {
            _pendingPresetDeleteId = string.Empty;
            if (!_suppressPresetChildReturn && _presetLibrary.IsOpen)
                _presetLibrary.GrabFocus();
        };

        // Legacy Window fallback retained for rollback until the Control-only
        // transaction -> picker path has Owner runtime evidence.
        _presetSaveDialog = new ConfirmationDialog();
        _presetNameInput = new LineEdit { CustomMinimumSize = new Vector2(320, 36) };
        _presetVisualMarkLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _presetVisualIconSlot = new RelicSequencePickerSlot(icons, tooltipHost);
        _presetVisualIconSlot.Pressed += () => OpenPresetVisualIconPicker(0);
        var savePresetBody = new VBoxContainer();
        savePresetBody.AddThemeConstantOverride("separation", 8);
        savePresetBody.AddChild(_presetNameInput);
        savePresetBody.AddChild(_presetVisualMarkLabel);
        savePresetBody.AddChild(_presetVisualIconSlot);
        _presetSaveDialog.AddChild(savePresetBody);
        _presetSaveDialog.Confirmed += ConfirmPresetSave;
        _presetNameInput.TextChanged += text => _presetSaveDialog.GetOkButton().Disabled = string.IsNullOrWhiteSpace(text);

        _presetOverwriteDialog = new ConfirmationDialog();
        _presetOverwriteDialog.Confirmed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_pendingPresetSaveName))
            {
                var icons = _pendingPresetSaveIcon is { IsValid: true } icon
                    ? new[] { SearchPresetVisualIconRef.FromRelic(icon) }
                    : Array.Empty<SearchPresetVisualIconRef>();
                PresetSaveConfirmed?.Invoke(new SearchPresetSaveCommit(
                    SearchPresetSaveIntentKind.CreateCurrentQuery,
                    string.Empty,
                    new SearchPresetMetadataDraft(_pendingPresetSaveName, string.Empty, icons)));
            }
            _pendingPresetSaveName = string.Empty;
            _pendingPresetSaveIcon = null;
        };

        _presetDeleteDialog = new ConfirmationDialog();
        _presetDeleteDialog.Confirmed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_pendingPresetDeleteId))
                PresetDeleteRequested?.Invoke(_pendingPresetDeleteId);
            _pendingPresetDeleteId = string.Empty;
        };

        _presetLibrary.SaveAsUserRequested += id =>
            OpenPresetSaveTransaction(SearchPresetSaveIntentKind.CreateFromExistingSnapshot, id);
        _presetLibrary.EditMetadataRequested += id =>
            OpenPresetSaveTransaction(SearchPresetSaveIntentKind.EditMetadata, id);
        _presetLibrary.LoadRequested += id =>
        {
            PresetLoadConfirmed?.Invoke(id);
            _presetLibrary.Cancel();
        };
        _presetLibrary.DeleteRequested += id =>
        {
            _pendingPresetDeleteId = id;
            if (UseLegacyPresetWindowFallback)
            {
                _presetDeleteDialog.PopupCentered(new Vector2I(420, 150));
            }
            else if (_uiText is not null)
            {
                _presetDeleteSurface.Open(
                    _uiText.Get(Ui1TextKey.SearchPresetDeleteTitle),
                    _uiText.Get(Ui1TextKey.SearchPresetDeletePrompt),
                    _uiText.Get(Ui1TextKey.SearchPresetDelete),
                    _uiText.Get(Ui1TextKey.SearchPresetCancel));
            }
        };

        _presetVisualIconPicker = new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);
        _presetVisualIconPicker.Closed += HandlePresetVisualPickerClosed;

        _categoryNavigation = new SearchCategoryNavigationBar(new SearchCategoryTabIconProvider(icons));
        _categoryHost = new SearchCategoryHost(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);
        _categoryNavigation.CategoryChanged += category => _categoryHost.Select(category);
        _categoryHost.NeowPage.Changed += OnQueryDraftChanged;
        _categoryHost.AncientPage.Changed += OnQueryDraftChanged;
        _categoryHost.BossMapPage.Changed += OnQueryDraftChanged;
        _categoryHost.RelicPage.Changed += OnQueryDraftChanged;
        _categoryHost.CombatRewardPage.Changed += OnQueryDraftChanged;
        _categoryHost.EventPage.Changed += OnQueryDraftChanged;
        _categoryHost.ShopPage.Changed += OnQueryDraftChanged;
        _contextBar.DraftChanged += () => ContextDraftChanged?.Invoke();
        _categoryNavigation.ClearConditionsRequested += ClearAllFilters;

        center.AddChild(titleRow);
        center.AddChild(_runtimeCompatibilityWarning);
        center.AddChild(_searchAuthorityWarning);
        center.AddChild(_contextBar);
        center.AddChild(_categoryNavigation);
        center.AddChild(_categoryHost);

        var legacyDraftStorage = new VBoxContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        var filtersPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(filtersPanel, Ui1SurfaceRole.Card, 4f, 1, 12f);
        var filtersColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        filtersColumn.AddThemeConstantOverride("separation", 9);
        _filtersTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        filtersColumn.AddChild(_filtersTitle);

        _requireBones = new CheckBox();
        filtersColumn.AddChild(_requireBones);

        _smallCapsule = new CheckBox();
        _largeCapsule = new CheckBox();
        _whetstone = new CheckBox();
        _warPaint = new CheckBox();
        var capsuleChecks = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        capsuleChecks.AddThemeConstantOverride("h_separation", 16);
        capsuleChecks.AddThemeConstantOverride("v_separation", 6);
        capsuleChecks.AddChild(_smallCapsule);
        capsuleChecks.AddChild(_largeCapsule);
        capsuleChecks.AddChild(_whetstone);
        capsuleChecks.AddChild(_warPaint);
        filtersColumn.AddChild(capsuleChecks);

        _validationPreset = new CheckBox();
        filtersColumn.AddChild(_validationPreset);
        filtersPanel.AddChild(filtersColumn);
        legacyDraftStorage.AddChild(filtersPanel);

        _startSeed = TextField("000000000000", 230);
        _scanCount = NumberField(1, 1000000000, 200000, 1, 180);
        _startSeedLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _scanCountLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _advancedSearchHint = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _advancedSearchBody = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _advancedSearchBody.AddThemeConstantOverride("separation", 8);
        var rangeRow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rangeRow.AddThemeConstantOverride("h_separation", 10);
        rangeRow.AddThemeConstantOverride("v_separation", 8);
        rangeRow.AddChild(Labeled(_startSeedLabel, _startSeed));
        rangeRow.AddChild(Labeled(_scanCountLabel, _scanCount));
        _advancedSearchBody.AddChild(_advancedSearchHint);
        _advancedSearchBody.AddChild(rangeRow);
        _advancedSearchToggle = CreateFoldout(_advancedSearchBody, UpdateFoldoutText);
        legacyDraftStorage.AddChild(CreateFoldoutPanel(_advancedSearchToggle, _advancedSearchBody));

        _neowAny = TextField(string.Empty, 230);
        _neowAll = TextField(string.Empty, 230);
        _neowBan = TextField(string.Empty, 230);
        _neowAnyLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _neowAllLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _neowBanLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bonesAny = TextField(string.Empty, 230);
        _bonesAll = TextField(string.Empty, 230);
        _bonesBan = TextField(string.Empty, 230);
        _bonesAnyLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bonesAllLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bonesBanLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _capsuleAny = TextField(string.Empty, 230);
        _capsuleAll = TextField(string.Empty, 230);
        _capsuleBan = TextField(string.Empty, 230);
        _capsuleAnyLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _capsuleAllLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _capsuleBanLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _finalCurse = TextField(string.Empty, 250);
        _banCurses = TextField(string.Empty, 250);
        _finalCurseLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _banCursesLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bossAct = NumberField(1, 3, 1, 1, 100);
        _bossAny = TextField(string.Empty, 260);
        _bossBan = TextField(string.Empty, 260);
        _bossActLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bossAnyLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bossBanLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ancientAct = NumberField(1, 3, 1, 1, 100);
        _ancientAny = TextField(string.Empty, 260);
        _ancientBan = TextField(string.Empty, 260);
        _ancientOptionAny = TextField(string.Empty, 300);
        _ancientOptionBan = TextField(string.Empty, 300);
        _ancientActLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ancientAnyLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ancientBanLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ancientOptionAnyLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ancientOptionBanLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _compatibilityHint = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _compatibilityBody = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _compatibilityBody.AddThemeConstantOverride("separation", 9);
        _compatibilityBody.AddChild(_compatibilityHint);
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_neowAnyLabel, _neowAny),
            Labeled(_neowAllLabel, _neowAll),
            Labeled(_neowBanLabel, _neowBan)));
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_bonesAnyLabel, _bonesAny),
            Labeled(_bonesAllLabel, _bonesAll),
            Labeled(_bonesBanLabel, _bonesBan)));
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_capsuleAnyLabel, _capsuleAny),
            Labeled(_capsuleAllLabel, _capsuleAll),
            Labeled(_capsuleBanLabel, _capsuleBan)));
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_finalCurseLabel, _finalCurse),
            Labeled(_banCursesLabel, _banCurses)));
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_bossActLabel, _bossAct),
            Labeled(_bossAnyLabel, _bossAny),
            Labeled(_bossBanLabel, _bossBan)));
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_ancientActLabel, _ancientAct),
            Labeled(_ancientAnyLabel, _ancientAny),
            Labeled(_ancientBanLabel, _ancientBan)));
        _compatibilityBody.AddChild(FilterFlow(
            Labeled(_ancientOptionAnyLabel, _ancientOptionAny),
            Labeled(_ancientOptionBanLabel, _ancientOptionBan)));
        _compatibilityToggle = CreateFoldout(_compatibilityBody, UpdateFoldoutText);
        legacyDraftStorage.AddChild(CreateFoldoutPanel(_compatibilityToggle, _compatibilityBody));

        _integratedFilters = new SearchIntegratedFilterEditor();
        _integratedFilters.Changed += OnQueryDraftChanged;
        _integratedFilters.ClearRequested += ClearAllFilters;
        legacyDraftStorage.AddChild(_integratedFilters);
        center.AddChild(legacyDraftStorage);
        pageLayout.AddChild(center);

        _controlPane = new PanelContainer
        {
            CustomMinimumSize = new Vector2(326, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(_controlPane, Ui1SurfaceRole.CardElevated, 4f, 1, 12f);
        var controls = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        controls.AddThemeConstantOverride("separation", 9);
        _controlTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        controls.AddChild(_controlTitle);

        var actions = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("separation", 8);
        _start = new Button
        {
            CustomMinimumSize = new Vector2(0, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        _stop = new Button
        {
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyButton(_start, Ui1ButtonRole.Primary);
        Ui1Theme.ApplyButton(_stop, Ui1ButtonRole.Secondary);
        actions.AddChild(_start);
        actions.AddChild(_stop);
        controls.AddChild(actions);

        _targetCountTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _targetCount = NumberField(1, 1000, 30, 1, 0);
        _targetCount.CustomMinimumSize = new Vector2(0f, 38f);
        _targetCount.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Release UI exposes only Auto / CPU Only. Legacy/developer preferences are
        // normalized before binding so the visible selection and runtime preference
        // can never diverge.
        _settings.NormalizeWorkshopSearchModePreference();
        _gpuBackendTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _gpuBackendSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(0, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyOptionButton(_gpuBackendSelect);
        _gpuBackendSelect.AddItem(string.Empty);
        _gpuBackendSelect.AddItem(string.Empty);
        _gpuBackendSelect.Selected = _settings.RelicComputeBackendPreference == Beta110RelicComputeBackendPreference.Cpu ? 1 : 0;
        _gpuBackendSelect.ItemSelected += index =>
        {
            _settings.RelicComputeBackendPreference = index == 1
                ? Beta110RelicComputeBackendPreference.Cpu
                : Beta110RelicComputeBackendPreference.Auto;
            QueryDraftChanged?.Invoke();
        };
        Control searchModeColumn = Labeled(_gpuBackendTitle, _gpuBackendSelect);
        searchModeColumn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        searchModeColumn.SizeFlagsStretchRatio = 1.65f;
        Control targetCountColumn = Labeled(_targetCountTitle, _targetCount);
        targetCountColumn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        targetCountColumn.SizeFlagsStretchRatio = 1f;
        var modeAndTargetRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        modeAndTargetRow.AddThemeConstantOverride("separation", 10);
        modeAndTargetRow.AddChild(searchModeColumn);
        modeAndTargetRow.AddChild(targetCountColumn);
        controls.AddChild(modeAndTargetRow);

        _probabilityValue = SummaryValue();
        _probabilityRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _probabilityRows.AddThemeConstantOverride("separation", 4);
        _probabilityDetailsBody = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _probabilityDetailsBody.AddChild(_probabilityRows);
        _probabilityToggle = CreateSummaryFoldout(_probabilityDetailsBody, UpdateProbabilityFoldoutText);
        controls.AddChild(CreateSummaryFoldoutPanel(
            _probabilityToggle,
            _probabilityValue,
            _probabilityDetailsBody));

        _etaValue = SummaryValue();
        _etaRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _etaRows.AddThemeConstantOverride("separation", 4);
        _etaDetailsBody = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _etaDetailsBody.AddChild(_etaRows);
        _etaToggle = CreateSummaryFoldout(_etaDetailsBody, UpdateEtaFoldoutText);
        controls.AddChild(CreateSummaryFoldoutPanel(
            _etaToggle,
            _etaValue,
            _etaDetailsBody));

        _scannedValue = SummaryValue();
        _speedTitle = MetricTitle();
        _speedValue = DetailValue();
        _elapsedTitle = MetricTitle();
        _elapsedValue = DetailValue();
        _progressDetailsBody = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _progressDetailsBody.AddThemeConstantOverride("separation", 4);
        _progressDetailsBody.AddChild(CreateDetailRow(_speedTitle, _speedValue));
        _progressDetailsBody.AddChild(CreateDetailRow(_elapsedTitle, _elapsedValue));
        _progressToggle = CreateSummaryFoldout(_progressDetailsBody, UpdateProgressFoldoutText);
        controls.AddChild(CreateSummaryFoldoutPanel(
            _progressToggle,
            _scannedValue,
            _progressDetailsBody));

        _issue = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _issue.Visible = false;
        controls.AddChild(_issue);

        var separator = new HSeparator();
        Ui1Theme.ApplySeparator(separator);
        controls.AddChild(separator);
        _recentTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        controls.AddChild(_recentTitle);
        _resultsStaleNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _resultsStaleNotice.Visible = false;
        controls.AddChild(_resultsStaleNotice);

        var resultScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _results = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _results.AddThemeConstantOverride("separation", 6);
        _emptyResults = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _results.AddChild(_emptyResults);
        resultScroll.AddChild(_results);
        controls.AddChild(resultScroll);

        _viewAll = new Button { Disabled = true, Visible = false, CustomMinimumSize = new Vector2(0, 38) };
        Ui1Theme.ApplyButton(_viewAll, Ui1ButtonRole.Ghost);
        controls.AddChild(_viewAll);
        _controlPane.AddChild(controls);
        pageLayout.AddChild(_controlPane);
        AddChild(pageLayout);

        // Godot CanvasItem.ZIndex controls draw order only; it does not control
        // Control GUI input order. Keep page content physically before every
        // page-local surface so pointer hit-testing follows the same bottom ->
        // workspace -> transaction -> picker -> confirmation order as rendering.
        AddChild(_presetSaveDialog);
        AddChild(_presetOverwriteDialog);
        AddChild(_presetDeleteDialog);
        AddChild(_presetLibrary);
        _presetLibrary.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_presetSaveSurface);
        _presetSaveSurface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_presetVisualIconPicker);
        _presetVisualIconPicker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_presetOverwriteSurface);
        _presetOverwriteSurface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_presetDeleteSurface);
        _presetDeleteSurface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _start.Pressed += OnStartPressed;
        _stop.Pressed += () => StopRequested?.Invoke();
        _targetCount.ValueChanged += _ =>
        {
            if (!_start.Disabled)
            {
                UpdateResultsHeader(_persistedResultDtos.Count, (int)_targetCount.Value);
                _etaView = SearchEtaQuickView.Unavailable((int)_targetCount.Value);
                BindEtaQuickView(_etaView);
                QueryDraftChanged?.Invoke();
            }
        };
        foreach (LineEdit field in new[]
        {
            _neowAny, _neowAll, _neowBan, _bonesAny, _bonesAll, _bonesBan,
            _capsuleAny, _capsuleAll, _capsuleBan, _finalCurse, _banCurses, _bossAny, _bossBan,
            _ancientAny, _ancientBan, _ancientOptionAny, _ancientOptionBan
        })
        {
            field.TextChanged += _ => OnQueryDraftChanged();
        }
        foreach (CheckBox check in new[]
        {
            _requireBones, _smallCapsule, _largeCapsule, _whetstone, _warPaint, _validationPreset
        })
        {
            check.Toggled += _ => OnQueryDraftChanged();
        }
        UpdateConditionSummary();
        SetProcess(true);
        ResetMetrics();
    }

    public event Action<SearchDraft, SearchRunDraft>? StartRequested;
    public event Action? ContextDraftChanged;
    public event Action? QueryDraftChanged;
    public event Action? StopRequested;
    public event Action? PollRequested;
    public event Action<SearchCandidate>? CandidateOpenRequested;
    public event Action<PersistedSearchResult>? PersistedCandidateOpenRequested;
    public event Action<string>? CandidateCopyRequested;
    public event Action? PresetWorkspaceRequested;
    public event Action<SearchPresetSaveCommit>? PresetSaveConfirmed;
    public event Action<string>? PresetLoadConfirmed;
    public event Action<string>? PresetDeleteRequested;

    public AppPageKey PageKey { get; }
    public Control View => this;
    public ModelKey CharacterKey => _contextBar.CharacterKey;
    public int Ascension => _contextBar.Ascension;
    public int CurrentEnabledConditionCount => ResolveEnabledConditionCount();

    public SearchDraft CurrentDraft
    {
        get
        {
            IntegratedSearchDraft integrated = _integratedFilters.CurrentDraft;
            _categoryHost.NeowPage.TryBuildDraft(out NeowRouteFilterDraft routeDraft, out _, focusInvalid: false);
            AncientOptionConditionProfile optionConditions = _categoryHost.AncientPage.OptionConditions;
            return new SearchDraft(
                _neowAny.Text,
                _neowAll.Text,
                _neowBan.Text,
                _requireBones.ButtonPressed,
                _bonesAny.Text,
                _bonesAll.Text,
                _bonesBan.Text,
                _smallCapsule.ButtonPressed,
                _largeCapsule.ButtonPressed,
                _capsuleAny.Text,
                _whetstone.ButtonPressed,
                _warPaint.ButtonPressed,
                _finalCurse.Text,
                _banCurses.Text,
                _validationPreset.ButtonPressed)
            {
                NeowRouteDraft = routeDraft,
                AncientMatrixDraft = _categoryHost.AncientPage.BuildDraft(),
                BossMapDraft = _categoryHost.BossMapPage.BuildDraft(),
                CombatRewardDraft = _categoryHost.CombatRewardPage.BuildDraft(),
                CapsuleRelicAll = _capsuleAll.Text,
                CapsuleRelicBan = _capsuleBan.Text,
                BonesAcquisitionOrder = integrated.BonesAcquisitionOrder,
                EffectOutputSource = integrated.EffectOutputSource,
                EffectOutputAny = integrated.EffectOutputAny,
                EffectOutputAll = integrated.EffectOutputAll,
                EffectOutputBan = integrated.EffectOutputBan,
                BossAct = (int)_bossAct.Value,
                BossAny = _bossAny.Text,
                BossAll = integrated.BossAll,
                BossBan = _bossBan.Text,
                BossOrdinal = integrated.BossOrdinal,
                BossOrdinalAny = integrated.BossOrdinalAny,
                BossOrdinalAll = integrated.BossOrdinalAll,
                BossOrdinalBan = integrated.BossOrdinalBan,
                AncientAct = (int)_ancientAct.Value,
                AncientAny = _ancientAny.Text,
                AncientAll = integrated.AncientAll,
                AncientBan = _ancientBan.Text,
                AncientOptionAny = _ancientOptionAny.Text,
                AncientOptionAll = integrated.AncientOptionAll,
                AncientOptionBan = _ancientOptionBan.Text,
                SeaGlassTargetAny = integrated.SeaGlassTargetAny,
                SeaGlassTargetAll = integrated.SeaGlassTargetAll,
                SeaGlassTargetBan = integrated.SeaGlassTargetBan,
                RelicShopSequenceDraft = _categoryHost.ShopPage.BuildRelicSearchConditions(),
                EventSequenceDraft = _categoryHost.EventPage.BuildSearchConditions(),
                EventResultDraft = _categoryHost.EventPage.BuildEventResultConditions(),
                MerchantColorlessSequenceDraft = _categoryHost.ShopPage.BuildSearchConditions(),
                MerchantColorlessDraft = _categoryHost.ShopPage.BuildLegacyColorlessConditions(),
                RelicSequenceDraft = _categoryHost.RelicPage.BuildSearchConditions()
                    .Concat(_categoryHost.ShopPage.BuildLegacyRelicConditions())
                    .ToArray(),
                RelicSequenceConditions = _categoryHost.RelicPage.SerializedConditions,
                EventSequenceConditions = _categoryHost.EventPage.SerializedConditions,
                TezcataraHasBasicStrike = optionConditions.TezcataraHasBasicStrike,
                NonupeipeSwiftEnchantableAtLeast4 = optionConditions.NonupeipeSwiftEnchantableAtLeast4,
                TanxInstinctEnchantableAtLeast3 = optionConditions.TanxInstinctEnchantableAtLeast3,
                PaelGoopyDefendCardsAtLeast3 = optionConditions.PaelGoopyDefendCardsAtLeast3,
                PaelAllowLegionNoEventPet = optionConditions.PaelAllowLegionNoEventPet,
                PaelRemovableCardsAtLeast5 = optionConditions.PaelRemovableCardsAtLeast5,
                OrobasArchaicToothConditionMet = optionConditions.OrobasArchaicToothConditionMet,
                OrobasTouchOfOrobasConditionMet = optionConditions.OrobasTouchOfOrobasConditionMet,
                DarvAllowPandorasBoxRelicSet = optionConditions.DarvAllowPandorasBoxRelicSet
            };
        }
    }

    public SearchRunDraft CurrentRunDraft => new(
        _startSeed.Text,
        UnlimitedScanCount,
        (int)_targetCount.Value,
        _settings.SearchWorkerCount);

    public void RestoreContext(ModelKey characterKey, int ascension, bool notify = false)
    {
        _contextBar.RestoreDraft(characterKey, ascension, notify);
    }

    public void RestoreWorkspace(ModelKey characterKey, int ascension, SearchDraft? draft, SearchRunDraft? runDraft, bool notify = false)
    {
        if (draft is null) return;
        _suppressQueryDraftChanged = true;
        try
        {
            _contextBar.RestoreDraft(characterKey, ascension, notify: false);
            if (runDraft is not null)
            {
                _targetCount.Value = Math.Clamp(runDraft.TargetMatchCount, 1, (int)_targetCount.MaxValue);
                _startSeed.Text = runDraft.StartSeed ?? string.Empty;
            }
            _neowAny.Text = draft.NeowAny ?? string.Empty;
            _neowAll.Text = draft.NeowAll ?? string.Empty;
            _neowBan.Text = draft.NeowBan ?? string.Empty;
            _requireBones.ButtonPressed = draft.RequireBones;
            _bonesAny.Text = draft.BonesAny ?? string.Empty;
            _bonesAll.Text = draft.BonesAll ?? string.Empty;
            _bonesBan.Text = draft.BonesBan ?? string.Empty;
            _smallCapsule.ButtonPressed = draft.RequireSmallCapsule;
            _largeCapsule.ButtonPressed = draft.RequireLargeCapsule;
            _capsuleAny.Text = draft.CapsuleRelicAny ?? string.Empty;
            _capsuleAll.Text = draft.CapsuleRelicAll ?? string.Empty;
            _capsuleBan.Text = draft.CapsuleRelicBan ?? string.Empty;
            _whetstone.ButtonPressed = draft.RequireWhetstone;
            _warPaint.ButtonPressed = draft.RequireWarPaint;
            _finalCurse.Text = draft.RequiredFinalCurse ?? string.Empty;
            _banCurses.Text = draft.BannedFinalCurses ?? string.Empty;
            _validationPreset.ButtonPressed = draft.ValidationPreset;
            _bossAct.Value = Math.Clamp(draft.BossAct, 1, 3);
            _bossAny.Text = draft.BossAny ?? string.Empty;
            _bossBan.Text = draft.BossBan ?? string.Empty;
            _ancientAct.Value = Math.Clamp(draft.AncientAct, 1, 3);
            _ancientAny.Text = draft.AncientAny ?? string.Empty;
            _ancientBan.Text = draft.AncientBan ?? string.Empty;
            _ancientOptionAny.Text = draft.AncientOptionAny ?? string.Empty;
            _ancientOptionBan.Text = draft.AncientOptionBan ?? string.Empty;

            _integratedFilters.ApplyDraft(new IntegratedSearchDraft(
                draft.BonesAcquisitionOrder, draft.EffectOutputSource, draft.EffectOutputAny, draft.EffectOutputAll, draft.EffectOutputBan,
                draft.BossAll, draft.BossOrdinal, draft.BossOrdinalAny, draft.BossOrdinalAll, draft.BossOrdinalBan,
                draft.AncientAll, draft.AncientOptionAll, draft.SeaGlassTargetAny, draft.SeaGlassTargetAll, draft.SeaGlassTargetBan,
                draft.RelicSequenceConditions, draft.EventSequenceConditions),
                notify: false);

            var optionProfile = new AncientOptionConditionProfile(
                draft.TezcataraHasBasicStrike, draft.NonupeipeSwiftEnchantableAtLeast4, draft.TanxInstinctEnchantableAtLeast3,
                draft.PaelGoopyDefendCardsAtLeast3, draft.PaelAllowLegionNoEventPet, draft.PaelRemovableCardsAtLeast5,
                draft.OrobasArchaicToothConditionMet, draft.OrobasTouchOfOrobasConditionMet, draft.DarvAllowPandorasBoxRelicSet);
            _categoryHost.NeowPage.RestoreDraft(draft.NeowRouteDraft, notify: false);
            _categoryHost.AncientPage.RestoreDraft(draft.AncientMatrixDraft, optionProfile, notify: false);
            _categoryHost.BossMapPage.RestoreDraft(draft.BossMapDraft, notify: false);
            _categoryHost.RelicPage.RestoreDraft(draft.RelicSequenceDraft.Where(condition => condition.Lane != RelicSequenceKind.Shop).ToArray(), notify: false);
            _categoryHost.EventPage.RestoreDraft(draft.EventSequenceDraft, draft.EventResultDraft, notify: false);
            _categoryHost.ShopPage.RestoreDraft(draft.MerchantColorlessSequenceDraft, draft.RelicShopSequenceDraft, notify: false);
            if (draft.MerchantColorlessSequenceDraft.Count == 0 && draft.MerchantColorlessDraft.Count > 0)
                _categoryHost.ShopPage.RestoreLegacyColorless(draft.MerchantColorlessDraft, notify: false);
            if (draft.RelicShopSequenceDraft.Count == 0)
                _categoryHost.ShopPage.RestoreLegacyRelics(draft.RelicSequenceDraft, notify: false);
            _categoryHost.CombatRewardPage.RestoreDraft(draft.CombatRewardDraft, notify: false);
        }
        finally
        {
            _suppressQueryDraftChanged = false;
        }
        UpdateConditionSummary();
        if (notify)
        {
            ContextDraftChanged?.Invoke();
            QueryDraftChanged?.Invoke();
        }
    }

    /// <summary>
    /// Preset load is a deterministic authored-state replacement. SearchRunDraft and runtime
    /// authority are intentionally not touched. The clear phase is silent so observers only
    /// see the final replaced state.
    /// </summary>
    public void ReplaceAuthoredState(ModelKey characterKey, int ascension, SearchDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ClearAllFilters(notify: false);
        RestoreWorkspace(characterKey, ascension, draft, runDraft: null, notify: true);
    }

    public void ClearAuthoredConditions() => ClearAllFilters();

    public void ShowSavePresetDialog(IReadOnlyList<SearchPresetDefinition> presets)
    {
        CancelTransientSurfaces(preservePresetWorkspace: false, reason: "preset-save-open");
        _resumePresetSaveDialogAfterVisualPicker = false;
        _knownPresets = presets ?? Array.Empty<SearchPresetDefinition>();

        if (UseLegacyPresetWindowFallback)
        {
            _presetNameInput.Text = string.Empty;
            _presetVisualIconSlot.SetSelection(null, notify: false);
            _presetSaveDialog.GetOkButton().Disabled = true;
            _presetSaveDialog.PopupCentered(new Vector2I(460, 240));
            _presetNameInput.GrabFocus();
            return;
        }

        OpenPresetSaveTransaction(SearchPresetSaveIntentKind.CreateCurrentQuery, string.Empty);
        RuntimeLog.Detail("presetSaveSurfaceOpened=true;surface=control;intent=CreateCurrentQuery;legacyWindowFallbackAvailable=true");
    }

    public void ShowPresetLibrary(IReadOnlyList<SearchPresetDefinition> presets)
    {
        CancelTransientSurfaces(preservePresetWorkspace: false, reason: "preset-library-open");
        _knownPresets = presets ?? Array.Empty<SearchPresetDefinition>();
        _presetLibrary.Open(_knownPresets);
    }

    public void RefreshPresetLibrary(IReadOnlyList<SearchPresetDefinition> presets)
    {
        _knownPresets = presets ?? Array.Empty<SearchPresetDefinition>();
        _presetLibrary.RefreshEntries(_knownPresets);
    }

    public void BindPresetVisualRelicCatalog(IReadOnlyList<ModelKey> candidates) =>
        _presetVisualRelicCandidates = (candidates ?? Array.Empty<ModelKey>())
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .OrderBy(key => key.Serialized, StringComparer.Ordinal)
            .ToArray();

    private void OpenPresetSaveTransaction(SearchPresetSaveIntentKind kind, string sourcePresetId)
    {
        if (_uiText is null) return;
        _activePresetSaveKind = kind;
        _activePresetSaveSourceId = sourcePresetId ?? string.Empty;
        _activePresetVisualIconSlot = 0;
        _pendingPresetSaveCommit = null;

        string title = string.Empty;
        string description = string.Empty;
        ModelKey[] icons = Array.Empty<ModelKey>();
        string dialogTitle = _uiText.Get(Ui1TextKey.SearchPresetSaveTitle);

        if (kind == SearchPresetSaveIntentKind.EditMetadata)
        {
            SearchPresetDefinition? source = _knownPresets.FirstOrDefault(candidate =>
                candidate.Source == SearchPresetSource.User &&
                string.Equals(candidate.Id, _activePresetSaveSourceId, StringComparison.Ordinal));
            if (source is null) return;
            title = source.Title;
            description = source.Description;
            icons = ResolveRelicVisualIcons(source.VisualIcons);
            dialogTitle = _uiText.Get(Ui1TextKey.SearchPresetEditInfoTitle);
        }
        else if (kind == SearchPresetSaveIntentKind.CreateFromExistingSnapshot)
        {
            SearchPresetDefinition? source = _knownPresets.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, _activePresetSaveSourceId, StringComparison.Ordinal));
            if (source is null) return;
            dialogTitle = _uiText.Get(Ui1TextKey.SearchPresetSaveTemporaryTitle);
        }

        _presetSaveSurface.OpenCreate(dialogTitle, title, description, icons);
    }

    private static ModelKey[] ResolveRelicVisualIcons(IReadOnlyList<SearchPresetVisualIconRef> visualIcons) =>
        (visualIcons ?? Array.Empty<SearchPresetVisualIconRef>())
            .Where(icon => string.Equals(icon.Kind, SearchPresetVisualIconRef.RelicKind, StringComparison.Ordinal))
            .Select(icon => icon.TryGetModelKey(out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .Take(3)
            .ToArray();

    private void ConfirmPresetSave()
    {
        if (UseLegacyPresetWindowFallback)
        {
            ConfirmLegacyPresetSave();
            return;
        }

        string title = _presetSaveSurface.TitleText.Trim();
        if (title.Length == 0) return;
        var visualIcons = _presetSaveSurface.SelectedRelicIcons
            .Select(SearchPresetVisualIconRef.FromRelic)
            .ToArray();
        var commit = new SearchPresetSaveCommit(
            _activePresetSaveKind,
            _activePresetSaveSourceId,
            new SearchPresetMetadataDraft(title, _presetSaveSurface.DescriptionText.Trim(), visualIcons));

        SearchPresetDefinition? sameNameUser = _knownPresets.FirstOrDefault(preset =>
            preset.Source == SearchPresetSource.User &&
            string.Equals(preset.Title, title, StringComparison.CurrentCultureIgnoreCase));

        if (_activePresetSaveKind == SearchPresetSaveIntentKind.EditMetadata)
        {
            if (sameNameUser is not null &&
                !string.Equals(sameNameUser.Id, _activePresetSaveSourceId, StringComparison.Ordinal))
            {
                if (_uiText is not null)
                    _presetSaveSurface.ShowValidationError(_uiText.Get(Ui1TextKey.SearchPresetTitleConflict));
                return;
            }

            PresetSaveConfirmed?.Invoke(commit);
            _presetSaveSurface.CloseCommitted();
            return;
        }

        if (sameNameUser is null)
        {
            PresetSaveConfirmed?.Invoke(commit);
            _presetSaveSurface.CloseCommitted();
            return;
        }

        _pendingPresetSaveCommit = commit;
        if (_uiText is not null)
        {
            _presetOverwriteSurface.Open(
                _uiText.Get(Ui1TextKey.SearchPresetOverwriteTitle),
                _uiText.Get(Ui1TextKey.SearchPresetOverwritePrompt),
                _uiText.Get(Ui1TextKey.SearchPresetSaveConfirm),
                _uiText.Get(Ui1TextKey.SearchPresetCancel));
        }
    }

    private void ConfirmLegacyPresetSave()
    {
        string name = _presetNameInput.Text.Trim();
        if (name.Length == 0) return;
        ModelKey? icon = _presetVisualIconSlot.SelectedKey;
        bool sameNameUser = _knownPresets.Any(preset => preset.Source == SearchPresetSource.User &&
            string.Equals(preset.Title, name, StringComparison.CurrentCultureIgnoreCase));
        if (!sameNameUser)
        {
            var icons = icon is { IsValid: true } selected
                ? new[] { SearchPresetVisualIconRef.FromRelic(selected) }
                : Array.Empty<SearchPresetVisualIconRef>();
            PresetSaveConfirmed?.Invoke(new SearchPresetSaveCommit(
                SearchPresetSaveIntentKind.CreateCurrentQuery,
                string.Empty,
                new SearchPresetMetadataDraft(name, string.Empty, icons)));
            return;
        }
        _pendingPresetSaveName = name;
        _pendingPresetSaveIcon = icon;
        _presetOverwriteDialog.PopupCentered(new Vector2I(430, 150));
    }

    private void OpenPresetVisualIconPicker(int slotIndex)
    {
        if (_uiText is null || _contentNames is null || _presetVisualRelicCandidates.Count == 0) return;

        if (UseLegacyPresetWindowFallback)
        {
            OpenPresetVisualIconPickerLegacyWindowFallback();
            return;
        }

        _activePresetVisualIconSlot = Math.Clamp(slotIndex, 0, 2);
        var request = new RelicPickerRequest(
            _uiText.Get(Ui1TextKey.SearchPresetChooseIcon),
            _presetVisualRelicCandidates,
            _presetSaveSurface.GetVisualIcon(_activePresetVisualIconSlot),
            Array.Empty<ModelKey>(),
            new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance),
            true,
            GameContentKind.Relic,
            IconVariant.Small,
            key => _presetSaveSurface.SetVisualIcon(_activePresetVisualIconSlot, key));

        _presetVisualIconPicker.Open(request);
        if (_presetVisualIconPicker.IsOpen)
            RuntimeLog.Detail($"presetVisualPickerOpened=true;parentSurface=control-save;sharedViewport=true;slot={_activePresetVisualIconSlot + 1}");
        else
            _presetSaveSurface.FocusAfterChildPicker(_activePresetVisualIconSlot);
    }

    private void HandlePresetVisualPickerClosed()
    {
        if (UseLegacyPresetWindowFallback)
        {
            ResumePresetSaveDialogAfterVisualPicker();
            return;
        }

        if (!_suppressPresetChildReturn)
            _presetSaveSurface.FocusAfterChildPicker(_activePresetVisualIconSlot);
    }

    private void OpenPresetVisualIconPickerLegacyWindowFallback()
    {
        if (_uiText is null || _contentNames is null || _presetVisualRelicCandidates.Count == 0) return;

        _resumePresetSaveDialogAfterVisualPicker = _presetSaveDialog.Visible;
        if (_resumePresetSaveDialogAfterVisualPicker) _presetSaveDialog.Hide();

        var request = new RelicPickerRequest(
            _uiText.Get(Ui1TextKey.SearchPresetChooseIcon),
            _presetVisualRelicCandidates,
            _presetVisualIconSlot.SelectedKey,
            Array.Empty<ModelKey>(),
            new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance),
            true,
            GameContentKind.Relic,
            IconVariant.Small,
            key => _presetVisualIconSlot.SetSelection(key, notify: false));

        Callable.From(() =>
        {
            if (!IsInsideTree()) return;
            _presetVisualIconPicker.Open(request);
            if (!_presetVisualIconPicker.IsOpen)
                ResumePresetSaveDialogAfterVisualPicker();
        }).CallDeferred();
    }

    private void ResumePresetSaveDialogAfterVisualPicker()
    {
        if (!_resumePresetSaveDialogAfterVisualPicker) return;
        _resumePresetSaveDialogAfterVisualPicker = false;

        Callable.From(() =>
        {
            if (!IsInsideTree() || _suppressPresetChildReturn) return;
            _presetSaveDialog.PopupCentered(new Vector2I(460, 240));
            _presetNameInput.GrabFocus();
        }).CallDeferred();
    }

    public void RestorePersistedResults(IReadOnlyList<PersistedSearchResult>? results)
    {
        _persistedResultDtos.Clear();
        if (results is not null) _persistedResultDtos.AddRange(results);
        RebuildPersistedResultCards();
    }

    private void RebuildPersistedResultCards()
    {
        foreach (Node child in _results.GetChildren())
        {
            if (!ReferenceEquals(child, _emptyResults)) child.QueueFree();
        }
        _emptyResults.Visible = _persistedResultDtos.Count == 0;
        foreach (PersistedSearchResult result in _persistedResultDtos) AddPersistedResultCard(result);
    }

    private void AddPersistedResultCard(PersistedSearchResult result)
    {
        _emptyResults.Visible = false;
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Page, 3f, 1, 8f);
        // Persisted query fingerprints are diagnostic/persistence metadata, not player-facing hover copy.
        // Keeping the whole result card tooltip-free avoids a screen-sized native tooltip.
        panel.TooltipText = string.Empty;
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        var title = Ui1Theme.Label(result.Seed, Ui1TextRole.Body);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddThemeFontSizeOverride("font_size", 14);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        var copy = new Button { CustomMinimumSize = new Vector2(70, 30), Text = _uiText?.Get(Ui1TextKey.SearchCopyResult) ?? "Copy" };
        var analyze = new Button { CustomMinimumSize = new Vector2(70, 30), Text = _uiText?.Get(Ui1TextKey.SearchAnalyzeResult) ?? "Analyze" };
        Ui1Theme.ApplyButton(copy, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(analyze, Ui1ButtonRole.Secondary);
        copy.Pressed += () => CandidateCopyRequested?.Invoke(result.Seed);
        analyze.Pressed += () => PersistedCandidateOpenRequested?.Invoke(result);
        copy.CustomMinimumSize = analyze.CustomMinimumSize = new Vector2(0, 28);
        copy.AddThemeFontSizeOverride("font_size", 13);
        analyze.AddThemeFontSizeOverride("font_size", 13);
        actions.AddChild(title);
        actions.AddChild(copy);
        actions.AddChild(analyze);
        column.AddChild(actions);
        panel.AddChild(column);
        _results.AddChild(panel);
    }

    public override void _Process(double delta) => PollRequested?.Invoke();

    public void BindRuntime(ModRuntimeSnapshot runtime)
    {
        _runtime = runtime;
        _startSeed.Text = new string(runtime.Profile.SeedAlphabet[0], runtime.Profile.SeedLength);
        RefreshRuntimeCompatibilityWarning();
    }

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _uiText = uiText;
        _contentNames = contentNames;
        _pageTitle.Text = uiText.Get(Ui1TextKey.SearchTitle);
        RefreshRuntimeCompatibilityWarning();
        _contextBar.ApplyLocalization(uiText, contentNames, uiText.Get(Ui1TextKey.MissingIconTooltip));
        _presetSaveDialog.Title = uiText.Get(Ui1TextKey.SearchPresetSaveTitle);
        _presetSaveDialog.DialogText = uiText.Get(Ui1TextKey.SearchPresetNamePrompt);
        _presetVisualMarkLabel.Text = uiText.Get(Ui1TextKey.SearchPresetVisualMark);
        _presetVisualIconSlot.BindText(contentNames, uiText.Get(Ui1TextKey.SearchPresetChooseIcon));
        _presetOverwriteDialog.Title = uiText.Get(Ui1TextKey.SearchPresetOverwriteTitle);
        _presetOverwriteDialog.DialogText = uiText.Get(Ui1TextKey.SearchPresetOverwritePrompt);
        _presetDeleteDialog.Title = uiText.Get(Ui1TextKey.SearchPresetDeleteTitle);
        _presetDeleteDialog.DialogText = uiText.Get(Ui1TextKey.SearchPresetDeletePrompt);
        _presetLibrary.ApplyLocalization(uiText, contentNames);
        _presetSaveSurface.ApplyLocalization(uiText, contentNames);
        _presetVisualIconPicker.ApplyLocalization(uiText, contentNames);
        _presetSaveDialog.OkButtonText = uiText.Get(Ui1TextKey.SearchPresetSaveConfirm);
        _savePresetAction.Text = uiText.Get(Ui1TextKey.SearchPresetSave);
        _loadPresetAction.Text = uiText.Get(Ui1TextKey.SearchPresetWorkspaceEntry);
        _categoryNavigation.ApplyLocalization(uiText);
        _categoryHost.ApplyLocalization(uiText, contentNames);
        _integratedFilters.ApplyLocalization(uiText);
        _filtersTitle.Text = uiText.Get(Ui1TextKey.SearchFiltersTitle);
        _requireBones.Text = uiText.Get(Ui1TextKey.SearchRequireBones);
        _smallCapsule.Text = uiText.Get(Ui1TextKey.SearchSmallCapsule);
        _largeCapsule.Text = uiText.Get(Ui1TextKey.SearchLargeCapsule);
        _whetstone.Text = uiText.Get(Ui1TextKey.SearchWhetstone);
        _warPaint.Text = uiText.Get(Ui1TextKey.SearchWarPaint);
        _validationPreset.Text = uiText.Get(Ui1TextKey.SearchValidationPreset);
        _startSeedLabel.Text = uiText.Get(Ui1TextKey.SearchStartSeed);
        _scanCountLabel.Text = uiText.Get(Ui1TextKey.SearchScanCount);
        _advancedSearchHint.Text = uiText.Get(Ui1TextKey.SearchAdvancedParametersHint);
        _compatibilityHint.Text = uiText.Get(Ui1TextKey.SearchCompatibilityInputsHint);
        _neowAnyLabel.Text = uiText.Get(Ui1TextKey.SearchNeowAny);
        _neowAllLabel.Text = uiText.Get(Ui1TextKey.SearchNeowAll);
        _neowBanLabel.Text = uiText.Get(Ui1TextKey.SearchNeowBan);
        _bonesAnyLabel.Text = uiText.Get(Ui1TextKey.SearchBonesAny);
        _bonesAllLabel.Text = uiText.Get(Ui1TextKey.SearchBonesAll);
        _bonesBanLabel.Text = uiText.Get(Ui1TextKey.SearchBonesBan);
        _capsuleAnyLabel.Text = uiText.Get(Ui1TextKey.SearchCapsuleAny);
        _capsuleAllLabel.Text = uiText.Get(Ui1TextKey.SearchCapsuleAll);
        _capsuleBanLabel.Text = uiText.Get(Ui1TextKey.SearchCapsuleBan);
        _finalCurseLabel.Text = uiText.Get(Ui1TextKey.SearchFinalCurse);
        _banCursesLabel.Text = uiText.Get(Ui1TextKey.SearchBanCurses);
        _bossActLabel.Text = uiText.Get(Ui1TextKey.SearchBossAct);
        _bossAnyLabel.Text = uiText.Get(Ui1TextKey.SearchBossAny);
        _bossBanLabel.Text = uiText.Get(Ui1TextKey.SearchBossBan);
        _ancientActLabel.Text = uiText.Get(Ui1TextKey.SearchAncientAct);
        _ancientAnyLabel.Text = uiText.Get(Ui1TextKey.SearchAncientAny);
        _ancientBanLabel.Text = uiText.Get(Ui1TextKey.SearchAncientBan);
        _ancientOptionAnyLabel.Text = uiText.Get(Ui1TextKey.SearchAncientOptionAny);
        _ancientOptionBanLabel.Text = uiText.Get(Ui1TextKey.SearchAncientOptionBan);
        _controlTitle.Text = uiText.Get(Ui1TextKey.SearchControlTitle);
        _start.Text = uiText.Get(Ui1TextKey.SearchStart);
        _stop.Text = uiText.Get(Ui1TextKey.SearchStop);
        _targetCountTitle.Text = uiText.Get(Ui1TextKey.SearchTargetMatches);
        _gpuBackendTitle.Text = uiText.Get(Ui1TextKey.SearchGpuBackendTitle);
        _gpuBackendSelect.SetItemText(0, uiText.Get(Ui1TextKey.SearchGpuBackendAuto));
        _gpuBackendSelect.SetItemText(1, uiText.Get(Ui1TextKey.SearchGpuBackendCpu));
        UpdateProbabilityFoldoutText();
        BindProbabilityQuickView(_probabilityView);
        UpdateEtaFoldoutText();
        RenderEtaQuickView(_etaView);
        RefreshGpuBackendControls();
        UpdateProgressFoldoutText();
        _speedTitle.Text = uiText.Get(Ui1TextKey.SearchSpeed);
        _elapsedTitle.Text = uiText.Get(Ui1TextKey.SearchElapsed);
        UpdateResultsHeader(_persistedResultDtos.Count, (int)_targetCount.Value);
        _resultsStaleNotice.Text = uiText.Get(Ui1TextKey.SearchResultsStale);
        _emptyResults.Text = uiText.Get(Ui1TextKey.SearchNoResults);
        _viewAll.Text = uiText.Get(Ui1TextKey.SearchViewAll);
        _viewAll.TooltipText = uiText.Get(Ui1TextKey.SearchAllResultsNotMigrated);
        if (_persistedResultDtos.Count > 0) RebuildPersistedResultCards();
        UpdateFoldoutText();
    }


    private void RefreshRuntimeCompatibilityWarning()
    {
        if (_uiText is null)
        {
            _runtimeCompatibilityWarning.Bind(Array.Empty<string>());
            return;
        }
        _runtimeCompatibilityWarning.Bind(RuntimeAuthorityWarningComposer.Build(_uiText, _runtime));
    }

    public void BindSearchAuthorityWarnings(IReadOnlyList<string> warnings) =>
        _searchAuthorityWarning.Bind(warnings ?? Array.Empty<string>());

    public void BindProbabilityQuickView(SearchProbabilityQuickView view)
    {
        _probabilityView = view ?? SearchProbabilityQuickView.Unavailable;
        if (_uiText is null)
            return;

        foreach (Node child in _probabilityRows.GetChildren())
        {
            _probabilityRows.RemoveChild(child);
            child.QueueFree();
        }

        switch (_probabilityView.Status)
        {
            case SearchProbabilityQuickViewStatus.Complete when _probabilityView.TotalProbability is > 0d:
                _probabilityValue.Text = "≈ " + CompactNumberFormatter.FormatRarity(_probabilityView.TotalProbability.Value);
                _probabilityValue.TooltipText = string.Empty;
                break;
            case SearchProbabilityQuickViewStatus.Impossible:
                _probabilityValue.Text = _uiText.Get(Ui1TextKey.SearchProbabilityImpossible);
                _probabilityValue.TooltipText = SearchImpossibilityPresentation.Format(
                    _probabilityView.ImpossibilityProof,
                    _uiText);
                break;
            case SearchProbabilityQuickViewStatus.Partial:
                _probabilityValue.Text = _uiText.Get(Ui1TextKey.SearchProbabilityUnavailable);
                _probabilityValue.TooltipText = _uiText.Get(Ui1TextKey.SearchProbabilityPartialHint);
                break;
            default:
                _probabilityValue.Text = _uiText.Get(Ui1TextKey.SearchProbabilityUnavailable);
                _probabilityValue.TooltipText = _probabilityView.UnknownComponentCount > 0
                    ? _uiText.Get(Ui1TextKey.SearchProbabilityPartialHint)
                    : string.Empty;
                break;
        }

        foreach (SearchProbabilityQuickViewRow row in _probabilityView.Rows)
        {
            Label name = MetricTitle();
            name.Text = ProbabilityRowLabel(row);
            Label value = DetailValue();
            value.Text = row.Probability.HasValue
                ? CompactNumberFormatter.FormatRarity(row.Probability.Value)
                : _uiText.Get(Ui1TextKey.SearchProbabilityRowUnavailable);
            _probabilityRows.AddChild(CreateDetailRow(name, value));
        }
    }

    public void BindEtaQuickView(SearchEtaQuickView view)
    {
        _predictedEtaView = view ?? SearchEtaQuickView.Unavailable((int)_targetCount.Value);
        _lastLiveEtaSeconds = -1;
        RenderEtaQuickView(_predictedEtaView);
    }

    private void RenderEtaQuickView(SearchEtaQuickView view)
    {
        _etaView = view;
        if (_uiText is null)
            return;
        UpdateEtaFoldoutText();

        foreach (Node child in _etaRows.GetChildren())
        {
            _etaRows.RemoveChild(child);
            child.QueueFree();
        }

        string unavailable = _uiText.Get(Ui1TextKey.SearchProbabilityRowUnavailable);
        switch (_etaView.Status)
        {
            case SearchEtaQuickViewStatus.NoConditions:
                _etaValue.Text = _uiText.Get(Ui1TextKey.SearchEtaNoConditions);
                break;
            case SearchEtaQuickViewStatus.Ready when _etaView.FirstResultMeanMs.HasValue:
                _etaValue.Text = CompactNumberFormatter.FormatDuration(_etaView.FirstResultMeanMs.Value);
                break;
            case SearchEtaQuickViewStatus.Calibrating:
                _etaValue.Text = _uiText.Get(Ui1TextKey.SearchEtaCalibrating);
                break;
            case SearchEtaQuickViewStatus.Impossible:
                _etaValue.Text = _uiText.Get(Ui1TextKey.SearchProbabilityImpossible);
                break;
            default:
                _etaValue.Text = _uiText.Get(Ui1TextKey.SearchEtaUnavailable);
                break;
        }

        AddEtaRow(
            _uiText.Get(_etaView.IsLive ? Ui1TextKey.SearchEtaLiveP99 : Ui1TextKey.SearchEtaFirstP99),
            _etaView.FirstResultP99Ms.HasValue ? CompactNumberFormatter.FormatDuration(_etaView.FirstResultP99Ms.Value) : unavailable);
        AddEtaRow(
            _etaView.IsLive ? _uiText.Format(Ui1TextKey.SearchEtaRemainingMean, _etaView.RemainingTargetCount) :
                _uiText.Format(Ui1TextKey.SearchEtaTargetMean, _etaView.TargetCount),
            _etaView.TargetMeanMs.HasValue ? CompactNumberFormatter.FormatDuration(_etaView.TargetMeanMs.Value) : unavailable);

        string missing = _etaView.MissingEvidence.Count == 0
            ? string.Empty
            : string.Join("\n", _etaView.MissingEvidence);
        string plan = string.IsNullOrWhiteSpace(_etaView.SelectedPlanId)
            ? string.Empty
            : "Plan: " + _etaView.SelectedPlanId;
        _etaValue.TooltipText = string.Join("\n", new[] { plan, missing }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private void AddEtaRow(string label, string value)
    {
        Label name = MetricTitle();
        name.Text = label;
        Label amount = DetailValue();
        amount.Text = value;
        _etaRows.AddChild(CreateDetailRow(name, amount));
    }

    private string ProbabilityRowLabel(SearchProbabilityQuickViewRow row)
    {
        if (_uiText is null)
            return string.Empty;

        string key = row.Kind switch
        {
            SearchProbabilityRowKind.Neow => Ui1TextKey.SearchProbabilityRowNeow,
            SearchProbabilityRowKind.Relic => Ui1TextKey.SearchProbabilityRowRelic,
            SearchProbabilityRowKind.CapsuleRelicJoint => Ui1TextKey.SearchProbabilityRowCapsuleRelic,
            SearchProbabilityRowKind.WorldEvent => Ui1TextKey.SearchProbabilityRowWorldEvent,
            SearchProbabilityRowKind.Ancient => Ui1TextKey.SearchProbabilityRowAncient,
            SearchProbabilityRowKind.CombatReward => Ui1TextKey.SearchProbabilityRowCombatReward,
            SearchProbabilityRowKind.EventResult => Ui1TextKey.SearchProbabilityRowEventResult,
            SearchProbabilityRowKind.Shop => Ui1TextKey.SearchProbabilityRowShop,
            _ => Ui1TextKey.SearchProbabilityRowUnavailable
        };
        string label = _uiText.Get(key);
        if (row.Explanation is
            { Kind: ProbabilityExplanationKind.NecessaryImplicitEnabler, TargetDomain: SearchSelectivityDomain.CombatReward } explanation &&
            explanation.ModelKey.IsValid)
        {
            string enabler = _contentNames?.Resolve(explanation.ModelKey, GameContentKind.Relic)
                ?? explanation.ModelKey.Entry;
            if (!string.IsNullOrWhiteSpace(enabler))
                label += " · " + enabler;
        }
        return label;
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
    }

    public string Localize(string key) => _uiText?.Get(key) ?? key;
    public string Format(string key, params object?[] args) => _uiText?.Format(key, args) ?? key;

    public string FormatSearchImpossibility(SearchImpossibilityProof? proof) =>
        _uiText is null
            ? proof?.ReasonCode.ToString() ?? "SearchImpossible"
            : SearchImpossibilityPresentation.Format(proof, _uiText);

    internal IGameContentNameResolver? ContentNameResolver => _contentNames;

    public void SetCompact(bool compact)
    {
        _compact = compact;
        _controlPane.CustomMinimumSize = new Vector2(compact ? 286 : 326, 0);
        _results.AddThemeConstantOverride("separation", compact ? 4 : 6);
        _categoryHost.NeowPage.SetCompact(compact);
        _categoryHost.AncientPage.SetCompact(compact);
        _categoryHost.BossMapPage.SetCompact(compact);
        _categoryHost.CombatRewardPage.SetCompact(compact);
    }

    public void SetRunning(bool running)
    {
        if (running && !_start.Disabled) { _scanningSpeed.Reset(); _stableSpeedObserved=false; }
        _start.Disabled = running;
        _stop.Disabled = !running;
        _targetCount.Editable = !running;
        _gpuBackendSelect.Disabled = running;
        // Preset actions intentionally remain available while Search is running.
        // Save captures the frozen authored query; Load follows cancel-then-replace.
        _savePresetAction.Disabled = false;
        _loadPresetAction.Disabled = false;
        _contextBar.SetRunning(running);
        SetEditable(_startSeed, !running);
        _scanCount.Editable = !running;
        _bossAct.Editable = !running;
        _ancientAct.Editable = !running;
        _advancedSearchToggle.Disabled = running;
        _compatibilityToggle.Disabled = running;
        _integratedFilters.SetRunning(running);
        _categoryHost.NeowPage.SetRunning(running);
        _categoryHost.AncientPage.SetRunning(running);
        _categoryHost.BossMapPage.SetRunning(running);
        _categoryHost.RelicPage.SetRunning(running);
        _categoryHost.CombatRewardPage.SetRunning(running);
        _categoryHost.EventPage.SetRunning(running);
        _categoryHost.ShopPage.SetRunning(running);
        _categoryNavigation.SetRunning(running);
        foreach (CheckBox check in new[]
        {
            _requireBones, _smallCapsule, _largeCapsule, _whetstone, _warPaint, _validationPreset
        })
        {
            check.Disabled = running;
        }
        foreach (LineEdit field in new[] { _neowAny, _neowAll, _neowBan, _bonesAny, _bonesAll, _bonesBan, _capsuleAny, _capsuleAll, _capsuleBan, _finalCurse, _banCurses, _bossAny, _bossBan, _ancientAny, _ancientBan, _ancientOptionAny, _ancientOptionBan })
        {
            SetEditable(field, !running);
        }
    }

    public void RefreshGpuBackendControls()
    {
        // Ordinary Workshop UI continuously fail-closes stale developer backend
        // preferences. Explicit developer diagnostics may retain a forced value
        // for acceptance tooling, but that is outside the player-mode contract.
        if (!SearchOperationalLogPolicy.DeveloperDiagnosticsEnabled)
            _settings.NormalizeWorkshopSearchModePreference();

        _gpuBackendSelect.Selected = _settings.RelicComputeBackendPreference == Beta110RelicComputeBackendPreference.Cpu ? 1 : 0;
        _gpuBackendSelect.Disabled = false;
    }

    public void ClearResults()
    {
        _persistedResultDtos.Clear();
        foreach (Node child in _results.GetChildren())
        {
            if (!ReferenceEquals(child, _emptyResults))
            {
                child.QueueFree();
            }
        }
        _emptyResults.Visible = true;
        _resultsStaleNotice.Visible = false;
        _issue.Visible = false;
        ResetMetrics();
    }

    public int PersistedResultCount => _persistedResultDtos.Count;

    public void SetResultsStale(bool stale)
    {
        _resultsStaleNotice.Visible = stale && _persistedResultDtos.Count > 0;
    }

    private readonly SearchScanningSpeed _scanningSpeed = new();
    private bool _stableSpeedObserved;

    public void UpdateProgress(SearchProgressSnapshot progress)
    {
        _scannedValue.Text = CompactNumberFormatter.FormatCount(progress.ScannedCount);
        UpdateResultsHeader(progress.MatchCount, progress.TargetMatchCount);
        double? scanningSpeed = _scanningSpeed.Observe(progress.ElapsedSeconds, progress.ScannedCount);
        if(scanningSpeed.HasValue && !_stableSpeedObserved)
        {
            _stableSpeedObserved=true;
            RolltheSpire2.Bootstrap.RuntimeLog.Detail($"searchStartup=true;phase=StableSpeedWindowReady;sinceSessionSeconds={progress.ElapsedSeconds:F4};scanned={progress.ScannedCount};rollingRootsPerSecond={scanningSpeed.Value:F0};samplingWindowSeconds=1;plannerInput=false");
        }
        scanningSpeed = SearchScanningSpeed.Terminal(progress,scanningSpeed);
        if (progress.State != SearchRunState.Running || _lastLiveEtaSeconds < 0 ||
            progress.ElapsedSeconds - _lastLiveEtaSeconds >= 1)
        {
            var live = SearchEtaPresentationBuilder.Live(_predictedEtaView, progress, scanningSpeed);
            if (live.IsLive && !_etaView.IsLive)
                RuntimeLog.Info($"searchLiveEta=true;source=CompletedRootRollingWallRate;frozenProbability={live.AcceptedResultSurvivalRate};rootsPerSecond={scanningSpeed};remainingTargets={live.RemainingTargetCount};firstMeanMs={live.FirstResultMeanMs};remainingMeanMs={live.TargetMeanMs};plannerChanged=false;costSnapshotChanged=false;calibrationInput=false");
            if (live != _etaView) RenderEtaQuickView(live);
            _lastLiveEtaSeconds = progress.ElapsedSeconds;
        }
        _speedValue.Text = scanningSpeed.HasValue ? CompactNumberFormatter.FormatRate(scanningSpeed.Value) : "—";
        _elapsedValue.Text = CompactNumberFormatter.FormatDuration(progress.ElapsedSeconds * 1000d);
        _scannedValue.TooltipText = _uiText?.Format(Ui1TextKey.SearchScannedExactTooltip, progress.ScannedCount)
            ?? progress.ScannedCount.ToString("N0");
        _speedValue.TooltipText = scanningSpeed.HasValue
            ? _uiText?.Format(Ui1TextKey.SearchSpeedExactTooltip, scanningSpeed.Value) ?? $"{scanningSpeed.Value:N0} seeds/s"
            : "—";
        _elapsedValue.TooltipText = _uiText?.Format(Ui1TextKey.SearchElapsedExactTooltip, progress.ElapsedSeconds)
            ?? $"{progress.ElapsedSeconds:0.0} s";
        _issue.Text = progress.FailureCode;
        _issue.Visible = !string.IsNullOrWhiteSpace(progress.FailureCode);
        SetRunning(progress.State == SearchRunState.Running);
    }

    public void ShowError(string issue)
    {
        _issue.Text = issue;
        _issue.Visible = true;
        SetRunning(false);
    }

    public void AddCandidate(SearchCandidate candidate, string queryFingerprint = "")
    {
        if (!_persistedResultDtos.Any(item => string.Equals(item.Seed, candidate.Seed, StringComparison.Ordinal) &&
                                             string.Equals(item.QueryFingerprint, queryFingerprint, StringComparison.Ordinal)))
        {
            _persistedResultDtos.Add(new PersistedSearchResult
            {
                Seed = candidate.Seed,
                CharacterKey = candidate.CharacterKey.Serialized,
                Ascension = candidate.Ascension,
                QueryFingerprint = queryFingerprint ?? string.Empty
            });
        }
        _emptyResults.Visible = false;
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Page, 3f, 1, 8f);
        // Match evidence and witness data remain available through Analyze/logging; do not attach
        // them to the entire compact result card as a native tooltip.
        panel.TooltipText = string.Empty;
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        var title = Ui1Theme.Label(BuildCandidateTitle(candidate), Ui1TextRole.Body);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddThemeFontSizeOverride("font_size", 14);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        var copy = new Button { CustomMinimumSize = new Vector2(70, 30) };
        var analyze = new Button { CustomMinimumSize = new Vector2(70, 30) };
        copy.Text = _uiText?.Get(Ui1TextKey.SearchCopyResult) ?? "Copy";
        analyze.Text = _uiText?.Get(Ui1TextKey.SearchAnalyzeResult) ?? "Analyze";
        Ui1Theme.ApplyButton(copy, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(analyze, Ui1ButtonRole.Secondary);
        copy.Pressed += () => CandidateCopyRequested?.Invoke(candidate.Seed);
        analyze.Pressed += () => CandidateOpenRequested?.Invoke(candidate);
        copy.CustomMinimumSize = analyze.CustomMinimumSize = new Vector2(0, 28);
        copy.AddThemeFontSizeOverride("font_size", 13);
        analyze.AddThemeFontSizeOverride("font_size", 13);
        actions.AddChild(title);
        actions.AddChild(copy);
        actions.AddChild(analyze);
        column.AddChild(actions);
        panel.AddChild(column);
        _results.AddChild(panel);
        UpdateResultsHeader(_persistedResultDtos.Count, (int)_targetCount.Value);
    }

    private string BuildCandidateTitle(SearchCandidate candidate) => candidate.Seed;

    public void BindNeowUiCatalog(NeowSearchUiCatalog catalog) =>
        _categoryHost.NeowPage.BindCatalog(catalog);

    public void BindAncientUiCatalog(AncientSearchUiCatalog catalog) =>
        _categoryHost.AncientPage.BindCatalog(catalog);

    public void BindBossMapUiCatalog(BossMapSearchUiCatalog catalog) =>
        _categoryHost.BossMapPage.BindCatalog(catalog);

    public void BindRelicSequenceUiCatalog(RelicSequenceSearchUiCatalog catalog)
    {
        _categoryHost.RelicPage.BindCatalog(catalog);
        _categoryHost.ShopPage.BindRelicCatalog(catalog);
    }

    public void BindCombatRewardUiCatalog(CombatRewardSearchUiCatalog catalog) =>
        _categoryHost.CombatRewardPage.BindCatalog(catalog);

    public void BindEventSequenceUiCatalog(EventSequenceSearchUiCatalog catalog) =>
        _categoryHost.EventPage.BindCatalog(catalog);

    public void BindShopColorlessUiCatalog(Shop.ShopColorlessSearchUiCatalog catalog) =>
        _categoryHost.ShopPage.BindCatalog(catalog);

    private void OnStartPressed()
    {
        if (!_categoryHost.NeowPage.TryBuildDraft(out NeowRouteFilterDraft routeDraft, out string issue, focusInvalid: true))
        {
            _categoryNavigation.Select(SearchCategoryKey.Neow, notify: true);
            _categoryHost.Select(SearchCategoryKey.Neow);
            ShowError(issue);
            return;
        }
        if (!_categoryHost.BossMapPage.TryBuildDraft(out BossMapSearchDraft bossMapDraft, out issue, focusInvalid: true))
        {
            _categoryNavigation.Select(SearchCategoryKey.BossAndMap, notify: true);
            _categoryHost.Select(SearchCategoryKey.BossAndMap);
            ShowError(issue);
            return;
        }
        if (!_categoryHost.RelicPage.TryValidateCatalogConsistency(out issue))
        {
            _categoryNavigation.Select(SearchCategoryKey.Relic, notify: true);
            _categoryHost.Select(SearchCategoryKey.Relic);
            ShowError(issue);
            return;
        }
        if (!_categoryHost.CombatRewardPage.TryBuildDraft(out CombatRewardSearchDraft combatRewardDraft, out issue, focusInvalid: true))
        {
            _categoryNavigation.Select(SearchCategoryKey.CombatReward, notify: true);
            _categoryHost.Select(SearchCategoryKey.CombatReward);
            ShowError(issue);
            return;
        }
        SearchDraft draft = CurrentDraft with
        {
            NeowRouteDraft = routeDraft,
            BossMapDraft = bossMapDraft,
            CombatRewardDraft = combatRewardDraft
        };
        StartRequested?.Invoke(draft, CurrentRunDraft);
    }

    private void ClearAllFilters() => ClearAllFilters(notify: true);

    private void ClearAllFilters(bool notify)
    {
        _suppressQueryDraftChanged = true;
        try
        {
            foreach (LineEdit field in new[]
            {
                _neowAny, _neowAll, _neowBan, _bonesAny, _bonesAll, _bonesBan,
                _capsuleAny, _capsuleAll, _capsuleBan, _finalCurse, _banCurses, _bossAny, _bossBan,
                _ancientAny, _ancientBan, _ancientOptionAny, _ancientOptionBan
            })
            {
                field.Text = string.Empty;
            }
            foreach (CheckBox check in new[]
            {
                _requireBones, _smallCapsule, _largeCapsule, _whetstone, _warPaint, _validationPreset
            })
            {
                check.ButtonPressed = false;
            }
            _integratedFilters.ClearFields();
            _categoryHost.NeowPage.ClearDraft();
            _categoryHost.AncientPage.ClearDraft();
            _categoryHost.BossMapPage.ClearDraft();
            _categoryHost.RelicPage.ClearDraft();
            _categoryHost.CombatRewardPage.ClearDraft();
            _categoryHost.EventPage.ClearDraft();
            _categoryHost.ShopPage.ClearDraft();
        }
        finally
        {
            _suppressQueryDraftChanged = false;
        }

        UpdateConditionSummary();
        if (notify) QueryDraftChanged?.Invoke();
    }

    private void OnQueryDraftChanged()
    {
        if (_suppressQueryDraftChanged)
        {
            return;
        }

        UpdateConditionSummary();
        QueryDraftChanged?.Invoke();
    }

    private int ResolveEnabledConditionCount()
    {
        // Player-visible condition count follows the six typed category pages that
        // actually compile into Search semantics. Hidden legacy draft storage must
        // never inflate the snapshot count persisted by Presets.
        return _categoryHost.NeowPage.EnabledConditionCount
            + _categoryHost.BossMapPage.EnabledConditionCount
            + _categoryHost.AncientPage.EnabledConditionCount
            + _categoryHost.RelicPage.EnabledConditionCount
            + _categoryHost.CombatRewardPage.EnabledConditionCount
            + _categoryHost.EventPage.EnabledConditionCount
            + _categoryHost.ShopPage.EnabledConditionCount;
    }

    private void UpdateConditionSummary()
    {
        int count = ResolveEnabledConditionCount();
        _integratedFilters.SetBaseConditionCount(count);
        _categoryNavigation.SetEnabledConditionCount(count);
    }

    public void CancelTransientSurfacesForPageSwitch()
    {
        CancelTransientSurfaces(preservePresetWorkspace: true, reason: "page-switch");
    }

    public bool TryHandleEscape()
    {
        // One Escape closes exactly one active Search-owned layer. The ordering
        // mirrors the Phase A contract: Picker -> Transaction -> Workspace ->
        // Page main surface. AppShell owns only the final Page-main -> RT2 close.
        if (_presetVisualIconPicker.IsOpen)
        {
            _presetVisualIconPicker.Cancel();
            RuntimeLog.Detail("uiSurfaceEscapeConsumed=true;page=Search;layer=picker");
            return true;
        }

        if (_presetOverwriteSurface.IsOpen)
        {
            _presetOverwriteSurface.Cancel();
            RuntimeLog.Detail("uiSurfaceEscapeConsumed=true;page=Search;layer=overwrite-transaction");
            return true;
        }
        if (UseLegacyPresetWindowFallback && _presetOverwriteDialog.Visible)
        {
            _presetOverwriteDialog.Hide();
            return true;
        }

        if (_presetDeleteSurface.IsOpen)
        {
            _presetDeleteSurface.Cancel();
            RuntimeLog.Detail("uiSurfaceEscapeConsumed=true;page=Search;layer=delete-transaction");
            return true;
        }
        if (UseLegacyPresetWindowFallback && _presetDeleteDialog.Visible)
        {
            _presetDeleteDialog.Hide();
            _pendingPresetDeleteId = string.Empty;
            return true;
        }

        if (_presetSaveSurface.IsOpen)
        {
            _presetSaveSurface.Cancel();
            RuntimeLog.Detail("uiSurfaceEscapeConsumed=true;page=Search;layer=save-transaction");
            return true;
        }
        if (UseLegacyPresetWindowFallback && _presetSaveDialog.Visible)
        {
            _resumePresetSaveDialogAfterVisualPicker = false;
            _presetSaveDialog.Hide();
            return true;
        }

        if (_presetLibrary.IsOpen)
        {
            _presetLibrary.Cancel();
            RuntimeLog.Detail("uiSurfaceEscapeConsumed=true;page=Search;layer=preset-workspace");
            return true;
        }

        bool categoryTransientClosed = _categoryHost.TryCancelSelectedTransientSurface();
        if (categoryTransientClosed)
            RuntimeLog.Detail("uiSurfaceEscapeConsumed=true;page=Search;layer=category-transient");
        return categoryTransientClosed;
    }

    public void ResetSurfacesForTopLevelClose()
    {
        CancelTransientSurfaces(preservePresetWorkspace: false, reason: "top-level-close");
    }

    private void CancelTransientSurfaces(bool preservePresetWorkspace, string reason)
    {
        _suppressPresetChildReturn = true;
        try
        {
            // Suppress the legacy deferred Window restore before closing the
            // picker; otherwise a Page switch could reopen the hidden Save
            // Window after the Search Page is already inactive.
            _resumePresetSaveDialogAfterVisualPicker = false;

            if (_presetVisualIconPicker.IsOpen) _presetVisualIconPicker.Cancel();
            if (_presetOverwriteSurface.IsOpen) _presetOverwriteSurface.Cancel();
            if (_presetDeleteSurface.IsOpen) _presetDeleteSurface.Cancel();
            if (_presetSaveSurface.IsOpen) _presetSaveSurface.Cancel();

            if (_presetOverwriteDialog.Visible) _presetOverwriteDialog.Hide();
            if (_presetDeleteDialog.Visible) _presetDeleteDialog.Hide();
            if (_presetSaveDialog.Visible) _presetSaveDialog.Hide();

            _pendingPresetSaveName = string.Empty;
            _pendingPresetSaveIcon = null;
            _pendingPresetSaveCommit = null;
            _activePresetSaveSourceId = string.Empty;
            _pendingPresetDeleteId = string.Empty;

            _categoryHost.CancelAllTransientSurfaces();

            if (!preservePresetWorkspace && _presetLibrary.IsOpen)
                _presetLibrary.Cancel();
        }
        finally
        {
            _suppressPresetChildReturn = false;
        }

        RuntimeLog.Detail(
            $"searchPageTransientSurfacesCancelled=true;reason={reason};" +
            $"presetWorkspacePreserved={preservePresetWorkspace.ToString().ToLowerInvariant()}");
    }

    private void UpdateResultsHeader(int matches, int target)
    {
        int safeMatches = Math.Max(0, matches);
        int safeTarget = Math.Max(1, target);
        _recentTitle.Text = _uiText?.Format(Ui1TextKey.SearchResultsWithCount, safeMatches, safeTarget)
            ?? $"Matching results ({safeMatches} / {safeTarget})";
    }

    private void ResetMetrics()
    {
        _scannedValue.Text = "0";
        UpdateResultsHeader(0, (int)_targetCount.Value);
        _speedValue.Text = "0/s";
        _elapsedValue.Text = "0 ms";
        _scannedValue.TooltipText = "0";
        _speedValue.TooltipText = "0";
        _elapsedValue.TooltipText = "0 s";
    }

    private void UpdateFoldoutText()
    {
        if (_uiText is null)
        {
            return;
        }
        _advancedSearchToggle.Text = FoldoutText(_advancedSearchBody.Visible, _uiText.Get(Ui1TextKey.SearchAdvancedParameters));
        _compatibilityToggle.Text = FoldoutText(_compatibilityBody.Visible, _uiText.Get(Ui1TextKey.SearchCompatibilityInputs));
        UpdateProbabilityFoldoutText();
        UpdateEtaFoldoutText();
        UpdateProgressFoldoutText();
    }

    private void UpdateProbabilityFoldoutText()
    {
        if (_uiText is null) return;
        _probabilityToggle.Text = FoldoutText(_probabilityDetailsBody.Visible, _uiText.Get(Ui1TextKey.SearchEstimatedRarity));
    }

    private void UpdateEtaFoldoutText()
    {
        if (_uiText is null) return;
        _etaToggle.Text = FoldoutText(_etaDetailsBody.Visible, _uiText.Get(_etaView.IsLive ?
            Ui1TextKey.SearchEtaLive : Ui1TextKey.SearchEstimatedTime));
    }

    private void UpdateProgressFoldoutText()
    {
        if (_uiText is null) return;
        _progressToggle.Text = FoldoutText(_progressDetailsBody.Visible, _uiText.Get(Ui1TextKey.SearchProgress));
    }

    private static string FoldoutText(bool expanded, string title) => $"{(expanded ? "▾" : "▸")}  {title}";

    private static Button CreateFoldout(Control body, Action refresh)
    {
        var toggle = new Button
        {
            ToggleMode = true,
            ButtonPressed = false,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyButton(toggle, Ui1ButtonRole.Ghost);
        toggle.Toggled += expanded =>
        {
            body.Visible = expanded;
            refresh();
        };
        return toggle;
    }

    private static Button CreateSummaryFoldout(Control body, Action refresh)
    {
        var toggle = new Button
        {
            ToggleMode = true,
            ButtonPressed = false,
            Alignment = HorizontalAlignment.Left,
            Flat = true,
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyButton(toggle, Ui1ButtonRole.Ghost);
        toggle.Flat = true;
        toggle.Toggled += expanded =>
        {
            body.Visible = expanded;
            refresh();
        };
        return toggle;
    }

    private static PanelContainer CreateSummaryFoldoutPanel(Button toggle, Label summary, Control body)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Page, 3f, 1, 6f);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 8);
        header.AddChild(toggle);
        header.AddChild(summary);

        column.AddChild(header);
        column.AddChild(body);
        panel.AddChild(column);
        return panel;
    }

    private static HBoxContainer CreateDetailRow(Label name, Label value)
    {
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

        var line = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(name);
        line.AddChild(value);
        return line;
    }

    private static PanelContainer CreateFoldoutPanel(Button toggle, Control body)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Card, 4f, 1, 10f);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        column.AddChild(toggle);
        column.AddChild(body);
        panel.AddChild(column);
        return panel;
    }

    private static Label MetricTitle() => Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);

    private static Label DetailValue()
    {
        Label value = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        return value;
    }

    private static Label SummaryValue()
    {
        Label value = Ui1Theme.Label(string.Empty, Ui1TextRole.Body);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        return value;
    }

    private static LineEdit TextField(string value, float width) => new()
    {
        Text = value,
        CustomMinimumSize = new Vector2(width, 36),
        ClearButtonEnabled = true
    };

    private static SpinBox NumberField(double min, double max, double value, double step, float width) => new()
    {
        MinValue = min,
        MaxValue = max,
        Value = value,
        Step = step,
        CustomMinimumSize = new Vector2(width, 36),
        AllowGreater = false,
        AllowLesser = false
    };

    private static Control Labeled(Label label, Control control)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(label);
        column.AddChild(control);
        return column;
    }

    private static Control FilterFlow(params Control[] fields)
    {
        var row = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("h_separation", 10);
        row.AddThemeConstantOverride("v_separation", 8);
        foreach (Control field in fields)
        {
            row.AddChild(field);
        }
        return row;
    }

    private static void SetEditable(LineEdit field, bool editable)
    {
        field.Editable = editable;
        field.MouseDefaultCursorShape = editable ? Control.CursorShape.Ibeam : Control.CursorShape.Arrow;
    }
}
