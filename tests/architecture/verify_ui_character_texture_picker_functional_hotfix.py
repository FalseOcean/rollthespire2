from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def require(condition: bool, message: str, code: str) -> None:
    if not condition:
        raise AssertionError(message)
    print(f"{code}=true")


provider = read("Ui/Icons/NativeCharacterPoolIconProvider.cs")
picker = read("Ui/Controls/Pickers/RelicPickerPanel.cs")
tile = read("Ui/Controls/Pickers/RelicPickerTile.cs")
selector = read("Ui/Components/CharacterPoolIconSelector.cs")
contracts = read("Search/Contracts/SearchContracts.cs")
compiler = read("Search/Compilation/SearchExecutionRequestFactory.cs")

require(
    "IsTextureUsable(descriptor.Texture)" in provider and
    "LoadDescriptor(key, path)" in provider,
    "Character icon provider can still return a cached invalid Godot texture wrapper without re-resolving it.",
    "characterProviderRejectsInvalidCachedTexture",
)
require(
    "characterPoolIconReloaded=true" in provider and
    "direct-static-resource" in provider,
    "Invalid cached character textures are not re-resolved from audited static resources.",
    "characterProviderReloadsInvalidTexture",
)
require(
    "consumerSafe=true" in provider,
    "Provider invalid-texture fallback lacks the functional-consumer diagnostic marker.",
    "characterProviderConsumerSafeDiagnostic",
)
require(
    "Texture2D? usableTexture = TryGetUsableTexture(texture);" in picker and
    "GodotObject.IsInstanceValid(texture) ? texture : null" in picker,
    "Card picker filter buttons do not validate Godot texture wrappers before assignment.",
    "cardPickerValidatesFilterTexture",
)
require(
    "catch (ObjectDisposedException)" in picker and
    "fall back to '?' rather than aborting the picker modal" in picker,
    "Card picker lacks a fail-soft disposed-texture boundary.",
    "cardPickerDisposedTextureFailsSoft",
)
require(
    "if (usableTexture is null)" in picker and
    'Ui1Theme.Label("?", Ui1TextRole.Muted)' in picker,
    "Card picker does not preserve the filter button when its icon is unavailable.",
    "cardPickerFilterRemainsInteractiveWithoutTexture",
)
require(
    "TryAssignTexture(image, texture)" in tile and
    "GodotObject.IsInstanceValid(texture)" in tile and
    "catch (ObjectDisposedException)" in tile,
    "Picker candidate tiles can still abort modal construction on a disposed texture wrapper.",
    "cardPickerCandidateTilesFailSoftOnDisposedTexture",
)
require(
    'Ui1Theme.Label(textureAssigned ? string.Empty : "?", Ui1TextRole.Muted)' in tile,
    "Picker candidate tiles do not preserve ModelKey choices when their texture is unavailable.",
    "cardPickerCandidateModelKeyPreservedWithoutTexture",
)
require(
    "CharacterPoolIconSelector" in selector and
    "GodotObject.IsInstanceValid(texture)" in selector,
    "Existing top-level character selector fail-soft behavior was removed.",
    "characterSelectorExistingFailSoftPreserved",
)
require(
    "NeowStructuredEffectSearchCondition" in contracts and
    "SearchExecutionRequestFactory" in compiler,
    "Frozen Neow Search business contracts are unavailable.",
    "neowBusinessContractsPresent",
)

print("uiCharacterTexturePickerFunctionalHotfixValidation=true")
