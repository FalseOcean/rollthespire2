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
            mode.NeowEnabled, mode.NeowOptions.ToArray(), mode.SelectedAct)).ToArray())
        { NeowOffers = draft.NeowStateExplicit ? ExportNeowOfferState(draft) : null };

    private static bool HasNeowResearchDraft(SeatDraft draft) =>
        draft.NeowBasic.Count > 0 || draft.NeowEmptyWhitelist || draft.NeowRelations.Any(keys => keys.Count > 0);

    private static bool HasActiveNeowResearch(SeatDraft draft) => draft.ActiveNeowOffers.Count > 0 ||
        draft.NeowAdvanced && draft.NeowRelation == 2 && draft.NeowEmptyWhitelist;

    internal bool HasResearchDraft(bool party) => (party ? _partyDrafts : _drafts).Values.Any(HasNeowResearchDraft);

    internal int NeowResearchConditionCount(bool party) => (party ? _partyDrafts : _drafts).Values.Count(HasActiveNeowResearch);

    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        HasNeowResearchDraft(d) || d.Modes.Any(m => m.Acts.Values.Any(rows => rows.Count > 0))) ||
        party && _partyIdentities.Any(mode => mode.Values.Any(keys => keys.Count > 0));

    internal Action ClearResearchConditions(bool party) => WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts,
        d => CopyForConditionReset(d, keepAncients: true, keepResearch: false),
        replaceActive: (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });

    internal Action ClearConditions(bool party, bool keepResearch = false)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts,
            d => CopyForConditionReset(d, keepAncients: false, keepResearch),
            replaceActive: (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });
        if (!party) return restore;
        var identities = _partyIdentities.Select(mode => mode.ToDictionary(p => p.Key, p => p.Value.ToArray())).ToArray();
        foreach (var mode in _partyIdentities) foreach (var list in mode.Values) list.Clear();
        return () =>
        {
            restore();
            for (int i = 0; i < _partyIdentities.Length; i++)
            {
                _partyIdentities[i].Clear();
                foreach (var (act, keys) in identities[i])
                    _partyIdentities[i][act] = keys.ToList();
            }
        };
    }

    private static SeatDraft CopyForConditionReset(SeatDraft draft, bool keepAncients, bool keepResearch)
    {
        var copy = new SeatDraft
        {
            Context = draft.Context, Problem = draft.Problem, Catalog = draft.Catalog, NeowCatalog = draft.NeowCatalog,
            Eligibility = draft.Eligibility, EligibilityExpanded = draft.EligibilityExpanded,
            Mode = draft.Mode, LastAdvancedMode = draft.LastAdvancedMode,
            NeowAdvanced = draft.NeowAdvanced, NeowRelation = draft.NeowRelation
        };
        if (keepAncients)
            for (int i = 0; i < draft.Modes.Length; i++)
            {
                var old = draft.Modes[i];
                var current = copy.Modes[i];
                current.SelectedAct = old.SelectedAct;
                foreach (var (act, rows) in old.Acts)
                    foreach (var row in rows)
                    {
                        var clone = new AncientDraft(row.Ancient) { SeaGlassTarget = row.SeaGlassTarget };
                        clone.Options.AddRange(row.Options);
                        current.Acts[act].Add(clone);
                    }
            }
        if (keepResearch)
        {
            copy.NeowStateExplicit = draft.NeowStateExplicit;
            copy.NeowEmptyWhitelist = draft.NeowEmptyWhitelist;
            copy.NeowBasic.AddRange(draft.NeowBasic);
            for (int i = 0; i < draft.Modes.Length; i++)
            {
                copy.Modes[i].NeowEnabled = draft.Modes[i].NeowEnabled;
                copy.Modes[i].NeowOptions.AddRange(draft.Modes[i].NeowOptions);
            }
            for (int i = 0; i < draft.NeowRelations.Length; i++)
                copy.NeowRelations[i].AddRange(draft.NeowRelations[i]);
        }
        return copy;
    }

    internal int AuthoredConditionCount()
    {
        ModeDraft mode = _draft.Current;
        int count = 0;
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
