using Godot;

namespace RolltheSpire2.Ui.Theme;

// Stable color tokens for the 1.3 Blue ink scheme; UiAppearance resolves the user's palette.
internal sealed record WorkspacePalette(string Canvas, string Surface, string Hover, string Line,
    string Text, string Secondary, string Disabled, string Active, string Selected, string Primary)
{
    public static readonly WorkspacePalette Canonical = new("131C29", "1E2B3C", "293B4E", "3B4D61", "E2E7EC", "A7B8CB", "78899C", "C9BA97", "C9BA97", "D7E2E5");
    public string NeutralSurface => "22292E";
    public Color Color(string hex) => UiAppearance.Resolve(hex);
    public Color Warning => new("D99783");
    public Color Error => new("D98780");
    public Color Confirmed => Color(Selected);

    // Shared resources also cover controls rebuilt while opening/closing a modal.
    // Keep actual Godot focus for Tab navigation; only its visible decoration follows input modality.
    private static readonly StyleBoxFlat KeyboardFocus = CreateFocusRing();
    private static readonly StyleBoxFlat PrimaryKeyboardFocus = CreateFocusRing();
    private static bool _keyboardFocusVisible;
    public StyleBoxFlat FocusRing(bool primary = false) => primary ? PrimaryKeyboardFocus : KeyboardFocus;
    private static StyleBoxFlat CreateFocusRing() => new()
    {
        DrawCenter = false, BorderColor = new Color(0, 0, 0, 0),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        // Inset instead of painting outside the hit rectangle into adjacent controls / clipped scroll edges.
        ExpandMarginLeft = -4, ExpandMarginTop = -4, ExpandMarginRight = -4, ExpandMarginBottom = -4,
        ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 0, ContentMarginBottom = 0
    };

    public static void SetKeyboardFocusVisible(bool visible)
    {
        if (_keyboardFocusVisible == visible) return;
        _keyboardFocusVisible = visible;
        KeyboardFocus.BorderColor = visible ? UiAppearance.Resolve("C0D7E5") : new Color(0, 0, 0, 0);
        PrimaryKeyboardFocus.BorderColor = visible ? UiAppearance.Resolve("416783") : new Color(0, 0, 0, 0);
    }

    public Label Label(string text, int size = 20, bool secondary = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Color(secondary ? Secondary : Text));
        return label;
    }

    public StyleBoxFlat Box(string fill, string? border = null, int width = 0) => new()
    {
        BgColor = Color(fill), BorderColor = Color(border ?? Line),
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8
    };

    public Button Button(string text, bool primary = false, bool selected = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 48) };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.AddThemeColorOverride("font_color", Color(primary ? Canvas : Text));
        button.AddThemeColorOverride("font_hover_color", Color(primary ? Canvas : Text));
        button.AddThemeColorOverride("font_pressed_color", Color(primary ? Canvas : Text));
        button.AddThemeColorOverride("font_focus_color", Color(primary ? Canvas : Text));
        button.AddThemeColorOverride("font_disabled_color", Color(Disabled));
        button.AddThemeStyleboxOverride("normal", Box(primary ? Primary : Surface, selected ? Selected : Line, selected ? 2 : 0));
        button.AddThemeStyleboxOverride("hover", Box(primary ? Text : Hover, selected ? Selected : Line, 1));
        button.AddThemeStyleboxOverride("pressed", Box(primary ? Secondary : Line, selected ? Selected : Line, selected ? 2 : 0));
        button.AddThemeStyleboxOverride("disabled", Box(Canvas));
        button.AddThemeStyleboxOverride("focus", FocusRing(primary));
        return button;
    }

    public void SetActive(Button button, bool active, int horizontalPadding = 16)
    {
        foreach (var (state, fill) in new[] { ("normal", Canvas), ("hover", Hover), ("pressed", Line) })
        {
            var box = Box(fill);
            box.ContentMarginLeft = box.ContentMarginRight = horizontalPadding;
            box.BorderColor = Color(Active);
            box.BorderWidthBottom = active ? 3 : 0;
            button.AddThemeStyleboxOverride(state, box);
        }
        button.AddThemeColorOverride("font_color", Color(active ? Text : Secondary));
    }

    // Fixed-size authoring controls must include theme padding in their height,
    // otherwise a small requested cell can grow into its neighbour.
    public Button CompactButton(string text, float height = 36, int fontSize = 16, bool selected = false)
    {
        var button = Button(text, selected: selected);
        button.CustomMinimumSize = new Vector2(0, height);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.ClipText = true;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.TooltipText = text;
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var style = (StyleBox)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginLeft = style.ContentMarginRight = 8;
            style.ContentMarginTop = style.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride(state, style);
        }
        return button;
    }
}
