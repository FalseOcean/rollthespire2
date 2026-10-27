param(
    [string]$ProjectRoot = '',
    [switch]$RequireAutomaticDiscovery
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path $PSScriptRoot '..\..'
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$project = Join-Path $ProjectRoot 'RolltheSpire2.csproj'
$props = Join-Path $ProjectRoot 'Sts2PathDiscovery.props'
$localProps = Join-Path $ProjectRoot 'local.props'

function Assert-Contains([string]$Text, [string]$Needle, [string]$Name) {
    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        throw "$Name missing expected text: $Needle"
    }
}

$propsText = Get-Content -LiteralPath $props -Raw
$projectText = Get-Content -LiteralPath $project -Raw
Assert-Contains $propsText 'Steam App 2868840' 'registry uninstall discovery'
Assert-Contains $propsText 'HKEY_CURRENT_USER\Software\Valve\Steam' 'SteamPath discovery'
Assert-Contains $propsText 'C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2' 'Windows default discovery'
Assert-Contains $propsText 'data_sts2_linuxbsd_x86_64' 'Linux discovery'
Assert-Contains $propsText 'data_sts2_macos_x86_64' 'macOS discovery'
Assert-Contains $propsText 'Exists(''$(_Rt2RegistryReferenceDir)/sts2.dll'')' 'registry sts2 validation'
Assert-Contains $propsText 'Exists(''$(_Rt2RegistryReferenceDir)/0Harmony.dll'')' 'registry Harmony validation'
Assert-Contains $projectText 'Could not locate the Slay the Spire 2 runtime assemblies.' 'clear error message'
Assert-Contains $projectText '-p:Sts2ReferenceDir=&lt;folder&gt;' 'command-line override help'
Assert-Contains $projectText '<Target Name="CopyModToMods"' 'existing install target'
Write-Host 'staticPathDiscoveryContract=Passed'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'msbuildDynamicChecks=PendingDotnet'
    exit 0
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('rt2-path-discovery-' + [Guid]::NewGuid().ToString('N'))
$backupLocalProps = $null
$oldReferenceEnv = $env:STS2_REFERENCE_DIR
$oldModsEnv = $env:RT2_MODS_DIR

function New-FakeReferenceDir([string]$Name, [bool]$Sts2 = $true, [bool]$Harmony = $true) {
    $dir = Join-Path $tempRoot $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if ($Sts2) { New-Item -ItemType File -Force -Path (Join-Path $dir 'sts2.dll') | Out-Null }
    if ($Harmony) { New-Item -ItemType File -Force -Path (Join-Path $dir '0Harmony.dll') | Out-Null }
    return $dir
}

function Invoke-Validate([string[]]$ExtraArgs, [bool]$ExpectSuccess = $true) {
    $arguments = @('msbuild', $project, '-nologo', '-target:ValidateSts2ReferenceDir') + $ExtraArgs
    $output = & dotnet @arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($ExpectSuccess -and $code -ne 0) { throw "Expected success, got $code`n$output" }
    if (-not $ExpectSuccess -and $code -eq 0) { throw "Expected failure, got success`n$output" }
    return $output
}

