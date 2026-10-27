using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Controls.Pickers;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    private void Pick(IReadOnlyList<ModelKey> pool, Action<ModelKey> selected)
    {
        var catalog = _catalog!;
        var cards = pool.Count > 0 && pool.All(key => key.Category == "CARD")
            ? catalog.CreateCardPickerContext(pool, true, "neow-prototype") : null;
        _picker.Open(new(pool, selected, _names, _text, _icons,
            key => key.Category == "RELIC" ? catalog.RelicRarity(key).ToString() : catalog.PotionRarity(key).ToString(),
            cards, catalog.MultiplayerOnlyCards, _players, catalog.Curses));
    }
}
