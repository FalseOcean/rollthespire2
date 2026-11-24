#!/usr/bin/env python3
"""Static regression gate for the Neow structured authoring cleanup."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]

def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")

def sha(rel: str) -> str:
    return hashlib.sha256((ROOT / rel).read_bytes()).hexdigest()

def require(ok: bool, code: str) -> None:
    print(("PASS" if ok else "FAIL") + ": " + code)
    if not ok:
        raise SystemExit(1)

page = read("Ui/Pages/Search/Neow/NeowFilterPage.cs")
master = read("Ui/Pages/Search/Neow/NeowRouteSelectionPanel.cs")
host = read("Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs")
keys = read("Presentation/Localization/Ui1TextKey.cs")
zh = json.loads(read("Localization/rt2_ui/zh.json"))
en = json.loads(read("Localization/rt2_ui/en.json"))

# Compile shape: the detail-side Bones header directly references the semantic order enum.
require("using RolltheSpire2.Search.Semantics;" in host,
        "bones_order_enum_namespace_imported")

# Bones: master is identity-only; detail is the only order visual authority.
require("SearchNeowBonesAnyOrder" not in master and "SearchNeowBonesExactOrder" not in master,
        "bones_left_order_controls_removed")
require('string badge = selected ? "✓" : string.Empty;' in master and 'badge = (i + 1)' not in master,
        "bones_left_order_badges_removed")
require("SearchNeowBonesOrderedPickup" in host and "BonesOrderModeChanged" in host,
        "bones_order_toggle_in_detail_header")
require("_bonesSwapOrder.Disabled" in host and "BonesRouteOrderMode.ExactOrder" in host,
        "bones_swap_disabled_when_unordered")
require("(_bonesSelected[0], _bonesSelected[1]) = (_bonesSelected[1], _bonesSelected[0]);" in master and
        "_bonesOrderMode != BonesRouteOrderMode.ExactOrder" in master,
        "bones_swap_reorders_canonical_ui_list")
require("resultOrdinal = _activeBonesOrderMode == BonesRouteOrderMode.ExactOrder ? i + 1 : null" in host and
        'resultOrdinal.Value == 1 ? "①" : "②"' in host,
        "bones_ordinals_only_when_ordered")
require("CaptureActiveHistory();" in host and "Dictionary<ModelKey, RelicEffectDraftState> _history" in host and
        "BuildRelicCard(_activeBonesRelics[i], childCard: true, resultOrdinal)" in host,
        "bones_nested_state_follows_relic_identity")

# Kaleidoscope: title-row toggle/action; swap actual selected targets; ordinals are dynamic.
require("SearchNeowKaleidoscopeOrderedResults" in host and "HeaderControls = new Control[] { _orderedToggle, _swapOrder };" in host,
        "kale_order_controls_in_header")
require("ModelKey? first = _slots[0].SelectedKey;" in host and
        "_slots[0].Select(second, notify: false);" in host and
        "_slots[1].Select(first, notify: false);" in host,
        "kale_swap_reorders_actual_targets")
require("foreach (Label label in _orderLabels) label.Visible = ordered;" in host,
        "kale_ordinals_hidden_when_unordered")
require("KaleidoscopeGroupOrder: _definition.SupportsOrderSelection" in host and
        "condition.KaleidoscopeGroupOrder" in host,
        "kale_canonical_order_contract_preserved")

# Scroll Boxes: mode is exposed as card-level header controls; condition semantics remain unchanged.
require("HeaderControls = new Control[] { _normalMode, _tripleMode };" in host and
        "SearchNeowScrollNormal" in host and "SearchNeowScrollTripleClaw" in host,
        "scroll_mode_controls_in_header")
require("NeowStructuredConditionKind.SpecialOffer" in host and
        "NeowSpecialOfferKind.ScrollBoxesTripleClaw" in host and
        "NeowStructuredConditionKind.StructuredCardComposition" in host,
        "scroll_existing_modes_preserved")
require("_normalBody.Visible = _mode == 0;" in host and "_specialBody.Visible = _mode == 1;" in host,
        "scroll_body_switches_with_mode")

# Clear behavior: no player-facing global nested reset; each structured card owns a local clear.
require("SearchNeowClearAllEffects" not in page and "BuildClearConfirmationOverlay" not in page and "_clearAllEffects" not in page,
        "global_nested_effect_reset_removed")
require("ClearBonesConditions" not in host,
        "bones_header_clear_condition_removed")
require("ClearRelicEffect" in host and "SearchNeowClearEffect" in host,
        "structured_relic_local_clear_condition_actions_present")

# Copy: touched player UI does not expose the retired implementation model.
retired = ["Offer Group", "Bundle", "自动验证", "合法取得顺序", "客观效果输出", "路线遗物身份", "清空所有效果", "精确顺序", "任意顺序"]
visible_text = "\n".join(str(v) for k,v in zh.items() if k.startswith("ui1.search.neow.")) + "\n" + \
               "\n".join(str(v) for k,v in en.items() if k.startswith("ui1.search.neow."))
require(not any(term in visible_text for term in retired), "legacy_neow_authoring_copy_removed")
for k in [
    "ui1.search.neow.bones.ordered_pickup",
    "ui1.search.neow.effect.kaleidoscope_ordered_results",
    "ui1.search.neow.swap_order",
    "ui1.search.neow.clear_effect",
    "ui1.search.neow.scroll.triple_claw_description",
]:
    require(k in zh and k in en, "localized:" + k)

# Canonical files are pinned to the current ordered-semantics closeout state; files not intentionally changed retain the prior baseline digest.
expected = {
    "Ui/Pages/Search/SearchDraft.cs": "4d3516dacfeb17cad50368d102c430ee20db90b14946cfbfb767cc1de47b27a3",
    "Search/Semantics/SearchSemanticContracts.cs": "da4b8e902a9a71d3ff034b3de2c91a7017894ca204b6ad82f98aa2b8589de749",
    "Search/Semantics/SearchCompiler.cs": "2d245bec2e576b244dfbde6146886e02aafcb09d1990bb612986a856c30adcd3",
    "Search/Contracts/SearchContracts.cs": "ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457",
    "Ui/Controllers/SearchPageController.cs": "b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5",
    "Ui/Persistence/SearchWorkspacePersistence.cs": "6b1b02dc951c89230c52f78180e88599ba00022f7b5be4f4066d7ec4e98a6ddd",
}
for rel, digest in expected.items():
    require(sha(rel) == digest, "baseline_unchanged:" + rel)

require("RestoreSelection(restored.RouteRelicKey, restored.RequiredBonesRelics, restored.BonesOrderMode" in page and
        "RestoreConditions(restored.EffectConditions" in page,
        "restore_path_preserves_order_and_nested_conditions")
require("condition.KaleidoscopeGroupOrder" in host and "scroll.Mode == 1" in host,
        "editor_restore_preserves_kale_and_scroll_modes")

print("SearchNeowStructuredAuthoringCleanupValidation=PASS")
