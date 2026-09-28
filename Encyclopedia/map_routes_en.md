# Map Routes: How Far Can the Best and Worst Cases Go?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

A map has many routes from the start to the Boss. Total Elite, Rest Site, or Unknown count is often less useful than:

> **Across all legal routes, what is the minimum count visited, and what is the maximum?**

RT2 expresses this with two map properties.

Click a seed below to open it in the predictor using the predictor's current character and Ascension settings.

# Guaranteed: The Minimum You Must Visit

`Guaranteed` is **the minimum across all complete routes**.

For example:

> [[seed:001W48N6QUWB]]

Its **GuaranteedElite = 4** means that even a route chosen specifically to minimize Elites still visits **at least four**. It is a genuinely unavoidable four-Elite map.

`GuaranteedElite ≥ 4` means **every route has at least four Elites**.

Conversely, `GuaranteedElite ≤ 2` only means **at least one route has at most two Elites**. It does not limit every route to two.

# Reachable Max: The Most You Can Visit

`ReachableMax` is **the maximum across all complete routes**.

For example:

> [[seed:001W48LRVYMR]]

Its **ReachableMaxRest = 1** means that even the route maximizing Rest Sites can visit only one.

`ReachableMaxRest ≤ 1` is therefore a strong restriction: **no route can visit two Rest Sites**.

Conversely, `ReachableMaxElite ≥ 4` means **at least one route can visit four Elites**.

# Two Properties, Two Comparison Directions

For one node type, four different requirements are possible:

| Condition | Meaning |
| --- | --- |
| `Guaranteed ≥ K` | **Every route** contains at least K |
| `Guaranteed ≤ K` | **Some route** contains at most K |
| `ReachableMax ≥ K` | **Some route** contains at least K |
| `ReachableMax ≤ K` | **Every route** contains at most K |

A good map does not have one universal direction. `ReachableMaxElite ≥ 4` seeks a high-Elite route; `GuaranteedElite ≥ 3` requires high Elite density whichever route you take.

For unwanted node types, the preferred direction may be reversed.

# A Special Property: Forced Monster Prefix

Some properties describe structure rather than total counts. For example:

> [[seed:001W48N78QTT]]

Its **ForcedMonsterPrefix = 6** means **every legal branch starts with six consecutive normal combat nodes**.

This is an unavoidable six-combat opening. The measure concerns the shared unavoidable normal-combat prefix, not the total normal combats across the Act.

# What Are Map Conditions Actually Filtering?

Three useful questions are:

> **What is the minimum?** — Guaranteed
> **What is the maximum?** — Reachable Max
> **What opening structure is unavoidable?** — Forced Monster Prefix

Use `≥` or `≤` to express the goal.

RT2 need not decide that more Elites or more Rest Sites are better. It describes **what the map permits and what no route can avoid**. The player decides which map is worth searching for.
