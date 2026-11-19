using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ShopSearchEditor
{
    private bool SupportsOrdinaryRelics => _players == 1 &&
        RuntimeVersionCompatibility.Resolve(_runtime.Detection.DisplayVersion).SemanticProfileId == RngSemanticProfileId.Modern113;

    internal static string DescribeOrdinaryRelic(MerchantRelicCondition condition, IUiTextProvider text,
        IGameContentNameResolver names)
    {
        string shops = condition.FirstMerchant == condition.LastMerchant
            ? text.Format("query.shop.column.shop", condition.FirstMerchant)
            : text.Format("query.shop.ordinary.range", condition.FirstMerchant, condition.LastMerchant);
        return shops + " · " + names.Resolve(condition.TargetRelicKey, GameContentKind.Relic);
    }

    private const int MaximumOrdinaryShops = 3;

    private float RenderOrdinaryRelics(Control content, float top, float width)
    {
        var draft = _draft;
        var targets = draft.OrdinaryShelf;
        int shops = draft.OrdinaryShopCount;
        Text(content, _text.Get("query.shop.ordinary.title"), 16, top + 4, 196, _english ? 18 : 20);
        Text(content, _text.Get("query.shop.ordinary.first_shops"), 212, top + 4, 110, 17, true);
        var decrease = Button(content, "−", 326, top, 38, () => { draft.OrdinaryShopCount--; Render(); }, false, 36);
        decrease.Name = "OrdinaryShelfDecrease";
        decrease.AccessibilityName = _text.Get("query.shop.range.decrease");
        decrease.Disabled = shops == 1 || targets.Count > (shops - 1) * 2;
        var count = Text(content, shops.ToString(), 372, top + 4, 34, 20);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        var increase = Button(content, "+", 418, top, 38, () => { draft.OrdinaryShopCount++; Render(); }, false, 36);
        increase.Name = "OrdinaryShelfIncrease";
        increase.AccessibilityName = _text.Get("query.shop.range.increase");
        increase.Disabled = shops >= MaximumOrdinaryShops;

        top += 48;
        const float tileWidth = WorkspaceResultTile.TileWidth;
        const float stride = tileWidth + 12;
        const float rowHeight = WorkspaceResultTile.CompactHeight + 8;
        int columns = Math.Max(1, (int)((width - 32 + 12) / stride));
        Vector2 At(int index) => new(16 + index % columns * stride, top + index / columns * rowHeight);
        for (int index = 0; index < targets.Count; index++)
        {
            int slot = index;
            var tile = new WorkspaceResultTile(_p, _icons, _names, _text, targets[slot], GameContentKind.Relic,
                () => PickOrdinaryRelic(slot), () => { targets.RemoveAt(slot); Render(); },
                compact: true, compactWidth: tileWidth) { Position = At(slot) };
            content.AddChild(tile);
        }
        int tiles = targets.Count;
        if (tiles < shops * 2)
        {
            var pos = At(tiles++);
            var add = Button(content, _text.Get("query.shop.ordinary.add"), pos.X, pos.Y, tileWidth,
                () => PickOrdinaryRelic(null), false, WorkspaceResultTile.CompactHeight);
            add.Name = "AddOrdinaryShopRelic";
            add.AddThemeFontSizeOverride("font_size", 15);
            add.AddThemeStyleboxOverride("normal", _p.EmptySlotBox());
            add.Disabled = !SupportsOrdinaryRelics || draft.Relics?.OrdinaryShopCandidates.Count is not > 0;
        }
        top += ((tiles + columns - 1) / columns) * rowHeight;

        // Older presets may require a specific visit or a different window for each target.
        foreach (var saved in draft.OtherShopWindows)
        {
            string label = DescribeOrdinaryRelic(saved, _text, _names);
            content.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, saved.TargetRelicKey,
                GameContentKind.Relic, null, () => { draft.OtherShopWindows.Remove(saved); Render(); },
                compact: true, compactWidth: width - 32, displayNameOverride: label) { Position = new(16, top) });
            top += rowHeight;
        }
        return top + 8;
    }

    private void PickOrdinaryRelic(int? slot)
    {
        if (!SupportsOrdinaryRelics || _draft.Relics is not { } catalog) return;
        var targets = _draft.OrdinaryShelf;
        var candidates = catalog.OrdinaryShopCandidates
            .Where(key => !targets.Contains(key) || (slot is int i && targets[i] == key)).ToArray();
        _picker.Open(new(candidates, key =>
        {
            if (slot is int i) targets[i] = key;
            else targets.Add(key);
            Render();
        }, _names, _text, _icons, key => catalog.Categories.GetValueOrDefault(key).ToString()));
    }
}
