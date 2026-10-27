using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private IGameIconResolver? _icons;
    private IGameIconResolver Icons => _icons ??= new ReflectionGameIconResolver("neow-operation-overlay");
    private static readonly Color Ink = new("ece9df");
    private static readonly Color MutedInk = new("b5c4d9");
    private static readonly Color GoldInk = new("e9cf8d");

    private static Label Text(string value, int size = 20, bool heading = false)
    {
        var label = new Label { Text = value, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", heading ? MainPanelTheme.Medium : MainPanelTheme.Regular);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", heading ? GoldInk : Ink);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .65f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    private static Label WrappedText(string value, bool heading = false)
    {
        var label = Text(value, 18, heading);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private HBoxContainer Identity(ModelKey key, string name, int iconSize = 38, bool heading = false)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 9);
        var kind = key.Category switch { "POTION" => GameContentKind.Potion, "CARD" => GameContentKind.Card, _ => GameContentKind.Relic };
        var texture = Icons.Resolve(key, kind, IconVariant.RelicLarge).Texture;
        if (texture != null)
            row.AddChild(new TextureRect { Texture = texture, CustomMinimumSize = new Vector2(iconSize, iconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore });
        var label = Text(name, heading ? 22 : 20, heading);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // Flow containers need a meaningful item width before wrapping; a
        // zero-width wrapped label would turn each relic name into a column.
        label.CustomMinimumSize = new(Math.Min(320, label.GetThemeFont("font")
            .GetStringSize(name, fontSize: heading ? 22 : 20).X), 0);
        if (MapCardPresentation) _mapSummaryNames.Add((row, label, label.CustomMinimumSize.X));
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(label);
        return row;
    }

    private MarginContainer Surface(Control content, float minWidth = 0)
    {
        var shell = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(minWidth, 0), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        if (!ExternallyFramed)
        {
            var shadow = NativeNightSurface.Backdrop(shadow: true);
            var background = NativeNightSurface.Backdrop();
            shell.AddChild(shadow); shell.AddChild(background);
            shell.Resized += () => { shadow.Position = new Vector2(5, 7); shadow.Size = shell.Size; };
        }
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 19); margin.AddThemeConstantOverride("margin_right", 23);
        margin.AddThemeConstantOverride("margin_top", 14); margin.AddThemeConstantOverride("margin_bottom", 17);
        margin.AddChild(content); shell.AddChild(margin);
        return shell;
    }

    private static VBoxContainer Column(int gap = 8)
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", gap);
        return column;
    }

    private static HBoxContainer PriceDisplay(int price)
    {
        var row = new HBoxContainer { Name = "Rt2OperationPrice", MouseFilter = MouseFilterEnum.Ignore };
        row.SetMeta("rt2_price", price);
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres"),
            CustomMinimumSize = new(28, 28), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });
        var amount = Text(price.ToString(System.Globalization.CultureInfo.InvariantCulture), 24, true);
        amount.AddThemeColorOverride("font_color", new Color("f4d477"));
        row.AddChild(amount);
        return row;
    }
}
