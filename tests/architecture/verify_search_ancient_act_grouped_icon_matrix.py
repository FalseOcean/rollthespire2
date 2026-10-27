#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")

def require(value: bool, message: str, code: str) -> None:
    if not value:
        raise AssertionError(message)
    print(f"{code}=true")

catalog = read("Ui/Pages/Search/Ancient/AncientSearchUiCatalog.cs")
page = read("Ui/Pages/Search/Ancient/AncientFilterPage.cs")
section = read("Ui/Pages/Search/Ancient/AncientActSection.cs")
card = read("Ui/Pages/Search/Ancient/AncientConditionRow.cs")
geometry = read("Ui/Pages/Search/Ancient/AncientRowGeometry.cs")
identity = read("Ui/Pages/Search/Ancient/AncientIdentityButton.cs")
option = read("Ui/Pages/Search/Ancient/AncientOptionIconButton.cs")
additional = read("Ui/Pages/Search/Ancient/AncientAdditionalConditionHost.cs")
host = read("Ui/Pages/Search/SearchCategoryHost.cs")
search_page = read("Ui/Pages/Search/SearchPage.cs")
controller = read("Ui/Controllers/SearchPageController.cs")
contracts = read("Search/Contracts/SearchContracts.cs")
compiler = read("Search/Compilation/SearchExecutionRequestFactory.cs")
evaluator = read("Search/Runtime/ProductionQueryValidator.cs")
zh = json.loads(read("Localization/rt2_ui/zh.json"))
en = json.loads(read("Localization/rt2_ui/en.json"))

require("AncientPage = new AncientFilterPage" in host,
        "The Ancient category is still a placeholder.", "searchAncientRealCategoryPage")
require('Act2AncientEntries = { "OROBAS", "PAEL", "TEZCATARA" }' in catalog and
        'Act3AncientEntries = { "NONUPEIPE", "TANX", "VAKUU" }' in catalog and
        'BindAncient("DARV"' in catalog,
        "The audited Act2/Act3/Darv eight-row matrix is incomplete.", "searchAncientEightAuditedRows")
require("foreach (int act in new[] { 2, 3 })" in catalog and "new List<AncientRowDefinition>(4)" in catalog,
        "The UI catalog does not build four Ancients for each supported Act.", "searchAncientTwoActsFourCardsEach")
require("Beta109Generation?.AncientEventContexts" in catalog and "context.Catalog.Pools" in catalog and
        ".SelectMany(pool => pool.OrderedOptions)" in catalog,
        "Modern option membership is not sourced from immutable authority.", "searchAncientModernCatalogAuthority")
require("LegacyOptionEntries(ancientKey.Entry, act)" in catalog and '"DARV" when act == 2' in catalog and '"DARV" when act == 3' in catalog,
        "Legacy option membership or Act-scoped Darv catalog is missing.", "searchAncientLegacyCatalogAuthority")
require("WorldCompendiumAncientIcon" in identity and "IconVariant.Small" in option,
        "Ancient portraits or option relic icons do not use the audited resolvers.", "searchAncientAuditedIconResolvers")
require("TooltipText = string.Empty" in option and "tooltipHost.ShowFor" in option and
        "_name.Text" not in option and "Label(optionName" not in option,
        "Option cells permanently render names instead of icon-only tooltip presentation.", "searchAncientOptionIconOnlyTooltip")
require("private readonly HBoxContainer _sectionsHost" in page and "Columns = 2" in section and
        "internal sealed partial class AncientConditionRow : PanelContainer" in card,
        "Act2/Act3 or per-Act Ancient card grouping is missing.", "searchAncientActColumnsAndCardGrid")
require("StandardOptionColumns = 5" in geometry and "DarvOptionColumns" not in geometry and
        "Math.Min(AncientRowGeometry.StandardOptionColumns, definition.Options.Count)" in card,
        "10-option and Act-scoped Darv five-column wrap geometry is missing.", "searchAncientOptionGridGeometry")
require('"PAEL" => 0' in section and '"TEZCATARA" => 1' in section and
        '"OROBAS" => 2' in section and '"DARV" => 3' in section,
        "Act 2 presentation order does not keep Orobas and Darv together on the bottom row.",
        "searchAncientAct2TallCardsBottomRow")
