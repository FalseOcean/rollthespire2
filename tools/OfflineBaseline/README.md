# Offline production CPU comparison

This .NET 9 tool loads a selected real RolltheSpire2 assembly. It uses the production RNG vectors, replay serializer, query normalizer, compiler, visible-seed codec, and CPU Exact evaluator. It does not launch the game, initialize Godot native code, create a GPU device, or write player state.

Run baseline and modified assemblies in separate processes with the same exported context, workload, start, and bounded count. Use your own compatible game installation and genuine captured inputs; game libraries, private contexts, and runtime logs are not distributed here.

## Reproduce

Build the modified Mod from the repository root:

```powershell
.\Build-Final.ps1 -GameReferenceDir '<game reference directory>'
dotnet build tools/OfflineBaseline -c Release
```

The reference directory must contain the game's `sts2.dll` and `0Harmony.dll`. Build the unmodified upstream assembly separately with `-p:Rt2ModsDir=` to disable deployment. Replace the placeholders below with your own paths:

```powershell
$referenceDir = '<game reference directory>'
$baselineAssembly = '<unmodified upstream RolltheSpire2.dll>'
$modifiedAssembly = (Resolve-Path 'artifacts/Final/RolltheSpire2.dll').Path
$workload = '<exported workload.json>'
$context = '<exported context.json>'
$runner = 'tools/OfflineBaseline/bin/Release/net9.0/OfflineBaseline.dll'
dotnet $runner --assembly $baselineAssembly --reference-dir $referenceDir `
  --workload $workload --context $context --count 64 --output baseline.json
dotnet $runner --assembly $modifiedAssembly --reference-dir $referenceDir `
  --workload $workload --context $context --count 64 --output modified.json
& tools/OfflineBaseline/Compare-Results.ps1 `
  -Baseline baseline.json -Modified modified.json -Output comparison.json
```

Without a workload, the runner checks the built-in RNG vectors. A workload also enables typed replay round-trip and query-normalization checks. Without a valid captured context, CPU Exact is reported as skipped rather than silently run with invented authority.

For previously captured data, `--import-runtime-log <path> --release-info <path>` is an explicit alternative to `--context`. It requires matching workload and production semantic-fingerprint evidence, validates runtime identity and authority, and rejects contradictory inputs. It does not synthesize a catalog or authority.

`--count` defaults to 64 and accepts 1–100,000. Alternatively, `--seeds <text-file>` evaluates canonical seeds from a file. The runner evaluates the complete requested list. `New-OpeningRouteControl.ps1` can create a separate, simpler control workload; it must not replace the original query in a paired comparison.

## What the comparison establishes

Reports identify the assembly and input hashes. The comparator checks typed workload, normalized query, semantic context, per-seed root hash, disposition, match flag, failure code, complete serialized Evaluation hash, match set, and duplicate inputs/results.

These are small CPU correctness checks. They do not establish GPU prefilter completeness, D3D12 scheduling correctness, resource/fence lifetime safety, pause/cancel/resume behavior, device recovery, first-result latency, or end-to-end throughput. Evaluator timings exclude preparation and serialization and must not be presented as a GPU or overall search speedup.

The repository's `tools/**/*.cs` exclusion keeps this runner out of the installed Mod. Original MPL-2.0 licensing and notices remain in effect.
