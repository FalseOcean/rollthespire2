using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// Compact main-thread route selector tile. The stable ModelKey captured by
/// the tile remains the only business identity; visual state never changes
/// tile geometry.
/// </summary>
internal sealed partial class NeowRouteRelicTile : Button
{
    private readonly Label _selectedBadge;

    public NeowRouteRelicTile(
        ModelKey key,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        AnchoredTooltipHost tooltipHost)
    {
        Key = key;
        CustomMinimumSize = new Vector2(50, 50);
        SizeFlagsHorizontal = Control.SizeFlags.Fill;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        FocusMode = Control.FocusModeEnum.All;
        ClipContents = true;

        string displayName = names.Resolve(key, GameContentKind.Relic);
        TooltipText = string.Empty;
        MouseEntered += () => tooltipHost.ShowFor(this, key, displayName);
        MouseExited += () => tooltipHost.Dismiss(this);
        TreeExiting += () => tooltipHost.Dismiss(this);

        var margin = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var iconCenter = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        var iconHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(38, 38),
            ClipContents = true
        };
        IconDescriptor descriptor = icons.Resolve(key, GameContentKind.Relic, IconVariant.RelicLarge);
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
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        iconHost.AddChild(icon);
        iconHost.AddChild(missing);
        iconCenter.AddChild(iconHost);
        margin.AddChild(iconCenter);
        AddChild(margin);

        _selectedBadge = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
        _selectedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _selectedBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _selectedBadge.VerticalAlignment = VerticalAlignment.Center;
        _selectedBadge.CustomMinimumSize = new Vector2(16, 16);
        _selectedBadge.ZIndex = 2;
        _selectedBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _selectedBadge.OffsetLeft = -17f;
        _selectedBadge.OffsetTop = 1f;
        _selectedBadge.OffsetRight = -1f;
        _selectedBadge.OffsetBottom = 17f;
        AddChild(_selectedBadge);

        SetState(selected: false, disabled: false);
    }

    public ModelKey Key { get; }

    public void SetState(bool selected, bool disabled, string? badgeText = null)
    {
        Disabled = disabled;
        _selectedBadge.Text = selected ? (badgeText ?? "✓") : string.Empty;
        Ui1Theme.ApplyButton(this, selected
            ? Ui1ButtonRole.NavigationSelected
            : Ui1ButtonRole.Secondary);
        Modulate = disabled ? new Color(1f, 1f, 1f, 0.52f) : Colors.White;
    }
}
