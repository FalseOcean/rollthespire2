using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed record EventQueueStripItem(
    int Order,
    ModelKey EventKey,
    string DisplayName,
    bool IsConditional,
    string ConditionTooltip);

/// <summary>Text-first ordered event queue display. It does not derive conditions or event identity.</summary>
internal sealed partial class EventQueueStrip : VBoxContainer
{
    public EventQueueStrip()
    {
        AddThemeConstantOverride("separation", 6);
        Visible = false;
    }

    public void Bind(IReadOnlyList<EventQueueStripItem> items)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        Visible = items.Count > 0;
        foreach (EventQueueStripItem item in items.OrderBy(item => item.Order))
        {
            var panel = new PanelContainer();
            Ui1Theme.ApplyPanel(panel, item.IsConditional ? Ui1SurfaceRole.Warning : Ui1SurfaceRole.Input, 3f, 1, 8f);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var order = Ui1Theme.Label(item.Order.ToString(), Ui1TextRole.Meta);
            order.CustomMinimumSize = new Vector2(28, 0);
            var name = Ui1Theme.Label(item.DisplayName, item.IsConditional ? Ui1TextRole.Warning : Ui1TextRole.Body, true);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(order);
            row.AddChild(name);
            panel.TooltipText = item.ConditionTooltip;
            panel.AddChild(row);
            AddChild(panel);
        }
    }
}