require("var options = new GridContainer" in card and "CenterContainer" in card and "new HBoxContainer" not in card.split("var options =", 1)[1].split("_pending", 1)[0],
        "Ancient options are still flattened into one horizontal row.", "searchAncientOptionsOwnCardWidth")
require("_identity.Pressed += ToggleIdentity" in card and
        "_selectionToggle" not in card and
        "_expanded = _active" in card and
        "_active = !_active" in card and "bool selecting = !_selectedOptions.Contains(optionKey);" in card and
        "_selectedOptions.Add(optionKey);\n            _active = true;" in card and "_selectedOptions.Remove(optionKey);" in card,
        "Unified identity reveal/selection or directional option auto-activation changed.", "searchAncientIdentityAndOptionInteraction")
identity_toggle = card.split("private void ToggleIdentity", 1)[1].split("private void ToggleOption", 1)[0]
require("_expanded = _active" in identity_toggle and "Changed?.Invoke" in identity_toggle and
        "Visible = _expanded" in card,
        "Identity reveal and Search activation are not unified per-card state.", "searchAncientIndependentRevealState")
require("public int EnabledConditionCount => _active ? 1 : 0" in card and
        "_categoryHost.AncientPage.EnabledConditionCount" in search_page,
        "Condition count is not based on active Ancient cards.", "searchAncientStructuralConditionCount")
require("Dictionary<(int Act, ModelKey Key), AncientRowUiState>" in page and "_history[(state.Act, state.AncientKey)]" in page,
        "Act2 and Act3 Darv/history state can be merged accidentally.", "searchAncientActScopedHistory")
restore_state = card.split("public void RestoreState", 1)[1].split("public void SetEnabled", 1)[0]
require("_expanded = _active" in restore_state and "row.RestoreState(state);" in page,
        "Active Ancient rows can restore selected options in a hidden/collapsed state after UI rebuild.",
        "searchAncientActiveRevealRestored")
require("AncientBranchConditions" in contracts and "OptionAny" in contracts and "SeaGlassTargetAny" in contracts and "NormalizeAncientBranches" in compiler,
        "The row-scoped structured Ancient contract changed.", "searchAncientStructuredBranchContract")
require(".Where(row => row.IsActive)" in controller and "AncientSearchBranchCondition[] ancientBranches" in controller,
        "Inactive card history can still compile into active Ancient branches.", "searchAncientOnlyActiveCardsCompile")
require("GroupBy(condition => condition.Act)" in evaluator and "FirstOrDefault(item => item.AncientKey == ancient.AncientKey)" in evaluator and "branch.OptionAny.Contains" in evaluator,
        "Evaluator no longer implements existing Act/card option semantics.", "searchAncientCanonicalEvaluatorPreserved")
require("AncientBranchSeaGlassTargetNotExact" in evaluator and "SeaGlassCharacterTarget" in catalog and "ICharacterPoolIconProvider" in additional,
        "SeaGlass target refinement was dropped or no longer fails closed.", "searchAncientSeaGlassRefinementPreserved")
require("Ui1ButtonRole.NavigationSelected" in identity and "Ui1ButtonRole.NavigationSelected" in option and 'selected ? "✓"' in identity and 'selected ? "✓"' in option,
        "Selected identity/option state relies on color alone.", "searchAncientSelectedBorderAndBadge")
require("ToggleMode = true" in option and "SetPressedNoSignal(selected)" in option,
        "Rebuilt Ancient option buttons do not restore their selected pressed-state chrome.",
        "searchAncientSelectedOptionPressedStateRestored")
require(zh["ui1.search.ancient.act2_title"] == "第二幕" and zh["ui1.search.ancient.act3_title"] == "第三幕" and
        en["ui1.search.ancient.act2_title"] == "Act 2" and en["ui1.search.ancient.act3_title"] == "Act 3",
        "Act section localization still exposes matching-rule implementation language.", "searchAncientActLocalization")

require("Predict(" in read("Core/World/Stable107AncientOptionPredictor.cs") and
        "Predict(" in read("Core/World/Beta109/Beta109AncientOptionProvider.cs") and
        "BossPredictionResult" in read("Core/World/Beta109/Beta109WorldPredictionProvider.cs"),
        "Current Ancient predictor/provider responsibilities are missing.",
        "searchAncientCurrentPredictorResponsibilities")

print("searchAncientActGroupedIconMatrixValidation=true")
