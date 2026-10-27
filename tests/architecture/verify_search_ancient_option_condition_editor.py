#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")

def require(value: bool, message: str, code: str) -> None:
    if not value:
        raise AssertionError(message)
    print(f"{code}=true")

editor = read("Ui/Pages/Search/Ancient/AncientOptionConditionEditor.cs")
page = read("Ui/Pages/Search/Ancient/AncientFilterPage.cs")
search_page = read("Ui/Pages/Search/SearchPage.cs")
controller = read("Ui/Controllers/SearchPageController.cs")
contracts = read("Search/Contracts/SearchContracts.cs")
profile = read("Core/World/AncientOptionConditionProfile.cs")
zh = read("Localization/rt2_ui/zh.json")
en = read("Localization/rt2_ui/en.json")

conditions = [
    "TezcataraHasBasicStrike", "NonupeipeSwiftEnchantableAtLeast4", "TanxInstinctEnchantableAtLeast3",
    "PaelGoopyDefendCardsAtLeast3", "PaelAllowLegionNoEventPet", "PaelRemovableCardsAtLeast5",
    "OrobasArchaicToothConditionMet", "OrobasTouchOfOrobasConditionMet", "DarvAllowPandorasBoxRelicSet",
]
require("AncientOptionConditionProfile.BroadDefault" in editor and "ButtonPressed = true" in editor and
        "assumed to be satisfied by default" in en and "默认视为满足" in zh,
        "Ancient option predicates are not broad/allowed by default.", "searchAncientOptionConditionsBroadDefault")
require(all(name in editor for name in conditions) and "CurrentProfile => new(" in editor,
        "The Ancient page editor does not expose all nine predicates.", "searchAncientOptionConditionsNinePredicates")
require("_conditionEditor = new AncientOptionConditionEditor(_icons, _tooltipHost)" in page and
        "public AncientOptionConditionProfile OptionConditions" in page,
        "The option-condition editor is not hosted by the real Ancient page.", "searchAncientOptionConditionsOnMatrixPage")
require("AncientOptionConditionProfile optionConditions = _categoryHost.AncientPage.OptionConditions" in search_page and
        all(f"= optionConditions.{name}" in search_page for name in conditions),
        "Search draft compilation does not source predicates from the Ancient editor.", "searchAncientOptionConditionsDraftWiring")
require("private void ClearAncientTargets" in page and "ClearAncientTargets(notify: true)" in page and
        "_conditionEditor.ResetToBroadDefault(notify: false)" in page,
        "Target clear and global eligibility reset are not separated.", "searchAncientOptionConditionsClearResponsibility")
require("_conditionEditor.SetEnabled(!running)" in page and "button.Disabled = !enabled" in editor,
        "Option predicates remain editable while Search is running.", "searchAncientOptionConditionsRunningLock")
require("ConditionButton(\"PAELS_CLAW\", Ui1TextKey.AncientConditionPaelGoopyDefend)" in editor and
        "ConditionButton(\"BEAUTIFUL_BRACELET\", Ui1TextKey.AncientConditionNonupeipeSwift)" in editor and
        "ConditionButton(\"TRI_BOOMERANG\", Ui1TextKey.AncientConditionTanxInstinct)" in editor and "CheckBox" not in editor,
        "Eligibility is still presented as a text checkbox list.", "searchAncientOptionConditionsIconSelector")
require("SearchAncientOptionConditionTooltipTitle" in editor and
        "ShowStructuredText" in editor and "button.TooltipText = string.Empty" in editor and
        "_tooltipHost.ShowStructuredText(" in editor,
        "Eligibility icon tooltips do not use the project advanced anchored tooltip with option name + appearance condition.", "searchAncientOptionConditionsOptionTooltips")
require("AncientOptionConditions" in contracts and "using Godot" not in profile,
        "The immutable profile boundary was replaced by live UI/game-state access.", "searchAncientOptionConditionsWorkerDtoBoundary")
require("ancientAssumptions = new AncientOptionConditionProfile" in controller,
        "Search controller no longer compiles the immutable condition profile.", "searchAncientOptionConditionsControllerPreserved")

print("searchAncientOptionConditionEditor=true")
