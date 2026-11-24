using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Infrastructure.ContentNames;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private void ChangeCharacter(ModelKey character)
    {
        if (character == (_multiplayer ? _seatCharacters[_seat] : _soloCharacter)) return;
        if (_multiplayer) _seatCharacters[_seat] = character;
        else _soloCharacter = character;
        UpdateSeatReadLabels();
        // Explicit user edit only. Restore, catalog refresh and seat navigation
        // retain authored choices and never acquire deletion authority.
        if (!RefreshEditorContexts(render: false))
        {
            UpdateDraftStatus();
            return;
        }
        var removed = new List<ModelKey>();
        removed.AddRange(_neowEditor.RemoveUnavailableSelectionsAfterCharacterChange());
        removed.AddRange(_combatEditor.RemoveUnavailableSelectionsAfterCharacterChange());
        removed.AddRange(_relicEditor.RemoveUnavailableSelectionsAfterCharacterChange());
        removed.AddRange(_shopEditor.RemoveUnavailableSelectionsAfterCharacterChange());
        removed.AddRange(_ancientEditor.RemoveUnavailableSelectionsAfterCharacterChange());
        if (!_multiplayer && _neowEditor.TryCharacterChangeTransformationCandidates(0, out var candidates))
            removed.AddRange(_transformationEditor.RemoveUnavailableNeowTargetsAfterCharacterChange(candidates));
        RefreshEditorContexts();
        UpdateDraftStatus();
        if (removed.Count > 0)
        {
            var names = RuntimeGameContentNameResolver.Create(_language);
            string Describe(ModelKey key) => names.Resolve(key, key.Category switch
            {
                "CARD" => GameContentKind.Card, "POTION" => GameContentKind.Potion,
                "CHARACTER" => GameContentKind.Character, _ => GameContentKind.Relic
            });
            ReceiptText(_text.Format("query.context.character_removed", string.Join(" · ",
                removed.Distinct(ModelKeyComparer.Instance).Select(Describe))));
        }
    }
}
