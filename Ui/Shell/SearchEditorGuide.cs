using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Shared presentation for editor headings and optional reference copy.</summary>
internal static class SearchEditorGuide
{
    internal static VBoxContainer Build(Control host, WorkspacePalette palette, float width,
        string title, string summary, string[] details, bool expanded, Action toggle, Action openArticle)
    {
        bool zh = title.Any(c => c > 0x3000);
        title = title.Replace(" · 使用说明", "").Replace(" · Guide", "").Replace(" · Usage", "");
        var guide = new VBoxContainer { Size = new(width, 0) };
        guide.AddThemeConstantOverride("separation", 8);
        host.AddChild(guide);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        guide.AddChild(header);
        var heading = palette.Label(title, 24);
        heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        heading.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(heading);
        void Link(string text, Action action)
        {
            var button = palette.CompactButton(text, 32, 15);
            // Give container links an explicit width: trimming styles can report
            // a zero text minimum even when ClipText is disabled.
            button.ClipText = false;
            button.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            button.CustomMinimumSize = new(112, 32);
            button.AddThemeFontSizeOverride("font_size", 15);
            button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            button.Pressed += action;
            header.AddChild(button);
        }
        Link(zh ? (expanded ? "收起说明 ▴" : "使用说明 ▾") : (expanded ? "Hide help ▴" : "Help ▾"), toggle);
        Link(zh ? "百科 ↗" : "Guide ↗", openArticle);
        var hint = palette.Label(summary, 16, true);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        guide.AddChild(hint);
        if (expanded)
        {
            var panel = new PanelContainer();
            var box = palette.Box(palette.Surface);
            box.ContentMarginLeft = box.ContentMarginRight = 12;
            box.ContentMarginTop = box.ContentMarginBottom = 8;
            panel.AddThemeStyleboxOverride("panel", box);
            guide.AddChild(panel);
            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new(0, 104),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            panel.AddChild(scroll);
            var paragraphs = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            paragraphs.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(paragraphs);
            foreach (string text in details.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                var paragraph = palette.Label(text, 15, true);
                paragraph.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                paragraph.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                paragraphs.AddChild(paragraph);
            }
        }
        guide.AddChild(new ColorRect
        {
            Color = palette.Color(palette.Line), CustomMinimumSize = new(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        return guide;
    }
}
