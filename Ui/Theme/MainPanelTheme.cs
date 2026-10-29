using Godot;

namespace RolltheSpire2.Ui.Theme;

/// <summary>Creation-time theme for the main panel and its owned pickers/modals.</summary>
internal static partial class MainPanelTheme
{
    private static FontVariation? _regular, _medium;
    private static Ui1Palette? _palette;
    public static Font Regular { get { EnsureFonts(); return _regular!; } }
    public static Font Medium { get { EnsureFonts(); return _medium!; } }
    public static Ui1Palette Palette
    {
        get
        {
            if (_palette is not null) return _palette;
            var p = WorkspacePalette.MainPanel;
            return _palette = new Ui1Palette
            {
                Window = p.Color(p.Canvas), Header = p.Color(p.Canvas), Navigation = p.Color(p.Canvas), Page = p.Color(p.ContentSurface),
                Card = p.Color(p.ContentSurface), CardHover = p.Color(p.Hover), CardSelected = p.Color(p.SelectedSurface), Input = p.Color(p.Surface), Drawer = p.Color(p.ContentSurface),
                BorderSubtle = p.Color(p.Line), BorderNormal = p.Color(p.Line), BorderFocus = p.Color(p.Line), BorderSelected = p.Color(p.Selected),
                TextPrimary = p.Color(p.Text), TextSecondary = p.Color(p.Secondary), TextMuted = p.Color(p.Secondary), TextDisabled = p.Color(p.Disabled),
                AccentWarm = p.Color(p.Selected), AccentFocus = p.Color(p.Secondary), Success = new Color("78B58A"), Partial = new Color("C9A95A"),
                Unknown = new Color("9AA7BA"), Warning = p.Warning, Error = p.Error, Unsupported = new Color("7E899C"), NotMigrated = new Color("9A8B72")
            };
        }
    }

    public static Godot.Theme Create()
    {
        var p = WorkspacePalette.MainPanel;
        var theme = new Godot.Theme { DefaultFont = Regular, DefaultFontSize = 16 };
        theme.SetMeta("rt2_mainpanel", true);
        foreach (string type in new[] { "Label", "RichTextLabel", "LineEdit", "TextEdit", "CodeEdit", "Button", "CheckBox", "CheckButton", "OptionButton", "MenuButton", "PopupMenu", "ItemList", "Tree", "SpinBox" })
        {
            theme.SetFont("font", type, type is "Button" or "CheckBox" or "CheckButton" or "OptionButton" or "MenuButton" ? Medium : Regular);
            ThemeColor(theme, "font_color", type, p.Text);
            ThemeColor(theme, "font_hover_color", type, p.Text);
            ThemeColor(theme, "font_pressed_color", type, p.Text);
            ThemeColor(theme, "font_focus_color", type, p.Text);
            ThemeColor(theme, "font_disabled_color", type, p.Disabled);
            ThemeColor(theme, "font_placeholder_color", type, p.Secondary);
            theme.SetColor("font_shadow_color", type, new Color(0, 0, 0, .65f));
            theme.SetConstant("shadow_offset_x", type, 0);
            theme.SetConstant("shadow_offset_y", type, 1);
        }
        ThemeColor(theme, "default_color", "RichTextLabel", p.Text);
        ThemeColor(theme, "caret_color", "LineEdit", p.Text);
        ThemeColor(theme, "caret_color", "TextEdit", p.Text);
        ThemeColor(theme, "selection_color", "LineEdit", p.SelectedSurface);
        ThemeColor(theme, "selection_color", "TextEdit", p.SelectedSurface);
        foreach (string font in new[] { "normal_font", "italics_font", "mono_font" }) theme.SetFont(font, "RichTextLabel", Regular);
        foreach (string font in new[] { "bold_font", "bold_italics_font" }) theme.SetFont(font, "RichTextLabel", Medium);
        foreach (string type in new[] { "Button", "CheckBox", "CheckButton", "OptionButton", "MenuButton" })
        {
            foreach (var (state, fill) in new[] { ("normal", p.OptionSurface), ("hover", p.Hover), ("pressed", p.SelectedSurface),
                ("hover_pressed", p.SelectedSurface), ("disabled", p.Surface) })
                theme.SetStylebox(state, type, CompactBox(fill, p.Line, state == "hover" ? 1 : 0));
            theme.SetStylebox("focus", type, p.FocusRing());
        }
        foreach (string type in new[] { "LineEdit", "TextEdit", "CodeEdit" })
        {
            theme.SetStylebox("normal", type, CompactBox(p.Surface, p.Line, 1));
            theme.SetStylebox("read_only", type, CompactBox(p.Surface, p.Line, 1));
            theme.SetStylebox("focus", type, p.FocusRing());
        }
        foreach (string type in new[] { "Panel", "PanelContainer", "PopupMenu" })
        {
            var panel = p.ContentBox();
            SetMargins(panel, 8, 4, 8, 4);
            theme.SetStylebox("panel", type, panel);
        }
        theme.SetStylebox("hover", "PopupMenu", CompactBox(p.Hover, p.Line, 0));
        // Text has no surface of its own. Callouts, code and other grouped
        // content provide their background through the containing panel.
        theme.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        return theme;
    }

