using Godot;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.DeveloperNotes;

internal sealed partial class DeveloperNotesArticleRenderer : VBoxContainer
{
    public DeveloperNotesArticleRenderer()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 14);
    }

    public void Render(DeveloperNotesChapter chapter)
    {
        ClearBlocks();
        foreach (DeveloperNotesBlock block in chapter.Blocks)
        {
            Control control = block.Type switch
            {
                DeveloperNotesBlockKinds.Heading => CreateHeading(block.Text!),
                DeveloperNotesBlockKinds.Paragraph => CreateRichText(block.Text!, 15),
                DeveloperNotesBlockKinds.BulletList => CreateBulletList(block.Items!),
                DeveloperNotesBlockKinds.Callout => CreateCallout(block.Text!),
                _ => CreateRichText(block.Text ?? string.Empty, 15)
            };
            AddChild(control);
        }
    }

    public void ClearBlocks()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Control CreateHeading(string text)
    {
        Label label = Ui1Theme.Label(text, Ui1TextRole.SectionTitle, wrap: true);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    private static RichTextLabel CreateRichText(string bbcode, int fontSize)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = bbcode
        };
        label.AddThemeColorOverride("default_color", Ui1Theme.Palette.TextPrimary);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.34f));
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_font_size", fontSize);
        label.AddThemeFontSizeOverride("italics_font_size", fontSize);
        label.AddThemeConstantOverride("line_separation", 5);
        return label;
    }

    private static Control CreateBulletList(IReadOnlyList<string> items)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 7);
        foreach (string item in items)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 9);
            Label bullet = Ui1Theme.Label("•", Ui1TextRole.Accent);
            bullet.VerticalAlignment = VerticalAlignment.Top;
            bullet.CustomMinimumSize = new Vector2(12, 0);
            RichTextLabel text = CreateRichText(item, 15);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(bullet);
            row.AddChild(text);
            column.AddChild(row);
        }
        return column;
    }

    private static Control CreateCallout(string text)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.CardElevated, 4f, 1, 12f);
        panel.AddChild(CreateRichText(text, 14));
        return panel;
    }
}
