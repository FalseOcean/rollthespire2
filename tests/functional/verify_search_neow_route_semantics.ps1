$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
python (Join-Path $repoRoot 'tests\functional\verify_search_neow_route_semantics.py')
