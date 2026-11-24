from __future__ import annotations

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def sha(relative: str) -> str:
    return hashlib.sha256((ROOT / relative).read_bytes()).hexdigest()


def require(condition: bool, message: str, code: str) -> None:
    if not condition:
        raise AssertionError(message)
    print(f"{code}=true")


page = read("Ui/Pages/Search/Neow/NeowFilterPage.cs")
master = read("Ui/Pages/Search/Neow/NeowRouteSelectionPanel.cs")
host = read("Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs")
slot = read("Ui/Pages/Search/Neow/SearchHorizontalResultSlot.cs")
zh = json.loads(read("Localization/rt2_ui/zh.json"))
en = json.loads(read("Localization/rt2_ui/en.json"))

require("WideMasterPanelWidth = 290f" in page and
        page.count("new Vector2(WideMasterPanelWidth, 0)") >= 2,
        "The wide Neow master panel was not narrowed through one shared width authority.",
        "searchNeowMasterPanelNarrowed")
require("RouteTileSize = 50f" in master and
        "RouteGridSeparation = 4f" in master and
        "_bonesButton.CustomMinimumSize = new Vector2(alignedWidth, 82)" in master and
        "_grid.CustomMinimumSize = new Vector2(alignedWidth, 0)" in master,
        "Narrowing the outer panel broke the shared Bones/grid alignment contract.",
        "searchNeowBonesGridAlignmentPreserved")
require('SearchNeowPickerTitleCurse),\n            string.Empty,\n            pickerTitle: _text.Get(Ui1TextKey.SearchNeowPickerTitleCurse)' in host,
        "The final-curse empty slot still uses a bare plus instead of localized Select Curse text.",
        "searchNeowFinalCurseEmptyTextLocalized")
require(zh["ui1.search.neow.picker.title_curse"] == "选择诅咒" and
        en["ui1.search.neow.picker.title_curse"] == "Select Curse",
        "Curse empty-state localization is not correct in both languages.",
        "searchNeowSelectCurseLocalization")
require(zh["ui1.search.neow.choose_relic"] == "选择遗物" and
        en["ui1.search.neow.choose_relic"] == "Select Relic",
        "Relic empty-state text still contains a duplicate plus marker.",
        "searchNeowRelicEmptyTextNoDuplicatePlus")
require(zh["ui1.search.neow.choose_card"] == "选择卡牌" and
        zh["ui1.search.neow.picker.title_potion"] == "选择药水" and
        "+" not in en["ui1.search.neow.choose_card"] and
        "+" not in en["ui1.search.neow.picker.title_potion"],
        "Card or potion empty-state text includes a duplicate plus marker.",
        "searchNeowOtherEmptyTextNoDuplicatePlus")
require('_missing.Text = "＋"' in slot and '_name.Text = _emptyText' in slot,
        "The shared result slot no longer owns the single independent plus icon.",
        "searchNeowSinglePlusIconContract")

expected_hashes = {
    "Search/Contracts/SearchContracts.cs": "ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457",
    "Search/Compilation/SearchExecutionRequestFactory.cs": "5a6d3f8298404d6a002184012c67f98b5678ee07b0a10507cfdf07b4e915f56f",
    "Search/Runtime/ProductionQueryValidator.cs": "e174e5b05f8393af6600540c0e065bb0b714a17db0eaee9f31943ff478e2223d",
    "Core/Effects/NeowEffectProjectionEngine.cs": "5957f21d33cff154f63130883207f19640874407b254ce32ea895ad086e46ac0",
    "Ui/Controllers/SearchPageController.cs": "b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5",
}
for file, expected in expected_hashes.items():
    require(sha(file) == expected, f"Frozen business file changed: {file}", "frozen_" + Path(file).stem)

print("searchNeowFinalMinorPolishValidation=true")
