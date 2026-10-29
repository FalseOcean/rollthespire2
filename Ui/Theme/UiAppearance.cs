using Godot;

namespace RolltheSpire2.Ui.Theme;

// The authored theme tokens are the current default palette. Saved legacy
// schemes no longer remap them or recolor already-created controls.
internal static class UiAppearance
{
    internal static Color Resolve(string hex) => new(hex);
}
