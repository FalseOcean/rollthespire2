# Orobas: Branching Candidates, {{relic:SEA_GLASS}}, and Conditional Options

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Orobas offers three Ancient relic positions with separate rules:

- **First:** {{relic:ELECTRIC_SHRYMP}}, {{relic:GLASS_EYE}}, and one of {{relic:SEA_GLASS}} / {{relic:PRISMATIC_GEM}}.
- **Second:** a fixed choice of four.
- **Third:** {{relic:TOUCH_OF_OROBAS}} and {{relic:ARCHAIC_TOOTH}}, subject to the player's current state.

The first and third are not simple fixed pools.

# First Position: {{relic:SEA_GLASS}} and {{relic:PRISMATIC_GEM}}

Two base candidates always exist:

- {{relic:ELECTRIC_SHRYMP}}
- {{relic:GLASS_EYE}}

The game first chooses the special third candidate:

> **1/3: {{relic:PRISMATIC_GEM}}**
> **2/3: {{relic:SEA_GLASS}}**

Then it selects the first offer from those three candidates. Four relics are possible overall, but this is not a uniform choice of four.

| Relic | Appearance probability |
| --- | --- |
| {{relic:ELECTRIC_SHRYMP}} | **1/3 ≈ 33.33%** |
| {{relic:GLASS_EYE}} | **1/3 ≈ 33.33%** |
| {{relic:SEA_GLASS}} | **2/9 ≈ 22.22%** |
| {{relic:PRISMATIC_GEM}} | **1/9 ≈ 11.11%** |

{{relic:SEA_GLASS}} is exactly twice as common as {{relic:PRISMATIC_GEM}}.

## {{relic:SEA_GLASS}} Also Has a Character Target

Orobas randomly selects a target from the player's unlocked **other characters**. If {{relic:SEA_GLASS}} becomes the first option, it uses that selected character.

In the default fully unlocked base game, there are five characters. Excluding the current character leaves **four targets**.

The probability of {{relic:SEA_GLASS}} targeting one specified character is:

> **2/9 × 1/4 = 1/18 ≈ 5.56%**

Wanting {{relic:SEA_GLASS}} and wanting it to target a specified character are therefore different conditions. Its later effects can be covered separately.

# Second Position: A Fixed Choice of Four

The candidates are always:

- {{relic:ALCHEMICAL_COFFER}}
- {{relic:DRIFTWOOD}}
- {{relic:RADIANT_PEARL}}
- {{relic:SAND_CASTLE}}

Each has **25%** probability, with no additional eligibility conditions.

# Third Position: What Do You Still Own?

Only two Ancient relics are possible:

- {{relic:TOUCH_OF_OROBAS}}
- {{relic:ARCHAIC_TOOTH}}

Neither is always eligible. The game checks for legal targets in the player's actual state upon reaching Orobas.

## {{relic:TOUCH_OF_OROBAS}}

{{relic:TOUCH_OF_OROBAS}} improves the character's starting relic. In base-game 0.111.0:

| Starting relic | Improved relic |
| --- | --- |
| {{relic:BURNING_BLOOD}} | {{relic:BLACK_BLOOD}} |
| {{relic:RING_OF_THE_SNAKE}} | {{relic:RING_OF_THE_DRAKE}} |
| {{relic:DIVINE_RIGHT}} | {{relic:DIVINE_DESTINY}} |
| {{relic:BOUND_PHYLACTERY}} | {{relic:PHYLACTERY_UNBOUND}} |
| {{relic:CRACKED_CORE}} | {{relic:INFUSED_CORE}} |

Keeping the corresponding starting relic makes {{relic:TOUCH_OF_OROBAS}} eligible. Losing it earlier removes that option from the pool.

The question is not merely which character you are, but **whether you still own a starting relic that can be improved**.

## {{relic:ARCHAIC_TOOTH}}

{{relic:ARCHAIC_TOOTH}} checks for a signature starting card:

| Starting card | Result |
| --- | --- |
| {{card:BASH}} | {{card:BREAK}} |
| {{card:NEUTRALIZE}} | {{card:SUPPRESS}} |
| {{card:UNLEASH}} | {{card:PROTECTOR}} |
| {{card:FALLING_STAR}} | {{card:METEOR_SHOWER}} |
| {{card:DUALCAST}} | {{card:QUADCAST}} |

If the card remains in your deck, {{relic:ARCHAIC_TOOTH}} is eligible. If removed or transformed, it has no target and cannot enter the pool.

Again, the relevant input is **the actual deck upon reaching Orobas**, not what it contained at the start.

# What Happens to the Third Position?

If both {{relic:TOUCH_OF_OROBAS}} and {{relic:ARCHAIC_TOOTH}} are eligible, each has **50%** probability.

If only one is eligible, it is the sole Ancient relic in that position.

If neither is eligible, the third position displays a locked option instead of substituting another relic.

The randomness is simple. The essential step is establishing which relics the current state permits.

# How Does RT2 Treat Orobas?

First specify whether {{relic:TOUCH_OF_OROBAS}} and {{relic:ARCHAIC_TOOTH}} still have legal targets. RT2 uses this to reconstruct the third pool.

Then choose the Ancient relics you actually want. For {{relic:SEA_GLASS}}, you can also require **a particular character target**.

Remember these three features:

> **Sea Glass and Prismatic Gem first compete for the special candidate position.**
> **Sea Glass has a random other-character target.**
> **The third position depends on retaining the corresponding starting relic and card.**

Orobas depends more on current player state than a fixed-pool Ancient, but the rules remain clear.
