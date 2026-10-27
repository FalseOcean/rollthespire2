using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Event;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    private Button CreateTile(AbstractModel model, string title, bool selected)
    {
        bool compact = _viewMode.Selected == 1;
        bool card = model is CardModel;
        bool ancient = model is AncientEventModel;
        bool heldCard = card && _holdings.ButtonPressed && !compact;
        bool eventTile = model is EventModel and not AncientEventModel;
        float width = compact ? card ? 112 : 64 : card ? 136 : ancient ? 80 : 132;
        float height = compact ? card ? 76 : 64 : heldCard ? 216 : card ? 142 : ancient ? 96 : eventTile ? 188 : 126;
        var tile = _p.Button(""); tile.CustomMinimumSize = new(width, height); tile.ClipContents = true;
        tile.AccessibilityName = title; tile.Disabled = _busy;
        _p.ApplyButton(tile, selected: selected); _p.ApplyCandidateSurface(tile, selected);
        float imageTop = compact ? 5 : ancient ? 38 : card || eventTile ? 60 : 46;
        Vector2 imageSize = new(width - (compact ? 10 : 12), compact ? height - 10 : card ? 76 : height - imageTop - 6);
        if (eventTile)
        {
            float square = Math.Min(imageSize.X, imageSize.Y);
            tile.AddChild(new EventThumbnailView(_eventThumbnails.Resolve(KeyOf(model)), new(square, square),
                EventThumbnailPresentation.PickerSquareCrop) { Position = new((width - square) / 2, imageTop) });
        }
        else if (TileTexture(model) is { } texture)
            tile.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = texture, Position = new(compact ? 5 : 6, imageTop),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore, Size = imageSize });
        else
        {
            var missing = _p.Label("?", 22, true); missing.Position = new(5, imageTop);
            missing.HorizontalAlignment = HorizontalAlignment.Center; missing.VerticalAlignment = VerticalAlignment.Center; missing.Size = imageSize; tile.AddChild(missing);
        }
        if (!compact)
        {
            var label = _p.Label((selected ? "✓ " : "") + title, ancient ? 14 : 16); label.Position = new(5, 5);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.ClipText = true; label.HorizontalAlignment = HorizontalAlignment.Center;
            if (card || eventTile) label.MaxLinesVisible = 2;
            Vector2 titleSize = new(width - 10, ancient ? 30 : card || eventTile ? 52 : 40); label.Size = titleSize; tile.AddChild(label);
            // The inherited font can briefly clamp Size to the unwrapped minimum
            // before the deferred wrapping minimum becomes 1px. Reapply after the
            // tile enters the themed tree, when that minimum has settled.
            tile.Ready += () => Callable.From(() => { if (IsInstanceValid(label)) label.Size = titleSize; }).CallDeferred();
        }
        else if (selected)
        {
            var badge = _p.Label("✓", 19); badge.Position = new(width - 24, 0); badge.Size = new(24, 24);
            badge.AddThemeColorOverride("font_color", _p.Color(_p.Selected)); tile.AddChild(badge);
        }
        if (heldCard)
        {
            var detail = _p.Label(CardInstanceDetails((CardModel)model), 15, true);
            detail.Name = "ConsoleCardInstanceDetails"; detail.Position = new(6, 138);
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart; detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; detail.ClipText = true;
            Vector2 detailSize = new(width - 12, 72); detail.Size = detailSize; tile.AddChild(detail);
            tile.Ready += () => Callable.From(() => { if (IsInstanceValid(detail)) detail.Size = detailSize; }).CallDeferred();
        }
        BindContentTooltip(tile, model, title);
        _grid.AddChild(tile); return tile;
    }

    private Texture2D? TileTexture(AbstractModel model)
    {
        if (model is EncounterModel encounter && encounter.RoomType != RoomType.Boss)
        {
            string? path = ImageHelper.GetRoomIconPath(encounter.RoomType == RoomType.Elite ? MapPointType.Elite : MapPointType.Monster, encounter.RoomType, null);
            return path is null ? null : PreloadManager.Cache.GetCompressedTexture2D(path);
        }
        var variant = Kind switch { GameContentKind.Card => IconVariant.CardPickerLarge,
            GameContentKind.Ancient => IconVariant.WorldCompendiumAncientIcon, GameContentKind.Encounter => IconVariant.WorldCompendiumBossIcon, _ => IconVariant.RelicLarge };
        return _icons.Resolve(KeyOf(model), Kind, variant).Texture;
    }
}
