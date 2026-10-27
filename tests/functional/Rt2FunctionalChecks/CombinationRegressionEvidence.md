# Fixed combination witnesses (2026-09-26)

Source baseline: `b8642e42068ac378afe492b6ac03b21463c04469`.
Entry: `dotnet run --project tests/functional/Beta110FastSearchParity -c Release --no-build -- --combination-regressions`.
Build with `-p:Rt2ModsDir= -m:1`; on this task's machine, acquire
`Local\RolltheSpire2.Validation.20260926` with `WaitOne(0)` in the invoking
PowerShell process and release/dispose in `finally` around builds/checks.

The suite uses only seeds `000000000000` and `0000000001FA`, one CPU candidate
per Family invocation, and Production Exact. There is no seed search, GPU or
native host execution, benchmark, deployment, or live game RunState.

## Counterexamples to treating necessary conditions as joint truth

| Fixture | Independently satisfied conditions | Joint Exact result |
| --- | --- | --- |
| Captured solo Ironclad A10, seed `0` | First-three-battle existential `HEADBUTT` repeated twice; solo C Fast retains the root | Typed unordered `[HEADBUTT, HEADBUTT, neutral]` is NoMatch: only battle 1 has it. Repeated legacy existential rows Match. |
| Controlled two-player fixture, seed `1FA`, P1 Bones Large Capsule + Arcane Scroll, AnyOrder | N requires Arcane `PYRE`; C requires first-battle NoDrop. Each CPU Family retains the root; each isolated Exact query Matches. | NoMatch: Large→Arcane produces PYRE and holds White Beast Statue; Arcane→Large produces UNMOVABLE and holds Prayer Wheel. Neither explicit order satisfies both predicates. |

These are **allowed Fast false positives**, not production bugs or recall loss.
The aligned `UNMOVABLE + NoDrop + BLUDGEON` query Matches; BLUDGEON appears only
in the unlisted Prayer Wheel's additional first-battle reward.
A future sound, stronger sieve may reject these candidates; that changes the
Fast expectation, not the Exact witness requirements.

## Added combination controls

- Same captured solo root: Any/All/Ban apply within the witnessing battle;
  separate existential rows may use separate battles. Set duplicates remain
  idempotent while typed duplicate slots require distinct battles. A three-battle
  card window does not expand to the accompanying six-battle potion window;
  card and potion targets may require different battles.
- Four fixed slots at `1FA`: P1 selects Bones Leafy Poultice/New Leaf in either
  explicit order; P2 observes New Leaf without selecting it; P3 is empty; P4
  selects New Leaf with a personal C condition. P2 sees initial `BODY_SLAM`,
  P4 sees `IMPERVIOUS` and first reward `FORGOTTEN_RITUAL`. Removing P1's selection,
  resetting the shared cursor at the gap, or advancing observation-only P2 gives
  distinct negative controls. Exact publishes only P1/P4 opening transactions.
- Controlled Capsule fixture through two empty slots: unlisted Whetstone skips
  its obtain shuffle and P4 observes `BRAND`; explicitly listing it executes the
  shuffle and P4 observes `CRIMSON_MANTLE`. Personal extraction RNG is unchanged.
  Every drawn relic retains its held reward effect under both premises.

## Authority and limits

Solo inputs reuse `tools/ProductionSearchHost/Corpus/v1/context.json`. Party
fixtures reuse those captured **solo pools** in a constructed fixed-slot party;
they are not captured multiplayer eligibility. The controlled Bones fixture
replaces the eligible pair with Large/Arcane and the bag with one Common
Whetstone, one Uncommon Prayer Wheel, and one Rare White Beast Statue. These tiny
lanes (including Prayer Wheel's fixture rarity) are synthetic, not game data.

Expected facts use Core Neow/reward projections with frozen output guards;
`ProductionExactSearchEvaluator` supplies whole-table truth. The checks exercise
`ProductionQueryValidator`'s distinct-battle assignment and
`PartyNeowProjection`/`NeowEffectProjectionEngine`'s explicit obtain premise.
CPU donor agreement is not treated as independent proof of game semantics.

No new production algorithm or approximation is introduced. Existing N/R
cross-order checks and the accepted solo hidden W/WP recall limitation are not
reclassified or expanded. Live multiplayer captures, native game acceptance,
GPU behavior, larger parties/pools, and other unmodeled hooks remain unverified
by this suite.

Validation on this baseline: root and functional-project Release builds passed
with zero warnings/errors and deployment disabled; the focused entry passed
192 assertions. `git diff --check` passed. All build/check invocations held the
named mutex; no performance or native runtime acceptance is claimed.
