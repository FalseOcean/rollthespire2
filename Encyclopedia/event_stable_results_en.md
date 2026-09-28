# Stable Event Results: Colorful Philosophers, Fake Merchant, and Junk Heap

> Applies to: **Slay the Spire 2 Beta 0.111.0**

These three result types share a feature: **once the event occurs, almost all inputs needed for the result are already known**.

They use the event's own randomness without reconstructing earlier combat rewards, shop visits, or relic pickups, making them well suited to direct filtering.

In multiplayer, Colorful Philosophers and Junk Heap use the relevant player's slot-specific event randomness, while Fake Merchant's inventory identities use shared event randomness. This describes generation assuming the event occurs; it does not establish that the event is eligible in every mode.

# Colorful Philosophers

## What You Want

Check **whether a specified character is among the three offered colors**.

The target is the initial character choice, not the cards generated after choosing a color.

## Why Is It Stable?

The game excludes the player's own character and locked character pools, then offers up to three of those remaining.

With all five base-game characters unlocked:

> Exclude your character
> → Four remain
> → Randomly remove one
> → Offer the remaining three

A specified other character appears with probability **3/4 = 75%**. With three or fewer legal other characters, all are offered without random elimination.

This depends on **seed, player slot, character, and unlock state**. Earlier Rewards, Shops, deck, and relic history do not change this offer.

## What Does RT2 Check?

RT2 checks whether the specified character is in the color offer.

The three card groups generated after choosing a color belong to a separate reward-generation process and are not the result filtered here.

# Fake Merchant

## What You Want

Require **a specified fake relic among the six inventory items**, without fixing its merchandise position.

## Why Is It Stable?

Fake Merchant has **nine fixed fake relics**. The game shuffles all nine and takes the **first six**.

Any specified one has probability **6/9 = 2/3** of appearing.

Inventory identities come entirely from the event's own randomness. They do not draw from normal relic bags, and prior normal shops do not change which six are sold.

## Prices Are a Separate Matter

The prices use the player's Shops RNG. **What is sold** is independently stable; **its prices and their impact on later normal shops** belong to shop history.

RT2 currently filters the former.

# Junk Heap

## What You Want

Two choices have random results:

> **Take a card: receive a random card.**
> **Take a relic: receive a random relic.**

You can specify the desired result for either branch.

## Taking a Card

The card branch chooses from **ten fixed cards**, each with probability **1/10 = 10%**.

It does not perform normal reward rarity generation or generate a new card from the player's current character pool.

## Taking a Relic

The relic branch chooses from **five fixed relics**, each with probability **1/5 = 20%**.

It does not use the Common, Uncommon, or Rare relic bags.

# Why Can the Branches Be Predicted Separately?

The two hidden outcomes are not both generated on entering Junk Heap. Only the chosen branch executes its random call.

Both hypothetical branches start from the same initial event random state. For the same seed and player slot, RT2 can separately determine the card if you choose the card branch and the relic if you choose the relic branch.

The actual choice belongs to the player. RT2 does not require the other branch to run first.

# What Does “Stable” Mean Here?

Once event identity is known, few inputs remain:

| Event | Main inputs | Current filter result |
| --- | --- | --- |
| Colorful Philosophers | Seed, slot, character, unlocked pools | Specified character in the offer |
| Fake Merchant | Seed | Specified fake relic in the six-item inventory |
| Junk Heap | Seed, slot, chosen branch | Specified card or relic |

None needs a long reconstruction of the journey to the event.

“Stable” does not mean guaranteed to occur. It means **that if the event occurs, the target result is not easily changed by earlier gameplay**.
