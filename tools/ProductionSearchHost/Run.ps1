param(
    [Parameter(Mandatory)][string]$Godot,
    [string]$Context,
    [string]$Workload,
    [Parameter(Mandatory)][string]$Output,
    [string]$DraftSnapshot,
    [string]$GamePck,
    [ValidateSet('vulkan','d3d12')][string]$Driver = 'vulkan',
    [int]$Warmup = 0,
    [int]$Repeat = 1,
    [int]$TimeoutSeconds = 300,
    [int]$CancelAfterMs = 0,
    [ValidateSet('EW-ordinary','EW-private','EW-verify','NC-ordinary','NC-private','NC-verify',
        'NWAE-private-ENAW','NWAE-private-NEAW','NWAE-private-EANW',
        'NWAE-verify-ENAW','NWAE-verify-NEAW','NWAE-verify-EANW')][string]$PrivateOrdinalCandidate,
    [switch]$PrivateOrdinalChecks,
    [switch]$SmallCandidateExactChecks,
    [switch]$FamilyPricingInventory,
    [string]$FamilyPhysicalMatrix,
    [switch]$NeowCanonicalCost,
    [ValidateSet('N','T')][string]$CanonicalFamily = 'N',
    [string]$CanonicalInputs = '16777216',
    [ValidateSet('both','dense','compact')][string]$CanonicalMode = 'both',
    [int]$CanonicalWarmups = 2,
    [int]$CanonicalSampleMs = 3000,
    [int]$CanonicalMinBatches = 16,
    [ValidateSet('training','validation')][string]$CanonicalSplit = 'training',
    [switch]$CanonicalInteger,
    [switch]$DirectNeowChecks,
    [switch]$PartyBonesPoolChecks,
    [switch]$PartyNeowAuthoringChecks,
    [switch]$PartyCpuCorpus,
    [switch]$PartyKaleidoscopeOrderChecks,
    [switch]$PartyNeowOrderedKernelChecks,
    [switch]$PartyNeowGpuChecks,
    [switch]$PartyNeowDensePairChecks,
    [switch]$PartyFamilyChecks,
    [switch]$PartyFastChecks,
    [switch]$RunEntryChecks,
    [switch]$NeowInitializationChecks,
    [switch]$GpuResourceFailureChecks,
    [switch]$UiSurfaceChecks,
    [switch]$OwnerUiPolish,
    [ValidateSet('all','chrome','editors','content','state','map','followup','release-copy','reference-copy','candidate-defaults')][string]$OwnerUiPolishScope = 'all',
    [switch]$DrawingNoticesUi,
    [switch]$DrawingBoundariesUi,
    [switch]$DrawingOverblocksUi,
    [switch]$DrawingCardOperationsUi,
    [switch]$DrawingDependenciesUi,
    [switch]$CollectionPickerUi,
    [switch]$ManualFamilyOrderUi,
    [switch]$MapEditorUi,
    [switch]$MapWitnessUi,
    [switch]$MapLossyPersistence,
    [switch]$MapLossyGpu,
    [switch]$CardCollectionEditorUi,
    [switch]$CardCollectionFlow,
    [switch]$StartupChecks,
    [string]$ResourceBaseline,
    [ValidateSet('N','N-readback','N-trials','E-readback','N-readyT','S','S-stable','R','C','W','NC')][string]$Y2Probe,
    [ValidateSet('ordinary','private-ordinal','verify-ordinal','ordinary-nc','ordinary-cn','private-nc','verify-nc')][string]$NcExperiment,
    [ValidateSet('ordinary-ew','ordinary-we','private-ordinal','verify-ordinal','ordinary-ecw','private-ecw','verify-ecw')][string]$EwExperiment,
    [string]$NwaeExperiment,
    [string]$RswExperiment,
    [string]$PrivateSerialExperiment,
    [switch]$CpuOnly,
    [switch]$CompatNoFp64,
    [switch]$CompatibilityChecks,
    [switch]$CompatibilityPartyChecks,
    [switch]$InitialRoomGpuChecks,
    [string]$PartyUnknownContextRoot,
    [string]$CpuFamilyWorkers,
    [string]$ReleaseBinaryDirectory,
    [switch]$Headless,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if ($ReleaseBinaryDirectory -and -not $SkipBuild) { throw '-ReleaseBinaryDirectory requires -SkipBuild for an immutable comparison.' }
if ($CpuOnly -and ($CompatNoFp64 -or $CompatibilityChecks)) { throw 'Compatibility GPU validation and CPU Only are separate controls.' }
if ($PSBoundParameters.ContainsKey('CanonicalFamily') -and -not $NeowCanonicalCost) { throw '-CanonicalFamily requires -NeowCanonicalCost (the existing dedicated N/T collector entry).' }
if ($NeowCanonicalCost -and ($CpuOnly -or $Headless -or $CompatNoFp64 -or $CompatibilityChecks -or $CpuFamilyWorkers -or $FamilyPhysicalMatrix -or $FamilyPricingInventory -or $Y2Probe)) { throw 'N/T canonical collection requires a rendering-enabled dedicated invocation; use -CanonicalInteger for IntegerUnitFloat N / IntegerRne T.' }
if (($CompatNoFp64 -or $CompatibilityChecks) -and ($CpuFamilyWorkers -or $Headless)) { throw 'Compatibility validation requires a rendering-enabled host and normal worker selection.' }
# This editor slice uses a separate process, mock saves and its own state root;
# it neither measures throughput nor reads the running game's RunState.
if ($OwnerUiPolish -or $DrawingNoticesUi -or $DrawingBoundariesUi -or $DrawingOverblocksUi -or $DrawingCardOperationsUi -or $DrawingDependenciesUi -or $CollectionPickerUi -or $ManualFamilyOrderUi -or $MapEditorUi -or $MapWitnessUi -or $MapLossyPersistence) { $UiSurfaceChecks = $true }
if (-not $CardCollectionFlow -and -not $CardCollectionEditorUi -and -not $UiSurfaceChecks -and (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue)) { throw 'Close SlayTheSpire2 before standalone measurements.' }
$hostRoot = $PSScriptRoot
# These native branches return before reading a replay in Main. All other
# branches still require both inputs; no default or substitute corpus is used.
$nativeOnly = $CardCollectionFlow -or $CardCollectionEditorUi -or $UiSurfaceChecks -or $StartupChecks -or $CompatibilityPartyChecks -or
    $PartyBonesPoolChecks -or $PartyNeowAuthoringChecks -or $PartyCpuCorpus -or
    $PartyKaleidoscopeOrderChecks -or $PartyNeowOrderedKernelChecks -or $PartyNeowGpuChecks -or $PartyNeowDensePairChecks -or
    $PartyFamilyChecks -or $PartyFastChecks -or $RunEntryChecks
if (-not $nativeOnly) {
    if (-not $Context -or -not $Workload) { throw 'Production replay requires -Context and -Workload.' }
    $contextPath = (Resolve-Path -LiteralPath $Context).Path
    $workloadPath = (Resolve-Path -LiteralPath $Workload).Path
}
$godotPath = (Resolve-Path -LiteralPath $Godot).Path
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
if (-not $SkipBuild) {
    dotnet build (Join-Path $hostRoot 'ProductionSearchHost.csproj') -c Release --nologo -p:Rt2ModsDir= -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
# The editor Godot executable resolves Debug; deploy the actual Release binaries there.
$releasePath = Join-Path $hostRoot '.godot/mono/temp/bin/Release'
if ($ReleaseBinaryDirectory) { $releasePath = (Resolve-Path -LiteralPath $ReleaseBinaryDirectory).Path }
$loadPath = Join-Path $hostRoot '.godot/mono/temp/bin/Debug'
if (-not (Test-Path -LiteralPath (Join-Path $releasePath 'ProductionSearchHost.dll') -PathType Leaf)) {
    throw 'Missing Release host output. Run without -SkipBuild to build ProductionSearchHost.'
}
New-Item -ItemType Directory -Path $loadPath -Force | Out-Null
Get-ChildItem -LiteralPath $releasePath | Copy-Item -Destination $loadPath -Recurse -Force
function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { throw 'Argument contains an unsupported quote.' }
    return '"' + $Value.TrimEnd('\') + '"'
}
$arguments = @('--path',(Quote-Argument $hostRoot),'--rendering-method','mobile','--rendering-driver',$Driver,
    '--',
    '--output',(Quote-Argument $outputPath),'--warmup',$Warmup,'--repeat',$Repeat,'--cancel-after-ms',$CancelAfterMs)
if (-not $nativeOnly) { $arguments += @('--context',(Quote-Argument $contextPath),'--workload',(Quote-Argument $workloadPath)) }
if ($Headless) { $arguments = @('--headless') + $arguments }
if ($DraftSnapshot) { $arguments += @('--draft-snapshot',(Quote-Argument (Resolve-Path -LiteralPath $DraftSnapshot).Path)) }
if ($GamePck) { $arguments += @('--game-pck',(Quote-Argument (Resolve-Path -LiteralPath $GamePck).Path)) }
if ($PrivateOrdinalChecks) { $arguments += '--private-ordinal-checks' }
if ($SmallCandidateExactChecks) { $arguments += '--small-candidate-exact-checks' }
if ($CpuOnly) { $arguments += '--cpu-only' }
if ($CompatNoFp64) { $arguments += '--compat-no-fp64' }
if ($CompatibilityChecks) { $arguments += '--compatibility-checks' }
if ($CompatibilityPartyChecks) { $arguments += '--compatibility-party-checks' }
if ($InitialRoomGpuChecks) { $arguments += '--initial-room-gpu-checks' }
if ($PartyUnknownContextRoot) { $arguments += @('--party-unknown-context-root',(Quote-Argument (Resolve-Path -LiteralPath $PartyUnknownContextRoot).Path)) }
if ($CpuFamilyWorkers) { $arguments += @('--cpu-family-workers', (Quote-Argument $CpuFamilyWorkers)) }
if ($FamilyPricingInventory) { $arguments += '--family-pricing-inventory' }
if ($FamilyPhysicalMatrix) { $arguments += @('--family-physical-matrix',$FamilyPhysicalMatrix) }
if ($NeowCanonicalCost) {
    $arguments += @('--neow-canonical-cost','--canonical-family',$CanonicalFamily.ToUpperInvariant(),'--canonical-inputs',$CanonicalInputs,'--canonical-mode',$CanonicalMode,
        '--canonical-warmups',$CanonicalWarmups,'--canonical-sample-ms',$CanonicalSampleMs,
        '--canonical-min-batches',$CanonicalMinBatches,'--canonical-split',$CanonicalSplit)
    if ($CanonicalInteger) { $arguments += '--canonical-integer' }
}
if ($DirectNeowChecks -or $NeowInitializationChecks -or $GpuResourceFailureChecks) { $arguments += '--direct-neow-checks' }
if ($PartyNeowAuthoringChecks) { $arguments += '--party-neow-authoring' }
if ($PartyCpuCorpus) { $arguments += '--party-cpu-corpus' }
if ($PartyKaleidoscopeOrderChecks) { $arguments += '--party-kaleidoscope-order' }
if ($PartyNeowOrderedKernelChecks) { $arguments += @('--party-family-checks', '--party-neow-gpu', '--party-neow-ordered-k-kernels') }
if ($PartyNeowGpuChecks -or $PartyNeowDensePairChecks) { $arguments += @('--party-family-checks', '--party-neow-gpu') }
if ($PartyNeowDensePairChecks) { $arguments += '--party-neow-dense-pair-checks' }
if ($PartyFamilyChecks) { $arguments += '--party-family-checks' }
if ($PartyFastChecks) { $arguments += @('--party-family-checks', '--party-fast-checks') }
if ($RunEntryChecks) { $arguments += '--run-entry' }
if ($PartyBonesPoolChecks) { $arguments += '--party-bones-pool' }
if ($NeowInitializationChecks) { $arguments += '--n-initialization-checks' }
if ($GpuResourceFailureChecks) { $arguments += '--gpu-resource-failure-checks' }
if ($UiSurfaceChecks) { $arguments += '--ui-surface-checks' }
if ($OwnerUiPolish) { $arguments += @('--owner-ui-polish', '--owner-ui-polish-scope', $OwnerUiPolishScope) }
if ($DrawingNoticesUi) { $arguments += @('--in-run-map-drawing-ui', '--drawing-notices-ui') }
if ($DrawingBoundariesUi) { $arguments += @('--in-run-map-drawing-ui', '--drawing-boundaries-ui') }
if ($DrawingOverblocksUi) { $arguments += @('--in-run-map-drawing-ui', '--drawing-overblocks-ui') }
if ($DrawingCardOperationsUi) { $arguments += @('--in-run-map-drawing-ui', '--drawing-card-operations-ui') }
if ($DrawingDependenciesUi) { $arguments += @('--in-run-map-drawing-ui', '--drawing-dependencies-ui') }
if ($CollectionPickerUi) { $arguments += @('--in-run-map-drawing-ui', '--collection-picker-ui') }
if ($ManualFamilyOrderUi) { $arguments += '--manual-family-order-ui' }
if ($MapEditorUi) { $arguments += '--map-editor-ui-only' }
if ($MapWitnessUi) { $arguments += '--map-witness-ui-only' }
if ($MapLossyPersistence) { $arguments += '--map-lossy-persistence-only' }
if ($MapLossyGpu) { $arguments += '--map-lossy-gpu' }
if ($CardCollectionEditorUi) { $arguments += @('--preset-library-surface-checks', '--card-collections-only', '--card-collection-editor-only') }
if ($CardCollectionFlow -and (-not $GamePck -or $Headless)) { throw '-CardCollectionFlow requires -GamePck and a rendering-enabled native host.' }
if ($StartupChecks) { $arguments += '--startup-checks' }
if ($ResourceBaseline) { $arguments += @('--resource-baseline',(Quote-Argument (Resolve-Path -LiteralPath $ResourceBaseline).Path)) }
if ($Y2Probe) { $arguments += @('--y2-probe',$Y2Probe) }
$previousRswExperiment = $env:RT2_RSW_EXPERIMENT
$previousPrivateSerialExperiment = $env:RT2_PRIVATE_SERIAL_EXPERIMENT
$previousExperiment = $env:RT2_EW_EXPERIMENT
$previousNcExperiment = $env:RT2_NC_EXPERIMENT
$previousNwaeExperiment = $env:RT2_NWAE_EXPERIMENT
$previousCandidate = $env:RT2_PRIVATE_ORDINAL_SELECTION
$previousCpuEvidence = $env:RT2_CPU_COST_EVIDENCE_PATH
try {
    # Isolate standalone allocation from the Owner's game selection file.
    $env:RT2_PRIVATE_ORDINAL_SELECTION = Join-Path $outputPath 'private-ordinal-candidate.json'
    if ($CpuFamilyWorkers) {
        # Keep each controlled trial independent of previous Host calibration.
        $env:RT2_CPU_COST_EVIDENCE_PATH = Join-Path $outputPath 'cpu-cost-evidence.json'
    }
    if ($PrivateOrdinalCandidate) {
        if ($EwExperiment -or $NcExperiment -or $NwaeExperiment -or $RswExperiment -or $PrivateSerialExperiment) { throw 'Use one explicit allocation selector.' }
        $parts = $PrivateOrdinalCandidate.Split('-')
        $selectionPath = Join-Path $outputPath 'private-ordinal-candidate.json'
        $selection = @{ Coverage=$parts[0]; Mode=$parts[1] }
        if ($parts.Length -eq 3) { $selection.Order = $parts[2] }
        $selection | ConvertTo-Json | Set-Content -LiteralPath $selectionPath
        $env:RT2_PRIVATE_ORDINAL_SELECTION = $selectionPath
    }
    $env:RT2_RSW_EXPERIMENT = $RswExperiment
    $env:RT2_PRIVATE_SERIAL_EXPERIMENT = $PrivateSerialExperiment
    $env:RT2_EW_EXPERIMENT = $EwExperiment
    $env:RT2_NC_EXPERIMENT = $NcExperiment
    $env:RT2_NWAE_EXPERIMENT = $NwaeExperiment
    $phases = if ($CardCollectionFlow) { @('create', 'reuse') } else { @('normal') }
    foreach ($phase in $phases) {
        $phaseArguments = $arguments
        $logPrefix = ''
        if ($CardCollectionFlow) {
            $phaseArguments += @('--card-collection-flow-phase', $phase)
            $logPrefix = 'card-collection-flow-' + $phase + '-'
        }
        $process = Start-Process -FilePath $godotPath -ArgumentList $phaseArguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $outputPath ($logPrefix + 'runtime.log')) -RedirectStandardError (Join-Path $outputPath ($logPrefix + 'stderr.log'))
        try {
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Standalone timeout ($phase); run is invalid." }
            $hostExitCode = $process.ExitCode
            if ($hostExitCode -ne 0) {
                if (-not $CardCollectionFlow) {
                    Write-Host "Standalone failed ($hostExitCode); inspect $outputPath/stderr.log"
                    exit $hostExitCode
                }
                throw "Standalone failed ($phase, exit $hostExitCode); inspect $outputPath/$($logPrefix)stderr.log"
            }
        } finally {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id }
            $process.Dispose()
        }
    }
    if ($CardCollectionFlow) {
        $created = Get-Content -LiteralPath (Join-Path $outputPath 'card-collection-flow-create.json') -Raw | ConvertFrom-Json
        $reused = Get-Content -LiteralPath (Join-Path $outputPath 'card-collection-flow-reuse.json') -Raw | ConvertFrom-Json
        if (-not $created.Passed -or -not $reused.Passed -or $created.ProcessId -eq $reused.ProcessId -or
            $created.ModBinary.PreparedSha256 -ne $reused.ModBinary.PreparedSha256 -or
            $created.HostBinary.PreparedSha256 -ne $reused.HostBinary.PreparedSha256 -or
            $created.ModBinary.LoadedMvid -ne $reused.ModBinary.LoadedMvid -or
            $created.HostBinary.LoadedMvid -ne $reused.HostBinary.LoadedMvid) {
            throw 'Card collection flow requires two successful different processes using identical binaries.'
        }
        Write-Output "Card collection production/restart flow passed: create PID $($created.ProcessId), reuse PID $($reused.ProcessId); $outputPath"
    } else { Write-Output "Standalone completed: $outputPath" }
} finally { $env:RT2_CPU_COST_EVIDENCE_PATH = $previousCpuEvidence; $env:RT2_PRIVATE_SERIAL_EXPERIMENT = $previousPrivateSerialExperiment; $env:RT2_RSW_EXPERIMENT = $previousRswExperiment; $env:RT2_EW_EXPERIMENT = $previousExperiment; $env:RT2_NC_EXPERIMENT = $previousNcExperiment; $env:RT2_NWAE_EXPERIMENT = $previousNwaeExperiment; $env:RT2_PRIVATE_ORDINAL_SELECTION = $previousCandidate }
exit 0
