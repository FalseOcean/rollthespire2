param([string]$Root = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) { throw 'python was not found on PATH.' }
& $python.Source (Join-Path $Root 'tests\architecture\verify_search_ancient_runtime_event_identity_hotfix.py')
if ($LASTEXITCODE -ne 0) { throw 'Ancient runtime EVENT identity hotfix validation failed.' }
