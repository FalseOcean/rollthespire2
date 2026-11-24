using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class AncientSearchEditor
{
    internal IReadOnlyList<ModelKey> RemoveUnavailableSelectionsAfterCharacterChange()
    {
        if (_draft.Catalog is not { CharacterTargetAuthorityExact: true } catalog) return [];
        var known = ModelDb.AllCharacters.Select(model => new ModelKey(model.Id.Category, model.Id.Entry)).ToHashSet(ModelKeyComparer.Instance);
        var removed = new List<ModelKey>();
        foreach (AncientDraft row in _draft.Modes.SelectMany(mode => mode.Acts.Values).SelectMany(rows => rows))
            if (row.SeaGlassTarget is { } target && known.Contains(target) && !catalog.SeaGlassTargetCharacters.Contains(target, ModelKeyComparer.Instance))
            { row.SeaGlassTarget = null; removed.Add(target); }
        return removed;
    }
}
