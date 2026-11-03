using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private const int PathIconSize = 40, PathFontSize = 23;
    private const float IconPathGap = 6, PathTokenGap = 5, PathBracketWidth = 12, PathArrowWidth = 22, PathWrapWidth = 480;
    private const int PathPaddingX = 6, PathPaddingY = 3;
    private const int AlternativeBottleSize = 36, AlternativeBottleOffset = 24, AlternativeOrFontSize = 16;
    private static readonly ImmutableArray<ModelKey> EquivalentCardPotions =
        [new("POTION", "COLORLESS_POTION"), new("POTION", "COSMIC_CONCOCTION")];
    // Follow the sprite silhouette so the original reward art remains visible through transparent areas.
    private static readonly ShaderMaterial PathIconOutline = new() { Shader = new Shader { Code = """
        shader_type canvas_item;
        void fragment() {
            vec2 step_uv = fwidth(UV) * 1.75;
            float edge = 0.0;
            for (int i = 0; i < 8; i++) {
                float angle = float(i) * TAU / 8.0;
                edge = max(edge, texture(TEXTURE, UV + vec2(cos(angle), sin(angle)) * step_uv).a);
            }
            vec4 icon = COLOR;
            float under = (1.0 - icon.a) * edge * 0.95;
            float alpha = icon.a + under;
            COLOR = vec4((icon.rgb * icon.a + vec3(0.065, 0.046, 0.035) * under) / max(alpha, 0.0001), alpha);
        }
        """ } };
    private sealed record PathToken(ModelKey? Model, string Symbol, string NodeName,
        int Segment = -1, bool Exchangeable = false, bool Target = false, bool RouteAlternative = false,
        ImmutableArray<ModelKey> Alternatives = default, bool AlternativeArtwork = false)
    {
        internal float Width { get; } = !Alternatives.IsDefaultOrEmpty
            ? AlternativeArtwork ? AlternativeBottleSize + AlternativeBottleOffset :
                Math.Max(AlternativeBottleSize + AlternativeBottleOffset, MainPanelTheme.Medium.GetStringSize(
                    AlternativeText(Alternatives), fontSize: PathFontSize).X)
            : Model.HasValue ? PathIconSize : Symbol is "[" or "]" ? PathBracketWidth :
            Math.Max(PathArrowWidth, MainPanelTheme.Medium.GetStringSize(Symbol, fontSize: PathFontSize).X);
    }
    private sealed record PathRoute(ImmutableArray<ImmutableArray<ModelKey>> Segments,
        int AlternativeSegment = -1, ModelKey? ReplacedPotion = null);
    private sealed record PathRow(List<PathToken> Tokens, int PathIndex, bool Continuation);
    private sealed record IconPathPresentation(string Language, List<PathRow> Rows, Vector2 Size, string Text);

    // Paths and language are immutable for this preview. Placement reuses the
    // same formatted tokens and dimensions instead of sorting routes per frame.
    private IconPathPresentation PathsFor(CardStack stack)
    {
        if (stack.PathPresentation is not { } cached || cached.Language != UiLanguagePreferences.LanguageCode)
            stack.PathPresentation = BuildIconPaths(stack.IconPaths);
        return stack.PathPresentation;
    }

    private IconPathPresentation BuildIconPaths(ImmutableArray<OperationIconPath> paths)
    {
        var rows = new List<PathRow>();
        if (!paths.IsDefaultOrEmpty)
        {
            string SegmentKey(ImmutableArray<ModelKey> segment) => string.Join("|", segment.Select(key => key.Serialized).Order(StringComparer.Ordinal));
            var distinct = paths.DistinctBy(path => string.Join("/", path.Segments.Select(SegmentKey))).ToArray();
            var first = distinct[0].Segments;
            var routes = PairEquivalentPotions(distinct);
            // One varying step can be replaced by a same-step choice without
            // creating combinations that were absent from the complete routes.
            var differences = Enumerable.Range(0, first.Length).Where(index =>
                distinct.Any(path => path.Segments.Length != first.Length || SegmentKey(path.Segments[index]) != SegmentKey(first[index]))).ToArray();
            bool compact = distinct.Length > 1 && distinct.All(path => path.Segments.Length == first.Length) && differences.Length == 1;
            if (compact)
            {
                var units = new List<List<PathToken>>();
                for (int index = 0; index < first.Length; index++)
                {
                    var unit = new List<PathToken>();
                    if (index > 0) unit.Add(Arrow());
                    if (index == differences[0])
                    {
                        for (int alternative = 0; alternative < routes.Count; alternative++)
                        {
                            if (alternative > 0) unit.Add(Or(false));
                            unit.AddRange(Segment(routes[alternative], index));
                        }
                    }
                    else unit.AddRange(Segment(new(first), index));
                    units.Add(unit);
                }
                AppendRows(units, 0);
            }
            else
            {
                // Complex alternatives remain independent complete routes.
                // A new line and its leading 'or' separate them; no parentheses
                // or distributed choices can suggest an unintended product.
                for (int alternative = 0; alternative < routes.Count; alternative++)
                {
                    var units = new List<List<PathToken>>();
                    var route = routes[alternative];
                    var segments = route.Segments;
                    for (int index = 0; index < segments.Length; index++)
                    {
                        var unit = new List<PathToken>();
                        if (index > 0) unit.Add(Arrow());
                        else if (alternative > 0) unit.Add(Or(true));
                        unit.AddRange(Segment(route, index));
                        units.Add(unit);
                    }
                    AppendRows(units, alternative);
                }
            }
        }
        float width = rows.Count == 0 ? 0 : rows.Max(row => TokensWidth(row.Tokens)) + PathPaddingX * 2;
        var size = new Vector2(width, rows.Count * (PathIconSize + PathPaddingY * 2 + IconPathGap));
        string text = IconPathsText(paths);
        return new(UiLanguagePreferences.LanguageCode, rows, size, text);

        void AppendRows(List<List<PathToken>> units, int pathIndex)
        {
            var tokens = new List<PathToken>();
            bool continuation = false;
            foreach (var unit in units)
            {
                if (tokens.Count > 0 && TokensWidth(tokens) + PathTokenGap + TokensWidth(unit) > PathWrapWidth - PathPaddingX * 2)
                {
                    rows.Add(new(tokens, pathIndex, continuation));
                    tokens = []; continuation = true;
                }
                tokens.AddRange(unit);
            }
            if (tokens.Count > 0) rows.Add(new(tokens, pathIndex, continuation));
        }
        PathToken Arrow() => new(null, "→", "Rt2PathArrow");
        PathToken Or(bool route) => new(null, UiText.Get("operation.path.or"), "Rt2PathOr", RouteAlternative: route);
        List<PathToken> Segment(PathRoute route, int index)
        {
            var result = new List<PathToken>();
            var segments = route.Segments;
            var segment = segments[index];
            if (segment.Length > 1) result.Add(new(null, "[", "Rt2PathOpenBracket"));
            bool replaced = false;
            foreach (var key in segment)
            {
                if (!replaced && route.AlternativeSegment == index && route.ReplacedPotion == key)
                {
                    bool artwork = EquivalentCardPotions.All(potion => Icons.Resolve(potion,
                        GameContentKind.Potion, IconVariant.RelicLarge).Texture != null);
                    result.Add(new(null, "", "Rt2IconAlternative", index, segment.Length > 1,
                        index == segments.Length - 1, Alternatives: EquivalentCardPotions, AlternativeArtwork: artwork));
                    replaced = true;
                }
                else result.Add(new(key, "", "Rt2IconStep", index, segment.Length > 1, index == segments.Length - 1));
            }
            if (segment.Length > 1) result.Add(new(null, "]", "Rt2PathCloseBracket"));
            return result;
        }
    }

    // Pair complete routes locally. Each pair differs by exactly one use of
    // these two potions; all other steps and required members remain identical.
    // Consuming each route once avoids factoring correlated differences into
    // independent choices, including when other routes cannot be paired.
    private static List<PathRoute> PairEquivalentPotions(OperationIconPath[] paths)
    {
        var result = new List<PathRoute>();
        var used = new bool[paths.Length];
        for (int left = 0; left < paths.Length; left++)
        {
            if (used[left]) continue;
            var route = new PathRoute(paths[left].Segments);
            for (int right = left + 1; right < paths.Length; right++)
            {
                if (used[right] || !TryEquivalentPotionStep(paths[left], paths[right], out int step, out var potion)) continue;
                route = route with { AlternativeSegment = step, ReplacedPotion = potion };
                used[right] = true;
                break;
            }
            result.Add(route);
        }
        return result;
    }

    private static bool TryEquivalentPotionStep(OperationIconPath left, OperationIconPath right, out int step, out ModelKey potion)
    {
        step = -1; potion = default;
        if (left.Segments.Length != right.Segments.Length) return false;
        for (int index = 0; index < left.Segments.Length; index++)
        {
            var a = left.Segments[index]; var b = right.Segments[index];
            var aKeys = a.Select(key => key.Serialized).Order(StringComparer.Ordinal).ToArray();
            var bKeys = b.Select(key => key.Serialized).Order(StringComparer.Ordinal).ToArray();
            if (aKeys.SequenceEqual(bKeys)) continue;
            if (step >= 0 || a.Length != b.Length) return false;
            foreach (var candidate in EquivalentCardPotions)
            {
                var counterpart = EquivalentCardPotions.First(key => key != candidate);
                if (!a.Contains(candidate) || !b.Contains(counterpart)) continue;
                var aRemainder = a.ToList(); aRemainder.Remove(candidate);
                var bRemainder = b.ToList(); bRemainder.Remove(counterpart);
                if (!aRemainder.Select(key => key.Serialized).Order(StringComparer.Ordinal)
                    .SequenceEqual(bRemainder.Select(key => key.Serialized).Order(StringComparer.Ordinal))) continue;
                step = index; potion = candidate;
                break;
            }
            if (step < 0) return false;
        }
        return step >= 0;
    }

    private static string AlternativeText(ImmutableArray<ModelKey> alternatives) =>
        "(" + string.Join(" " + UiText.Get("operation.path.or") + " ", alternatives.Select(NameOf)) + ")";

    private static string TokenText(PathToken token) => !token.Alternatives.IsDefaultOrEmpty
        ? AlternativeText(token.Alternatives)
        : token.Model is { } key ? NameOf(key) : token.Symbol;

    private static float TokensWidth(List<PathToken> tokens) => tokens.Sum(token => token.Width) + Math.Max(0, tokens.Count - 1) * PathTokenGap;
    private Vector2 IconPathsSize(CardStack stack) => PathsFor(stack).Size;

    private Control IconPathsStrip(CardStack stack)
    {
        var list = new VBoxContainer { Name = "Rt2IconPaths", MouseFilter = MouseFilterEnum.Ignore };
        list.AddThemeConstantOverride("separation", (int)IconPathGap);
        int tokenIndex = 0;
        foreach (var pathRow in PathsFor(stack).Rows)
        {
            int rowIndex = list.GetChildCount();
            // The background belongs to this complete line, not to each icon
            // or to the wider card group. A lone White Star stays a narrow tab.
            var backing = new PanelContainer { Name = "Rt2IconPathPanel_" + rowIndex,
                MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            backing.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.045f, 0.075f, 0.12f, 0.86f),
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                ContentMarginLeft = PathPaddingX, ContentMarginRight = PathPaddingX,
                ContentMarginTop = PathPaddingY, ContentMarginBottom = PathPaddingY
            });
            list.AddChild(backing);
            var row = new HBoxContainer { Name = "Rt2IconPath_" + rowIndex,
                MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            row.SetMeta("rt2_path_index", pathRow.PathIndex);
            row.SetMeta("rt2_continuation", pathRow.Continuation);
            row.AddThemeConstantOverride("separation", (int)PathTokenGap);
            backing.AddChild(row);
            foreach (var token in pathRow.Tokens)
            {
                Control control;
                if (!token.Alternatives.IsDefaultOrEmpty)
                {
                    control = PotionAlternativeIcon(token);
                    control.Name = "Rt2IconAlternative_" + tokenIndex;
                    control.SetMeta("rt2_alternatives", token.Alternatives.Select(key => key.Serialized).ToArray());
                    control.SetMeta("rt2_segment", token.Segment);
                    control.SetMeta("rt2_exchangeable", token.Exchangeable);
                    control.SetMeta("rt2_target", token.Target);
                }
                else if (token.Model is { } key)
                {
                    control = ModelIcon(key, PathIconSize);
                    if (control is TextureRect icon) icon.Material = PathIconOutline;
                    control.Name = "Rt2IconStep_" + tokenIndex;
                    control.SetMeta("rt2_model", key.Serialized);
                    control.SetMeta("rt2_segment", token.Segment);
                    control.SetMeta("rt2_exchangeable", token.Exchangeable);
                    control.SetMeta("rt2_target", token.Target);
                }
                else
                {
                    var symbol = PathSymbol(token.Symbol, PathFontSize);
                    symbol.Name = token.NodeName;
                    symbol.CustomMinimumSize = new(token.Width, PathIconSize);
                    symbol.SetMeta("rt2_route_alternative", token.RouteAlternative);
                    control = symbol;
                }
                row.AddChild(control);
                tokenIndex++;
            }
        }
        return list;
    }

    private Control PotionAlternativeIcon(PathToken token)
    {
        var choice = new Control { Size = new(token.Width, PathIconSize), CustomMinimumSize = new(token.Width, PathIconSize),
            MouseFilter = MouseFilterEnum.Ignore };
        if (!token.AlternativeArtwork)
        {
            var text = PathSymbol(TokenText(token), PathFontSize);
            text.Name = "Rt2AlternativeText";
            text.Size = choice.Size;
            choice.AddChild(text);
            return choice;
        }
        for (int index = 0; index < token.Alternatives.Length; index++)
        {
            var bottle = ModelIcon(token.Alternatives[index], AlternativeBottleSize);
            bottle.Name = "Rt2AlternativeBottle_" + index;
            bottle.SetMeta("rt2_model", token.Alternatives[index].Serialized);
            bottle.Position = new(index * AlternativeBottleOffset, index * (PathIconSize - AlternativeBottleSize));
            if (bottle is TextureRect icon) icon.Material = PathIconOutline;
            choice.AddChild(bottle);
        }
        var either = PathSymbol(UiText.Get("operation.path.or"), AlternativeOrFontSize);
        either.Name = "Rt2AlternativeOr";
        either.VerticalAlignment = VerticalAlignment.Bottom;
        choice.AddChild(either);
        either.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return choice;
    }

    private static Label PathSymbol(string text, int size)
    {
        var label = Text(text, size);
        label.AddThemeFontOverride("font", MainPanelTheme.Medium);
        label.AddThemeColorOverride("font_outline_color", new Color("1a130f"));
        label.AddThemeConstantOverride("outline_size", 4);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    // Plain text retains complete original routes. It never relies on visual
    // overlap to distinguish one optional potion from other required members.
    private static string IconPathsText(ImmutableArray<OperationIconPath> paths) =>
        paths.IsDefaultOrEmpty ? "" : string.Join("\n" + UiText.Get("operation.path.or") + " ", paths.Select(path =>
            string.Join(" → ", path.Segments.Select(segment => segment.Length > 1
                ? "[ " + string.Join(" ", segment.Select(NameOf)) + " ]"
                : string.Join(" ", segment.Select(NameOf))))));
}
