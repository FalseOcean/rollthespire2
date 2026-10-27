using Godot;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private OptionButton _colorScheme = null!;
    private readonly List<ColorPickerButton> _colorPickers = [];
    private bool _bindingAppearance, _appearancePending;
    private double _appearanceDelay;
    private OptionButton _fontChoice = null!;
    private Label _fontFeedback = null!;
    private static readonly string[] FontChoices = ["game-sans", "game-serif"];
    private string _appliedFontChoice = "";

    private void PrepareDeferredPage()
    {
        RefreshWorkspaceFont();
        UiAppearance.Initialize(_persistence!.Preferences.Appearance);
        RefreshLanguage();
    }

    private void RefreshWorkspaceFont()
    {
        string choice = _persistence!.Preferences.FontChoice;
        MainPanelTheme.SelectFont(choice);
        if (_appliedFontChoice == choice) return;
        MainPanelTheme.RefreshFonts(this); _appliedFontChoice = choice;
    }

    private void BuildFontSettings(VBoxContainer controls)
    {
        _fontChoice = new() { Name = "SettingsFontChoice", CustomMinimumSize = new(360, 44), FitToLongestItem = false };
        foreach (var choice in FontChoices) _fontChoice.AddItem(choice);
        _fontChoice.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        MainPanelTheme.ApplyOptionButton(_fontChoice); _fontChoice.AddThemeFontSizeOverride("font_size", 18);
        controls.AddChild(_fontChoice);
        _fontFeedback = SettingsLabel("settings.font.sample", 18, wrap: true);
        controls.AddChild(_fontFeedback);
        _fontChoice.ItemSelected += index =>
        {
            if (_bindingAppearance) return;
            try
            {
                MainPanelTheme.SelectFont(FontChoices[(int)index]);
                _persistence!.SetFontChoice(FontChoices[(int)index]);
                RefreshWorkspaceFont();
                _fontFeedback.Text = _settingsText!.Get("settings.font.sample");
            }
            catch (Exception ex)
            {
                RolltheSpire2.Bootstrap.RuntimeLog.Fault("fontSelectionFailed=true;choice=" + FontChoices[(int)index], ex);
                _fontChoice.Select(Array.IndexOf(FontChoices, _persistence!.Preferences.FontChoice));
                _fontFeedback.Text = _settingsText!.Get("settings.font.unavailable");
            }
        };
    }

    private IEnumerable<object?> BuildAppearanceSettings(VBoxContainer controls)
    {
        _colorScheme = new() { Name = "SettingsColorScheme", CustomMinimumSize = new(300, 44), FitToLongestItem = false };
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); controls.AddChild(row);
        foreach (string _ in new[] { "ink", "stars", "custom" }) _colorScheme.AddItem("");
        MainPanelTheme.ApplyOptionButton(_colorScheme); _colorScheme.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(_colorScheme);
        row.AddChild(SettingsButton("settings.appearance.reset", () => ChangeAppearance(new())));
        var colors = new HFlowContainer(); colors.AddThemeConstantOverride("h_separation", 20);
        colors.AddThemeConstantOverride("v_separation", 12); controls.AddChild(colors);
        yield return null;
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
            yield return null;
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
        UiAppearance.Apply(this, _persistence!.Preferences.Appearance, PendingPageBuildViews());
        // Only visual resources change. Keep search workers, editor state and open pickers intact.
    }

    private void RefreshAppearanceSettings()
    {
        if (_settingsText is null || _colorPickers.Count != 4) return;
        _bindingAppearance = true;
        try
        {
            for (int i = 0; i < FontChoices.Length; i++) _fontChoice.SetItemText(i, _settingsText.Get("settings.font." + FontChoices[i]));
            _fontChoice.Select(Array.IndexOf(FontChoices, _persistence!.Preferences.FontChoice));
            var saved = (_persistence!.Preferences.Appearance ?? new()).Normalize();
            string[] schemes = ["ink", "stars", "custom"];
            for (int i = 0; i < schemes.Length; i++) _colorScheme.SetItemText(i, _settingsText.Get("settings.appearance." + schemes[i]));
            _colorScheme.Select(Array.IndexOf(schemes, saved.Scheme));
            var shown = saved.Scheme == "ink" ? new UiColorPreferences { Background = "070D1A", Panel = "0B1224", Accent = "C7B07B", Text = "E5EDF8" }
                : saved.Scheme == "stars" ? new UiColorPreferences() : saved;
            string[] values = [shown.Background, shown.Panel, shown.Accent, shown.Text];
            for (int i = 0; i < values.Length; i++) _colorPickers[i].Color = new Color(values[i]);
        }
        finally { _bindingAppearance = false; }
    }
}
