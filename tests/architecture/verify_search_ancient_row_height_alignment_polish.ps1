$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
python (Join-Path $Root 'tests\architecture\verify_search_ancient_row_height_alignment_polish.py')
