param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"
function Read-Text([string]$relative) {
    return [System.IO.File]::ReadAllText((Join-Path $Root $relative))
}

$host = Read-Text 'Ui\Shell\AppShellHost.cs'
$shell = Read-Text 'Ui\Shell\AppShell.cs'
$patch = Read-Text 'Bootstrap\MainMenuPatchInstaller.cs'
$top = Read-Text 'Ui\Shell\Rt2TopLevelSubmenu.cs'

$checks = [ordered]@{}
$checks.mainMenuStillInjectsHost = $patch.Contains('MinimalPanelInjector.EnsureAttached(mainMenu, ModRuntime.Snapshot)')
$checks.launcherRemainsEntryOnly = $host.Contains('_launcher.Pressed += ShowShell;') -and -not $host.Contains('AppShell? _shell')
$checks.typedStackAttach = $host.Contains('NMainMenuSubmenuStack stack = _mainMenu.SubmenuStack;') -and $host.Contains('stack.AddChild(surface);')
$checks.deferredPush = $host.Contains('Callable.From(PushTopLevelDeferred).CallDeferred();') -and $host.Contains('stack.Push(_topLevelSurface);')
$checks.topLevelOwnsShell = $top.Contains('class Rt2TopLevelSubmenu : NSubmenu') -and $top.Contains('_shell = new AppShell')
$checks.headerCloseIsRequest = $shell.Contains('TopLevelCloseRequested') -and $shell.Contains('_header.CloseRequested += () => TopLevelCloseRequested?.Invoke();')
$checks.singleGuardedPop = ([regex]::Matches($top, '_stack\.Pop\(\);')).Count -eq 1 -and $top.Contains('RequestTopLevelClose')
$checks.cleanupDoesNotHideShell = $shell.Contains('public void CleanupForTopLevelClose()') -and -not $shell.Contains('ShellClosed?.Invoke();')

$failed = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$checks.GetEnumerator() | ForEach-Object { Write-Output ("{0}={1}" -f $_.Key, $_.Value.ToString().ToLowerInvariant()) }
if ($failed.Count -gt 0) {
    throw ('Main-menu top-level launch verification failed: ' + (($failed | ForEach-Object Key) -join ', '))
}
