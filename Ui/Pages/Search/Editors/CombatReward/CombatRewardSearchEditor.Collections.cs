using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class CombatRewardSearchEditor
{
    private CardCollectionStore? _collections;
    internal event Action<string?>? CardCollectionEditRequested;

    internal void ConfigureCardCollections(CardCollectionStore store) => _collections = store;

    internal CombatCardExclusionSearchCondition? ExportExclusionCondition() => _draft.Exclusion is { } exclusion
        ? exclusion with { BattleCount = _draft.ExclusionBattleCount, CardKeys = exclusion.CardKeys.ToArray() } : null;

    internal CombatCardExclusionSearchCondition? ExportPartyExclusionCondition(int slot) =>
        _partyDrafts.TryGetValue(slot, out var draft) && draft.Exclusion is { } exclusion
            ? exclusion with { BattleCount = draft.ExclusionBattleCount, CardKeys = exclusion.CardKeys.ToArray() } : null;

    internal CombatRewardSearchDraft ExportEditorState() => CaptureEditorState(_draft);
    internal CombatRewardSearchDraft? ExportPartyEditorState(int slot) =>
        _partyDrafts.TryGetValue(slot, out var draft) ? CaptureEditorState(draft) : null;

    private static CombatRewardSearchDraft CaptureEditorState(SeatDraft draft) => new(
        new(draft.BattleRange, draft.Unordered ? CombatRewardSequenceOrderMode.Unordered : CombatRewardSequenceOrderMode.Ordered,
            draft.Slots.Take(draft.BattleRange).ToArray()),
        new(draft.PotionRange, draft.PotionsUnordered ? CombatRewardSequenceOrderMode.Unordered : CombatRewardSequenceOrderMode.Ordered,
            draft.Potions.Take(draft.PotionRange).Select(p => new CombatRewardPotionSlotDraft(p.Requirement, p.PotionKey)).ToArray()))
    { ExcludedCollection = draft.ExcludedCollection?.Copy(), ExclusionBattleCount = draft.ExclusionBattleCount };

    // The query owns execution truth. This sidecar restores the collection's name
    // and revision only after their members are checked against the query.
    internal void ImportEditorState(CombatRewardSearchDraft state)
    {
        if (state.ExclusionBattleCount is < 1 or > 6) throw new InvalidOperationException("integration.load_shape");
        if (state.ExcludedCollection is { } snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Id) || string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Revision < 1 ||
                snapshot.Cards is not { Count: > 0 } || snapshot.Cards.Any(k => !k.IsValid || k.Category != BaseGameModelKeys.Categories.Card) ||
                snapshot.Cards.Distinct(ModelKeyComparer.Instance).Count() != snapshot.Cards.Count ||
                _draft.Exclusion is not { } exclusion || exclusion.CardKeys is null || exclusion.BattleCount != state.ExclusionBattleCount ||
                !new HashSet<ModelKey>(exclusion.CardKeys, ModelKeyComparer.Instance).SetEquals(snapshot.Cards))
                throw new InvalidOperationException("integration.load_shape");
            _draft.ExcludedCollection = snapshot.Copy();
        }
        _draft.ExclusionBattleCount = _draft.Exclusion?.BattleCount ?? state.ExclusionBattleCount;
    }

    private void SelectExcludedCollection(CardCollectionSnapshot snapshot)
    {
        if (snapshot.Cards.Count == 0) return;
        _draft.ExcludedCollection = snapshot.Copy();
        _draft.Exclusion = new(_draft.ExclusionBattleCount, snapshot.Cards.ToArray());
        Render();
    }

    private void RenderCardExclusion(Control content, float width, float top)
    {
        Text(content, _text.Get("query.combat.exclusion.title"), 16, top, width - 32, 18);
        Text(content, _text.Get("query.combat.range"), 16, top + 40, 150, 17, true);
        var decrease = Button(content, "−", 170, top + 36, 38, () => SetExclusionRange(_draft.ExclusionBattleCount - 1), false, 36);
        decrease.Name = "CombatCardExclusionDecrease";
        decrease.Disabled = _draft.ExclusionBattleCount <= MinimumBattleRange;
        Text(content, _draft.ExclusionBattleCount.ToString(), 220, top + 40, 34, 18);
        var increase = Button(content, "+", 258, top + 36, 38, () => SetExclusionRange(_draft.ExclusionBattleCount + 1), false, 36);
        increase.Name = "CombatCardExclusionIncrease";
        increase.Disabled = _draft.ExclusionBattleCount >= MaximumBattleRange;

        var snapshots = _collections?.GetAll().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() ?? [];
        var selected = _draft.ExcludedCollection;
        var selector = new OptionButton { Name = "CombatCardExclusionCollection", Position = new(16, top + 84),
            Size = new(Math.Max(160, width - 150), 40), FitToLongestItem = false, ClipText = true };
        selector.AddThemeFontSizeOverride("font_size", 17);
        // Index zero always represents the retained request, even when the live
        // asset was deleted or edited. Selecting another item is an explicit edit.
        selector.AddItem(selected is { } saved ? _text.Format("query.combat.exclusion.snapshot", saved.Name, saved.Revision, saved.Cards.Count)
            : _draft.Exclusion is { } imported ? _text.Format("query.combat.exclusion.imported", imported.CardKeys.Count)
            : _text.Get("query.combat.exclusion.off"));
        foreach (var snapshot in snapshots)
        {
            selector.AddItem(_text.Format("query.combat.exclusion.snapshot", snapshot.Name, snapshot.Revision, snapshot.Cards.Count));
            selector.SetItemDisabled(selector.ItemCount - 1, snapshot.Cards.Count == 0);
        }
        selector.Select(0);
        selector.ItemSelected += index => { if (index > 0) SelectExcludedCollection(snapshots[(int)index - 1]); };
        content.AddChild(selector);
        var clear = Button(content, _text.Get("query.combat.exclusion.clear"), width - 122, top + 84, 106,
            () => { _draft.Exclusion = null; _draft.ExcludedCollection = null; Render(); }, false);
        clear.Disabled = _draft.Exclusion is null;

        var edit = Button(content, _text.Get("query.combat.exclusion.manage"), 16, top + 136, 180,
            () => CardCollectionEditRequested?.Invoke(selected?.Id), false, 36);
        bool currentAvailable = selected is not null && _collections is not null && _collections.TryGet(selected.Id, out _);
        if (currentAvailable && _collections!.TryGet(selected!.Id, out var current) &&
            (current.Revision != selected.Revision || !new HashSet<ModelKey>(current.Cards, ModelKeyComparer.Instance).SetEquals(selected.Cards)))
        {
            var update = Button(content, _text.Get("query.combat.exclusion.update"), 208, top + 136, 240,
                () => SelectExcludedCollection(current), false, 36);
            update.Disabled = current.Cards.Count == 0;
        }
        var notes = new List<string> { _text.Get("query.combat.exclusion.scope") };
        if (_collections is null || snapshots.Length == 0) notes.Add(_text.Get("query.combat.exclusion.no_collections"));
        if (_collections?.LoadIssues.Count > 0) notes.Add(_text.Get("query.combat.exclusion.load_issues") + " " + string.Join("; ", _collections.LoadIssues));
        if (selected is not null && !currentAvailable) notes.Add(_text.Get("query.combat.exclusion.source_missing"));
        if (snapshots.Any(c => c.Cards.Count == 0)) notes.Add(_text.Get("query.combat.exclusion.empty"));
        if (_draft.Exclusion is { } condition)
        {
            var missing = MissingCollectionCards(condition.CardKeys);
            if (missing is null) notes.Add(_text.Get("query.combat.exclusion.catalog_unknown"));
            else if (missing.Count > 0) notes.Add(_text.Get("query.combat.exclusion.members_missing") + " " + string.Join(", ", missing.Select(k => k.Serialized)));
        }
        var note = Text(content, string.Join("\n", notes), 16, top + 184, width - 32, 16, true);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.ClipText = false;
        note.Size = new(width - 32, 1);
        float height = Math.Max(52, note.GetMinimumSize().Y);
        note.Size = new(width - 32, height);
        content.CustomMinimumSize = new(width, top + 184 + height + 16);
    }

    private static IReadOnlyList<ModelKey>? MissingCollectionCards(IReadOnlyList<ModelKey> keys)
    {
        var domain = RuntimeAuthorityEnvironment.Current.Authority.SemanticUniverse.FirstOrDefault(d => d.Domain == RuntimeAuthorityDomains.Cards);
        if (domain?.AuthorityComplete != true) return null;
        var known = domain.Items.Select(i => i.StableIdentity).ToHashSet(StringComparer.Ordinal);
        return keys.Where(k => !known.Contains(k.Serialized)).ToArray();
    }

    private void SetExclusionRange(int range)
    {
        _draft.ExclusionBattleCount = Math.Clamp(range, MinimumBattleRange, MaximumBattleRange);
        Render();
    }
}
