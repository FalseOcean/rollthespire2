# Bosses and Act Variants

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

RT2 can filter the Act 1 variant, each Act's Boss, and an ordered final-Act Boss pair at A10. These are generated at the opening, before you reach a Boss room.

## Act Variants and Discovery

| Position | Variant |
| --- | --- |
| Act 1 | {{act:OVERGROWTH}} / {{act:UNDERDOCKS}} |
| Act 2 | {{act:HIVE}} |
| Act 3 | {{act:GLORY}} |

Only Act 1 has random variants in 0.111.0. Each variant has its own enemies, events, Bosses, and other world content.

The game first collects unlocked Act 1 candidates. With full unlocks and both variants discovered, {{act:OVERGROWTH}} and {{act:UNDERDOCKS}} each have a 1/2 chance.

In single-player, an unlocked non-default Act that has not been discovered takes priority. Newly unlocking {{act:UNDERDOCKS}} can therefore cause it to be selected directly; normal randomness resumes after discovery. An explicit Act 1 choice in the opening settings uses that choice instead.

## Boss Pools by Variant

### {{act:OVERGROWTH}}

- {{encounter:VANTOM_BOSS}}
- {{encounter:CEREMONIAL_BEAST_BOSS}}
- {{encounter:THE_KIN_BOSS}}

### {{act:UNDERDOCKS}}

- {{encounter:WATERFALL_GIANT_BOSS}}
- {{encounter:SOUL_FYSH_BOSS}}
- {{encounter:LAGAVULIN_MATRIARCH_BOSS}}

### {{act:HIVE}}

- {{encounter:THE_INSATIABLE_BOSS}}
- {{encounter:KNOWLEDGE_DEMON_BOSS}}
- {{encounter:KAISER_CRAB_BOSS}}

### {{act:GLORY}}

- {{encounter:QUEEN_BOSS}}
- {{encounter:TEST_SUBJECT_BOSS}}
- {{encounter:AEONGLASS_BOSS}}

Normally, each Act selects one Boss from its three-entry pool, with a 1/3 chance each. {{encounter:WATERFALL_GIANT_BOSS}} belongs to {{act:UNDERDOCKS}} and cannot be paired with {{act:OVERGROWTH}}.

Boss selection shares an advancing world RNG with events, normal encounters, Elites, and other content. Changing the variant changes both its candidates and preceding generation steps. RT2 determines the actual variant before recovering its Boss through that world-generation sequence.

## Two Bosses at A10

The final Act at A10 selects the first Boss normally, then selects the second from the remaining two. They cannot repeat.

For {{act:GLORY}}, an ordered pair such as {{encounter:QUEEN_BOSS}} → {{encounter:AEONGLASS_BOSS}} has base probability **1/3 × 1/2 = 1/6**. Order matters. RT2 lets you specify the first and second Boss separately and combine Boss requirements with an Act variant.
