#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")

def require(value: bool, message: str, code: str) -> None:
    if not value:
        raise AssertionError(message)
    print(f"{code}=true")

geometry = read("Ui/Pages/Search/Ancient/AncientRowGeometry.cs")
identity = read("Ui/Pages/Search/Ancient/AncientIdentityButton.cs")
option = read("Ui/Pages/Search/Ancient/AncientOptionIconButton.cs")
card = read("Ui/Pages/Search/Ancient/AncientConditionRow.cs")

require("HeaderHeight = 44f" in geometry and "PortraitSize = 38f" in geometry and
        "OptionCellSize = 41f" in geometry and "OptionCellInset = 2f" in geometry,
        "Ancient card prototype geometry is not shared/compact.", "searchAncientSharedCardGeometry")
require("AncientRowGeometry.HeaderHeight" in identity and "AncientRowGeometry.PortraitSize" in identity and
        "AncientRowGeometry.OptionCellSize" in option,
        "Identity header and option cells do not use shared geometry.", "searchAncientHeaderOptionGeometry")
require("iconHost.OffsetLeft = AncientRowGeometry.OptionCellInset" in option and
        "iconHost.OffsetRight = -AncientRowGeometry.OptionCellInset" in option,
        "Option icon chrome is not compact around the game icon.", "searchAncientOptionIconChromeCompact")
require("name.AutowrapMode = TextServer.AutowrapMode.Off" in identity and "name.ClipText = true" in identity and
        "name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis" in identity,
        "Ancient names can wrap and grow card headers.", "searchAncientIdentityNameSingleLineEllipsis")
require("Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card" in card and "var options = new GridContainer" in card,
        "Ancient row was not converted into a card with a dedicated option grid.", "searchAncientCardSurfaceAndGrid")
require('selected ? "✓"' in identity and 'selected ? "✓"' in option and
        "Ui1ButtonRole.NavigationSelected" in identity and "Ui1ButtonRole.NavigationSelected" in option,
        "Selected badges/chrome were lost.", "searchAncientSelectedStateGeometryStable")

require("ToggleMode = true" in option and "SetPressedNoSignal(selected)" in option,
        "Selected option visual state is not restored through the native Button pressed state.",
        "searchAncientSelectedPressedStateRestored")

print("searchAncientRowHeightAlignmentPolishValidation=true")
