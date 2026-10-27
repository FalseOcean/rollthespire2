#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")



def require(value: bool, code: str, message: str) -> None:
    if not value:
        raise AssertionError(f"{code}: {message}")
    print(f"PASS: {code}")

page = read("Ui/Pages/Search/Ancient/AncientFilterPage.cs")
section = read("Ui/Pages/Search/Ancient/AncientActSection.cs")
card = read("Ui/Pages/Search/Ancient/AncientConditionRow.cs")
geometry = read("Ui/Pages/Search/Ancient/AncientRowGeometry.cs")
eligibility = read("Ui/Pages/Search/Ancient/AncientOptionConditionEditor.cs")
identity = read("Ui/Pages/Search/Ancient/AncientIdentityButton.cs")
option = read("Ui/Pages/Search/Ancient/AncientOptionIconButton.cs")
zh = json.loads(read("Localization/rt2_ui/zh.json"))
en = json.loads(read("Localization/rt2_ui/en.json"))

require("private readonly HBoxContainer _sectionsHost" in page and
        "_sectionsHost.AddThemeConstantOverride(\"separation\", AncientRowGeometry.ActGap)" in page,
        "act2_act3_horizontal_split", "Ancient page is not split into two horizontal Act columns.")
require("var grid = new GridContainer" in section and "Columns = 2" in section,
        "two_by_two_ancient_card_grid", "Each Act does not use a 2-column Ancient-card grid.")
require("internal sealed partial class AncientConditionRow : PanelContainer" in card and
        "content.AddChild(_identity);" in card and "var options = new GridContainer" in card,
        "ancient_identity_above_options", "Ancient identity and its options are not separated vertically.")
require("public const int StandardOptionColumns = 5" in geometry,
        "ten_option_five_by_two", "Default 10-option Ancient layout is not 5 columns.")
require("DarvOptionColumns" not in geometry and
        "Math.Min(AncientRowGeometry.StandardOptionColumns, definition.Options.Count)" in card and
        "Key.F6" not in page and "Ancient layout prototype: DarvColumns=" not in page,
        "darv_five_column_wrap", "Darv does not share the five-column wrap rule (Act3 10 -> 5x2; Act2 12 -> 5+5+2).")
require('"PAEL" => 0' in section and '"TEZCATARA" => 1' in section and
        '"OROBAS" => 2' in section and '"DARV" => 3' in section,
        "act2_tall_cards_bottom_row", "Orobas and Darv are not paired on the Act 2 bottom row.")
require("_optionsViewport = new Control" in card and "ClipContents = true" in card and
        "CustomMinimumSize = new Vector2(0, optionHeight)" in card,
        "option_grid_width_isolated", "Option-grid minimum width can still dictate the Ancient-card width.")
require("OptionCellSize = 41f" in geometry and "OptionCellInset = 2f" in geometry and
        "IconVariant.Small" in option,
        "option_icons_remain_primary_visual", "Search option icon size/source is not at the current compact contract.")
require("new AncientOptionConditionEditor(_icons, _tooltipHost)" in page and
        "HFlowContainer _conditions" in eligibility and
        "ConditionButton(\"PAELS_CLAW\", Ui1TextKey.AncientConditionPaelGoopyDefend)" in eligibility and
        "CheckBox" not in eligibility,
        "eligibility_icon_selector", "Eligibility conditions are still a permanent checkbox text list.")
require("expectedEligible ? Ui1ButtonRole.Secondary : Ui1ButtonRole.Ghost" in eligibility and
        "new Color(1f, 1f, 1f, 0.38f)" in eligibility and
        "Ui1ButtonRole.NavigationSelected" in option,
        "eligibility_target_visual_distinction", "Eligibility and search-target selection use indistinguishable selected styles.")
require("SearchAncientOptionConditionTooltipTitle" in eligibility and
        "_tooltipHost.ShowStructuredText(" in eligibility and
        "button.TooltipText = string.Empty" in eligibility,
        "eligibility_player_tooltip", "Eligibility icon tooltip does not use the advanced anchored tooltip for option name and appearance condition.")
require("SearchAncientPageHint" not in page and
        "Any" not in zh["ui1.search.ancient.act2_title"] and "Any" not in zh["ui1.search.ancient.act3_title"] and
        "Any" not in en["ui1.search.ancient.act2_title"] and "Any" not in en["ui1.search.ancient.act3_title"],
        "legacy_any_copy_removed", "Player-facing Act header still exposes Any/row-matching implementation language.")
