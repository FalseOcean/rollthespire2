using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ActInformationSearchEditor
{
    internal SearchQuery ExportQuery(SearchQuery q)
    {
        var maps = _draft.Routes.Select(kv => new StandardMapSearchCondition(kv.Key,
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
        return q with { VariantBossBranches = branches.ToArray(), StandardMaps = maps,
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
    internal void ImportQuery(SearchQuery q)
    {
        _draft.Routes.Clear(); _draft.Properties.Clear();
        foreach (var c in q.StandardMaps)
        {
            var metric=Enum.Parse<MapMetricKind>(c.Metric.ToString());
            if(c.RouteObjective) _draft.Routes[c.Scope]=new(metric,c.Value);
            else { if(!_draft.Properties.TryGetValue(c.Scope,out var rows)) _draft.Properties[c.Scope]=rows=[];
                rows.Add(new(metric,Enum.Parse<MapMetricComparison>(c.Comparison.ToString()),c.Value)); }
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
}
