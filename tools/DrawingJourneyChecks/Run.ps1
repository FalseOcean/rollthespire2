param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [string]$Seeds = (Join-Path $PSScriptRoot 'seeds-01140.json'),
    [string]$Batch = 'receipts',
    [ValidateRange(0,99)][int]$Start = 0,
    [ValidateRange(1,100)][int]$Count = 100,
    [ValidateRange(0,99)][int[]]$Ordinals,
    [string]$AncientNode,
    [string]$AncientRelic,
    [switch]$NodeHoverUi,
    [string]$ProductDll,
    [string]$GameVersion = '0.114.0',
    [ValidateRange(1,12)][int]$Workers = 1,
    [switch]$SkipProductBuild
)
$ErrorActionPreference = 'Stop'
if ($Start + $Count -gt 100) { throw 'Requested range exceeds the 100 supplied seeds.' }
$selection = if ($PSBoundParameters.ContainsKey('Ordinals')) { @($Ordinals) } else { $Start..($Start+$Count-1) }
if (@($selection).Count -eq 0) { throw 'Select at least one sample.' }
if (@($selection | Sort-Object -Unique).Count -ne @($selection).Count) { throw 'Duplicate sample ordinal.' }
$selection = @($selection)
if ($NodeHoverUi) { $selection = @(0) }
if (($AncientNode -or $AncientRelic) -and (!$AncientNode -or !$AncientRelic -or $selection.Count -ne 1)) {
    throw 'An ancient replay requires one selected seed and both its node and relic.'
}
$seedPath = (Resolve-Path -LiteralPath $Seeds).Path
$godotPath = (Resolve-Path -LiteralPath $Godot).Path
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$out = [IO.Path]::GetFullPath($WorkDirectory)
$hostPath = Join-Path $out 'host'
$receiptPath = Join-Path $out $Batch
if (Test-Path "$receiptPath/worker-*") { throw "Batch already contains worker receipts. Choose another batch name: $Batch" }
$game = (Resolve-Path -LiteralPath $GameRoot).Path
$references = Join-Path $game 'data_sts2_windows_x86_64'
New-Item -ItemType Directory -Force -Path $hostPath,$receiptPath,"$out/product" | Out-Null
if (!$SkipProductBuild -and !$ProductDll) {
    dotnet build "$repo/RolltheSpire2.csproj" -c Release -m:1 '-p:Rt2ModsDir=' "-p:Sts2ReferenceDir=$references" "-p:OutputPath=$out/product/bin" "-p:Rt2BuildEvidenceDir=$out/product/evidence" --nologo > "$out/product/build.log"
    if ($LASTEXITCODE) { Get-Content "$out/product/build.log" -Tail 24; throw 'Product build failed' }
}
$product = if ($ProductDll) { (Resolve-Path -LiteralPath $ProductDll).Path } else { "$out/product/bin/RolltheSpire2.dll" }
Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Extension -in '.cs','.csproj','.tscn','.godot' | Copy-Item -Destination $hostPath -Force
dotnet build "$hostPath/DrawingJourneyChecks.csproj" -c Release -m:1 "-p:ProductDll=$product" "-p:GameDlls=$references" --nologo > "$out/host-build.log"
if ($LASTEXITCODE) { Get-Content "$out/host-build.log" -Tail 24; throw 'Journey host build failed' }
$lookup = "$hostPath/.godot/mono/temp/bin/Debug"
New-Item -ItemType Directory -Force -Path $lookup,"$out/appdata","$out/localappdata" | Out-Null
Get-ChildItem "$hostPath/.godot/mono/temp/bin/Release" | Copy-Item -Destination $lookup -Recurse -Force
$expectedHash = (Get-FileHash -LiteralPath $product -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath "$lookup/RolltheSpire2.dll").Hash -ne $expectedHash) { throw 'Staged host product hash differs from the selected DLL.' }
[IO.File]::WriteAllText("$receiptPath/product.sha256",$expectedHash)
$children = @()
try {
    $step = [int][Math]::Ceiling($selection.Count / $Workers)
    for ($offset = 0; $offset -lt $selection.Count; $offset += $step) {
        $part = [Math]::Min($selection.Count - $offset, $step)
        $workerOrdinals = @($selection[$offset..($offset+$part-1)])
        $first = $workerOrdinals[0]
        $nativeOutput = Join-Path $receiptPath ("worker-" + $first)
        New-Item -ItemType Directory -Force -Path $nativeOutput,"$nativeOutput/appdata","$nativeOutput/local" | Out-Null
        $info = [Diagnostics.ProcessStartInfo]::new($godotPath)
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.WorkingDirectory = $hostPath
        $info.Environment['APPDATA'] = "$nativeOutput/appdata"
        $info.Environment['LOCALAPPDATA'] = "$nativeOutput/local"
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($arg in @('--headless','--path',$hostPath,'--','--game-pck',"$game/SlayTheSpire2.pck",'--game-version',$GameVersion,'--output',$nativeOutput,'--seeds',$seedPath,'--ordinals',($workerOrdinals -join ','))) {
            $info.ArgumentList.Add($arg)
        }
        if ($AncientNode) {
            foreach ($arg in @('--ancient-node',$AncientNode,'--ancient-relic',$AncientRelic)) { $info.ArgumentList.Add($arg) }
        }
        if ($NodeHoverUi) { $info.ArgumentList.Add('--node-hover-ui') }
        $child = [Diagnostics.Process]::Start($info)
        $children += [pscustomobject]@{ Process=$child; Stdout=$child.StandardOutput.ReadToEndAsync(); Stderr=$child.StandardError.ReadToEndAsync(); Output=$nativeOutput; Start=$first; Count=$part; Ordinals=$workerOrdinals }
    }
    $result = 0
    foreach ($worker in $children) {
        $worker.Process.WaitForExit()
        [IO.File]::WriteAllText("$($worker.Output)/runtime.log",$worker.Stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText("$($worker.Output)/stderr.log",$worker.Stderr.GetAwaiter().GetResult())
        $workerStatus = @{start=$worker.Start;count=$worker.Count;ordinals=$worker.Ordinals;exitCode=$worker.Process.ExitCode}
        [IO.File]::WriteAllText("$($worker.Output)/exit-status.json",($workerStatus | ConvertTo-Json))
        if ($worker.Process.ExitCode -ne 0) { $result = $worker.Process.ExitCode }
    }
} finally {
    foreach ($worker in $children) {
        if (!$worker.Process.HasExited) { $worker.Process.Kill($true) }
        $worker.Process.Dispose()
    }
}
Write-Output "Journey receipts and native logs: $receiptPath"
exit $result
