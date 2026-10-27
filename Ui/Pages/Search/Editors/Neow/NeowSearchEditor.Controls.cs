using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    private Button ModeButton(Control parent, string title, float x, float y, float width, Action action, bool active, float height = 40)
    {
        var button = Button(parent, title, x, y, width, action, false, height);
        _p.SetModeActive(button, active);
        return button;
    }

    private Button Button(Control parent, string text, float x, float y, float width, Action click, bool selected = false, float height = 40)
    {
        var b = _p.CompactButton(text, height, height < 32 ? 16 : 18, selected);
        b.Position = new(x, y); b.Size = new(width, height);
        parent.AddChild(b); b.Pressed += click; return b;
    }
    private Label Text(Control p, string text, float x, float y, float width, int size = 20, bool secondary = false)
    { var l = _p.Label(text, size, secondary); l.Position = new(x, y); l.Size = new(width, 28); l.ClipText = true; p.AddChild(l); return l; }
    private void ObjectSlot(Control p, ModelKey? key, string empty, float x, float y, float width, Action pick, Action clear)
    {
        var b = Button(p, key is null ? empty : "", x, y, width - (key.HasValue ? 48 : 0), pick, selected: key.HasValue, height: key.HasValue ? 56 : 40);
        if (key is not { } k) return;
        Picture(b, k, 8, 4, 48); Text(b, NameOf(k), 64, 14, width - 120, 18);
        Button(p, "×", x + width - 40, y + 8, 36, clear);
    }
    private OptionButton Options(Control p, string[] values, int selected, float x, float y, float width, Action<int> change)
    {
        var o = new OptionButton { Position = new(x, y), Size = new(width, 40), FitToLongestItem = false, ClipText = true };
        foreach (string value in values) o.AddItem(value); o.Select(selected);
        o.AddThemeFontSizeOverride("font_size", 17); o.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        o.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface)); o.AddThemeStyleboxOverride("hover", _p.Box(_p.Hover));
        o.AddThemeStyleboxOverride("pressed", _p.Box(_p.Line)); o.AddThemeStyleboxOverride("focus", _p.FocusRing());
        p.AddChild(o); o.ItemSelected += i => change((int)i); return o;
    }
    private void Picture(Control p, ModelKey key, float x, float y, float size)
    {
        GameContentKind kind = Kind(key);
        var texture = _icons.Resolve(key, kind, kind == GameContentKind.Card ? IconVariant.CardPickerLarge : IconVariant.RelicLarge).Texture;
        if (texture is null) return;
        Vector2 dimensions = texture.GetSize();
        p.AddChild(new Sprite2D { Texture = texture, Position = new(x + size / 2, y + size / 2),
            Scale = Vector2.One * (size / Math.Max(1f, Math.Max(dimensions.X, dimensions.Y))) });
    }
}
