using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Capabilities;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Events;

internal sealed partial class EventBrowserPage : MarginContainer, IAppPage
{
    private readonly Label _pageTitle;
    private readonly Label _subtitle;
    private readonly Label _searchAreaTitle;
    private readonly CapabilityStatePanel _catalogState;
    private readonly PanelContainer _details;
    private readonly Label _detailsTitle;
    private readonly Label _detailsMessage;

    public EventBrowserPage()
    {
        PageKey = AppPageKey.Events;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 14);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _subtitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _searchAreaTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _catalogState = new CapabilityStatePanel();
        _details = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(_details, Ui1SurfaceRole.Page, 4f, 1, 18f);
        var detailsColumn = new VBoxContainer();
        detailsColumn.AddThemeConstantOverride("separation", 8);
        _detailsTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _detailsMessage = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        detailsColumn.AddChild(_detailsTitle);
        detailsColumn.AddChild(_detailsMessage);
        detailsColumn.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _details.AddChild(detailsColumn);
        column.AddChild(_pageTitle);
        column.AddChild(_subtitle);
        column.AddChild(_searchAreaTitle);
        column.AddChild(_catalogState);
        column.AddChild(_details);
        AddChild(column);
    }

    public AppPageKey PageKey { get; }
    public Control View => this;

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _pageTitle.Text = uiText.Get(Ui1TextKey.EventBrowserTitle);
        _subtitle.Text = uiText.Get(Ui1TextKey.EventBrowserSubtitle);
        _searchAreaTitle.Text = uiText.Get(Ui1TextKey.EventBrowserSearchArea);
        _catalogState.Bind(Ui1CapabilityCatalog.EventCatalog, uiText);
        _detailsTitle.Text = uiText.Get(Ui1TextKey.EventBrowserDetailsTitle);
        _detailsMessage.Text = uiText.Get(Ui1TextKey.EventBrowserNoCatalog);
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
    }
}
