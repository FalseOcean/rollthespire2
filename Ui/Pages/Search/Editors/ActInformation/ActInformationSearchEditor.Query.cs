using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ActInformationSearchEditor
{
    internal static bool CanImportMapConditions(SearchQuery q) =>
        q.StandardMaps is not null && q.StandardMapRoutes is not null &&
        q.StandardMaps.All(condition => condition is not null && condition.IsValid) &&
        q.StandardMaps.Where(condition => condition.RouteObjective).GroupBy(condition => condition.Scope).All(group => group.Count() == 1) &&
        q.StandardMaps.Where(condition => !condition.RouteObjective).GroupBy(condition => (condition.Scope, condition.Metric)).All(group => group.Count() == 1) &&
        q.StandardMapRoutes.All(route => route is not null && route.IsValid && !route.IsEmpty &&
            route.Groups.All(group => group.Nodes.Count > 0)) &&
        q.StandardMapRoutes.GroupBy(route => route.Act).All(group => group.Count() == 1) &&
        !q.StandardMapRoutes.Any(route => q.StandardMaps.Any(condition => condition.RouteObjective && condition.Scope == route.Act));

    internal SearchQuery ExportQuery(SearchQuery q)
    {
        var maps = _draft.Routes.Where(kv => !DrawingActive(_draft, kv.Key)).Select(kv => new StandardMapSearchCondition(kv.Key,
            Enum.Parse<StandardMapMetric>(kv.Value.Metric.ToString()), StandardMapComparison.AtLeast, kv.Value.Value, true))
            .Concat(_draft.Properties.SelectMany(kv => kv.Value.Select(c => new StandardMapSearchCondition(kv.Key,
                Enum.Parse<StandardMapMetric>(c.Metric.ToString()),Enum.Parse<StandardMapComparison>(c.Comparison.ToString()),c.Value)))).ToArray();
        var branches = new List<VariantScopedBossBranch>();
        var bossOnly = new List<ActOrdinalModelKeySetFilter>();
        for (int act = 1; act <= 3; act++)
        {
            var variants = _draft.SelectedVariants[act - 1]; var bosses = _draft.SelectedBosses[act - 1];
            var secondBosses = _draft.SelectedSecondBosses[act - 1];
            bool includesSecond = _draft.IncludesSecondBoss[act - 1];
            if (variants.Count == 0 && bosses.Count == 0 && !includesSecond) continue;
            // Export the player's conjunction, including an empty catalog
            // intersection. Dropping all such branches would erase this Act's
            // requirement. Exact runtime authority owns impossibility proofs.
            var keys = bosses.OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
            var secondKeys = secondBosses.OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
            if (variants.Count == 0)
            {
                // A Boss declaration does not depend on the current catalog's
                // variant inventory. Keep it unscoped until runtime resolves it.
                if (keys.Length > 0) bossOnly.Add(new(act, 1, new(keys, [], [])));
                if (secondKeys.Length > 0) bossOnly.Add(new(act, 2, new(secondKeys, [], [])));
                continue;
            }
            foreach (var variantKey in variants.OrderBy(key => key.Serialized, StringComparer.Ordinal))
            {
                branches.Add(new(act, variantKey, new(keys, [], []), new(secondKeys, [], []), includesSecond));
            }
        }
        var drawing = Enumerable.Range(1, 3).Where(act => DrawingActive(_draft, act) &&
                (_draft.Drawings.GetValueOrDefault(act)?.Count ?? 0) > 0)
            .Select(act => new StandardMapRouteCondition(act, DrawingGroups(_draft.Drawings[act]))).ToArray();
        return q with { VariantBossBranches = branches.ToArray(), StandardMaps = maps, StandardMapRoutes = drawing,
            LegacyWorld = q.LegacyWorld with { BossOrdinalFilters = bossOnly.ToArray() } };
    }

    internal static bool CanImportBossOrdinals(SearchQuery q) =>
        q.LegacyWorld.BossOrdinalFilters.All(filter => filter.Act is >= 1 and <= 3 &&
            (filter.Ordinal == 1 && filter.Keys.Any.Count > 0 || filter.Act == 3 && filter.Ordinal == 2 && filter.Keys.Any.Count > 0) &&
            filter.Keys.All.Count == 0 && filter.Keys.Ban.Count == 0 &&
            filter.Keys.Any.All(key => key.IsValid && key.Category == BaseGameModelKeys.Categories.Encounter) &&
            filter.Keys.Any.Distinct(ModelKeyComparer.Instance).Count() == filter.Keys.Any.Count &&
            !q.VariantBossBranches.Any(branch => branch.Act == filter.Act)) &&
        q.LegacyWorld.BossOrdinalFilters.Select(filter => (filter.Act, filter.Ordinal)).Distinct().Count() == q.LegacyWorld.BossOrdinalFilters.Count;

    internal static bool CanImportVariantBossBranches(SearchQuery q)
    {
        bool Simple(ModelKeySetFilter filter) => filter.All.Count == 0 && filter.Ban.Count == 0 &&
            filter.Any.All(key => key.IsValid && key.Category == BaseGameModelKeys.Categories.Encounter) &&
            filter.Any.Distinct(ModelKeyComparer.Instance).Count() == filter.Any.Count;
        bool Same(ModelKeySetFilter left, ModelKeySetFilter right) =>
            left.Any.Count == right.Any.Count && left.Any.All(right.Any.Contains);
        return q.VariantBossBranches.All(branch => branch.IsValid && Simple(branch.FirstBoss) && Simple(branch.SecondBoss) &&
                (branch.IncludesSecondBoss ? branch.Act == 3 : branch.SecondBoss.IsEmpty)) &&
            q.VariantBossBranches.GroupBy(branch => branch.Act).All(group =>
            {
                var first = group.First();
                // This editor owns one pair of position filters per Act. Do not
                // flatten distinct branch-local pairs into a Cartesian product.
                return group.All(branch => branch.IncludesSecondBoss == first.IncludesSecondBoss &&
                    Same(branch.FirstBoss, first.FirstBoss) && Same(branch.SecondBoss, first.SecondBoss));
            });
    }
    internal void ImportQuery(SearchQuery q, MapEditorStateSnapshot? mapState = null)
    {
        _draft.Routes.Clear(); _draft.Properties.Clear();
        _draft.Drawings.Clear(); _draft.DailyMetrics.Clear(); Array.Clear(_draft.DrawingModes);
        _draft.Scope = mapState?.SelectedScope is >= 0 and <= 3 ? mapState.SelectedScope : 0;
        foreach (var act in mapState?.Acts ?? [])
        {
            if (act is null || act.Act is < 1 or > 3) continue;
            _draft.DrawingModes[act.Act] = act.DrawingMode;
            if (act.Daily is not null && Enum.TryParse(act.Daily.Metric.ToString(), out MapMetricKind metric) && IsDailyMetric(metric))
            {
                _draft.DailyMetrics[act.Act] = metric;
                // A snapshot supplies only the inactive daily draft.
                if (act.DrawingMode && act.Daily.Minimum is >= 0)
                    _draft.Routes[act.Act] = new(metric, act.Daily.Minimum.Value);
            }
            if (ValidDrawingSnapshot(act.Nodes) && !act.DrawingMode)
                _draft.Drawings[act.Act] = act.Nodes.ToList();
        }
        foreach (var c in q.StandardMaps)
        {
            var metric=Enum.Parse<MapMetricKind>(c.Metric.ToString());
            if(c.RouteObjective)
            {
                _draft.Routes[c.Scope]=new(metric,c.Value);
                _draft.DailyMetrics[c.Scope] = metric;
                if (c.Scope > 0) _draft.DrawingModes[c.Scope] = false;
                // A stale mode flag must not erase the inactive drawing when q selects daily.
                var saved = mapState?.Acts?.FirstOrDefault(act => act?.Act == c.Scope);
                if (saved is not null && ValidDrawingSnapshot(saved.Nodes))
                    _draft.Drawings[c.Scope] = saved.Nodes.ToList();
            }
            else { if(!_draft.Properties.TryGetValue(c.Scope,out var rows)) _draft.Properties[c.Scope]=rows=[];
                rows.Add(new(metric,Enum.Parse<MapMetricComparison>(c.Comparison.ToString()),c.Value)); }
        }
        foreach (var route in q.StandardMapRoutes)
        {
            if (route.Act is < 1 or > 3) continue;
            _draft.DrawingModes[route.Act] = true;
            var saved = mapState?.Acts?.FirstOrDefault(act => act?.Act == route.Act);
            if (saved is not null && ValidDrawingSnapshot(saved.Nodes) && SameDrawing(route.Groups, DrawingGroups(saved.Nodes)))
                _draft.Drawings[route.Act] = saved.Nodes.ToList();
            else
            {
                long serial = 0, groupId = 0;
                var nodes = new List<MapDrawingNodeSnapshot>();
                foreach (var group in route.Groups)
                {
                    long id = group.Nodes.Count > 1 ? ++groupId : 0;
                    foreach (var room in group.Nodes) { serial++; nodes.Add(new(serial, room, id, serial)); }
                }
                _draft.Drawings[route.Act] = nodes;
            }
            // Even when a stale snapshot selects daily, retain its daily draft as inactive.
            if (saved?.Daily is not null && saved.Daily.Minimum is >= 0 &&
                Enum.TryParse(saved.Daily.Metric.ToString(), out MapMetricKind metric) && IsDailyMetric(metric))
                _draft.Routes[route.Act] = new(metric, saved.Daily.Minimum.Value);
        }
        foreach (var set in _draft.SelectedVariants.Concat(_draft.SelectedBosses).Concat(_draft.SelectedSecondBosses)) set.Clear();
        Array.Clear(_draft.IncludesSecondBoss);
        foreach (var b in q.VariantBossBranches)
        {
            _draft.SelectedVariants[b.Act - 1].Add(b.VariantKey);
            _draft.SelectedBosses[b.Act - 1].UnionWith(b.FirstBoss.Any);
            _draft.SelectedSecondBosses[b.Act - 1].UnionWith(b.SecondBoss.Any);
            _draft.IncludesSecondBoss[b.Act - 1] |= b.IncludesSecondBoss;
        }
        foreach (var boss in q.LegacyWorld.BossOrdinalFilters)
        {
            if (boss.Ordinal == 1) _draft.SelectedBosses[boss.Act - 1].UnionWith(boss.Keys.Any);
            else
            {
                _draft.SelectedSecondBosses[boss.Act - 1].UnionWith(boss.Keys.Any);
                _draft.IncludesSecondBoss[boss.Act - 1] = true;
            }
        }
        _draft.Context = "";
    }

    internal MapEditorStateSnapshot ExportMapState() => new(_draft.Scope, Enumerable.Range(1, 3).Select(act =>
    {
        var daily = _draft.Routes.GetValueOrDefault(act);
        var metric = daily?.Metric ?? _draft.DailyMetrics.GetValueOrDefault(act, MapMetricKind.ReachableMaxElite);
        return new MapActEditorStateSnapshot(act, _draft.DrawingModes[act],
            new(Enum.Parse<StandardMapMetric>(metric.ToString()), daily?.Value), DrawingForScope(act).ToArray());
    }).ToArray());

    private static bool IsDailyMetric(MapMetricKind metric) => metric is MapMetricKind.ReachableMaxElite or
        MapMetricKind.ReachableMaxRest or MapMetricKind.ReachableMaxUnknown;

    private static bool ValidDrawingSnapshot(IReadOnlyList<MapDrawingNodeSnapshot>? nodes)
    {
        if (nodes is null || nodes.Any(node => node is null || node.Id <= 0 || node.AddedOrder <= 0 || node.GroupId < 0 || !Enum.IsDefined(node.Room)) ||
            nodes.Select(node => node.Id).Distinct().Count() != nodes.Count ||
            nodes.Select(node => node.AddedOrder).Distinct().Count() != nodes.Count) return false;
        foreach (var group in nodes.Select((node, index) => (node, index)).Where(item => item.node.GroupId != 0).GroupBy(item => item.node.GroupId))
            if (group.Count() < 2 || group.Max(item => item.index) - group.Min(item => item.index) + 1 != group.Count()) return false;
        return true;
    }

    private static IReadOnlyList<StandardMapRouteGroup> DrawingGroups(IReadOnlyList<MapDrawingNodeSnapshot> nodes)
    {
        var groups = new List<StandardMapRouteGroup>();
        for (int index = 0; index < nodes.Count;)
        {
            int count = 1;
            if (nodes[index].GroupId != 0)
                while (index + count < nodes.Count && nodes[index + count].GroupId == nodes[index].GroupId) count++;
            groups.Add(new(nodes.Skip(index).Take(count).Select(node => node.Room).ToArray()));
            index += count;
        }
        return groups;
    }

    private static bool SameDrawing(IReadOnlyList<StandardMapRouteGroup> left, IReadOnlyList<StandardMapRouteGroup> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.Nodes.Order().SequenceEqual(pair.Second.Nodes.Order()));
}
