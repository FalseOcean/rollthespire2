from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
read = lambda p: (ROOT / p).read_text(encoding='utf-8')

shell = read('Ui/Shell/AppShell.cs')
page_host = read('Ui/Shell/PageHost.cs')
contract = read('Ui/Pages/IPageSurfaceLifecycle.cs')
search = read('Ui/Pages/Search/SearchPage.cs')
category = read('Ui/Pages/Search/SearchCategoryHost.cs')
lib = read('Ui/Components/SearchPresetLibraryOverlay.cs')
save_surface = read('Ui/Components/SearchPresetSaveTransactionOverlay.cs')
confirm_surface = read('Ui/Components/SearchConfirmationTransactionOverlay.cs')
z = read('Ui/Theme/UiZLayers.cs')
neow = read('Ui/Pages/Search/Neow/NeowFilterPage.cs')
relic = read('Ui/Pages/Search/Relic/RelicSequenceFilterPage.cs')
event = read('Ui/Pages/Search/Event/EventSequenceFilterPage.cs')
reward = read('Ui/Pages/Search/CombatReward/CombatRewardFilterPage.cs')
ancient = read('Ui/Pages/Search/Ancient/AncientFilterPage.cs')

def check(name, cond):
    print(('PASS' if cond else 'FAIL') + ': ' + name)
    if not cond:
        raise AssertionError(name)

check('narrow page lifecycle contract exists', 'interface IPageSurfaceLifecycle' in contract)
check('Search owns lifecycle', 'IPageSurfaceLifecycle' in search.split('{', 1)[0])
check('PageHost exposes active page', 'public IAppPage? ActivePage => _active;' in page_host)
check('Page switch cancels outgoing transient first', 'outgoingLifecycle.CancelTransientSurfacesForPageSwitch();' in shell and shell.index('outgoingLifecycle.CancelTransientSurfacesForPageSwitch();') < shell.index('IAppPage page = _pageHost!.Activate(key);'))
check('Page switch releases hidden focus', '_pageHost.ReleaseFocusFromActivePage();' in shell)
check('AppShell Escape delegates one page layer first', 'lifecycle.TryHandleEscape()' in shell and 'TopLevelCloseRequested?.Invoke();' in shell)
esc = shell.split('public override void _UnhandledKeyInput', 1)[1].split('public override void _ExitTree', 1)[0]
check('final top-level close occurs only after page decline', esc.index('lifecycle.TryHandleEscape()') < esc.index('TopLevelCloseRequested?.Invoke();'))

check('Preset Library is workspace layer', 'ZIndex = UiZLayers.WorkspaceOverlay;' in lib)
check('Preset Workspace backdrop blocks without outside-click dismiss', 'MouseFilter = MouseFilterEnum.Stop' in lib and 'backdrop.GuiInput' not in lib)
check('Save is Control transaction not Window', 'class SearchPresetSaveTransactionOverlay : Control' in save_surface)
check('Confirmation is Control transaction not Window', 'class SearchConfirmationTransactionOverlay : Control' in confirm_surface)
check('surface z hierarchy declared', all(x in z for x in ['WorkspaceOverlay = 24', 'TransactionModal = 29', 'PickerModal = 30', 'ConfirmationModal = 31']))
check('default save path uses Control replacement', 'UseLegacyPresetWindowFallback' not in search and '_presetSaveSurface.OpenCreate(' in search)
check('save transaction stays under child picker', 'ZIndex = UiZLayers.TransactionModal;' in save_surface and '_presetVisualIconPicker.Open(request);' in search)
check('retired Window fallback removed', 'ConfirmationDialog' not in search and 'ResumePresetSaveDialogAfterVisualPicker' not in search)

try_start = search.index('public bool TryHandleEscape()')
try_end = search.index('public void ResetSurfacesForTopLevelClose()', try_start)
esc_search = search[try_start:try_end]
ordered = [
    '_presetVisualIconPicker.IsOpen',
    '_presetOverwriteSurface.IsOpen',
    '_presetDeleteSurface.IsOpen',
    '_presetSaveSurface.IsOpen',
    '_presetLibrary.IsOpen',
    '_categoryHost.TryCancelSelectedTransientSurface()'
]
pos = [esc_search.index(x) for x in ordered]
check('Search Escape order picker transaction workspace page-local', pos == sorted(pos))

cancel_start = search.index('private void CancelTransientSurfaces(bool preservePresetWorkspace')
cancel_end = search.index('private void ResetMetrics()', cancel_start)
cancel = search[cancel_start:cancel_end]
check('Page switch suppresses child focus return', '_suppressPresetChildReturn = true;' in cancel and '_suppressPresetChildReturn = false;' in cancel)
check('Page switch cancels picker/save/confirm transactions', all(x in cancel for x in ['_presetVisualIconPicker.Cancel()', '_presetOverwriteSurface.Cancel()', '_presetDeleteSurface.Cancel()', '_presetSaveSurface.Cancel()']))
check('Page switch preserves workspace by contract', 'if (!preservePresetWorkspace && _presetLibrary.IsOpen)' in cancel)
check('Top-level close may close workspace', 'preservePresetWorkspace: false' in search)
check('AppShell top-level cleanup resets all cached lifecycle pages', '_pageHost.CachedPages.OfType<IPageSurfaceLifecycle>()' in shell)

check('Category switch cancels outgoing transient', 'TryCancelSelectedTransientSurface();' in category)
check('Neow picker now exposes cancellation', 'public bool TryCancelTransientSurface()' in neow and '_picker.Cancel();' in neow)
check('Relic picker exposes cancellation', 'public bool TryCancelTransientSurface()' in relic and '_picker.Cancel();' in relic)
check('Event picker exposes cancellation', 'public bool TryCancelTransientSurface()' in event and '_picker.Cancel();' in event)
check('Reward picker exposes cancellation', 'public bool TryCancelTransientSurface()' in reward and '_picker.Cancel();' in reward)
check('Ancient confirmation exposes cancellation', 'public bool TryCancelTransientSurface()' in ancient and 'CloseClearConfirmation();' in ancient)

# A2 does not alter the A1 top-level guard contract.
top = read('Ui/Shell/Rt2TopLevelSubmenu.cs')
check('A1 single Pop guard unchanged', top.count('_stack.Pop();') == 1 and 'ReferenceEquals(_stack.Peek(), this)' in top)

# Pointer ownership must follow SceneTree sibling order. Godot ZIndex changes
# drawing order but not Control input processing order, so the page content must
# be added before every page-local overlay surface.
content_add = search.index('AddChild(pageLayout);')
surface_adds = [
    "AddChild(_presetLibrary);",
    "AddChild(_presetSaveSurface);",
    "AddChild(_presetVisualIconPicker);",
    "AddChild(_presetOverwriteSurface);",
    "AddChild(_presetDeleteSurface);",
]
check('page content is physically below page-local Control surfaces', all(search.index(x) > content_add for x in surface_adds))
check('page-local Control sibling order matches workspace transaction picker confirmation', [search.index(x) for x in surface_adds] == sorted(search.index(x) for x in surface_adds))

print('PASS: UI Surface A2 internal lifecycle gate')
