param(
    [string]$ProjectRoot = '',
    [Parameter(Mandatory=$true)][string]$BuildDirectory,
    [Parameter(Mandatory=$true)][string]$OutputZip
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Join-Path $PSScriptRoot '..' }
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
if ([System.IO.Path]::GetExtension($out) -ne '.zip') { throw 'OutputZip must name a .zip file.' }
if (Test-Path -LiteralPath $out -PathType Container) { throw 'OutputZip names a directory.' }
$dll = Join-Path $build 'RolltheSpire2.dll'
$manifestPath = Join-Path $build 'RolltheSpire2.json'
foreach ($path in @($dll, $manifestPath, (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Mod package input: $path" }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.has_pck -ne $false) { throw 'This DLL/manifest packager requires has_pck=false; declared resource packs must not be silently omitted.' }
if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $root 'RolltheSpire2.json')).Hash) {
    throw 'Build manifest does not match the selected source tree.'
}

# Explicit members: never archive an entire build, game or developer-input directory.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$temp = [System.IO.Path]::GetTempFileName()
try {
    $stream = [System.IO.File]::Open($temp, [System.IO.FileMode]::Create)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $dll, 'RolltheSpire2/RolltheSpire2.dll')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $manifestPath, 'RolltheSpire2/RolltheSpire2.json')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root 'LICENSE'), 'LICENSE')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root 'THIRD_PARTY_NOTICES.md'), 'THIRD_PARTY_NOTICES.md')
            $entry = $archive.CreateEntry('INSTALL.txt')
            $writer = [System.IO.StreamWriter]::new($entry.Open(), [System.Text.UTF8Encoding]::new($false))
            try {
                $writer.WriteLine('Place the RolltheSpire2 folder in the Slay the Spire 2 mods directory. The game is required. Source and internal test inputs are separate packages.')
                $writer.WriteLine('See THIRD_PARTY_NOTICES.md for algorithm and artwork sources and rights. Keep it and LICENSE with redistributed packages.')
            }
            finally { $writer.Dispose() }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($out)) -Force | Out-Null
    Copy-Item -LiteralPath $temp -Destination $out -Force
    Write-Host "Mod package: $out"
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
