namespace RolltheSpire2.Ui.Shell;

// Swap UI drafts instead of compiling a Query: incomplete edits must be clearable
// too. Detached drafts retain hidden mode choices for the single-step undo.
internal static class WorkbenchConditionReset
{
    internal static Action Swap<T>(Dictionary<int, T> drafts, Func<T, T> empty, int? onlySlot = null) where T : class
    {
        var saved = drafts.Where(p => onlySlot is null || p.Key == onlySlot).ToArray();
        foreach (var (slot, value) in saved) drafts[slot] = empty(value);
        return () =>
        {
            if (onlySlot is null) drafts.Clear();
            foreach (var (slot, value) in saved) drafts[slot] = value;
        };
    }
}

internal sealed partial class NeowEditorPrototype
{
    internal bool HasConditions(bool party) => (party ? _partyCatalogDrafts : _drafts).Values.Any(d => d.Count > 0) ||
        party && _partyOffers.Values.Any(keys => keys.Count > 0);

    internal Action ClearConditions(bool party, int? slot = null)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyCatalogDrafts : _drafts, _ => new SeatDraft(), slot);
        if (!party) return restore;
        var offers = WorkbenchConditionReset.Swap(_partyOffers, _ => new HashSet<Core.Identity.ModelKey>(), slot);
        var modes = _partyModes.Where(p => slot is null || p.Key == slot).ToArray(); var choices = _partyChoices.Where(p => slot is null || p.Key == slot).ToArray();
        foreach (var pair in modes) _partyModes.Remove(pair.Key); foreach (var pair in choices) _partyChoices.Remove(pair.Key);
        return () =>
        {
            restore(); offers(); foreach (var pair in modes) _partyModes.Remove(pair.Key); foreach (var pair in choices) _partyChoices.Remove(pair.Key);
            foreach (var (slot, mode) in modes) _partyModes[slot] = mode;
            foreach (var (slot, value) in choices) _partyChoices[slot] = value;
        };
    }
}

internal sealed partial class CombatRewardEditorPrototype
{
    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.Slots.Any(k => k.HasValue) || d.Potions.Any(p => !p.IsNeutral));
    internal Action ClearConditions(bool party, int? slot = null) =>
        WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft(), slot);
}

internal sealed partial class ShopEditorPrototype
{
    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.Relic.Slots.Concat(d.Uncommon.Slots).Concat(d.Rare.Slots).Any(k => k.HasValue));
    internal Action ClearConditions(bool party, int? slot = null) =>
        WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft(), slot);
}

internal sealed partial class RelicSequenceEditorPrototype
{
    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d => d.Conditions.Count > 0);
    internal Action ClearConditions(bool party, int? slot = null) =>
        WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft(), slot);
}

internal sealed partial class AncientEditorPrototype
{
    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.Modes.Any(m => m.NeowEnabled || m.NeowOptions.Count > 0 || m.Acts.Values.Any(rows => rows.Count > 0))) ||
        party && _partyIdentities.Any(mode => mode.Values.Any(keys => keys.Count > 0));
    internal Action ClearConditions(bool party)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts,
            d => new SeatDraft { Eligibility = d.Eligibility, Mode = d.Mode, LastAdvancedMode = d.LastAdvancedMode,
                EligibilityExpanded = d.EligibilityExpanded });
        if (!party) return restore;
        var identities = _partyIdentities.Select(mode => mode.ToDictionary(p => p.Key, p => p.Value.ToArray())).ToArray();
        foreach (var mode in _partyIdentities) foreach (var list in mode.Values) list.Clear();
        return () =>
        {
            restore();
            for (int i = 0; i < _partyIdentities.Length; i++)
                foreach (var (act, keys) in identities[i])
                { _partyIdentities[i][act].Clear(); _partyIdentities[i][act].AddRange(keys); }
        };
    }
}

internal sealed partial class EventEditorPrototype
{
    internal bool HasConditions(bool party) => party && _partyQueue.Count > 0 || (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.QueueConditions.Count > 0 || d.Results.Values.Any(keys => keys.Any(k => k.HasValue)) ||
        d.CharacterColor >= 0 || d.TrialCase >= 0 || d.TinkerType >= 0 || d.PrototypeTargets.Count > 0);
    internal Action ClearConditions(bool party)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft());
        var queue = _partyQueue.ToArray();
        if (party) _partyQueue.Clear();
        return () => { restore(); if (party) { _partyQueue.Clear(); _partyQueue.AddRange(queue); } };
    }
}

internal sealed partial class ActInformationEditorPrototype
{
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
        foreach (var set in draft.SelectedVariants.Concat(draft.SelectedBosses)) set.Clear();
        return () => { for (int i = 0; i < variants.Length; i++) { draft.SelectedVariants[i].UnionWith(variants[i]); draft.SelectedBosses[i].UnionWith(bosses[i]); } };
    }

    internal bool HasConditions(bool party) => _drafts.TryGetValue(party ? -1 : 0, out var d) &&
        (d.Routes.Count > 0 || d.Properties.Values.Any(rows => rows.Count > 0) ||
         d.SelectedVariants.Concat(d.SelectedBosses).Any(keys => keys.Count > 0));
    internal Action ClearConditions(bool party)
    {
        int key = party ? -1 : 0;
        bool existed = _drafts.TryGetValue(key, out var saved);
        _drafts[key] = new SeatDraft();
        return () => { if (existed) _drafts[key] = saved!; else _drafts.Remove(key); };
    }
}

internal sealed partial class TransformationEditorPrototype
{
    internal Action PreserveConditionsForUndo()
    {
        var saved = _drafts.ToDictionary(p => p.Key, p =>
        {
            var copy = new SeatDraft { Objective = p.Value.Objective, ResultCount = p.Value.ResultCount, RareCount = p.Value.RareCount };
            copy.TakenOver.UnionWith(p.Value.TakenOver); copy.Cards.AddRange(p.Value.Cards); return copy;
        });
        return () => { _drafts.Clear(); foreach (var pair in saved) _drafts[pair.Key] = pair.Value; };
    }

    internal bool HasConditions() => _drafts.Values.Any(d => d.TakenOver.Count > 0 || d.Cards.Count > 0);
    internal Action ClearConditions() => WorkbenchConditionReset.Swap(_drafts, _ => new SeatDraft());
}
