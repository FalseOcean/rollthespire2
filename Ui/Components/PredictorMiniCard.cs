using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Predictor-only compact card identity: localized name above official card portrait.
/// The whole mini-card is a read-only tooltip target. It does not own selection state
/// and never invokes card/gameplay behavior.
/// </summary>
internal sealed partial class PredictorMiniCard : PanelContainer
{
    public const float DefaultWidth = 96f;
    public const float DefaultArtworkHeight = 72f;
    public const float CompactWidth = 90f;
    public const float CompactArtworkHeight = 66f;
    public const float NeowSummaryWidth = 112f;
    public const float NeowSummaryArtworkHeight = 84f;
    public const int InnerGap = 4;
    public const float ArtworkHeightPerWidth = 0.75f;
    public const float MinimumResponsiveWidth = 88f;
    public const float MaximumResponsiveWidth = 156f;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly Label _title;
    private readonly Control _artHost;
    private readonly TextureRect _artwork;
    private readonly Label _missing;
    private GameContentDisplayViewModel? _card;

    public PredictorMiniCard(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost,
        float width = DefaultWidth,
        float artworkHeight = DefaultArtworkHeight)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));

        CustomMinimumSize = new Vector2(width, 0f);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = string.Empty;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 5f);

        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        column.AddThemeConstantOverride("separation", InnerGap);

        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, wrap: false);
        _title.MouseFilter = MouseFilterEnum.Ignore;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.CustomMinimumSize = new Vector2(0f, 20f);
        column.AddChild(_title);

        _artHost = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0f, artworkHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            ClipContents = true
        };
        _artwork = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        _artwork.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _missing = Ui1Theme.Label("?", Ui1TextRole.Muted);
        _missing.MouseFilter = MouseFilterEnum.Ignore;
        _missing.HorizontalAlignment = HorizontalAlignment.Center;
        _missing.VerticalAlignment = VerticalAlignment.Center;
        _missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _artHost.AddChild(_artwork);
        _artHost.AddChild(_missing);
        column.AddChild(_artHost);
        AddChild(column);

        MouseEntered += () =>
        {
            if (_card is { } card)
            {
                _tooltipHost.ShowCardFor(this, card.ModelKey, card.DisplayName);
            }
        };
        MouseExited += () => _tooltipHost.Dismiss(this);
        TreeExiting += () => _tooltipHost.Dismiss(this);
    }


    public void SetResponsiveWidth(float requestedWidth)
    {
        float width = Math.Clamp(requestedWidth, MinimumResponsiveWidth, MaximumResponsiveWidth);
        CustomMinimumSize = new Vector2(width, 0f);
        _artHost.CustomMinimumSize = new Vector2(0f, MathF.Round(width * ArtworkHeightPerWidth));
    }

    public void Bind(GameContentDisplayViewModel card, string visibleTitle, string missingIconText)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _title.Text = visibleTitle;
        IconDescriptor descriptor = _icons.Resolve(card.ModelKey, GameContentKind.Card, IconVariant.CardPickerLarge);
        _artwork.Texture = descriptor.Texture;
        _artwork.Visible = !descriptor.IsMissing && descriptor.Texture is not null;
        _missing.Visible = !_artwork.Visible;
        _missing.TooltipText = descriptor.IsMissing ? missingIconText : string.Empty;
    }
}
