using Godot;

namespace RolltheSpire2.Ui.Theme;

// Stable color tokens for the 1.3 Blue ink scheme; UiAppearance resolves the user's palette.
internal sealed record WorkspacePalette(string Canvas, string Surface, string Hover, string Line,
    string Text, string Secondary, string Disabled, string Active, string Selected, string Primary)
{
    public static readonly WorkspacePalette Canonical = new("131C29", "1E2B3C", "293B4E", "3B4D61", "E2E7EC", "A7B8CB", "78899C", "C9BA97", "C9BA97", "D7E2E5");
    public static readonly WorkspacePalette MainPanel = new("070D1A", "0B1224", "182543", "3A587E", "E5EDF8", "AFBED4", "71839C", "C7B07B", "C7B07B", "151E32") { IsMainPanel = true };
    internal bool IsMainPanel { get; private init; }
    public string NeutralSurface => IsMainPanel ? Surface : "22292E";
    public string OptionSurface => IsMainPanel ? "151E32" : Surface;
    public string ContentSurface => IsMainPanel ? "151E32" : Surface;
    public string CandidateSurface => IsMainPanel ? "1C2940" : OptionSurface;
    public string SelectedSurface => IsMainPanel ? "1C2A4A" : Hover;
    public Color Color(string hex) => UiAppearance.Resolve(hex);
    public Color Warning => new("D99783");
    public Color Error => new("D98780");
    public Color Confirmed => Color(Selected);

    // Shared resources also cover controls rebuilt while opening/closing a modal.
    // Keep actual Godot focus for Tab navigation; only its visible decoration follows input modality.
    private static readonly StyleBoxFlat KeyboardFocus = CreateFocusRing();
    private static readonly StyleBoxFlat PrimaryKeyboardFocus = CreateFocusRing();
    private static readonly StyleBoxFlat MainPanelKeyboardFocus = CreateFocusRing();
    private static bool _keyboardFocusVisible;
    public StyleBoxFlat FocusRing(bool primary = false)
    {
        if (!IsMainPanel) return primary ? PrimaryKeyboardFocus : KeyboardFocus;
        MainPanelKeyboardFocus.SetMeta("rt2_mainpanel", true);
        MainPanelKeyboardFocus.SetMeta("rt2_color_border", Line);
        return MainPanelKeyboardFocus;
    }
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
        MainPanelKeyboardFocus.BorderColor = visible ? MainPanel.Color(MainPanel.Line) : new Color(0, 0, 0, 0);
        MainPanelKeyboardFocus.SetMeta("rt2_color_border", MainPanel.Line);
    }

    public Label Label(string text, int size = 20, bool secondary = false, bool heading = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Color(secondary ? Secondary : Text));
        if (IsMainPanel)
        {
            MainPanelTheme.ApplyFont(label, heading);
            MainPanelTheme.SetColor(label, "font_color", secondary ? Secondary : Text);
        }
        return label;
    }

    public StyleBoxFlat Box(string fill, string? border = null, int width = 0)
    {
        var box = new StyleBoxFlat
        {
            BgColor = Color(fill), BorderColor = Color(border ?? Line),
            BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8
        };
        if (IsMainPanel)
        {
            box.SetMeta("rt2_mainpanel", true);
            box.SetMeta("rt2_color_bg", fill);
            box.SetMeta("rt2_color_border", border ?? Line);
        }
        return box;
    }

    // Content planes carry the grouping; their outline must stay quieter than
    // workspace boundaries and actual authored-condition borders.
    public StyleBoxFlat ContentBox(int borderWidth = 1)
    {
        var box = Box(ContentSurface, Line, borderWidth);
        box.BorderColor = new Color(box.BorderColor, IsMainPanel ? .4f : 1);
        return box;
    }

    public StyleBoxFlat EmptySlotBox()
    {
        var box = Box(Surface);
        if (IsMainPanel) box.BgColor = new Color(box.BgColor, .55f);
        return box;
    }

    // Explicit opt-in for game-object choices inside a content plane. Navigation
    // and ordinary editor actions retain their existing resting appearance.
    public void ApplyCandidateSurface(Button button, bool selected = false)
    {
        button.SetMeta("rt2_candidate_item", true);
        if (selected) return; // Keep the caller's authored gold / mode selection.
        foreach (var (state, fill) in new[] { ("normal", CandidateSurface),
            ("hover", IsMainPanel ? "243650" : Hover), ("pressed", SelectedSurface) })
        {
            var style = (StyleBoxFlat)button.GetThemeStylebox(state).Duplicate();
            style.BgColor = Color(fill);
            if (IsMainPanel) style.SetMeta("rt2_color_bg", fill);
            style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 0;
            button.AddThemeStyleboxOverride(state, style);
        }
    }

    public Button Button(string text, bool primary = false, bool selected = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 48) };
        button.AddThemeFontSizeOverride("font_size", 20);
        if (IsMainPanel)
        {
            ApplyButton(button, primary, selected);
            return button;
        }
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
        if (IsMainPanel)
        {
            foreach (string state in new[] { "normal", "hover", "pressed" })
            {
                var box = Box(state == "normal" ? OptionSurface : state == "hover" ? Hover : SelectedSurface);
                // Transparent at rest still needs a visible pointer response.
                // Keep the current-page underline on every pointer state.
                box.BgColor = new Color(box.BgColor, state == "normal" ? 0 : state == "hover" ? .55f : .8f);
                box.BorderColor = Color(Active);
                box.SetMeta("rt2_color_border", Active);
                box.ContentMarginLeft = box.ContentMarginRight = horizontalPadding;
                box.BorderWidthBottom = active ? 2 : 0;
                button.AddThemeStyleboxOverride(state, box);
            }
            MainPanelTheme.SetColor(button, "font_color", active ? Text : Secondary);
            button.AddThemeStyleboxOverride("focus", FocusRing());
            return;
        }
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

    // Actual query members are selected explicitly by the caller. Primary actions
    // and editor modes keep blue decoration and never imply an exported condition.
    public void ApplyButton(Button button, bool primary = false, bool selected = false)
    {
        if (!IsMainPanel) throw new InvalidOperationException("ApplyButton belongs to the main panel palette.");
        MainPanelTheme.ApplyFont(button, heading: true);
        foreach (string name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            MainPanelTheme.SetColor(button, name, selected ? Selected : Text);
        MainPanelTheme.SetColor(button, "font_disabled_color", Disabled);
        foreach (var (state, fill) in new[] { ("normal", selected ? SelectedSurface : OptionSurface),
            ("hover", Hover), ("pressed", SelectedSurface), ("disabled", Surface) })
            button.AddThemeStyleboxOverride(state, Box(fill, selected && state != "disabled" ? Selected : Line,
                selected && state != "disabled" ? 2 : state == "hover" || primary ? 1 : 0));
        button.AddThemeStyleboxOverride("focus", FocusRing());
    }

    public void SetModeActive(Button button, bool active, int horizontalPadding = 8)
    {
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            StyleBoxFlat box = button.GetThemeStylebox(state) is StyleBoxFlat existing
                ? (StyleBoxFlat)existing.Duplicate() : Box(OptionSurface);
            string fill = state == "disabled" ? Surface : active ? SelectedSurface : state == "hover" ? Hover : OptionSurface;
            box.BgColor = Color(fill); box.BorderColor = Color(Line);
            box.BorderWidthLeft = box.BorderWidthTop = box.BorderWidthRight = box.BorderWidthBottom = active ? 1 : 0;
            box.ContentMarginLeft = box.ContentMarginRight = horizontalPadding;
            if (IsMainPanel) { box.SetMeta("rt2_mainpanel", true); box.SetMeta("rt2_color_bg", fill); box.SetMeta("rt2_color_border", Line); }
            button.AddThemeStyleboxOverride(state, box);
        }
        foreach (string name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            if (IsMainPanel) MainPanelTheme.SetColor(button, name, Text); else button.AddThemeColorOverride(name, Color(Text));
        button.AddThemeStyleboxOverride("focus", FocusRing());
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
