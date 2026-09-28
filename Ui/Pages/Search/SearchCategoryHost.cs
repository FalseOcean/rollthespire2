using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Ancient;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Pages.Search.Shop;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class SearchCategoryHost : PanelContainer
{
    private readonly Dictionary<SearchCategoryKey, Control> _pages = new();
    private readonly Dictionary<SearchCategoryKey, SearchCategoryPlaceholderPage> _placeholders = new();
    private readonly Control _pageStack;
    private readonly AnchoredTooltipHost _tooltipHost;
    private SearchCategoryKey _selectedCategory = SearchCategoryKey.Neow;

    public SearchCategoryHost(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        ICardPickerFilterIconProvider cardPickerFilterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        _tooltipHost = tooltipHost;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0, 280);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 16f);

        _pageStack = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 248)
        };
        AddChild(_pageStack);

        NeowPage = new NeowFilterPage(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost)
        {
            Visible = true
        };
        _pages[SearchCategoryKey.Neow] = NeowPage;
        _pageStack.AddChild(NeowPage);
        NeowPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        AncientPage = new AncientFilterPage(icons, characterPoolIcons, tooltipHost)
        {
            Visible = false
        };
        _pages[SearchCategoryKey.Ancient] = AncientPage;
        _pageStack.AddChild(AncientPage);
        AncientPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        BossMapPage = new BossMapFilterPage(icons)
        {
            Visible = false
        };
        _pages[SearchCategoryKey.BossAndMap] = BossMapPage;
        _pageStack.AddChild(BossMapPage);
        BossMapPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        RelicPage = new RelicSequenceFilterPage(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost)
        {
            Visible = false
        };
        _pages[SearchCategoryKey.Relic] = RelicPage;
        _pageStack.AddChild(RelicPage);
        RelicPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        CombatRewardPage = new CombatRewardFilterPage(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost)
        {
            Visible = false
        };
        _pages[SearchCategoryKey.CombatReward] = CombatRewardPage;
        _pageStack.AddChild(CombatRewardPage);
        CombatRewardPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        EventPage = new EventSequenceFilterPage(
            new EventThumbnailProvider(),
            icons,
            characterPoolIcons,
            cardPickerFilterIcons,
            tooltipHost)
        {
            Visible = false
        };
        _pages[SearchCategoryKey.Event] = EventPage;
        _pageStack.AddChild(EventPage);
        EventPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        ShopPage = new ShopColorlessFilterPage(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost)
        {
            Visible = false
        };
        _pages[SearchCategoryKey.Shop] = ShopPage;
        _pageStack.AddChild(ShopPage);
        ShopPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        TransformationPage = new(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost) { Visible = false };
        _pages[SearchCategoryKey.Transformation] = TransformationPage;
        _pageStack.AddChild(TransformationPage);
        TransformationPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        TransformationPage.OpeningRequested += NeowPage.SetTransformationOpening;
        NeowPage.Changed += UpdateCompositeOwnership;
        TransformationPage.Changed += UpdateCompositeOwnership;
        Select(SearchCategoryKey.Neow);
    }

    public SearchCategoryKey SelectedCategory => _selectedCategory;
    public NeowFilterPage NeowPage { get; }
    public AncientFilterPage AncientPage { get; }
    public BossMapFilterPage BossMapPage { get; }
    public RelicSequenceFilterPage RelicPage { get; }
    public CombatRewardFilterPage CombatRewardPage { get; }
    public EventSequenceFilterPage EventPage { get; }
    public ShopColorlessFilterPage ShopPage { get; }
    public TransformationAggregatePage TransformationPage { get; }

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        NeowPage.ApplyLocalization(text, names);
        AncientPage.ApplyLocalization(text, names);
        BossMapPage.ApplyLocalization(text, names);
        RelicPage.ApplyLocalization(text, names);
        CombatRewardPage.ApplyLocalization(text, names);
        EventPage.ApplyLocalization(text, names);
        ShopPage.ApplyLocalization(text, names);
        TransformationPage.ApplyLocalization(text, names);
    }

    public void Select(SearchCategoryKey category)
    {
        _tooltipHost.Dismiss();
        bool changed = _selectedCategory != category;
        if (changed)
        {
            // Category switches are local workspace navigation. Any picker or
            // confirmation belonging to the outgoing category is transient and
            // must not reappear when that category becomes visible again.
            TryCancelSelectedTransientSurface();
        }

        _selectedCategory = category;
        foreach ((SearchCategoryKey key, Control page) in _pages)
        {
            page.Visible = key == category;
        }
        UpdateCompositeOwnership();
        if (changed)
        {
            RuntimeLog.Ui($"Search category activated: {category}");
        }
    }

    public bool TryCancelSelectedTransientSurface() => _selectedCategory switch
    {
        SearchCategoryKey.Neow => NeowPage.TryCancelTransientSurface(),
        SearchCategoryKey.Ancient => AncientPage.TryCancelTransientSurface(),
        SearchCategoryKey.Relic => RelicPage.TryCancelTransientSurface(),
        SearchCategoryKey.CombatReward => CombatRewardPage.TryCancelTransientSurface(),
        SearchCategoryKey.Event => EventPage.TryCancelTransientSurface(),
        SearchCategoryKey.Shop => ShopPage.TryCancelTransientSurface(),
        SearchCategoryKey.Transformation => TransformationPage.TryCancelTransientSurface(),
        _ => false
    };

    public void CancelAllTransientSurfaces()
    {
        TransformationPage.TryCancelTransientSurface();
        NeowPage.TryCancelTransientSurface();
        AncientPage.TryCancelTransientSurface();
        RelicPage.TryCancelTransientSurface();
        CombatRewardPage.TryCancelTransientSurface();
        EventPage.TryCancelTransientSurface();
        ShopPage.TryCancelTransientSurface();
    }

    internal void UpdateCompositeOwnership()
    {
        TransformationPage.ReadNeow(NeowPage.ReadOpeningSelection());
        NeowPage.Visible = _selectedCategory == SearchCategoryKey.Neow;
        EventPage.SetMorphicManaged(TransformationPage.UsesMorphic);
    }

    private void AddPage(SearchCategoryKey category)
    {
        var page = new SearchCategoryPlaceholderPage
        {
            Visible = category == _selectedCategory
        };
        _pages[category] = page;
        _placeholders[category] = page;
        _pageStack.AddChild(page);
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }
}

internal sealed partial class SearchCategoryPlaceholderPage : MarginContainer
{
    private readonly Label _title;

    public SearchCategoryPlaceholderPage()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2);
        AddThemeConstantOverride("margin_top", 2);
        AddThemeConstantOverride("margin_right", 2);
        AddThemeConstantOverride("margin_bottom", 2);

        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.HorizontalAlignment = HorizontalAlignment.Left;
        _title.VerticalAlignment = VerticalAlignment.Top;
        AddChild(_title);
    }

    public void SetTitle(string title) => _title.Text = title;
}
