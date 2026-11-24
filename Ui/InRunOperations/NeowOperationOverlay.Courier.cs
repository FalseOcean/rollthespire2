using Godot;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private float? _courierTipWidth;

    // Native tooltip containers include transparent skin margins. Match the
    // actual paper rectangle and nine-patch geometry, not just their 360px dock.
    internal void MatchCourierTooltip(Control? nativeTip)
    {
        _courierTipWidth = null;
        if (_preview?.Source.Entry != "THE_COURIER" || nativeTip == null) return;
        var paper = nativeTip.GetChildren().OfType<Control>()
            .Select(tip => tip.GetNodeOrNull<NinePatchRect>("Bg")).FirstOrDefault(bg => bg?.IsVisibleInTree() == true);
        if (paper == null || paper.Size.X <= 0) return;
        _courierTipWidth = paper.GetGlobalRect().Size.X;
        foreach (var panel in _summaryPanels)
            foreach (var background in panel.Shell.GetChildren().OfType<NinePatchRect>())
            {
                background.Texture = paper.Texture;
                background.RegionRect = paper.RegionRect;
                background.PatchMarginLeft = paper.PatchMarginLeft;
                background.PatchMarginRight = paper.PatchMarginRight;
                background.PatchMarginTop = paper.PatchMarginTop;
                background.PatchMarginBottom = paper.PatchMarginBottom;
                background.AxisStretchHorizontal = paper.AxisStretchHorizontal;
                background.AxisStretchVertical = paper.AxisStretchVertical;
            }
    }
}
