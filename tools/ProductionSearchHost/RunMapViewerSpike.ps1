param(
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$GamePck,
    [Parameter(Mandatory)][string]$Output,
    [switch]$AnalysisMapCard
)
$ErrorActionPreference = 'Stop'
# Run after the host Release build. No game process, prediction or search is started.
$godotPath = (Resolve-Path -LiteralPath $Godot).Path
$pckPath = (Resolve-Path -LiteralPath $GamePck).Path
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$loadPath = Join-Path $PSScriptRoot '.godot/mono/temp/bin/Debug'
New-Item -ItemType Directory -Force -Path $outputPath,$loadPath | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.godot/mono/temp/bin/Release') |
    Copy-Item -Destination $loadPath -Recurse -Force
function Quote([string]$value) {
    if ($value.Contains('"')) { throw 'Unexpected quote in path' }
    return '"' + $value.TrimEnd('\') + '"'
}
$arguments = @(
    '--path',(Quote $PSScriptRoot),'--rendering-method','gl_compatibility',
    'res://MapViewerSpike.tscn','--','--game-pck',(Quote $pckPath),'--output',(Quote $outputPath)
)
if ($AnalysisMapCard) { $arguments += '--analysis-map-card' }
$process = Start-Process -FilePath $godotPath -WindowStyle Hidden -PassThru -ArgumentList $arguments `
    -RedirectStandardOutput (Join-Path $outputPath 'stdout.log') -RedirectStandardError (Join-Path $outputPath 'stderr.log')
try {
    if (-not $process.WaitForExit(45000)) { throw "Map spike process $($process.Id) did not finish within 45 seconds" }
    if ($process.ExitCode -ne 0) { throw "Map spike failed: see $outputPath/stderr.log" }
    Get-Content -LiteralPath (Join-Path $outputPath 'result.json')
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
