from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
host = (ROOT / "Ui" / "Shell" / "AppShellHost.cs").read_text(encoding="utf-8")
patch = (ROOT / "Bootstrap" / "MainMenuPatchInstaller.cs").read_text(encoding="utf-8")
top = (ROOT / "Ui" / "Shell" / "Rt2TopLevelSubmenu.cs").read_text(encoding="utf-8")

checks = {
    "submenuStateHookPatched": (
        'AccessTools.Method(type, "OnSubmenuStackChanged")' in patch
        and "SubmenuStackChangedPostfix" in patch
        and "AppShellHost.SyncMainMenuSurfaceVisibility(mainMenu)" in patch
    ),
    "typedMainMenuSubmenuStateRead": "mainMenu.SubmenuStack.SubmenusOpen" in host,
    "typedPatchNotesStateRead": "mainMenu.PatchNotesScreen.IsOpen" in host,
    "patchNotesVisibilityLifecycleHooked": (
        "mainMenu.PatchNotesScreen.Connect(" in host
        and "CanvasItem.SignalName.VisibilityChanged" in host
        and "HandlePatchNotesVisibilityChanged" in host
    ),
    "launcherRootHiddenOutsideMainMenuRoot": (
        "bool mainMenuRootVisible = !submenuOpen && !patchNotesOpen;" in host
        and '"patch-notes-open"' in host
        and "Visible = visible;" in host
        and "_launcher.Visible = visible;" in host
    ),
    "launcherDoesNotOwnAppShellLifecycle": (
        "AppShell? _shell" not in host
        and "HideForMainMenuEntry" not in host
        and "_shell.Open()" not in host
    ),
    "boneDiceUsesGameStack": (
        "NMainMenuSubmenuStack stack = _mainMenu.SubmenuStack;" in host
        and "stack.AddChild(surface);" in host
        and "stack.Push(_topLevelSurface);" in host
    ),
    "deferredPushAfterAttach": "Callable.From(PushTopLevelDeferred).CallDeferred();" in host,
    "topLevelOwnsAppShell": "class Rt2TopLevelSubmenu : NSubmenu" in top and "_shell = new AppShell" in top,
    "hiddenLauncherCannotOpenRt2": "!_mainMenuSurfaceVisible" in host and "_pushScheduled" in host,
    "visibilityEvidenceLogged": "mainMenuLauncherSurfaceVisible=" in host,
}

for name, passed in checks.items():
    print(f"{name}={str(passed).lower()}")

if not all(checks.values()):
    raise SystemExit(1)
