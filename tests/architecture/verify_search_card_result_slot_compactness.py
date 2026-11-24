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


card_slot = read("Ui/Pages/Search/Neow/SearchCardResultSlot.cs")
shared_slot = read("Ui/Pages/Search/Neow/SearchHorizontalResultSlot.cs")
slot_contract = read("Ui/Pages/Search/Neow/INeowResultSlot.cs")
host = read("Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs")
master = read("Ui/Pages/Search/Neow/NeowRouteSelectionPanel.cs")
master_tile = read("Ui/Pages/Search/Neow/NeowRouteRelicTile.cs")
page = read("Ui/Pages/Search/Neow/NeowFilterPage.cs")

require("class SearchCardResultSlot : SearchHorizontalResultSlot" in card_slot and
        "SlotWidth = 196f" in shared_slot and "SlotHeight = 64f" in shared_slot,
        "Card result slot does not use the unified fixed-height contract.",
        "searchCardSlotFixedCompactSize")
require("IconSize = 40f" in shared_slot,
        "Card thumbnail region is not constrained.",
        "searchCardSlotThumbnailBounded")
require("ClipContents = true" in shared_slot and
        "ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize" in shared_slot and
        "StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered" in shared_slot,
        "Card texture can still determine or overflow layout size.",
        "searchCardTextureCannotExpandLayout")
require("_slotHost" in shared_slot and "RefreshVisual" in shared_slot and
        "_missing.Text = \"＋\"" in shared_slot and "_clear.Visible = true" in shared_slot,
        "Selected and empty states do not share one fixed slot surface.",
        "searchCardSelectedAndEmptySameGeometry")
require("Button.Icon" not in shared_slot and "_body.Icon" not in shared_slot,
        "Compact card slot still uses Button.Icon natural sizing.",
        "searchCardSlotNoLargeButtonIcon")
require("SearchCardResultSlot" in host and
        "NeowStructuredOutputKind.Card => new SearchCardResultSlot" in host,
        "Generic card effects do not use the dedicated compact slot.",
        "searchCardEffectsUseCompactSlot")
require("private readonly SearchCardResultSlot _uncommon" in host and
        "private readonly SearchCardResultSlot _commonA" in host and
        "private readonly SearchCardResultSlot _commonB" in host,
        "ScrollBoxes does not use the same compact card slot standard.",
        "searchScrollBoxesUsesCompactSlots")
require("Control.SizeFlags.ShrinkBegin" in host,
        "Small card result groups can still stretch to fill the detail pane.",
        "searchCardGroupsDoNotAutoExpand")
require("CustomMinimumSize = new Vector2(50, 50)" in master_tile and
        "new Vector2(38, 38)" in master_tile,
        "Neow route tiles are not compact icon-first controls.",
        "searchNeowRouteTileCompact")
require("Columns = 5" in master and "UpdateGridColumns" in master and
        "GridWidthForColumns(5)" in master and "GridWidthForColumns(4)" in master,
        "Neow route grid is not adaptive across five, four and three columns.",
        "searchNeowRouteGridAdaptiveFiveFourThree")
require("HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled" in master,
        "Neow route grid can introduce horizontal scrolling.",
        "searchNeowRouteGridNoHorizontalScroll")
require("WideMasterPanelWidth = 290f" in page and "new Vector2(WideMasterPanelWidth, 0)" in page,
        "Master pane was not compacted to return width to effect editing.",
        "searchNeowMasterPaneCompactWidth")
require("ModelKey" in slot_contract and "LocalizedName" not in slot_contract,
        "Result-slot contract stores display identity instead of ModelKey.",
        "searchCardSlotStableModelKeyOnly")

expected_hashes = {
    "Search/Contracts/SearchContracts.cs": "4861a35346d95a6b43d0e6534b5188cb6364c7fbcc71a65aef915fbc5871fb11",
    "Search/Compilation/SearchExecutionRequestFactory.cs": "74011a50989f72a412d10d2d48ebd9ebdfced9d58712ea425e5d32eadacc7754",
    "Search/Runtime/ProductionQueryValidator.cs": "89ed8426341479a0991ce8068d4a530c61b5a2d01f435b1547ba11977b6c714c",
    "Core/Effects/NeowEffectProjectionEngine.cs": "3783e1dae86619a4a7870dd9a94099fa40d3ad560cc28402b59d403c8682a355",
    "Ui/Controllers/SearchPageController.cs": "61c8ccc842916f02104c9e0064d13a26253d79039366780df3452aa96e00ec71",
}
for file, expected in expected_hashes.items():
    require(sha(file) == expected, f"Frozen business file changed: {file}", "frozen_" + Path(file).stem)

print("searchCardResultSlotCompactnessValidation=true")
