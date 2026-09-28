using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Controls;

/// <summary>
/// Shared UI-only controls for sequence range, ordinal and appearance/exclusion mode.
/// The two button pairs are always in exactly one valid state.
/// </summary>
internal sealed partial class SequenceConditionControlStrip : HBoxContainer
{
    private readonly Button _exact;
    private readonly Button _first;
    private readonly OptionButton _number;
    private readonly Label _unit;
    private readonly Button _appears;
    private readonly Button _excluded;
    private bool _suppressChanged;
    private SearchSequenceRangeMode _rangeMode = SearchSequenceRangeMode.FirstN;
    private bool _isExcluded;

    public SequenceConditionControlStrip()
    {
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddThemeConstantOverride("separation", 8);

        _exact = SegmentButton(46f);
        _first = SegmentButton(46f);
        PanelContainer rangeGroup = CreateSegmentGroup(_first, _exact);

        _number = new OptionButton
        {
            CustomMinimumSize = new Vector2(66f, 38f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyOptionButton(_number);
        for (int value = 1; value <= 10; value++)
        {
            _number.AddItem(value.ToString(), value);
        }
        SelectNumber(1);

        _unit = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _unit.VerticalAlignment = VerticalAlignment.Center;
        _unit.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var numberGroup = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        numberGroup.AddThemeConstantOverride("separation", 4);
        numberGroup.AddChild(_number);
        numberGroup.AddChild(_unit);

        _appears = SegmentButton(66f);
        _excluded = SegmentButton(66f);
        PanelContainer matchGroup = CreateSegmentGroup(_appears, _excluded);

        _exact.Pressed += () => SetRangeMode(SearchSequenceRangeMode.ExactSlot, notify: true);
        _first.Pressed += () => SetRangeMode(SearchSequenceRangeMode.FirstN, notify: true);
        _appears.Pressed += () => SetExcluded(false, notify: true);
        _excluded.Pressed += () => SetExcluded(true, notify: true);
        _number.ItemSelected += _ => NotifyChanged();

        AddChild(rangeGroup);
        AddChild(numberGroup);
        AddChild(matchGroup);
        RefreshStyles();
    }

    public event Action? Changed;

    public SearchSequenceRangeMode RangeMode => _rangeMode;

    public int RangeValue => _number.ItemCount > 0 && _number.Selected >= 0
        ? _number.GetItemId(_number.Selected)
        : 1;

    public bool IsExcluded => _isExcluded;

    public void ApplyText(
        string exactText,
        string firstText,
        string unitText,
        string appearsText,
        string excludedText,
        string exactTooltip,
        string firstTooltip,
        string appearsTooltip,
        string excludedTooltip)
    {
        _exact.Text = exactText;
        _first.Text = firstText;
        _unit.Text = unitText;
        _appears.Text = appearsText;
        _excluded.Text = excludedText;
        _exact.TooltipText = exactTooltip;
        _first.TooltipText = firstTooltip;
        _appears.TooltipText = appearsTooltip;
        _excluded.TooltipText = excludedTooltip;
    }

    public void SetDisabled(bool disabled)
    {
        _exact.Disabled = disabled;
        _first.Disabled = disabled;
        _number.Disabled = disabled;
        _appears.Disabled = disabled;
        _excluded.Disabled = disabled;
        RefreshStyles();
    }

    public void ResetDefaults(bool notify, int firstNValue = 1)
    {
        _suppressChanged = true;
        _rangeMode = SearchSequenceRangeMode.FirstN;
        _isExcluded = false;
        SelectNumber(Math.Clamp(firstNValue, 1, 10));
        _suppressChanged = false;
        RefreshStyles();
        if (notify)
        {
            Changed?.Invoke();
        }
    }

    private void SetRangeMode(SearchSequenceRangeMode rangeMode, bool notify)
    {
        if (_rangeMode == rangeMode)
        {
            RefreshStyles();
            return;
        }
        _rangeMode = rangeMode;
        RefreshStyles();
        if (notify)
        {
            NotifyChanged();
        }
    }

    private void SetExcluded(bool excluded, bool notify)
    {
        if (_isExcluded == excluded)
        {
            RefreshStyles();
            return;
        }
        _isExcluded = excluded;
        RefreshStyles();
        if (notify)
        {
            NotifyChanged();
        }
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
        if (_number.ItemCount > 0)
        {
            _number.Select(0);
        }
    }

    private void RefreshStyles()
    {
        ApplySegmentButtonStyle(
            _first,
            _rangeMode == SearchSequenceRangeMode.FirstN,
            isLeft: true);
        ApplySegmentButtonStyle(
            _exact,
            _rangeMode == SearchSequenceRangeMode.ExactSlot,
            isLeft: false);
        ApplySegmentButtonStyle(
            _appears,
            !_isExcluded,
            isLeft: true);
        ApplySegmentButtonStyle(
            _excluded,
            _isExcluded,
            isLeft: false);
    }

    private void NotifyChanged()
    {
        if (!_suppressChanged)
        {
            Changed?.Invoke();
        }
    }

    private static PanelContainer CreateSegmentGroup(Button left, Button right)
    {
        var group = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(
                left.CustomMinimumSize.X + right.CustomMinimumSize.X + 3f,
                38f)
        };

        StyleBoxFlat frame = Ui1Theme.Surface(Ui1SurfaceRole.Input, 3f, 1);
        frame.ShadowSize = 0;
        Ui1Theme.SetMargins(frame, 0f, 0f, 0f, 0f);
        group.AddThemeStyleboxOverride("panel", frame);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddThemeConstantOverride("separation", 0);

        var divider = new VSeparator
        {
            CustomMinimumSize = new Vector2(1f, 30f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        Ui1Theme.ApplySeparator(divider);

        row.AddChild(left);
        row.AddChild(divider);
        row.AddChild(right);
        group.AddChild(row);
        return group;
    }

    private static void ApplySegmentButtonStyle(Button button, bool selected, bool isLeft)
    {
        Ui1Palette palette = Ui1Theme.Palette;
        Color transparent = new(0f, 0f, 0f, 0f);
        Color selectedBackground = new(
            palette.AccentWarm.R,
            palette.AccentWarm.G,
            palette.AccentWarm.B,
            0.17f);
        Color selectedHover = new(
            palette.AccentWarm.R,
            palette.AccentWarm.G,
            palette.AccentWarm.B,
            0.24f);
        Color unselectedHover = new(
            palette.CardHover.R,
            palette.CardHover.G,
            palette.CardHover.B,
            0.82f);

        StyleBoxFlat normal = SegmentBox(
            selected ? selectedBackground : transparent,
            isLeft);
        StyleBoxFlat hover = SegmentBox(
            selected ? selectedHover : unselectedHover,
            isLeft);
        StyleBoxFlat pressed = SegmentBox(selectedHover, isLeft);
        StyleBoxFlat focus = SegmentBox(
            selected ? selectedBackground : transparent,
            isLeft);
        StyleBoxFlat disabled = SegmentBox(
            selected
                ? new Color(selectedBackground.R, selectedBackground.G, selectedBackground.B, 0.42f)
                : transparent,
            isLeft);

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.AddThemeColorOverride(
            "font_color",
            selected ? palette.AccentWarm : palette.TextPrimary);
        button.AddThemeColorOverride("font_hover_color", palette.TextPrimary);
        button.AddThemeColorOverride("font_pressed_color", palette.AccentWarm);
        button.AddThemeColorOverride(
            "font_focus_color",
            selected ? palette.AccentWarm : palette.TextPrimary);
        button.AddThemeColorOverride("font_disabled_color", palette.TextDisabled);
        button.AddThemeFontSizeOverride("font_size", 15);
        button.FocusMode = FocusModeEnum.All;
    }

    private static StyleBoxFlat SegmentBox(Color background, bool isLeft)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            BorderWidthLeft = 0,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0,
            CornerRadiusTopLeft = isLeft ? 2 : 0,
            CornerRadiusBottomLeft = isLeft ? 2 : 0,
            CornerRadiusTopRight = isLeft ? 0 : 2,
            CornerRadiusBottomRight = isLeft ? 0 : 2
        };
        Ui1Theme.SetMargins(box, 9f, 7f, 9f, 7f);
        return box;
    }

    private static Button SegmentButton(float width) => new()
    {
        CustomMinimumSize = new Vector2(width, 36f),
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        FocusMode = FocusModeEnum.All
    };
}
