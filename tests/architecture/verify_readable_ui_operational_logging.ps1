param([string]$Root = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
python (Join-Path $Root 'tests\architecture\verify_readable_ui_operational_logging.py')
if ($LASTEXITCODE -ne 0) { throw 'Readable UI operational logging validation failed.' }
