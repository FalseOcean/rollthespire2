Set-StrictMode -Version 2.0

function Test-Rt2Sts2ReferenceDirectory {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    return (Test-Path -LiteralPath (Join-Path $Path 'sts2.dll') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Path '0Harmony.dll') -PathType Leaf)
}

function ConvertFrom-Rt2SteamVdfEscapedPath {
    [CmdletBinding()]
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    return $Path.Replace('\\', '\').Trim().TrimEnd([char[]]@('\', '/'))
}

function Get-Rt2SteamLibraryRootsFromVdf {
    [CmdletBinding()]
    param([string]$SteamRoot)

    if ([string]::IsNullOrWhiteSpace($SteamRoot)) {
        return @()
    }

    $libraryFile = Join-Path $SteamRoot 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) {
        return @()
    }

    try {
        $text = [System.IO.File]::ReadAllText($libraryFile)
    } catch {
        Write-Warning "Could not read Steam library file '$libraryFile': $($_.Exception.Message)"
        return @()
    }

    $results = New-Object System.Collections.Generic.List[string]

    # Current Steam VDF format: "path" "D:\\SteamLibrary"
    foreach ($match in [regex]::Matches($text, '"path"\s*"(?<path>[^"]+)"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $candidate = ConvertFrom-Rt2SteamVdfEscapedPath $match.Groups['path'].Value
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $results.Add($candidate)
        }
    }

    # Older Steam VDF format: "1" "D:\\SteamLibrary"
    foreach ($match in [regex]::Matches($text, '"\d+"\s*"(?<path>[A-Za-z]:\\\\[^"]+)"')) {
        $candidate = ConvertFrom-Rt2SteamVdfEscapedPath $match.Groups['path'].Value
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $results.Add($candidate)
        }
    }

    return $results.ToArray()
}

function Get-Rt2RegistryValueSafe {
    [CmdletBinding()]
    param(
        [string]$Path,
        [string]$Name
    )

    try {
        $item = Get-ItemProperty -LiteralPath $Path -ErrorAction Stop
        $value = $item.$Name
        if ($null -ne $value) {
            return [string]$value
        }
    } catch {
        return $null
    }

    return $null
}