    // Explicit root boundary for an existing map/other independent native surface.
    // No subtree scan, recoloring or font replacement is involved.
    public static Godot.Theme CreateClassicBoundary(Godot.Theme? original = null)
    {
        var theme = (Godot.Theme)ThemeDB.GetDefaultTheme().Duplicate();
        theme.SetMeta("rt2_independent_ui", true);
        foreach (Godot.Theme? source in new[] { ThemeDB.GetProjectTheme(), original })
        {
            if (source is null) continue;
            theme.MergeWith(source);
            if (source.DefaultFont is not null) theme.DefaultFont = source.DefaultFont;
            if (source.DefaultFontSize > 0) theme.DefaultFontSize = source.DefaultFontSize;
        }
        return theme;
    }

    public static void ApplyFont(Control control, bool heading = false)
    {
        control.SetMeta("rt2_mainpanel", true);
        control.SetMeta("rt2_font_heading", heading);
        void Font(string name, Godot.Font value)
        {
            if (!control.HasThemeFontOverride(name) || control.GetThemeFont(name) != value)
                control.AddThemeFontOverride(name, value);
        }
        Font("font", heading ? Medium : Regular);
        if (control is RichTextLabel)
        {
            Font("normal_font", heading ? Medium : Regular);
            Font("bold_font", Medium);
            Font("italics_font", Regular);
            Font("bold_italics_font", Medium);
            Font("mono_font", Regular);
        }
        var shadow = new Color(0, 0, 0, .65f);
        if (!control.HasThemeColorOverride("font_shadow_color") || control.GetThemeColor("font_shadow_color") != shadow)
            control.AddThemeColorOverride("font_shadow_color", shadow);
        foreach (var (name, value) in new[] { ("shadow_offset_x", 0), ("shadow_offset_y", 1) })
            if (!control.HasThemeConstantOverride(name) || control.GetThemeConstant(name) != value)
                control.AddThemeConstantOverride(name, value);
    }

    public static void SetColor(Control control, string name, string token)
    {
        control.SetMeta("rt2_mainpanel", true);
        control.SetMeta("rt2_color_theme_override_colors_" + name, token);
        control.AddThemeColorOverride(name, WorkspacePalette.MainPanel.Color(token));
    }

    public static void SetBackground(ColorRect rect, string token, float alpha = 1)
    {
        Color color = WorkspacePalette.MainPanel.Color(token);
        rect.SetMeta("rt2_mainpanel", true);
        rect.SetMeta("rt2_color_color", token);
        rect.Color = new Color(color.R, color.G, color.B, alpha);
    }

    public static void ApplyButton(Button button, Ui1ButtonRole role = Ui1ButtonRole.Secondary)
    {
        // NavigationSelected is a navigation state, not a selected Query member.
        var p = WorkspacePalette.MainPanel;
        p.ApplyButton(button, primary: role == Ui1ButtonRole.Primary);
        button.AddThemeFontSizeOverride("font_size", 15);
        if (role is Ui1ButtonRole.Navigation or Ui1ButtonRole.NavigationSelected)
            p.SetActive(button, role == Ui1ButtonRole.NavigationSelected);
        else if (role == Ui1ButtonRole.Danger)
        {
            button.AddThemeColorOverride("font_color", p.Error);
            button.AddThemeColorOverride("font_hover_color", p.Error);
        }
    }

    public static void ApplyOptionButton(OptionButton option) => ApplyButton(option);

    public static StyleBoxFlat Surface(Ui1SurfaceRole role, float radius = 4f, int borderWidth = 1)
    {
        var p = WorkspacePalette.MainPanel;
        string fill = role switch { Ui1SurfaceRole.Window or Ui1SurfaceRole.Header or Ui1SurfaceRole.Navigation or Ui1SurfaceRole.Status => p.Canvas,
            Ui1SurfaceRole.Input => p.Surface, Ui1SurfaceRole.Error => "251524", _ => p.ContentSurface };
        string border = role switch { Ui1SurfaceRole.Warning => "D99783", Ui1SurfaceRole.Error => "D98780", _ => p.Line };
        var box = p.Box(fill, border, borderWidth);
        if (border == p.Line) box.BorderColor = new Color(box.BorderColor, role == Ui1SurfaceRole.Input ? .25f : .4f);
        SetMargins(box, -1, -1, -1, -1);
        box.CornerRadiusTopLeft = box.CornerRadiusTopRight = box.CornerRadiusBottomLeft = box.CornerRadiusBottomRight = (int)radius;
        box.ShadowColor = new Color(0, 0, 0, role == Ui1SurfaceRole.Window ? .55f : .16f);
        box.ShadowSize = role == Ui1SurfaceRole.Window ? 8 : 1;
        return box;
    }

    public static void SetMargins(StyleBoxFlat box, float left, float top, float right, float bottom)
    {
        box.ContentMarginLeft = left; box.ContentMarginTop = top;
        box.ContentMarginRight = right; box.ContentMarginBottom = bottom;
    }

