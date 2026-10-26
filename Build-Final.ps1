# 只編譯最後一版 Mod，不部署到遊戲。
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameReferenceDir,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$modProject = Join-Path $PSScriptRoot 'RolltheSpire2.csproj'
$output = Join-Path $PSScriptRoot 'artifacts\Final'
# 固定 K4 與 epoch 配置，tiny CPU 保留在源碼中，預設關閉。
& $Dotnet build $modProject -c Release -m:1 -o $output '-p:Rt2ModsDir=' `
    "-p:Sts2ReferenceDir=$GameReferenceDir" `
    '-p:Rt2NeowSmallReadback=true' '-p:Rt2NeowCombinedReadback=true' '-p:Rt2NeowMediumReadback=true' `
    '-p:Rt2NeowBatchMerge=true' '-p:Rt2NeowBatchMergeSize=4' '-p:Rt2NeowEpochBatch=true' `
    '-p:Rt2TinyInputCpuOffload=false' '-p:Rt2BatchMergeRelic=false' '-p:Rt2NeowPipeline=false'
if ($LASTEXITCODE -ne 0) { throw "Mod build failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $output -Force
Write-Output "Build output: $output"
