using Godot;

namespace RolltheSpire2.Ui.Components;

/// <summary>Compact character rows, including portrait-shaped mod artwork.</summary>
internal static class CharacterDropdownVisuals
{
    internal const int IconExtent = 28;

    internal static void Apply(OptionButton picker)
    {
        picker.ExpandIcon = true;
        picker.AddThemeConstantOverride("icon_max_width", IconExtent);
        PopupMenu popup = picker.GetPopup();
        popup.AddThemeFontSizeOverride("font_size", 18);
        popup.AddThemeConstantOverride("v_separation", 8);
        void BoundIcons()
        {
            for (int i = 0; i < popup.ItemCount; i++)
            {
                Texture2D? icon = popup.GetItemIcon(i);
                // PopupMenu limits width only. Adjust that width to also bound
                // portrait height, without replacing or cropping the artwork.
                int width = icon is not null && icon.GetHeight() > 0
                    ? Math.Clamp((int)Math.Floor(IconExtent * Math.Min(1d,
                        (double)icon.GetWidth() / icon.GetHeight())), 1, IconExtent)
                    : IconExtent;
                popup.SetItemIconMaxWidth(i, width);
            }
        }
        BoundIcons();
        popup.AboutToPopup += BoundIcons;
    }
}
