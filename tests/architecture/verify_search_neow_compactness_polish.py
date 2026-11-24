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


master = read("Ui/Pages/Search/Neow/NeowRouteSelectionPanel.cs")
tile = read("Ui/Pages/Search/Neow/NeowRouteRelicTile.cs")
card = read("Ui/Pages/Search/Neow/SearchCardResultSlot.cs")
potion = read("Ui/Pages/Search/Neow/SearchPotionResultSlot.cs")
shared_slot = read("Ui/Pages/Search/Neow/SearchHorizontalResultSlot.cs")
host = read("Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs")
page = read("Ui/Pages/Search/Neow/NeowFilterPage.cs")
theme = read("Ui/Theme/Ui1Theme.cs")

require("LineEdit" not in master and "_search" not in master,
        "The Neow route search box is still present.",
        "searchNeowRouteSearchRemoved")
require("CustomMinimumSize = new Vector2(50, 50)" in tile and
        "CustomMinimumSize = new Vector2(38, 38)" in tile and
        "Ui1Theme.Label(displayName" not in tile,
        "Neow route tiles are not square icon-only controls.",
        "searchNeowRouteIconOnlyTiles")
require("tooltipHost.ShowFor(this, key, displayName)" in tile and "ModelKey Key" in tile,
        "Icon-only route tiles lost localized anchored tooltip or stable ModelKey identity.",
        "searchNeowRouteTooltipModelKey")
require("Columns = 5" in master and "GridWidthForColumns(5)" in master and
        "GridWidthForColumns(4)" in master and
        "HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled" in master,
        "Neow route grid does not preserve adaptive five/four/three-column behavior.",
        "searchNeowRouteAdaptiveIconGrid")
require("class SearchCardResultSlot : SearchHorizontalResultSlot" in card and
        "SlotWidth = 196f" in shared_slot and "SlotHeight = 64f" in shared_slot and
        "IconSize = 40f" in shared_slot,
        "Card result slots do not use the unified horizontal geometry.",
        "searchCardResultSlotFurtherReduced")
require("ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize" in shared_slot and
        "ClipContents = true" in shared_slot and "Button.Icon" not in shared_slot,
        "Card texture can still expand the Search layout.",
        "searchCardResultTextureBounded")
require("class SearchPotionResultSlot : SearchHorizontalResultSlot" in potion and
        "SlotWidth = 196f" in shared_slot and "SlotHeight = 64f" in shared_slot and
        "IconSize = 40f" in shared_slot and
        "ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize" in shared_slot,
        "Lost Coffer does not have a dedicated compact horizontal potion slot.",
        "searchPotionResultSlotCompact")
require("relic == BaseGameModelKeys.Relics.LostCoffer" in host and
        "var resultRow = new HFlowContainer" in host and
        "NeowStructuredOutputKind.Potion => new SearchPotionResultSlot" in host,
        "Lost Coffer card and potion results are not grouped in one wrapping row.",
        "searchLostCofferCardPotionSameRow")
require("WideMasterPanelWidth = 290f" in page and "new Vector2(WideMasterPanelWidth, 0)" in page,
        "The compact master pane did not return width to the detail pane.",
        "searchNeowMasterWidthPolished")
require("DefaultWidth = 1600f" in theme and "DefaultHeight = 900f" in theme,
        "The unified default shell size is not 1600x900.",
        "defaultShell1600x900")

expected_hashes = {
    "Search/Contracts/SearchContracts.cs": "ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457",
    "Search/Compilation/SearchExecutionRequestFactory.cs": "5a6d3f8298404d6a002184012c67f98b5678ee07b0a10507cfdf07b4e915f56f",
    "Search/Runtime/ProductionQueryValidator.cs": "e174e5b05f8393af6600540c0e065bb0b714a17db0eaee9f31943ff478e2223d",
    "Core/Effects/NeowEffectProjectionEngine.cs": "5957f21d33cff154f63130883207f19640874407b254ce32ea895ad086e46ac0",
    "Ui/Controllers/SearchPageController.cs": "b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5",
}
for file, expected in expected_hashes.items():
    require(sha(file) == expected, f"Frozen business file changed: {file}", "frozen_" + Path(file).stem)

print("searchNeowCompactnessPolishValidation=true")
