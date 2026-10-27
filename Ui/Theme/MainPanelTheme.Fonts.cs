using Godot;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Theme;

internal static partial class MainPanelTheme
{
    private static readonly Dictionary<string, (FontVariation Regular, FontVariation Medium)> Fonts = [];
    internal static string FontChoice { get; private set; } = "game-serif";

    private static void EnsureFonts()
    {
        if (_regular is not null) return;
        SelectFont(SearchWorkspace.Open(OS.GetUserDataDir()).Preferences.FontChoice);
    }

    internal static void SelectFont(string choice)
    {
        choice = SearchWorkspace.NormalizeFontChoice(choice);
        if (!Fonts.TryGetValue(choice, out var fonts))
        {
            FontVariation GameFont(string name)
            {
                // Reuse the native variation and its existing MSDF glyph cache.
                // A fresh wrapper around BaseFont has different variation metrics
                // and misses that cache. Never modify the shared font's settings.
                return ResourceLoader.Load<FontVariation>("res://themes/fonts/zhs/" + name + "_shared.tres")
                    ?? throw new InvalidOperationException("Game font unavailable: " + name);
            }
            fonts = choice switch
            {
                "game-serif" => (GameFont("source_han_serif_sc_medium"), GameFont("source_han_serif_sc_bold")),
                _ => (GameFont("noto_sans_mono_cjksc_regular"), GameFont("noto_sans_mono_cjksc_regular"))
            };
            Fonts.Add(choice, fonts);
        }
        _regular = fonts.Regular; _medium = fonts.Medium; FontChoice = choice;
    }

    // Explicit user action only. Keep UI instances and live work intact. Native
    // card faces and independently themed map/game surfaces keep their fonts.
    internal static void RefreshFonts(Control root)
    {
        var visitedThemes = new HashSet<ulong>();
        void Visit(Node node)
        {
            Godot.Theme? theme = node is Control control ? control.Theme : node is Window window ? window.Theme : null;
            if (theme?.HasMeta("rt2_independent_ui") == true) return;
            if (theme?.HasMeta("rt2_mainpanel") == true && visitedThemes.Add(theme.GetInstanceId()))
            {
                using var changes = new ThemeChangeBatch([theme]);
                if (theme.DefaultFont != Regular)
                {
                    changes.MarkChanged();
                    theme.DefaultFont = Regular;
                }
                foreach (string type in theme.GetTypeList())
                    foreach (string name in theme.GetFontList(type))
                    {
                        var font = name is "bold_font" or "bold_italics_font" ||
                            type is "Button" or "CheckBox" or "CheckButton" or "OptionButton" or "MenuButton" ? Medium : Regular;
                        if (theme.GetFont(name, type) == font) continue;
                        changes.MarkChanged();
                        theme.SetFont(name, type, font);
                    }
            }
            if (node is Control marked && marked.HasMeta("rt2_font_heading"))
                ApplyFont(marked, marked.GetMeta("rt2_font_heading").AsBool());
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(root);
    }
}
