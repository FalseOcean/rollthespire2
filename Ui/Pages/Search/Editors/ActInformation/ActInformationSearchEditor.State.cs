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
    internal bool AdvancedOpen { get => _draft.AdvancedProperties; set => _draft.AdvancedProperties = value; }

    internal Action ClearPageConditions(bool party, bool map)
    {
        if (!_drafts.TryGetValue(party ? -1 : 0, out var draft)) return () => { };
        if (map)
        {
            var routes = draft.Routes.ToArray(); var properties = draft.Properties.ToArray();
            draft.Routes.Clear(); draft.Properties.Clear();
            return () => { foreach (var p in routes) draft.Routes[p.Key] = p.Value; foreach (var p in properties) draft.Properties[p.Key] = p.Value; };
        }
        var variants = draft.SelectedVariants.Select(s => s.ToArray()).ToArray();
        var bosses = draft.SelectedBosses.Select(s => s.ToArray()).ToArray();
        var secondBosses = draft.SelectedSecondBosses.Select(s => s.ToArray()).ToArray();
        var includesSecond = draft.IncludesSecondBoss.ToArray();
        foreach (var set in draft.SelectedVariants.Concat(draft.SelectedBosses).Concat(draft.SelectedSecondBosses)) set.Clear();
        Array.Clear(draft.IncludesSecondBoss);
        return () => { for (int i = 0; i < variants.Length; i++) {
            draft.SelectedVariants[i].UnionWith(variants[i]); draft.SelectedBosses[i].UnionWith(bosses[i]);
            draft.SelectedSecondBosses[i].UnionWith(secondBosses[i]); draft.IncludesSecondBoss[i] = includesSecond[i]; } };
    }

    internal bool HasConditions(bool party) => _drafts.TryGetValue(party ? -1 : 0, out var d) &&
        (d.Routes.Count > 0 || d.Properties.Values.Any(rows => rows.Count > 0) ||
         d.SelectedVariants.Concat(d.SelectedBosses).Concat(d.SelectedSecondBosses).Any(keys => keys.Count > 0) ||
         d.IncludesSecondBoss.Any(required => required));
    internal Action ClearConditions(bool party)
    {
        int key = party ? -1 : 0;
        bool existed = _drafts.TryGetValue(key, out var saved);
        _drafts[key] = new SeatDraft();
        return () => { if (existed) _drafts[key] = saved!; else _drafts.Remove(key); };
    }

    internal int BossVariantConditionCount() => Enumerable.Range(0, 3).Sum(act =>
        (_draft.SelectedVariants[act].Count > 0 ? 1 : 0) + (_draft.SelectedBosses[act].Count > 0 ? 1 : 0) +
        (_draft.IncludesSecondBoss[act] ? 1 : 0));

    internal int MapConditionCount() => _draft.Routes.Count + _draft.Properties.Values.Sum(conditions => conditions.Count);
}
