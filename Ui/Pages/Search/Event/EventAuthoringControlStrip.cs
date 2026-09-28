using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Event;

/// <summary>Player-facing Event authoring controls. Event Search authors prefix-only ranges.</summary>
internal sealed partial class EventAuthoringControlStrip : HBoxContainer
{
    private const int DefaultRangeValue = 3;
    private readonly Label _prefix;
    private readonly OptionButton _number;
    private readonly Label _unit;
    private readonly Button _appears;
    private readonly Button _excluded;
    private bool _isExcluded;
    private bool _suppressChanged;

    public EventAuthoringControlStrip()
    {
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddThemeConstantOverride("separation", 10);

        var range = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        range.AddThemeConstantOverride("separation", 5);
        _prefix = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _prefix.VerticalAlignment = VerticalAlignment.Center;
        _number = new OptionButton
        {
            CustomMinimumSize = new Vector2(64f, 38f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyOptionButton(_number);
        for (int value = 1; value <= 10; value++) _number.AddItem(value.ToString(), value);
        _unit = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _unit.VerticalAlignment = VerticalAlignment.Center;
        range.AddChild(_prefix);
        range.AddChild(_number);
        range.AddChild(_unit);

        var modes = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        modes.AddThemeConstantOverride("separation", 4);
        _appears = ModeButton();
        _excluded = ModeButton();
        _appears.Pressed += () => SetExcluded(false, true);
        _excluded.Pressed += () => SetExcluded(true, true);
        modes.AddChild(_appears);
        modes.AddChild(_excluded);

        _number.ItemSelected += _ => NotifyChanged();
        AddChild(range);
        AddChild(modes);
        SelectNumber(DefaultRangeValue);
        RefreshStyles();
    }

    public event Action? Changed;
    public int RangeValue => _number.ItemCount > 0 && _number.Selected >= 0
        ? _number.GetItemId(_number.Selected)
        : 1;
    public bool IsExcluded => _isExcluded;
    public bool NeedsReset => RangeValue != DefaultRangeValue || IsExcluded;

    public void ApplyText(
        string prefixText,
        string unitText,
        string appearsText,
        string excludedText,
        string appearsTooltip,
        string excludedTooltip)
    {
        _prefix.Text = prefixText;
        _unit.Text = unitText;
        _appears.Text = appearsText;
        _excluded.Text = excludedText;
        _appears.TooltipText = appearsTooltip;
        _excluded.TooltipText = excludedTooltip;
    }

    public void SetDisabled(bool disabled)
    {
        _number.Disabled = disabled;
        _appears.Disabled = disabled;
        _excluded.Disabled = disabled;
        RefreshStyles();
    }

    public void ResetDefaults(bool notify)
    {
        _suppressChanged = true;
        _isExcluded = false;
        SelectNumber(DefaultRangeValue);
        _suppressChanged = false;
        RefreshStyles();
        if (notify) Changed?.Invoke();
    }

    private void SetExcluded(bool excluded, bool notify)
    {
        _isExcluded = excluded;
        RefreshStyles();
        if (notify) NotifyChanged();
    }

    private void RefreshStyles()
    {
        Ui1Theme.ApplyButton(_appears, _isExcluded ? Ui1ButtonRole.Secondary : Ui1ButtonRole.NavigationSelected);
        Ui1Theme.ApplyButton(_excluded, _isExcluded ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
    }

    private void SelectNumber(int value)
    {
        for (int index = 0; index < _number.ItemCount; index++)
        {
            if (_number.GetItemId(index) == value)
            {
                _number.Select(index);
                return;
            }
        }
    }

    private void NotifyChanged()
    {
        if (!_suppressChanged) Changed?.Invoke();
    }

    private static Button ModeButton() => new()
    {
        CustomMinimumSize = new Vector2(72f, 38f),
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        FocusMode = FocusModeEnum.All
    };
}
