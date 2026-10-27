using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class AncientSearchEditor
{
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal bool AdvancedOpen { get => _draft.AdvancedExpanded; set => _draft.AdvancedExpanded = value; }

    internal PartyAncientEditorStateSnapshot ExportSharedPartyEditorState() => new(_partyAdvanced,
        _partyIdentities[0].OrderBy(pair => pair.Key)
            .Select(pair => new PartyAncientIdentitySnapshot(pair.Key, pair.Value.ToArray())).ToArray(),
        _partyIdentities[1].OrderBy(pair => pair.Key)
            .Select(pair => new PartyAncientIdentitySnapshot(pair.Key, pair.Value.ToArray())).ToArray());

    internal AncientEditorStateSnapshot ExportPartyEditorState(int slot)
    {
        return ExportEditorState(PartyDraftOrDefault(slot));
    }

    // An unvisited seat owns an independent empty personal draft, even while
    // another seat is active and the Ancient identities/mode are shared.
    private SeatDraft PartyDraftOrDefault(int slot) => _partyDrafts.TryGetValue(slot, out var draft)
        ? draft : new SeatDraft { Advanced = _partyAdvanced };

    internal AncientEditorStateSnapshot ExportEditorState() => ExportEditorState(_draft);
    private static AncientEditorStateSnapshot ExportEditorState(SeatDraft draft) => new(draft.Mode, draft.LastAdvancedMode,
        draft.Modes.Select(mode => new AncientEditorModeSnapshot(
            mode.Acts.SelectMany(pair => pair.Value.Select(row => new AncientEditorRowSnapshot(
                pair.Key, row.Ancient, row.Options.ToArray(), row.SeaGlassTarget))).ToArray(),
            mode.NeowEnabled, mode.NeowOptions.ToArray(), mode.SelectedAct)).ToArray());

    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.Modes.Any(m => m.NeowEnabled || m.NeowOptions.Count > 0 || m.Acts.Values.Any(rows => rows.Count > 0))) ||
        party && _partyIdentities.Any(mode => mode.Values.Any(keys => keys.Count > 0));
    internal Action ClearConditions(bool party)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts,
            d => new SeatDraft { Eligibility = d.Eligibility, Mode = d.Mode, LastAdvancedMode = d.LastAdvancedMode,
                EligibilityExpanded = d.EligibilityExpanded, AdvancedExpanded = d.AdvancedExpanded });
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

    internal int AuthoredConditionCount()
    {
        ModeDraft mode = _draft.Current;
        int count = _draft.Mode != 0 && mode.NeowEnabled ? 1 + (mode.NeowOptions.Count > 0 ? 1 : 0) : 0;
        if (_multiplayer)
        {
            foreach (int act in new[] { 2, 3 })
            {
                if (SharedAncients(act).Count > 0) count++;
                count += SharedAncients(act).Count(key => mode.Acts[act]
                    .Any(row => row.Ancient == key && (row.Options.Count > 0 || row.SeaGlassTarget.HasValue)));
            }
            return count;
        }
        foreach (List<AncientDraft> rows in mode.Acts.Values)
            if (rows.Count > 0)
                count += 1 + (rows.Any(row => row.Options.Count > 0) ? 1 : 0) +
                    (rows.Any(row => row.SeaGlassTarget.HasValue) ? 1 : 0);
        return count;
    }
}
