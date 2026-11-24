$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$resolverPath = Join-Path $projectRoot 'tools\build\Resolve-Sts2Runtime.ps1'
. $resolverPath

$oldReferenceEnv = $env:STS2_REFERENCE_DIR
$oldPathEnv = $env:STS2_PATH
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('rt2-sts2-discovery-' + [Guid]::NewGuid().ToString('N'))
$mainSteamRoot = Join-Path $tempRoot 'MainSteam'
$secondaryLibraryRoot = Join-Path $tempRoot ('Secondary Steam ' + [char]0x6D4B + [char]0x8BD5)
$gameRoot = Join-Path $secondaryLibraryRoot 'steamapps\common\Slay the Spire 2'
$referenceDir = Join-Path $gameRoot 'data_sts2_windows_x86_64'

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $mainSteamRoot 'steamapps') | Out-Null
    New-Item -ItemType Directory -Force -Path $referenceDir | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $referenceDir 'sts2.dll') | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $referenceDir '0Harmony.dll') | Out-Null

    $escapedLibrary = $secondaryLibraryRoot.Replace('\', '\\')
    $libraryFolders = @"
"libraryfolders"
{
    "0"
    {
        "path" "$escapedLibrary"
    }
}
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $mainSteamRoot 'steamapps\libraryfolders.vdf'),
        $libraryFolders,
        (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false))

    $manifest = @"
"AppState"
{
    "appid" "2868840"
    "installdir" "Slay the Spire 2"
}
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $secondaryLibraryRoot 'steamapps\appmanifest_2868840.acf'),
        $manifest,
        (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false))

    $env:STS2_REFERENCE_DIR = $null
    $env:STS2_PATH = $null
    $invalidExplicit = Join-Path $tempRoot 'InvalidExplicitReference'
    $result = Resolve-Rt2Sts2Runtime `
        -ProjectRoot $tempRoot `
        -PreferredReferenceDir $invalidExplicit `
        -AdditionalSteamRoots @($mainSteamRoot) `
        -SkipMachineDiscovery

    if ([string]::IsNullOrWhiteSpace($result.ReferenceDir)) {
        throw 'Resolver returned no runtime directory.'
    }

    $expectedReference = (Resolve-Path -LiteralPath $referenceDir).Path
    $expectedGameRoot = (Resolve-Path -LiteralPath $gameRoot).Path
    if ($result.ReferenceDir -ne $expectedReference) {
        throw "ReferenceDir mismatch. expected='$expectedReference' actual='$($result.ReferenceDir)'"
    }
    if ($result.GameRoot -ne $expectedGameRoot) {
        throw "GameRoot mismatch. expected='$expectedGameRoot' actual='$($result.GameRoot)'"
    }
    if ($result.Source -ne 'SteamLibraryFolders') {
        throw "Discovery source mismatch. expected='SteamLibraryFolders' actual='$($result.Source)'"
    }
    if (-not (Test-Rt2Sts2ReferenceDirectory -Path $result.ReferenceDir)) {
        throw 'Resolved directory does not contain both required DLLs.'
    }

    Write-Host 'InvalidExplicitPathFallback=true'
    Write-Host 'SteamLibraryFoldersDiscovery=true'
    Write-Host 'SiblingDllValidation=true'

    # Missing optional properties must not abort the XML reader. Relative literal
    # paths belong to ProjectRoot, even when invoked from a different directory.
    $relativeGameRoot = $gameRoot.Substring($tempRoot.Length + 1)
    $escapedGameRoot = [System.Security.SecurityElement]::Escape($relativeGameRoot)
    [System.IO.File]::WriteAllText((Join-Path $tempRoot 'local.props'),
        "<Project><PropertyGroup><Sts2Path>$escapedGameRoot</Sts2Path></PropertyGroup></Project>")
    $localGame = Resolve-Rt2Sts2Runtime -ProjectRoot $tempRoot -SkipMachineDiscovery
    if ($localGame.ReferenceDir -ne $expectedReference -or $localGame.Source -ne 'LocalPropsGameRoot') {
        throw 'local.props with only a relative Sts2Path did not resolve from ProjectRoot.'
    }
    $escapedReference = [System.Security.SecurityElement]::Escape($referenceDir.Substring($tempRoot.Length + 1))
    [System.IO.File]::WriteAllText((Join-Path $tempRoot 'local.props'),
        "<Project><PropertyGroup><Unrelated>true</Unrelated><Sts2ReferenceDir>missing</Sts2ReferenceDir></PropertyGroup><PropertyGroup><Sts2ReferenceDir>$escapedReference</Sts2ReferenceDir></PropertyGroup></Project>")
    $localReference = Resolve-Rt2Sts2Runtime -ProjectRoot $tempRoot -SkipMachineDiscovery
    if ($localReference.ReferenceDir -ne $expectedReference -or $localReference.Source -ne 'LocalPropsReferenceDir') {
        throw 'local.props must read the last literal Sts2ReferenceDir without requiring Sts2Path.'
    }
    Push-Location -LiteralPath $tempRoot
    try { $relativeProject = Resolve-Rt2Sts2Runtime -ProjectRoot '.' -SkipMachineDiscovery }
    finally { Pop-Location }
    if ($relativeProject.ReferenceDir -ne $expectedReference -or $relativeProject.Source -ne 'LocalPropsReferenceDir') {
        throw 'Relative ProjectRoot must use the PowerShell current location.'
    }
    Write-Host 'LocalPropsOptionalPropertiesAndRelativePaths=true'
    Write-Host 'SpaceAndUnicodePaths=true'
    Write-Host 'Sts2RuntimeAutoDiscoveryFixturePassed=true'
} finally {
    $env:STS2_REFERENCE_DIR = $oldReferenceEnv
    $env:STS2_PATH = $oldPathEnv
    $cleanupPath = [System.IO.Path]::GetFullPath($tempRoot)
    $allowedRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $cleanupPath.StartsWith($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside temporary directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force -ErrorAction SilentlyContinue
}
