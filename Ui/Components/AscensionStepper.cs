using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class AscensionStepper : HBoxContainer
{
    private readonly Button _decrement;
    private readonly Label _value;
    private readonly Button _increment;
    private int _current;
    private int _minimum;
    private int _maximum;
    private bool _enabled = true;

    public AscensionStepper()
    {
        AddThemeConstantOverride("separation", 5);
        _decrement = new Button { Text = "−", CustomMinimumSize = new Vector2(34, 34) };
        _value = Ui1Theme.Label("A0", Ui1TextRole.CardTitle);
        _value.CustomMinimumSize = new Vector2(52, 34);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        _increment = new Button { Text = "+", CustomMinimumSize = new Vector2(34, 34) };
        Ui1Theme.ApplyButton(_decrement, Ui1ButtonRole.Secondary);
        Ui1Theme.ApplyButton(_increment, Ui1ButtonRole.Secondary);
        _decrement.Pressed += () => SetValue(_current - 1, notify: true);
        _increment.Pressed += () => SetValue(_current + 1, notify: true);
        AddChild(_decrement);
        AddChild(_value);
        AddChild(_increment);
    }

    public event Action<int>? ValueChanged;
    public int Value => _current;

    public void Configure(int minimum, int maximum, int value)
    {
        _minimum = minimum;
        _maximum = Math.Max(minimum, maximum);
        SetValue(value, notify: false);
    }

    public void SetValue(int value, bool notify)
    {
        int clamped = Math.Clamp(value, _minimum, _maximum);
        bool changed = clamped != _current;
        _current = clamped;
        _value.Text = $"A{_current}";
        RefreshDisabledState();
        if (notify && changed)
        {
            ValueChanged?.Invoke(_current);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        RefreshDisabledState();
    }

    private void RefreshDisabledState()
    {
        _decrement.Disabled = !_enabled || _current <= _minimum;
        _increment.Disabled = !_enabled || _current >= _maximum;
    }
}
