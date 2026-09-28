using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class NeowEditorPrototype
{
    private const float FullCardTileWidth = 184, FullCardTileHeight = 258;
    private const float ResultObjectWidth = WorkspaceResultTile.TileWidth, ResultObjectHeight = WorkspaceResultTile.TileHeight;

    private void ResultContents(Control tile, ModelKey value, NeowStructuredOutputKind kind)
    {
        tile.TooltipText = NameOf(value);
        var name = Text(tile, NameOf(value), 6, 5, ResultObjectWidth - 12, 17);
        name.Size = new(ResultObjectWidth - 12, 26);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MouseFilter = MouseFilterEnum.Ignore;
        tile.AddChild(new TextureRect
        {
            Position = new(6, 36), Size = new(ResultObjectWidth - 12, ResultObjectHeight - 42),
            Texture = _icons.Resolve(value, Kind(value), IconVariant.CardPickerLarge).Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = kind == NeowStructuredOutputKind.Card ? TextureRect.StretchModeEnum.KeepAspectCovered : TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore, ClipContents = true
        });
    }

    private void ResultObject(Control parent, ModelKey? key, NeowStructuredOutputKind kind,
        float x, float y, Action pick, Action clear)
    {
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, key, KindForOutput(kind), pick, clear)
        { Position = new(x, y) };
        parent.AddChild(tile);
    }

    private static GameContentKind KindForOutput(NeowStructuredOutputKind kind) => kind switch
    {
        NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Curse => GameContentKind.Card,
        NeowStructuredOutputKind.Potion => GameContentKind.Potion,
        _ => GameContentKind.Relic
    };

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
