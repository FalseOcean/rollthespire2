# GPU readback and scheduling improvements

[简体中文](gpu-performance.zh-CN.md) · [繁體中文](gpu-performance.zh-HK.md)

## Problem and changes

A Neow batch may produce few candidates while still paying fixed costs for reading from a large source buffer and submitting each batch separately. This change extends the original Mod to reduce transfers and submissions.

- Pack ABI1 directly from a `ulong` span into GPU `uint` bytes, retaining ordering and overflow checks.
- Use 4 KiB scratch for ≤1024 candidates; a combined bank reads a 32-byte header and candidate prefix together.
- Use a 256 KiB medium bank for 1025–65536 candidates, retaining the complete fallback for larger outputs.
- Share Submit/Sync across K4 groups; single-player N→R epochs combine the current Neow group and previous Relic group.
- Fix unsupported pending-tail fallback and premature GPU parity ownership after CPU offload.

Uploads finish before a command list opens, and synchronization finishes before readback or resource release. Original candidate ordering, filter semantics, CPU Exact, safe cursors, and cancellation commitment boundaries remain. Necessary Sync calls are retained; no standalone application is included.

## Defaults

Small/combined/medium readback, Neow K4 batching, and epochs are enabled. `Rt2TinyInputCpuOffload`, `Rt2BatchMergeRelic`, and `Rt2NeowPipeline` are disabled. Root batches remain `1 << 24`. The dual-RenderingDevice experiment is not multi-GPU execution.

## Reproduce

Requires .NET 9 SDK, NuGet package Godot.NET.Sdk 4.5.1, and references from your own Beta 0.111.0 game installation. Game DLLs are not redistributed.

```powershell
.\Build-Final.ps1 -GameReferenceDir '<game reference directory>'
dotnet run --project tools/NeowReadbackChecks -c Release -- readback-checks.json
dotnet run --project tools/BenchmarkRunLimitChecks -c Release
dotnet run --project tools/OrdinalPackingBenchmark -c Release -- --full-capacity --output=packing.json
```

The build entry point disables deployment and writes to `artifacts/Final/`. See the tool READMEs for scheduling and CPU Exact checks, using your own genuine exported inputs.

## Validation and limits

Recorded offline checks include a Release build, 431 readback/digest assertions across 86 scenarios, 32 range checks, 421 packing assertions, 12 scheduling-boundary checks for the default configuration, and production RNG/CPU Exact comparisons.

These tests do not execute a GPU. The latest integrated DLL has not completed a fresh paired in-game test, so these checks do not establish a new end-to-end speedup. D3D12 candidate completeness, native resource lifetimes, pause/cancel/resume, device recovery, and time to first valid result still require in-game validation.

Sync includes host waiting and driver costs; epoch shared waiting is mainly attributed to Neow, and some readback counters represent logical windows. Evaluate end-to-end validated root throughput and correctness rather than promising GPU utilization.

The original MPL-2.0 `LICENSE` and author notices are preserved; corresponding source for modifications and tools is included.
