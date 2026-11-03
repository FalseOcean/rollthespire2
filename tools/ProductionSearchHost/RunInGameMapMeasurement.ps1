[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameExe,
    [Parameter(Mandatory)][string]$Context,
    [Parameter(Mandatory)][string]$Workload,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$ReleaseBinaryDirectory,
    [string]$Settings,
    [ValidateRange(1, 2147483)][int]$TimeoutSeconds = 300,
    [switch]$Pipeline
)

$ErrorActionPreference = 'Stop'
$gamePath = (Get-Item -LiteralPath $GameExe).FullName
$contextPath = (Get-Item -LiteralPath $Context).FullName
$workloadPath = (Get-Item -LiteralPath $Workload).FullName
$releasePath = (Get-Item -LiteralPath $ReleaseBinaryDirectory).FullName
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$gameDirectory = [IO.Path]::GetDirectoryName($gamePath)
$processName = [IO.Path]::GetFileNameWithoutExtension($gamePath)
$temporaryMod = Join-Path $gameDirectory 'mods/ProductionSearchHost'
$installedMod = Join-Path $gameDirectory 'mods/RolltheSpire2/RolltheSpire2.dll'
$hostSource = Join-Path $releasePath 'ProductionSearchHost.dll'
$productSource = Join-Path $releasePath 'RolltheSpire2.dll'
foreach ($path in @($gamePath, $contextPath, $workloadPath, $hostSource, $productSource, $installedMod)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file missing: $path" }
}
if ($Settings) {
    $settingsPath = (Get-Item -LiteralPath $Settings).FullName
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) { throw 'Settings must name a file.' }
}
$productHash = (Get-FileHash -LiteralPath $productSource -Algorithm SHA256).Hash
if ($productHash -ne (Get-FileHash -LiteralPath $installedMod -Algorithm SHA256).Hash) {
    throw 'ReleaseBinaryDirectory/RolltheSpire2.dll differs from the installed production Mod. Deploy the intended build separately.'
}
$hostHash = (Get-FileHash -LiteralPath $hostSource -Algorithm SHA256).Hash
$guard = [Threading.Mutex]::new($false, 'Local\RT2_PerformanceBench_20261012')
$owned = $false; $createdMod = $false; $createdOutput = $false
$createdFiles = [Collections.Generic.List[string]]::new()
$cleanupErrors = [Collections.Generic.List[string]]::new()
$process = $null; $failure = $null; $exitCode = $null; $receiptPassed = $false
$started = [DateTimeOffset]::UtcNow
$previousAppData = $env:APPDATA
$previousEnable = $env:RT2_MAP_GAME_MEASUREMENT_DIR
$previousPipeline = $env:RT2_MAP_GAME_MEASUREMENT_PIPELINE
try {
    try { $owned = $guard.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { throw 'Another cooperating performance measurement owns the GPU mutex.' }
    if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { throw 'Game already running; close it before measuring.' }
    if (Test-Path -LiteralPath $temporaryMod) { throw "Temporary Host Mod already exists; refusing to overwrite: $temporaryMod" }
    if (Test-Path -LiteralPath $outputPath) { throw "Output must be a new directory: $outputPath" }
    New-Item -ItemType Directory -Path $outputPath | Out-Null
    $createdOutput = $true
    [IO.File]::Copy($contextPath, (Join-Path $outputPath 'context.json'), $false)
    [IO.File]::Copy($workloadPath, (Join-Path $outputPath 'workload.json'), $false)
    $env:APPDATA = Join-Path $outputPath 'appdata'
    $settingsDirectory = Join-Path $env:APPDATA 'SlayTheSpire2/default/1'
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $isolatedSettings = if ($Settings) { Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    if ($null -eq $isolatedSettings -or $isolatedSettings -isnot [pscustomobject]) { throw 'Settings must contain a JSON object.' }
    if ($null -eq $isolatedSettings.mod_settings) {
        $isolatedSettings | Add-Member -MemberType NoteProperty -Name mod_settings -Value ([pscustomobject]@{}) -Force
    }
    if ($isolatedSettings.mod_settings -isnot [pscustomobject]) { throw 'mod_settings must contain a JSON object.' }
    $isolatedSettings.mod_settings | Add-Member -MemberType NoteProperty -Name mods_enabled -Value $true -Force
    $isolatedSettings | Add-Member -MemberType NoteProperty -Name skip_intro_logo -Value $true -Force
    $isolatedSettings | Add-Member -MemberType NoteProperty -Name seen_ea_disclaimer -Value $true -Force
    $isolatedSettings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $settingsDirectory 'settings.save') -Encoding UTF8

    New-Item -ItemType Directory -Path $temporaryMod | Out-Null
    $createdMod = $true
    $destination = Join-Path $temporaryMod 'ProductionSearchHost.dll'
    $target = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $createdFiles.Add($destination)
    try {
        $source = [IO.File]::OpenRead($hostSource)
        try { $source.CopyTo($target) } finally { $source.Dispose() }
    } finally { $target.Dispose() }
    $manifest = @{
        id = 'ProductionSearchHost'; name = 'RT2 temporary map measurement'; author = 'RT2 development'
        description = 'Explicit opt-in diagnostic harness'; version = '1.0.0'
        has_dll = $true; has_pck = $false; affects_gameplay = $false
        dependencies = @(@{ id = 'RolltheSpire2'; min_version = '1.4.0' })
    } | ConvertTo-Json -Depth 4
    $destination = Join-Path $temporaryMod 'ProductionSearchHost.json'
    $target = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $createdFiles.Add($destination)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($manifest)
        $target.Write($bytes, 0, $bytes.Length)
    } finally { $target.Dispose() }
    # Recheck immediately before launch; never stop a pre-existing user process.
    if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { throw 'Game started during preparation; refusing to launch another instance.' }
    if ($productHash -ne (Get-FileHash -LiteralPath $installedMod -Algorithm SHA256).Hash) {
        throw 'Installed production Mod changed during preparation.'
    }
    $env:RT2_MAP_GAME_MEASUREMENT_DIR = $outputPath
    $env:RT2_MAP_GAME_MEASUREMENT_PIPELINE = if ($Pipeline) { '1' } else { '0' }
    $godotLog = Join-Path $outputPath 'godot.log'
    $process = Start-Process -FilePath $gamePath -WorkingDirectory $gameDirectory -WindowStyle Hidden -PassThru `
        -ArgumentList @('--rendering-driver', 'd3d12', '--force-steam', 'off', '--log-file', "`"$godotLog`"") `
        -RedirectStandardOutput (Join-Path $outputPath 'runtime.log') -RedirectStandardError (Join-Path $outputPath 'stderr.log')
    $process.Id | Set-Content -LiteralPath (Join-Path $outputPath 'pid.txt')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Game measurement timed out after $TimeoutSeconds seconds." }
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) { throw "Game measurement exited with code $exitCode." }
    $receiptPath = Join-Path $outputPath 'in-game-measurement-receipt.json'
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'Game exited without a measurement receipt.' }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.passed -isnot [bool] -or -not $receipt.passed -or $receipt.state -ne 'completed') {
        throw "Game measurement did not pass: $($receipt.error)"
    }
    if ($receipt.identity.pid -ne $process.Id -or $receipt.identity.mod.sha256 -ne $productHash -or
        $receipt.identity.diagnosticHost.sha256 -ne $hostHash) { throw 'Receipt process or loaded assembly identity differs from the submitted build.' }
    $receiptPassed = $true
} catch { $failure = $_ }
finally {
    $canClean = $true
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                # This handle belongs only to the process started above.
                $process.Kill()
                if (-not $process.WaitForExit(30000)) { throw 'Owned game process did not exit after termination.' }
            }
            $exitCode = $process.ExitCode
        } catch { $canClean = $false; $cleanupErrors.Add("Process shutdown: $_; temporary Host Mod retained: $temporaryMod") }
    }
    $env:APPDATA = $previousAppData
    $env:RT2_MAP_GAME_MEASUREMENT_DIR = $previousEnable
    $env:RT2_MAP_GAME_MEASUREMENT_PIPELINE = $previousPipeline
    if ($createdMod -and $canClean) {
        foreach ($path in $createdFiles) {
            try { [IO.File]::Delete($path) } catch { $cleanupErrors.Add("Temporary file $path : $_") }
        }
        try {
            if (@([IO.Directory]::EnumerateFileSystemEntries($temporaryMod)).Count -eq 0) {
                [IO.Directory]::Delete($temporaryMod, $false)
            } else { $cleanupErrors.Add("Temporary Mod directory contains other files; retained: $temporaryMod") }
        } catch { $cleanupErrors.Add("Temporary directory: $_") }
    }
    try {
        if ($createdOutput) {
            @{
                startedAt = $started; finishedAt = [DateTimeOffset]::UtcNow
                processId = if ($null -ne $process) { $process.Id } else { $null }; exitCode = $exitCode
                receiptPassed = $receiptPassed; error = if ($failure) { $failure.ToString() } else { $null }
                cleanupErrors = $cleanupErrors.ToArray(); pipeline = $Pipeline.IsPresent
                gameExe = $gamePath; gameExeSha256 = (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash
                installedModSha256 = $productHash; diagnosticHostSha256 = $hostHash
                contextSha256 = (Get-FileHash -LiteralPath (Join-Path $outputPath 'context.json') -Algorithm SHA256).Hash
                workloadSha256 = (Get-FileHash -LiteralPath (Join-Path $outputPath 'workload.json') -Algorithm SHA256).Hash
                scope = 'Offline real-game main menu; isolated APPDATA; portable captured inputs; no real RunState replay'
            } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'launcher.json') -Encoding UTF8
        }
    } catch { $cleanupErrors.Add("Launcher receipt: $_") }
    try { if ($owned) { $guard.ReleaseMutex() } } finally { $guard.Dispose() }
    if ($null -ne $process) { $process.Dispose() }
}
if ($failure) {
    foreach ($issue in $cleanupErrors) { Write-Warning $issue }
    throw $failure
}
if ($cleanupErrors.Count -gt 0) { throw ($cleanupErrors -join [Environment]::NewLine) }
Write-Output "In-game map measurement passed: $outputPath"