try {
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    if (Test-Path -LiteralPath $localProps) {
        $backupLocalProps = Join-Path $tempRoot 'local.props.backup'
        Copy-Item -LiteralPath $localProps -Destination $backupLocalProps
        Remove-Item -LiteralPath $localProps -Force
    }

    $explicitRef = New-FakeReferenceDir 'explicit-ref'
    $environmentRef = New-FakeReferenceDir 'environment-ref'
    $localRef = New-FakeReferenceDir 'local-ref'
    $explicitMods = Join-Path $tempRoot 'explicit-mods'
    $environmentMods = Join-Path $tempRoot 'environment-mods'
    $localMods = Join-Path $tempRoot 'local-mods'

    $env:STS2_REFERENCE_DIR = $environmentRef
    $env:RT2_MODS_DIR = $environmentMods
    $output = Invoke-Validate @("-p:Sts2ReferenceDir=$explicitRef", "-p:Rt2ModsDir=$explicitMods")
    Assert-Contains $output 'source=Explicit' 'explicit priority source'
    Assert-Contains $output $explicitRef 'explicit priority reference'
    Assert-Contains $output $explicitMods 'explicit priority mods'
    Write-Host 'explicitPropertiesPriority=Passed'

    $output = Invoke-Validate @()
    Assert-Contains $output 'source=Environment' 'environment source'
    Assert-Contains $output $environmentRef 'environment reference'
    Assert-Contains $output $environmentMods 'environment mods'
    Write-Host 'environmentVariables=Passed'

    $env:STS2_REFERENCE_DIR = $null
    $env:RT2_MODS_DIR = $null
    @"
<Project>
  <PropertyGroup>
    <Sts2ReferenceDir>$localRef</Sts2ReferenceDir>
    <Rt2ModsDir>$localMods</Rt2ModsDir>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $localProps -Encoding UTF8
    $output = Invoke-Validate @()
    Assert-Contains $output 'source=LocalProps' 'local.props source'
    Assert-Contains $output $localRef 'local.props reference'
    Assert-Contains $output $localMods 'local.props mods'
    Write-Host 'localPropsDirectValues=Passed'

    $gameRoot = Join-Path $tempRoot 'game-root'
    $dataDir = Join-Path $gameRoot 'data_sts2_windows_x86_64'
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $dataDir 'sts2.dll') | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $dataDir '0Harmony.dll') | Out-Null
    @"
<Project>
  <PropertyGroup>
    <Sts2Path>$gameRoot</Sts2Path>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $localProps -Encoding UTF8
    $output = Invoke-Validate @()
    Assert-Contains $output $gameRoot 'Sts2Path derived game root'
    Assert-Contains $output 'data_sts2_windows_x86_64' 'Sts2Path derived reference'
    Assert-Contains $output 'modsDir=' 'Sts2Path derived mods log'
    Assert-Contains $output '/mods' 'Sts2Path derived mods'
    Write-Host 'localPropsGameRootDerivation=Passed'

    Remove-Item -LiteralPath $localProps -Force
    $onlySts2 = New-FakeReferenceDir 'only-sts2' $true $false
    $output = Invoke-Validate @("-p:Sts2ReferenceDir=$onlySts2") $false
    Assert-Contains $output '0Harmony.dll' 'missing Harmony error'
    Assert-Contains $output 'STS2_REFERENCE_DIR' 'missing Harmony override help'
    Write-Host 'missingHarmonyFails=Passed'

    $onlyHarmony = New-FakeReferenceDir 'only-harmony' $false $true
    $output = Invoke-Validate @("-p:Sts2ReferenceDir=$onlyHarmony") $false
    Assert-Contains $output 'sts2.dll' 'missing sts2 error'
    Assert-Contains $output 'local.props' 'missing sts2 override help'
    Write-Host 'missingSts2Fails=Passed'

    if ($RequireAutomaticDiscovery) {
        $output = Invoke-Validate @()
        Assert-Contains $output 'RT2 path discovery source=' 'automatic discovery log'
        if ($output -match 'source=(Explicit|Environment|LocalProps|NotFound)') {
            throw "Automatic Steam discovery was required but did not resolve from Registry/SteamDefault.`n$output"
        }
        Write-Host 'windowsSteamAutomaticDiscovery=Passed'
    }
    else {
        Write-Host 'windowsSteamAutomaticDiscovery=ContractChecked_RunWithRequireAutomaticDiscoveryForInstalledGame'
    }

    Write-Host 'pathDiscoveryVerification=Passed'
}
finally {
    $env:STS2_REFERENCE_DIR = $oldReferenceEnv
    $env:RT2_MODS_DIR = $oldModsEnv
    if (Test-Path -LiteralPath $localProps) { Remove-Item -LiteralPath $localProps -Force }
    if ($null -ne $backupLocalProps -and (Test-Path -LiteralPath $backupLocalProps)) {
        Copy-Item -LiteralPath $backupLocalProps -Destination $localProps
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
