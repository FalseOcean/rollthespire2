using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed record CompactIconResultItem(
    int Order,
    ModelKey Key,
    string DisplayName,
    IconDescriptor Icon,
    string IdentityStatus,
    string CapabilityStatus,
    string Tooltip);

/// <summary>
/// Reusable ordered icon row for future compact identity summaries. It consumes
/// resolved display models only and never resolves identity, icons, or effects.
/// </summary>
internal sealed partial class CompactIconResultRow : GridContainer
{
    public CompactIconResultRow()
    {
        Columns = 3;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("h_separation", 10);
        AddThemeConstantOverride("v_separation", 10);
    }

    public void SetCompact(bool compact) => Columns = compact ? 2 : 3;

    public void Bind(IReadOnlyList<CompactIconResultItem> items, string missingIconText)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        foreach (CompactIconResultItem item in items.OrderBy(item => item.Order))
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, 4f, 1, 10f);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var order = Ui1Theme.Label(item.Order.ToString(), Ui1TextRole.Accent);
            order.CustomMinimumSize = new Vector2(22, 0);
            var icon = new IconWithLabel(44f, Ui1TextRole.CardTitle);
            icon.Bind(item.Icon, item.DisplayName, item.Tooltip, missingIconText);
            var status = Ui1Theme.Label($"{item.IdentityStatus} · {item.CapabilityStatus}", Ui1TextRole.Muted, true);
            status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(order);
            row.AddChild(icon);
            row.AddChild(status);
            panel.AddChild(row);
            AddChild(panel);
        }
    }
}
