using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed partial class BossIconButton : Button
{
    private readonly Label _selectedBadge;

    public BossIconButton(
        ModelKey bossKey,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        string missingIconText)
    {
        BossKey = bossKey;
        CustomMinimumSize = new Vector2(0, BossMapRowGeometry.CellHeight);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        FocusMode = Control.FocusModeEnum.All;
        ClipContents = true;

        string displayName = names.Resolve(bossKey, GameContentKind.Encounter);
        TooltipText = displayName;

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", (int)BossMapRowGeometry.CellInset);
        margin.AddThemeConstantOverride("margin_top", (int)BossMapRowGeometry.CellInset);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_bottom", (int)BossMapRowGeometry.CellInset);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 6);

        var iconHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(BossMapRowGeometry.IconSize, BossMapRowGeometry.IconSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ClipContents = true
        };
        IconDescriptor descriptor = icons.Resolve(
            bossKey,
            GameContentKind.Encounter,
            IconVariant.WorldCompendiumBossIcon);
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

        Label name = Ui1Theme.Label(displayName, Ui1TextRole.Body, true);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.TooltipText = displayName;

        row.AddChild(iconHost);
        row.AddChild(name);
        margin.AddChild(row);
        AddChild(margin);

        _selectedBadge = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
        _selectedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _selectedBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _selectedBadge.VerticalAlignment = VerticalAlignment.Center;
        _selectedBadge.CustomMinimumSize = new Vector2(
            BossMapRowGeometry.SelectedBadgeSize,
            BossMapRowGeometry.SelectedBadgeSize);
        _selectedBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _selectedBadge.OffsetLeft = -18;
        _selectedBadge.OffsetTop = 2;
        _selectedBadge.OffsetRight = -3;
        _selectedBadge.OffsetBottom = 17;
        AddChild(_selectedBadge);
        SetState(false, true);
    }

    public ModelKey BossKey { get; }

    public void SetState(bool selected, bool enabled)
    {
        Disabled = !enabled;
        _selectedBadge.Text = selected ? "✓" : string.Empty;
        Ui1Theme.ApplyButton(this, selected
            ? Ui1ButtonRole.NavigationSelected
            : Ui1ButtonRole.Secondary);
        Modulate = enabled ? Colors.White : new Color(1f, 1f, 1f, 0.52f);
    }
}
