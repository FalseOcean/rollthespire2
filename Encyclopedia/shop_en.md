# Shops: What Will Each Visit Sell?

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

RT2 filters the initial inventories of up to **five normal shops** for Shop-exclusive relics, Uncommon colorless cards, and Rare colorless cards. All three categories share the visit range; each can require a particular visit or a result anywhere within that range.

For example, require a relic within the first three shops and a Rare colorless card in shop two. A fixed second-visit target is not satisfied by finding it in shop one. With several targets allowed anywhere in the range, each must have a corresponding result within that range.

## Visit Order

Shop ordinals follow **the normal shops actually entered**, rather than their positions on the map. A skipped shop generates no inventory for that visit and takes no ordinal. The filter assumes these visits occur in sequence; it does not establish a route through them.

Multiplayer inventories follow each player's personal state rather than one shared party inventory.

## Relic Queues and Colorless Cards

Shop-exclusive relics come from the Shop bag shuffled at the opening. Each normal shop takes the next sellable relic from its back. The Relics page shows initial queues without subtracting later pickups; the Shop page follows the exclusive slot by visit. Other effects that remove relics or change eligibility require the sequence to be reconsidered. See [[relic-shop-consumption]].

Colorless cards use the player's persistent `Shops` RNG when the inventory is generated. Uncommon comes before Rare, with price rolls and other inventory steps advancing the same stream. Shop two continues after shop one's complete inventory; there is no separate “second-shop seed.”

## Initial-Inventory Premise

The colorless sequence starts from unused Shops RNG and assumes no extra consumption between normal inventories. Fake Merchant prices and Courier restocks can shift later colorless results. Restocked items are outside the initial-inventory target. See [[shop-stability]] for these effects and the additional history required by ordinary items.
