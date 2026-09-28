using Godot;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.DeveloperNotes;

internal sealed partial class DeveloperNotesArticleRenderer : VBoxContainer
{
    internal sealed record Section(string Title, Control Anchor);
    public IReadOnlyList<Section> Sections => _sections;
    private readonly List<Section> _sections = [];

    public DeveloperNotesArticleRenderer()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 18);
    }

    public void Render(DeveloperNotesChapter chapter)
    {
        ClearBlocks();
        foreach (DeveloperNotesBlock block in chapter.Blocks)
        {
            Control control = block.Type switch
            {
                DeveloperNotesBlockKinds.Heading => CreateHeading(block.Text!),
                DeveloperNotesBlockKinds.Paragraph => CreateRichText(block.Text!, 20),
                DeveloperNotesBlockKinds.BulletList => CreateBulletList(block.Items!),
                DeveloperNotesBlockKinds.Callout => CreateCallout(block.Text!),
                _ => CreateRichText(block.Text ?? string.Empty, 20)
            };
            AddChild(control);
            if (block.Type == DeveloperNotesBlockKinds.Heading)
                _sections.Add(new Section(block.Text ?? string.Empty, control));
        }
    }

    public void ClearBlocks()
    {
        _sections.Clear();
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    private Control CreateHeading(string text)
    {
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_top", GetChildCount() == 0 ? 4 : 20);
        var label = CreateRichText(text, 24);
        label.AddThemeColorOverride("default_color", new Color("CDBF98"));
        margin.AddChild(label);
        return margin;
    }

    private RichTextLabel CreateRichText(string bbcode, int fontSize)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            FocusMode = FocusModeEnum.None,
            SelectionEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
            Text = bbcode
        };
        Font font = GetThemeFont("font", "Label");
        label.AddThemeFontOverride("normal_font", font);
        label.AddThemeFontOverride("bold_font", font);
        label.AddThemeColorOverride("default_color", new Color("C4D0DC"));
        label.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_font_size", fontSize);
        label.AddThemeFontSizeOverride("italics_font_size", fontSize);
        label.AddThemeConstantOverride("line_separation", 5);
        return label;
    }

    private Control CreateBulletList(IReadOnlyList<string> items)
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
            RichTextLabel text = CreateRichText(item, 20);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(bullet);
            row.AddChild(text);
            column.AddChild(row);
        }
        return column;
    }

    private Control CreateCallout(string text)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = new StyleBoxFlat
        {
            BgColor = new Color("223143"), BorderColor = new Color("8B806A"), BorderWidthLeft = 2,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 13, ContentMarginBottom = 13,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(CreateRichText(text, 20));
        return panel;
    }
}
