using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    private void ResultTile(Godot.Control content, float x, float y, EventResultConditionKind kind, int index = 0)
    {
        if (!_draft.Results.TryGetValue(kind, out var slots)) _draft.Results[kind] = slots = new ModelKey?[ResultSlotCount(kind)];
        var key = slots[index];
        var pool = ResultPool(kind);
        var objectKind = kind is EventResultConditionKind.FakeMerchantOfferedFakeRelic or EventResultConditionKind.TrashHeapDiveRelic
            ? GameContentKind.Relic : GameContentKind.Card;
        content.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, key, objectKind,
            () =>
            {
                if (pool.Count == 0) return;
                var cards = objectKind == GameContentKind.Card
                    ? new CardPickerContext(pool, _draft.CardPickerMetadata, true, "neow-prototype") : null;
                Picker?.Open(new(pool, selected => { slots[index] = selected; Render(); },
                    _names, _text, _icons, _ => "Other", cards, _draft.MultiplayerOnlyCards, _queuePickerPlayers));
            },
            () => { slots[index] = null; Render(); }, compact: true, candidateSurface: true) { Position = new(x, y) });
    }
}
