> PublicSource: internal decision records and full captured datasets are omitted. See the public development guide in DEVELOPMENT.md at the source root for available checks and input requirements.

# Standalone Production Search

## Resident warm replay desk (opt-in prototype)

`Resident.ps1` keeps one rendering-enabled Godot window and one frozen candidate
alive across ordinary replay jobs. All chats use the same absolute `StateRoot`
(default: `D:/STS2Modding/RolltheSpire2/.build-evidence/experiment-desk`).
Start once after building; submitting a replay does not build or open a window:

```powershell
dotnet build tools/ProductionSearchHost/ProductionSearchHost.csproj -c Release -p:Rt2ModsDir= -m:1
$release = 'D:/STS2Modding/RolltheSpire2/tools/ProductionSearchHost/.godot/mono/temp/bin/Release'
$desk = 'D:/STS2Modding/RolltheSpire2/tools/ProductionSearchHost/Resident.ps1'
& $desk -Action Start -Godot 'D:/STS2Modding/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64_console.exe' -ReleaseBinaryDirectory $release -Driver d3d12
# Hash the candidate YOU intend to test, not an arbitrary current host.
$sha = (Get-FileHash "$release/RolltheSpire2.dll" -Algorithm SHA256).Hash
& $desk -Action Submit -Owner 'your-chat-or-task' -ExpectedModSha256 $sha -Context 'D:/path/context.json' -Workload 'D:/path/workload.json' -Warmup 1 -Repeat 3
& $desk -Action Status
# Use the id returned by Submit to read the final result, or cancel that active job.
& $desk -Action Status -JobId '<id>'
& $desk -Action Cancel -JobId '<id>'
& $desk -Action Pause
& $desk -Action Resume
& $desk -Action Stop
```

Requests are frozen before atomic enqueue, FIFO, and serial. Jobs wait while the
game is open or another cooperating experiment owns
`Local\RT2_PerformanceBench_20261012`. The lock is acquired/released on the Godot
main thread, and is free between jobs. Legacy scripts that ignore this lock are
not automatically coordinated. Pause affects queued work; Cancel targets only
the named active job. Timeouts request cooperative cancellation; an unresponsive
native call is not forcibly killed or reported as cleanly cancelled. Status
detects a stale heartbeat. Stop/closing the window waits for active cancellation.

Each `runs/<id>/` retains submitted inputs, host identity, ordinary Production
summaries, candidate output and continuation receipts. `results/<id>.json` records
completion, cancellation or the original exception. Startup failures and the
combined runtime log are in `instances/<instance>/`; `instance.json` points to the
current process. A failed/timed-out run requires an explicit restart before further
work; an interrupted old job is recorded without automatic retry. The prepared
DLL hash is tied to the loaded module MVID even when Godot byte-loads assemblies.
No game Mod installation is performed.

This is **warm process, fresh Production session**: every repeat still compiles,
plans, creates/disposes its session and may create GPU resources. It is not a
cold-start benchmark or a guarantee of cached shaders/devices. Changing the DLL
requires stopping and starting the desk with the desired build. The desk uses its
own Godot user-data directory; calibration/cache state may persist between jobs.
Do not change process-wide experiment environment settings between queued jobs.

First supported scope is ordinary CPU/GPU context/workload replay. UI suites,
startup checks, probes and fault-injection groups remain on the existing isolated
host; they are not accepted as arbitrary flags by this queue. A future hidden
worker integration can cover those groups without claiming their global state is
safe to reuse. The window is unfocusable and capped at 10 fps; no per-job window is
created. The user can close it or pause the queue at any time.

## Public source and internal replay inputs

