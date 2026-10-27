param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$ErrorActionPreference = 'Stop'

function Require-Contains([string]$Path, [string]$Text, [string]$Name) {
    if (-not (Select-String -Path $Path -SimpleMatch $Text -Quiet)) {
        throw "$Name=false missing '$Text' in $Path"
    }
    Write-Output "$Name=true"
}

$snapshot = Join-Path $Root 'Core\World\Snapshots\Beta109WorldGenerationSnapshot.cs'
$grabBag = Join-Path $Root 'Core\World\Beta109\Beta109EncounterGrabBag.cs'
$provider = Join-Path $Root 'Core\World\Beta109\Beta109WorldPredictionProvider.cs'
$capture = Join-Path $Root 'Infrastructure\Snapshots\ReflectionNeowEffectSnapshotAdapter.Beta109Authority.cs'
$diagnostics = Join-Path $Root 'Core\Diagnostics\RuntimePredictionDiagnosticReporter.cs'
$search = Join-Path $Root 'Search'

Require-Contains $snapshot 'SourceOrdinal' 'encounterSourceOrdinalDto'
Require-Contains $snapshot 'ReferenceIdentityId' 'encounterReferenceIdentityDto'
Require-Contains $snapshot 'SharesTagsWithSourceOrdinals' 'encounterTagConflictMatrixDto'
Require-Contains $snapshot 'EncounterRetryShapeExact' 'encounterRetryAuthorityGate'
Require-Contains $grabBag '_items.Any(predicate)' 'predicatePrecheckNoRng'
Require-Contains $grabBag 'while (true)' 'fullBagRejectionLoop'
Require-Contains $grabBag '_items.RemoveAt(index)' 'removeOnlyAcceptedEntry'
Require-Contains $provider 'predicate: null' 'sourceFallbackDraw'
Require-Contains $provider 'if (selected is null) continue;' 'emptyBagFixedSlotLoop'
Require-Contains $capture 'Beta109KnownPureVanillaActTypes' 'vanillaOverrideAllowlist'
Require-Contains $capture 'Beta109VanillaEncounterSourceShapes' 'vanillaSourceShapeAuthority'
Require-Contains $capture 'GenerateAllEncounters' 'rawEncounterCatalogCapture'
Require-Contains $capture '"MONSTER" or "REGULAR" or "NORMAL"' 'monsterClassificationMapping'
Require-Contains $capture 'SharesTagsWithSourceOrdinals' 'pairwiseTagConflictCapture'
Require-Contains $capture 'ReferenceEqualityComparer.Instance' 'referenceIdentityCapture'
Require-Contains $diagnostics 'CaptureEncounterAuthority' 'encounterAuthorityDiagnostic'
Require-Contains $diagnostics 'EncounterCatalogShapeMismatch' 'sourceShapeMismatchDiagnostic'
Require-Contains $provider 'MissingEncounterRetryCallShape' 'retryShapeFailsClosed'

$forbidden = Get-ChildItem $search -Recurse -Filter *.cs | Select-String -Pattern 'GenerateAllEncounters','SharesTagsWith','EncounterModel','ReferenceEqualityComparer'
if ($forbidden) { throw 'searchEncounterGameAuthorityLeak=false' }
Write-Output 'searchEncounterGameAuthorityLeak=true'
Write-Output 'productionExactPromoted=false'
