# Compact ordinal upload check and microbenchmark

This offline tool links the **production** `FamilyGpuOrdinalPacking.cs` helper.
It compares its bytes with the original `ExportAbi1().ToArray().Select(...).ToArray()`
and `Buffer.BlockCopy` upload path. It checks empty, boundary, sliced-memory,
deterministic random and overflow inputs. `--full-capacity` adds the default
16,777,216-candidate batch. No game or GPU is started and no Mod is deployed.

Run from the repository root using a .NET 9 SDK:

```powershell
dotnet run --project tools/OrdinalPackingBenchmark -c Release -- --full-capacity --output=packing-benchmark.json
```

Results include five alternating original/direct rounds for three compact input
sizes. Each measurement reports elapsed milliseconds and managed bytes allocated
per serialization call. This measures CPU serialization, **not** GPU execution,
validated seeds per second, time to first match, or an overall search speedup.
Keep input sizes, SDK/runtime and concurrent workloads constant for comparisons.

The existing root project excludes `tools/**/*.cs`, so this runner is not included
in the Mod assembly. It requires no NuGet test framework or game libraries.
