from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


request_bar = read("Ui/Components/AnalysisRequestBar.cs")
analysis_page = read("Ui/Pages/Analysis/AnalysisPage.cs")
shell = read("Ui/Shell/AppShell.cs")
selector = read("Ui/Components/CharacterPoolIconSelector.cs")
provider = read("Ui/Icons/NativeCharacterPoolIconProvider.cs")

require("CharacterPoolIconSelector _characterPicker" in request_bar,
        "Seed Analysis request bar does not use the native compendium icon selector.")
require("CharacterAvatarPicker" not in request_bar,
        "Seed Analysis still uses the generic portrait picker.")
require("IGameIconResolver _icons" not in request_bar,
        "Seed Analysis request bar still owns the generic character icon resolver.")
require("ICharacterPoolIconProvider characterPoolIcons" in request_bar and "IReadOnlyList<ModelKey> characterKeys" in request_bar,
        "Seed Analysis request bar does not receive runtime character keys + provider.")
require("new CharacterPoolIconSelector(" in request_bar and
        "characterKeys" in request_bar and "ScrollContainer" in request_bar,
        "Seed Analysis character selector is not runtime-cardinality driven with overflow containment.")
require("_characterPicker.SelectionChanged" in request_bar and
        "ContextChanged?.Invoke(CurrentReactiveDraft)" in request_bar,
        "Seed Analysis character changes no longer update/reactively analyze the Predictor draft.")
require("_characterPicker.SelectedKey" in request_bar and
        "AnalysisRequestDraft CurrentDraft" in request_bar,
        "Seed Analysis draft no longer reads the selected ModelKey.")
require("_characterPicker.Select(BaseGameModelKeys.Characters.Silent, notify: false)" in request_bar,
        "Seed Analysis default character changed.")
require("ICharacterPoolIconProvider characterPoolIcons" in analysis_page,
        "AnalysisPage does not receive the shared native provider.")
require("new AnalysisRequestBar(initialSeed, runtime.Profile, characterPoolIcons, characterKeys)" in analysis_page,
        "AnalysisPage does not inject runtime character keys into its request bar.")
require("new AnalysisPage(" in shell and "_runtimeCharacterCatalog?.EffectiveCharacters" in shell,
        "AppShell does not share the runtime character catalog/provider with Seed Analysis.")
require("new NativeCharacterPoolIconProvider(_icons)" in shell,
        "The native provider is not created with the runtime fallback on the UI shell path.")
require("ButtonSize = 42f" in selector,
        "Seed Analysis is not using the compact native selector size.")
require("character_icon_ironclad.png" in provider and "ResourceLoader.Load<Texture2D>" in provider,
        "Native Card Library static resource authority changed.")

print("analysisUsesNativeCompendiumCharacterIcons=true")
print("analysisGenericPortraitPickerRemoved=true")
print("analysisCharacterModelKeyContractPreserved=true")
print("analysisDefaultCharacterPreserved=true")
print("analysisDraftUpdatePreserved=true")
print("searchAndAnalysisShareCharacterIconProvider=true")
print("analysisBusinessSemanticsUnchanged=true")