`package_sourcecandidate.ps1` defaults to an internal handoff with the full frozen
Corpus. Its explicit `-PackageKind PublicSource` mode includes the v1 requests,
expected results and provenance, plus all capture/schema/replay source, but omits
full runtime contexts and the remaining internal input collection. The public
Corpus README carries that notice; its manifest still identifies the original
context hash. Full `RunCorpus.ps1` replay requires the matching internal context.
Missing context is an unavailable input, not a passing benchmark or permission
to substitute a new capture. Keep local recaptures in a new dataset/version.
See [packaging commands](../../BUILDING.md#packaging). Neither mode installs or
publishes a Mod. Public statistical MapEvidence keeps both JSON source records.

## Controlled CPU worker trials

For long fixed-range CPU studies, use `Run.ps1 -CpuOnly -Headless -CpuFamilyWorkers 'R:4'` with the usual Context/Workload/Output arguments. The workload's WorkerCount remains the Exact budget. The host substitutes only that Family's already registered worker configuration with the same numerical revision, preserving the selected stage order, obligations and observation-window size. Unsupported configurations fail explicitly; this is a tool-only unpriced trial, not a Production worker policy. Normal session channels, Exact, cancellation, cursor tracking and result processing still execute. CPU calibration reads/writes are isolated to `cpu-cost-evidence.json` under that trial's Output; use a fresh directory per process. Coordinate heavy runs with the research named mutex documented in worker scaling evidence (internal record omitted).

For an immutable binary A/B comparison, add `-SkipBuild -ReleaseBinaryDirectory <saved-host-output>`. The directory must contain the complete compatible Release host output. The launcher uses that snapshot for the editor's Debug lookup directory; it does not rebuild or install the game Mod. Record the measured DLL hashes with the results. Default builds and launches retain their existing behavior.

## Settings-page smoke

Add `-UiSurfaceChecks` to the usual `Run.ps1` command (same required Context/Workload/Output arguments). This tool-only mode constructs the real Advanced and Settings Godot pages, checks English/Chinese localization, workers, persistent origin/cursor, Auto/CPU persistence and button callbacks, and writes page screenshots under Output. It does not start Search or change player state. Workers retain their existing main-menu lifetime; only the already-persisted settings are tested across reloads. Run ordinary Host replay separately for Search/Exact evidence.

## Y2 cost decomposition (diagnostic only)

`-Y2Probe N-readback` exercises current N public/private readback, paired roots,
module ownership, cancellation/reuse and isolated slot faults. `E-readback`
checks E's cumulative boundary. `N-readyT` compares actual ready old T plus new
N in one submission against two submissions, with complete populations checked
outside timing. It is a local physical experiment; Production does not enable
this ready-stage schedule. `-PartyNeowDensePairChecks` runs the existing focused
two-player N public/private pair slice without the unrelated party effect matrix.

`Run.ps1 -Y2Probe N|S|S-stable|R|C|W|NC` uses the selected workload's real compiled
Family metadata and the first `min(2^24, ScanCount)` roots. `NC` requires an NC
workload and consumes the actual N private append population without host sorting.
Use the corpus context and workload arguments as for normal runs. Each process
performs three warm rounds and nine alternating measured rounds and writes
`y2.json` with complete ordered-output digests and existing executor metrics.
Run three processes with distinct output directories for process-level spread.

This bypasses the session for numerical decomposition; it does **not** write
Hardware Performance observations or provide Search E2E evidence. Full-decode
substitution preserves N/S/R geometry. Precomputed-root probes allocate temporary
packed u32 root buffers; their setup/store cost is excluded from consumer timing
and reported separately. C's `carry-cooperative8` is a rejected diagnostic, not a
Production selector. The normal runner and game behavior are unchanged.
Use ordinary `Run.ps1` with the accepted private-ordinal selector for whole-session
measurements. See Y2 evidence (internal record omitted).

This Godot host calls the current `SearchCompiler`, `ExactSearchExecutionRequestFactory`, `FamilyExecutionCoordinator`, Family GPU implementations and Production Exact. It does not have a separate Query compiler or combination support list. `sts2.dll`, Harmony and their resolved dependencies are copied as references; the game executable is not launched.

## Capture once in the normal game

From the repository root, after a normal Release build/deployment:

```powershell
./tools/ProductionSearchHost/RequestCapture.ps1 -Output .build-evidence/production-replay/runtime
```

Start the game normally through Steam, open RT2 Search. The next normal Search context compilation writes the current `context.json` and `workload.json`, then enumerates the actual runtime character catalog × `SeedAnalysisInputLimits` legal ascensions on the main thread with the same canonical zero probe seed and explicit singleplayer capture parameters as Production. No character/ascension switching is required. It saves `runtime-authority.json` (semantic universe, unlock authority and environment fingerprint), `character-catalog.json`, `contexts-manifest.json` and a `contexts/` file per captured combination. The request is removed after the capture pass completes. No Search execution is needed to capture. Check `productionReplayExport=true` in the game log. Select the desired character/ascension file through the manifest for measurements. Incomplete character discovery rejects enumeration instead of using the presentation fallback. Per-combination failures have no file path and explicit diagnostics; captured but incomplete authority retains its real flags and dependencies. Empty dependencies are not a universal semantic-support guarantee: each Query still passes normal Production checks. On nonstandard game user-data paths, set `RT2_PRODUCTION_REPLAY_EXPORT_DIR` for the game process instead (continuous opt-in exports).

The reusable context contains schema/provenance plus the existing `SearchContext`: immutable `AnalysisAuthoritySnapshot`, detection, character/ascension and evaluation assumptions. The separate workload contains canonical `SearchQuery` and `SearchRunOptions`. Exported run options are an explicit **example** interval (`000000000000`, 2^24 roots, max target, four workers), not a recording of the game's active scan interval. Edit/copy the workload for experiments. Query authoring uses the existing DTO property names and exact `CATEGORY:ENTRY` identities. Unknown JSON properties fail rather than silently dropping predicates.

No Node, RunState, Player, game model, live RNG, GPU resource, compiled plan or selected ordering is saved. Export round-trips both files and refuses a changed reconstruction. Captures are source-schema inputs, not a claim of authority across a different game version/mod environment. The normal Production validation remains active.

## Run with the game closed

```powershell
./tools/ProductionSearchHost/Run.ps1 `
  -Godot D:/STS2Modding/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64_console.exe `
  -Context .build-evidence/production-replay/runtime/context.json `
  -Workload .build-evidence/production-replay/runtime/workload.json `
  -Output .build-evidence/production-replay/experiment-01 `
  -Warmup 1 -Repeat 3
```

Use a fresh output directory per comparison. `-Driver vulkan|d3d12` records the actual backend through the existing device identity foundation; do not compare backends as an implementation A/B. The hidden rendering-enabled Godot process provides a real local RenderingDevice. Do not use `--headless`, which may have no rendering device. The launcher builds Release and copies those **Release binaries** into the Debug lookup directory used by the editor executable; this does not compile Debug code. `-SkipBuild` is for repeated runs after a known build only.

Each repeat recompiles and replans the same Query/range using current Production code. Warmup is a separate session, not a claim that all next-session setup disappears. Normal Family timing boundaries exclude their documented setup/reference checks; whole-session E2E includes session overhead. A thread-pool consumer drains results independently of Godot frame rate.

Outputs: `runtime.log` (normal Production timing/planner/parity/cursor logs), `stderr.log`, input copies, `runN.summary.json`, and `runN.matches.jsonl` with seed, match evidence, route IDs and Witnesses. Warmup files are labeled separately. Godot's host-specific user-data directory isolates its performance store from the game's store. A synthetic context must not be presented as live performance evidence.

`hostObservations` records first confirmed candidate, requested/completed
cancellation, total managed allocation and process peak working set. Its25ms
`completedScanSamples` observer records actual completed-root growth; the final
post-completion point is marked separately. An interpolated central scan interval
is a host observation with sampling error, not GPU-only throughput. The ordinary
`ObservedScanningSeconds` counter begins after the first root and can include
later lazy setup; whole-session elapsed and first result keep their own boundaries.

Selected physical recovery is rejected before publishing to downstream/Exact; selected GPU errors do not become CPU rescans. Normal compiler-selected CPU paths for physically inapplicable GPU shapes remain normal Production behavior. Closing the host requests session cancellation; the launcher timeout force-terminates only its own host and marks the run invalid (not a clean cursor completion). Default game recovery behavior is unchanged.

## Validation status

For fixed reusable Family and composite before/after inputs, use the [versioned baseline corpus](Corpus/README.md) and `RunCorpus.ps1`. This is separate from historical Game/Standalone calibration. It includes a captured context, canonical workloads, input hashes and initial full-record result hashes; no need to rediscover Query conditions for each physical change.

See source audit (internal record omitted). Synthetic smoke inputs are generated through the existing C# test entry point:

```powershell
dotnet run --project tests/functional/Rt2FunctionalChecks -c Release -p:Rt2ModsDir= -- --production-replay
```

These are tests of portability and shared execution, not a replacement for one real context capture and game/standalone parity. No runtime fixture fallback exists in the host.

The format does not encode a vanilla character count, pool size or content allowlist. Capturing a modded environment is distinct from predicting custom mechanics: the runtime-discovered immutable facts and fingerprint are preserved, while unsupported RNG/state/start/relic/event behavior remains outside existing Family/Exact semantic coverage. No all-unlocks substitution, vanilla context substitution or generic custom-mechanics simulator is supplied. Current default ascension bounds come from Production input limits, not the archive format.

## Y1 experimental allocation switches

Owner rare NC cases live in `Corpus/owner-rare-nc-v1`; select that directory with `RunCorpus.ps1 -CorpusDirectory`. Their explicit A/B modes are `ordinary-nc`, `ordinary-cn`, `private-nc`, and unmeasured full-parity `verify-nc`. `Run.ps1 -DraftSnapshot` can import an actual saved UI draft through the current UI builder while checking context assumptions. Rare NC evidence (internal record omitted) supports a narrower high-intermediate/rare-terminal option; defaults remain unchanged.

Y5 SILKEN experiments use `-NcExperiment ordinary|private-ordinal|verify-ordinal`. They require the natural C→N GPU allocation; no order is forced. Verification checks complete intermediate/final populations and is excluded from timing. Both experimental switches are empty by default and cannot be combined. Y5 results (internal record omitted) reject promotion on the fixed workloads.

`Run.ps1 -EwExperiment ordinary-ew|ordinary-we|private-ordinal` explicitly selects an unpriced E/W-only allocation. Leave unset for normal Production selection. `verify-ordinal` add complete intermediate/final population checks and are not performance modes. `-CancelAfterMs 1000` requests normal session cancellation. These switches do not change saved Query semantics or default Planner pricing. See Y1 evidence (internal record omitted) for fixed inputs, repeated results and recommendation.

## Bounded private ordinal Production allocation

The current candidate shares only a device-private u32 LogicalOrdinal buffer, bounds, verification and strict cleanup. EW remains E→W; private NC remains N→C. Existing C→N controls remain available for short/Exact-heavy counterevidence. Root retention switches have been removed from executable source and remain historical evidence only. Y4 adds normal pricing of these existing allocations on the evidenced device/work shapes; numerical bodies are unchanged. Run without any explicit selection to exercise normal pricing. Look for `selectionPolicy=FamilyPlanner.PrivateOrdinal.WorkShape.20260908.v1`, `RegisteredAllocations`, comparative cost ranges and `SelectedAllocation`. Unknown device/shape prices preserve ordinary execution. See Y4 pricing evidence (internal record omitted).

Standalone: use `Run.ps1 -PrivateOrdinalCandidate EW-ordinary|EW-private|EW-verify|NC-ordinary|NC-private|NC-verify` with the same captured context/workload/range. `ordinary` is the explicit E→W or N→C ordered public control; it is distinct from leaving every selector unset for normal Planner selection. Verification compares complete intermediate and final populations, and is not a performance mode. `-PrivateOrdinalChecks` runs focused real-GPU buffer/lifecycle checks instead of a session. All modes use the same candidate source as the game. The runner isolates its selection file from the game's selection.

Owner game A/B, from this repository in PowerShell:

```powershell
./tools/ProductionSearchHost/SetPrivateOrdinalCandidate.ps1 -Coverage NC -Mode ordinary
# Start the matching saved rare NC Query; stop and save its log/cursor.
./tools/ProductionSearchHost/SetPrivateOrdinalCandidate.ps1 -Coverage NC -Mode private
# Reset to the SAME start seed/range/target and run the SAME Query.
./tools/ProductionSearchHost/SetPrivateOrdinalCandidate.ps1 -Mode default
```

Use `-Coverage EW` for EW. Restart the game once to load the newly built DLL; changing this selector afterwards only requires starting the next Search. Launch normally through Steam. No Steam process launch override or source edit is needed. File location: `%APPDATA%/SlayTheSpire2/RolltheSpire2/private-ordinal-candidate.json`. For nonstandard game data, use `RT2_PRIVATE_ORDINAL_SELECTION` for the game and point the script's `-GameUserData` to the same directory layout.

The file persists until `-Mode default`; it only applies to exactly two registered, GPU-supported EW/NC Families. Other Coverage retains its normal allocation with `applicable=False`. Invalid configuration or an already-selected implementation fault fails explicitly. No per-survival/time threshold is added. Do not combine this selector with legacy `RT2_EW_EXPERIMENT`/`RT2_NC_EXPERIMENT` environment overrides.

Look for `privateOrdinalCandidateRequested` and `applicable=True`, the selected `PrivateOrdinal.CanonicalAbi1Ready.20260908.v2` revision, `ewPrivateBatch`/`ncPrivateBatch`, and the existing Exact/cursor/session summaries. Compare actual input/intermediate/final populations, final Exact witnesses/count and safe cursor as well as timing. Concurrent Exact publication can change raw result order; public ABI1 remains strictly ordered, and the full result/Witness multiset is the comparison authority. Selected private cleanup faults make the session Faulted; there is no CPU rescan or fail-open.

Fixed recommended inputs: `Corpus/v1/cases/EW-high-output.json`, `Corpus/owner-rare-nc-v1/NC_RARE_01.json` and `NC_RARE_03.json`. Preserve the old short `NC-silken-cards` baseline as negative economic evidence; it is never automatically forced private. See the productionization report in `docs/source_audits/PRIVATE_ORDINAL_PRODUCTION_CANDIDATE_20260908.md` for measured results and maintenance cost.

## Bootstrap/resource smoke

`Run.ps1 -UiSurfaceChecks -StartupChecks` additionally calls the real ModManager initializer, checks the menu patch and Godot entry binding, and loads Search/localization/official presets. Optional `-ResourceBaseline <json>` compares renamed resource SHA-256 values from an earlier build. Use the supported .NET runtime (the local Godot editor may roll forward to an incompatible newer Harmony runtime). This isolated Host has no full game asset bundle or ModManager assembly registration; it does not replace a local/Workshop startup check.

## Operational logging (2026-09-12)

CPU integration uses the same Production coordinator. `Run.ps1 -CpuOnly` applies a GPU resource constraint; `-Headless` exercises actual GPU absence with normal Auto. The existing Game CPU Only selector applies the same constraint to Search and ETA. No `RT2_CPU_*` Product policy was ported. Fixed cases live in `Corpus/cpu-main-v1`; current logging includes `familyCpuRealizations`, `familyCpuStageSelection`, `searchPhysicalAvailability` and aggregate `familyCpuSummary` (raw batch rows are Detail). Reference CPU values are resolved through independent CPU calibration; `RT2_CPU_COST_EVIDENCE_PATH` isolates the raw test store, not execution policy.

### Device compatibility correctness checks

`Run.ps1 -CompatNoFp64` constrains the actual Production numeric implementation;
it keeps the real device and rendering driver identity. On D3D12 this is a real
D3D12 execution of the compatibility path, not a simulated Metal device. Normal
unconstrained runs retain their default implementation. Each complete session
now writes `run0.invocations.json` (actual selected physical revisions, numeric
requirements and completed invocation counters) and `run0.continuation.json`.
The host flushes candidate records before acknowledging their receipt.

The existing host's `-CompatibilityChecks` branch performs CPU Only and forced
FP64-free complete sessions, with unmodified Production Exact, for an existing
fixed positive input. It bounds the interval to at most 4,099 roots, retains the
original start and worker count, and removes target-count early stopping. It
then adds a separately saved, explicitly authored first-shop condition from
Core's projection at that same positive root, plus a one-root rejection with a
different actual pool identity. It requires real GPU N/R/C/W/A/E/S
participation in the seven-family positive, compares complete semantic result
multisets and cursors, and checks warnings separately. A deterministic
publication gate cancels after actual filtering/Exact work; the serialized
continuation is recompiled and resumed to the original interval end. Attempt
snapshot identities remain in raw outputs; continuation identity binds the
same request across changed range options.

The same branch dispatches the production integer NextInt/unit-float primitives
against binary64 CPU expectations, including rounding boundaries and the known
historical-floor counterexample. Public family calls compare native GPU,
integer GPU and complete CPU results for dense tail, sparse compact (including
ordinal zero), and empty inputs. Each input mode owns fresh family instances;
the full ordered ABI1 outputs and actual shader sources/hashes are retained.
It also checks tracked Rfull and T Leafy/NewLeaf/Bones implementations using
the unchanged captured authority and separately exported Exact-positive fixtures.

`-CompatibilityPartyChecks -GamePck <installed game PCK>` uses the existing
Party host initialization to capture a real two-player authority and exercise
Generic Rfull through the actual personal factory. This proves the personal
module's ordered ABI1 parity; it does not claim complete party-session parity.
The existing `-PartyNeowGpuChecks` and `-PartyNeowOrderedKernelChecks` also
compare native and integer public N bodies. The ordered-kernel check expects
`-Context` inside a `2p/`, `3p/` or `4p/` corpus directory with all three siblings;
the existing `-PartyCpuCorpus` branch can capture them using the same game PCK.
Party N receipts retain existing route-only predecessor cases whose final Exact
is Unknown despite equal native/integer/CPU ABI1 output. The suite explicitly
reports this unresolved scope and verifies separate stronger queries with an
authored predecessor effect; it does not claim those original queries became Match.

Build the host once without deployment, then run from the repository root with
the installed Godot .NET console path and the usual shared resource window:

```powershell
./tools/ProductionSearchHost/Run.ps1 -Godot $g -Driver d3d12 -Context tools/ProductionSearchHost/Corpus/v1/context.json -Workload tools/ProductionSearchHost/Corpus/private-first-rc-v1/positive.json -Output .build-evidence/device-compat-phase2/complete-checks -CompatibilityChecks -SkipBuild
```

Use a fresh output directory. `compatibility-checks.json` exists with `Passed`
only after all comparisons pass; the individual JSONL, invocation, summary and
continuation receipts remain available to diagnose an earlier failure. These
are bounded correctness checks, without a throughput claim or M1/Metal
execution claim. `-CompatibilityChecks` already controls both CPU and GPU
sessions; do not combine it with `-CpuOnly`, worker trials, or Family-only probes.

`-InitialRoomGpuChecks` runs the existing first-two-treasure and shared Unknown
checks with native and integer GPU bodies, CPU/Core and actual game RNG
references, dense/compact tails, and multi-window ABI1 output. It then runs
complete CPU/native/integer Production sessions for six solo queries, including
a real duplicate-consumption rejection and serialized cancellation/resume.
Add `-PartyUnknownContextRoot <directory>` containing actual captured
`2p/context.json`, `3p/context.json` and `4p/context.json` to include all three
ordinary party sizes and their complete mixed N/E/Unknown sessions. Without
that argument, this branch covers solo queries only; it makes no party claim.
The existing `-PartyCpuCorpus -GamePck <installed game PCK>` can capture those
contexts. Use a fresh output directory and inspect both
`initial-rooms-gpu-checks.json` and
`complete-sessions/initial-rooms-complete-sessions.json` for the executed scope.
Native InitialRooms calls retain their current finite canonical references;
integer implementations have distinct identities and cannot borrow those
prices. InitialRooms private bindings remain unavailable for either variant.

For a tool-only selected CPU failure check, launch Main with `--cpu-only --cpu-fault` and a priced CPU case. It injects a failing matcher before session construction and expects Faulted; it is not a Product recovery path.

Default RT2 logs retain complete Production evidence; Host stdout/stderr remains capturable. Per-batch execution trace requires `RT2_REWRITE_DETAIL_LOG=1` before process launch and is also persisted to the RT2 file. Godot receives discovery/fault summaries only. Search rows correlate through `logSession`; `productionSearchWorkload` retains canonical JSON to end-of-line.

For aggregate analysis use `physicalExecutionSummary`, `gpuCostObservation` (best eligible window), `gpuCostPeakDecision`, existing flush records and `searchExactOutcome`/`familyExecutionCompleted`. Raw stage and calibration keys remain available in Detail. Do not substitute observed populations into running prices.

`Run.ps1` does not accept or forward the logging switches. The dedicated `--logging-checks` branch runs before context/workload/output parsing, so it needs none of those inputs. From the repository root, set the path to a compatible Godot .NET console executable as described in [BUILDING](../../BUILDING.md#native-host-requirements), build without deployment, stage the Release host in the editor's Debug lookup directory, and call it directly:

```powershell
$ErrorActionPreference = 'Stop'
$godot = 'C:/Tools/Godot_v4.5.1-stable_mono_win64_console.exe' # Set your installed editor path.
$hostRoot = (Resolve-Path './tools/ProductionSearchHost').Path
dotnet build "$hostRoot/ProductionSearchHost.csproj" -c Release --nologo -p:Rt2ModsDir= -m:1
if ($LASTEXITCODE -ne 0) { throw 'Release host build failed.' }
$releasePath = Join-Path $hostRoot '.godot/mono/temp/bin/Release'
$loadPath = Join-Path $hostRoot '.godot/mono/temp/bin/Debug'
New-Item -ItemType Directory -Path $loadPath -Force | Out-Null
Get-ChildItem -LiteralPath $releasePath | Copy-Item -Destination $loadPath -Recurse -Force
& $godot --headless --path $hostRoot -- --logging-checks
if ($LASTEXITCODE -ne 0) { throw "Logging checks failed ($LASTEXITCODE)." }
```

Add `--logging-io-failure` after `--logging-checks` for the injected sink I/O failure. Success records `loggingChecks=PASS` in the RT2 operational log. These commands are checked against the current entry points, not newly executed by the documentation alignment.

The separate `--logging-exact-fault` session branch requires normal context/workload/output inputs and is not forwarded by the wrapper either. Its legacy host assertion still expects `Faulted`, whereas current candidate Exact failures retain pending work; do not use that assertion as acceptance of the current recovery contract. This documentation change does not repair or run the tool fixture. Read an active log with shared-write access. Normal support starts with the RT2 log path; obtain Godot logs when RT2 itself did not initialize.

Heterogeneous Cost/ETA checks in an isolated study: `dotnet run --project tests/functional/Rt2FunctionalChecks -c Release -p:Rt2ModsDir= -- --cpu-cost-closure`. Targeted measurement uses `--cpu-cost-measure` (no Product benchmark). `Corpus/heterogeneous-cost-v1/manifest.json` binds the current Product cases, contexts, ranges and worker budget. Run each with normal `Run.ps1`, `-CpuOnly` only for the marked cases, and `-Repeat 2` to inspect next-session calibration.

The existing measurement entry accepts `--worker-configs`, `--sample-ms` (at most 60000), `--repeats` and `--warmup-batches`; defaults retain the prior short-study protocol. `--passed-neow-input --families R --populations 65536` measures admitted direct Capsule bodies using the same request's real N survivors and existing N coverage binding. Its R denominator is actual Compact input and R timing excludes N; synthetic stride input cannot stand in for that calibration geometry. Keep every isolated process below the study's time limit and hold the shared performance mutex.

## Integrated Neow computation checks and local pricing

`-Y2Probe N-trials` checks current optimized N against the original body, both NativeFloat64 and IntegerUnitFloat, with complete ABI1, bounded CPU oracles, dual slots, public/private output, prefix/seed-space boundaries and cancellation reuse. `RT2_NEOW_CHECK_MODE=combined` limits this existing check to the production combination when individual methods already have current evidence.

`-NeowCanonicalCost -CanonicalMode both -CanonicalInputs '65536,1048576,16777216' -Repeat 3` reuses the existing R43/R44 public Invoke collector in a bounded N entry. It runs through a live Godot main loop and records actual body/workshape/device/source-survival, complete Invoke wall time, excluded warmups, ABI1 digests outside timing and cleanup status. `-CanonicalInteger` selects IntegerUnitFloat; `-CanonicalSplit validation` retains separate validation evidence. `-CanonicalSampleMs` and `-CanonicalMinBatches` are both minima per sample. The output does not update prices. Offline reviewed training knots can enter `CanonicalCostReference.json`; never relabel Y2 kernel diagnostics, private bindings, full searches or blocked-main-loop timing as these public calls.

CPU uses the existing functional `--cpu-cost-measure` entry. Its `outerInvokeMs/batches` is the complete-call boundary; `ms` remains the older inner executor diagnostic. Actual canonical keys and modeled source survival accompany each row. Environment controls and credit are maintained in [Search/README](../../Search/README.md). The adopted methods reference shion0528 (Egoistic今宵), resource.17 fixed `da82f7f9023c3d3598d50b8642e93f2801c37ccc`; their applicability is independently adapted to current RT2. Full-search off/on comparisons must use the actual automatic planner and complete results; individual method gains do not add together.
