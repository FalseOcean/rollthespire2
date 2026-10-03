# Neow's Offer and {{relic:NEOWS_BONES}}

A normal base-game opening offers three relics: **one is drawn from a pool of relics with a trade-off, then two are selected from a separate pool.** These are not three draws from all opening relics. Pools and odds below use fully unlocked single-player 0.111.0; see [[reading-guide]] for the shared environment. Game modifiers that replace Neow's options require their own rules.

## Relics with a Trade-off

These relics have different costs; they do not all add curses. For example, {{relic:LEAFY_POULTICE}} reduces Max HP, {{relic:SILKEN_TRESS}} loses all gold, and {{relic:LARGE_CAPSULE}} adds an extra Strike and Defend. The pool has ten candidates:

- {{relic:CURSED_PEARL}}
- {{relic:DOWSING_ROD}}
- {{relic:HEFTY_TABLET}}
- {{relic:LARGE_CAPSULE}}
- {{relic:LEAFY_POULTICE}}
- {{relic-icon:NEOWS_BONES}}
- {{relic:NEOWS_SACRIFICE}}
- {{relic:PRECARIOUS_SHEARS}}
- {{relic:SILKEN_TRESS}}
- {{relic:SILVER_CRUCIBLE}}

One is selected from the currently legal candidates. All ten are available in the default environment, so a specified relic appears in this offer with probability **1/10**. Multiplayer excludes {{relic:SILVER_CRUCIBLE}}, changing those odds.

## Generating the Other Two Options

The first drawn relic excludes conflicting relics from the other pool:

| First drawn relic | Relics excluded from the other pool |
| --- | --- |
| {{relic:CURSED_PEARL}} | {{relic:GOLDEN_PEARL}} |
| {{relic:HEFTY_TABLET}} | {{relic:ARCANE_SCROLL}} |
| {{relic:LEAFY_POULTICE}} | {{relic:NEW_LEAF}} |
| {{relic:PRECARIOUS_SHEARS}} | {{relic:PRECISE_SCISSORS}} |
| {{relic:NEOWS_SACRIFICE}} | {{relic:PHIAL_HOLSTER}}, {{relic:LOST_COFFER}} |

Three pairs each undergo an either-or choice before the winner joins this pool: {{relic:LAVA_ROCK}} / {{relic:SMALL_CAPSULE}}, {{relic:NUTRITIOUS_OYSTER}} / {{relic:STONE_HUMIDIFIER}}, and {{relic:NEOWS_TALISMAN}} / {{relic:POMANDER}}. The first pair is omitted when the first drawn relic is {{relic:LARGE_CAPSULE}}.

Mode and unlock eligibility then remove illegal candidates; the game shuffles the result and takes two. For example, {{relic:MASSIVE_SCROLL}} is multiplayer-only, {{relic:WINGED_BOOTS}} is single-player-only, {{relic:KALEIDOSCOPE}} requires all character card pools unlocked, and {{relic:SCROLL_BOXES}} has its own eligibility requirement. The other two options depend on the first drawn relic and the three pair choices, so “3 ÷ all Neow relics” is not a valid calculation.

## Bones' Two Internal Relics

Seeing Bones only gives you the option to take it; its effects start on pickup. Bones uses all currently eligible Neow relics except itself, shuffling them with the player's **Rewards RNG** and taking two distinct relics. It does not reuse the top-level two-plus-one grouping or the top-level conflict exclusions.

Fully unlocked single-player has **28 internal candidates**. Conditional on taking Bones, a specified relic appears in either position with probability **1/14**; two specified distinct relics in either order have probability **1/378**. Including the top-level Bones chance of **1/10**, these become about **1/140** and **1/3,780**.

## Pickup Order

Internal relics are obtained and resolved in sequence. If the earlier one consumes random state read by the later one, or changes its deck, relic bag, or pending effect, **A → B** and **B → A** can differ. RT2 supports unrestricted or specified pickup order.

Kaleidoscope's card generation, for example, advances the Rewards state used for Capsule rarity; taking {{relic:SILKEN_TRESS}} first changes the next generated card reward. See [[neow-capsule]], [[neow-kaleidoscope]], and [[neow-silken-tress]]. The final curse generated after both effects is covered in [[neow-bones-curse]]; the shared random principle is in [[why-predictable]].
