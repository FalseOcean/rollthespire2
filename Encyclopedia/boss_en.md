# Bosses and Act Variants

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

You may require a particular Act 1 variant, a specified Boss in an Act, or a particular final-Act Boss pair at A10.

These are determined from the seed at the start. You need not reach the Boss room to know them.

# Act Variants

An Act position can use different variants. In 0.111.0, **Act 1** is the one with random alternatives:

| Position | Variant |
| --- | --- |
| Act 1 | {{act:OVERGROWTH}} / {{act:UNDERDOCKS}} |
| Act 2 | {{act:HIVE}} |
| Act 3 | {{act:GLORY}} |

Each variant has its own enemies, events, Bosses, and other world content. {{act:OVERGROWTH}} versus {{act:UNDERDOCKS}} changes the Act's content, not only its appearance.

# How Is the Act 1 Variant Chosen?

The game first obtains unlocked Act 1 candidates. If both {{act:OVERGROWTH}} and {{act:UNDERDOCKS}} are available, normal selection chooses between them.

With full unlocks and both discovered:

> **{{act:OVERGROWTH}}: 1/2**
> **{{act:UNDERDOCKS}}: 1/2**

## Newly Unlocked Variants May Take Priority

In single-player, an unlocked non-default Act **not yet discovered by the player** takes priority over ordinary random selection.

Newly unlocking {{act:UNDERDOCKS}} may therefore cause it to be chosen directly. Normal random selection resumes after it is recorded as discovered.

If the opening settings explicitly choose Act 1, that choice is authoritative; it is no longer a random result to filter.

# Bosses Belong to Their Act Variant

Each Act has its own pool.

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

Normally, one of the Act's three Bosses is chosen, each with **1/3** probability.

Act 1's Boss therefore depends on its variant. {{encounter:WATERFALL_GIANT_BOSS}} belongs to {{act:UNDERDOCKS}}, so {{act:OVERGROWTH}} + {{encounter:WATERFALL_GIANT_BOSS}} is impossible, not merely rare.

# Boss Randomness Is Part of World Generation

To a player, the draw is one of three Bosses. During world generation, however, Bosses share an advancing world random state with events, normal encounters, Elites, and other content.

Changing the variant changes both its Boss pool and earlier generated content and RNG consumption.

Boss identity is therefore **part of the complete world-generation result for that variant**. All of this occurs at the opening, so it remains predictable early.

# Two Bosses at A10

At **A10**, the final Act has a second Boss. The game selects the first normally, then the second from the remaining two. They cannot be identical.

For {{act:GLORY}}:

> First Boss: one of three
> Second Boss: one of the remaining two

A specified ordered pair, such as {{encounter:QUEEN_BOSS}} → {{encounter:AEONGLASS_BOSS}}, has base probability **1/3 × 1/2 = 1/6**.

Order matters because the game distinguishes the first and second Boss.

# How Does RT2 Do It?

RT2 determines the actual Act variant, then recovers Boss identities from that variant's world generation.

You can filter for a variant, a Boss, or **a variant and Boss together**. At A10, you can also specify the final Act's first and second Bosses.

They belong together because **the variant determines the Boss pool, and the Boss is generated from that world content**.
