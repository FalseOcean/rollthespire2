# Frozen map probability evidence

These twelve gzip files are RT2 research statistics for game0.111.0, not a bundle of game maps, text, media or source. They remain numerical inputs to `StandardMapProbabilityEstimator.cs` and its `Party` partial. Source packages must retain this explanation and both JSON records alongside the data; the Mod embeds only the gzip resources through `RolltheSpire2.csproj`.

| Files | Source record | Sampling and interpretation |
| --- | --- | --- |
| Six `*.hist.gz` | `provenance.json` | 65,536 canonical roots drawn by the recorded Xoshiro rejection sampler, paired across contexts. Only Final histograms are shipped; context variants are not independent extra samples. |
| Six `MP-*.signatures.gz` | `manifest.json` | 65,536 paired roots from the prospective contiguous range beginning1146000000000. This is not an unbiased global sample. Each root retains its joint scalar signature; legacy FastMask is ignored. |

The original game DLL SHA256 is `0861BFA1DF347538D932F22D580E75420F08082792EB914E53B4882764ACDBE9`. Solo `sourceSha256` identifies the earlier compressed Final/Stage/Both research file; `finalOnlySha256` identifies the current compressed Final-only file. Their hashes describe different artifacts. The current decompressed payloads were checked against the original Final prefixes. The precise trimming command/commit is unknown / not recorded; the data was not regenerated. Multiplayer names, sizes, hashes, range and encoding are in `manifest.json` and match the original paired study files.

Solo encoding is gzip containing little-endian uint32 magic `0x4d354831`, uint64 sample count, then six families, each int32 size and repeated uint32 key + uint64 frequency. Family0 packs six4-bit guaranteed/prefix scalars; each other family adds one reachable-maximum scalar. Multiplayer encoding is MPS1 as specified in its manifest. Both records describe the actual shipped compressed bytes and formats, not a promise that all historical sampling tools are included here.

`A1plus` names the Swarming input class; it does not establish complete A10 second-Boss coverage. Phase10 reclassified the supported scope without changing these six solo numerical files. These are model observations with bounded independent native witnesses, not individually accepted real-game outcomes for every sample. Zero observed hits do not prove impossibility. Current probability/independence/confidence rules remain with the consumers; this packaging change does not alter them.

The study names/paths in the JSON records identify retained research provenance. The checked-in probability summary includes `docs/source_audits/SINGLEPLAYER_PROBABILITY_COVERAGE_20260922.md`; the original multiplayer study is identified by the manifest's Evidence/Source fields, not promised as an included source file. This source record does not assert additional permission to redistribute mirrored game expressions or resolve the project's outstanding permission scope.
