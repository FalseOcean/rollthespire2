using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Builds grouped editor spacing once per render; no appearance rollback or query state.</summary>
internal sealed class SearchEditorLayout
{
    private sealed record SectionRange(Control Host, float Top, float Bottom, string? Caption);
    private readonly List<SectionRange> _sections = [];

    internal void Begin() => _sections.Clear();
    internal void Section(Control host, float top, float bottom, string? caption = null)
        => _sections.Add(new(host, top, bottom, caption));

    internal void Finish()
    {
        var palette = WorkspacePalette.MainPanel;
        foreach (var sections in _sections.GroupBy(s => s.Host))
        {
            Control host = sections.Key;
            // Direct Sprite2D children are authored object artwork. Their siblings
            // and owning controls receive the same section offset.
            var content = host.GetChildren().Select(node => node switch
            {
                Control control => (Node: (CanvasItem)control, Position: control.Position),
                Node2D visual => (Node: (CanvasItem)visual, Position: visual.Position),
                _ => (Node: (CanvasItem?)null, Position: Vector2.Zero)
            }).Where(item => item.Node is not null).ToArray();
            float offset = 4;
            float contentBottom = 0;
            foreach (SectionRange section in sections.OrderBy(s => s.Top))
            {
                float captionHeight = section.Caption is null ? 0 : 24;
                float top = section.Top + offset;
                float bottom = Math.Max(section.Top + 20, Math.Min(section.Bottom, host.CustomMinimumSize.Y));
                var members = content.Where(item => item.Position.Y >= section.Top && item.Position.Y < section.Bottom).ToArray();
                float height = bottom - section.Top + captionHeight + 16;
                var surface = palette.ContentBox();
                var panel = new Panel { Name = "SearchConditionGroup", Position = new(0, top),
                    MouseFilter = Control.MouseFilterEnum.Ignore };
                panel.AddThemeStyleboxOverride("panel", surface);
                host.AddChild(panel);
                if (section.Caption is not null)
                {
                    var title = palette.Label(section.Caption, 15, true);
                    title.Name = "SearchConditionGroupTitle"; title.Position = new(8, 6);
                    title.Size = new(Math.Max(1, host.CustomMinimumSize.X - 16), 24);
                    panel.AddChild(title);
                }
                foreach (var item in members)
                {
                    Vector2 local = new(item.Position.X, item.Position.Y - section.Top + captionHeight + 8);
                    item.Node!.Reparent(panel, false);
                    if (item.Node is Control control)
                    {
                        control.Position = local;
                        height = Math.Max(height, local.Y + Math.Max(control.Size.Y, control.GetCombinedMinimumSize().Y) + 8);
                    }
                    else if (item.Node is Node2D visual) visual.Position = local;
                }
                // The group owns its controls, not only a rectangle behind siblings.
                // Include actual control height before placing the following group.
                panel.Size = new(Math.Max(1, host.CustomMinimumSize.X), height);
                offset += height - (bottom - section.Top) + 12;
                contentBottom = Math.Max(contentBottom, top + height);
            }
            foreach (var item in content.Where(item => item.Node!.GetParent() == host))
                if (item.Node is Control control)
                    contentBottom = Math.Max(contentBottom, control.Position.Y + Math.Max(control.Size.Y, control.GetCombinedMinimumSize().Y));
            host.CustomMinimumSize = new(host.CustomMinimumSize.X, contentBottom + 4);
        }
    }
}