function Resolve-Rt2Sts2Runtime {
    [CmdletBinding()]
    param(
        [string]$ProjectRoot = '',
        [string]$PreferredReferenceDir = '',
        [string]$PreferredGameRoot = '',
        [string[]]$AdditionalSteamRoots = @(),
        [switch]$SkipMachineDiscovery
    )

    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $ProjectRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ProjectRoot)
    }

    $isWindowsPlatform = $env:OS -eq 'Windows_NT'
    $isLinuxVariable = Get-Variable -Name IsLinux -ErrorAction SilentlyContinue
    $isMacVariable = Get-Variable -Name IsMacOS -ErrorAction SilentlyContinue
    $isLinuxPlatform = (-not $isWindowsPlatform) -and ($null -ne $isLinuxVariable) -and ([bool]$isLinuxVariable.Value)
    $isMacPlatform = (-not $isWindowsPlatform) -and ($null -ne $isMacVariable) -and ([bool]$isMacVariable.Value)
    $platformReferenceRelativePath = 'data_sts2_windows_x86_64'
    if ($isLinuxPlatform) {
        $platformReferenceRelativePath = 'data_sts2_linuxbsd_x86_64'
    } elseif ($isMacPlatform) {
        $platformReferenceRelativePath = 'SlayTheSpire2.app/Contents/Resources/data_sts2_macos_x86_64'
    }

    $checked = New-Object System.Collections.Generic.List[string]
    $seenReferences = New-Object -TypeName 'System.Collections.Generic.HashSet[string]' -ArgumentList ([System.StringComparer]::OrdinalIgnoreCase)
    $seenSteamRoots = New-Object -TypeName 'System.Collections.Generic.HashSet[string]' -ArgumentList ([System.StringComparer]::OrdinalIgnoreCase)
    $referenceCandidates = New-Object System.Collections.Generic.List[object]
    $steamRoots = New-Object System.Collections.Generic.List[string]

    function Add-ReferenceCandidate {
        param(
            [string]$Path,
            [string]$Source,
            [string]$GameRoot = ''
        )

        if ([string]::IsNullOrWhiteSpace($Path)) {
            return
        }

        $normalized = $Path.Trim().TrimEnd([char[]]@('\', '/'))
        if ($seenReferences.Add($normalized)) {
            $referenceCandidates.Add([pscustomobject]@{
                ReferenceDir = $normalized
                GameRoot = $GameRoot
                Source = $Source
            })
        }
    }

    function Add-GameRootCandidate {
        param(
            [string]$Path,
            [string]$Source
        )

        if ([string]::IsNullOrWhiteSpace($Path)) {
            return
        }

        $gameRoot = $Path.Trim().TrimEnd([char[]]@('\', '/'))
        Add-ReferenceCandidate -Path (Join-Path $gameRoot $platformReferenceRelativePath) -Source $Source -GameRoot $gameRoot
    }

    function Add-SteamRoot {
        param([string]$Path)

        if ([string]::IsNullOrWhiteSpace($Path)) {
            return
        }

        $normalized = $Path.Trim().TrimEnd([char[]]@('\', '/'))
        if ($seenSteamRoots.Add($normalized)) {
            $steamRoots.Add($normalized)
        }
    }

    # Explicit input is tried first, but an invalid value no longer disables
    # automatic discovery. This is intentional for the G0-A developer wrapper.
    Add-ReferenceCandidate -Path $PreferredReferenceDir -Source 'ExplicitReferenceDir'
    Add-ReferenceCandidate -Path $env:STS2_REFERENCE_DIR -Source 'EnvironmentReferenceDir'
    Add-GameRootCandidate -Path $PreferredGameRoot -Source 'ExplicitGameRoot'
    Add-GameRootCandidate -Path $env:STS2_PATH -Source 'EnvironmentGameRoot'

    # Read the same per-machine local.props contract used by the project.
    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $localPropsPath = Join-Path $ProjectRoot 'local.props'
        if (Test-Path -LiteralPath $localPropsPath -PathType Leaf) {
            try {
                [xml]$localProps = [System.IO.File]::ReadAllText($localPropsPath)
                # Missing optional XML properties must not throw under StrictMode.
                # This wrapper reads literal paths; MSBuild expressions/conditions
                # are evaluated only by the project, not by this XML reader.
                foreach ($propertyName in @('Sts2ReferenceDir', 'Sts2Path')) {
                    $node = $localProps.SelectSingleNode("(Project/PropertyGroup/$propertyName)[last()]")
                    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) { continue }
                    $localPath = $node.InnerText.Trim()
                    if (-not [System.IO.Path]::IsPathRooted($localPath)) {
                        $localPath = Join-Path $ProjectRoot $localPath
                    }
                    if ($propertyName -eq 'Sts2ReferenceDir') {
                        Add-ReferenceCandidate -Path $localPath -Source 'LocalPropsReferenceDir'
                    } else {
                        Add-GameRootCandidate -Path $localPath -Source 'LocalPropsGameRoot'
                    }
                }
            } catch {
                Write-Warning "Could not parse '$localPropsPath': $($_.Exception.Message)"
            }
        }
    }

    foreach ($additionalRoot in $AdditionalSteamRoots) {
        Add-SteamRoot $additionalRoot
    }

    if ($isWindowsPlatform) {
        # Legacy107-compatible uninstall and Steam registry discovery.
        if (-not $SkipMachineDiscovery) {
        $uninstallKeys = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2868840',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2868840',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2868840'
        )
        foreach ($key in $uninstallKeys) {
            Add-GameRootCandidate -Path (Get-Rt2RegistryValueSafe -Path $key -Name 'InstallLocation') -Source 'SteamAppRegistry'
        }

        $steamRegistryCandidates = @(
            @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
            @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamExe' },
            @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
            @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
        )
        foreach ($candidate in $steamRegistryCandidates) {
            $value = Get-Rt2RegistryValueSafe -Path $candidate.Path -Name $candidate.Name
            if (-not [string]::IsNullOrWhiteSpace($value) -and $value.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
                $value = Split-Path -Parent $value
            }
            Add-SteamRoot $value
        }

        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            Add-SteamRoot (Join-Path ${env:ProgramFiles(x86)} 'Steam')
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            Add-SteamRoot (Join-Path $env:ProgramFiles 'Steam')
        }

        # Add common secondary-library layouts without recursively scanning a disk.
        foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($drive.Root)) {
                continue
            }
            Add-SteamRoot (Join-Path $drive.Root 'SteamLibrary')
            Add-SteamRoot (Join-Path $drive.Root 'Steam')
            Add-SteamRoot (Join-Path $drive.Root 'Games\Steam')
        }
        }

        # Steam's libraryfolders.vdf is authoritative for secondary libraries.
        $steamRootSnapshot = @($steamRoots.ToArray())
        foreach ($steamRoot in $steamRootSnapshot) {
            foreach ($libraryRoot in @(Get-Rt2SteamLibraryRootsFromVdf -SteamRoot $steamRoot)) {
                Add-SteamRoot $libraryRoot
            }
        }

        foreach ($steamRoot in @($steamRoots.ToArray())) {
            $steamApps = Join-Path $steamRoot 'steamapps'
            $manifestPath = Join-Path $steamApps 'appmanifest_2868840.acf'
            $installDir = 'Slay the Spire 2'
            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                try {
                    $manifestText = [System.IO.File]::ReadAllText($manifestPath)
                    $match = [regex]::Match($manifestText, '"installdir"\s*"(?<name>[^"]+)"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
                    if ($match.Success) {
                        $installDir = $match.Groups['name'].Value
                    }
                } catch {
                    Write-Warning "Could not read Steam manifest '$manifestPath': $($_.Exception.Message)"
                }
            }

            Add-GameRootCandidate -Path (Join-Path $steamApps "common\$installDir") -Source 'SteamLibraryFolders'
        }
    } elseif ($isLinuxPlatform) {
        Add-GameRootCandidate -Path (Join-Path $HOME '.local/share/Steam/steamapps/common/Slay the Spire 2') -Source 'LinuxSteamDefault'
    } elseif ($isMacPlatform) {
        Add-GameRootCandidate -Path (Join-Path $HOME 'Library/Application Support/Steam/steamapps/common/Slay the Spire 2') -Source 'MacSteamDefault'
    }

    foreach ($candidate in $referenceCandidates) {
        $checked.Add($candidate.ReferenceDir)
        if (-not (Test-Rt2Sts2ReferenceDirectory -Path $candidate.ReferenceDir)) {
            continue
        }

        $resolvedReference = (Resolve-Path -LiteralPath $candidate.ReferenceDir).Path
        $resolvedGameRoot = $candidate.GameRoot
        if ([string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
            $resolvedGameRoot = Split-Path -Parent $resolvedReference
        }
        if (Test-Path -LiteralPath $resolvedGameRoot -PathType Container) {
            $resolvedGameRoot = (Resolve-Path -LiteralPath $resolvedGameRoot).Path
        }

        if ($isMacPlatform) {
            $modsDir = Join-Path $resolvedGameRoot 'SlayTheSpire2.app/Contents/MacOS/mods'
        } else {
            $modsDir = Join-Path $resolvedGameRoot 'mods'
        }

        return [pscustomobject]@{
            ReferenceDir = $resolvedReference
            GameRoot = $resolvedGameRoot
            ModsDir = $modsDir
            Source = $candidate.Source
            CheckedReferenceDirs = @($checked.ToArray())
        }
    }

    return [pscustomobject]@{
        ReferenceDir = ''
        GameRoot = ''
        ModsDir = ''
        Source = 'NotFound'
        CheckedReferenceDirs = @($checked.ToArray())
    }
}
