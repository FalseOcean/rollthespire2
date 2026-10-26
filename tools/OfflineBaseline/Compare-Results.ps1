param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Modified,
    [Parameter(Mandatory = $true)][string]$Output
)
$ErrorActionPreference = 'Stop'
$before = Get-Content -Raw -LiteralPath $Baseline | ConvertFrom-Json
$after = Get-Content -Raw -LiteralPath $Modified | ConvertFrom-Json
if (!$before.Succeeded -or !$after.Succeeded -or $before.CpuExact.Status -ne 'Completed' -or $after.CpuExact.Status -ne 'Completed') {
    throw 'Both reports must contain successfully completed CPU Exact runs.'
}
$fields = @('Seed', 'RootHash', 'Disposition', 'IsMatch', 'FailureCode', 'EvaluationSha256')
$differences = @()
if ($before.CpuExact.Count -ne $after.CpuExact.Count) { $differences += 'Result count differs' }
for ($i = 0; $i -lt [Math]::Min($before.CpuExact.Results.Count, $after.CpuExact.Results.Count); $i++) {
    foreach ($field in $fields) {
        if ($before.CpuExact.Results[$i].$field -cne $after.CpuExact.Results[$i].$field) {
            $differences += "Row $i field $field differs"
        }
    }
}
$beforeMatches = @($before.CpuExact.Results | Where-Object IsMatch | Select-Object -ExpandProperty Seed)
$afterMatches = @($after.CpuExact.Results | Where-Object IsMatch | Select-Object -ExpandProperty Seed)
$baselineDuplicates = @($before.CpuExact.Results | Group-Object Seed | Where-Object Count -gt 1).Count
$modifiedDuplicates = @($after.CpuExact.Results | Group-Object Seed | Where-Object Count -gt 1).Count
$sameMatches = (@(Compare-Object $beforeMatches $afterMatches).Count -eq 0)
$sameWorkload = $before.Workload.TypedJsonSha256 -ceq $after.Workload.TypedJsonSha256
$sameNormalization = $before.QueryNormalization.NormalizedQuerySha256 -ceq $after.QueryNormalization.NormalizedQuerySha256
$sameContext = $before.Context.SemanticFingerprintSha256 -ceq $after.Context.SemanticFingerprintSha256
$report = [ordered]@{
    SchemaVersion = 1
    BaselineReport = [IO.Path]::GetFullPath($Baseline)
    ModifiedReport = [IO.Path]::GetFullPath($Modified)
    BaselineAssemblySha256 = $before.AssemblySha256
    ModifiedAssemblySha256 = $after.AssemblySha256
    SameTypedWorkload = $sameWorkload
    SameNormalizedQuery = $sameNormalization
    SameSemanticContext = $sameContext
    ComparedFieldsPerSeed = $fields
    BaselineCount = $before.CpuExact.Count
    ModifiedCount = $after.CpuExact.Count
    BaselineMatches = $beforeMatches.Count
    ModifiedMatches = $afterMatches.Count
    MatchSeeds = $beforeMatches
    SameMatchSet = $sameMatches
    BaselineDuplicateSeeds = $baselineDuplicates
    ModifiedDuplicateSeeds = $modifiedDuplicates
    Differences = $differences
    ResultsSha256 = $before.CpuExact.ResultsSha256
    BaselineExactEvaluationMilliseconds = $before.CpuExact.EvaluationMilliseconds
    ModifiedExactEvaluationMilliseconds = $after.CpuExact.EvaluationMilliseconds
    TimingLimitation = 'Small CPU-only correctness run with shared system activity, no speedup claim. GPU filtering and end-to-end search were not exercised.'
    Passed = ($sameWorkload -and $sameNormalization -and $sameContext -and $sameMatches -and $differences.Count -eq 0 -and $baselineDuplicates -eq 0 -and $modifiedDuplicates -eq 0)
}
$fullOutput = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
$report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $fullOutput -Encoding utf8
$report | ConvertTo-Json -Depth 15
if (!$report.Passed) { exit 1 }
