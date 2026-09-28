using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class GlobalStatusBar : PanelContainer
{
    private readonly Label _status;
    private readonly Label _context;

    public GlobalStatusBar()
    {
        CustomMinimumSize = new Vector2(0, Ui1Metrics.StatusBarHeight);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Status, 0f, 0, 8f);
        var row = new HBoxContainer();
        _status = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _context = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted);
        _context.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(_status);
        row.AddChild(_context);
        AddChild(row);
    }

    public void SetStatus(GlobalStatusKind kind, string text, string context = "")
    {
        _status.Text = text;
        _context.Text = context;
        _status.AddThemeColorOverride("font_color", kind switch
        {
            GlobalStatusKind.Busy => Ui1Theme.Palette.AccentFocus,
            GlobalStatusKind.Warning => Ui1Theme.Palette.Warning,
            GlobalStatusKind.Error => Ui1Theme.Palette.Error,
            _ => Ui1Theme.Palette.TextSecondary
        });
    }
}
