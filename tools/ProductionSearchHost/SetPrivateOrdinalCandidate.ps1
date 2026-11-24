param(
    [Parameter(Mandatory)][ValidateSet('default','ordinary','private','verify')][string]$Mode,
    [ValidateSet('EW','NC','NWAE','NR')][string]$Coverage = 'NC',
    [ValidateSet('ENAW','NEAW','EANW','keep','serial','serial-direct','pregate-serial','rarity-serial','pregate-fused','entry-rarity-serial')][string]$Order,
    [string]$GameUserData = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SlayTheSpire2')
)
$ErrorActionPreference = 'Stop'
$directory = Join-Path $GameUserData 'RolltheSpire2'
if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'Game user-data directory missing; supply -GameUserData.' }
$path = Join-Path $directory 'private-ordinal-candidate.json'
if ($Mode -eq 'default') {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    Write-Output 'Normal Production allocation restored for the next Search.'
} else {
    if ($Coverage -eq 'NR') {
        if (($Mode -eq 'ordinary' -and $Order) -or ($Mode -ne 'ordinary' -and $Order -notin @('keep','serial','serial-direct','pregate-serial','rarity-serial','pregate-fused','entry-rarity-serial'))) { throw 'NR ordinary omits Order; NR private/verify requires an explicit Capsule path or keep preset.' }
    } elseif (($Coverage -eq 'NWAE' -and $Order -notin @('ENAW','NEAW','EANW')) -or ($Coverage -notin @('NWAE','NR') -and $Order)) { throw 'NWAE requires explicit -Order ENAW/NEAW/EANW; NC/EW must omit -Order.' }
    $temp = "$path.tmp"
    $selection = @{ Coverage=$Coverage; Mode=$Mode }
    if ($Order) { $selection.Order = $Order }
    $selection | ConvertTo-Json | Set-Content -LiteralPath $temp
    Move-Item -LiteralPath $temp -Destination $path -Force
    if ($Coverage -eq 'NR' -and $Order -eq 'keep') {
        Write-Output 'Explicit Capsule KEEP preset: Small -> rarity-serial; Large -> entry-rarity-serial. Unsupported Queries keep normal selection. Running sessions do not change. Restore with -Mode default.'
    } else {
        Write-Output "Next matching $Coverage GPU-only Search: $Mode $Order. This order is explicit, not inferred from the Query. Other Coverage keeps normal selection. Running sessions do not change. Restore with -Mode default."
    }
}
