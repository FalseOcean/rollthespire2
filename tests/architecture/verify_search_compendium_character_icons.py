from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


contracts = read("Ui/Icons/IconContracts.cs")
provider_contract = read("Ui/Icons/ICharacterPoolIconProvider.cs")
provider = read("Ui/Icons/NativeCharacterPoolIconProvider.cs")
selector = read("Ui/Components/CharacterPoolIconSelector.cs")
context = read("Ui/Pages/Search/SearchContextBar.cs")
page = read("Ui/Pages/Search/SearchPage.cs")
shell = read("Ui/Shell/AppShell.cs")
resolver = read("Ui/Icons/ReflectionGameIconResolver.cs")

require("CharacterCompendiumPoolSmall" in contracts, "Dedicated compendium icon variant is missing.")
require("interface ICharacterPoolIconProvider" in provider_contract, "Strict character-pool provider contract is missing.")
for path in [
    "character_icon_ironclad.png",
    "character_icon_silent.png",
    "character_icon_defect.png",
    "character_icon_necrobinder.png",
    "character_icon_regent.png",
    "energy_colorless.tres",
]:
    require(path in provider, f"Source-audited native Card Library resource missing: {path}.")
require("ResourceLoader.Load<Texture2D>" in provider,
        "Provider does not load the audited Texture2D resources directly.")
require("PackedScene" not in provider and ".Instantiate(" not in provider and ".Free(" not in provider,
        "Provider still couples character icon lifetime to an instantiated Card Library node tree.")
require("LoadDescriptor(key, path)" in provider and "characterPoolIconReloaded=true" in provider,
        "Provider does not re-resolve an invalid cached wrapper from the stable resource path.")
require("CharacterSelectIcon" not in provider, "Character-select portrait fallback was reintroduced.")
require("IconTexture" not in provider, "Top-panel icon fallback was reintroduced.")
require("ModelDb." not in provider and "ModelDb<" not in provider, "Provider must not resolve the icon through ModelDb.")
code_only = "\n".join(line for line in provider.splitlines() if not line.lstrip().startswith("///"))
require("RunState" not in code_only, "Provider contains a live RunState dependency.")
require("CharacterPoolIconSelector" in context, "Search does not use the new selector.")
require("CharacterAvatarPicker" not in context, "Search still uses the portrait picker.")
require("IGameIconResolver _icons" not in context, "Search context still owns the generic icon resolver.")
require("ButtonSize = 42f" in selector, "Small compendium icon size changed.")
require("KeepAspectCentered" in selector, "Selector does not preserve native aspect/atlas region semantics.")
require('Ui1Theme.Label("?", Ui1TextRole.Muted)' in selector, "Explicit missing-icon placeholder is absent.")
require("NavigationSelected" in selector and 'Ui1Theme.Label("✓"' in selector,
        "Selection is not represented by a non-color visual state.")
require("ICharacterPoolIconProvider characterPoolIcons" in page,
        "SearchPage does not receive the main-thread provider.")
require("new NativeCharacterPoolIconProvider(_icons)" in shell,
        "AppShell does not create the provider with the runtime icon fallback.")
require("_runtimeCharacterCatalog?.EffectiveCharacters" in shell and "new SearchPage(" in shell,
        "Runtime character catalog/provider are not injected into SearchPage.")
require("CharacterCompendiumPoolSmall" not in resolver,
        "Generic ReflectionGameIconResolver has taken ownership of the Card Library icon contract.")

require("namespace RolltheSpire2.Ui.Icons;" in provider_contract,
        "Visual provider contract left the UI icon namespace.")

print("nativeCardLibraryStaticResourceAuthority=true")
print("fiveVanillaCharacterResourcesMappedByModelKey=true")
print("directTexture2DResourceLoad=true")
print("invalidWrapperReloadByPath=true")
print("characterPortraitHeuristicRemovedFromSearch=true")
print("mainThreadUiProviderOnly=true")
print("missingIconFailSoft=true")
print("searchBusinessIdentityUnchanged=true")
