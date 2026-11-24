using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// UI-only result-slot contract shared by compact card slots and the existing
/// small generic ModelKey slot. Business state remains a stable ModelKey.
/// </summary>
internal interface INeowResultSlot
{
    Control View { get; }
    event Action? Changed;
    ModelKey? SelectedKey { get; }
    bool IsEmpty { get; }

    void Configure(
        string label,
        IReadOnlyList<ModelKey> candidates,
        GameContentKind kind,
        IconVariant variant,
        IGameContentNameResolver names,
        string emptyText,
        string tooltip,
        IReadOnlyDictionary<ModelKey, RelicPickerCategory>? categories = null,
        string pickerTitle = "",
        CardPickerContext? cardPickerContext = null);

    void SetDisabledKeys(IEnumerable<ModelKey> keys);
    void Clear(bool notify);
    void Select(ModelKey? key, bool notify);
    void SetEnabled(bool enabled);
    void GrabSlotFocus();
}
