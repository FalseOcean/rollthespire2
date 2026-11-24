using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private bool _bindingFont;
    private OptionButton _fontChoice = null!;
    private Label _fontFeedback = null!;
    private static readonly string[] FontChoices = ["game-sans", "game-serif"];
    private string _appliedFontChoice = "";

    private void PrepareDeferredPage()
    {
        RefreshWorkspaceFont();
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
            if (_bindingFont) return;
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

    private void RefreshFontSettings()
    {
        if (_settingsText is null || _fontChoice is null) return;
        _bindingFont = true;
        try
        {
            for (int i = 0; i < FontChoices.Length; i++) _fontChoice.SetItemText(i, _settingsText.Get("settings.font." + FontChoices[i]));
            _fontChoice.Select(Array.IndexOf(FontChoices, _persistence!.Preferences.FontChoice));
        }
        finally { _bindingFont = false; }
    }
}
