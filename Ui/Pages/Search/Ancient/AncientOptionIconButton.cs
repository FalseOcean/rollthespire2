using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal sealed partial class AncientOptionIconButton : Button
{
    private readonly Label _selectedBadge;

    public AncientOptionIconButton(
        AncientOptionCandidate candidate,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        AnchoredTooltipHost tooltipHost,
        string missingIconText)
    {
        Candidate = candidate;
        CustomMinimumSize = new Vector2(AncientRowGeometry.OptionCellSize, AncientRowGeometry.OptionCellSize);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        FocusMode = Control.FocusModeEnum.All;
        ToggleMode = true;
        ClipContents = true;

        string optionName = names.Resolve(candidate.OptionKey, GameContentKind.Relic);
        TooltipText = string.Empty;
        MouseEntered += () => tooltipHost.ShowFor(this, candidate.OptionKey, optionName);
        MouseExited += () => tooltipHost.Dismiss(this);
        TreeExiting += () => tooltipHost.Dismiss(this);

        var iconHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = true
        };
        iconHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconHost.OffsetLeft = AncientRowGeometry.OptionCellInset;
        iconHost.OffsetTop = AncientRowGeometry.OptionCellInset;
        iconHost.OffsetRight = -AncientRowGeometry.OptionCellInset;
        iconHost.OffsetBottom = -AncientRowGeometry.OptionCellInset;

        IconDescriptor descriptor = icons.Resolve(candidate.OptionKey, GameContentKind.Relic, IconVariant.Small);
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
        AddChild(iconHost);

        _selectedBadge = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
        _selectedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _selectedBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _selectedBadge.VerticalAlignment = VerticalAlignment.Center;
        _selectedBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _selectedBadge.OffsetLeft = -16;
        _selectedBadge.OffsetTop = 1;
        _selectedBadge.OffsetRight = -1;
        _selectedBadge.OffsetBottom = 16;
        AddChild(_selectedBadge);
        SetState(false, candidate.IsAvailable);
    }

    public AncientOptionCandidate Candidate { get; }

    public void SetState(bool selected, bool enabled)
    {
        Disabled = !enabled;
        SetPressedNoSignal(selected);
        _selectedBadge.Text = selected ? "✓" : string.Empty;
        Ui1Theme.ApplyButton(this, selected
            ? Ui1ButtonRole.NavigationSelected
            : Ui1ButtonRole.Secondary);
        if (selected && !enabled)
        {
            // Persisted Search state must remain visibly selected even when the
            // rebuilt runtime catalog temporarily disables interaction. A disabled
            // Button otherwise fades the selected border enough to look unselected.
            Ui1Palette palette = Ui1Theme.Palette;
            var selectedDisabled = new StyleBoxFlat
            {
                BgColor = new Color(
                    palette.CardSelected.R,
                    palette.CardSelected.G,
                    palette.CardSelected.B,
                    0.58f),
                BorderColor = palette.BorderSelected,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4
            };
            AddThemeStyleboxOverride("disabled", selectedDisabled);
        }
        Modulate = enabled || selected ? Colors.White : new Color(1f, 1f, 1f, 0.48f);
    }
}
