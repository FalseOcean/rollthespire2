param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$captureRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SlayTheSpire2/RolltheSpire2'
if (-not (Test-Path -LiteralPath $captureRoot)) { throw 'Game user-data directory not found; use RT2_PRODUCTION_REPLAY_EXPORT_DIR instead.' }
$capturePath = [IO.Path]::GetFullPath($Output)
[IO.File]::WriteAllText((Join-Path $captureRoot 'production-replay-export.request'), $capturePath)
Write-Output "Capture armed: on the next normal game launch, open RT2 Search. Immutable inputs will be saved to $capturePath. No search run required."
