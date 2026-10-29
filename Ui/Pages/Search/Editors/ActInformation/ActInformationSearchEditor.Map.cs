using Godot;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ActInformationSearchEditor
{
    private readonly Dictionary<int, Label> _mapScopeBadges = [];

    private static bool DrawingActive(SeatDraft draft, int scope) => scope is >= 1 and <= 3 && draft.DrawingModes[scope];
    private static int ScopeConditionCount(SeatDraft draft, int scope) =>
        (DrawingActive(draft, scope) ? (draft.Drawings.GetValueOrDefault(scope)?.Count > 0 ? 1 : 0)
            : draft.Routes.ContainsKey(scope) ? 1 : 0) + (draft.Properties.GetValueOrDefault(scope)?.Count ?? 0);

    private void RefreshMapCounts()
    {
        foreach ((int scope, Label label) in _mapScopeBadges)
        {
            if (!GodotObject.IsInstanceValid(label)) continue;
            int count = ScopeConditionCount(_draft, scope);
            label.Text = count == 0 ? _text.Get("query.map.scope.empty") : _text.Format("query.map.scope.count", count);
        }
    }

    private float RenderMap(Control content, float width, float top)
    {
        _mapScopeBadges.Clear();
        var lossy = new CheckButton { Name = "MapLossyEnabled", Text = _text.Get("query.map.lossy.enable"),
            Position = new(0, top), Size = new(width, 36), ButtonPressed = _persistence.Preferences.MapLossyEnabled };
        lossy.AddThemeFontSizeOverride("font_size", 17);
        lossy.Toggled += enabled => _persistence.SetMapLossyEnabled(enabled);
        content.AddChild(lossy);
        float lossyHint = MapHint(content, _text.Get("query.map.lossy.hint"), 0, top + 38, width);
        top += 38 + lossyHint + 12;
        Text(content, _text.Get("query.map.scope.current_label"), 0, top, width, 14, true);
        string[] scopes = ["query.map.scope.total", "query.map.scope.act1", "query.map.scope.act2", "query.map.scope.act3"];
        float scopeWidth = (width - 24) / scopes.Length;
        for (int index = 0; index < scopes.Length; index++)
        {
            int captured = index;
            Button scope = ModeButton(content, string.Empty, index * (scopeWidth + 8), top + 28, scopeWidth,
                () => SetMapScope(captured), _draft.Scope == index, 66);
            scope.Name = $"MapScope{index}";
            Label name = Text(scope, _text.Get(scopes[index]), 8, 7, scopeWidth - 16, width < 520 ? 15 : 18);
            name.MouseFilter = MouseFilterEnum.Ignore;
            Label summary = Text(scope, string.Empty, 8, 35, scopeWidth - 16, 13, true);
            summary.Name = $"MapScopeCount{index}";
            summary.MouseFilter = MouseFilterEnum.Ignore;
            _mapScopeBadges[index] = summary;
        }
        RefreshMapCounts();
        float y = DrawingActive(_draft, _draft.Scope)
            ? RenderDrawing(content, width, top + 114) : RenderDailyRoute(content, width, top + 114);
        if (_draft.Scope == 0 && (_draft.Properties.GetValueOrDefault(0)?.Count ?? 0) == 0) return y + 12;
        AddLine(content, 0, y + 4, width);
        return RenderMapProperties(content, width, y + 22);
    }

    private void SetMapScope(int scope)
    {
        if (_mapScroll is not null && GodotObject.IsInstanceValid(_mapScroll)) _mapScroll.ScrollVertical = 0;
        _draft.Scope = scope;
        LoadRouteEditor();
        Render();
    }

    private float RenderDailyRoute(Control content, float width, float top)
    {
        int scope = _draft.Scope;
        Text(content, _text.Get("query.map.route"), 0, top, Math.Max(100, width - 200), 18);
        if (scope > 0)
        {
            Button mode = Button(content, _text.Get("query.map.mode.drawing"), width - 190, top - 4, 190,
                () => { _draft.DrawingModes[scope] = true; Render(); }, false, 36);
            mode.Name = "MapDrawingMode";
            mode.AddThemeFontSizeOverride("font_size", 15);
        }
        float hintHeight = MapHint(content, _text.Get(scope == 0 ? "query.map.route.hint.total" : "query.map.route.hint.act"),
            0, top + 38, width);
        float fieldsTop = top + 38 + hintHeight + 12;
        RouteCondition? active = _draft.Routes.GetValueOrDefault(scope);
        MapMetricKind[] metrics = RouteMetrics(scope);
        if (metrics.Length == 0) return top + 82;
        MapMetricKind selectedMetric = active?.Metric ?? _draft.DailyMetrics.GetValueOrDefault(scope, MapMetricKind.ReachableMaxElite);
        if (!metrics.Contains(selectedMetric)) selectedMetric = metrics[0];
        _draft.DailyMetrics[scope] = selectedMetric;
        OptionButton node = Options(content, metrics.Select(metric => NodeName(NodeKind(metric))).ToArray(),
            Array.IndexOf(metrics, selectedMetric), 0, fieldsTop + 28, 1, index =>
            {
                _draft.DailyMetrics[scope] = metrics[index];
                if (active is not null) _draft.Routes[scope] = new(metrics[index], active.Value);
                Render();
            });
        node.Name = "MapRouteNode";
        MapQuantityInput number = Number(content, "MapRouteTargetValue", active?.Value ?? SuggestedValue(scope, selectedMetric),
            0, fieldsTop + 28, 1, active is not null,
            value =>
            {
                if (!_draft.Routes.ContainsKey(scope)) return;
                _draft.Routes[scope] = new(_draft.DailyMetrics[scope], value);
                RefreshMapCounts();
            });
        string[] choices = [_text.Get("query.map.option.any"), _text.Get("query.map.route.at_least")];
        OptionButton target = Options(content, choices, active is null ? 0 : 1, 0, fieldsTop + 28, 1, index =>
            {
                if (index == 0) _draft.Routes.Remove(scope);
                else _draft.Routes[scope] = new(_draft.DailyMetrics[scope], (int)number.Value);
                Render();
            });
        target.Name = "MapRouteTarget";
        float compareWidth = MinimumOptionWidth(target, choices);
        float numberWidth = number.GetCombinedMinimumSize().X;
        float nodeMinimum = Math.Max(120, MinimumOptionWidth(node, metrics.Select(metric => NodeName(NodeKind(metric))).ToArray()));
        bool stacked = width < nodeMinimum + compareWidth + numberWidth + 18;
        float nodeWidth = stacked ? Math.Min(width, Math.Max(160, nodeMinimum)) : Math.Max(160, nodeMinimum);
        if (!stacked) nodeWidth = Math.Min(nodeWidth, width - compareWidth - numberWidth - 18);
        float valueLeft = stacked ? 0 : nodeWidth + 12;
        float controlsTop = fieldsTop + (stacked ? 108 : 28);
        node.Size = new(nodeWidth, 38);
        target.Position = new(valueLeft, controlsTop); target.Size = new(compareWidth, 38);
        number.Position = new(valueLeft + compareWidth + 6, controlsTop);
        number.Size = new(numberWidth, 38);
        Text(content, _text.Get("query.map.route.node_type"), 0, fieldsTop, nodeWidth, 14, true);
        Text(content, _text.Get("query.map.route.minimum_target"), valueLeft, controlsTop - 28, compareWidth + 6 + numberWidth, 14, true);
        return controlsTop + 52;
    }

    private float RenderDrawing(Control content, float width, float top)
    {
        int scope = _draft.Scope;
        Text(content, _text.Get("query.map.drawing.title"), 0, top, width - 200, 18);
        Button daily = Button(content, _text.Get("query.map.mode.daily"), width - 190, top - 4, 190,
            () => { _draft.DrawingModes[scope] = false; Render(); }, false, 36);
        daily.Name = "MapDailyMode";
        daily.AddThemeFontSizeOverride("font_size", 15);
        float hintHeight = MapHint(content, _text.Get("query.map.drawing.hint"), 0, top + 40, width);
        float clearTop = top + 40 + hintHeight + 8;
        List<MapDrawingNodeSnapshot> nodes = DrawingForScope(scope);
        Button clear = Button(content, _text.Get("query.map.drawing.clear"), width - 90, clearTop, 90,
            () => { nodes.Clear(); Render(); }, false, 28);
        clear.Name = "MapDrawingClear";
        clear.Disabled = nodes.Count == 0;
        var drawing = new MapRouteDrawingControl();
        drawing.Name = "MapRouteDrawing";
        drawing.Position = new(0, clearTop + 34);
        drawing.Configure(width, nodes, _p, _text, () => Render());
        content.AddChild(drawing);
        float noteY = drawing.Position.Y + drawing.ContentHeight + 8;
        float noteHeight = MapHint(content, _text.Get("query.map.drawing.fixed_note"), 0, noteY, width);
        if (drawing.MissingArtRooms.Count > 0)
            noteHeight += MapHint(content, _text.Format("query.map.drawing.art_unavailable", string.Join(", ", drawing.MissingArtRooms)),
                0, noteY + noteHeight + 6, width) + 6;
        return noteY + noteHeight + 8;
    }

    private float RenderMapProperties(Control content, float width, float top)
    {
        Text(content, _text.Get("query.map.property.filter"), 0, top, width, 18);
        float hintHeight = MapHint(content, _text.Get("query.map.property.hint"), 0, top + 32, width);
        float y = top + 32 + hintHeight + 12;
        int scope = _draft.Scope;
        if (scope == 0)
        {
            // Retain imported declarations even when this scope has no new-property editor.
            foreach (PropertyCondition condition in PropertiesForScope(scope).ToArray())
            {
                PropertyRow(content, condition.Metric, 0, y, width, condition);
                y += 48;
            }
            return y;
        }
        MapMetricKind[][] columns =
        [
            [MapMetricKind.GuaranteedElite, MapMetricKind.GuaranteedRest, MapMetricKind.GuaranteedUnknown, MapMetricKind.GuaranteedMonster],
            [MapMetricKind.ReachableMaxElite, MapMetricKind.ReachableMaxRest, MapMetricKind.ReachableMaxUnknown, MapMetricKind.ReachableMaxMonster]
        ];
        // Native minimums include the step buttons and popup arrow, in addition to text.
        // Choose columns from readable content widths rather than shrinking controls into a fixed grid.
        bool narrow = width < MinimumPropertyColumnWidth(content) * 2 + 20;
        float columnWidth = narrow ? width : (width - 20) / 2;
        for (int column = 0; column < columns.Length; column++)
        {
            float x = narrow ? 0 : column * (columnWidth + 20);
            float columnTop = y + (narrow ? column * 236 : 0);
            Text(content, _text.Get(column == 0 ? "query.map.property.family.guaranteed" : "query.map.property.family.maximum"),
                x, columnTop, columnWidth, 14, true);
            for (int row = 0; row < columns[column].Length; row++)
                PropertyRow(content, columns[column][row], x, columnTop + 32 + row * 48, columnWidth);
        }
        y += narrow ? 472 : 236;
        AddLine(content, 0, y, width);
        PropertyRow(content, MapMetricKind.ForcedMonsterPrefix, 0, y + 12, width);
        return y + 64;
    }

    private void PropertyRow(Control content, MapMetricKind metric, float x, float y, float width,
        PropertyCondition? retained = null)
    {
        int scope = _draft.Scope;
        PropertyCondition? active = retained ?? PropertiesForScope(scope).FirstOrDefault(condition => condition.Metric == metric);
        if (active is not null)
        {
            var panel = new Panel { Position = new(x - 3, y - 2), Size = new(width + 6, 44), MouseFilter = MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface, _p.Selected, 1));
            content.AddChild(panel);
        }
        MapQuantityInput number = Number(content, $"MapPropertyNumber{metric}", active?.Value ?? SuggestedValue(scope, metric),
            x, y, 1, active is not null, value =>
            {
                List<PropertyCondition> conditions = PropertiesForScope(scope);
                int index = retained is null ? conditions.FindIndex(condition => condition.Metric == metric) : conditions.IndexOf(retained);
                if (index < 0) return;
                conditions[index] = conditions[index] with { Value = value };
                RefreshMapCounts();
            });
        string[] choices = [_text.Get("query.map.option.any"), _text.Get("query.map.route.at_least"), _text.Get("query.map.route.at_most")];
        OptionButton compare = Options(content, choices, active is null ? 0 : active.Comparison == MapMetricComparison.AtLeast ? 1 : 2,
            x, y, 1, index =>
            {
                List<PropertyCondition> conditions = PropertiesForScope(scope);
                int old = retained is null ? conditions.FindIndex(condition => condition.Metric == metric) : conditions.IndexOf(retained);
                if (old >= 0) conditions.RemoveAt(old);
                if (index != 0) conditions.Add(new(metric, index == 1 ? MapMetricComparison.AtLeast : MapMetricComparison.AtMost, (int)number.Value));
                Render();
            });
        compare.Name = $"MapPropertyComparison{metric}";
        float valueWidth = number.GetCombinedMinimumSize().X;
        float compareWidth = MinimumOptionWidth(compare, choices);
        float compareLeft = x + width - valueWidth - compareWidth - 6;
        compare.Position = new(compareLeft, y); compare.Size = new(compareWidth, 38);
        number.Position = new(x + width - valueWidth, y); number.Size = new(valueWidth, 38);
        string label = metric == MapMetricKind.ForcedMonsterPrefix ? PropertyLabel(metric) : NodeName(NodeKind(metric));
        Text(content, label, x + 4, y + 5, compareLeft - x - 10, 15);
    }

    private MapQuantityInput Number(Control content, string name, int value, float x, float y, float width, bool enabled, Action<int> change)
    {
        var number = new MapQuantityInput(_p, value, enabled) { Name = name, Position = new(x, y) };
        number.ValueChanged += change;
        content.AddChild(number);
        number.Size = new(Math.Max(width, number.GetCombinedMinimumSize().X), 38);
        return number;
    }
    private static float MinimumOptionWidth(OptionButton option, IEnumerable<string> choices)
    {
        Font font = option.GetThemeFont("font");
        float textWidth = choices.Select(value => font.GetStringSize(value, HorizontalAlignment.Left, -1, 15).X).DefaultIfEmpty(0).Max();
        float decoration = option.GetThemeStylebox("normal").GetMinimumSize().X + option.GetThemeIcon("arrow").GetWidth() +
            option.GetThemeConstant("arrow_margin") * 2 + 8;
        return Math.Max(option.GetCombinedMinimumSize().X, Mathf.Ceil(textWidth + decoration));
    }

    private float MinimumPropertyColumnWidth(Control content)
    {
        Font font = content.GetThemeDefaultFont();
        float label = new[] { 0, 1, 2, 4 }.Select(kind => font.GetStringSize(NodeName(kind), HorizontalAlignment.Left, -1, 15).X).Max();
        float compare = new[] { "query.map.option.any", "query.map.route.at_least", "query.map.route.at_most" }
            .Select(key => font.GetStringSize(_text.Get(key), HorizontalAlignment.Left, -1, 15).X).Max() + 64;
        float number = font.GetStringSize(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HorizontalAlignment.Left, -1, 15).X + 76;
        return Mathf.Ceil(label + compare + number + 26);
    }

    private int SuggestedValue(int scope, MapMetricKind metric) => MapMetricCapabilityCatalog.Find(_draft.Players,
        _draft.Ascension, scope, metric)?.Options.FirstOrDefault(option => !option.IsAny && option.Comparison == MapMetricComparison.AtLeast)?.Value ?? 1;

    private float MapHint(Control content, string value, float x, float y, float width)
    {
        Label hint = Text(content, value, x, y, width, 14, true);
        hint.ClipText = false;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.MaxLinesVisible = -1;
        float height = Mathf.Ceil(hint.GetThemeFont("font").GetMultilineStringSize(value,
            HorizontalAlignment.Left, width, 14).Y) + 8;
        hint.Size = new(width, height);
        return height;
    }

    private List<MapDrawingNodeSnapshot> DrawingForScope(int scope)
    {
        if (!_draft.Drawings.TryGetValue(scope, out var nodes)) _draft.Drawings[scope] = nodes = [];
        return nodes;
    }

    private void RevalidateMapCapabilities() => LoadRouteEditor();
    private void LoadRouteEditor()
    {
        if (_draft.Routes.TryGetValue(_draft.Scope, out RouteCondition? route))
            _draft.DailyMetrics[_draft.Scope] = route.Metric;
    }

    private MapMetricKind[] RouteMetrics(int scope) => new[] { MapMetricKind.ReachableMaxElite,
        MapMetricKind.ReachableMaxRest, MapMetricKind.ReachableMaxUnknown }.Where(metric =>
        MapMetricCapabilityCatalog.Find(_draft.Players, _draft.Ascension, scope, metric) is not null).ToArray();

    private string PropertyLabel(MapMetricKind metric) => _text.Get(metric switch
    {
        MapMetricKind.GuaranteedMonster => "query.map.property.monster",
        MapMetricKind.GuaranteedElite => "query.map.property.elite",
        MapMetricKind.GuaranteedRest => "query.map.property.rest",
        MapMetricKind.GuaranteedUnknown => "query.map.property.unknown",
        MapMetricKind.ForcedMonsterPrefix => "query.map.property.prefix",
        MapMetricKind.ReachableMaxMonster => "query.map.property.max_monster",
        MapMetricKind.ReachableMaxElite => "query.map.property.max_elite",
        MapMetricKind.ReachableMaxRest => "query.map.property.max_rest",
        _ => "query.map.property.max_unknown"
    });

    private static int NodeKind(MapMetricKind metric) => metric switch
    {
        MapMetricKind.GuaranteedMonster or MapMetricKind.ReachableMaxMonster => 0,
        MapMetricKind.GuaranteedElite or MapMetricKind.ReachableMaxElite => 1,
        MapMetricKind.GuaranteedRest or MapMetricKind.ReachableMaxRest => 2,
        _ => 4
    };

    private string NodeName(int kind) => _text.Get(kind switch
    {
        0 => "query.map.node.monster", 1 => "query.map.node.elite", 2 => "query.map.node.rest",
        3 => "query.map.node.shop", _ => "query.map.node.unknown"
    });

    private List<PropertyCondition> PropertiesForScope(int scope)
    {
        if (!_draft.Properties.TryGetValue(scope, out var rows)) _draft.Properties[scope] = rows = [];
        return rows;
    }
}
