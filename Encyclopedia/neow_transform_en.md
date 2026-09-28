# {{relic:LEAFY_POULTICE}}: Checking Transformations Early

## What You Want

{{relic-icon:LEAFY_POULTICE}} transforms one starting Strike and one starting Defend during the opening.

Usually, you want to know:

> **What will those two cards become?**

You may require a specified card among the results, or seek two very rare outcomes. Such goals can be extremely unlikely, yet especially useful for early filtering.

## Why Can They Be Checked So Early?

Both transformations from {{relic:LEAFY_POULTICE}} use **Transformations RNG**.

This stream is separate from the `Rewards` and `Niche` streams used by many Neow effects. In an ordinary opening, it can be recovered almost directly from the seed's starting point:

> Seed
> → Transformations RNG
> → Strike transformation
> → Defend transformation

Knowing the starting targets and their pools is enough. RT2 does not first need to generate the full Neow offer or execute unrelated opening randomness.

This is particularly useful for rare targets. If the two transformation results reject almost every seed, RT2 can **check the transformations first**, then ask the few survivors:

> Does Neow actually offer {{relic:LEAFY_POULTICE}} in this run?

In the game, you still obtain the relic before transforming cards. RT2 changes the **order of checks**, not their meaning.

## How Is New Leaf Different?

{{relic-icon:NEW_LEAF}} is also an opening transformation, but in this version it uses **Niche RNG**.

Niche is not exclusive to {{relic:NEW_LEAF}}. Other Neow effects can consume it, making the result more dependent on earlier opening effects.

Although both transform cards:

> **{{relic:LEAFY_POULTICE}}: Transformations RNG**

> **{{relic:NEW_LEAF}}: Niche RNG**

Their prediction properties differ. {{relic:LEAFY_POULTICE}} usually has a more independent random position and is easier to check early. For {{relic:NEW_LEAF}}, we more often need to know whether another effect already used Niche RNG.

Similar-looking effects need not be calculated in the same way. What matters is:

> **Which RNG stream do they read, and what may already have happened to it?**

## Combining Multiple Transformations

With multiple opening transformation sources, you may not care whether a card came from {{relic:LEAFY_POULTICE}} or {{relic:NEW_LEAF}}.

You may simply want to know:

> **Do all these transformations together give me the cards I want?**

For example, obtaining {{relic:LEAFY_POULTICE}} and {{relic:NEW_LEAF}} through {{relic:NEOWS_BONES}} gives three relevant transformation opportunities.

RT2 can check the results together, requiring specified cards to be present or a minimum number of Rare results. This is what combined transformations mean.

Neow still determines which relics are obtained and any required pickup order. The transformation filter asks whether **their combined results satisfy your intended combination**.

A complex opening can thus be separated into two questions:

> **Did I obtain these transformation sources?**

> **Did they collectively produce the results I wanted?**
