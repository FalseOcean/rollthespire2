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
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal SearchQuery ExportPartyQuery(int slot, SearchQuery q)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return q;
        var active = _draft;
        try { _draft = draft; return ExportQuery(q); }
        finally { _draft = active; }
    }

    internal SearchQuery ExportQuery(SearchQuery q) => q with
    {
        MerchantRelicConditions = _draft.OrdinaryShelf.Select(key => new MerchantRelicCondition(1, _draft.OrdinaryShopCount, key))
            .Concat(_draft.OtherShopWindows).ToArray(),
        MerchantColorlessSequenceConditions = BuildCardConditions(),
        RelicShopSequenceConditions = BuildRelicConditions()
    };
    internal void ImportQuery(SearchQuery q)
    {
        _draft.OrdinaryShelf.Clear();
        _draft.OtherShopWindows.Clear();
        _draft.OrdinaryShopCount = q.MerchantRelicConditions
            .FirstOrDefault(c => c.FirstMerchant == 1 && c.LastMerchant <= MaximumOrdinaryShops)?.LastMerchant ?? DefaultShopRange;
        foreach (var condition in q.MerchantRelicConditions)
        {
            if (condition.FirstMerchant == 1 && condition.LastMerchant == _draft.OrdinaryShopCount)
                _draft.OrdinaryShelf.Add(condition.TargetRelicKey);
            else
                _draft.OtherShopWindows.Add(condition);
        }
        foreach (RowDraft row in new[] { _draft.Relic, _draft.Uncommon, _draft.Rare })
        {
            Array.Clear(row.Slots); row.ShopRange = DefaultShopRange;
            row.OrderMode = CombatRewardSequenceOrderMode.Unordered; row.RangeProblem = string.Empty;
        }
        foreach (var c in q.MerchantColorlessSequenceConditions)
        {
            var row = c.Slot == MerchantColorlessSlot.Rare ? _draft.Rare : _draft.Uncommon;
            row.ShopRange = c.Count; row.OrderMode = c.OrderMode;
            for (int i = 0; i < c.Count; i++) row.Slots[i] = c.Slots[i];
        }
        foreach (var c in q.RelicShopSequenceConditions)
        {
            _draft.Relic.ShopRange = c.Count; _draft.Relic.OrderMode = c.OrderMode;
            for (int i = 0; i < c.Count; i++) _draft.Relic.Slots[i] = c.Slots[i];
        }
        _draft.Context = "";
    }
}
