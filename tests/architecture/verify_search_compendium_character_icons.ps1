param(
    [string]$Python = "python"
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $root
try {
    & $Python ".\tests\architecture\verify_search_compendium_character_icons.py"
    if ($LASTEXITCODE -ne 0) { throw "Search compendium character icon validation failed." }
}
finally {
    Pop-Location
}
