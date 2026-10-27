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
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal bool AdvancedOpen { get => _draft.Builder.Advanced; set => _draft.Builder.Advanced = value; }

    internal bool HasConditions(bool party) => (party ? _partyDrafts : _drafts).Values.Any(d => d.Conditions.Count > 0 || d.TreasureSlots.Any(k => k.HasValue));
    internal Action ClearConditions(bool party, int? slot = null) =>
        WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft(), slot);

    internal int AuthoredConditionCount() => _draft.Conditions.Count + _draft.TreasureSlots.Take(_draft.TreasureCount).Count(k => k.HasValue);
}
