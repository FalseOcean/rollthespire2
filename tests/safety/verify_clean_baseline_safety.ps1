param(
    [string]$ProjectRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path $PSScriptRoot '..\..'
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

function Get-CsFiles([string]$Relative) {
    $path = Join-Path $ProjectRoot $Relative
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-ChildItem -LiteralPath $path -Recurse -File -Filter *.cs)
}

function Assert-NoSourceMatch([System.IO.FileInfo[]]$Files, [string]$Pattern, [string]$Message) {
    $matches = @($Files | Select-String -Pattern $Pattern)
    if ($matches.Count -gt 0) {
        $matches | Select-Object -First 20 | ForEach-Object {
            Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
        }
        throw "$Message Match count: $($matches.Count)."
    }
}

# This validator protects only durable source-boundary invariants. It deliberately
# does not freeze helper names, implementation phases, Planner policy, performance
# evidence, or historical fallback/gate strings.
$searchSources = @(Get-CsFiles 'Search')
$productionSources = @(Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter *.cs |
    # Evidence includes archived third-party source; it is excluded from the
    # production project, just like test/host and generated build directories.
    Where-Object { $_.FullName -notmatch '[\\/](tests|tools|bin|obj|\.build-evidence|\.godot|\.git)[\\/]' })

Assert-NoSourceMatch $searchSources '^\s*using\s+RolltheSpire2\.Ui(?:\.|;)' 'Search/runtime computation layer imports UI implementation.'
Assert-NoSourceMatch $searchSources '^\s*using\s+MegaCrit\.Sts2(?:\.|;)' 'Search/runtime computation layer imports live game implementation.'
Assert-NoSourceMatch $searchSources '\b(?:RunManager|SaveManager|ModelDb)\s*\.' 'Search/runtime computation layer references live game authority directly.'
Assert-NoSourceMatch $productionSources '\.(?:AfterObtained|OnObtained|OnObtain)\s*\(' 'Production prediction/search invokes a real obtain hook.'

foreach ($relative in @(
    'RolltheSpire2.csproj',
    'RolltheSpire2.json',
    'project.godot',
    'Search\Runtime\ProductionExactSearchEvaluator.cs',
    'Search\Runtime\ProductionQueryValidator.cs',
    'Search\Runtime\ProductionSearchSession.cs',
    'BUILDING.md'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $relative) -PathType Leaf)) {
        throw "Required current-baseline file is missing: $relative"
    }
}

# Internal handoffs retain their authority documents; public exports replace those
# private records with self-contained development guidance, not weaker source checks.
$metadataPath = Join-Path $ProjectRoot 'SOURCE_PACKAGE.json'
$isPublic = (Test-Path -LiteralPath $metadataPath -PathType Leaf) -and
    ((Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json).PackageKind -eq 'PublicSource')
$requiredDocs = if ($isPublic) { @('DEVELOPMENT.md') } else { @('CURRENT_STATUS.md', 'PRODUCT_STRATEGY.md', 'PROJECT_ARCHITECTURE.md') }
foreach ($relative in $requiredDocs) {
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $relative) -PathType Leaf)) { throw "Required development document is missing: $relative" }
}

Write-Host 'searchUiImplementationImports=0'
Write-Host 'searchLiveGameImplementationImports=0'
Write-Host 'searchLiveAuthorityDirectReferences=0'
Write-Host 'productionObtainHookInvocations=0'
Write-Host 'currentSafetyBoundaryChecks=Passed'
Write-Host 'validationPolicy=ExposeMismatchDoNotManufactureFallback'
