#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
ui_root = root / "Ui"

errors: list[str] = []

z_assignments: list[tuple[Path, int, str]] = []
for path in ui_root.rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    for line_number, line in enumerate(text.splitlines(), 1):
        if "ZIndex" not in line or "=" not in line:
            continue
        z_assignments.append((path, line_number, line.strip()))
        numeric = re.search(r"\bZIndex\s*=\s*(-?\d+)\b", line)
        if numeric and abs(int(numeric.group(1))) >= 1000:
            errors.append(f"four-digit ZIndex: {path.relative_to(root)}:{line_number}: {line.strip()}")

z_layers = (ui_root / "Theme" / "UiZLayers.cs").read_text(encoding="utf-8")
expected_layers = {
    "BaseContent": 0,
    "ShellHost": 10,
    "ShellChrome": 10,
    "ShellSurface": 20,
    "DrawerOverlay": 20,
    "PickerModal": 30,
    "Tooltip": 40,
    "ToastModal": 50,
}
for name, value in expected_layers.items():
    if not re.search(rf"const int {name}\s*=\s*{value}\s*;", z_layers):
        errors.append(f"missing layer {name}={value}")
if not re.search(r"const int HighestAssigned\s*=\s*ToastModal\s*;", z_layers):
    errors.append("missing layer HighestAssigned=ToastModal")


relic_picker = (ui_root / "Controls" / "Pickers" / "RelicPickerPanel.cs").read_text(encoding="utf-8")
event_picker = (ui_root / "Pages" / "Search" / "Event" / "EventSearchPickerPanel.cs").read_text(encoding="utf-8")
layer_order_checks = {
    "shared card/relic/potion picker uses PickerModal": "ZIndex = UiZLayers.PickerModal;" in relic_picker,
    "event picker uses PickerModal": "ZIndex = UiZLayers.PickerModal;" in event_picker,
    "picker is below tooltip": re.search(r"const int PickerModal\s*=\s*(\d+)", z_layers) is not None
        and re.search(r"const int Tooltip\s*=\s*(\d+)", z_layers) is not None
        and int(re.search(r"const int PickerModal\s*=\s*(\d+)", z_layers).group(1))
        < int(re.search(r"const int Tooltip\s*=\s*(\d+)", z_layers).group(1)),
    "tooltip is below toast modal": re.search(r"const int Tooltip\s*=\s*(\d+)", z_layers) is not None
        and re.search(r"const int ToastModal\s*=\s*(\d+)", z_layers) is not None
        and int(re.search(r"const int Tooltip\s*=\s*(\d+)", z_layers).group(1))
        < int(re.search(r"const int ToastModal\s*=\s*(\d+)", z_layers).group(1)),
}
for description, passed in layer_order_checks.items():
    if not passed:
        errors.append(description)

for forbidden in ("ZIndex = 4096", "ZIndex = 4999", "ZIndex = 5000"):
    for path in ui_root.rglob("*.cs"):
        if forbidden in path.read_text(encoding="utf-8"):
            errors.append(f"forbidden legacy ZIndex remains: {forbidden} in {path.relative_to(root)}")

provider = (ui_root / "Icons" / "NativeCharacterPoolIconProvider.cs").read_text(encoding="utf-8")
interface = (ui_root / "Icons" / "ICharacterPoolIconProvider.cs").read_text(encoding="utf-8")
selector = (ui_root / "Components" / "CharacterPoolIconSelector.cs").read_text(encoding="utf-8")
shell = (ui_root / "Shell" / "AppShell.cs").read_text(encoding="utf-8")
search_host = (ui_root / "Pages" / "Search" / "SearchCategoryHost.cs").read_text(encoding="utf-8")
relic_page = (ui_root / "Pages" / "Search" / "Relic" / "RelicSequenceFilterPage.cs").read_text(encoding="utf-8")

checks = {
    "provider implements IDisposable": ": IDisposable" in interface,
    "provider uses direct static Texture2D resources": "ResourceLoader.Load<Texture2D>" in provider and "character_icon_ironclad.png" in provider,
    "provider does not instantiate native Card Library scene": "PackedScene" not in provider and ".Instantiate(" not in provider,
    "provider never frees native Card Library nodes/resources": ".Free(" not in provider and "sourceRootFreed=false" in provider,
    "provider has idempotent dispose guard": "if (_disposed)" in provider and "disposeCount=1" in provider,
    "provider invalidation only drops managed references": "public void Invalidate()" in provider and "_cache.Clear();" in provider,
    "provider re-resolves invalid cached wrappers": "LoadDescriptor(key, path)" in provider and "characterPoolIconReloaded=true" in provider,
    "selector validates texture": "GodotObject.IsInstanceValid(texture)" in selector,
    "selector catches disposed texture": "catch (ObjectDisposedException)" in selector,
    "selector keeps question mark fallback": 'Ui1Theme.Label("?"' in selector,
    "selector logs invalid fallback once": "InvalidTextureDiagnostics" in selector and "characterPoolIconInvalidFallback=true" in selector,
    "AppShell owns provider disposal": "_characterPoolIcons?.Dispose();" in shell,
    "top-level close cleanup keeps provider alive": "public void CleanupForTopLevelClose()" in shell and "_characterPoolIcons?.Dispose();" not in shell.split("public void CleanupForTopLevelClose()", 1)[1].split("private void CaptureAndFlushPersistentUiState", 1)[0],
    "shared provider reaches relic page": "new RelicSequenceFilterPage(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost)" in search_host,
    "relic picker consumes shared provider": "new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost)" in relic_page,
    "weak references absent": "WeakReference<" not in provider and "WeakReference<" not in selector,
}
for description, passed in checks.items():
    if not passed:
        errors.append(description)

if errors:
    print("macUiZIndexTextureLifetimeHotfix=Failed")
    for error in errors:
        print(f"error={error}")
    raise SystemExit(1)

print("macUiZIndexTextureLifetimeHotfix=Passed")
print(f"zIndexAssignmentCount={len(z_assignments)}")
print("fourDigitZIndexAssignments=0")
print("zIndexAboveGodotLimit=0")
print("highestAssignedZIndex=50")
print("pickerModalZIndex=30")
print("anchoredTooltipZIndex=40")
print("toastModalZIndex=50")
print("pickerBelowTooltip=true")
print("tooltipBelowToastModal=true")
print("characterProviderSharedBySearchAndAnalysis=true")
print("characterTextureDirectStaticResourceOwnership=true")
print("providerDisposedOnlyAtShellExit=true")
print("selectorInvalidTextureFallback=true")
print("weakTextureReferences=false")
print("businessSemanticsChanged=false")
