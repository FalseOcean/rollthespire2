using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ShopSearchEditor
{
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.Relic.Slots.Concat(d.Uncommon.Slots).Concat(d.Rare.Slots).Any(k => k.HasValue));
    internal Action ClearConditions(bool party, int? slot = null) =>
        WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft(), slot);

    internal int AuthoredConditionCount() => CountRow(_draft.Relic) + CountRow(_draft.Uncommon) + CountRow(_draft.Rare);

    private int CountRow(RowDraft row)
    {
        int filled = row.Slots.Take(row.ShopRange).Count(key => key.HasValue);
        return row.OrderMode == CombatRewardSequenceOrderMode.Ordered ? filled : filled > 0 ? 1 : 0;
    }
}
