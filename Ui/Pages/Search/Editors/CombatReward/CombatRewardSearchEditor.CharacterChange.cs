using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class CombatRewardSearchEditor
{
    internal IReadOnlyList<ModelKey> RemoveUnavailableSelectionsAfterCharacterChange()
    {
        if (!_draft.CardSupportKnown || _catalog is not { CardSupportProofAvailable: true } catalog) return [];
        var known = ModelDb.AllCards.Select(model => new ModelKey(model.Id.Category, model.Id.Entry)).ToHashSet(ModelKeyComparer.Instance);
        var removed = new List<ModelKey>();
        for (int i = 0; i < _draft.Slots.Length; i++)
            if (_draft.Slots[i] is { } key && known.Contains(key) && !catalog.CardCandidates.Contains(key, ModelKeyComparer.Instance))
            { _draft.Slots[i] = null; removed.Add(key); }
        return removed;
    }
}
