param(
    [Parameter(Mandatory = $true)][string]$GodotExe,
    [string]$LocalizationSource,
    [string]$GamePck,
    [string]$ReleaseBinaryDirectory,
    [string]$Output = (Join-Path $PSScriptRoot '.build-evidence/latest'),
    [ValidateRange(1, 2147483)][int]$TimeoutSeconds = 300,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
# Native host for the existing focused test entry. Reference localization is
# read-only. RT2_NATIVE_GAME_PCK optionally supplies the matching installed resource
# pack instead of older extracted strings; this does not load game saves or mods.
if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) { throw 'Missing Godot executable' }
if ($ReleaseBinaryDirectory -and -not $SkipBuild) { throw '-ReleaseBinaryDirectory requires -SkipBuild for an immutable comparison.' }
$godotPath = (Resolve-Path -LiteralPath $GodotExe).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$previousGamePck = $env:RT2_NATIVE_GAME_PCK
$packSource = if ($GamePck) { $GamePck } else { $previousGamePck }
$resolvedGamePck = if ($packSource) { (Resolve-Path -LiteralPath $packSource).Path } else { $null }
$localizationTarget = Join-Path $PSScriptRoot 'localization'
if ($LocalizationSource) {
    $localizationPath = (Resolve-Path -LiteralPath $LocalizationSource).Path
    if (-not (Test-Path -LiteralPath (Join-Path $localizationPath 'eng') -PathType Container)) { throw 'Missing English localization directory' }
    if (-not (Test-Path -LiteralPath (Join-Path $localizationPath 'completion.json') -PathType Leaf)) { throw 'Missing localization completion.json' }
    New-Item -ItemType Directory -Force -Path $localizationTarget | Out-Null
    Copy-Item -LiteralPath (Join-Path $localizationPath 'eng') -Destination $localizationTarget -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $localizationPath 'completion.json') -Destination $localizationTarget -Force
} elseif (-not $resolvedGamePck) {
    # Reuse only explicitly prepared resources. A fresh host needs a source or
    # matching PCK; a missing supplied PCK never falls back to local strings.
    if (-not $SkipBuild -or
        -not (Test-Path -LiteralPath (Join-Path $localizationTarget 'eng') -PathType Container) -or
        -not (Test-Path -LiteralPath (Join-Path $localizationTarget 'completion.json') -PathType Leaf)) {
        throw 'Pass -GamePck (or RT2_NATIVE_GAME_PCK) or -LocalizationSource; -SkipBuild may reuse prepared localization.'
    }
}
if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot 'AuthoredEventChecks.csproj') -c Release -p:Rt2ModsDir= -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Release native test host build failed' }
}
# Godot's editor executable looks up Debug. Populate that lookup with the full
# selected Release output, as ProductionSearchHost does; execution never builds.
$releasePath = Join-Path $PSScriptRoot '.godot/mono/temp/bin/Release'
if ($ReleaseBinaryDirectory) { $releasePath = (Resolve-Path -LiteralPath $ReleaseBinaryDirectory).Path }
foreach ($required in @('AuthoredEventChecks.dll', 'AuthoredEventChecks.deps.json',
    'AuthoredEventChecks.runtimeconfig.json', 'Rt2FunctionalChecks.dll', 'RolltheSpire2.dll',
    'GodotSharp.dll', 'sts2.dll', '0Harmony.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $releasePath $required) -PathType Leaf)) {
        throw "Missing Release host dependency: $required. Build without -SkipBuild or supply the complete -ReleaseBinaryDirectory."
    }
}
$loadPath = Join-Path $PSScriptRoot '.godot/mono/temp/bin/Debug'
if ([IO.Path]::GetFullPath($releasePath).TrimEnd('\') -eq [IO.Path]::GetFullPath($loadPath).TrimEnd('\')) {
    throw 'ReleaseBinaryDirectory must be separate from the Godot Debug lookup directory.'
}
New-Item -ItemType Directory -Path $loadPath -Force | Out-Null
Get-ChildItem -LiteralPath $releasePath | Copy-Item -Destination $loadPath -Recurse -Force
Write-Output "Authored host prepared from Release: $releasePath; native check uses one fresh Godot process."
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { throw 'Argument contains an unsupported quote.' }
    return '"' + $Value.TrimEnd('\') + '"'
}
$arguments = @('--headless', '--path', (Quote-Argument $PSScriptRoot))
# Workbench checks locate the repository from the child working directory.
# Restore the caller's environment immediately after the child inherits it.
try {
    $env:RT2_NATIVE_GAME_PCK = $resolvedGamePck
    $process = Start-Process -FilePath $godotPath -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputPath 'runtime.log') `
        -RedirectStandardError (Join-Path $outputPath 'stderr.log')
} finally {
    $env:RT2_NATIVE_GAME_PCK = $previousGamePck
}
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Authored native check timed out after $TimeoutSeconds seconds; run is invalid. Inspect $outputPath"
    }
    $hostExitCode = $process.ExitCode
    Write-Output "Authored native check exited ($hostExitCode): $outputPath"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
exit $hostExitCode
