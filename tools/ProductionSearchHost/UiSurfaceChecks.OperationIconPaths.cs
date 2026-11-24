using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

internal static partial class UiSurfaceChecks
{
    private static void RecordRerollNativeLayout(NeowOperationOverlay view, OperationPreview preview, string output, string name)
    {
        var tiles = Descendants(view).OfType<NCard>().Select(card => card.GetParent<Control>()).ToArray();
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, name + "-" + (int)view.GetViewportRect().Size.X + "-layout.json"),
            System.Text.Json.JsonSerializer.Serialize(new { view.CardDisplayScale,
                ordinals = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).Select(group => group.SequenceOrdinal),
                strips = Descendants(view).Count(node => node.Name == "Rt2IconPaths"),
                nativeCardRects = tiles.Select(tile => tile.GetGlobalRect().ToString()),
                visibleRegions = view.VisibleRegions.Select(region => region.ToString()) }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    // Independent expression oracle: alternatives select one route; arrows
    // concatenate steps; brackets enumerate every order of all enclosed icons.
    private static IEnumerable<string[]> ExpandOperationIconPath(OperationIconPath path)
    {
        IEnumerable<string[]> rows = [Array.Empty<string>()];
        foreach (var segment in path.Segments)
            rows = rows.SelectMany(prefix => CombatRerollPermutations(segment.Select(key => key.Serialized).ToArray())
                .Select(order => prefix.Concat(order).ToArray())).ToArray();
        return rows;
    }

