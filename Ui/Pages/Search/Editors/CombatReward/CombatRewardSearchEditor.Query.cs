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
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal CombatCardRewardSequenceSearchCondition? ExportPartyCondition(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return null;
        var active = _draft;
        try { _draft = draft; return ExportCondition(); }
        finally { _draft = active; }
    }

    internal CombatCardRewardSequenceSearchCondition? ExportCondition()
    {
        if (!_draft.Slots.Any(k => k.HasValue)) return null;
        if (_draft.BattleRange > 6) throw new InvalidOperationException("integration.prototype.c");
        return new(_draft.BattleRange, _draft.Unordered ? CombatRewardSequenceOrderMode.Unordered : CombatRewardSequenceOrderMode.Ordered,
            _draft.Slots.Take(_draft.BattleRange).ToArray());
    }
    internal void ImportQuery(SearchQuery q)
    {
        _draft.Exclusion = q.CombatCardExclusion is { } exclusion ? exclusion with { CardKeys = exclusion.CardKeys.ToArray() } : null;
        _draft.ExclusionBattleCount = q.CombatCardExclusion?.BattleCount ?? DefaultBattleRange;
        _draft.ExcludedCollection = null;
        _draft.CardRangeProblem = _draft.PotionRangeProblem = string.Empty;
        Array.Clear(_draft.Slots);
        _draft.BattleRange = q.CombatCardRewards?.Count ?? 3;
        _draft.Unordered = q.CombatCardRewards?.OrderMode != CombatRewardSequenceOrderMode.Ordered;
        if (q.CombatCardRewards is { } c) for (int i = 0; i < c.Count; i++) _draft.Slots[i] = c.Slots[i];
        Array.Fill(_draft.Potions, new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null));
        _draft.PotionRange = q.CombatPotionRewards?.Count ?? 3;
        _draft.PotionsUnordered = q.CombatPotionRewards?.OrderMode != CombatRewardSequenceOrderMode.Ordered;
        if (q.CombatPotionRewards is { } p) for (int i = 0; i < p.Count; i++) _draft.Potions[i] = p.Slots[i];
        _draft.Context = "";
    }
}
