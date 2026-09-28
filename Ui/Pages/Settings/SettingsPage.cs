using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Settings;

internal sealed partial class SettingsPage : MarginContainer, IAppPage, IPageSurfaceLifecycle
{
    private readonly Label _pageTitle;
    private readonly Label _subtitle;
    private readonly Label _windowTitle;
    private readonly Label _windowGeometry;
    private readonly Button _centerWindow;
    private readonly Button _resetWindow;
    private readonly Label _historicalTitle;
    private readonly Label _historicalHelp;
    private readonly Button _deleteHistoricalData;
    private readonly ConfirmationDialog _deleteHistoricalConfirmation;
    private IUiTextProvider? _text;
    private bool _deletePending;
    private readonly Label _languageTitle;
    private readonly OptionButton _language;

    public SettingsPage()
    {
        PageKey = AppPageKey.Settings;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 14);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _subtitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        column.AddChild(_pageTitle);
        column.AddChild(_subtitle);

        var languagePanel = new PanelContainer();
        Ui1Theme.ApplyPanel(languagePanel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var languageColumn = new VBoxContainer();
        _languageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _language = new OptionButton { CustomMinimumSize = new Vector2(240, 42), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _language.AddItem("Follow game");
        _language.AddItem("简体中文");
        _language.AddItem("English");
        _language.ItemSelected += index => LanguageRequested?.Invoke(index == 1 ? "zh" : index == 2 ? "en" : "");
        languageColumn.AddChild(_languageTitle);
        languageColumn.AddChild(_language);
        languagePanel.AddChild(languageColumn);
        column.AddChild(languagePanel);

        var window = new PanelContainer();
        Ui1Theme.ApplyPanel(window, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var windowColumn = new VBoxContainer();
        windowColumn.AddThemeConstantOverride("separation", 10);
        _windowTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _windowGeometry = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        var windowActions = new HBoxContainer();
        windowActions.AddThemeConstantOverride("separation", 8);
        _centerWindow = new Button();
        _resetWindow = new Button();
        Ui1Theme.ApplyButton(_centerWindow, Ui1ButtonRole.Secondary);
        Ui1Theme.ApplyButton(_resetWindow, Ui1ButtonRole.Secondary);
        windowActions.AddChild(_centerWindow);
        windowActions.AddChild(_resetWindow);
        windowColumn.AddChild(_windowTitle);
        windowColumn.AddChild(_windowGeometry);
        windowColumn.AddChild(windowActions);
        window.AddChild(windowColumn);
        column.AddChild(window);

        var historical = new PanelContainer();
        Ui1Theme.ApplyPanel(historical, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var historicalColumn = new VBoxContainer();
        historicalColumn.AddThemeConstantOverride("separation", 10);
        _historicalTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _historicalHelp = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _deleteHistoricalData = new Button { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        Ui1Theme.ApplyButton(_deleteHistoricalData, Ui1ButtonRole.Secondary);
        historicalColumn.AddChild(_historicalTitle);
        historicalColumn.AddChild(_historicalHelp);
        historicalColumn.AddChild(_deleteHistoricalData);
        historical.AddChild(historicalColumn);
        column.AddChild(historical);
        _deleteHistoricalConfirmation = new ConfirmationDialog();
        AddChild(_deleteHistoricalConfirmation);
        _deleteHistoricalData.Pressed += () => {
            if (_text is null) return;
            _deletePending = true;
            _deleteHistoricalConfirmation.PopupCentered(new Vector2I(560, 180));
        };
        _deleteHistoricalConfirmation.Canceled += () => _deletePending=false;
        _deleteHistoricalConfirmation.Confirmed += () => {
            if (!_deletePending) return;
            _deletePending=false;
            DeleteHistoricalDataRequested?.Invoke();
        };

        scroll.AddChild(column);
        AddChild(scroll);

        _centerWindow.Pressed += () => CenterWindowRequested?.Invoke();
        _resetWindow.Pressed += () => ResetWindowRequested?.Invoke();
    }

    public event Action? CenterWindowRequested;
    public event Action<string>? LanguageRequested;
    public void SyncLanguage(string language) => _language.Select(language == "zh" ? 1 : language == "en" ? 2 : 0);
    public event Action? ResetWindowRequested;
    public event Action? DeleteHistoricalDataRequested;

    public AppPageKey PageKey { get; }
    public Control View => this;
    public void CancelTransientSurfacesForPageSwitch() { _deletePending=false;_deleteHistoricalConfirmation.Hide(); }
    public void ResetSurfacesForTopLevelClose() => CancelTransientSurfacesForPageSwitch();
    public bool TryHandleEscape()
    {
        if (!_deleteHistoricalConfirmation.Visible) return false;
        CancelTransientSurfacesForPageSwitch();return true;
    }

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _text = uiText;
        _languageTitle.Text = uiText.Get(Ui1TextKey.SettingsLanguage);
        _language.SetItemText(0, uiText.Get("ui1.settings.follow_game"));
        _pageTitle.Text = uiText.Get(Ui1TextKey.SettingsTitle);
        _subtitle.Text = uiText.Get(Ui1TextKey.SettingsSubtitle);
        _windowTitle.Text = uiText.Get(Ui1TextKey.SettingsWindow);
        _centerWindow.Text = uiText.Get(Ui1TextKey.SettingsCenterWindow);
        _resetWindow.Text = uiText.Get(Ui1TextKey.SettingsResetWindow);
        _historicalTitle.Text = uiText.Get(Ui1TextKey.SettingsHistoricalTitle);
        _historicalHelp.Text = uiText.Get(Ui1TextKey.SettingsHistoricalHelp);
        _deleteHistoricalData.Text = uiText.Get(Ui1TextKey.SettingsHistoricalDelete);
        _deleteHistoricalConfirmation.Title = _deleteHistoricalData.Text;
        _deleteHistoricalConfirmation.DialogText = uiText.Get(Ui1TextKey.SettingsHistoricalConfirm);
        _deleteHistoricalConfirmation.OkButtonText = _deleteHistoricalData.Text;
        _deleteHistoricalConfirmation.GetCancelButton().Text = uiText.Get(Ui1TextKey.SearchPresetCancel);
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
    }

    public void Sync(Vector2 position, Vector2 size, IUiTextProvider? uiText)
    {
        _windowGeometry.Text = uiText is null
            ? $"{size.X:0} × {size.Y:0} @ {position.X:0}, {position.Y:0}"
            : uiText.Format(Ui1TextKey.SettingsWindowGeometry, size.X, size.Y, position.X, position.Y);
    }
}
