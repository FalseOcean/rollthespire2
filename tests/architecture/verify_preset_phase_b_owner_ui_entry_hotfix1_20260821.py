#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding="utf-8")
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f"{name}=true")

context = read("Ui/Pages/Search/SearchContextBar.cs")
nav = read("Ui/Pages/Search/SearchCategoryNavigationBar.cs")
page = read("Ui/Pages/Search/SearchPage.cs")
overlay = read("Ui/Components/SearchPresetLibraryOverlay.cs")
en = json.loads(read("Localization/rt2_ui/en.json"))
zh = json.loads(read("Localization/rt2_ui/zh.json"))

req("topContextPresetRemoved", "_presetWorkspace" not in context and "PresetWorkspaceRequested" not in context)
req("navHasNoPresetActions", all(x not in nav for x in ["_savePreset", "_loadPreset", "SavePresetRequested", "LoadPresetRequested"]))
req("titleRowOwnsExplicitSaveLoadButtons", all(x in page for x in ["_savePresetAction", "_loadPresetAction", "titleRow.AddChild(_savePresetAction);", "titleRow.AddChild(_loadPresetAction);"]))
req("saveOpensSaveTransaction", "_savePresetAction.Pressed += () => ShowSavePresetDialog(_knownPresets);" in page)
req("loadOpensPresetWorkspace", "_loadPresetAction.Pressed += () => PresetWorkspaceRequested?.Invoke();" in page)
req("workspaceHeaderSaveRemoved", "_saveCurrent" not in overlay and "SaveCurrentRequested" not in overlay)
req("workspaceHeaderKeepsClose", "header.AddChild(_title);" in overlay and "header.AddChild(_close);" in overlay)
req("zhCopy", zh.get("ui1.search.preset.save") == "保存为预设" and zh.get("ui1.search.preset.workspace_entry") == "加载预设")
req("enCopy", en.get("ui1.search.preset.save") == "Save as Preset" and en.get("ui1.search.preset.workspace_entry") == "Load Preset")
req("saveLoadRemainAvailableWhileRunning", "_savePresetAction.Disabled = false;" in page and "_loadPresetAction.Disabled = false;" in page)

print("presetPhaseBOwnerUiEntryHotfix1=true")
