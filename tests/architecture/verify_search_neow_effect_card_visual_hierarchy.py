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


panel = read("Ui/Controls/Pickers/RelicPickerPanel.cs")
request = read("Ui/Controls/Pickers/RelicPickerRequest.cs")
tile = read("Ui/Controls/Pickers/RelicPickerTile.cs")
master = read("Ui/Pages/Search/Neow/NeowRouteSelectionPanel.cs")
master_tile = read("Ui/Pages/Search/Neow/NeowRouteRelicTile.cs")
slot = read("Ui/Pages/Search/Neow/NeowModelKeySlot.cs")
host = read("Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs")
page = read("Ui/Pages/Search/Neow/NeowFilterPage.cs")
catalog = read("Ui/Pages/Search/Neow/NeowSearchUiCatalog.cs")
search_page = read("Ui/Pages/Search/SearchPage.cs")
zh = json.loads(read("Localization/rt2_ui/zh.json"))
en = json.loads(read("Localization/rt2_ui/en.json"))

require("NeowRouteSelectionPanel" in page and "NeowRouteEffectCardHost" in page,
        "Neow page is not a master-detail layout.", "neowMasterDetailLayout")
require("HBoxContainer" in page and "VBoxContainer" in page and "SetCompact" in page,
        "Master-detail layout does not switch to a stacked compact layout.", "neowMasterDetailResponsive")
require("BaseGameModelKeys.Relics.NeowsBones" in master and
        "key != BaseGameModelKeys.Relics.NeowsBones" in master,
        "Bones is not a dedicated master control outside the normal grid.", "bonesDedicatedMasterEntry")
require("GridContainer" in master and "LineEdit" not in master and "RebuildGrid" in master,
        "Normal Neow routes are not shown in the compact icon-only grid.", "neowRouteCompactIconGrid")
require("_normalSelected = _normalSelected == key ? null : key" in master,
        "Normal route selection is not toggleable single-select.", "normalRouteToggleSingleSelect")
require("_bonesSelected.Count < 2" in master and "pairFull" in master,
        "Bones mode is not an explicit maximum-two selection.", "bonesMaximumTwoSelection")
require("_bonesSelected" in master and "ActiveRouteRelic" in master,
        "Master selection does not retain stable ModelKey route state.", "masterUsesStableModelKeys")
require("LocalizedName" in tile and "Texture" in tile and "candidate.IsSelected" in tile,
        "Shared result picker tiles lack icon, name, or selection state.", "resultPickerVisualIdentity")
require("ContentKind" in request and "IconVariant" in request and
        "_request.ContentKind" in panel,
        "Result picker is still relic-only instead of typed ModelKey UI.", "typedResultPickerPanel")
require("MenuButton" not in slot and "PopupMenu" not in slot and "RelicPickerRequest" in slot,
        "Result slots still use narrow PopupMenu input.", "resultSlotPopupRemoved")
require("BuildBonesCard" in host and "var children = new VBoxContainer" in host,
        "Bones child effect cards are not vertically owned by the parent card.", "bonesVerticalChildCards")
bones_body = host[host.index("private Control BuildBonesCard") : host.index("private Control BuildRelicCard")]
require("HFlowContainer" not in bones_body,
        "Bones effect cards still auto-switch to a horizontal two-column layout.", "bonesNeverHorizontal")
require("BuildRelicCard(_activeBonesRelics[i], childCard: true, resultOrdinal)" in host,
        "Each selected Bones relic does not receive its own ordered-capable effect card.", "bonesRelicOwnedEffectCards")
require("Dictionary<ModelKey, RelicEffectDraftState> _history" in host and
        "CaptureActiveHistory" in host and "RestoreRelicState" in host,
        "Inactive relic effect history is not retained.", "neowEffectHistoryRetained")
require("_activeEditors" in host and "TryBuildConditions" in host,
        "Search activation is not restricted to the visible active route editors.", "onlyActiveRouteEffectsCompile")
require("SearchNeowClearAllEffects" not in page and "BuildClearConfirmationOverlay" not in page and
        "_clearAllEffects" not in page,
        "Player-facing global nested-effect clear still exists.", "globalNestedEffectClearRemoved")
require("clearAction: null" in bones_body and "ClearBonesConditions" not in host and
        "ClearRelicEffect" in host and "SearchNeowClearEffect" in host,
        "Bones must not expose a parent clear action; structured child relics must retain their own clear-condition action.",
        "perRelicClearConditions")
require("SearchNeowBonesOrderedPickup" in host and "SearchNeowSwapOrder" in host and
        "SearchNeowKaleidoscopeOrderedResults" in host and "HeaderControls" in host,
        "Structured primary controls are not hosted in card headers.", "structuredHeaderControls")
require("RelicRarity(ModelKey key)" in catalog and "EffectRelicRarity.Shop" in host,
        "Ordinary relic categories are not sourced from runtime authority.", "relicPickerCategoryAuthority")
require("AddChild(_picker)" in page and "Control.LayoutPreset.FullRect" in page,
        "Picker is not constrained to the Neow center page.", "pickerContainedInSearchCenter")
require("_categoryHost.NeowPage.SetCompact(compact)" in search_page,
        "Search responsive state does not reach the Neow master-detail page.", "searchPassesCompactToNeow")
require("RolltheSpire2.Search" not in panel + request + tile + master_tile,
        "Pure visual picker/tile components depend on Search evaluation logic.", "pickerUiDoesNotPredict")

# Pinned business-file hashes for the current ordered-semantics closeout state.
expected_hashes = {
    "Search/Contracts/SearchContracts.cs": "e4c5b052f1a6b33bc0557dfb8941cb09604cc66618bd9a02fed617a32ad13347",
    "Search/Compilation/SearchExecutionRequestFactory.cs": "5a6d3f8298404d6a002184012c67f98b5678ee07b0a10507cfdf07b4e915f56f",
    "Search/Runtime/ProductionQueryValidator.cs": "fac219676c78fb6e7a7f7d2e9b10b371a8c4bd9bcb5f5a729effb53eacd9531e",
    "Core/Effects/NeowEffectProjectionEngine.cs": "a95d98786728ea02ce66ce93aba702865b6970fd3d8d369d57b54c2d67fa8f31",
    "Ui/Controllers/SearchPageController.cs": "8ecbf387cac5b62b91c0d346276a58f1ea5c72e586b2e11dceb96996026bba85",
}
for file, expected in expected_hashes.items():
    require(sha(file) == expected, f"Frozen business file changed: {file}", "frozen_" + Path(file).stem)

for key in [
    "ui1.search.neow.master.normal_title",
    "ui1.search.neow.master.bones_count",
    "ui1.search.neow.detail.unconstrained_title",
    "ui1.search.neow.bones.ordered_pickup",
    "ui1.search.neow.effect.kaleidoscope_ordered_results",
    "ui1.search.neow.swap_order",
    "ui1.search.neow.picker.title_card",
    "ui1.search.neow.picker.title_potion",
    "ui1.search.neow.picker.title_curse",
]:
    require(key in zh and key in en, f"Missing localization: {key}", "neowUiLocalization_" + key.rsplit(".", 1)[-1])

print("searchNeowMasterDetailVisualValidation=true")
