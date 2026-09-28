using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Controls.Pickers;

/// <summary>
/// Shared ModelKey picker tile. Relics and potions use compact icon-only tiles;
/// cards use a larger portrait-and-name tile with the shared anchored tooltip.
/// </summary>
internal sealed partial class RelicPickerTile : Button
{
    private const float CompactTileSize = 54f;
    private const float CompactIconSize = 42f;
    internal const float CardTileMinWidth = 150f;
    internal const float CardTileMaxWidth = 158f;
    internal const float CardTileHeight = 164f;
    internal const float CardArtMaxWidth = 146f;
    internal const float CardArtHeight = 106f;

    public RelicPickerTile(
        RelicPickerCandidate candidate,
        GameContentKind contentKind,
        AnchoredTooltipHost tooltipHost,
        float cardTileWidth = CardTileMaxWidth)
    {
        FocusMode = Control.FocusModeEnum.All;
        ClipContents = true;
        Disabled = candidate.IsDisabled;
        Ui1Theme.ApplyButton(this, candidate.IsSelected
            ? Ui1ButtonRole.NavigationSelected
            : Ui1ButtonRole.Secondary);
        Modulate = candidate.IsDisabled
            ? new Color(1f, 1f, 1f, 0.52f)
            : Colors.White;

        if (contentKind is GameContentKind.Relic or GameContentKind.Potion)
        {
            BuildCompact(candidate, contentKind, tooltipHost);
        }
        else if (contentKind == GameContentKind.Card)
        {
            BuildCard(candidate, tooltipHost, cardTileWidth);
        }
        else
        {
            BuildFallback(candidate);
        }
    }

    private void BuildCompact(
        RelicPickerCandidate candidate,
        GameContentKind contentKind,
        AnchoredTooltipHost tooltipHost)
    {
        CustomMinimumSize = new Vector2(CompactTileSize, CompactTileSize);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        TooltipText = string.Empty;

        MouseEntered += () => tooltipHost.ShowFor(this, candidate.ModelKey, contentKind, candidate.LocalizedName);
        MouseExited += () => tooltipHost.Dismiss(this);
        TreeExiting += () => tooltipHost.Dismiss(this);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 5);
        margin.AddThemeConstantOverride("margin_top", 5);
        margin.AddThemeConstantOverride("margin_right", 5);
        margin.AddThemeConstantOverride("margin_bottom", 5);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var iconCenter = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        iconCenter.AddChild(BuildIconHost(candidate.Texture, CompactIconSize));
        margin.AddChild(iconCenter);
        AddChild(margin);
        AddSelectedBadge(candidate.IsSelected);
    }

    private void BuildCard(
        RelicPickerCandidate candidate,
        AnchoredTooltipHost tooltipHost,
        float requestedTileWidth)
    {
        float tileWidth = Math.Clamp(requestedTileWidth, CardTileMinWidth, CardTileMaxWidth);
        CustomMinimumSize = new Vector2(tileWidth, CardTileHeight);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        TooltipText = string.Empty;

        MouseEntered += () => tooltipHost.ShowCardFor(
            this,
            candidate.ModelKey,
            candidate.LocalizedName);
        MouseExited += () => tooltipHost.Dismiss(this);
        TreeExiting += () => tooltipHost.Dismiss(this);

        var margin = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 5);
        margin.AddThemeConstantOverride("margin_top", 5);
        margin.AddThemeConstantOverride("margin_right", 5);
        margin.AddThemeConstantOverride("margin_bottom", 5);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var content = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        content.Alignment = BoxContainer.AlignmentMode.Center;
        content.AddThemeConstantOverride("separation", 4);

        var name = Ui1Theme.Label(candidate.LocalizedName, Ui1TextRole.Body);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.CustomMinimumSize = new Vector2(0f, 36f);
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        content.AddChild(name);

        var artCenter = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0f, CardArtHeight)
        };
        float artWidth = Math.Min(CardArtMaxWidth, tileWidth - 10f);
        artCenter.AddChild(BuildCardArtHost(candidate.Texture, artWidth, CardArtHeight));
        content.AddChild(artCenter);

        margin.AddChild(content);
        AddChild(margin);
        AddSelectedBadge(candidate.IsSelected);
    }

    private void BuildFallback(RelicPickerCandidate candidate)
    {
        CustomMinimumSize = new Vector2(142, 126);
        TooltipText = candidate.LocalizedName;
        var label = Ui1Theme.Label(candidate.LocalizedName, Ui1TextRole.Body, true);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(label);
        AddSelectedBadge(candidate.IsSelected);
    }

    private static Control BuildCardArtHost(Texture2D? texture, float width, float height)
    {
        var host = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(width, height),
            ClipContents = true
        };
        var image = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        bool textureAssigned = TryAssignTexture(image, texture);
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var missing = Ui1Theme.Label(textureAssigned ? string.Empty : "?", Ui1TextRole.Muted);
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(image);
        host.AddChild(missing);
        return host;
    }

    private static Control BuildIconHost(Texture2D? texture, float size)
    {
        var host = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(size, size),
            ClipContents = true
        };
        var image = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        bool textureAssigned = TryAssignTexture(image, texture);
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var missing = Ui1Theme.Label(textureAssigned ? string.Empty : "?", Ui1TextRole.Muted);
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(image);
        host.AddChild(missing);
        return host;
    }

    private static bool TryAssignTexture(TextureRect target, Texture2D? texture)
    {
        if (texture is null)
        {
            return false;
        }

        try
        {
            if (!GodotObject.IsInstanceValid(texture))
            {
                return false;
            }

            target.Texture = texture;
            target.Visible = true;
            return true;
        }
        catch (ObjectDisposedException)
        {
            target.Texture = null;
            target.Visible = false;
            return false;
        }
    }

    private void AddSelectedBadge(bool selected)
    {
        var badge = Ui1Theme.Label(selected ? "✓" : string.Empty, Ui1TextRole.Accent);
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.CustomMinimumSize = new Vector2(18, 18);
        badge.ZIndex = 2;
        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.OffsetLeft = -20f;
        badge.OffsetTop = 2f;
        badge.OffsetRight = -2f;
        badge.OffsetBottom = 20f;
        AddChild(badge);
    }
}
