# Pael: Three Pools with Unequal Weights

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Pael offers three Ancient relics, each drawn from its own pool:

> **First position: a fixed choice of three.**
> **Second position: deck-dependent candidates with special weights.**
> **Third position: depends on whether the player already has an Event Pet.**

The main question is **what the second and third pools actually contain in this run**.

# First Position: A Fixed Choice of Three

The candidates are always:

- {{relic:PAELS_FLESH}}
- {{relic:PAELS_HORN}}
- {{relic:PAELS_TEARS}}

There are no additional eligibility requirements. Each has probability **1/3**, making this the simplest position.

# Second Position: A Weighted Pool

Four relics are possible:

- {{relic:PAELS_WING}}
- {{relic:PAELS_CLAW}}
- {{relic:PAELS_TOOTH}}
- {{relic:PAELS_GROWTH}}

It is not a uniform choice of four. **{{relic:PAELS_WING}} is always present.** Two other candidates require deck conditions.

### {{relic:PAELS_CLAW}}

The deck must contain at least **3 Defend cards that can legally receive Goopy**. Only then does {{relic:PAELS_CLAW}} enter the pool.

### {{relic:PAELS_TOOTH}}

The deck must contain at least **5 normally removable cards**. Only then does {{relic:PAELS_TOOTH}} enter the pool.

## Why Is {{relic:PAELS_GROWTH}} Less Likely?

{{relic:PAELS_WING}} and any eligible {{relic:PAELS_CLAW}} / {{relic:PAELS_TOOTH}} each receive **2 units of weight**. **{{relic:PAELS_GROWTH}} always receives 1**.

The pool is therefore:

> **Each eligible regular candidate ×2 + {{relic:PAELS_GROWTH}} ×1**

| Eligibility | Probabilities in the second position |
| --- | --- |
| Neither Claw nor Tooth | {{relic:PAELS_WING}} **2/3** · {{relic:PAELS_CLAW}} 0 · {{relic:PAELS_TOOTH}} 0 · {{relic:PAELS_GROWTH}} **1/3** |
| Only Claw | {{relic:PAELS_WING}} **2/5** · {{relic:PAELS_CLAW}} **2/5** · {{relic:PAELS_TOOTH}} 0 · {{relic:PAELS_GROWTH}} **1/5** |
| Only Tooth | {{relic:PAELS_WING}} **2/5** · {{relic:PAELS_CLAW}} 0 · {{relic:PAELS_TOOTH}} **2/5** · {{relic:PAELS_GROWTH}} **1/5** |
| Both Claw and Tooth | {{relic:PAELS_WING}} **2/7** · {{relic:PAELS_CLAW}} **2/7** · {{relic:PAELS_TOOTH}} **2/7** · {{relic:PAELS_GROWTH}} **1/7** |

{{relic:PAELS_GROWTH}} has no special rarity classification here; its weight is simply **half that of each other eligible option**.

# Third Position: Event Pets

The base candidates are:

- {{relic:PAELS_EYE}}
- {{relic:PAELS_BLOOD}}

If the player **does not currently have an Event Pet**, the game also adds:

- {{relic:PAELS_LEGION}}

### Without an Event Pet

> {{relic:PAELS_EYE}}: **1/3**
> {{relic:PAELS_BLOOD}}: **1/3**
> {{relic:PAELS_LEGION}}: **1/3**

### With an Event Pet

{{relic:PAELS_LEGION}} is excluded:

> {{relic:PAELS_EYE}}: **1/2**
> {{relic:PAELS_BLOOD}}: **1/2**

Event Pet ownership directly changes the third pool.

## You Can Already Have an Event Pet in Act 1

“Event Pet” does not only mean a companion already following you.

In Act 1's **Overgrowth**, **{{event:BYRDONIS_NEST}}** lets you take **{{card:BYRDONIS_EGG}}** into your deck.

**As long as {{card:BYRDONIS_EGG}} remains in the deck, the game already treats you as having an Event Pet**, even before it hatches:

> Take the egg in Overgrowth
> → Meet Pael in Act 2
> → {{relic:PAELS_LEGION}} cannot enter the third pool

Only {{relic:PAELS_EYE}} and {{relic:PAELS_BLOOD}} remain, each at **1/2**.

## It Still Counts After Hatching

{{card:BYRDONIS_EGG}} provides a hatching option at a Rest Site. Hatching gives you **{{relic:BYRDPIP}}**, which is itself an Event Pet.

> **Egg still in the deck: counts as an Event Pet.**
> **Egg hatched into {{relic:BYRDPIP}}: still counts.**

Pael cares whether the player currently has any Event Pet source, not whether the pet has hatched. The egg is a base-game example that can establish this state as early as Act 1.

# Why Should These Conditions Be Accurate?

**An option's eligibility can affect your target even when that option is not itself a target.**

Suppose you only want {{relic:PAELS_WING}}. With neither {{relic:PAELS_CLAW}} nor {{relic:PAELS_TOOTH}}, its probability is **2/3**; with both eligible, it becomes **2/7**.

Likewise, even if you do not care about {{relic:PAELS_LEGION}}, taking {{card:BYRDONIS_EGG}} in Act 1 changes the third pool from {{relic:PAELS_EYE}} / {{relic:PAELS_BLOOD}} / {{relic:PAELS_LEGION}} to {{relic:PAELS_EYE}} / {{relic:PAELS_BLOOD}}.

Eligibility describes the **actual candidate pool**, not just which relics you want to filter.

# How Does RT2 Treat Pael?

RT2 separates two kinds of information:

**Targets:** which relics you want Pael to offer.

**Premises:** whether {{relic:PAELS_CLAW}}, {{relic:PAELS_TOOTH}}, and {{relic:PAELS_LEGION}} are currently eligible.

The premises do not require the seed to offer those relics. They tell RT2 **which candidates participate in the random draw**.

If you already took {{card:BYRDONIS_EGG}} or have another Event Pet source, treat {{relic:PAELS_LEGION}} as unavailable.

Once the pools are known, all three draws are straightforward. Remember:

> **The second pool is weighted: Wing, Claw, and Tooth have 2 units each; Growth has 1.**
> **Legion's eligibility depends on Event Pet ownership, and an Act 1 Byrdonis Egg already counts.**

Pael's complexity comes from **prior player history changing the pool before randomness happens**, rather than from a long random process.
