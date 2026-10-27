# Public baseline requests

This PublicSource tree contains only the v1 requests, initial expected results,
build provenance and manifest. Workload hashes identify the original frozen input.
The shared full runtime context and other internal datasets are not distributed.
RunCorpus.ps1 requires that matching context; it cannot run the complete frozen
benchmark from this public subset. Capture a fresh context and matching workload
using RequestCapture.ps1 and Run.ps1 for a separate dataset. Never substitute a
fresh capture while claiming the frozen baseline or its historical measurements.

See [the host guide](../README.md) and [development guide](../../../DEVELOPMENT.md)
for prerequisites and the available validation scope. Capture/replay implementation
is retained under Infrastructure/Snapshots and tools/ProductionSearchHost.