    private static void CheckOperationIconPaths(NeowOperationOverlay view, OperationPreview preview, Action<bool, string> check)
    {
        var sources = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups.Select(group => (section, group))).ToArray();
        var groups = sources.Select(source => source.group).ToArray();
        var stacks = Descendants(view).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
        check(stacks.Length == groups.Length, "path renderer retains each result card group");
        for (int index = 0; index < groups.Length; index++)
        {
            var strips = Descendants(stacks[index]).OfType<Control>().Where(node => node.Name == "Rt2IconPaths").ToArray();
            var source = sources[index];
            bool reroll = source.section.Category == OperationPredictionCategory.RewardReroll ||
                source.section.Category == null && preview.OptionKey == "REROLL";
            bool distinctAtOrdinal = groups[index].SequenceOrdinal.HasValue && source.section.CardGroups
                .Where(other => other.SequenceOrdinal == groups[index].SequenceOrdinal && !other.Cards.IsEmpty)
                .Select(other => CombatRerollCards(other.Cards)).Distinct().Count() > 1;
            bool show = !groups[index].IconPaths.IsEmpty && (!reroll || distinctAtOrdinal);
            if (reroll && groups[index].Label?.StartsWith("RewardRerollStep:", StringComparison.Ordinal) == true)
                check(groups[index].SequenceOrdinal == int.Parse(groups[index].Label!["RewardRerollStep:".Length..]),
                    "the UI projection preserves the actual ordinal separately from its display label");
            check(strips.Length == (show ? 1 : 0), "reroll paths appear only for distinct complete ordered results at the same ordinal; potion route facts keep their own semantics");
            if (strips.Length == 0) continue;
            var strip = strips[0];
            var tokens = ReadOperationIconTokens(strip).ToArray();
            var expected = groups[index].IconPaths.SelectMany(ExpandOperationIconPath).Select(path => string.Join(">", path)).ToHashSet();
            var rendered = ParseOperationIconExpression(tokens, check).Select(path => string.Join(">", path)).ToHashSet();
            check(rendered.SetEquals(expected), "visible or, arrows and brackets express exactly every legal route to this card result;tokens=" + string.Join(" ", tokens));
            check(Descendants(strip).OfType<Control>().All(node => view.GetViewportRect().Grow(.2f).Encloses(node.GetGlobalRect())),
                "all route icons and operators remain inside the native viewport");
            var icons = Descendants(strip).OfType<Control>().Where(node => node.HasMeta("rt2_model")).ToArray();
            check(icons.All(node => node.CustomMinimumSize.IsEqualApprox(node.Name.ToString().StartsWith("Rt2AlternativeBottle_", StringComparison.Ordinal) ? new(36, 36) : new(40, 40))) &&
                icons.OfType<TextureRect>().All(icon => icon.Material is ShaderMaterial { Shader: not null }),
                "potion and relic routes share larger original artwork with a silhouette outline shader");
            foreach (var alternative in Descendants(strip).OfType<Control>().Where(node => node.HasMeta("rt2_alternatives")))
            {
                var keys = alternative.GetMeta("rt2_alternatives").AsStringArray();
                var sprites = Descendants(alternative).OfType<TextureRect>().Where(node => node.HasMeta("rt2_model")).ToArray();
                bool realSprites = sprites.Length == 0;
                if (sprites.Length == 2)
                    realSprites = sprites.Select(sprite => sprite.GetMeta("rt2_model").AsString()).SequenceEqual(keys) &&
                        sprites.All(sprite => sprite.Texture != null && sprite.Texture.ResourcePath == MegaCrit.Sts2.Core.Models.ModelDb
                            .GetById<MegaCrit.Sts2.Core.Models.PotionModel>(new("POTION", sprite.GetMeta("rt2_model").AsString().Split(':')[1])).ImagePath) &&
                        sprites[0].Position.X < sprites[1].Position.X && sprites[0].Position.Y < sprites[1].Position.Y &&
                        sprites[0].GetRect().Intersects(sprites[1].GetRect());
                bool choiceText = Descendants(alternative).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                    (label.Text is "或" or "or" || label.Name == "Rt2AlternativeText"));
                check(keys.Length == 2 && keys.Distinct().Count() == 2 && realSprites && choiceText,
                    "alternative bottle pair shows both real native sprites with an explicit choice marker and preserves both identities");
                if (sprites.Length == 2)
                {
                    var box = alternative.GetGlobalRect();
                    var panel = alternative.GetParent();
                    while (panel is not PanelContainer && panel != strip) panel = panel.GetParent();
                    check(alternative.Size.IsEqualApprox(new(60, 40)) && !alternative.ClipContents &&
                        sprites[0].Size.IsEqualApprox(new(36, 36)) && sprites[1].Size.IsEqualApprox(new(36, 36)) &&
                        sprites[0].Position.IsEqualApprox(Vector2.Zero) && sprites[1].Position.IsEqualApprox(new(24, 4)) &&
                        sprites.All(sprite => box.Grow(.1f).Encloses(sprite.GetGlobalRect())) && panel is PanelContainer rowPanel &&
                        sprites.All(sprite => rowPanel.GetGlobalRect().Grow(.1f).Encloses(sprite.GetGlobalRect())),
                        "both actual bottle rectangles remain visible inside the 60x40 pair and its night-blue row without clipping");
                    var overlap = sprites[0].GetGlobalRect().Intersection(sprites[1].GetGlobalRect());
                    check(overlap.Size.X > 0 && overlap.Size.Y > 0 && overlap.Size.X * overlap.Size.Y < sprites[0].Size.X * sprites[0].Size.Y * .75f &&
                        Descendants(alternative).OfType<Label>().All(label => box.Grow(.1f).Encloses(label.GetGlobalRect())) &&
                        Descendants(strip).OfType<Label>().Where(label => label.Text == "→").All(arrow => !arrow.GetGlobalRect().Intersects(box)),
                        "overlapping bottles each stay visible and their choice marker cannot cover a neighbouring sequence arrow;box=" + box +
                        ";overlap=" + overlap + ";labels=" + string.Join(";", Descendants(alternative).OfType<Label>().Select(label => label.Text + "=" + label.GetGlobalRect())) +
                        ";arrows=" + string.Join(";", Descendants(strip).OfType<Label>().Where(label => label.Text == "→").Select(label => label.GetGlobalRect())));
                }
            }
            var panels = strip.GetChildren().OfType<PanelContainer>().ToArray();
            var lines = Descendants(strip).OfType<HBoxContainer>().Where(node => node.HasMeta("rt2_continuation")).ToArray();
            check(panels.Length == lines.Length && panels.Length > 0 && panels.All(panel =>
                panel.GetThemeStylebox("panel") is StyleBoxFlat style && style.BgColor.A is > 0 and < 1 &&
                style.BorderWidthLeft == 0 && style.BorderWidthRight == 0 && style.BorderWidthTop == 0 && style.BorderWidthBottom == 0 &&
                panel.GetChildCount() == 1 && panel.GetChild(0) is HBoxContainer line &&
                panel.Size.X - line.Size.X is > 0 and <= 16 && panel.Size.Y - line.Size.Y is > 0 and <= 8),
                "each complete path line has one narrow translucent night-blue backing without a thick border or per-icon boxes");
            foreach (var line in lines)
            {
                var symbols = line.GetChildren().OfType<Label>().Select(label => label.Text).ToArray();
                check(symbols.Count(symbol => symbol == "[") == symbols.Count(symbol => symbol == "]"),
                    "path line wrapping never splits an exchangeable bracket");
            }
        }
        CheckOperationCardRows(view, check);
    }

    private static IEnumerable<string> ReadOperationIconTokens(Node owner)
    {
        foreach (var child in owner.GetChildren())
        {
            if (child.HasMeta("rt2_alternatives"))
            { yield return "CHOICE{" + string.Join('|', child.GetMeta("rt2_alternatives").AsStringArray()) + "}"; continue; }
            if (child.HasMeta("rt2_model")) { yield return child.GetMeta("rt2_model").AsString(); continue; }
            if (child is Label label && label.Text is "→" or "[" or "]" or "(" or ")" or "或" or "or")
            { yield return child.HasMeta("rt2_route_alternative") && child.GetMeta("rt2_route_alternative").AsBool() ? "OR_ROUTE" : label.Text; continue; }
            foreach (var token in ReadOperationIconTokens(child)) yield return token;
        }
    }

    private static IEnumerable<string[]> ParseOperationIconExpression(string[] tokens, Action<bool, string> check)
    {
        static bool Or(string token) => token is "或" or "or" or "OR_ROUTE";
        List<string[]> Evaluate(int start, int end, bool grouped)
        {
            if (start == end) return [Array.Empty<string>()];
            var arrows = new List<int>(); var ors = new List<int>(); var routeOrs = new List<int>(); int level = 0;
            for (int i = start; i < end; i++)
            {
                if (tokens[i] is "(" or "[") level++;
                if (tokens[i] is ")" or "]") level--;
                if (level == 0 && tokens[i] == "→") arrows.Add(i);
                if (level == 0 && Or(tokens[i])) ors.Add(i);
                if (level == 0 && tokens[i] == "OR_ROUTE") routeOrs.Add(i);
                check(level >= 0, "route expression brackets remain balanced");
            }
            check(level == 0, "route expression closes every grouping symbol");
            // One-step alternatives bind as one step: A or B → target.
            // Parenthesized multi-step alternatives split as complete routes.
            var split = routeOrs.Count > 0 ? routeOrs : grouped && ors.Count > 0 ? ors : arrows.Count > 0 ? arrows : ors;
            if (split.Count > 0)
            {
                bool alternatives = Or(tokens[split[0]]);
                var parts = new List<List<string[]>>(); int cursor = start;
                foreach (int separator in split.Append(end)) { parts.Add(Evaluate(cursor, separator, false)); cursor = separator + 1; }
                if (alternatives) return parts.SelectMany(part => part).ToList();
                IEnumerable<string[]> result = [Array.Empty<string>()];
                foreach (var part in parts) result = result.SelectMany(prefix => part.Select(suffix => prefix.Concat(suffix).ToArray())).ToArray();
                return result.ToList();
            }
            if (tokens[start] == "(" && tokens[end - 1] == ")") return Evaluate(start + 1, end - 1, true);
            if (tokens[start] == "[" && tokens[end - 1] == "]")
            {
                var enclosed = tokens.Skip(start + 1).Take(end - start - 2).ToArray();
                check(enclosed.All(token => token.Contains(':')), "exchangeable bracket contains required icons, without alternative or sequence operators");
                IEnumerable<string[]> choices = [Array.Empty<string>()];
                foreach (var atom in enclosed)
                    choices = choices.SelectMany(prefix => Atom(atom).Select(choice => prefix.Append(choice).ToArray())).ToArray();
                return choices.SelectMany(CombatRerollPermutations).ToList();
            }
            check(end - start == 1 && tokens[start].Contains(':'), "route expression atom is one actual icon: " + string.Join(" ", tokens.Skip(start).Take(end - start)));
            return Atom(tokens[start]).Select(key => new[] { key }).ToList();
        }
        static string[] Atom(string token) => token.StartsWith("CHOICE{", StringComparison.Ordinal) ? token[7..^1].Split('|') : [token];
        return Evaluate(0, tokens.Length, false);
    }

    private static void CheckOperationCardRows(NeowOperationOverlay view, Action<bool, string> check)
    {
        var stacks = Descendants(view).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
        foreach (var row in stacks.GroupBy(stack => Math.Round(stack.GlobalPosition.Y, 1)))
        {
            var tops = row.Where(stack => Descendants(stack).OfType<NCard>().Any()).Select(stack => Descendants(stack).OfType<NCard>()
                .Min(card => card.GetParent<Control>().GlobalPosition.Y)).ToArray();
            check(tops.Length == 0 || tops.Max() - tops.Min() < .2f, "all native card faces in a result row share their top baseline despite unequal headings");
        }
    }
}
