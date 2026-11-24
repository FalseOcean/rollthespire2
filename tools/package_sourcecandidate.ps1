param(
    [string]$ProjectRoot = '',
    [Parameter(Mandatory=$true)][string]$OutputZip,
    [ValidateSet('Internal', 'PublicSource')][string]$PackageKind = 'Internal',
    [string]$SourceCommit = 'HEAD'
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Join-Path $PSScriptRoot '..' }
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
if ([System.IO.Path]::GetExtension($out) -ne '.zip') { throw 'OutputZip must name a .zip file.' }
if (Test-Path -LiteralPath $out -PathType Container) { throw "OutputZip names a directory, not a file: $out" }
if (-not (Test-Path -LiteralPath (Join-Path $root 'RolltheSpire2.csproj') -PathType Leaf)) {
    throw "ProjectRoot is not a RolltheSpire2 source tree: $root"
}
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
$temp = Join-Path $tempRoot ("rt2-sourcecandidate-" + [guid]::NewGuid().ToString("N"))
$stage = Join-Path $temp "RolltheSpire2"

try {
    if ($PackageKind -eq 'PublicSource') {
        # Resolve once: neither branch movement nor workspace edits may change the input.
        $resolvedCommit = @(& git -C $root rev-parse --verify "$SourceCommit^{commit}")
        if ($LASTEXITCODE -ne 0 -or $resolvedCommit.Count -ne 1) { throw "Invalid SourceCommit: $SourceCommit" }
        & (Join-Path $PSScriptRoot 'source/Export-PublicSource.ps1') -ProjectRoot $root -SourceCommit $resolvedCommit[0] -Destination $stage
    }
    else {
        New-Item -ItemType Directory -Force -Path $stage | Out-Null

        # Directory names are passed as names so nested test/build output is excluded too.
        # Historical evidence paths are repository history, not ordinary Development handoff input.
        $excludedDirNames = @(
            "bin", "obj", ".godot", "artifacts", ".artifacts", ".vs", ".idea", "TestResults", "__pycache__",
            ".sourcecandidate-staging", ".sourcecandidate-output", ".git", ".build-evidence*", ".rewrite-build-evidence*"
        )
        $excludedHistoryPaths = @(
            "docs\archive", "docs\delivery_evidence", "docs\validation", "docs\reports",
            "docs\clean_baseline", "docs\implementation_reports"
        )

        $xd = @()
        $xd += $excludedDirNames
        $xd += $temp
        $xd += (Join-Path $root 'tools\AuthoredEventChecks\localization')
        foreach ($relative in $excludedHistoryPaths) { $xd += (Join-Path $root $relative) }

        # .git is a file in linked worktrees. Machine configuration/logs and a prior
        # copy of the requested output must not become part of the source package.
        $xf = @('.git', 'local.props', '*.pdb', '*.user', '*.suo', '*.pyc', '*.tmp', '*.bak', '*.dmp', '*.dump', '*.log', 'rt2_diagnostic_*.jsonl', $out)
        # Audit text stays in the handoff; attached historical evidence archives stay
        # in the repository. Do not blanket-exclude archives elsewhere (or .gz resources).
        $auditRoot = Join-Path $root 'docs\source_audits'
        if (Test-Path -LiteralPath $auditRoot -PathType Container) {
            foreach ($archive in Get-ChildItem -LiteralPath $auditRoot -File -Recurse -Force |
                Where-Object { $_.Extension -in '.zip', '.7z' }) {
                $xf += $archive.FullName
                Write-Host "Excluded repository-only audit archive: $($archive.FullName.Substring($root.TrimEnd('\', '/').Length + 1))"
            }
        }

        & robocopy $root $stage /E /XJ /NFL /NDL /NJH /NJS /NP /XD $xd /XF $xf | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "SourceCandidate copy failed (robocopy exit $LASTEXITCODE): $root -> $stage" }

        [ordered]@{ SchemaVersion = 1; PackageKind = $PackageKind } | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $stage 'SOURCE_PACKAGE.json') -Encoding UTF8
    }

    # A current source package should not recursively carry another delivery/source archive.
    $nestedArchives = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force |
        Where-Object { $_.Extension -in '.zip', '.7z' })
    if ($nestedArchives.Count -gt 0) {
        $paths = ($nestedArchives | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }) -join [Environment]::NewLine
        throw "Unexpected nested archives in source tree '$root' (outside repository-only audit/history exclusions). Review these inputs; no source files were removed:`n$paths"
    }

    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($out)) | Out-Null
    # ZipFile includes hidden files; Compress-Archive can silently omit them.
    # Finish the new archive before replacing an existing successful output.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stagedArchive = Join-Path $temp 'SourceCandidate.zip'
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $stagedArchive, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    Copy-Item -LiteralPath $stagedArchive -Destination $out -Force
    Write-Host "$PackageKind source package: $out"
}
finally {
    $cleanupPath = [System.IO.Path]::GetFullPath($temp)
    if (-not $cleanupPath.StartsWith($tempRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside temporary directory: $cleanupPath"
    }
    if (Test-Path -LiteralPath $cleanupPath) { Remove-Item -LiteralPath $cleanupPath -Recurse -Force }
}
