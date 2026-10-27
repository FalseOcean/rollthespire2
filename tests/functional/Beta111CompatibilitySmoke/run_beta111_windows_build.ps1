param(
    [string]$ProjectRoot = '',
    [string]$Sts2ReferenceDir = '',
    [string]$ReleaseInfoPath = '',
    [string]$Configuration = 'Release',
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ProjectRoot 'artifacts\beta111-compatibility'
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$resolvedGameRoot = ''
$resolverPath = Join-Path $ProjectRoot 'tools\build\Resolve-Sts2Runtime.ps1'
if (Test-Path -LiteralPath $resolverPath) {
    . $resolverPath
    $runtime = Resolve-Rt2Sts2Runtime -ProjectRoot $ProjectRoot -PreferredReferenceDir $Sts2ReferenceDir
    if ([string]::IsNullOrWhiteSpace($runtime.ReferenceDir)) {
        throw 'Could not locate v0.111 sts2.dll and 0Harmony.dll.'
    }
    $Sts2ReferenceDir = $runtime.ReferenceDir
    $resolvedGameRoot = $runtime.GameRoot
}
if ([string]::IsNullOrWhiteSpace($Sts2ReferenceDir)) {
    throw 'Sts2ReferenceDir is required when automatic resolver is unavailable.'
}
$Sts2ReferenceDir = (Resolve-Path -LiteralPath $Sts2ReferenceDir).Path
if ([string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
    $resolvedGameRoot = Split-Path -Parent $Sts2ReferenceDir
}

$sts2 = Join-Path $Sts2ReferenceDir 'sts2.dll'
$harmony = Join-Path $Sts2ReferenceDir '0Harmony.dll'
if (!(Test-Path -LiteralPath $sts2 -PathType Leaf) -or !(Test-Path -LiteralPath $harmony -PathType Leaf)) {
    throw "Expected sts2.dll and 0Harmony.dll under $Sts2ReferenceDir"
}

if ([string]::IsNullOrWhiteSpace($ReleaseInfoPath)) {
    $releaseCandidates = @(
        (Join-Path $resolvedGameRoot 'release_info.json'),
        (Join-Path $resolvedGameRoot 'SlayTheSpire2_Data\release_info.json')
    )
    $ReleaseInfoPath = $releaseCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($ReleaseInfoPath) -or !(Test-Path -LiteralPath $ReleaseInfoPath -PathType Leaf)) {
    throw 'Could not find release_info.json for the supplied runtime. Pass -ReleaseInfoPath explicitly; Beta111 build evidence must prove the assemblies came from a 0.111.0 game tree.'
}
$ReleaseInfoPath = (Resolve-Path -LiteralPath $ReleaseInfoPath).Path
try {
    $release = Get-Content -LiteralPath $ReleaseInfoPath -Raw | ConvertFrom-Json
} catch {
    throw "Could not parse release_info.json at $ReleaseInfoPath : $($_.Exception.Message)"
}
$releaseVersion = [string]$release.version
$normalizedReleaseVersion = $releaseVersion.Trim()
if ($normalizedReleaseVersion.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedReleaseVersion = $normalizedReleaseVersion.Substring(1)
}
if ($normalizedReleaseVersion -ne '0.111.0') {
    throw "Beta111 compatibility build requires game version 0.111.0; release_info.json reported '$releaseVersion'."
}

$evidence = Join-Path $OutputDir 'beta111_reference_evidence.txt'
$sts2Hash = (Get-FileHash -LiteralPath $sts2 -Algorithm SHA256).Hash.ToLowerInvariant()
$harmonyHash = (Get-FileHash -LiteralPath $harmony -Algorithm SHA256).Hash.ToLowerInvariant()
$releaseHash = (Get-FileHash -LiteralPath $ReleaseInfoPath -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    'beta111ReferenceEvidence=true',
    'gameVersion=0.111.0',
    "gameRoot=$resolvedGameRoot",
    "referenceDir=$Sts2ReferenceDir",
    "releaseInfoPath=$ReleaseInfoPath",
    "releaseInfoSha256=$releaseHash",
    "sts2Sha256=$sts2Hash",
    "harmonySha256=$harmonyHash",
    "configuration=$Configuration"
) | Set-Content -LiteralPath $evidence -Encoding UTF8

$buildLog = Join-Path $OutputDir 'beta111_release_build.log'
Push-Location $ProjectRoot
try {
    & dotnet build .\RolltheSpire2.csproj -c $Configuration -p:Sts2ReferenceDir="$Sts2ReferenceDir" 2>&1 |
        Tee-Object -FilePath $buildLog
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}
finally { Pop-Location }
Write-Host "beta111Build=Passed;gameVersion=0.111.0;referenceEvidence=$evidence;buildLog=$buildLog"
