using Godot;

namespace RolltheSpire2.Ui.Theme;

internal enum Ui1SurfaceRole
{
    Window,
    Header,
    Navigation,
    Page,
    Card,
    CardElevated,
    Input,
    Drawer,
    Status,
    Warning,
    Error,
    NotMigrated
}

internal enum Ui1ButtonRole
{
    Primary,
    Secondary,
    Ghost,
    Navigation,
    NavigationSelected,
    Danger,
    WindowControl
}

internal enum Ui1TextRole
{
    AppTitle,
    PageTitle,
    SectionTitle,
    CardTitle,
    Body,
    Meta,
    Muted,
    Accent,
    Success,
    Warning,
    Error,
    Code
}

internal static class Ui1Metrics
{
    public const float HeaderHeight = 56f;
    public const float StatusBarHeight = 34f;
    public const float NavigationWidth = 124f;
    public const float NavigationCompactWidth = 72f;
    public const float PageMargin = 24f;
    public const float CardGap = 16f;
    public const float CompactGap = 8f;
    public const float DefaultWidth = 1600f;
    public const float DefaultHeight = 900f;
    public const float MinimumWidth = 1120f;
    public const float MinimumHeight = 700f;
    public const float CompactThreshold = 1260f;
}

internal sealed class Ui1Palette
{
    public Color Window { get; init; }
    public Color Header { get; init; }
    public Color Navigation { get; init; }
    public Color Page { get; init; }
    public Color Card { get; init; }
    public Color CardHover { get; init; }
    public Color CardSelected { get; init; }
    public Color Input { get; init; }
    public Color Drawer { get; init; }
    public Color BorderSubtle { get; init; }
    public Color BorderNormal { get; init; }
    public Color BorderFocus { get; init; }
    public Color BorderSelected { get; init; }
    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextMuted { get; init; }
    public Color TextDisabled { get; init; }
    public Color AccentWarm { get; init; }
    public Color AccentFocus { get; init; }
    public Color Success { get; init; }
    public Color Partial { get; init; }
    public Color Unknown { get; init; }
    public Color Warning { get; init; }
    public Color Error { get; init; }
    public Color Unsupported { get; init; }
    public Color NotMigrated { get; init; }
}

internal static class Ui1Theme
{
    public static Ui1Palette Palette { get; } = new()
    {
        Window = Hex("#080B14", 0.996f),
        Header = Hex("#0D1220", 0.998f),
        Navigation = Hex("#0A0F1C", 0.996f),
        Page = Hex("#101827", 0.994f),
        Card = Hex("#172238", 0.998f),
        CardHover = Hex("#1D2B45", 1f),
        CardSelected = Hex("#1B2942", 1f),
        Input = Hex("#0B111E", 1f),
        Drawer = Hex("#0C1322", 1f),
        BorderSubtle = Hex("#273650", 0.86f),
        BorderNormal = Hex("#3A4B68", 0.96f),
        BorderFocus = Hex("#69B6C7", 1f),
        BorderSelected = Hex("#C69A43", 1f),
        TextPrimary = Hex("#EEEAE1", 1f),
        TextSecondary = Hex("#C5CCD8", 1f),
        TextMuted = Hex("#8996AA", 1f),
        TextDisabled = Hex("#596477", 1f),
        AccentWarm = Hex("#C69A43", 1f),
        AccentFocus = Hex("#7EC4D2", 1f),
        Success = Hex("#78B58A", 1f),
        Partial = Hex("#C9A95A", 1f),
        Unknown = Hex("#9AA7BA", 1f),
        Warning = Hex("#D1A04C", 1f),
        Error = Hex("#D66B63", 1f),
        Unsupported = Hex("#7E899C", 1f),
        NotMigrated = Hex("#9A8B72", 1f)
    };

