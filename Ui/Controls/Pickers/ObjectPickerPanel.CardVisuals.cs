using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Controls.Pickers;

internal sealed partial class ObjectPickerPanel
{
    private const float FullCardTileWidth = 184, FullCardTileHeight = 258;
    private Control AddNativePickerCard(Button tile, ModelKey key)
        => AddNativeCard(tile, key, new(FullCardTileWidth, FullCardTileHeight), .55f);

    private Control AddNativeCard(Control tile, ModelKey key, Vector2 size, float scale)
    {
        // Center the native frame (-150,-211)..(150,211), not the protruding cost gem.
        var crop = new Control
        {
            Size = size,
            MouseFilter = MouseFilterEnum.Ignore
        };
        tile.AddChild(crop);
        var model = ModelDb.GetById<CardModel>(new ModelId(key.Category, key.Entry)).ToMutable();
        // Instantiate the donor scene directly: no dependency on the combat NodePool,
        // no holder/input, no live card model and therefore no "mark as seen" mutation.
        var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        card.Model = model;
        card.Position = size / 2;
        card.Scale = Vector2.One * scale;
        void IgnoreInput(Node node)
        {
            node.SetProcessInput(false); node.SetProcessUnhandledInput(false);
            node.SetProcessUnhandledKeyInput(false);
            if (node is Control control) { control.MouseFilter = MouseFilterEnum.Ignore; control.FocusMode = FocusModeEnum.None; }
            foreach (var child in node.GetChildren()) IgnoreInput(child);
        }
        card.Ready += () =>
        {
            card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            card.GetNode<Control>("%Highlight").Hide();
            IgnoreInput(card);
        };
        crop.AddChild(card);
        return crop;
    }
}
