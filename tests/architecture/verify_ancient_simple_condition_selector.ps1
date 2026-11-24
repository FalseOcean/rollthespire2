$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')

function Read-Text([string]$relative) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relative))
}

$profile = Read-Text 'Core\World\AncientOptionConditionProfile.cs'
$request = Read-Text 'Core\Prediction\SeedPredictionRequest.cs'
$provider = Read-Text 'Core\World\Beta109\Beta109AncientOptionProvider.cs'
$capture = Read-Text 'Infrastructure\Snapshots\ReflectionNeowEffectSnapshotAdapter.Beta109AncientOptions.cs'
$analysisUi = Read-Text 'Ui\Components\AnalysisRequestBar.cs'
$searchUi = Read-Text 'Ui\Pages\Search\Ancient\AncientOptionConditionEditor.cs'
$searchContracts = Read-Text 'Search\Contracts\SearchContracts.cs'
$worker = Read-Text 'Search\Runtime\SearchExecutionPipeline.cs'

$checks = [ordered]@{
    conditionProfilePresent = $profile.Contains('AncientOptionConditionProfile')
    broadDefaultEnabled = $profile.Contains('TezcataraHasBasicStrike: true') -and $profile.Contains('NonupeipeSwiftEnchantableAtLeast4: true') -and $profile.Contains('TanxInstinctEnchantableAtLeast3: true')
    requestCarriesProfile = $request.Contains('AncientOptionConditions')
    nonupeipeConditionalCatalog = $capture.Contains('nonupeipe.pool.swift') -and $capture.Contains('BEAUTIFUL_BRACELET')
    tanxConditionalCatalog = $capture.Contains('tanx.pool.instinct') -and $capture.Contains('TRI_BOOMERANG')
    tezcataraConditionalCatalog = $capture.Contains('tezcatara.pool1.basic-strike') -and $capture.Contains('NUTRITIOUS_SOUP')
    providerCombinesBeforeShuffle = $provider.Contains('pool.AddRange(catalog.Pool("nonupeipe.pool.swift"))') -and $provider.Contains('rng.UnstableShuffle(pool, "nonupeipe:shuffle")')
    analysisUiSelector = $analysisUi.Contains('_nonupeipeSwift') -and $analysisUi.Contains('ButtonPressed = true')
    searchUiSelector = $searchUi.Contains('_nonupeipeSwift') -and $searchUi.Contains('AncientOptionConditionProfile.BroadDefault')
    searchCarriesProfile = $searchContracts.Contains('AncientOptionConditions') -and $worker.Contains('_plan.Filter.AncientOptionConditions')
    workerHasNoGodotReference = -not $provider.Contains('using Godot')
}

$failed = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$checks.GetEnumerator() | ForEach-Object { Write-Output ("{0}={1}" -f $_.Key, $_.Value.ToString().ToLowerInvariant()) }
if ($failed.Count -gt 0) {
    throw ('Ancient simple condition selector verification failed: ' + (($failed | ForEach-Object Key) -join ', '))
}
