using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal sealed partial class AncientIdentityButton : Button
{
    private readonly Label _selectedBadge;
    private bool _selected;
    private bool _expanded;

    public AncientIdentityButton(
        ModelKey key,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        string missingIconText)
    {
        Key = key;
        CustomMinimumSize = new Vector2(0, AncientRowGeometry.HeaderHeight);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        FocusMode = Control.FocusModeEnum.All;
        ClipContents = true;

        string displayName = names.Resolve(key, GameContentKind.Ancient);
        TooltipText = displayName;

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_top", 3);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_bottom", 3);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 7);

        var iconHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(AncientRowGeometry.PortraitSize, AncientRowGeometry.PortraitSize),
            ClipContents = true
        };
        IconDescriptor descriptor = icons.Resolve(
            key,
            GameContentKind.Ancient,
            IconVariant.WorldCompendiumAncientIcon);
        var icon = new TextureRect
        {
            Texture = descriptor.Texture,
            Visible = descriptor.Texture is not null,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var missing = Ui1Theme.Label(descriptor.Texture is null ? "?" : string.Empty, Ui1TextRole.Muted);
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.TooltipText = descriptor.Texture is null ? missingIconText : string.Empty;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        iconHost.AddChild(icon);
        iconHost.AddChild(missing);

        Label name = Ui1Theme.Label(displayName, Ui1TextRole.CardTitle, true);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(iconHost);
        row.AddChild(name);
        margin.AddChild(row);
        AddChild(margin);

        _selectedBadge = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
        _selectedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _selectedBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _selectedBadge.VerticalAlignment = VerticalAlignment.Center;
        _selectedBadge.CustomMinimumSize = new Vector2(AncientRowGeometry.SelectedBadgeSize, AncientRowGeometry.SelectedBadgeSize);
        _selectedBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _selectedBadge.OffsetLeft = -18;
        _selectedBadge.OffsetTop = 2;
        _selectedBadge.OffsetRight = -3;
        _selectedBadge.OffsetBottom = 17;
        AddChild(_selectedBadge);
        SetState(false, true);
    }

    public ModelKey Key { get; }

    public void SetState(bool selected, bool enabled)
    {
        _selected = selected;
        Disabled = !enabled;
        _selectedBadge.Text = selected ? "✓" : string.Empty;
        RefreshStyle();
        Modulate = enabled ? Colors.White : new Color(1f, 1f, 1f, 0.52f);
    }

    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        RefreshStyle();
    }

    private void RefreshStyle() => Ui1Theme.ApplyButton(
        this,
        _expanded
            ? Ui1ButtonRole.Secondary
            : _selected
                ? Ui1ButtonRole.NavigationSelected
                : Ui1ButtonRole.Ghost);
}
