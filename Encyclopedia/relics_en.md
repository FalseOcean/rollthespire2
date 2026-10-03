# Relic Queues: Common, Uncommon, Rare, and Shop

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

RT2 reconstructs four personal opening queues: Common, Uncommon, Rare, and Shop-exclusive. A target can be required among the first few positions or at a specified position. These are **initial bag orders**, without subtracting later inventory generation, reward generation, or pickups.

## How Is the Order Established?

Available relics are grouped by rarity and shuffled at the opening. The seed, character, unlocks, and relevant generation conditions determine their initial order. Each bag is shuffled separately within the same opening generation process, rather than receiving a fresh independent RNG start.

Common, Uncommon, and Rare default to front order. For a Rare bag A → B → C → D, a front-drawing Rare source encounters A, then B. An actual reward must still choose which rarity to request, so “A is first in the Rare bag” does not mean “A is the first relic obtained in the run.”

The Shop queue reads from the back and skips relics that cannot be sold. Its first displayed entry is the first available relic for a normal shop's fixed Shop-exclusive slot. See [[relic-shop-consumption]] for how shops read the other rarity bags.

## A Queue Position Is Not a Pickup

“A target is among the first three Rare relics” describes its position. It does not establish when a Rare reward occurs, whether its source is reachable, or whether another effect has already consumed the bag. Analysis can also show the opposite end of Common, Uncommon, and Rare bags; both ends belong to the same bag.

Treasure rooms use separate shared bags, explained in [[relic-chests]]. See [[shop]] to filter inventories by normal shop visit order.
