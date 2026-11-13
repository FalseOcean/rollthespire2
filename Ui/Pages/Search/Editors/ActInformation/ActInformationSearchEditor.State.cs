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
        return WorkbenchConditionReset.Swap(_drafts, saved =>
        {
            var cleared = CopyForConditionReset(saved);
            if (map) { cleared.Routes.Clear(); cleared.Properties.Clear(); cleared.Drawings.Clear(); }
            else
            {
                foreach (var set in cleared.SelectedVariants.Concat(cleared.SelectedBosses).Concat(cleared.SelectedSecondBosses)) set.Clear();
                Array.Clear(cleared.IncludesSecondBoss);
            }
            return cleared;
        }, party ? -1 : 0, (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });
    }

    internal Action ClearResearchConditions(bool party) =>
        WorkbenchConditionReset.Swap(_drafts, saved =>
        {
            var cleared = CopyForConditionReset(saved);
            cleared.EliteResearchEnabled = false;
            cleared.EliteResearchCounts = ElitePrefixResearchCondition.FloorSeven;
            cleared.EncounterQueues.Clear();
            return cleared;
        }, party ? -1 : 0, (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });

    private static SeatDraft CopyForConditionReset(SeatDraft saved)
    {
        var copy = new SeatDraft { Context = saved.Context, Players = saved.Players, Ascension = saved.Ascension,
            Catalog = saved.Catalog, AdvancedProperties = saved.AdvancedProperties, Scope = saved.Scope,
            EliteResearchEnabled = saved.EliteResearchEnabled, EliteResearchCounts = saved.EliteResearchCounts };
        for (int index = 0; index < saved.SelectedVariants.Length; index++)
        {
            copy.SelectedVariants[index].UnionWith(saved.SelectedVariants[index]);
            copy.SelectedBosses[index].UnionWith(saved.SelectedBosses[index]);
            copy.SelectedSecondBosses[index].UnionWith(saved.SelectedSecondBosses[index]);
        }
        copy.EncounterQueues.AddRange(saved.EncounterQueues);
        saved.IncludesSecondBoss.CopyTo(copy.IncludesSecondBoss, 0);
        saved.DrawingModes.CopyTo(copy.DrawingModes, 0);
        foreach (var pair in saved.Properties) copy.Properties[pair.Key] = pair.Value.ToList();
        foreach (var pair in saved.Routes) copy.Routes[pair.Key] = pair.Value;
        foreach (var pair in saved.Drawings) copy.Drawings[pair.Key] = pair.Value.ToList();
        foreach (var pair in saved.DailyMetrics) copy.DailyMetrics[pair.Key] = pair.Value;
        return copy;
    }

    internal bool HasConditions(bool party) => _drafts.TryGetValue(party ? -1 : 0, out var d) &&
        (HasResearch(d) || Enumerable.Range(0, 4).Any(scope => ScopeConditionCount(d, scope) > 0) ||
         d.SelectedVariants.Concat(d.SelectedBosses).Concat(d.SelectedSecondBosses).Any(keys => keys.Count > 0) ||
         d.IncludesSecondBoss.Any(required => required));
    internal Action ClearConditions(bool party) => WorkbenchConditionReset.Swap(_drafts, _ => new SeatDraft(), party ? -1 : 0,
        (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });

    internal int BossVariantConditionCount() => Enumerable.Range(0, 3).Sum(act =>
        (_draft.SelectedVariants[act].Count > 0 ? 1 : 0) + (_draft.SelectedBosses[act].Count > 0 ? 1 : 0) +
        (_draft.IncludesSecondBoss[act] ? 1 : 0));

    internal int MapConditionCount() => Enumerable.Range(0, 4).Sum(scope => ScopeConditionCount(_draft, scope)) +
        (_draft.Players == 1 && _unknownRoomPrefix?.HasConditions == true ? 1 : 0);

    internal int EliteResearchConditionCount => _draft.EliteResearchEnabled ? 1 : 0;
    internal ElitePrefixResearchCondition? EliteResearchDraft => _draft.EliteResearchEnabled ||
        _draft.EliteResearchCounts != ElitePrefixResearchCondition.FloorSeven ? _draft.EliteResearchCounts : null;
    internal int EncounterResearchConditionCount => _draft.EncounterQueues.Count(q => q.AllowedKeys.Count > 0);
    internal bool HasResearchDraft => HasResearch(_draft);
    private static bool HasResearch(SeatDraft draft) => draft.EliteResearchEnabled ||
        draft.EliteResearchCounts != ElitePrefixResearchCondition.FloorSeven || draft.EncounterQueues.Count > 0;
    internal IReadOnlyList<EncounterQueueSearchCondition> EncounterResearchDraft => _draft.EncounterQueues;
}