require("ClearAncientTargets(notify: true)" in page and
        "private void ClearAncientTargets" in page and
        "_conditionEditor.ResetToBroadDefault(notify: false);" in page and
        page.index("ClearAncientTargets(notify: false);") < page.index("_conditionEditor.ResetToBroadDefault(notify: false);"),
        "clear_target_vs_eligibility_responsibility", "Page clear and global draft reset are not separated.")
require("bool hasAnyTarget" in page and "!_conditionEditor.CurrentProfile.IsBroadDefault" not in page.split("private void RefreshClearState", 1)[1].split("private void OpenClearConfirmation", 1)[0],
        "clear_button_ignores_eligibility_state", "Clear Ancient Conditions is still enabled by eligibility-only customization.")
require("CustomMinimumSize = new Vector2(132, 42)" in page,
        "ancient_clear_header_height", "Clear Ancient Conditions is not aligned to the eligibility header strip height.")
require(zh["ui1.search.ancient.act2_title"] == "第二幕" and zh["ui1.search.ancient.act3_title"] == "第三幕" and
        en["ui1.search.ancient.act2_title"] == "Act 2" and en["ui1.search.ancient.act3_title"] == "Act 3",
        "minimal_act_titles", "Act titles still duplicate matching-rule explanation.")
require("默认视为满足" in zh["ui1.search.ancient.option_conditions_hint"] and
        "实时牌组" not in zh["ui1.search.ancient.option_conditions_hint"] and
        "rebuild" not in en["ui1.search.ancient.option_conditions_hint"].lower(),
        "eligibility_help_copy_player_facing", "Eligibility help still explains internal reconstruction/live-state machinery.")
require("选项出现条件不会改变" in zh["ui1.search.ancient.clear_confirm_body"] and
        "unchanged" in en["ui1.search.ancient.clear_confirm_body"].lower(),
        "clear_confirmation_scope_explicit", "Clear confirmation does not preserve the eligibility responsibility boundary.")
require("SizeFlagsHorizontal = Control.SizeFlags.ExpandFill" in identity and
        "AncientRowGeometry.HeaderHeight" in identity and
        "AncientRowGeometry.PortraitSize" in identity,
        "ancient_card_header_full_width", "Ancient portrait/name header does not own the top of the card.")
require("_identity.Pressed += ToggleIdentity" in card and
        "_selectionToggle" not in card and
        "private void ToggleIdentity" in card and
        "_expanded = _active" in card and
        "_optionsViewport.Visible = _expanded" in card,
        "ancient_identity_reveal_and_condition_are_unified", "Identity click does not own both activation and option reveal.")
identity_toggle = card.split("private void ToggleIdentity", 1)[1].split("private void ToggleOption", 1)[0]
require("_active = !_active" in identity_toggle and "_expanded = _active" in identity_toggle and
        "Changed?.Invoke" in identity_toggle,
        "ancient_identity_click_updates_condition", "Identity click does not unify reveal and Search activation.")
restore_state = card.split("public void RestoreState", 1)[1].split("public void SetEnabled", 1)[0]
require("_expanded = _active" in restore_state and
        "row.RestoreState(state);" in page and
        "_expanded = false" in card and
        "foreach (AncientActSection section in _sections) section.Clear" in page,
        "ancient_active_reveal_survives_rebuild",
        "Active Ancient option reveal is not restored after a page/catalog rebuild, or clear no longer collapses it.")

contracts = read("Search/Contracts/SearchContracts.cs")
compiler = read("Search/Compilation/SearchExecutionRequestFactory.cs")
controller = read("Ui/Controllers/SearchPageController.cs")
persistence = read("Ui/Persistence/SearchWorkspacePersistence.cs")
require("AncientBranchConditions" in contracts and "OptionAny" in contracts and "SeaGlassTargetAny" in contracts,
        "ancient_structured_contract_current", "Structured Ancient branch semantics are missing from the current Search contract.")
require("NormalizeAncientBranches" in compiler and ".Where(row => row.IsActive)" in controller,
        "ancient_active_rows_compile_only", "Current compilation no longer restricts Ancient branches to active rows.")
require("public SearchDraft? Draft { get; set; }" in persistence and "AncientOptionConditions => new(" in persistence,
        "ancient_workspace_persistence_current", "Current Search draft / Ancient eligibility persistence contract is missing.")

print("SearchAncientPlayerSemanticsLayoutPrototypeValidation=PASS")
