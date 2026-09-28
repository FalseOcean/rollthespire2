using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages;

internal sealed partial class NotMigratedPage : MarginContainer, IAppPage
{
    private readonly string _pageTitleKey;
    private readonly Label _pageTitle;
    private readonly Label _stateTitle;
    private readonly Label _message;
    private readonly Label _honesty;
    private readonly Button _goAnalysis;

    public NotMigratedPage(AppPageKey pageKey, string pageTitleKey)
    {
        PageKey = pageKey;
        _pageTitleKey = pageTitleKey;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 18);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        var state = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        Ui1Theme.ApplyPanel(state, Ui1SurfaceRole.NotMigrated, 4f, 1, 28f);
        var stateRow = new HBoxContainer();
        stateRow.AddThemeConstantOverride("separation", 18);
        var icon = Ui1Theme.Label("◇", Ui1TextRole.Warning);
        icon.CustomMinimumSize = new Vector2(58, 58);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var textColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        textColumn.AddThemeConstantOverride("separation", 8);
        _stateTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _message = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        _honesty = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _goAnalysis = new Button();
        Ui1Theme.ApplyButton(_goAnalysis, Ui1ButtonRole.Primary);
        _goAnalysis.Pressed += () => NavigateToAnalysisRequested?.Invoke();
        textColumn.AddChild(_stateTitle);
        textColumn.AddChild(_message);
        textColumn.AddChild(_honesty);
        textColumn.AddChild(_goAnalysis);
        stateRow.AddChild(icon);
        stateRow.AddChild(textColumn);
        state.AddChild(stateRow);
        column.AddChild(_pageTitle);
        column.AddChild(state);
        AddChild(column);
    }

    public event Action? NavigateToAnalysisRequested;

    public AppPageKey PageKey { get; }
    public Control View => this;

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _pageTitle.Text = uiText.Get(_pageTitleKey);
        _stateTitle.Text = uiText.Get(Ui1TextKey.NotMigratedTitle);
        _message.Text = uiText.Get(Ui1TextKey.NotMigratedMessage);
        _honesty.Text = uiText.Get(Ui1TextKey.NotMigratedHonesty);
        _goAnalysis.Text = uiText.Get(Ui1TextKey.GoToAnalysis);
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
    }
}
