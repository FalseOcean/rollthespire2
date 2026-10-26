# Offline Neow readback and digest checks

This .NET 9 console tool links the production small, combined, and medium readback transports and `NeowCandidateDigest.cs`. A narrow fake Godot API queues copies until synchronous readback flushes them. The root Mod excludes `tools/**/*.cs`, so the fake and runner never enter the installed Mod.

## Reproduce

From the repository root:

```powershell
dotnet run --project tools/NeowReadbackChecks -c Release -- readback-checks.json
```

The current suite passes **431 assertions across 86 scenarios**. Its project enables the small, combined, and medium transport switches, matching the helpers under test. It needs neither game libraries nor a Godot native runtime.

## Coverage

- Small transport: up to 1,024 candidates use a 4 KiB scratch buffer. Checks cover empty output, boundaries, malformed counts, reuse, missing buffers, and full-capacity fallback.
- Combined transport: a 32-byte header and at most 1,024 `uint` candidates occupy a 4,128-byte bank. Deferred copies must complete before the returned header or payload is consumed.
- Medium transport: 1,025–65,536 candidates use a 256 KiB bank, with only the live payload copied. Larger output retains the direct-source fallback. Counts around both thresholds and the full 16,777,216-candidate capacity are covered.
- Byte contracts: ordering, duplicate values, `uint` extrema, changing payload sizes, stale/unwritten tails, invalid/overflow counts, short/empty/oversized native arrays, copy failures, read exceptions, and preservation of previously returned arrays.
- Digest contracts: independent SHA-256 goldens, empty batches, each metadata field, candidate values/order, batch order, repeated nondestructive reads, caller-array ownership, invalid counts, and disposal.

The transport helpers do not replace the executor's semantic-header, candidate-range, or strict-order validation. If a copy fails, a queued earlier copy may still exist; the caller must reject the batch and follow its existing failure cleanup rather than reuse the bank.

## Digest encoding

Version 1 starts with ASCII `RolltheSpire2.NeowCandidateDigest.v1\0` (37 bytes including NUL), once. Each batch adds unsigned little-endian `u64 base, u32 window, u32 survivorCount, u32 curseCount, u32 pairCount` (24 bytes), followed by each relative ordinal as little-endian `u64` without padding. `digest-golden-fixture.json` records an independent Python construction.

The digest describes completed Neow batches in order, including empty batches and work ahead of terminal commitment. It is not restricted to the safely committed cursor prefix.

## Limits

These checks do not execute a GPU, initialize Godot native code, measure real transfer/compute time, validate native fence/resource lifetimes, model device-failure cleanup, or replace paired in-game candidate-set validation. Exceptions propagate to the production executor's failure path.

Original MPL-2.0 licensing and notices remain in effect.
