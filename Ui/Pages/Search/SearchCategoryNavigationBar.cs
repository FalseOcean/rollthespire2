using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class SearchCategoryNavigationBar : HBoxContainer
{
    private readonly Dictionary<SearchCategoryKey, SearchCategoryTabButton> _buttons = new();
    private readonly ISearchCategoryTabIconProvider _iconProvider;
    private readonly Button _clearConditions;
    private IUiTextProvider? _uiText;
    private SearchCategoryKey _selectedCategory = SearchCategoryKey.Neow;
    private int _enabledConditionCount;
    private bool _running;

    public SearchCategoryNavigationBar(ISearchCategoryTabIconProvider iconProvider)
    {
        _iconProvider = iconProvider ?? throw new ArgumentNullException(nameof(iconProvider));
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 44),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };

        var tabs = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        tabs.AddThemeConstantOverride("separation", 4);

        AddTab(tabs, SearchCategoryKey.Neow);
        AddTab(tabs, SearchCategoryKey.Ancient);
        AddTab(tabs, SearchCategoryKey.Shop);
        AddTab(tabs, SearchCategoryKey.CombatReward);
        AddTab(tabs, SearchCategoryKey.Event);
        AddTab(tabs, SearchCategoryKey.Transformation);
        AddTab(tabs, SearchCategoryKey.BossAndMap);
        AddTab(tabs, SearchCategoryKey.Relic);

        scroll.AddChild(tabs);
        AddChild(scroll);

        _clearConditions = new Button { CustomMinimumSize = new Vector2(160, 38) };
        Ui1Theme.ApplyButton(_clearConditions, Ui1ButtonRole.Ghost);
        _clearConditions.Pressed += () => ClearConditionsRequested?.Invoke();
        AddChild(_clearConditions);

        RefreshSelectionStyles();
        RefreshClearButton();
    }

    public event Action<SearchCategoryKey>? CategoryChanged;
    public event Action? ClearConditionsRequested;

    public SearchCategoryKey SelectedCategory => _selectedCategory;

    public void ApplyLocalization(IUiTextProvider text)
    {
        _uiText = text;
        _buttons[SearchCategoryKey.Neow].SetLabel(text.Get(Ui1TextKey.SearchTabNeow));
        _buttons[SearchCategoryKey.Ancient].SetLabel(text.Get(Ui1TextKey.SearchTabAncient));
        _buttons[SearchCategoryKey.BossAndMap].SetLabel(text.Get(Ui1TextKey.SearchTabBossAndMap));
        _buttons[SearchCategoryKey.Relic].SetLabel(text.Get(Ui1TextKey.SearchTabRelic));
        _buttons[SearchCategoryKey.CombatReward].SetLabel(text.Get(Ui1TextKey.SearchTabCombatReward));
        _buttons[SearchCategoryKey.Event].SetLabel(text.Get(Ui1TextKey.SearchTabEvent));
        _buttons[SearchCategoryKey.Shop].SetLabel(text.Get(Ui1TextKey.SearchTabShop));
        _buttons[SearchCategoryKey.Transformation].SetLabel(text.LanguageCode.StartsWith("zh") ? "变牌组合" : "Transforms");
        RefreshClearButton();
    }

    public void SetEnabledConditionCount(int count)
    {
        _enabledConditionCount = Math.Max(0, count);
        RefreshClearButton();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        RefreshClearButton();
    }

    public void Select(SearchCategoryKey category, bool notify)
    {
        if (_selectedCategory == category)
        {
            RefreshSelectionStyles();
            return;
        }

        _selectedCategory = category;
        RefreshSelectionStyles();
        if (notify)
        {
            CategoryChanged?.Invoke(category);
        }
    }

    private void AddTab(HBoxContainer tabs, SearchCategoryKey category)
    {
        var button = new SearchCategoryTabButton()
        {
            ButtonPressed = category == _selectedCategory
        };
        SearchCategoryTabIconAsset icon = _iconProvider.Resolve(category);
        button.SetIcon(icon.Texture);

        SearchCategoryKey captured = category;
        button.Pressed += () => Select(captured, notify: true);
        _buttons[category] = button;
        tabs.AddChild(button);
    }

    private void RefreshSelectionStyles()
    {
        foreach ((SearchCategoryKey category, SearchCategoryTabButton button) in _buttons)
        {
            bool selected = category == _selectedCategory;
            button.ButtonPressed = selected;
            Ui1Theme.ApplyButton(
                button,
                selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
            button.SetSelectedVisual(selected);
        }
    }

    private void RefreshClearButton()
    {
        string format = _uiText?.Get(Ui1TextKey.SearchClearDraftCount)
            ?? "Clear {0} conditions";
        _clearConditions.Text = string.Format(format, _enabledConditionCount);
        _clearConditions.Disabled = _running || _enabledConditionCount == 0;
        _clearConditions.TooltipText = _clearConditions.Text;
    }
}

/// <summary>
/// Content-sized Search category tab. Each localized label owns only the width it
/// actually needs, while the surrounding ScrollContainer remains the final fallback
/// for unusually long localization rather than clipping ordinary English labels.
/// </summary>
internal sealed partial class SearchCategoryTabButton : Button
{
    private readonly TextureRect _icon;
    private readonly Label _label;

    public SearchCategoryTabButton()
    {
        ToggleMode = true;
        Text = string.Empty;
        CustomMinimumSize = new Vector2(80, 38);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        FocusMode = Control.FocusModeEnum.All;
        ClipContents = true;

        var content = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = true
        };
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        content.OffsetLeft = 14f;
        content.OffsetRight = -14f;
        content.AddThemeConstantOverride("separation", 6);

        _icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(18, 18),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };

        _label = Ui1Theme.Label(string.Empty, Ui1TextRole.Body);
        _label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.AutowrapMode = TextServer.AutowrapMode.Off;
        _label.ClipText = false;
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;

        content.AddChild(_icon);
        content.AddChild(_label);
        AddChild(content);
    }

    public void SetLabel(string text)
    {
        _label.Text = text ?? string.Empty;
        TooltipText = _label.Text;
        RefreshContentWidth();
    }

    public void SetIcon(Texture2D? texture)
    {
        bool valid = texture is not null && GodotObject.IsInstanceValid(texture);
        _icon.Texture = valid ? texture : null;
        _icon.Visible = valid;
        RefreshContentWidth();
    }

    private void RefreshContentWidth()
    {
        // Keep content-driven widths, but reserve deliberate horizontal breathing room.
        // Short labels stay compact, while longer localized labels grow naturally
        // without touching the button chrome or escaping the tab boundary.
        float textWidth = _label.GetMinimumSize().X;
        if (textWidth <= 0f && !string.IsNullOrEmpty(_label.Text))
        {
            // Conservative pre-layout fallback; the next localized label assignment
            // will recompute from the actual Label minimum once theme metrics exist.
            textWidth = _label.Text.Length * 8f;
        }
        float iconWidth = _icon.Visible ? 18f : 0f;
        float iconGap = _icon.Visible && !string.IsNullOrEmpty(_label.Text) ? 6f : 0f;
        float desired = 28f + iconWidth + iconGap + textWidth;
        CustomMinimumSize = new Vector2(Mathf.Max(80f, Mathf.Ceil(desired)), 38f);
    }

    public void SetSelectedVisual(bool selected)
    {
        _label.AddThemeColorOverride(
            "font_color",
            selected ? Ui1Theme.Palette.AccentWarm : Ui1Theme.Palette.TextPrimary);
        _icon.Modulate = new Color(1f, 1f, 1f, selected ? 0.9f : 0.72f);
    }
}
