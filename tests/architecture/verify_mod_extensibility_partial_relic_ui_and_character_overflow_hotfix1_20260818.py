from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")

def check(name, cond):
    if not cond:
        raise SystemExit(f"FAIL: {name}")
    print(f"PASS: {name}")

selector = read("Ui/Components/CharacterPoolIconSelector.cs")
search_context = read("Ui/Pages/Search/SearchContextBar.cs")
analysis = read("Ui/Components/AnalysisRequestBar.cs")
relic_ui = read("Ui/Pages/Search/Relic/RelicSequenceSearchUiCatalog.cs")
controller = read("Ui/Controllers/SearchPageController.cs")
relic_page = read("Ui/Pages/Search/Relic/RelicSequenceFilterPage.cs")
predictor = read("Core/Relics/RelicSequencePredictor.cs")

check("character rows keep page-specific bounded thresholds",
      "CharacterOverflowThreshold = 10" in search_context and
      "CharacterOverflowThreshold = 7" in analysis)
check("character selectors retain direct host before their overflow threshold",
      "characterHost = _characters" in search_context and
      "characterHost = _characterPicker" in analysis)
check("characters beyond each page threshold retain bounded horizontal overflow",
      "RequiresHorizontalOverflow(CharacterOverflowThreshold)" in search_context and
      "PreferredViewportWidth(CharacterOverflowThreshold)" in search_context and
      "ScrollContainer.ScrollMode.Auto" in search_context and
      "RequiresHorizontalOverflow(CharacterOverflowThreshold)" in analysis)
check("selector width remains fixed-button data-driven",
      "ButtonSize = 42f" in selector and
      "ButtonSeparation = 6f" in selector and
      "CalculateWidth" in selector)

check("partial runtime relic buckets no longer blank UI catalog",
      'return Empty(profileId, evidenceCode + ":lane-mapping-not-exact")' not in relic_ui and
      "lane-mapping-partial-ui-discovery" in relic_ui)
check("ambiguous relic lane memberships remain excluded locally",
      "lanesForKey.Count == 1" in relic_ui and
      "ambiguous-mappings-excluded" in relic_ui)
check("partial relic catalog cannot destructively reconcile saved conditions",
      "catalog.CatalogAvailable && catalog.LaneMappingExact" in relic_page and
      "bool LaneMappingExact" in relic_ui)
check("relic predictor exactness boundary remains conservative",
      "!modern.RelicRarityAuthorityExact" in predictor and
      "!modern.PlayerRelicPoolCompositionExact" in predictor and
      "!modern.RelicShopEligibilityAuthorityExact" in predictor and
      "UnknownModernRelicSequenceHooksOrModifiers" in predictor)
check("partial relic catalog diagnostics expose captured-vs-exact state",
      "searchRelicCatalogAuthority=true" in controller and
      "playerRelicCapturedCount=" in controller and
      "relicSequenceUiLaneMappingExact=" in controller and
      "relicSequenceUiEvidence=" in controller)

print("modExtensibilityHotfix=PartialRelicUiDiscovery+PageSpecificCharacterOverflowThresholds")
print("predictionAuthorityUnchanged=true")
