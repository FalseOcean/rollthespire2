param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
function Read([string]$p) { Get-Content (Join-Path $root $p) -Raw }
function Require([bool]$condition, [string]$name) {
  if (-not $condition) { throw "World compendium/prediction-context check failed: $name" }
  Write-Output ("{0}=true" -f $name)
}

$mode = Read 'Compatibility\PredictionGameModeResolver.cs'
$adapter = Read 'Infrastructure\Snapshots\ReflectionNeowEffectSnapshotAdapter.Beta109Authority.cs'
$icons = Read 'Ui\Icons\ReflectionGameIconResolver.cs'
$worldUi = Read 'Ui\Components\WorldPredictionSummaryList.cs'

Require ($mode -match 'ExplicitPredictionContext:Singleplayer') 'explicitSingleplayerAuthority'
Require ($mode -match 'InvalidPredictionContext') 'invalidExplicitContextFailsClosed'
Require ($mode -notmatch 'PredictionContextRuntimeMismatch') 'runtimeSessionNotProductionAuthority'
Require ($adapter -match 'PredictionGameModeResolver\.Resolve') 'runtimeAdapterUsesPureModeResolver'
Require ($icons -match 'IsWorldCompendiumRequest') 'worldResolverVariantScoped'
Require ($icons -match 'RunHistoryIcon') 'ancientRunHistoryIcon'
Require ($icons -match 'GetRoomIconPath') 'bossRunHistoryIconPath'
Require ($icons -match 'audited-ImageHelper\.GetRoomIconPath-model-id-fallback') 'abiSafeAuditedPathFallback'
Require ($icons -notmatch 'BossNodePath') 'bossMapIconNotUsed'
Require ($icons -notmatch '\.MapIcon') 'ancientMapIconNotUsed'
Require ($worldUi -match 'WorldCompendiumBossIcon') 'bossWorldUiBinding'
Require ($worldUi -match 'WorldCompendiumAncientIcon') 'ancientWorldUiBinding'
Require ($worldUi -match 'new IconWithLabel\(40f') 'compactFortyPixelLayout'
