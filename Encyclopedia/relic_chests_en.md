# Relics in Treasure Rooms

> Applies to: **Slay the Spire 2 Beta 0.111.0**

Treasure-room relics must be understood separately from the four player queues described earlier.

Normal chests use the **Shared Relic Bag**, rather than the player's personal Common / Uncommon / Rare bags.

# How Does a Chest Determine Its Relic?

A normal chest roughly follows two steps:

> Use its own random state to roll **Common / Uncommon / Rare**;
> take an identity from the **front** of that rarity's shared bag.

Predicting the result therefore requires both the rolled rarity and the current consumption position in the shared bag.

# Why Not Just Read the Relic Queue?

The four queues introduced earlier are **the initial orders of the player's personal bags**. Chests use a different, shared set.

The first relic in the player's Rare queue does not imply that the first Rare chest contains it. These are different queues.

# RT2's Current Boundary

RT2 can recover the initial Common / Uncommon / Rare orders of the shared bags to help explain chest mechanics.

However, **a formal filter requiring a specified relic in a treasure room is not yet complete**.

An actual chest result additionally needs its rarity RNG, prior shared-bag consumption, and the run state upon arrival.

Current relic filtering therefore focuses primarily on **the player's four initial Common / Uncommon / Rare / Shop queues**. Chest relics remain a separate future prediction problem.
