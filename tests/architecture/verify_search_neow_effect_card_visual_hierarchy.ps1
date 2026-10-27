$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
python (Join-Path $repoRoot 'tests\architecture\verify_search_neow_effect_card_visual_hierarchy.py')
