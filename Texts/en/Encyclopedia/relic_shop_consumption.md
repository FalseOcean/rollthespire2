# How Shops Consume Relic Queues

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

A normal initial shop has three relic slots. The first two roll Common, Uncommon, or Rare; the third requests Shop rarity. All take identities from the **back** of the player's corresponding bag.

## Two Ends of the Same Bag

RT2 displays Common, Uncommon, and Rare queues from the front by default. In A → B → C → D → E, a front-drawing reward encounters A, while a shop requesting that rarity begins looking at E. Both ends consume the same bag, so a queue position is not an ordinal among all relic pickups in the run.

Shops skip relics that cannot be sold. In vanilla 0.111.0, {{relic:LUCKY_FYSH}}, {{relic:AMETHYST_AUBERGINE}}, {{relic:BOWLER_HAT}}, {{relic:OLD_COIN}}, and {{relic:THE_COURIER}} cannot appear in a normal shop, even at the back of a bag. The first four relate to gold gains; the Courier changes discounts and restocking.

RT2's Shop-exclusive queue already follows back order and filters unsellable entries. Its first entry is the first available relic for a normal shop's exclusive slot.

## Generating Inventory and Purchasing

A relic leaves the bag when inventory is generated, rather than when purchased. Leaving it unsold does not preserve it for another reward. Taking the identity adds no fresh random draw; the first two slots roll randomness when choosing rarity. Purchasing separately obtains the relic and triggers its on-obtain effects.

With {{relic:THE_COURIER}}, a purchased relic restocks by rolling Common, Uncommon, or Rare and taking from that bag's back. This does not draw another Shop-exclusive relic. See [[shop-stability]].
