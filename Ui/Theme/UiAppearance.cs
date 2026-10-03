using Godot;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Theme;

/// <summary>Recolor RT2's own surfaces in place; never rebuild a running workspace or touch art assets.</summary>
internal static class UiAppearance
{
    private static UiColorPreferences _current = new();
    private static Dictionary<string, Color> _colors = Build(_current);
    internal static UiColorPreferences Current => _current;
    internal static Color Resolve(string hex)
    {
        var color = new Color(hex);
        return _colors.TryGetValue(color.ToHtml(false).ToUpperInvariant(), out var mapped)
            ? new Color(mapped.R, mapped.G, mapped.B, color.A) : color;
    }

    internal static void Initialize(UiColorPreferences? preferences)
    {
        _current = (preferences ?? new()).Normalize(); _colors = Build(_current);
        Ui1Theme.RefreshPalette();
    }

    internal static void Apply(Control root, UiColorPreferences preferences)
    {
        var previous = _colors;
        Initialize(preferences);
        var visited = new HashSet<ulong>();
        void Recolor(GodotObject target, StringName property, Color color, Action<Color> write)
        {
            if (color.A == 0) return;
            // Retain the original role across custom colors which happen to coincide.
            var key = new StringName("rt2_color_" + property.ToString().Replace('/', '_'));
            string? token = target.HasMeta(key) ? target.GetMeta(key).AsString() : null;
            bool Same(Color a, Color b) => a.ToHtml(false).Equals(b.ToHtml(false), StringComparison.OrdinalIgnoreCase);
            if (token is null || !previous.TryGetValue(token, out var prior) || !Same(prior, color))
                token = previous.FirstOrDefault(pair => Same(pair.Value, color)).Key;
            if (token is null || !_colors.TryGetValue(token, out var next)) return;
            target.SetMeta(key, token);
            write(new Color(next.R, next.G, next.B, color.A));
        }
        void Style(StyleBox? style)
        {
            if (style is not StyleBoxFlat box || !visited.Add(box.GetInstanceId())) return;
            Recolor(box, "bg", box.BgColor, c => box.BgColor = c);
            Recolor(box, "border", box.BorderColor, c => box.BorderColor = c);
        }
        void Visit(Node node)
        {
            if (node is Control control)
            {
                foreach (var entry in control.GetPropertyList())
                {
                    string name = entry["name"].AsString();
                    if (name.StartsWith("theme_override_colors/", StringComparison.Ordinal))
                    {
                        var value = control.Get(name);
                        if (value.VariantType == Variant.Type.Color)
                            Recolor(control, name, value.AsColor(), c => control.Set(name, c));
                    }
                    else if (name.StartsWith("theme_override_styles/", StringComparison.Ordinal))
                        Style(control.Get(name).AsGodotObject() as StyleBox);
                }
                if (control.Theme is { } theme && visited.Add(theme.GetInstanceId()))
                    foreach (var type in theme.GetTypeList())
                        foreach (var name in theme.GetStyleboxList(type)) Style(theme.GetStylebox(name, type));
                if (control is ColorRect rect) Recolor(rect, "color", rect.Color, c => rect.Color = c);
            }
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(root);
    }

    private static Dictionary<string, Color> Build(UiColorPreferences value)
    {
        var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        Color bg = new(value.Scheme == "custom" ? value.Background : "080B14");
        Color panel = new(value.Scheme == "custom" ? value.Panel : "131B30");
        Color accent = new(value.Scheme == "custom" ? value.Accent : "C69A43");
        Color text = new(value.Scheme == "custom" ? value.Text : "EEEAE1");
        void Role(Color chosen, params string[] sources)
        {
            foreach (string source in sources) map[source] = value.Scheme == "ink" ? new Color(source) : chosen;
        }
        Role(bg, "131C29", "080B14", "0A0F1C", "0B111E");
        Role(bg.Lerp(panel, .36f), "0D1220", "0C1322");
        Role(bg.Lerp(panel, .72f), "101827");
        Role(panel, "1E2B3C", "172238", "22292E");
        Role(panel.Lerp(text, .09f), "293B4E", "1D2B45", "1B2942");
        Role(panel.Lerp(text, .22f), "3B4D61", "273650", "3A4B68");
        Role(text, "E2E7EC", "EEEAE1");
        Role(text.Lerp(panel, .25f), "A7B8CB", "C5CCD8");
        Role(text.Lerp(panel, .48f), "78899C", "8996AA", "596477");
        Role(accent, "C9BA97", "C69A43", "69B6C7", "7EC4D2", "C0D7E5");
        Role(accent.Lerp(text, .64f), "D7E2E5");
        Role(panel.Lerp(accent, .18f), "14384A", "416783");
        // Ui1 buttons use small RGB offsets for hover/pressed, including translucent ghost buttons.
        foreach (var pair in map.ToArray())
            foreach (float amount in new[] { .07f, -.06f })
            {
                Color Shift(Color color) => new(Math.Clamp(color.R + amount, 0, 1),
                    Math.Clamp(color.G + amount, 0, 1), Math.Clamp(color.B + amount, 0, 1));
                map.TryAdd(Shift(new Color(pair.Key)).ToHtml(false).ToUpperInvariant(), Shift(pair.Value));
            }
        return map;
    }
}
