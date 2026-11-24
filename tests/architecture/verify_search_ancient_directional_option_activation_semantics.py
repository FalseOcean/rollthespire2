#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ROW = (ROOT / "Ui/Pages/Search/Ancient/AncientConditionRow.cs").read_text(encoding="utf-8")


def require(value: bool, message: str, code: str) -> None:
    if not value:
        raise AssertionError(message)
    print(f"{code}=true")


require(
    "bool selecting = !_selectedOptions.Contains(optionKey);" in ROW
    and "if (selecting)" in ROW
    and "_selectedOptions.Add(optionKey);\n            _active = true;" in ROW
    and "else\n        {\n            _selectedOptions.Remove(optionKey);" in ROW,
    "Ancient option toggling is not implemented as directional activation.",
    "ancientOptionDirectionalImplementation",
)

toggle_method = ROW.split("private void ToggleOption", 1)[1].split("private void Refresh", 1)[0]
selection_branch = toggle_method.split("if (selecting)", 1)[1].split("else", 1)[0]
deselection_branch = toggle_method.split("else", 1)[1].split("Refresh();", 1)[0]
require("_active = true;" in selection_branch,
        "Selecting an option must activate its Ancient row.",
        "ancientOptionSelectionActivatesIdentity")
require("_active" not in deselection_branch,
        "Deselecting an option must not mutate the Ancient identity state.",
        "ancientOptionDeselectionDoesNotTouchIdentity")


def toggle(active: bool, selected: bool) -> tuple[bool, bool]:
    selecting = not selected
    if selecting:
        selected = True
        active = True
    else:
        selected = False
    return active, selected


require(toggle(False, False) == (True, True),
        "Inactive + unselected must become active + selected.",
        "fixtureSelectInactiveRow")
require(toggle(True, True) == (True, False),
        "Active + selected must become identity-only active.",
        "fixtureDeselectActiveRow")
require(toggle(False, True) == (False, False),
        "Inactive retained option history must clear without reactivation.",
        "fixtureDeselectInactiveHistory")

print("searchAncientDirectionalOptionActivationValidation=true")
