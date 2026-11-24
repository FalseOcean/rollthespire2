using Godot;

namespace RolltheSpire2.Ui.Theme;

// The operation hover and map node use the same native paper and night-blue tint.
internal static class NativeNightSurface
{
    internal static NinePatchRect Backdrop(bool shadow = false)
    {
        return new NinePatchRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/ui/hover_tip.png"),
            Modulate = shadow ? new Color(0, 0, 0, .48f) : new Color(.48f, .67f, .94f, 1),
            PatchMarginLeft = 32, PatchMarginTop = 20,
            PatchMarginRight = 32, PatchMarginBottom = 20,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = shadow
        };
    }
}
