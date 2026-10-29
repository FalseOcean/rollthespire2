using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class RelicSequenceSearchEditor
{
    internal IReadOnlyList<ModelKey> RemoveUnavailableSelectionsAfterCharacterChange()
    {
        if (_draft.Catalog is not { LaneMappingExact: true } catalog) return [];
        var known = ModelDb.AllRelics.Select(model => new ModelKey(model.Id.Category, model.Id.Entry)).ToHashSet(ModelKeyComparer.Instance);
        var removed = new List<ModelKey>();
        foreach (var condition in _draft.Conditions.Where(condition => condition.MatchMode == RelicSequenceUiMatchMode.Appears &&
            known.Contains(condition.RelicKey) && !catalog.CandidatesFor(condition.Lane).Contains(condition.RelicKey, ModelKeyComparer.Instance)).ToArray())
        { _draft.Conditions.Remove(condition); removed.Add(condition.RelicKey); }
        // Treasure outcomes also allow already acquired opening relics. Only the
        // current complete ordinary catalog plus N can establish their reachability;
        // this editor's queue catalog alone does not own that negative proof.
        return removed;
    }
}
