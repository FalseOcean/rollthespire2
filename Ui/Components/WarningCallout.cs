using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// User-facing warnings only. Diagnostic codes remain in operational evidence.
/// </summary>
internal sealed partial class WarningCallout : PanelContainer
{
    private readonly Label _message;

    public WarningCallout()
    {
        Visible = false;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Warning, 4f, 1, 10f);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var icon = Ui1Theme.Label("△", Ui1TextRole.Warning);
        icon.CustomMinimumSize = new Vector2(26, 0);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        _message = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _message.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(icon);
        row.AddChild(_message);
        AddChild(row);
    }

    public void Bind(IReadOnlyList<string> messages)
    {
        Visible = messages.Count > 0;
        _message.Text = messages.Count == 0
            ? string.Empty
            : string.Join("\n", messages);
    }
}
