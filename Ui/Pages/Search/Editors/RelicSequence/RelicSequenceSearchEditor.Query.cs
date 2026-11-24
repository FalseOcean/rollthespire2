using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class RelicSequenceSearchEditor
{
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal IReadOnlyList<RelicSequenceSearchCondition> BuildPartyConditions(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return [];
        var active = _draft;
        try { _draft = draft; return BuildConditions(); }
        finally { _draft = active; }
    }

    internal void ImportQuery(SearchQuery q)
    {
        var treasure = q.TreasureRelics ?? TreasureRelicSearchCondition.Empty;
        _draft.TreasureStart = treasure.StartOrdinal;
        _draft.TreasureCount = treasure.Count;
        _draft.TreasureOrder = treasure.OrderMode;
        _draft.TreasureSlots = Enumerable.Range(0, 2).Select(i => treasure.Slots.ElementAtOrDefault(i)).ToArray();
        if (!_party && !treasure.IsEmpty) _treasurePage = true;
        _draft.Conditions.Clear();
        foreach (var c in q.RelicSequenceConstraints)
        {
            foreach (var key in c.Keys.Any.Concat(c.Keys.All))
                _draft.Conditions.Add(new(Guid.NewGuid(), c.Lane, c.RangeMode, c.RangeValue, key, RelicSequenceUiMatchMode.Appears));
            foreach (var key in c.Keys.Ban)
                _draft.Conditions.Add(new(Guid.NewGuid(), c.Lane, c.RangeMode, c.RangeValue, key, RelicSequenceUiMatchMode.Excluded));
        }
        _draft.Context = "";
    }
}
