from __future__ import annotations

import hashlib
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


slot = read("Ui/Pages/Search/Neow/SearchHorizontalResultSlot.cs")
master = read("Ui/Pages/Search/Neow/NeowRouteSelectionPanel.cs")

require("ClearInset = 8f" in slot and
        'margin_right", 44' in slot and
        "OffsetRight = -ClearInset" in slot and
        "OffsetTop = ClearInset" in slot,
        "The shared clear button does not have the corrected top/right inset.",
        "searchNeowClearButtonComfortableInset")
require("ClearButtonSize = 24f" in slot,
        "The minor alignment polish changed clear-button size semantics.",
        "searchNeowClearButtonSizePreserved")
require("RouteTileSize = 50f" in master and
        "RouteGridSeparation = 4f" in master and
        "GridWidthForColumns" in master,
        "The route selector does not derive one shared visible content width.",
        "searchNeowRouteSharedGridWidthContract")
require("_grid.CustomMinimumSize = new Vector2(alignedWidth, 0)" in master and
        "_bonesButton.CustomMinimumSize = new Vector2(alignedWidth, 82)" in master,
        "The Bones card and route grid do not receive the same aligned width.",
        "searchNeowBonesGridRightBoundaryAligned")
require("SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin" in master and
        "HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled" in master,
        "The aligned left column can still stretch or introduce horizontal scrolling.",
        "searchNeowAlignedColumnNoHorizontalOverflow")

expected_hashes = {
    "Search/Contracts/SearchContracts.cs": "ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457",
    "Search/Compilation/SearchExecutionRequestFactory.cs": "5a6d3f8298404d6a002184012c67f98b5678ee07b0a10507cfdf07b4e915f56f",
    "Search/Runtime/ProductionQueryValidator.cs": "e174e5b05f8393af6600540c0e065bb0b714a17db0eaee9f31943ff478e2223d",
    "Core/Effects/NeowEffectProjectionEngine.cs": "5957f21d33cff154f63130883207f19640874407b254ce32ea895ad086e46ac0",
    "Ui/Controllers/SearchPageController.cs": "b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5",
}
for file, expected in expected_hashes.items():
    require(sha(file) == expected, f"Frozen business file changed: {file}", "frozen_" + Path(file).stem)

print("searchNeowMinorAlignmentPolishValidation=true")
