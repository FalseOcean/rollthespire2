using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Shared 1.3 object-result tile used by N and C authoring surfaces.</summary>
internal sealed partial class WorkspaceResultTile : Control
{
    public const float TileWidth = 120, TileHeight = 128;

    public WorkspaceResultTile(WorkspacePalette palette, IGameIconResolver icons,
        IGameContentNameResolver names, IUiTextProvider text, ModelKey? key,
        GameContentKind kind, Action choose, Action clear)
    {
        Size = new(TileWidth, TileHeight);
        CustomMinimumSize = Size;
        MouseFilter = MouseFilterEnum.Pass;

        var tile = palette.Button(key.HasValue ? string.Empty : "+");
        tile.Size = Size;
        tile.CustomMinimumSize = Size;
        tile.AccessibilityName = key is { } named ? names.Resolve(named, kind) : text.Get("picker.any_choose_result");
        tile.Pressed += choose;
        AddChild(tile);

        if (key is not { } value)
        {
            tile.AddThemeStyleboxOverride("normal", palette.Box(palette.Surface, palette.Line, 1));
            return;
        }

        string displayName = names.Resolve(value, kind);
        tile.TooltipText = displayName;
        var name = palette.Label(displayName, 16);
        name.Position = new(6, 5);
        name.Size = new(TileWidth - 12, 26);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MouseFilter = MouseFilterEnum.Ignore;
        tile.AddChild(name);

        var artwork = new TextureRect
        {
            Position = new(6, 36),
            Size = new(TileWidth - 12, TileHeight - 42),
            Texture = icons.Resolve(value, kind, kind == GameContentKind.Card ? IconVariant.CardPickerLarge : IconVariant.RelicLarge).Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = kind == GameContentKind.Card ? TextureRect.StretchModeEnum.KeepAspectCovered : TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true
        };
        tile.AddChild(artwork);

        var removeHost = new Control
        {
            Position = new(TileWidth - 22, 2),
            Size = new(20, 20),
            CustomMinimumSize = new(20, 20),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Pass
        };
        AddChild(removeHost);
        var remove = new Button
        {
            Text = "×",
            Position = Vector2.Zero,
            Size = new(20, 20),
            CustomMinimumSize = new(20, 20),
            ClipText = true
        };
        remove.AddThemeFontSizeOverride("font_size", 14);
        foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            remove.AddThemeColorOverride(color, palette.Color(palette.Text));
        remove.AddThemeColorOverride("font_disabled_color", palette.Color(palette.Disabled));
        remove.AccessibilityName = text.Format("common.remove_named", displayName);
        remove.Pressed += clear;
        foreach (var (state, fill, border, width) in new[]
        {
            ("normal", palette.Surface, palette.Line, 0),
            ("hover", palette.Hover, palette.Line, 1),
            ("pressed", palette.Line, palette.Line, 0),
            ("disabled", palette.Canvas, palette.Line, 0)
        })
        {
            var style = palette.Box(fill, border, width);
            style.ContentMarginLeft = style.ContentMarginRight = 0;
            style.ContentMarginTop = style.ContentMarginBottom = 0;
            remove.AddThemeStyleboxOverride(state, style);
        }
        remove.AddThemeStyleboxOverride("focus", palette.FocusRing());
        removeHost.AddChild(remove);
        remove.CustomMinimumSize = new(20, 20);
        remove.Size = new(20, 20);
        removeHost.Hide();

        void UpdateRemove()
        {
            if (!GodotObject.IsInstanceValid(tile) || tile.IsQueuedForDeletion()) return;
            removeHost.Visible = tile.GetGlobalRect().HasPoint(tile.GetGlobalMousePosition()) || tile.HasFocus() || remove.HasFocus();
        }
        void DeferUpdate() => Callable.From(UpdateRemove).CallDeferred();
        tile.MouseEntered += UpdateRemove;
        tile.MouseExited += DeferUpdate;
        tile.FocusEntered += UpdateRemove;
        tile.FocusExited += DeferUpdate;
        remove.MouseExited += DeferUpdate;
        remove.FocusExited += DeferUpdate;
    }
}
