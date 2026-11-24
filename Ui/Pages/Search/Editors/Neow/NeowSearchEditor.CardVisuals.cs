using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    private const float ResultObjectWidth = WorkspaceResultTile.TileWidth, ResultObjectHeight = WorkspaceResultTile.CompactHeight;

    private void ResultContents(Control tile, ModelKey value, NeowStructuredOutputKind kind)
    {
        SearchTooltip.Set(tile, NameOf(value));
        var name = Text(tile, NameOf(value), 44, 8, ResultObjectWidth - 50, 14);
        name.Size = new(ResultObjectWidth - 50, 48);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.MouseFilter = MouseFilterEnum.Ignore;
        tile.AddChild(new TextureRect
        {
            Position = new(8, 16), Size = new(32, 32),
            Texture = _icons.Resolve(value, Kind(value), IconVariant.CardPickerLarge).Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = kind == NeowStructuredOutputKind.Card ? TextureRect.StretchModeEnum.KeepAspectCovered : TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore, ClipContents = true
        });
    }

    private void ResultObject(Control parent, ModelKey? key, NeowStructuredOutputKind kind,
        float x, float y, Action pick, Action clear)
    {
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, key, KindForOutput(kind), pick, clear, compact: true, compactWidth: ResultObjectWidth)
        { Position = new(x, y) };
        parent.AddChild(tile);
    }

    private static GameContentKind KindForOutput(NeowStructuredOutputKind kind) => kind switch
    {
        NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Curse => GameContentKind.Card,
        NeowStructuredOutputKind.Potion => GameContentKind.Potion,
        _ => GameContentKind.Relic
    };

}
