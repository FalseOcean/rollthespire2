using System.Globalization;
using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class MapQuantityInput : HBoxContainer
{
    private readonly Button _minus;
    private readonly LineEdit _input;
    private readonly Button _plus;
    private readonly WorkspacePalette _palette;
    private int _value;
    private bool _enabled;
    private bool _updating;

    internal int Value
    {
        get => _value;
        set => SetValue(value);
    }
    internal event Action<int>? ValueChanged;

    internal MapQuantityInput(WorkspacePalette palette, int value, bool enabled)
    {
        _palette = palette;
        AddThemeConstantOverride("separation", 4);
        _minus = StepButton("Minus", "−"); AddChild(_minus);
        _input = new LineEdit { Name = "Input", Alignment = HorizontalAlignment.Center,
            ExpandToTextLength = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        MainPanelTheme.ApplyFont(_input);
        _input.AddThemeFontSizeOverride("font_size", 15);
        StyleBoxFlat box = palette.Box(palette.Surface);
        box.ContentMarginLeft = box.ContentMarginRight = 6;
        box.ContentMarginTop = box.ContentMarginBottom = 3;
        _input.AddThemeStyleboxOverride("normal", box);
        _input.AddThemeStyleboxOverride("read_only", box);
        _input.AddThemeStyleboxOverride("focus", palette.FocusRing());
        float digits = _input.GetThemeFont("font").GetStringSize(int.MaxValue.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment.Left, -1, 15).X;
        _input.CustomMinimumSize = new(Mathf.Ceil(digits + 12), 38);
        AddChild(_input);
        _plus = StepButton("Plus", "+"); AddChild(_plus);
        _enabled = enabled;
        SetValue(value, notify: false);
        _minus.Pressed += () => { if (_enabled && _value > 0) SetValue(_value - 1); };
        _plus.Pressed += () => { if (_enabled && _value < int.MaxValue) SetValue(_value + 1); };
        _input.TextChanged += OnTextChanged;
        _input.TextSubmitted += _ => CommitText();
        _input.FocusExited += CommitText;
    }

    private Button StepButton(string name, string title)
    {
        var button = new Button { Name = name, Text = title, CustomMinimumSize = new(28, 38) };
        MainPanelTheme.ApplyFont(button);
        button.AddThemeFontSizeOverride("font_size", 18);
        foreach ((string state, string fill) in new[] { ("normal", _palette.Surface), ("hover", _palette.Hover),
                     ("pressed", _palette.SelectedSurface), ("disabled", _palette.Surface) })
        {
            StyleBoxFlat box = _palette.Box(fill);
            box.ContentMarginLeft = box.ContentMarginRight = 4;
            box.ContentMarginTop = box.ContentMarginBottom = 3;
            button.AddThemeStyleboxOverride(state, box);
        }
        button.AddThemeStyleboxOverride("focus", _palette.FocusRing());
        return button;
    }

    private void OnTextChanged(string value)
    {
        if (_updating || !_enabled) return;
        bool valid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed);
        SetInputValidity(valid);
        if (!valid) return; // Empty or unfinished text does not rewrite the condition.
        bool changed = _value != parsed;
        _value = parsed;
        RefreshEnabled();
        if (changed) ValueChanged?.Invoke(_value);
    }

    private void CommitText()
    {
        // Keep the last valid value. Invalid transient input is visibly marked while editing.
        _updating = true;
        _input.Text = _value.ToString(CultureInfo.InvariantCulture);
        _updating = false;
        SetInputValidity(true);
    }

    private void SetValue(int value, bool notify = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        bool changed = _value != value;
        _value = value;
        CommitText(); RefreshEnabled();
        if (changed && notify) ValueChanged?.Invoke(value);
    }

    private void RefreshEnabled()
    {
        _minus.Disabled = !_enabled || _value == 0;
        _plus.Disabled = !_enabled || _value == int.MaxValue;
        _input.Editable = _enabled;
        _input.AddThemeColorOverride("font_color", _palette.Color(_enabled ? _palette.Text : _palette.Disabled));
    }

    private void SetInputValidity(bool valid)
    {
        StyleBoxFlat box = _palette.Box(_palette.Surface);
        box.ContentMarginLeft = box.ContentMarginRight = 6;
        box.ContentMarginTop = box.ContentMarginBottom = 3;
        if (!valid)
        {
            box.BorderColor = _palette.Error;
            box.BorderWidthLeft = box.BorderWidthRight = box.BorderWidthTop = box.BorderWidthBottom = 1;
        }
        _input.AddThemeStyleboxOverride("normal", box);
        _input.AddThemeColorOverride("font_color", valid ? _palette.Color(_enabled ? _palette.Text : _palette.Disabled) : _palette.Error);
    }
}
