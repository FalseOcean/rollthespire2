using Godot;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal enum EventThumbnailPresentation
{
    Contain,
    PickerSquareCrop,
    ConditionSquareCrop
}

internal sealed partial class EventThumbnailView : PanelContainer
{
    // Owner visual prototype v1: preserve x=[15%,50%], y=[15%,85%].
    // For the audited ~2:1 Vanilla Default Event portraits this yields an
    // approximately square, left-biased subject crop without copying assets.
    internal const float PickerCropLeft = 0.15f;
    internal const float PickerCropRight = 0.50f;
    internal const float PickerCropTop = 0.15f;
    internal const float PickerCropBottom = 0.15f;

    public EventThumbnailView(
        EventThumbnailDescriptor descriptor,
        Vector2 size,
        EventThumbnailPresentation presentation = EventThumbnailPresentation.Contain)
    {
        CustomMinimumSize = size;
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 2f);

        if (descriptor.HasTexture)
        {
            Texture2D texture = descriptor.Texture!;
            bool cropDefaultPortrait =
                (presentation is EventThumbnailPresentation.PickerSquareCrop
                    or EventThumbnailPresentation.ConditionSquareCrop) &&
                descriptor.Kind == EventThumbnailKind.DefaultPortrait;
            if (cropDefaultPortrait)
            {
                texture = CreatePickerCrop(texture);
            }

            var image = new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = presentation == EventThumbnailPresentation.PickerSquareCrop && cropDefaultPortrait
                    ? TextureRect.StretchModeEnum.KeepAspectCovered
                    : TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            };
            image.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(image);
            return;
        }

        Label fallback = Ui1Theme.Label("?", Ui1TextRole.Accent);
        fallback.HorizontalAlignment = HorizontalAlignment.Center;
        fallback.VerticalAlignment = VerticalAlignment.Center;
        fallback.MouseFilter = MouseFilterEnum.Ignore;
        fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(fallback);
    }

    private static Texture2D CreatePickerCrop(Texture2D source)
    {
        float width = Math.Max(1f, source.GetWidth());
        float height = Math.Max(1f, source.GetHeight());
        float x = width * PickerCropLeft;
        float y = height * PickerCropTop;
        float cropWidth = Math.Max(1f, width * (1f - PickerCropLeft - PickerCropRight));
        float cropHeight = Math.Max(1f, height * (1f - PickerCropTop - PickerCropBottom));
        return new AtlasTexture
        {
            Atlas = source,
            Region = new Rect2(x, y, cropWidth, cropHeight),
            FilterClip = true
        };
    }
}
