# Developing RolltheSpire2

Start with [BUILDING.md](BUILDING.md) for dependencies, build and validation
commands. [README.md](README.md) describes player behavior and current limits;
[Search/README.md](Search/README.md) maps the search implementation. Preserve
[LICENSE](LICENSE) and [third-party attribution](THIRD_PARTY_NOTICES.md) with
source and binary distributions. Contributions should explain their purpose,
reproduction or expected behavior, affected version/context and checks actually run.
Keep existing authorship when importing contributions; partial reuse needs accurate
attribution and must not be represented as a complete merge.

## Source and correctness boundaries

`Core/Prediction` defines prediction contracts and the game-domain folders under
`Core` implement rules. `Infrastructure/Snapshots` captures immutable runtime facts.
`Search/Semantics` compiles requests, `Search/FamilyExecution` owns filters and
physical implementations, and `Search/Runtime/ProductionSearchSession.cs` owns
execution, continuation and result delivery. `ProductionExactSearchEvaluator` and
`ProductionQueryValidator` decide the original request's final modeled result.
`Ui` and `Presentation` consume those results.

Prediction and search must never mutate the real game RunState. Capture and Godot
UI updates belong on the main thread; background/GPU paths operate on detached
values. Fast filtering cannot confirm a result. Preserve the complete authored
request, shared-state/route witnesses and real failure causes; unknown observations
must affect only their dependents. Only final Match confirms modeled conditions;
Unknown/Unsupported remain explicitly unverified. The GPU/public composition ABI
is an ordered array of batch-local logical ordinals.

For a rules change, identify the actual game binary and affected RNG stream,
eligibility, draw order, side effects and continuation consumers before editing.
Check relevant CPU/GPU realizations and Exact independently. Record sampled parity,
build success and actual gameplay acceptance as different evidence. Use focused
existing flags in [Program.cs](tests/functional/Rt2FunctionalChecks/Program.cs);
there is no single default command that validates every subsystem.

## Public source scope

The public repository and PublicSource ZIP share
[Export-PublicSource.ps1](tools/source/Export-PublicSource.ps1). It exports one fixed
Git commit, including source, assets, embedded Texts, numerical resources, relevant
tests/tools and the minimal v1 Corpus. It never reads uncommitted source changes.
Internal task, audit, experiment and coordination documents are omitted, as are
private synchronization tooling and documentation-assertion checks that require
those records. Necessary public development descriptions remain self-contained.
The retained authored-Morphic JSON under `docs/source_audits` is a functional-test
input consumed directly by current checks, not an included audit document.

The minimal [Corpus](tools/ProductionSearchHost/Corpus/README.md) preserves requests,
expected results and provenance. Its full captured runtime context is internal,
so the frozen full benchmark cannot run from this subset. Many functional modes
(including predictor closure, Exact boundary and Family checks) read those contexts
and additional workloads directly; their source compiles but requires the matching
dataset to execute. The managed `--compatibility-reference` smoke, pure seed-vector
runner and source/package safety checks need no full captured Corpus. Native/game checks also
need a compatible local Godot .NET editor, game assemblies and sometimes matching
game resources; distribution includes none of those binaries. Use capture/replay
tools for a separate local dataset rather than replacing hash-pinned inputs.

The public main branch carries current development source. Tags and Releases
identify stable snapshots; a main checkout is not itself a released or accepted
game build. Internal source ZIPs are separate current-workspace handoffs and can
contain uncommitted development work. SOURCE_PACKAGE.json identifies the tree kind,
not a claim of validation or runtime acceptance.
