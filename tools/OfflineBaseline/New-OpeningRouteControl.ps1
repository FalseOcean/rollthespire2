param(
    [Parameter(Mandatory = $true)][string]$Workload,
    [Parameter(Mandatory = $true)][string]$Output
)
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw -LiteralPath $Workload | ConvertFrom-Json
if ($null -eq $source.Query.OpeningRoute) { throw 'The source workload must contain an opening route.' }


$query = $source.Query
$query.OpeningRouteRelicRequirement = $null
$query.CombatCardRewards = $null
$query.CombatPotionRewards = $null
$query.TransformationAggregate = $null
foreach ($field in @('StructuredOpeningEffects', 'VariantBossBranches', 'AncientBranches',
    'RelicSequenceConstraints', 'EventSequenceConstraints', 'LegacyCombatRewardConstraints',
    'Players', 'StandardMaps', 'EventResultConditions', 'MerchantColorlessConditions',
    'MerchantColorlessSequenceConditions', 'RelicShopSequenceConditions')) {
    $query.$field = @()
}
if (@($query.LegacyNeow.NeowRelics.Any).Count -ne 0 -or
    @($query.LegacyWorld.BossFilters).Count -ne 0 -or
    $query.LegacyNeow.RequireNeowsBones -or $query.LegacyNeow.RequireSmallCapsule -or
    $query.LegacyNeow.RequireLargeCapsule -or $query.LegacyNeow.RequireWhetstone -or
    $query.LegacyNeow.RequireWarPaint) {
    throw 'This control helper expects the captured workload to have empty legacy constraints.'
}
$query.PSObject.Properties.Remove('HasCombatRewardConstraints')
$source.Options.ScanCount = 64
$source.Options.TargetMatchCount = 64
$source.Options.WorkerCount = 1
$fullOutput = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
$source | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $fullOutput -Encoding utf8
