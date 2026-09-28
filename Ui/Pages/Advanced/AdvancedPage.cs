using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Advanced;

internal sealed partial class AdvancedPage : MarginContainer, IAppPage
{
    private readonly RuntimePredictionSettings _settings;
    private readonly SearchWorkspacePersistence _searchPersistence;
    private readonly Label _pageTitle;
    private readonly Label _subtitle;
    private readonly Label _performanceTitle;
    private readonly Label _workerTitle;
    private readonly SpinBox _workerCount;
    private readonly Label _workerHelp;
    private readonly Label _performanceSummary;
    private readonly Label _searchRangeTitle;
    private readonly LineEdit _customStartSeed;
    private readonly Label _customStartSeedHelp;
    private readonly Label _customStartSeedStatus;
    private readonly Button _persistentOriginSet;
    private readonly Button _persistentOriginRandomize;
    private readonly Label _logsTitle;
    private readonly Button _openLogDirectory;
    private IUiTextProvider? _uiText;

    public AdvancedPage(RuntimePredictionSettings settings, SearchWorkspacePersistence searchPersistence)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _searchPersistence = searchPersistence ?? throw new ArgumentNullException(nameof(searchPersistence));
        PageKey = AppPageKey.Advanced;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 14);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _subtitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        column.AddChild(_pageTitle);
        column.AddChild(_subtitle);

        // Keep the existing budget setting intact while its release UI is hidden.
        var performancePanel = new PanelContainer { Visible = false };
        Ui1Theme.ApplyPanel(performancePanel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var performanceColumn = new VBoxContainer();
        performanceColumn.AddThemeConstantOverride("separation", 10);
        _performanceTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _workerTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _workerCount = new SpinBox
        {
            MinValue = 1,
            MaxValue = 64,
            Step = 1,
            Value = _settings.SearchWorkerCount,
            AllowGreater = false,
            AllowLesser = false,
            CustomMinimumSize = new Vector2(180, 38)
        };
        _workerCount.ValueChanged += value =>
        {
            _settings.SearchWorkerCount = (int)value;
            RefreshPerformanceSummary();
        };
        _workerHelp = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _performanceSummary = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        performanceColumn.AddChild(_performanceTitle);
        performanceColumn.AddChild(_workerTitle);
        performanceColumn.AddChild(_workerCount);
        performanceColumn.AddChild(_workerHelp);
        performanceColumn.AddChild(_performanceSummary);
        performancePanel.AddChild(performanceColumn);
        column.AddChild(performancePanel);

        var searchRangePanel = new PanelContainer();
        Ui1Theme.ApplyPanel(searchRangePanel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var searchRangeColumn = new VBoxContainer();
        searchRangeColumn.AddThemeConstantOverride("separation", 10);
        _searchRangeTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        // Persistent origin is the user-owned Search range setting.
        _customStartSeed = new LineEdit
        {
            Text = _searchPersistence.CurrentPersistentOriginSeed,
            MaxLength = Beta110Profile.Instance.SeedLength,
            PlaceholderText = "000000000000",
            CustomMinimumSize = new Vector2(280, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
        };
        _customStartSeed.TextChanged += _ => RefreshCustomStartSeedControls();
        _customStartSeedHelp = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _customStartSeedStatus = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _persistentOriginSet = new Button { CustomMinimumSize = new Vector2(170, 38) };
        _persistentOriginRandomize = new Button { CustomMinimumSize = new Vector2(150, 38) };
        Ui1Theme.ApplyButton(_persistentOriginSet, Ui1ButtonRole.Primary);
        Ui1Theme.ApplyButton(_persistentOriginRandomize, Ui1ButtonRole.Secondary);
        _persistentOriginSet.Pressed += () =>
        {
            if (_searchPersistence.TrySetPersistentOrigin(_customStartSeed.Text, out string canonical, out _))
                _customStartSeed.Text = canonical;
            RefreshCustomStartSeedControls();
        };
        _persistentOriginRandomize.Pressed += () =>
        {
            _customStartSeed.Text = _searchPersistence.RandomizePersistentOrigin();
            RefreshCustomStartSeedControls();
        };
        var persistentOriginActions = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        persistentOriginActions.AddThemeConstantOverride("h_separation", 8);
        persistentOriginActions.AddThemeConstantOverride("v_separation", 8);
        persistentOriginActions.AddChild(_persistentOriginSet);
        persistentOriginActions.AddChild(_persistentOriginRandomize);
        searchRangeColumn.AddChild(_searchRangeTitle);
        searchRangeColumn.AddChild(_customStartSeed);
        searchRangeColumn.AddChild(persistentOriginActions);
        searchRangeColumn.AddChild(_customStartSeedHelp);
        searchRangeColumn.AddChild(_customStartSeedStatus);
        searchRangePanel.AddChild(searchRangeColumn);
        column.AddChild(searchRangePanel);

        // Actionable bug-report controls use the existing operational log.
        var logsPanel = new PanelContainer();
        Ui1Theme.ApplyPanel(logsPanel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var logsColumn = new VBoxContainer();
        logsColumn.AddThemeConstantOverride("separation", 10);
        _logsTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _openLogDirectory = new Button
        {
            CustomMinimumSize = new Vector2(180, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyButton(_openLogDirectory, Ui1ButtonRole.Secondary);
        _openLogDirectory.Pressed += () => OpenLogDirectoryRequested?.Invoke();
        logsColumn.AddChild(_logsTitle);
        logsColumn.AddChild(_openLogDirectory);
        logsPanel.AddChild(logsColumn);
        column.AddChild(logsPanel);

        scroll.AddChild(column);
        AddChild(scroll);
    }

    public event Action? OpenLogDirectoryRequested;
    public AppPageKey PageKey { get; }
    public Control View => this;
    public void ApplyDisplayMode(AppDisplayMode mode) { }

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _uiText = uiText;
        _pageTitle.Text = uiText.Get(Ui1TextKey.AdvancedTitle);
        _subtitle.Text = uiText.Get(Ui1TextKey.AdvancedSubtitle);
        _performanceTitle.Text = uiText.Get(Ui1TextKey.AdvancedSearchPerformance);
        _workerTitle.Text = uiText.Get(Ui1TextKey.AdvancedWorkerCount);
        _workerHelp.Text = uiText.Get(Ui1TextKey.AdvancedWorkerCountHelp);
        RefreshPerformanceSummary();
        _searchRangeTitle.Text = uiText.Get(Ui1TextKey.AdvancedSearchRange);
        _customStartSeedHelp.Text = uiText.Get(Ui1TextKey.AdvancedCustomStartSeedHelp);
        _persistentOriginSet.Text = uiText.Get(Ui1TextKey.AdvancedPersistentOriginSet);
        _persistentOriginRandomize.Text = uiText.Get(Ui1TextKey.AdvancedPersistentOriginRandomize);
        RefreshCustomStartSeedControls();
        _logsTitle.Text = uiText.Get(Ui1TextKey.AdvancedLogs);
        _openLogDirectory.Text = uiText.Get(Ui1TextKey.AdvancedOpenLogDirectory);
    }

    private void RefreshCustomStartSeedControls()
    {
        _customStartSeed.Editable = true;
        if (_uiText is null) return;

        bool valid = Beta110Profile.Instance.TryCanonicalizeSeed(
            _customStartSeed.Text, out string canonicalSeed, out string issue);
        _persistentOriginSet.Disabled = !valid;
        string validation = valid
            ? _uiText.Format(Ui1TextKey.AdvancedCustomStartSeedValid, canonicalSeed)
            : _uiText.Format(Ui1TextKey.AdvancedCustomStartSeedInvalid, issue);
        string cursor = _uiText.Format(
            Ui1TextKey.AdvancedPersistentOriginStatus,
            _searchPersistence.CurrentPersistentOriginSeed,
            _searchPersistence.CurrentNextCursorSeed,
            _searchPersistence.Cursor.WrapCount);
        _customStartSeedStatus.Text = validation + "\n" + cursor;
    }

    private void RefreshPerformanceSummary()
    {
        if (_uiText is null)
        {
            return;
        }
        _performanceSummary.Text = _uiText.Format(Ui1TextKey.AdvancedPerformanceSummary, _settings.SearchWorkerCount);
    }

}