    public static StyleBoxFlat Surface(Ui1SurfaceRole role, float radius = 4f, int borderWidth = 1)
    {
        Ui1Palette p = Palette;
        Color bg = role switch
        {
            Ui1SurfaceRole.Window => p.Window,
            Ui1SurfaceRole.Header => p.Header,
            Ui1SurfaceRole.Navigation => p.Navigation,
            Ui1SurfaceRole.Page => p.Page,
            Ui1SurfaceRole.Card => p.Card,
            Ui1SurfaceRole.CardElevated => p.CardSelected,
            Ui1SurfaceRole.Input => p.Input,
            Ui1SurfaceRole.Drawer => p.Drawer,
            Ui1SurfaceRole.Status => p.Header,
            Ui1SurfaceRole.Warning => new Color(0.10f, 0.13f, 0.19f, 1f),
            Ui1SurfaceRole.Error => new Color(0.18f, 0.075f, 0.085f, 0.998f),
            Ui1SurfaceRole.NotMigrated => new Color(0.12f, 0.15f, 0.21f, 0.998f),
            _ => p.Page
        };
        Color border = role switch
        {
            Ui1SurfaceRole.Window => p.BorderNormal,
            Ui1SurfaceRole.CardElevated => p.BorderNormal,
            Ui1SurfaceRole.Warning => p.Warning,
            Ui1SurfaceRole.Error => p.Error,
            Ui1SurfaceRole.NotMigrated => p.NotMigrated,
            Ui1SurfaceRole.Input => p.BorderNormal,
            _ => p.BorderSubtle
        };

        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = (int)radius,
            CornerRadiusTopRight = (int)radius,
            CornerRadiusBottomLeft = (int)radius,
            CornerRadiusBottomRight = (int)radius,
            ShadowColor = role == Ui1SurfaceRole.Window ? new Color(0f, 0f, 0f, 0.68f) : new Color(0f, 0f, 0f, 0.28f),
            ShadowSize = role == Ui1SurfaceRole.Window ? 10 : 3
        };
        return box;
    }

    public static void SetMargins(StyleBoxFlat box, float left, float top, float right, float bottom)
    {
        box.ContentMarginLeft = left;
        box.ContentMarginTop = top;
        box.ContentMarginRight = right;
        box.ContentMarginBottom = bottom;
    }

    public static void ApplyPanel(PanelContainer panel, Ui1SurfaceRole role, float radius = 4f, int borderWidth = 1, float margin = 0f)
    {
        StyleBoxFlat box = Surface(role, radius, borderWidth);
        if (margin > 0f)
        {
            SetMargins(box, margin, margin, margin, margin);
        }
        panel.AddThemeStyleboxOverride("panel", box);
    }

    public static void ApplyPanel(Panel panel, Ui1SurfaceRole role, float radius = 4f, int borderWidth = 1)
    {
        panel.AddThemeStyleboxOverride("panel", Surface(role, radius, borderWidth));
    }

    public static void ApplyButton(Button button, Ui1ButtonRole role)
    {
        Ui1Palette p = Palette;
        Color normal = role switch
        {
            Ui1ButtonRole.Primary => new Color(0.08f, 0.22f, 0.29f, 1f),
            Ui1ButtonRole.NavigationSelected => p.CardSelected,
            Ui1ButtonRole.Navigation => p.Navigation,
            Ui1ButtonRole.Ghost => new Color(p.Card.R, p.Card.G, p.Card.B, 0.32f),
            Ui1ButtonRole.Danger => new Color(0.24f, 0.09f, 0.07f, 1f),
            Ui1ButtonRole.WindowControl => new Color(0f, 0f, 0f, 0f),
            _ => p.Card
        };
        Color border = role switch
        {
            Ui1ButtonRole.Primary => p.BorderSelected,
            Ui1ButtonRole.NavigationSelected => p.BorderSelected,
            Ui1ButtonRole.Danger => p.Error,
            Ui1ButtonRole.WindowControl => new Color(0f, 0f, 0f, 0f),
            _ => p.BorderSubtle
        };
        Color text = role == Ui1ButtonRole.Ghost ? p.TextMuted : p.TextPrimary;

        button.AddThemeStyleboxOverride("normal", ButtonBox(normal, border));
        button.AddThemeStyleboxOverride("hover", ButtonBox(Lighten(normal, 0.07f), p.BorderFocus));
        button.AddThemeStyleboxOverride("pressed", ButtonBox(Darken(normal, 0.06f), p.BorderSelected));
        button.AddThemeStyleboxOverride("focus", ButtonBox(new Color(0f, 0f, 0f, 0f), p.BorderFocus));
        button.AddThemeStyleboxOverride("disabled", ButtonBox(new Color(normal.R, normal.G, normal.B, 0.36f), new Color(border.R, border.G, border.B, 0.34f)));
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_hover_color", p.TextPrimary);
        button.AddThemeColorOverride("font_pressed_color", p.TextPrimary);
        button.AddThemeColorOverride("font_focus_color", p.TextPrimary);
        button.AddThemeColorOverride("font_disabled_color", p.TextDisabled);
        button.AddThemeFontSizeOverride("font_size", 15);
        button.FocusMode = Control.FocusModeEnum.All;
    }

    public static void ApplyLineEdit(LineEdit edit)
    {
        Ui1Palette p = Palette;
        StyleBoxFlat normal = Surface(Ui1SurfaceRole.Input, 3f, 1);
        SetMargins(normal, 10f, 8f, 10f, 8f);
        StyleBoxFlat focus = Surface(Ui1SurfaceRole.Input, 3f, 2);
        focus.BorderColor = p.BorderFocus;
        SetMargins(focus, 10f, 8f, 10f, 8f);
        edit.AddThemeStyleboxOverride("normal", normal);
        edit.AddThemeStyleboxOverride("focus", focus);
        edit.AddThemeStyleboxOverride("read_only", normal);
        edit.AddThemeColorOverride("font_color", p.TextPrimary);
        edit.AddThemeColorOverride("font_placeholder_color", p.TextMuted);
        edit.AddThemeColorOverride("caret_color", p.AccentFocus);
        edit.AddThemeColorOverride("selection_color", new Color(p.AccentWarm.R, p.AccentWarm.G, p.AccentWarm.B, 0.35f));
        edit.AddThemeFontSizeOverride("font_size", 16);
    }

    public static void ApplyTextEdit(TextEdit edit)
    {
        Ui1Palette p = Palette;
        StyleBoxFlat normal = Surface(Ui1SurfaceRole.Input, 3f, 1);
        SetMargins(normal, 10f, 8f, 10f, 8f);
        StyleBoxFlat focus = Surface(Ui1SurfaceRole.Input, 3f, 2);
        focus.BorderColor = p.BorderFocus;
        SetMargins(focus, 10f, 8f, 10f, 8f);
        edit.AddThemeStyleboxOverride("normal", normal);
        edit.AddThemeStyleboxOverride("focus", focus);
        edit.AddThemeStyleboxOverride("read_only", normal);
        edit.AddThemeColorOverride("font_color", p.TextPrimary);
        edit.AddThemeColorOverride("font_placeholder_color", p.TextMuted);
        edit.AddThemeColorOverride("caret_color", p.AccentFocus);
        edit.AddThemeColorOverride("selection_color", new Color(p.AccentWarm.R, p.AccentWarm.G, p.AccentWarm.B, 0.35f));
        edit.AddThemeFontSizeOverride("font_size", 15);
    }

    public static void ApplyOptionButton(OptionButton option)
    {
        ApplyButton(option, Ui1ButtonRole.Secondary);
    }

    public static void ApplyLabel(Label label, Ui1TextRole role)
    {
        Ui1Palette p = Palette;
        Color color = role switch
        {
            Ui1TextRole.AppTitle => p.TextPrimary,
            Ui1TextRole.PageTitle => p.TextPrimary,
            Ui1TextRole.SectionTitle => p.TextPrimary,
            Ui1TextRole.CardTitle => p.TextPrimary,
            Ui1TextRole.Meta => p.TextSecondary,
            Ui1TextRole.Muted => p.TextMuted,
            Ui1TextRole.Accent => p.AccentFocus,
            Ui1TextRole.Success => p.Success,
            Ui1TextRole.Warning => p.Warning,
            Ui1TextRole.Error => p.Error,
            Ui1TextRole.Code => p.AccentFocus,
            _ => p.TextPrimary
        };
        int size = role switch
        {
            Ui1TextRole.AppTitle => 23,
            Ui1TextRole.PageTitle => 26,
            Ui1TextRole.SectionTitle => 20,
            Ui1TextRole.CardTitle => 18,
            Ui1TextRole.Meta => 13,
            Ui1TextRole.Muted => 13,
            Ui1TextRole.Code => 13,
            _ => 15
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.48f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", size);
    }

    public static void ApplyRichText(RichTextLabel label)
    {
        label.AddThemeColorOverride("default_color", Palette.TextPrimary);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.44f));
        label.AddThemeFontSizeOverride("normal_font_size", 14);
        label.AddThemeFontSizeOverride("bold_font_size", 15);
        label.AddThemeStyleboxOverride("normal", Surface(Ui1SurfaceRole.Input, 3f, 1));
    }

    public static void ApplyCheckButton(CheckButton check)
    {
        check.AddThemeColorOverride("font_color", Palette.TextSecondary);
        check.AddThemeColorOverride("font_hover_color", Palette.TextPrimary);
        check.AddThemeFontSizeOverride("font_size", 14);
    }

    public static void ApplySeparator(HSeparator separator)
    {
        var line = new StyleBoxFlat
        {
            BgColor = Palette.BorderSubtle,
            ContentMarginTop = 1f,
            ContentMarginBottom = 1f
        };
        separator.AddThemeStyleboxOverride("separator", line);
    }

    public static void ApplySeparator(VSeparator separator)
    {
        var line = new StyleBoxFlat
        {
            BgColor = Palette.BorderSubtle,
            ContentMarginLeft = 1f,
            ContentMarginRight = 1f
        };
        separator.AddThemeStyleboxOverride("separator", line);
    }

    public static Label Label(string text, Ui1TextRole role, bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyLabel(label, role);
        return label;
    }

    private static StyleBoxFlat ButtonBox(Color bg, Color border)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3
        };
        SetMargins(box, 10f, 7f, 10f, 7f);
        return box;
    }

    private static Color Hex(string hex, float alpha)
    {
        string value = hex.TrimStart('#');
        byte r = Convert.ToByte(value.Substring(0, 2), 16);
        byte g = Convert.ToByte(value.Substring(2, 2), 16);
        byte b = Convert.ToByte(value.Substring(4, 2), 16);
        return new Color(r / 255f, g / 255f, b / 255f, alpha);
    }

    private static Color Lighten(Color color, float amount) => new(
        Math.Min(1f, color.R + amount),
        Math.Min(1f, color.G + amount),
        Math.Min(1f, color.B + amount),
        color.A);

    private static Color Darken(Color color, float amount) => new(
        Math.Max(0f, color.R - amount),
        Math.Max(0f, color.G - amount),
        Math.Max(0f, color.B - amount),
        color.A);
}
