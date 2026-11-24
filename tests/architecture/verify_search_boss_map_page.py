#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PAGE = (ROOT / "Ui/Pages/Search/BossMap/BossMapFilterPage.cs").read_text(encoding="utf-8")
ROW = (ROOT / "Ui/Pages/Search/BossMap/BossMapVariantEditor.cs").read_text(encoding="utf-8")
CATALOG = (ROOT / "Ui/Pages/Search/BossMap/BossMapSearchUiCatalog.cs").read_text(encoding="utf-8")
CONTROLLER = (ROOT / "Ui/Controllers/SearchPageController.cs").read_text(encoding="utf-8")
HOST = (ROOT / "Ui/Pages/Search/SearchCategoryHost.cs").read_text(encoding="utf-8")


def require(value: bool, message: str, code: str) -> None:
    if not value:
        raise AssertionError(message)
    print(f"{code}=true")


require("ActSelectionGroups" in CATALOG and "OrderedActCatalog" in CATALOG and "ActGroups" in CATALOG,
        "Boss/map UI catalog is not projected from production world authority.",
        "bossMapDynamicCatalog")
require("BossMapPage = new BossMapFilterPage" in HOST,
        "Boss/map category is still a placeholder.",
        "bossMapPageMounted")

toggle = ROW.split("private void ToggleBoss", 1)[1].split("private void RestoreSelection", 1)[0]
select_branch = toggle.split("if (selecting)", 1)[1].split("else", 1)[0]
deselect_branch = toggle.split("else", 1)[1].split("Refresh();", 1)[0]
require("_active = true;" in select_branch,
        "Selecting a Boss must activate its selectable map variant.",
        "bossSelectionActivatesVariant")
require("_active" not in deselect_branch,
        "Deselecting a Boss must preserve the map variant's current active state.",
        "bossDeselectionDoesNotTouchVariant")
require("row.FirstBossAny.Count > 0 ? row.FirstBossAny : row.AllBossKeys" in PAGE
        and "row.SecondBossAny.Count > 0 ? row.SecondBossAny : row.AllBossKeys" in PAGE
        and "secondBoss != firstBoss" in PAGE,
        "Act 3 legal-route validation does not expand empty rows or enforce distinct ordered Bosses.",
        "act3DistinctBossValidation")
require("BuildBossOrdinalFilters" in CONTROLLER and "new ActOrdinalModelKeySetFilter" in CONTROLLER,
        "Boss/map draft is not compiled through ordered Boss filters.",
        "bossMapOrdinalCompilation")
require("BossMapSearchUiCatalog.FromAuthority" in CONTROLLER,
        "Boss/map catalog is not bound from captured production authority.",
        "bossMapAuthorityBinding")
require("第一位与第二位首领不能同时只限定为同一首领。" in PAGE,
        "Required Act 3 no-route message is missing.",
        "act3NoRouteMessage")

print("searchBossMapPageValidation=true")
