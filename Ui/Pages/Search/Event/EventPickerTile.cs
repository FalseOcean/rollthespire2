using Godot;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Event;

/// <summary>
/// Visual-only Event picker tile. The event identity comes from the existing catalog;
/// the tile presents a normalized Vanilla thumbnail and localized display name.
/// Tooltip occurrence-condition text is supplied by the same presentation knowledge
/// used by Predictor. Event-option presentation remains intentionally omitted.
/// </summary>
internal sealed partial class EventPickerTile : Button
{
    internal const float MinWidth = 148f;
    internal const float MaxWidth = 166f;
    internal const float Height = 190f;
    internal const float ArtMaxSize = 146f;

    public EventPickerTile(
        EventThumbnailDescriptor thumbnail,
        string eventName,
        string conditionsTitle,
        string? conditionText,
        AnchoredTooltipHost tooltipHost,
        float requestedWidth)
    {
        float tileWidth = Math.Clamp(requestedWidth, MinWidth, MaxWidth);
        CustomMinimumSize = new Vector2(tileWidth, Height);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        TooltipText = string.Empty;
        Ui1Theme.ApplyButton(this, Ui1ButtonRole.Secondary);

        MouseEntered += () =>
        {
            if (string.IsNullOrWhiteSpace(conditionText))
            {
                tooltipHost.ShowText(this, eventName);
                return;
            }

            tooltipHost.ShowStructuredText(
                this,
                eventName,
                conditionsTitle,
                conditionText);
        };
        MouseExited += () => tooltipHost.Dismiss(this);
        TreeExiting += () => tooltipHost.Dismiss(this);

        var margin = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 5);
        margin.AddThemeConstantOverride("margin_top", 5);
        margin.AddThemeConstantOverride("margin_right", 5);
        margin.AddThemeConstantOverride("margin_bottom", 5);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var content = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        content.AddThemeConstantOverride("separation", 5);

        float artSize = Math.Min(ArtMaxSize, tileWidth - 10f);
        var artCenter = new CenterContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0f, artSize)
        };
        artCenter.AddChild(new EventThumbnailView(
            thumbnail,
            new Vector2(artSize, artSize),
            EventThumbnailPresentation.PickerSquareCrop));
        content.AddChild(artCenter);

        Label name = Ui1Theme.Label(eventName, Ui1TextRole.Body);
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.CustomMinimumSize = new Vector2(0f, 30f);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        content.AddChild(name);

        margin.AddChild(content);
        AddChild(margin);
    }
}
