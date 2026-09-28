using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Components;

internal enum SearchPresetSaveIntentKind : byte
{
    CreateCurrentQuery = 0,
    CreateFromExistingSnapshot = 1,
    EditMetadata = 2
}

internal sealed record SearchPresetMetadataDraft(
    string Title,
    string Description,
    IReadOnlyList<SearchPresetVisualIconRef> VisualIcons);

internal sealed record SearchPresetSaveCommit(
    SearchPresetSaveIntentKind Kind,
    string SourcePresetId,
    SearchPresetMetadataDraft Metadata);
