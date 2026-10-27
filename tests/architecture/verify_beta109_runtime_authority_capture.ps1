param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

function Require-Contains([string]$Path, [string]$Pattern, [string]$Label) {
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -notmatch $Pattern) { throw "$Label=false ($Path)" }
    Write-Output "$Label=true"
}

function Require-NotContains([string]$Path, [string]$Pattern, [string]$Label) {
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -match $Pattern) { throw "$Label=false ($Path)" }
    Write-Output "$Label=true"
}

$snapshot = Join-Path $RepoRoot "Core\World\Snapshots\Beta109WorldGenerationSnapshot.cs"
$projector = Join-Path $RepoRoot "Core\World\Beta109\Beta109WorldSnapshotProjector.cs"
$adapter = Join-Path $RepoRoot "Infrastructure\Snapshots\ReflectionNeowEffectSnapshotAdapter.Beta109Authority.cs"

Require-Contains $snapshot "ulong ActSelectionRoot" "actSelectionRootSeparated"
Require-Contains $snapshot "ulong RunSeedRoot" "runSeedRootSeparated"
Require-Contains $snapshot "AllSharedAncients" "allSharedAncientsSeparated"
Require-Contains $snapshot "SharedAncients" "unlockedSharedAncientsSeparated"
Require-Contains $snapshot "TutorialBossOverrideAuthorityExact" "tutorialBossOverrideFailClosed"
Require-Contains $projector "CreateLobbyLocal\(actSelectionRoot, \"act_selection\"\)" "selectedActsPureProjection"
Require-Contains $adapter "ReadStaticMemberWorld\(modelDb, \"ActsByIndex\"\)" "actsByIndexRuntimeAuthorityUsed"
Require-Contains $adapter "MissingActsByIndexOrder" "actSelectionRuntimeFailureGranular"
Require-Contains $adapter "SharedRelicPool" "trueSharedRelicPoolCaptured"
Require-Contains $adapter "CharacterRelicPool" "trueCharacterRelicPoolCaptured"
Require-Contains $adapter "GetUnlockedRelics" "runtimeRelicPoolsUseUnlockState"
Require-Contains $adapter "SharesTagsWith" "encounterTagComparerCaptured"
Require-Contains $adapter "GetNumberOfRooms" "roomShapeCaptured"
Require-Contains $adapter "MegaCrit.Sts2.Core.Runs.RunManager" "multiplayerReadsRunManager"
Require-Contains $adapter "ReadProperty\(runManager, \"NetService\"\)" "multiplayerReadsNetServiceInstanceProperty"
Require-Contains $adapter "ReadProperty\(netService, \"Type\"\)" "multiplayerReadsNetGameTypeProperty"
Require-Contains $adapter "MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType" "multiplayerUsesExactEnumType"
Require-Contains $adapter "numericValue is 2 or 3" "multiplayerHostClientMapping"
Require-NotContains $adapter "MegaCrit.Sts2.Core.Multiplayer.NetService" "fictitiousStaticNetServiceRemoved"

$workerFiles = @(
    Get-ChildItem (Join-Path $RepoRoot "Core\World\Beta109") -Filter *.cs -Recurse
)
$forbidden = "using\s+Godot|MegaCrit\.Sts2\.Core\.Models\.ModelDb|MegaCrit\.Sts2\.Core\.Runs\.RunState|SaveManager\.Instance|\.AfterObtained\s*\(|\.OnObtain\s*\("
foreach ($file in $workerFiles) {
    Require-NotContains $file.FullName $forbidden ("workerPureDto_" + $file.BaseName)
}

$productionFiles = Get-ChildItem $RepoRoot -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch "[\\/]tests[\\/]" }
foreach ($file in $productionFiles) {
    Require-NotContains $file.FullName "Beta109WorldFixtureFactory|SyntheticCatalogFixture" ("productionFixtureIsolation_" + $file.BaseName)
}

Write-Output "beta109RuntimeAuthorityCaptureStaticVerification=true"
