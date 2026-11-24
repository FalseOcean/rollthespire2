using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class AncientSearchEditor
{
    internal static IReadOnlyList<string> NeowRelationKeys { get; } = ["any", "all", "only", "ban"];

    private static SearchQuery ExportNeowOffers(SearchQuery query, SeatDraft draft)
    {
        var keys = draft.ActiveNeowOffers.ToArray();
        int relation = draft.NeowAdvanced ? draft.NeowRelation : 0;
        var set = keys.Length == 0 ? ModelKeySetFilter.Empty : relation switch
        {
            1 => new ModelKeySetFilter([], keys, []),
            3 => new ModelKeySetFilter([], [], keys),
            _ => relation == 2 ? ModelKeySetFilter.Empty : new ModelKeySetFilter(keys, [], [])
        };
        return query with { LegacyNeow = query.LegacyNeow with { NeowRelics = set },
            NeowOfferWhitelist = relation == 2 && (keys.Length > 0 || draft.NeowEmptyWhitelist) ? new(keys) : null };
    }

    internal IEnumerable<(int Slot, int Relation, IReadOnlyList<ModelKey> Targets, bool Active)> NeowResearchSummary(bool party)
    {
        foreach (var (slot, draft) in (party ? _partyDrafts : _drafts).OrderBy(pair => pair.Key))
        {
            if (!HasNeowResearchDraft(draft)) continue;
            if (draft.NeowBasic.Count > 0) yield return (slot, 0, draft.NeowBasic, !draft.NeowAdvanced);
            for (int relation = 0; relation < draft.NeowRelations.Length; relation++)
                if (draft.NeowRelations[relation].Count > 0 || relation == 2 && draft.NeowEmptyWhitelist)
                    yield return (slot, relation, draft.NeowRelations[relation], draft.NeowAdvanced && draft.NeowRelation == relation);
        }
    }

    private static NeowOfferEditorStateSnapshot ExportNeowOfferState(SeatDraft draft) => new(
        draft.NeowAdvanced, draft.NeowRelation, draft.NeowBasic.ToArray(),
        draft.NeowRelations.Select(keys => (IReadOnlyList<ModelKey>)keys.ToArray()).ToArray())
        { EmptyWhitelistEnabled = draft.NeowEmptyWhitelist };

    private void ImportNeowOffers(SearchQuery query, AncientEditorStateSnapshot? saved)
    {
        _draft.NeowBasic.Clear();
        _draft.NeowEmptyWhitelist = false;
        foreach (var keys in _draft.NeowRelations) keys.Clear();
        var legacy = query.LegacyNeow.NeowRelics;
        _draft.NeowStateExplicit = saved?.NeowOffers is not null ||
            saved is null && (query.NeowOfferWhitelist is not null || !legacy.IsEmpty);
        if (saved?.NeowOffers is { } state)
        {
            if (state.Relation is < 0 or > 3 || state.BasicSelection.Count > 1 || state.AdvancedSelections.Count != 4 ||
                state.BasicSelection.Concat(state.AdvancedSelections.SelectMany(keys => keys)).Any(key => !key.IsValid || key.Category != "RELIC"))
                throw new InvalidOperationException("integration.load_shape");
            _draft.NeowAdvanced = state.Advanced;
            _draft.NeowRelation = state.Relation;
            _draft.NeowEmptyWhitelist = state.EmptyWhitelistEnabled;
            _draft.NeowBasic.AddRange(state.BasicSelection);
            for (int i = 0; i < 4; i++) _draft.NeowRelations[i].AddRange(state.AdvancedSelections[i]);
        }
        else
        {
            // Preserve both historical advanced drafts; only the active one is a query.
            if (saved is not null)
            {
                var old = saved.Modes[saved.ActiveMode];
                var oldKeys = old.NeowEnabled && saved.ActiveMode != 0 ? old.NeowOptions : [];
                var oldFilter = saved.ActiveMode == 2 ? new ModelKeySetFilter([], oldKeys, []) : new(oldKeys, [], []);
                if (query.NeowOfferWhitelist is not null ||
                    !oldFilter.Any.ToHashSet().SetEquals(legacy.Any) || !oldFilter.All.ToHashSet().SetEquals(legacy.All) || legacy.Ban.Count != 0)
                    throw new InvalidOperationException("integration.load_shape");
                _draft.NeowRelations[0].AddRange(saved.Modes[1].NeowOptions);
                _draft.NeowRelations[1].AddRange(saved.Modes[2].NeowOptions);
            }
            _draft.NeowRelation = query.NeowOfferWhitelist is not null ? 2 : legacy.Ban.Count > 0 ? 3 : legacy.All.Count > 0 ? 1 : 0;
            _draft.NeowAdvanced = query.NeowOfferWhitelist is not null || !legacy.IsEmpty;
            _draft.NeowEmptyWhitelist = query.NeowOfferWhitelist is { AllowedKeys.Count: 0 };
            var active = _draft.NeowRelations[_draft.NeowRelation];
            active.Clear();
            active.AddRange(query.NeowOfferWhitelist?.AllowedKeys ?? legacy.Any.Concat(legacy.All).Concat(legacy.Ban).ToArray());
        }
        static string Intent(SearchQuery q) => System.Text.Json.JsonSerializer.Serialize(new {
            Any = q.LegacyNeow.NeowRelics.Any.OrderBy(k => k.Serialized).ToArray(),
            All = q.LegacyNeow.NeowRelics.All.OrderBy(k => k.Serialized).ToArray(),
            Ban = q.LegacyNeow.NeowRelics.Ban.OrderBy(k => k.Serialized).ToArray(),
            Allowed = q.NeowOfferWhitelist?.AllowedKeys.OrderBy(k => k.Serialized).ToArray() });
        if (Intent(query) != Intent(ExportNeowOffers(SearchQuery.Empty, _draft)))
            throw new InvalidOperationException("integration.load_shape");
    }

    private float RenderNeowOffers(Control content, float width, float top)
    {
        bool narrow = width < 650;
        int activeRelation = _draft.NeowAdvanced ? _draft.NeowRelation : 0;
        int relationColumns = width < 420 ? 1 : narrow ? 2 : 4;
        float relationWidth = (width - 32 - (relationColumns - 1) * 8) / relationColumns;
        for (int i = 0; i < NeowRelationKeys.Count; i++)
        {
            int relation = i;
            ModeButton(content, _text.Get("query.ancient.neow." + NeowRelationKeys[i]),
                16 + i % relationColumns * (relationWidth + 8), top + i / relationColumns * 44, relationWidth,
                () =>
                {
                    if (relation == activeRelation) return;
                    _draft.NeowStateExplicit = _draft.NeowAdvanced = true;
                    _draft.NeowRelation = relation;
                    if (relation == 2 && _draft.NeowRelations[2].Count == 0) _draft.NeowEmptyWhitelist = true;
                    Render();
                }, activeRelation == i, 36).Name = "NeowOffersRelation" + i;
        }
        float y = top + (NeowRelationKeys.Count / relationColumns) * 44;
        string noteKey = _draft.NeowAdvanced && _draft.NeowRelation == 2 && _draft.NeowEmptyWhitelist &&
            _draft.ActiveNeowOffers.Count == 0 ? "query.ancient.neow.empty_only" : "query.ancient.neow.menu_scope";
        var note = Text(content, _text.Get(noteKey), 16, y, width - 32, 14, true);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.Size = new(width - 32, narrow ? 52 : 28);
        y += narrow ? 56 : 30;
        var selected = _draft.ActiveNeowOffers;
        var candidates = NeowOptionCandidates().Concat(selected).Distinct(ModelKeyComparer.Instance).ToArray();
        int columns = Math.Max(1, (int)((width - 32) / 152));
        const float gap = 8, tileHeight = 48;
        float tileWidth = Math.Min(144, width - 32);
        void Toggle(ModelKey key)
        {
            _draft.NeowStateExplicit = true;
            var targets = selected;
            if (!_draft.NeowAdvanced)
            {
                // The legacy single offer already uses Any semantics. Carry
                // those targets forward when the user edits the visible list.
                _draft.NeowAdvanced = true;
                _draft.NeowRelation = 0;
                targets = _draft.NeowRelations[0];
                targets.Clear();
                targets.AddRange(selected);
            }
            if (!targets.Remove(key)) targets.Add(key);
            if (_draft.NeowRelation == 2) _draft.NeowEmptyWhitelist = targets.Count == 0;
            Render();
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            ModelKey key = candidates[i];
            bool active = selected.Contains(key, ModelKeyComparer.Instance);
            var choice = Button(content, string.Empty, 16 + i % columns * (tileWidth + gap),
                y + i / columns * (tileHeight + gap), tileWidth, () => Toggle(key), active, tileHeight);
            _p.ApplyCandidateSurface(choice, active);
            AddPicture(choice, key, GameContentKind.Relic, IconVariant.RelicLarge, 6, 8, 32);
            var label = Text(choice, _names.Resolve(key, GameContentKind.Relic), 46, 5, tileWidth - 52, 12);
            label.Size = new(tileWidth - 52, tileHeight - 10);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = MouseFilterEnum.Ignore;
            choice.Name = "NeowOffer_" + key.Entry;
        }
        y += Math.Max(1, (candidates.Length + columns - 1) / columns) * (tileHeight + gap) + 12;
        if (_draft.NeowAdvanced && _draft.NeowBasic.Count > 0)
        {
            string prior = string.Join(" · ", _draft.NeowBasic.Select(key => _names.Resolve(key, GameContentKind.Relic)));
            ModeButton(content, _text.Format("query.dead_seed.neow.restore_single", prior), 16, y, width - 32, () =>
            {
                _draft.NeowStateExplicit = true;
                _draft.NeowAdvanced = false;
                Render();
            }, false, 36).Name = "RestoreSavedSingleNeowOffer";
            y += 44;
        }
        return y + 12;
    }
}
