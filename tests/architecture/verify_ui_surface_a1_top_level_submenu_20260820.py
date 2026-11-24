from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
top = (ROOT / "Ui" / "Shell" / "Rt2TopLevelSubmenu.cs").read_text(encoding="utf-8")
host = (ROOT / "Ui" / "Shell" / "AppShellHost.cs").read_text(encoding="utf-8")
shell = (ROOT / "Ui" / "Shell" / "AppShell.cs").read_text(encoding="utf-8")
patch = (ROOT / "Bootstrap" / "MainMenuPatchInstaller.cs").read_text(encoding="utf-8")
search_page = (ROOT / "Ui" / "Pages" / "Search" / "SearchPage.cs").read_text(encoding="utf-8")

ready_body = top.split("public override void _Ready()", 1)[1].split("protected override void ConnectSignals()", 1)[0]
connect_body = top.split("protected override void ConnectSignals()", 1)[1].split("public override void OnSubmenuOpened()", 1)[0]

checks = {
    "typed NSubmenu top-level": "class Rt2TopLevelSubmenu : NSubmenu" in top,
    "derived ready never calls base": "base._Ready();" not in ready_body,
    "custom ConnectSignals avoids base direct pop": "base.ConnectSignals();" not in connect_body,
    "no NBackButton runtime dependency": "new NBackButton" not in top and "GetNode<NBackButton>" not in top,
    "header x becomes close request": "TopLevelCloseRequested" in shell and "_header.CloseRequested += () => TopLevelCloseRequested?.Invoke();" in shell,
    "app shell cleanup does not own visibility": "public void CleanupForTopLevelClose()" in shell and "Visible = false;" not in shell.split("public void CleanupForTopLevelClose()", 1)[1].split("private void CaptureAndFlushPersistentUiState", 1)[0],
    "single guarded pop call": top.count("_stack.Pop();") == 1 and "RequestTopLevelClose" in top,
    "expected-top guard": "ReferenceEquals(_stack.Peek(), this)" in top,
    "duplicate-close guard": "if (_closeRequested)" in top and "_closeRequested = true;" in top,
    "OnSubmenuClosed cleanup-only": "public override void OnSubmenuClosed()" in top and "CleanupForTopLevelClose" in top.split("public override void OnSubmenuClosed()", 1)[1].split("public bool RequestTopLevelClose", 1)[0] and "Pop()" not in top.split("public override void OnSubmenuClosed()", 1)[1].split("public bool RequestTopLevelClose", 1)[0],
    "typed main-menu stack property": "NMainMenuSubmenuStack stack = _mainMenu.SubmenuStack;" in host,
    "custom surface parent is stack": "stack.AddChild(surface);" in host,
    "push uses existing custom instance": "stack.Push(_topLevelSurface);" in host and "PushSubmenuType" not in host,
    "add-child then deferred push": "stack.AddChild(surface);" in host and "Callable.From(PushTopLevelDeferred).CallDeferred();" in host,
    "main-menu injector typed": "NMainMenu mainMenu" in patch and "MinimalPanelInjector.EnsureAttached(mainMenu" in patch,
    "launcher visibility no reflection authority": "SubmenuStack.SubmenusOpen" in host and "BindingFlags" not in host and "TryGetBooleanMember" not in host,
    "save-picker Control transaction retained": "_presetSaveSurface.OpenCreate(" in search_page and "_presetVisualIconPicker.Closed += HandlePresetVisualPickerClosed;" in search_page,
}

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {name}")
if failed:
    raise SystemExit(f"A1 top-level surface gate failed: {failed}")
print(f"PASS: {len(checks)}/{len(checks)} A1 top-level surface checks")
