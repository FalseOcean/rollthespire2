param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$ErrorActionPreference = 'Stop'

function Require-Contains([string]$Path, [string]$Text, [string]$Name) {
    if (-not (Select-String -Path $Path -SimpleMatch $Text -Quiet)) {
        throw "$Name=false missing '$Text' in $Path"
    }
    Write-Output "$Name=true"
}

$authority = Join-Path $Root 'Infrastructure\Snapshots\ReflectionNeowEffectSnapshotAdapter.Beta109Events.cs'
$snapshot = Join-Path $Root 'Core\World\Snapshots\Beta109WorldGenerationSnapshot.cs'
$provider = Join-Path $Root 'Core\World\Beta109\Beta109WorldPredictionProvider.cs'
$search = Join-Path $Root 'Search'

Require-Contains $authority 'Event1Epoch' 'event1MembershipCapture'
Require-Contains $authority 'Event2Epoch' 'event2MembershipCapture'
Require-Contains $authority 'Event3Epoch' 'event3MembershipCapture'
Require-Contains $authority 'IsEpochRevealed' 'eventEpochRevealCapture'
Require-Contains $authority 'result.RemoveAll' 'eventFilterNoRng'
Require-Contains $snapshot 'OrderedEligibleEvents' 'filteredEventDto'
Require-Contains $snapshot 'EventEpochRevealFactsExact' 'splitEventAuthority'
Require-Contains $provider 'act.OrderedEligibleEvents.ToList()' 'providerUsesFilteredEvents'
Require-Contains $provider 'MissingEventEpochAuthority' 'missingEpochFailsClosed'

$forbidden = Get-ChildItem $search -Recurse -Filter *.cs | Select-String -Pattern 'Event1Epoch','IsEpochRevealed','ModelDb','RunState'
if ($forbidden) { throw 'searchGameAuthorityLeak=false' }
Write-Output 'searchGameAuthorityLeak=true'
Write-Output 'productionExactPromoted=false'
