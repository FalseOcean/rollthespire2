param(
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$Output,
    [string[]]$Case,
    [string]$CorpusDirectory,
    [int]$Repeat = 2,
    [ValidateSet('vulkan','d3d12')][string]$Driver = 'd3d12',
    [int]$TimeoutSeconds = 180,
    [string]$ReferenceResults,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$corpus = if ($CorpusDirectory) { (Resolve-Path -LiteralPath $CorpusDirectory).Path } else { Join-Path $PSScriptRoot 'Corpus/v1' }
$manifest = Get-Content (Join-Path $corpus 'manifest.json') -Raw | ConvertFrom-Json
$context = Join-Path $corpus $manifest.Context
if ((Get-FileHash $context).Hash -ne $manifest.ContextSha256) { throw 'Corpus context changed; version the baseline explicitly.' }
$selected = @($manifest.Cases | Where-Object { -not $Case -or $_.Id -in $Case })
if ($selected.Count -eq 0 -or ($Case -and @($Case | Where-Object {$_ -notin $manifest.Cases.Id}).Count)) { throw 'Unknown corpus case.' }
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a fresh output directory to preserve before/after evidence.' }
foreach ($entry in $selected) {
    if ((Get-FileHash (Join-Path $corpus $entry.Workload)).Hash -ne $entry.Sha256) { throw "Corpus workload changed: $($entry.Id)" }
}
if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot 'ProductionSearchHost.csproj') -c Release --nologo -p:Rt2ModsDir= -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
New-Item -ItemType Directory -Path $outputPath | Out-Null
$report = @()
$reference = if ($ReferenceResults) { @(Get-Content $ReferenceResults -Raw | ConvertFrom-Json) } else { @() }
foreach ($entry in $selected) {
    $caseOutput = Join-Path $outputPath $entry.Id
    & (Join-Path $PSScriptRoot 'Run.ps1') -Godot $Godot -Context $context -Workload (Join-Path $corpus $entry.Workload) -Output $caseOutput -Driver $Driver -Repeat $Repeat -TimeoutSeconds $TimeoutSeconds -SkipBuild
    $summaryFiles = @(Get-ChildItem -LiteralPath $caseOutput -Filter 'run*.summary.json' | Sort-Object Name)
    if ($summaryFiles.Count -ne $Repeat) { throw "Incomplete corpus run: $($entry.Id); expected $Repeat summaries, found $($summaryFiles.Count). Inspect $caseOutput/runtime.log" }
    foreach ($summaryFile in $summaryFiles) {
        $summary = Get-Content $summaryFile.FullName -Raw | ConvertFrom-Json
        $matches = $summaryFile.FullName.Replace('.summary.json','.matches.jsonl')
        if ($summary.progress.State -ne 'Completed' -or $summary.progress.ScannedCount -ne $entry.Roots) { throw "Incomplete corpus run: $($entry.Id)" }
        $digest = (Get-FileHash $matches).Hash
        # Exact publishes concurrently within a batch. Preserve the raw order
        # hash, but compare complete records as a multiset (duplicates retained).
        [string[]]$records = [IO.File]::ReadAllLines($matches)
        [Array]::Sort($records, [StringComparer]::Ordinal)
        $normalizedDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $records))))
        $prior = @($reference | Where-Object Case -eq $entry.Id)
        if ($ReferenceResults -and $prior.Count -eq 0) { throw "Missing reference case: $($entry.Id)" }
        foreach ($baseline in $prior) {
            if ($baseline.ContextSha256 -ne $manifest.ContextSha256 -or $baseline.WorkloadSha256 -ne $entry.Sha256) { throw "Different baseline inputs: $($entry.Id)" }
            if ($baseline.NormalizedResultSha256 -ne $normalizedDigest -or $baseline.Cursor -ne $summary.safeNextOrdinal) { throw "Exact result/cursor mismatch: $($entry.Id)" }
        }
        if (@($report | Where-Object { $_.Case -eq $entry.Id -and $_.NormalizedResultSha256 -ne $normalizedDigest }).Count) { throw "Repeat result mismatch: $($entry.Id)" }
        $runLabel = $summaryFile.Name.Replace('.summary.json','')
        $log = Get-Content (Join-Path $caseOutput 'runtime.log') -Raw
        $segment = [regex]::Match($log, '(?ms)^standaloneRun=' + [regex]::Escape($runLabel) + ';.*?(?=^standaloneRun=|\z)').Value
        $revisions = @([regex]::Matches($segment, '(?:physicalImplementationRevision|physicalRevision)=([^;\r\n,]+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
        $report += [ordered]@{Case=$entry.Id;Run=$summaryFile.BaseName;Families=$summary.families;PhysicalRevisions=$revisions;Roots=$summary.progress.ScannedCount;Matches=$summary.progress.MatchCount;Cursor=$summary.safeNextOrdinal;SessionSeconds=$summary.progress.ElapsedSeconds;ResultSha256=$digest;NormalizedResultSha256=$normalizedDigest;WorkloadSha256=$entry.Sha256;ContextSha256=$manifest.ContextSha256;Driver=$Driver}
    }
    $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $outputPath 'results.json')
}
Write-Output "Corpus complete: $outputPath/results.json. Compare actual physical revisions and ordering in each runtime.log before pricing a speedup."
