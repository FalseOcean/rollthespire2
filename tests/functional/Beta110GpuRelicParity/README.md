# Retained Beta110 numerical reference vectors

The pure vector generators remain available. The old G0-A Advanced controller, complete GPU backend and launch/log-validation scripts retired on 2026-09-11. This directory is no longer an executable Search/diagnostic host.

Current N/R checks use `tests/functional/Rt2FunctionalChecks` (`--family-neow`) and `NeowGpuSanity.gd` / `RfullGpuSanity.gd`. Production workloads use `tools/ProductionSearchHost/Run.ps1`.

Static fixed expectations are stored in:

- `Beta110GpuRelicG0A_CPU_VECTORS.json` for SplitMix64/xoshiro256**/NextInt;
- `Beta110GpuRelicG0A_RELIC_VECTORS.json` for all four relic prefixes and final RNG state using the synthetic dynamic pool.

The automatic runtime resolver can be fixture-tested independently with `tests/build/verify_sts2_runtime_autodiscovery.ps1`.
