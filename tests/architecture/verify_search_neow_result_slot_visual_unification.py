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


shared = read("Ui/Pages/Search/Neow/SearchHorizontalResultSlot.cs")
card = read("Ui/Pages/Search/Neow/SearchCardResultSlot.cs")
relic = read("Ui/Pages/Search/Neow/NeowModelKeySlot.cs")
potion = read("Ui/Pages/Search/Neow/SearchPotionResultSlot.cs")
host = read("Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs")

require("SlotWidth = 196f" in shared and "SlotHeight = 64f" in shared and "IconSize = 40f" in shared,
        "Neow result slots do not share the required horizontal geometry.",
        "searchNeowResultSlotSharedGeometry")
require("class SearchCardResultSlot : SearchHorizontalResultSlot" in card and
        "class NeowModelKeySlot : SearchHorizontalResultSlot" in relic and
        "class SearchPotionResultSlot : SearchHorizontalResultSlot" in potion,
        "Card, relic/curse and potion slots do not share one visual component.",
        "searchNeowCardRelicPotionSharedChrome")
require("ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize" in shared and
        "StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered" in shared and
        "ClipContents = true" in shared,
        "Result textures can still determine or overflow slot geometry.",
        "searchNeowResultTextureBounded")
require("var row = new HBoxContainer" in shared and
        "_name.ClipText = true" in shared and
        "margin_right\", 44" in shared,
        "Horizontal icon/name layout does not reserve space for the clear overlay.",
        "searchNeowResultHorizontalTextLayout")
require("_clear.SetAnchorsPreset(Control.LayoutPreset.TopRight)" in shared and
        "ClearInset = 8f" in shared and
        "ClearButtonSize = 24f" in shared and
        "Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Secondary)" in shared,
        "Clear button is not an inset independent top-right control.",
        "searchNeowResultClearTopRightInset")
require("row.AddChild(_clear)" not in shared and "_slotHost.AddChild(_clear)" in shared,
        "Clear button still participates in the horizontal content row.",
        "searchNeowResultClearOverlayNotHBox")
require("_clear.Visible = false" in shared and "_clear.Visible = true" in shared,
        "Empty and selected clear-button states are not explicit.",
        "searchNeowResultEmptySelectedStable")
require("Button.Icon" not in shared and "_body.Icon" not in shared,
        "Result slots still use natural-size Button.Icon rendering.",
        "searchNeowResultNoButtonIcon")
require("NeowStructuredOutputKind.Card => new SearchCardResultSlot" in host and
        "NeowStructuredOutputKind.Potion => new SearchPotionResultSlot" in host and
        "_ => new NeowModelKeySlot" in host,
        "Neow output kinds no longer route through the unified wrappers.",
        "searchNeowResultKindsUseUnifiedWrappers")

expected_hashes = {
    "Search/Contracts/SearchContracts.cs": "ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457",
    "Search/Compilation/SearchExecutionRequestFactory.cs": "5a6d3f8298404d6a002184012c67f98b5678ee07b0a10507cfdf07b4e915f56f",
    "Search/Runtime/ProductionQueryValidator.cs": "e174e5b05f8393af6600540c0e065bb0b714a17db0eaee9f31943ff478e2223d",
    "Core/Effects/NeowEffectProjectionEngine.cs": "5957f21d33cff154f63130883207f19640874407b254ce32ea895ad086e46ac0",
    "Ui/Controllers/SearchPageController.cs": "b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5",
}
for file, expected in expected_hashes.items():
    require(sha(file) == expected, f"Frozen business file changed: {file}", "frozen_" + Path(file).stem)

print("searchNeowResultSlotVisualUnificationValidation=true")