    public static void ApplyPanel(PanelContainer panel, Ui1SurfaceRole role, float radius = 4f, int borderWidth = 1, float margin = 0)
    {
        var box = Surface(role, radius, borderWidth);
        if (margin > 0) SetMargins(box, margin, margin, margin, margin);
        panel.SetMeta("rt2_mainpanel", true);
        panel.AddThemeStyleboxOverride("panel", box);
    }

    public static void ApplyPanel(Panel panel, Ui1SurfaceRole role, float radius = 4f, int borderWidth = 1)
    {
        panel.SetMeta("rt2_mainpanel", true);
        panel.AddThemeStyleboxOverride("panel", Surface(role, radius, borderWidth));
    }

    public static void ApplyCheckButton(CheckButton check)
    {
        ApplyFont(check, heading: true);
        SetColor(check, "font_color", WorkspacePalette.MainPanel.Secondary);
        SetColor(check, "font_hover_color", WorkspacePalette.MainPanel.Text);
        check.AddThemeFontSizeOverride("font_size", 14);
    }

    public static void ApplySeparator(HSeparator separator) => Separator(separator, vertical: false);
    public static void ApplySeparator(VSeparator separator) => Separator(separator, vertical: true);

    private static void Separator(Control separator, bool vertical)
    {
        var p = WorkspacePalette.MainPanel;
        var box = p.Box(p.Line);
        SetMargins(box, vertical ? 1 : 0, vertical ? 0 : 1, vertical ? 1 : 0, vertical ? 0 : 1);
        separator.SetMeta("rt2_mainpanel", true);
        separator.AddThemeStyleboxOverride("separator", box);
    }

    public static void ApplyLineEdit(LineEdit edit)
    {
        ApplyFont(edit);
        InputStyles(edit);
        edit.AddThemeFontSizeOverride("font_size", 16);
    }

    public static void ApplyTextEdit(TextEdit edit)
    {
        ApplyFont(edit);
        InputStyles(edit);
        edit.AddThemeFontSizeOverride("font_size", 15);
    }

    public static void ApplyRichText(RichTextLabel label)
    {
        ApplyFont(label);
        SetColor(label, "default_color", WorkspacePalette.MainPanel.Text);
        label.AddThemeFontSizeOverride("normal_font_size", 14);
        label.AddThemeFontSizeOverride("bold_font_size", 15);
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty
        {
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4
        });
    }

    public static void ApplyLabel(Label label, Ui1TextRole role)
    {
        var p = WorkspacePalette.MainPanel;
        bool heading = role is Ui1TextRole.AppTitle or Ui1TextRole.PageTitle or Ui1TextRole.SectionTitle or Ui1TextRole.CardTitle;
        ApplyFont(label, heading);
        int size = role switch { Ui1TextRole.AppTitle => 23, Ui1TextRole.PageTitle => 26, Ui1TextRole.SectionTitle => 20,
            Ui1TextRole.CardTitle => 18, Ui1TextRole.Meta or Ui1TextRole.Muted or Ui1TextRole.Code => 13, _ => 15 };
        label.AddThemeFontSizeOverride("font_size", size);
        string token = role is Ui1TextRole.Meta or Ui1TextRole.Muted ? p.Secondary : p.Text;
        SetColor(label, "font_color", token);
        Color? semantic = role switch { Ui1TextRole.Success => Ui1Theme.Palette.Success, Ui1TextRole.Warning => p.Warning,
            Ui1TextRole.Error => p.Error, _ => null };
        if (semantic is { } color) label.AddThemeColorOverride("font_color", color);
    }

    public static Label Label(string text, Ui1TextRole role, bool wrap = false)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            VerticalAlignment = VerticalAlignment.Center };
        ApplyLabel(label, role);
        return label;
    }

    private static void InputStyles(Control input)
    {
        var p = WorkspacePalette.MainPanel;
        input.AddThemeStyleboxOverride("normal", CompactBox(p.Surface, p.Line, 1));
        input.AddThemeStyleboxOverride("read_only", CompactBox(p.Surface, p.Line, 1));
        input.AddThemeStyleboxOverride("focus", p.FocusRing());
        SetColor(input, "font_color", p.Text);
        SetColor(input, "font_placeholder_color", p.Secondary);
        SetColor(input, "caret_color", p.Text);
        SetColor(input, "selection_color", p.SelectedSurface);
    }

    private static StyleBoxFlat CompactBox(string fill, string border, int width)
    {
        var box = WorkspacePalette.MainPanel.Box(fill, border, width);
        box.ContentMarginLeft = box.ContentMarginRight = 8;
        box.ContentMarginTop = box.ContentMarginBottom = 4;
        return box;
    }

    private static void ThemeColor(Godot.Theme theme, string name, string type, string token)
    {
        theme.SetMeta("rt2_color_theme_color_" + type + "_" + name, token);
        theme.SetColor(name, type, WorkspacePalette.MainPanel.Color(token));
    }

}
