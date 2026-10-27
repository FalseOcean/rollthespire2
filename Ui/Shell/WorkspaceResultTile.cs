using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Shared 1.3 object-result tile used by N and C authoring surfaces.</summary>
internal sealed partial class WorkspaceResultTile : Control
{
    public const float TileWidth = 120, TileHeight = 128;
    public const float CompactWidth = 160, CompactHeight = 64;

    public WorkspaceResultTile(WorkspacePalette palette, IGameIconResolver icons,
        IGameContentNameResolver names, IUiTextProvider text, ModelKey? key,
        GameContentKind kind, Action? choose, Action clear, bool allowClear = true, string? displayNameOverride = null,
        bool isRequested = true, bool compact = false, float compactWidth = CompactWidth, bool candidateSurface = false)
    {
        float width = compact ? compactWidth : TileWidth;
        float height = compact ? CompactHeight : TileHeight;
        Size = new(width, height);
        CustomMinimumSize = Size;
        MouseFilter = MouseFilterEnum.Pass;

        Control tile;
        if (choose is null)
        {
            // A committed read-only target still has its own delete action,
            // but must not pretend the object can be reopened in a picker.
            var display = new Panel { MouseFilter = MouseFilterEnum.Pass,
                FocusMode = allowClear ? FocusModeEnum.All : FocusModeEnum.None };
            display.AddThemeStyleboxOverride("panel", palette.Box(
                isRequested ? palette.SelectedSurface : palette.OptionSurface,
                isRequested ? palette.Selected : palette.Line, isRequested ? 2 : 1));
            tile = display;
        }
        else
        {
            var button = palette.Button(key.HasValue ? string.Empty : "+", selected: key.HasValue && isRequested);
            if (candidateSurface) palette.ApplyCandidateSurface(button, key.HasValue && isRequested);
            button.Pressed += choose;
            tile = button;
        }
        tile.Size = Size;
        tile.CustomMinimumSize = Size;
        tile.ClipContents = true;
        tile.AccessibilityName = displayNameOverride ?? (key is { } named ? names.Resolve(named, kind) : text.Get("picker.any_choose_result"));
        AddChild(tile);

        if (key is not { } value)
        {
            tile.AddThemeStyleboxOverride("normal", candidateSurface ? palette.Box(palette.CandidateSurface) : palette.EmptySlotBox());
            return;
        }

        string displayName = displayNameOverride ?? names.Resolve(value, kind);
        SearchTooltip.SetIfScoped(tile, displayName);
        var name = palette.Label(displayName, 15);
        if (palette.IsMainPanel) MainPanelTheme.SetColor(name, "font_color", isRequested ? palette.Selected : palette.Text);
        name.Position = compact ? new(44, 8) : new(8, 82);
        name.Size = compact ? new(width - 52, 48) : new(width - 16, 40);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.MaxLinesVisible = 2;
        name.ClipText = false;
        // Label initially measures its unwrapped text; apply the authored bounds
        // after wrapping/clipping has removed that minimum-width constraint.
        name.Size = compact ? new(width - 52, 48) : new(width - 16, 40);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        name.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        name.MouseFilter = MouseFilterEnum.Ignore;
        // Godot defers the wrapped label's minimum-size update until it enters
        // the themed tree. Apply bounds after that update, not against its old
        // unwrapped width.
        name.Ready += () => name.SetDeferred(Control.PropertyName.Size,
            compact ? new Vector2(width - 52, 48) : new Vector2(width - 16, 40));
        tile.AddChild(name);

        var artwork = new TextureRect
        {
            Position = compact ? new(8, 16) : new((width - 64) / 2, 10),
            Size = compact ? new(32, 32) : new(64, 64),
            Texture = icons.Resolve(value, kind, kind == GameContentKind.Card ? IconVariant.CardPickerLarge : IconVariant.RelicLarge).Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true
        };
        tile.AddChild(artwork);

        if (!allowClear) return;
        var removeHost = new Control
        {
            Position = new(width - 22, 2),
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
        foreach (var (state, fill, border, borderWidth) in new[]
        {
            ("normal", palette.Surface, palette.Line, 0),
            ("hover", palette.Hover, palette.Line, 1),
            ("pressed", palette.Line, palette.Line, 0),
            ("disabled", palette.Canvas, palette.Line, 0)
        })
        {
            var style = palette.Box(fill, border, borderWidth);
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
