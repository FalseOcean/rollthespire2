$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
python (Join-Path $repoRoot 'tests\functional\verify_search_neow_ui_history_semantics.py')
