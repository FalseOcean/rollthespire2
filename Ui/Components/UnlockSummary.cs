using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class UnlockSummary : PanelContainer
{
    private readonly Label _label;

    public UnlockSummary()
    {
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 8f);
        _label = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        AddChild(_label);
    }

    public void Bind(string text, string tooltip, bool? complete)
    {
        _label.Text = text;
        _label.AddThemeColorOverride(
            "font_color",
            complete switch
            {
                true => Ui1Theme.Palette.Success,
                false => Ui1Theme.Palette.Warning,
                null => Ui1Theme.Palette.Unknown
            });
        TooltipText = tooltip;
    }
}
