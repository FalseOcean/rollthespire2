using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class UnlockSummary : PanelContainer
{
    private readonly Label _label;

    public UnlockSummary()
    {
        MainPanelTheme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 8f);
        _label = MainPanelTheme.Label(string.Empty, Ui1TextRole.Meta);
        AddChild(_label);
    }

    public void Bind(string text, string tooltip, bool? complete)
    {
        _label.Text = text;
        _label.AddThemeColorOverride(
            "font_color",
            complete switch
            {
                true => MainPanelTheme.Palette.Success,
                false => MainPanelTheme.Palette.Warning,
                null => MainPanelTheme.Palette.Unknown
            });
        TooltipText = tooltip;
    }
}
