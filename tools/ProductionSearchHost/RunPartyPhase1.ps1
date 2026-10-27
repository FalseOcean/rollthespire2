param(
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$GamePck,
    [Parameter(Mandatory)][string]$Output,
    [switch]$Results,
    [switch]$Visual
)
$ErrorActionPreference = 'Stop'
$hostRoot = $PSScriptRoot
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$godotPath = (Resolve-Path -LiteralPath $Godot).Path
$pckPath = (Resolve-Path -LiteralPath $GamePck).Path
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
# Owner requested coexistence with Route Runtime work: never deploy to game mods.
dotnet build (Join-Path $hostRoot 'ProductionSearchHost.csproj') -c Release --nologo -p:Rt2ModsDir= -m:1 *> (Join-Path $outputPath 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Party Phase 1 host build failed; inspect build.log' }
$loadPath = Join-Path $hostRoot '.godot/mono/temp/bin/Debug'
New-Item -ItemType Directory -Force -Path $loadPath | Out-Null
Get-ChildItem -LiteralPath (Join-Path $hostRoot '.godot/mono/temp/bin/Release') | Copy-Item -Destination $loadPath -Recurse -Force
function Quote-Argument([string]$value) {
    if ($value.Contains('"')) { throw 'Unsupported quote in path' }
    return '"' + $value.TrimEnd('\') + '"'
}
$arguments = @(if ($Visual) { '--rendering-method'; 'gl_compatibility' } else { '--headless' })
$arguments += @('--path', (Quote-Argument $hostRoot), '--', $(if ($Results) { '--party-phase1b-checks' } else { '--party-phase1-checks' }),
    '--output', (Quote-Argument $outputPath), '--game-pck', (Quote-Argument $pckPath))
$process = Start-Process -FilePath $godotPath -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $outputPath 'runtime.log') -RedirectStandardError (Join-Path $outputPath 'stderr.log')
try {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Party Phase 1 timeout' }
    }
    $stdout = Get-Content -LiteralPath (Join-Path $outputPath 'runtime.log') -Raw
    $stderr = Get-Content -LiteralPath (Join-Path $outputPath 'stderr.log') -Raw
    if (($Results -and $stdout -notmatch 'PARTY_PHASE1B_PASS') -or $process.ExitCode -ne 0 -or $stdout -notmatch 'PARTY_PHASE1_PASS checks=\d+' -or $stderr -match 'System\.[A-Za-z.]*Exception:') {
        throw 'Party Phase 1 checks failed; inspect runtime.log and stderr.log'
    }
    Write-Output ($stdout -split "`n" | Where-Object { $_ -match 'PARTY_PHASE1(B)?_PASS' })
} finally {
    if (-not $process.HasExited) { $process.Kill($true) }
    $process.Dispose()
}
