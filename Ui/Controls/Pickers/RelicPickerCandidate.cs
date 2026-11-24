using Godot;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Controls.Pickers;

internal sealed record RelicPickerCandidate(
    ModelKey ModelKey,
    string LocalizedName,
    Texture2D? Texture,
    RelicPickerCategory Category,
    bool IsSelected,
    bool IsDisabled,
    string DisabledReason);
