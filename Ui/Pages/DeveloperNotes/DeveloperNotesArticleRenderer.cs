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
        Theme = MainPanelTheme.Create();
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 18);
    }

    internal void AppendBlock(DeveloperNotesBlock block)
    {
        Control? control = null;
        try
        {
            control = block.Type switch
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
        catch
        {
            if (control is not null && control.GetParent() is null) control.Free();
            throw;
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
        try
        {
            margin.AddThemeConstantOverride("margin_top", GetChildCount() == 0 ? 4 : 20);
            var label = CreateRichText(text, 24);
            margin.AddChild(label);
            MainPanelTheme.SetColor(label, "default_color", WorkspacePalette.MainPanel.Selected);
            return margin;
        }
        catch { margin.Free(); throw; }
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
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        try
        {
            MainPanelTheme.ApplyFont(label);
            MainPanelTheme.SetColor(label, "default_color", WorkspacePalette.MainPanel.Text);
            label.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            label.AddThemeFontSizeOverride("normal_font_size", fontSize);
            label.AddThemeFontSizeOverride("bold_font_size", fontSize);
            label.AddThemeFontSizeOverride("italics_font_size", fontSize);
            label.AddThemeConstantOverride("line_separation", 5);
            // Apply the final font and size before native shaping sees content.
            label.Text = bbcode;
            return label;
        }
        catch { label.Free(); throw; }
    }

    private Control CreateBulletList(IReadOnlyList<string> items)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        try
        {
            column.AddThemeConstantOverride("separation", 7);
            foreach (string item in items)
            {
                var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                column.AddChild(row);
                row.AddThemeConstantOverride("separation", 9);
                Label bullet = MainPanelTheme.Label("•", Ui1TextRole.Accent);
                row.AddChild(bullet);
                bullet.VerticalAlignment = VerticalAlignment.Top;
                bullet.CustomMinimumSize = new Vector2(12, 0);
                RichTextLabel text = CreateRichText(item, 20);
                row.AddChild(text);
                text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            }
            return column;
        }
        catch { column.Free(); throw; }
    }

    private Control CreateCallout(string text)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        try
        {
            var palette = WorkspacePalette.MainPanel;
            var style = palette.Box(palette.OptionSurface, palette.Line, 0);
            style.BorderWidthLeft = 2;
            MainPanelTheme.SetMargins(style, 18, 13, 18, 13);
            panel.AddThemeStyleboxOverride("panel", style);
            panel.AddChild(CreateRichText(text, 20));
            return panel;
        }
        catch { panel.Free(); throw; }
    }
}
