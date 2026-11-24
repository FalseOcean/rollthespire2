#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding="utf-8")
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f"{name}=true")

page = read("Ui/Pages/Search/SearchPage.cs")
nav = read("Ui/Pages/Search/SearchCategoryNavigationBar.cs")
context = read("Ui/Pages/Search/SearchContextBar.cs")
overlay = read("Ui/Components/SearchPresetLibraryOverlay.cs")
en = json.loads(read("Localization/rt2_ui/en.json"))
zh = json.loads(read("Localization/rt2_ui/zh.json"))

req("titleRowUsesExistingVerticalSlot", "center.AddChild(titleRow);" in page and "center.AddChild(_pageTitle);" not in page)
req("titleExpandsBeforeActions", "_pageTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;" in page)
req("titleRowOrder", page.index("titleRow.AddChild(_pageTitle);") < page.index("titleRow.AddChild(_savePresetAction);") < page.index("titleRow.AddChild(_loadPresetAction);"))
req("saveAndLoadAreExplicit", "_savePresetAction.Pressed += () => ShowSavePresetDialog(_knownPresets);" in page and "_loadPresetAction.Pressed += () => PresetWorkspaceRequested?.Invoke();" in page)
req("domainNavigationNoPresetButtons", all(token not in nav for token in ["_savePreset", "_loadPreset", "SavePresetRequested", "LoadPresetRequested"]))
req("domainNavigationRetainsClear", "_clearConditions" in nav and "ClearConditionsRequested" in nav)
req("contextRemainsCharacterAscensionOnly", "PresetWorkspaceRequested" not in context and "_presetWorkspace" not in context)
req("workspaceHeaderHasNoSaveCurrent", "_saveCurrent" not in overlay and "SaveCurrentRequested" not in overlay)
req("saveLoadRemainAvailableWhileRunning", "_savePresetAction.Disabled = false;" in page and "_loadPresetAction.Disabled = false;" in page)
req("localizationStable", zh.get("ui1.search.preset.save") == "保存为预设" and zh.get("ui1.search.preset.workspace_entry") == "加载预设" and en.get("ui1.search.preset.save") == "Save as Preset" and en.get("ui1.search.preset.workspace_entry") == "Load Preset")

print("presetPhaseBOwnerUiTitleRowHotfix2=true")
