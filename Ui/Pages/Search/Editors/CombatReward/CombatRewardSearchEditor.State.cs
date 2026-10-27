using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class CombatRewardSearchEditor
{
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.Slots.Any(k => k.HasValue) || d.Potions.Any(p => !p.IsNeutral) || d.Exclusion is not null);
    internal Action ClearConditions(bool party, int? slot = null) =>
        WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft(), slot);

    internal int AuthoredConditionCount()
    {
        int cards = _draft.Slots.Take(_draft.BattleRange).Count(key => key.HasValue);
        int potions = _draft.Potions.Take(_draft.PotionRange).Count(p => !p.IsNeutral);
        return (_draft.Unordered ? cards > 0 ? 1 : 0 : cards) +
            (_draft.PotionsUnordered ? potions > 0 ? 1 : 0 : potions) + (_draft.Exclusion is not null ? 1 : 0);
    }
}
