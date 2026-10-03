# Relics in Treasure Rooms

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

Normal chests draw from **shared relic bags**, separate from the player's personal Common, Uncommon, and Rare bags described in [[relics]]. Single-player also has these separate shared bags.

A chest first uses its chest RNG to roll Common, Uncommon, or Rare, then takes an identity from the front of the matching shared bag. The result depends on both rarity and earlier shared-bag consumption. The first personal Rare relic does not establish the first Rare chest's contents.

RT2 can reconstruct and display the shared bags' initial Common, Uncommon, and Rare orders. A formal filter requiring a particular relic in a treasure room is not yet complete. Actual chest prediction also needs rarity RNG, the run state on arrival, and prior consumption; displaying the queue does not supply those steps.
