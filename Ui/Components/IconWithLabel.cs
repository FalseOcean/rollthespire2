using Godot;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class IconWithLabel : HBoxContainer
{
    private readonly PanelContainer _iconSurface;
    private readonly TextureRect _texture;
    private readonly Label _missing;
    private readonly Label _name;

    public IconWithLabel(float iconSize, Ui1TextRole nameRole)
    {
        Alignment = AlignmentMode.Center;
        AddThemeConstantOverride("separation", 10);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _iconSurface = new PanelContainer { CustomMinimumSize = new Vector2(iconSize, iconSize) };
        Ui1Theme.ApplyPanel(_iconSurface, Ui1SurfaceRole.Input, 3f, 1);
        _texture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _texture.OffsetLeft = 4;
        _texture.OffsetTop = 4;
        _texture.OffsetRight = -4;
        _texture.OffsetBottom = -4;
        _missing = Ui1Theme.Label("?", Ui1TextRole.SectionTitle);
        _missing.HorizontalAlignment = HorizontalAlignment.Center;
        _missing.VerticalAlignment = VerticalAlignment.Center;
        _missing.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _iconSurface.AddChild(_texture);
        _iconSurface.AddChild(_missing);

        // Game object names are atomic presentation units. Do not let Chinese
        // character-level wrapping split card/relic/potion/curse names simply because
        // an old dense result item advertised an 88px minimum width. With wrapping off,
        // the label contributes its natural text width and the surrounding Flow/Grid
        // decides whether the whole result item moves as a unit.
        _name = Ui1Theme.Label(string.Empty, nameRole, wrap: false);
        _name.HorizontalAlignment = HorizontalAlignment.Left;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _name.CustomMinimumSize = new Vector2(0f, iconSize);
        _name.ClipText = false;
        _name.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_iconSurface);
        AddChild(_name);
    }

    public Control IconAnchor => _iconSurface;

    public void Bind(IconDescriptor descriptor, string displayName, string tooltip, string missingTooltip)
    {
        _texture.Texture = descriptor.Texture;
        _texture.Visible = !descriptor.IsMissing && descriptor.Texture is not null;
        _missing.Visible = !_texture.Visible;
        _name.Text = displayName;
        TooltipText = descriptor.IsMissing ? $"{displayName}\n{missingTooltip}" : tooltip;
    }

    public void UseClippedName()
    {
        _name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }
}
