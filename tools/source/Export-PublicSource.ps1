param(
    [Parameter(Mandatory=$true)][string]$ProjectRoot,
    [Parameter(Mandatory=$true)][string]$SourceCommit,
    [Parameter(Mandatory=$true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$destinationPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$resolved = @(& git -C $root rev-parse --verify "$SourceCommit^{commit}")
if ($LASTEXITCODE -ne 0 -or $resolved.Count -ne 1 -or $resolved[0] -notmatch '^[0-9a-f]{40,64}$') {
    throw "SourceCommit does not resolve to one commit: $SourceCommit"
}
$commit = $resolved[0]
if (Test-Path -LiteralPath $destinationPath) {
    if (-not (Test-Path -LiteralPath $destinationPath -PathType Container) -or
        @(Get-ChildItem -LiteralPath $destinationPath -Force).Count -ne 0) {
        throw "Destination must be a new or empty directory: $destinationPath"
    }
}

# This is the single PublicSource selection used by both Git sync and ZIP delivery.
# Product directories (including Texts, shaders and hash-pinned resources) are kept.
# Internal documentation is opt-in; this one JSON is a live functional-test input.
$publicRootDocs = @('README.md', 'README.zh-CN.md', 'BUILDING.md', 'DEVELOPMENT.md', 'THIRD_PARTY_NOTICES.md')
$publicDocInputs = @('docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json')
$privateWorkflow = @('tools/Sync-SourceMain.ps1', 'tests/safety/verify_source_main_sync.py')
$internalDocChecks = @(
    'tests/diagnostics/verify_runtime_prediction_diagnostics.ps1',
    'tests/architecture/verify_combat_reward_route_policy_p10_rp1_1_2_fixture_discovery_hotfix.py',
    'tests/architecture/verify_predictor_act_boss_event_visual_hard_cut8_20260818.py',
    'tests/architecture/verify_predictor_ancient_event_relic_readability_convergence14_20260819.py',
    'tests/architecture/verify_predictor_ancient_final_convergence6_20260818.py',
    'tests/architecture/verify_predictor_bones_ancient_tail_warning_convergence17_20260819.py',
    'tests/architecture/verify_predictor_mixed_identity_readability_convergence12_20260818.py',
    'tests/architecture/verify_predictor_neow_effect_shop_relics_convergence16_20260819.py',
    'tests/architecture/verify_predictor_header_neow_visual_hard_cut2_20260818.py',
    'tests/architecture/verify_predictor_neow_result_surface_cleanup3_20260818.py',
    'tests/architecture/verify_predictor_opening_presentation_convergence13_20260818.py',
    'tests/architecture/verify_predictor_opening_route_chain_convergence10_20260818.py',
    'tests/architecture/verify_predictor_relic_shop_group_card_hard_cut5_20260818.py',
    'tests/architecture/verify_predictor_relic_shop_structure_convergence7_20260818.py',
    'tests/architecture/verify_predictor_result_space_relic_names_convergence15_20260819.py',
    'tests/architecture/verify_predictor_seed_report_presentation_hard_cut_20260818.py',
    'tests/architecture/verify_predictor_ui_narrow_convergence9_20260818.py'
)
function Test-PublicMember([string]$Relative) {
    if ($Relative -match '(^|/)(bin|obj|\.godot|\.git|\.vs|\.idea|TestResults|__pycache__|artifacts|\.artifacts|outputs|\.sourcecandidate-staging|\.sourcecandidate-output)(/|$)' -or
        $Relative -match '(^|/)\.(rewrite-build-evidence|build-evidence)[^/]*(/|$)' -or
        $Relative -match '(^|/)(local\.props|\.git)$' -or
        $Relative -match '\.(pdb|user|suo|pyc|tmp|bak|dmp|dump|log|dll|exe|pck)$' -or
        $Relative -match '(^|/)rt2_diagnostic_[^/]*\.jsonl$') { return $false }
    if ($Relative.StartsWith('docs/')) { return $Relative -in $publicDocInputs }
    if ($Relative -notmatch '/' -and $Relative.EndsWith('.md')) { return $Relative -in $publicRootDocs }
    if ($Relative -in $privateWorkflow -or $Relative -in $internalDocChecks) { return $false }
    if ($Relative.StartsWith('tools/AuthoredEventChecks/localization/')) { return $false }
    if ($Relative.StartsWith('tools/ProductionSearchHost/Corpus/')) { return $false }
    return $true
}

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
$temp = Join-Path $tempRoot ('rt2-public-export-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temp | Out-Null
    $archive = Join-Path $temp 'commit.zip'
    & git -C $root archive --format=zip "--output=$archive" $commit
    if ($LASTEXITCODE -ne 0) { throw "Could not archive source commit $commit" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $snapshot = Join-Path $temp 'snapshot'
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $snapshot)
    $committedPaths = @(& git -C $root -c core.quotepath=false ls-tree -r --name-only $commit)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate committed source files.' }
    $selected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($relative in $committedPaths) {
        if ($relative -match '[\r\n]' -or $relative.StartsWith('/') -or $relative -match '(^|/)\.\.(/|$)') { throw "Unsupported source path: $relative" }
        if (Test-PublicMember $relative) { [void]$selected.Add($relative) }
    }

    $corpusPrefix = 'tools/ProductionSearchHost/Corpus/'
    $manifestPath = Join-Path $snapshot ($corpusPrefix + 'v1/manifest.json')
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($relative in @('v1/manifest.json', 'v1/initial-results.json', 'v1/initial-build.json') + @($manifest.Cases | ForEach-Object { 'v1/' + $_.Workload })) {
        if ($relative -match '(^|/)\.\.(/|$)|^[\\/]|:' -or $relative.Contains('\')) { throw "Public Corpus path escapes its owner: $relative" }
        [void]$selected.Add($corpusPrefix + $relative)
        $inputDocument = Get-Content -LiteralPath (Join-Path $snapshot ($corpusPrefix + $relative)) -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($inputDocument.PSObject.Properties['Context'] -and $inputDocument.Context -is [System.Management.Automation.PSCustomObject]) {
            throw "Full runtime context is not a public minimal input: $relative"
        }
    }
    foreach ($required in $publicRootDocs + $publicDocInputs + @('LICENSE', 'RolltheSpire2.csproj', 'project.godot', 'RolltheSpire2.json', 'tools/source/Export-PublicSource.ps1')) {
        if (-not $selected.Contains($required) -or -not (Test-Path -LiteralPath (Join-Path $snapshot $required) -PathType Leaf)) {
            throw "Required public source input is absent from $commit : $required"
        }
    }

    # Never make an incomplete selection look successful. Check every explicit
    # project input and every committed wildcard match before writing public files.
    foreach ($project in @($committedPaths | Where-Object { $_ -like '*.csproj' -and $selected.Contains($_) })) {
        [xml]$xml = Get-Content -LiteralPath (Join-Path $snapshot $project) -Raw -Encoding UTF8
        $projectDirectory = Split-Path -Parent (Join-Path $snapshot $project)
        foreach ($node in $xml.SelectNodes('//*[@Include or @Project]')) {
            if ($node.Name -notin @('Compile', 'EmbeddedResource', 'None', 'Content', 'ProjectReference', 'Import')) { continue }
            $inputPath = if ($node.HasAttribute('Include')) { $node.GetAttribute('Include') } else { $node.GetAttribute('Project') }
            if ($inputPath.Contains('$(') -or $inputPath.Contains('@(')) { continue }
            foreach ($pattern in $inputPath.Split(';')) {
                $matches = @(Get-ChildItem -Path (Join-Path $projectDirectory $pattern) -File -ErrorAction SilentlyContinue)
                if ($pattern.Contains('**')) {
                    # .NET globs recurse; PowerShell's filesystem glob does not.
                    $prefix = $pattern.Substring(0, $pattern.IndexOf('**'))
                    $tail = $pattern.Substring($pattern.LastIndexOf('\') + 1)
                    $matches = @(Get-ChildItem -LiteralPath (Join-Path $projectDirectory $prefix) -Filter $tail -Recurse -File -ErrorAction SilentlyContinue)
                }
                if ($matches.Count -eq 0) { throw "Required project input missing at $commit : $project -> $pattern" }
                foreach ($input in $matches) {
                    $relative = $input.FullName.Substring($snapshot.Length + 1).Replace('\', '/')
                    if (-not $selected.Contains($relative)) { throw "Public selection omits project input: $project -> $relative" }
                }
            }
        }
    }
    New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
    foreach ($relative in $selected | Sort-Object) {
        $source = Join-Path $snapshot $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Selected committed file missing from archive: $relative" }
        if ($relative -match '\.(zip|7z)$') { throw "Unexpected nested archive selected: $relative" }
        $target = Join-Path $destinationPath $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $corpusReadme = @'
# Public baseline requests

This PublicSource tree contains only the v1 requests, initial expected results,
build provenance and manifest. Workload hashes identify the original frozen input.
The shared full runtime context and other internal datasets are not distributed.
RunCorpus.ps1 requires that matching context; it cannot run the complete frozen
benchmark from this public subset. Capture a fresh context and matching workload
using RequestCapture.ps1 and Run.ps1 for a separate dataset. Never substitute a
fresh capture while claiming the frozen baseline or its historical measurements.

See [the host guide](../README.md) and [development guide](../../../DEVELOPMENT.md)
for prerequisites and the available validation scope. Capture/replay implementation
is retained under Infrastructure/Snapshots and tools/ProductionSearchHost.
'@
    [System.IO.File]::WriteAllText((Join-Path $destinationPath ($corpusPrefix + 'README.md')), $corpusReadme.Replace("`r`n", "`n") + "`n", $utf8)
    # Keep useful development guidance, but do not leave links that require a
    # private task/audit/authority document. Product encyclopedia text is untouched.
    foreach ($relative in @('Search/README.md', 'tools/ProductionSearchHost/README.md')) {
        $path = Join-Path $destinationPath $relative
        $text = [System.IO.File]::ReadAllText($path)
        $text = [regex]::Replace($text, '\[([^\]]+)\]\(([^)]+)\)', {
            param($match)
            $link = $match.Groups[2].Value.Split('#')[0]
            if ($link -match '^\.\./(?:\.\./)?(?:docs/|AGENTS\.md|CURRENT_STATUS\.md|PRODUCT_STRATEGY\.md|PROJECT_ARCHITECTURE\.md)') {
                return $match.Groups[1].Value + ' (internal record omitted)'
            }
            return $match.Value
        })
        if (-not $text.StartsWith('> PublicSource:')) {
            $text = "> PublicSource: internal decision records and full captured datasets are omitted. See the public development guide in DEVELOPMENT.md at the source root for available checks and input requirements.`n`n" + $text
        }
        [System.IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n"), $utf8)
    }
    [System.IO.File]::WriteAllText((Join-Path $destinationPath 'SOURCE_PACKAGE.json'), "{`n  `"SchemaVersion`": 1,`n  `"PackageKind`": `"PublicSource`"`n}`n", $utf8)
    # Provenance belongs to the caller's private receipt, never the public tree.
    Write-Host "PublicSource exported: sourceCommit=$commit destination=$destinationPath"
}
finally {
    $cleanup = [System.IO.Path]::GetFullPath($temp)
    if (-not $cleanup.StartsWith($tempRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup target: $cleanup" }
    if (Test-Path -LiteralPath $cleanup) { Remove-Item -LiteralPath $cleanup -Recurse -Force }
}
