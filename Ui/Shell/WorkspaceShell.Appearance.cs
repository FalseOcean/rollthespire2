using Godot;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private readonly OptionButton _colorScheme = new() { Name = "SettingsColorScheme", CustomMinimumSize = new(300, 44), FitToLongestItem = false };
    private readonly List<ColorPickerButton> _colorPickers = [];
    private bool _bindingAppearance, _appearancePending;
    private double _appearanceDelay;

    private void BuildAppearanceSettings(VBoxContainer controls)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); controls.AddChild(row);
        foreach (string _ in new[] { "ink", "stars", "custom" }) _colorScheme.AddItem("");
        Ui1Theme.ApplyOptionButton(_colorScheme); _colorScheme.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(_colorScheme);
        row.AddChild(SettingsButton("settings.appearance.reset", () => ChangeAppearance(new())));
        var colors = new HFlowContainer(); colors.AddThemeConstantOverride("h_separation", 20);
        colors.AddThemeConstantOverride("v_separation", 12); controls.AddChild(colors);
        foreach (string key in new[] { "background", "panel", "accent", "text" })
        {
            var column = new VBoxContainer(); colors.AddChild(column);
            column.AddChild(SettingsLabel("settings.appearance." + key, 16, true));
            var picker = new ColorPickerButton { Name = "Appearance" + key, CustomMinimumSize = new(124, 42), EditAlpha = false };
            column.AddChild(picker); _colorPickers.Add(picker);
            picker.ColorChanged += _ =>
            {
                if (_bindingAppearance) return;
                var value = new UiColorPreferences { Scheme = "custom",
                    Background = _colorPickers[0].Color.ToHtml(false), Panel = _colorPickers[1].Color.ToHtml(false),
                    Accent = _colorPickers[2].Color.ToHtml(false), Text = _colorPickers[3].Color.ToHtml(false) };
                _persistence!.SetAppearance(value, flush: false);
                _colorScheme.Select(2); _appearancePending = true;
            };
            picker.PopupClosed += () => { ApplyPendingAppearance(); _persistence!.FlushPreferences(); };
        }
        _colorScheme.ItemSelected += index =>
        {
            if (_bindingAppearance) return;
            ChangeAppearance((_persistence!.Preferences.Appearance ?? new()) with { Scheme = index == 1 ? "stars" : index == 2 ? "custom" : "ink" });
        };
    }

    private void ChangeAppearance(UiColorPreferences value)
    {
        _persistence!.SetAppearance(value);
        _appearancePending = true; ApplyPendingAppearance(); RefreshAppearanceSettings();
    }

    private void ApplyPendingAppearance()
    {
        if (!_appearancePending) return;
        _appearancePending = false; _appearanceDelay = .12;
        UiAppearance.Apply(this, _persistence!.Preferences.Appearance);
        // Only visual resources change. Keep search workers, editor state and open pickers intact.
    }

    private void RefreshAppearanceSettings()
    {
        if (_settingsText is null || _colorPickers.Count == 0) return;
        _bindingAppearance = true;
        try
        {
            var saved = (_persistence!.Preferences.Appearance ?? new()).Normalize();
            string[] schemes = ["ink", "stars", "custom"];
            for (int i = 0; i < schemes.Length; i++) _colorScheme.SetItemText(i, _settingsText.Get("settings.appearance." + schemes[i]));
            _colorScheme.Select(Array.IndexOf(schemes, saved.Scheme));
            var shown = saved.Scheme == "ink" ? new UiColorPreferences { Background = "131C29", Panel = "1E2B3C", Accent = "C9BA97", Text = "E2E7EC" }
                : saved.Scheme == "stars" ? new UiColorPreferences() : saved;
            string[] values = [shown.Background, shown.Panel, shown.Accent, shown.Text];
            for (int i = 0; i < values.Length; i++) _colorPickers[i].Color = new Color(values[i]);
        }
        finally { _bindingAppearance = false; }
    }
}
