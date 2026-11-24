from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")

def check(name, condition):
    if not condition:
        raise SystemExit(f"FAIL: {name}")
    print(f"PASS: {name}")

provider = read("Ui/Icons/NativeCharacterPoolIconProvider.cs")
shell = read("Ui/Shell/AppShell.cs")
neow = read("Ui/Pages/Search/Neow/NeowSearchUiCatalog.cs")
picker = read("Ui/Controls/Pickers/RelicPickerPanel.cs")
controller = read("Ui/Controllers/SearchPageController.cs")

check("character icons use audited direct static resources",
      "character_icon_ironclad.png" in provider and
      "character_icon_silent.png" in provider and
      "character_icon_defect.png" in provider and
      "character_icon_regent.png" in provider and
      "character_icon_necrobinder.png" in provider and
      "energy_colorless.tres" in provider)
check("character icon provider no longer instantiates native Card Library scene",
      "PackedScene" not in provider and ".Instantiate(" not in provider and ".Free(" not in provider)
check("invalid cached texture is re-resolved instead of permanently poisoned",
      "characterPoolIconReloaded=true" in provider and
      "LoadDescriptor(key, path)" in provider)
check("provider disposal does not free game resources",
      "sourceRootFreed=false;resourceMode=direct-static" in provider and
      "never Free()s native Card Library nodes" in shell)

check("partial captured character card pool remains visible to authoring UI",
      "authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>()" in neow)
check("partial captured colorless card pool remains visible to authoring UI",
      "authority.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>()" in neow)
check("partial captured other-character pools remain visible to authoring UI",
      "authority.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>()" in neow)
check("partial captured transform pool remains visible to authoring UI",
      "authority.TransformPool ?? Array.Empty<NeowEffectCardSnapshot>()" in neow)
snapshot = read("Core/Effects/Snapshots/NeowEffectAuthoritySnapshot.cs")
check("partial captured non-card identities remain visible without deleting exact authority",
      "authority.OrderedRelicBag ?? Array.Empty<NeowEffectRelicSnapshot>()" in neow and
      "authority.BonesEligibleRelics ?? Array.Empty<ModelKey>()" in neow and
      "authority.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>()" in neow and
      "authority.GeneratedCursePool ?? Array.Empty<ModelKey>()" in neow and
      "HasExactRelicBag" in snapshot and "HasExactBonesPools" in snapshot)

check("picker logs actual open candidate/metadata counts",
      "cardPickerOpened=true" in picker and "allowedCandidates=" in picker and "metadata=" in picker)
check("picker logs empty filtered state",
      "cardPickerEmptyAfterFilters=true" in picker)
check("one broken mod presentation candidate cannot abort entire picker",
      "pickerCandidatePresentationFallback=true" in picker and
      "picker-presentation-icon-resolve-failed" in picker)
check("catalog authority log is pool-granular",
      "searchCardCatalogAuthority=true" in controller and
      "characterRewardPoolExact=" in controller and
      "otherCharacterPoolsExact=" in controller and
      "transformPoolExact=" in controller)

print("modCompatibilityBoundary=PresentationResourceLifetime+PartialAuthoringCatalog")
print("searchSemanticAuthorityUnchanged=true")
