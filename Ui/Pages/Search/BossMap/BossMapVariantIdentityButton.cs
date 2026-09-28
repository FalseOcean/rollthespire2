using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed partial class BossMapVariantIdentityButton : Button
{
    private readonly Label _selectedBadge;

    public BossMapVariantIdentityButton(
        ModelKey actKey,
        IGameContentNameResolver names)
    {
        ActKey = actKey;
        CustomMinimumSize = new Vector2(BossMapRowGeometry.VariantWidth, BossMapRowGeometry.CellHeight);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        FocusMode = Control.FocusModeEnum.All;
        ClipContents = true;

        string displayName = names.Resolve(actKey, GameContentKind.Act);
        TooltipText = displayName;

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", (int)BossMapRowGeometry.CellInset);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", (int)BossMapRowGeometry.CellInset);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        Label name = Ui1Theme.Label(displayName, Ui1TextRole.Body, true);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        margin.AddChild(name);
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

    public ModelKey ActKey { get; }

    // Compatibility shim for overlay upgrades where the obsolete
    // BossMapReadOnlyVariantHeader.cs file is still present. The current
    // compact matrix does not call this method. Keep it icon-free so the
    // retired header cannot reintroduce the old map-icon presentation.
    internal static Control BuildIdentityContent(
        ModelKey actKey,
        string displayName,
        IGameIconResolver icons,
        string missingIconText)
    {
        _ = actKey;
        _ = icons;
        _ = missingIconText;

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", (int)BossMapRowGeometry.CellInset);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", (int)BossMapRowGeometry.CellInset);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        Label name = Ui1Theme.Label(displayName, Ui1TextRole.Body, true);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        margin.AddChild(name);
        return margin;
    }

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
