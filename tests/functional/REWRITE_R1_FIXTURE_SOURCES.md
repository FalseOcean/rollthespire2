# Rewrite functional fixture sources — current Beta111 development baseline note

The fixture executable still exercises the production prediction classes and the
historical donor algorithms. The current runtime-selection boundary is:

```text
RuntimeProfileRegistry
  -> Beta110 profile for exact 0.110.1
  -> Beta111 profile for accepted-compatible 0.111.0
  -> Stable107 / Beta109 / Beta 0.110.0 remain archive/donor-only
```

Stable107 and Beta109 fixtures are retained as **direct donor/audit replay**. They invoke
`Stable107AnalysisProfile` or `Beta109AnalysisProfile` explicitly and must not be read as
runtime-registration tests. This preserves algorithm characterization without restoring
an archived profile to the production registry.

Assertions compare complete `ModelKey(Category, Entry)` values; readable aliases are not
business identities. The fixture project is not a substitute for the production build. `tests/run_r1_local.ps1` runs
current source-boundary safety/package-hygiene validation, production Debug/Release builds,
Seed/RNG vectors, and this functional fixture project. Focused GPU/Fast parity validators remain
separate because they are materially heavier.

## Stable107 donor fixture

```text
Seed=H0T0Z0S23C
Character=CHARACTER:SILENT
Ascension=10
Players=1
Authority=AuditedStaticExact / Complete
Expected=RELIC:SMALL_CAPSULE,RELIC:WINGED_BOOTS,RELIC:NEOWS_BONES
```

The identity and ordering come from the accepted old Workshop implementation and the
user's accepted 0.107.1 in-game sample. Stable107 is now an archived direct-reference
profile; exact `0.107.1` detection must resolve to `Unsupported` in production.

## Beta109 donor fixtures

```text
Seed=VADSEV23455G
RequestCharacter=CHARACTER:DEFECT
RequestAscension=0
Players=1
Authority=AuditedStaticExact / Complete
Expected=RELIC:ARCANE_SCROLL,RELIC:POMANDER,RELIC:CURSED_PEARL
```

The identity and ordering come from the accepted g10ab6 P1 path and the user's accepted
0.109.0 in-game sample. Beta109 direct fixtures remain available for audit and donor
regression only. Exact `0.109.0` and `0.109.1` detections must resolve to historical-donor
status and cannot select a production analyzer. `0.109.2` is unsupported and does not use
a Beta109 fallback.

## Beta110 production / Beta111 compatibility fixtures

Exact `0.110.1` must select `RuntimeProfileId.Beta110` with `AuditedExact`. Beta `0.110.0`
is prior-version evidence only and resolves to `ArchivedBetaReference` with an unsupported
runtime profile. Exact `0.111.0` must select the independent `RuntimeProfileId.Beta111`
game-version identity with `AcceptedCompatible`; it must not alias Beta110, while both Beta110 and Beta111 map to the shared `Modern110` RNG semantic profile. A version newer than `0.111.0` may use Beta111 only
through `LatestKnownFallback`; it must preserve the detected version, emit the fallback
warning path, and never inherit Beta110 acceptance.
