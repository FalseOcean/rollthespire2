using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed record QueueStripItem(
    int Order,
    ModelKey Key,
    string DisplayName,
    IconDescriptor Icon,
    string Tooltip);

/// <summary>Ordered display-only queue. Empty queues remain hidden; no placeholder data is invented.</summary>
internal sealed partial class QueueStrip : GridContainer
{
    public QueueStrip()
    {
        Columns = 6;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("h_separation", 8);
        AddThemeConstantOverride("v_separation", 8);
        Visible = false;
    }

    public void Bind(IReadOnlyList<QueueStripItem> items, string missingIconText)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        Visible = items.Count > 0;
        foreach (QueueStripItem item in items.OrderBy(item => item.Order))
        {
            var panel = new PanelContainer();
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, 3f, 1, 8f);
            var column = new VBoxContainer();
            var order = Ui1Theme.Label(item.Order.ToString(), Ui1TextRole.Meta);
            order.HorizontalAlignment = HorizontalAlignment.Center;
            var icon = new IconWithLabel(42f, Ui1TextRole.Meta);
            icon.Bind(item.Icon, item.DisplayName, item.Tooltip, missingIconText);
            column.AddChild(order);
            column.AddChild(icon);
            panel.AddChild(column);
            AddChild(panel);
        }
    }
}
