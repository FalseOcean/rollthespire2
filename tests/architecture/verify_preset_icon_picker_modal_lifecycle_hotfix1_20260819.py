#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel):
    return (ROOT / rel).read_text(encoding='utf-8')

def req(name, ok):
    if not ok:
        raise AssertionError(name)
    print(f'{name}=true')

page = read('Ui/Pages/Search/SearchPage.cs')
picker = read('Ui/Controls/Pickers/RelicPickerPanel.cs')

# Control transactions preserve the save editor while its child picker is open.
# The former native Window suspend/resume fallback was removed by UI consolidation.
start = page.index('private void OpenPresetVisualIconPicker(int slotIndex)')
end = page.index('private void HandlePresetVisualPickerClosed()', start)
open_block = page[start:end]
close_block = page[end:page.index('public void RestorePersistedResults', end)]
req('oneControlSavePath', 'UseLegacyPresetWindowFallback' not in page and '_presetSaveSurface.OpenCreate(' in page)
req('saveEditorRemainsMounted', '_presetSaveSurface.Hide' not in open_block and '_presetVisualIconPicker.Open(request);' in open_block)
req('pickerCommitUpdatesSelectedSlot', '_presetSaveSurface.SetVisualIcon(_activePresetVisualIconSlot, key)' in open_block)
req('failedOpenRestoresFocus', '_presetSaveSurface.FocusAfterChildPicker(_activePresetVisualIconSlot);' in open_block)
req('closedPickerRestoresFocusUnlessLeavingPage', 'if (!_suppressPresetChildReturn)' in close_block and '_presetSaveSurface.FocusAfterChildPicker(_activePresetVisualIconSlot);' in close_block)

req('pickerPublishesClosedEvent', 'public event Action? Closed;' in picker)
req('presetPickerSubscribesClosed', '_presetVisualIconPicker.Closed += HandlePresetVisualPickerClosed;' in page)
req('cancelPublishesClosed', 'CloseCore();\n        Closed?.Invoke();' in picker)
commit_start = picker.index('private void Commit(ModelKey? key)')
commit_block = picker[commit_start:picker.index('private void UpdateResponsiveLayout()', commit_start)]
req('commitPublishesAfterSelection', commit_block.index('request?.OnSelected(key);') < commit_block.index('Closed?.Invoke();'))

exit_start = picker.index('public override void _ExitTree()')
exit_block = picker[exit_start:picker.index('private static HBoxContainer FilterGroup()', exit_start)]
req('treeExitDoesNotPublishClosed', 'CloseCore();' in exit_block and 'Closed?.Invoke();' not in exit_block)

print('presetIconPickerModalLifecycleHotfix1_20260819=true')
