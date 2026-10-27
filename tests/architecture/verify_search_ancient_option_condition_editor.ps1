param([string]$Root = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path $PSScriptRoot '..\..' }
$Root = (Resolve-Path -LiteralPath $Root).Path
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { throw 'python was not found on PATH.' }
& $python.Source (Join-Path $Root 'tests\architecture\verify_search_ancient_option_condition_editor.py')
if ($LASTEXITCODE -ne 0) { throw 'Search Ancient option condition editor verification failed.' }
