param([string]$Root = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
python (Join-Path $Root 'tests\architecture\verify_search_ancient_act_grouped_icon_matrix.py')
if ($LASTEXITCODE -ne 0) { throw 'Ancient Act-grouped icon matrix verification failed.' }
