using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Controls.Pickers;

internal enum RelicPickerCategory
{
    All,
    Neow,
    Common,
    Uncommon,
    Rare,
    Shop,
    Other
}

/// <summary>
/// Historical type name retained for source compatibility. The visual panel is
/// now a generic ModelKey picker for relic, card, potion, and curse result slots.
/// </summary>
internal sealed record RelicPickerRequest(
    string Title,
    IReadOnlyList<ModelKey> CandidateModelKeys,
    ModelKey? SelectedModelKey,
    IReadOnlyCollection<ModelKey> DisabledModelKeys,
    IReadOnlyDictionary<ModelKey, RelicPickerCategory> Categories,
    bool AllowClear,
    GameContentKind ContentKind,
    IconVariant IconVariant,
    Action<ModelKey?> OnSelected)
{
    public CardPickerContext? CardContext { get; init; }
}
