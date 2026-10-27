using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    private bool _restoring;

    private void Build()
    {
        _rail = Surface(this, "ConsoleRail", new(188, 760));
        var railBody = Body(_rail, 10);
        var railHeader = Row(railBody);
        var title = _p.Label("RT2", 24, heading: true); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; railHeader.AddChild(title);
        CloseButton = Button(railHeader, "×", () => CloseRequested?.Invoke(), "ConsoleClose", allowWhileBusy: true);
        CloseButton.CustomMinimumSize = new(36, 36);
        _travel = Button(railBody, "", () => Execute(ConsoleActions.ToggleTravelAsync), "ConsoleTravel");
        _godmode = Button(railBody, "", () => Execute(ConsoleActions.ToggleGodModeAsync), "ConsoleGodmode");
        Button(railBody, T("console.win.combat"), () => Execute(ConsoleActions.WinAsync), "ConsoleWin");
        _goldLabel = _p.Label("", 16, true); railBody.AddChild(_goldLabel);
        _goldAmount = new SpinBox { Name = "ConsoleGoldAmount", MinValue = 1, MaxValue = 99999, Value = 100, Step = 1,
            Prefix = "+/− ", CustomMinimumSize = new(0, 34) };
        railBody.AddChild(_goldAmount);
        var goldRow = Row(railBody);
        _goldAdd = Button(goldRow, "+", () => Execute(() => ConsoleActions.AdjustGoldAsync((int)_goldAmount.Value)), "ConsoleGoldAdd");
        _goldRemove = Button(goldRow, "−", () => Execute(() => ConsoleActions.AdjustGoldAsync(-(int)_goldAmount.Value)), "ConsoleGoldRemove");
        _goldAdd.SizeFlagsHorizontal = _goldRemove.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        railBody.AddChild(new HSeparator());
        var categoryScroll = new ScrollContainer { Name = "ConsoleCategoryScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        railBody.AddChild(categoryScroll);
        var categories = new VBoxContainer { Name = "ConsoleTabs", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        categories.AddThemeConstantOverride("separation", 5); categoryScroll.AddChild(categories);
        foreach (int category in new[] { 0, 1, 2, 3, 5, 4 })
        {
            int tab = category;
            var button = Button(categories, "", () => OpenCategory(tab), "ConsoleCategory" + tab);
            button.CustomMinimumSize = new(0, 46); button.AccessibilityName = CategoryTitle(tab);
            Texture2D? icon = CategoryIcon(tab);
            if (icon is not null) button.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = icon, Position = new(7, 7), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore, Size = new(32, 32) });
            var label = _p.Label(CategoryTitle(tab), 17); label.Position = new(46, 4);
            label.VerticalAlignment = VerticalAlignment.Center; label.Size = new(112, 38); button.AddChild(label); _tabs[tab] = button;
        }
        _status = _p.Label(T("console.choose.a.category"), 15, true);
        _status.Name = "ConsoleStatus"; _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var statusScroll = new ScrollContainer { Name = "ConsoleStatusScroll", CustomMinimumSize = new(0, 70), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        railBody.AddChild(statusScroll); statusScroll.AddChild(_status);

        _content = Surface(this, "ConsoleContent", new(1008, 760)); _content.Position = new(196, 0); _content.Hide();
        var contentBody = Body(_content, 14);
        var contentHeader = Row(contentBody);
        Button(contentHeader, "‹", CollapseContent, "ConsoleCollapse", allowWhileBusy: true);
        _contentTitle = _p.Label("", 23, heading: true); _contentTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; contentHeader.AddChild(_contentTitle);
        Button(contentHeader, T("console.refresh"), RefreshHoldings, "ConsoleRefresh");
        _viewMode = Dropdown("ConsoleDisplayMode");
        _viewMode.CustomMinimumSize = new(178, 36);
        _viewMode.AddItem(T("console.text.image")); _viewMode.AddItem(T("console.compact.icons")); contentHeader.AddChild(_viewMode);
        _viewMode.ItemSelected += _ => { if (!_restoring) { RememberDisplayMode(); RenderGrid(); } };
        var split = Row(contentBody); split.SizeFlagsVertical = SizeFlags.ExpandFill;
        var filterScroll = new ScrollContainer { Name = "ConsoleFilters", CustomMinimumSize = new(224, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        split.AddChild(filterScroll);
        _filters = new VBoxContainer { CustomMinimumSize = new(206, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _filters.AddThemeConstantOverride("separation", 10); filterScroll.AddChild(_filters);
        _search = new LineEdit { Name = "ConsoleSearch", PlaceholderText = T("console.search.name.or.id"), CustomMinimumSize = new(0, 38) };
        _search.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface)); _search.AddThemeStyleboxOverride("focus", _p.FocusRing()); _filters.AddChild(_search);
        _search.TextChanged += _ => { if (!_restoring) { _page = 0; RenderGrid(true); } };
        _holdings = new CheckButton { Name = "ConsoleHoldings", Text = T("console.holdings.remove") }; _filters.AddChild(_holdings);
        _holdings.Toggled += _ => { if (!_restoring) { ResetSelection(); RefreshHoldings(); } };
        _cardOptions = Section(_filters);
        _pile = Dropdown("ConsoleCardPile"); _pile.AddItem(T("console.deck.permanent")); _pile.AddItem(T("console.hand.combat")); _cardOptions.AddChild(_pile);
        _pile.ItemSelected += _ => { if (!_restoring) { ResetSelection(); RefreshHoldings(); } };
        _upgraded = new CheckButton { Name = "ConsoleCardUpgraded", Text = T("console.add.upgraded") }; _cardOptions.AddChild(_upgraded);
        _quantityOptions = Section(_filters); _quantityOptions.AddChild(_p.Label(T("console.quantity.per.item"), 17, true));
        _quantity = new SpinBox { Name = "ConsoleQuantity", MinValue = 1, MaxValue = 999, Step = 1, Value = 1, CustomMinimumSize = new(0, 36) }; _quantityOptions.AddChild(_quantity);
        _potionOptions = Section(_filters); _potionOptions.AddChild(_p.Label(T("console.replace.slot"), 17, true));
        _potionSlot = Dropdown("ConsolePotionSlot"); _potionOptions.AddChild(_potionSlot);
        _potionSlot.ItemSelected += _ => { if (!_restoring) UpdateSelectionStatus(); };
        _replace = Button(_potionOptions, T("console.replace.with.selection"), ReplacePotion, "ConsoleReplacePotion");
        _cardFilterRail = Section(_filters); _cardFilterRail.Name = "ConsoleCardFilters";
        _rarityFilter = Section(_filters); _rarityFilter.Name = "ConsoleRarityFilter";
        _actFilter = FilterDropdown(_filters, "ConsoleActFilter");
        _actChoices = Section(_filters); _actChoices.Name = "ConsoleActChoices";
        _roomFilter = FilterDropdown(_filters, "ConsoleRoomFilter");
        _roomChoices = Section(_filters); _roomChoices.Name = "ConsoleFightTypeChoices";
        Button(_filters, T("console.clear.filters"), ClearFilters, "ConsoleClearFilters");
        BuildCardFilterButtons();

        var results = new VBoxContainer { Name = "ConsoleResults", SizeFlagsHorizontal = SizeFlags.ExpandFill }; split.AddChild(results);
        _scroll = new ScrollContainer { Name = "ConsoleCatalogScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; results.AddChild(_scroll);
        _grid = new HFlowContainer { Name = "ConsoleCatalog", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 8); _grid.AddThemeConstantOverride("v_separation", 8); _scroll.AddChild(_grid);
        BuildAncientOptions(results);
        var footer = Row(results);
        _selection = _p.Label("", 17); _selection.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(_selection);
        _previous = Button(footer, "‹", () => { _page--; RenderGrid(true); });
        _paging = _p.Label("", 16); footer.AddChild(_paging);
        _next = Button(footer, "›", () => { _page++; RenderGrid(true); });
        Button(results, T("console.clear.selection"), () => { ResetSelection(); RenderGrid(); }, "ConsoleClearSelection");
        _apply = Button(results, "", ApplySelection, "ConsoleApply");
        _remove = Button(results, T("console.remove.selected.instances"), RemoveSelection, "ConsoleRemove");
    }

    private Panel Surface(Node parent, string name, Vector2 size)
    {
        var panel = new Panel { Name = name, Size = size, MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Canvas, _p.Line, 1)); parent.AddChild(panel); return panel;
    }
    private static VBoxContainer Body(Control parent, int padding)
    {
        var margin = new MarginContainer(); parent.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, padding);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 7); margin.AddChild(body); return body;
    }
    private static VBoxContainer Section(Node parent)
    { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6); parent.AddChild(box); return box; }
    private OptionButton Dropdown(string name)
    { return new OptionButton { Name = name, FitToLongestItem = false, ClipText = true, CustomMinimumSize = new(0, 36) }; }
    private OptionButton FilterDropdown(Node parent, string name)
    {
        var choice = Dropdown(name); parent.AddChild(choice);
        choice.ItemSelected += _ => { if (!_restoring) { _page = 0; RenderGrid(true); } }; return choice;
    }
    private string CategoryTitle(int tab) => tab switch
    { 0 => T("console.cards"), 1 => T("console.relics"), 2 => T("console.potions"), 3 => T("console.ancients"), 5 => T("console.events"), _ => T("console.fights") };
    private Texture2D? CategoryIcon(int tab)
    {
        if (tab != 2)
        {
            var category = tab switch { 0 => RolltheSpire2.Ui.Pages.Search.SearchCategoryKey.CombatReward,
                1 => RolltheSpire2.Ui.Pages.Search.SearchCategoryKey.Relic, 3 => RolltheSpire2.Ui.Pages.Search.SearchCategoryKey.Ancient,
                5 => RolltheSpire2.Ui.Pages.Search.SearchCategoryKey.Event, _ => RolltheSpire2.Ui.Pages.Search.SearchCategoryKey.BossIdentity };
            return _categoryIcons.Resolve(category).Texture;
        }
        var potion = ModelDb.AllPotions.First();
        return _icons.Resolve(new(potion.Id.Category, potion.Id.Entry), RolltheSpire2.Presentation.ContentNames.GameContentKind.Potion,
            RolltheSpire2.Ui.Icons.IconVariant.RelicLarge).Texture;
    }

    internal void OpenCategory(int category)
    {
        if (_busy || !_tabs.ContainsKey(category)) return;
        InvalidateConsoleScrollRestore();
        if (_contentOpen && _tab == category) { CollapseContent(); return; }
        if (!_contentOpen && _hasCategory && _tab == category)
        { _contentOpen = true; _content.Show(); RefreshControls(); LayoutChanged?.Invoke(); return; }
        _tab = category; _contentOpen = true; _content.Show(); _restoring = true;
        ApplyCategoryDisplayPreference();
        _hasCategory = true;
        _holdings.ButtonPressed = false; _selected.Clear(); _selectedSlots.Clear(); _search.Text = "";
        _potionHoldings = ConsoleActions.GetLocalPlayer()?.PotionSlots.ToArray() ?? [];
        PrepareCategoryFilters();
        _restoring = false; _page = 0; RefreshControls(); ResetAncientOptions(); RenderGrid(true); LayoutChanged?.Invoke();
    }
    internal void CollapseContent()
    {
        if (!_contentOpen) return;
        DismissContentTooltip();
        InvalidateConsoleScrollRestore();
        _contentOpen = false; _content.Hide();
        foreach (var button in _tabs.Values) _p.SetModeActive(button, false);
        LayoutChanged?.Invoke();
    }
    internal bool HandleBack()
    { if (!_contentOpen) return false; CollapseContent(); return true; }
}
