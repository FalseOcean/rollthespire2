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
    internal SearchQuery ExportSharedPartyQuery(SearchQuery q) => q with
    {
        LegacyWorld = q.LegacyWorld with
        {
            AncientIdentityFilters = _partyIdentities[_partyAdvanced ? 1 : 0].OrderBy(pair => pair.Key)
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => new ActModelKeySetFilter(pair.Key, new(pair.Value.ToArray(), [], []))).ToArray()
        }
    };

    internal void ImportSharedPartyQuery(SearchQuery q, PartyAncientEditorStateSnapshot? saved = null,
        IReadOnlyList<WorkbenchPlayerDraft>? players = null)
    {
        foreach (Dictionary<int, List<ModelKey>> mode in _partyIdentities)
            foreach (List<ModelKey> identities in mode.Values) identities.Clear();
        if (saved is not null)
        {
            static void Restore(IReadOnlyList<PartyAncientIdentitySnapshot> source,
                Dictionary<int, List<ModelKey>> target, int limit)
            {
                if (source.GroupBy(item => item.Act).Any(group => group.Count() > 1) ||
                    source.Any(item => item.Act is not (2 or 3) || item.Ancients.Count > limit ||
                        item.Ancients.Any(key => !key.IsValid) || item.Ancients.Distinct(ModelKeyComparer.Instance).Count() != item.Ancients.Count))
                    throw new InvalidOperationException("integration.load_shape");
                foreach (PartyAncientIdentitySnapshot item in source) target[item.Act].AddRange(item.Ancients);
            }
            Restore(saved.DefaultIdentities, _partyIdentities[0], 1);
            Restore(saved.AdvancedIdentities, _partyIdentities[1], 4);
            _partyAdvanced = saved.Advanced;
            foreach (int act in new[] { 2, 3 })
            {
                ModelKey[] authored = q.LegacyWorld.AncientIdentityFilters.Where(filter => filter.Act == act)
                    .SelectMany(filter => filter.Keys.Any).ToArray();
                ModelKey[] restored = _partyIdentities[_partyAdvanced ? 1 : 0][act].ToArray();
                if (!authored.ToHashSet(ModelKeyComparer.Instance).SetEquals(restored))
                    throw new InvalidOperationException("integration.load_shape");
            }
        }
        else
        {
            _partyAdvanced = players?.Any(player => player.AncientEditor?.ActiveMode > 0 ||
                player.AncientEditor is null && q.Players.Where(p => p.Slot == player.Slot).Any(p =>
                    p.Conditions.AncientBranches.GroupBy(b => b.Act).Any(g => g.Count() > 1) ||
                    p.Conditions.AncientBranches.Any(b => b.OptionAny.Count > 1) ||
                    p.Conditions.LegacyWorld.AncientOptionFilters.Any(f => f.Keys.All.Count > 0))) ?? _partyAdvanced;
            foreach (int act in new[] { 2, 3 })
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    var sets = players?.Select(player =>
                    {
                        AncientEditorStateSnapshot? editor = player.AncientEditor;
                        // Older/semantic-only party drafts have no UI snapshot. Their
                        // active per-player branches still constrain the shared identity.
                        if (editor is null)
                            return mode == (_partyAdvanced ? 1 : 0)
                                ? q.Players.Where(p => p.Slot == player.Slot)
                                    .SelectMany(p => p.Conditions.AncientBranches)
                                    .Where(branch => branch.Act == act).Select(branch => branch.AncientKey)
                                    .Distinct(ModelKeyComparer.Instance).ToArray()
                                : Array.Empty<ModelKey>();
                        if (editor.Modes.Count != 3) return Array.Empty<ModelKey>();
                        int personalMode = mode == 0 ? 0 : editor.ActiveMode == 0 ? editor.LastAdvancedMode : editor.ActiveMode;
                        return editor.Modes[personalMode].Rows.Where(row => row.Act == act)
                            .Select(row => row.Ancient).Distinct(ModelKeyComparer.Instance).ToArray();
                    }).Where(set => set.Length > 0).ToArray() ?? [];
                    ModelKey[] common = sets.Length == 0 ? [] : sets.Skip(1)
                        .Aggregate(sets[0].AsEnumerable(), (keys, set) => keys.Intersect(set, ModelKeyComparer.Instance))
                        .ToArray();
                    bool activeWithoutSharedFilter = mode == (_partyAdvanced ? 1 : 0) &&
                        !q.LegacyWorld.AncientIdentityFilters.Any(filter => filter.Act == act);
                    if (sets.Length > 0 && common.Length == 0 && activeWithoutSharedFilter)
                        throw new InvalidOperationException("integration.load_shape");
                    _partyIdentities[mode][act].AddRange(common);
                }
                ActModelKeySetFilter? authored = q.LegacyWorld.AncientIdentityFilters.FirstOrDefault(filter => filter.Act == act);
                if (authored is not null)
                {
                    List<ModelKey> current = _partyIdentities[_partyAdvanced ? 1 : 0][act];
                    current.Clear(); current.AddRange(authored.Keys.Any);
                }
            }
        }
        foreach (SeatDraft draft in _partyDrafts.Values) draft.Advanced = _partyAdvanced;
    }

    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal SearchQuery ExportPartyQuery(int slot, SearchQuery q)
    {
        var draft = PartyDraftOrDefault(slot);
        var active = _draft;
        try { _draft = draft; return ExportQuery(q); }
        finally { _draft = active; }
    }
    internal AncientOptionConditionProfile PartyOptionConditions(int slot) =>
        _partyDrafts.TryGetValue(slot, out var draft) ? draft.Eligibility : AncientOptionConditionProfile.BroadDefault;

    internal SearchQuery ExportQuery(SearchQuery q)
    {
        var branches = new List<AncientSearchBranchCondition>();
        var all = new List<ActModelKeySetFilter>();
        foreach (int act in new[] { 2, 3 })
        {
            IEnumerable<AncientDraft> rows = _multiplayer
                ? SharedAncients(act).Select(key => _draft.Current.Acts[act].FirstOrDefault(row => row.Ancient == key)
                    ?? new AncientDraft(key))
                : _draft.Acts[act];
            foreach (AncientDraft row in rows)
            {
                bool together = _draft.RequireAll;
                branches.Add(new(act, row.Ancient, together ? [] : row.Options.ToArray(),
                    row.SeaGlassTarget is { } target ? [target] : []));
                // Sea Glass's target requires its parent option in the modern branch.
                if (together && row.SeaGlassTarget.HasValue)
                    branches[^1] = branches[^1] with { OptionAny = row.Options.Where(k => k.Entry == "SEA_GLASS").ToArray() };
                if (together && row.Options.Count > 0) all.Add(new(act, new([], row.Options.ToArray(), [])));
            }
        }
        ModelKeySetFilter neow = _draft.Mode == 0 || !_draft.NeowEnabled ? ModelKeySetFilter.Empty : _draft.OfferedTogether
            ? new([], _draft.NeowOptions.ToArray(), []) : new(_draft.NeowOptions.ToArray(), [], []);
        return q with { AncientBranches = branches.ToArray(),
            LegacyWorld = q.LegacyWorld with { AncientOptionFilters = all.ToArray() },
            LegacyNeow = q.LegacyNeow with { NeowRelics = neow } };
    }
    internal void ImportQuery(SearchQuery q, AncientOptionConditionProfile premises,
        AncientEditorStateSnapshot? saved = null)
    {
        _draft.Eligibility = premises;
        foreach (ModeDraft mode in _draft.Modes)
        {
            mode.Acts[2].Clear(); mode.Acts[3].Clear(); mode.NeowOptions.Clear();
            mode.NeowEnabled = false; mode.SelectedAct = 1;
        }
        if (saved is not null)
        {
            if (saved.Modes.Count != 3 || saved.ActiveMode is < 0 or > 2 ||
                saved.LastAdvancedMode is < 1 or > 2 || saved.Modes.Any(mode =>
                    mode.SelectedAct is < 1 or > 3 || mode.Rows.Any(row => row.Act is not (2 or 3))))
                throw new InvalidOperationException("integration.load_shape");
            for (int index = 0; index < 3; index++)
            {
                AncientEditorModeSnapshot source = saved.Modes[index];
                ModeDraft target = _draft.Modes[index];
                foreach (AncientEditorRowSnapshot row in source.Rows)
                {
                    var restored = new AncientDraft(row.Ancient) { SeaGlassTarget = row.SeaGlassTarget };
                    restored.Options.AddRange(row.Options);
                    target.Acts[row.Act].Add(restored);
                }
                target.NeowEnabled = source.NeowEnabled;
                target.NeowOptions.AddRange(source.NeowOptions);
                target.SelectedAct = source.SelectedAct;
            }
            _draft.Mode = saved.ActiveMode;
            _draft.LastAdvancedMode = saved.LastAdvancedMode;
            // UI sidecars retain inactive preferences; they may not replace the
            // active personal requirements declared by the saved query, including
            // the Neow offers authored on this page. This also
            // protects older drafts affected by an unvisited seat copying P1.
            if (ActiveAncientIntent(q) != ActiveAncientIntent(ExportQuery(SearchQuery.Empty)))
                throw new InvalidOperationException("integration.load_shape");
        }
        else
        {
            bool together = q.LegacyWorld.AncientOptionFilters.Any(c => c.Keys.All.Count > 0) ||
                            q.LegacyNeow.NeowRelics.All.Count > 0;
            bool advancedAny = q.LegacyNeow.NeowRelics.Any.Count > 0 ||
                               q.AncientBranches.GroupBy(branch => branch.Act).Any(group => group.Count() > 1) ||
                               q.AncientBranches.Any(branch => branch.OptionAny.Count > 1);
            _draft.Mode = together ? 2 : advancedAny ? 1 : 0;
            _draft.LastAdvancedMode = _draft.Mode == 0 ? 2 : _draft.Mode;
            ModeDraft target = _draft.Current;
            foreach (AncientSearchBranchCondition branch in q.AncientBranches)
            {
                var row = new AncientDraft(branch.AncientKey)
                { SeaGlassTarget = branch.SeaGlassTargetAny.Select(key => (ModelKey?)key).FirstOrDefault() };
                row.Options.AddRange(together
                    ? q.LegacyWorld.AncientOptionFilters.Where(filter => filter.Act == branch.Act)
                        .SelectMany(filter => filter.Keys.All).Distinct(ModelKeyComparer.Instance)
                    : branch.OptionAny);
                target.Acts[branch.Act].Add(row);
            }
            target.NeowOptions.AddRange(q.LegacyNeow.NeowRelics.Any.Concat(q.LegacyNeow.NeowRelics.All));
            target.NeowEnabled = target.NeowOptions.Count > 0;
        }
        _draft.Context = "";
    }

    private string ActiveAncientIntent(SearchQuery query)
    {
        static string Keys(IEnumerable<ModelKey> keys) => System.Text.Json.JsonSerializer.Serialize(
            keys.Select(key => key.Serialized).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        // Shared identity owns empty personal rows in a party. Restrictive rows
        // remain personal, including OR alternatives and their attached targets.
        var rows = query.AncientBranches.GroupBy(row => row.Act)
            .Where(group => !_multiplayer || group.Any(row => row.OptionAny.Count > 0 || row.SeaGlassTargetAny.Count > 0))
            .SelectMany(group => group)
            .Select(row => System.Text.Json.JsonSerializer.Serialize(new
                { row.Act, row.AncientKey, Options = Keys(row.OptionAny), Targets = Keys(row.SeaGlassTargetAny) }))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var groups = query.LegacyWorld.AncientOptionFilters.Select(filter => System.Text.Json.JsonSerializer.Serialize(new
                { filter.Act, Any = Keys(filter.Keys.Any), All = Keys(filter.Keys.All), Ban = Keys(filter.Keys.Ban) }))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var neow = query.LegacyNeow.NeowRelics;
        return System.Text.Json.JsonSerializer.Serialize(new { Rows = rows, Groups = groups,
            Neow = new { Any = Keys(neow.Any), All = Keys(neow.All), Ban = Keys(neow.Ban) } });
    }
}
