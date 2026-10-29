using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ShopSearchEditor
{
    internal IReadOnlyList<ModelKey> RemoveUnavailableSelectionsAfterCharacterChange()
    {
        var removed = new List<ModelKey>();
        var known = ModelDb.AllCards.Cast<AbstractModel>().Concat(ModelDb.AllRelics)
            .Select(model => new ModelKey(model.Id.Category, model.Id.Entry)).ToHashSet(ModelKeyComparer.Instance);
        void Clear(RowDraft row, IReadOnlyList<ModelKey> pool)
        {
            for (int i = 0; i < row.Slots.Length; i++)
                if (row.Slots[i] is { } key && known.Contains(key) && !pool.Contains(key, ModelKeyComparer.Instance))
                { row.Slots[i] = null; removed.Add(key); }
        }
        if (_draft.Cards is { AuthorityExact: true } cards)
        { Clear(_draft.Uncommon, cards.UncommonCandidates); Clear(_draft.Rare, cards.RareCandidates); }
        if (_draft.Relics is { LaneMappingExact: true } relics) Clear(_draft.Relic, relics.CandidatesFor(RelicSequenceKind.Shop));
        return removed;
    }
}
