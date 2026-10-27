# Shops: Which Results Can Change?

Shops is usually easier to predict by visit count than Rewards, but generating a normal shop's initial inventory is not its only consumer. When I describe colorless cards as predictably stable, the premise is that nothing else advances Shops between those inventories.

## Fake Merchant Rolls Prices Before You Buy

Fake Merchant uses the event's own RNG to select six fake relics from its fake-relic collection, without drawing from your Shop bag. However, each item's price variation uses your Shops stream when that inventory is generated.

Meeting Fake Merchant can therefore shift later normal shops' colorless cards even if you buy nothing. Generating those six items is not the difficult part; establishing whether the event actually appears along your chosen route is. The Shop filter currently does not reconstruct that history for every seed.

## Courier Restocks Advance Shops

{{relic-icon:THE_COURIER}} refills purchased item slots. Its discount needs no randomness, but new item identities or prices still have to be generated. Even buying something other than a colorless card can advance Shops through restocking and change later colorless cards.

Here we need to distinguish refilling a relic slot from supplying another Shop-exclusive relic. A purchased Shop-exclusive relic's slot can refill, but its replacement rolls Common, Uncommon, or Rare; **it does not request Shop rarity again**. Repeated purchases therefore do not keep drawing later Shop-exclusive relics in the same shop.

Fake Merchant does not draw from the Shop bag, and Courier restocks do not keep drawing from it. Under these two effects, each normal shop's initial exclusive relic still corresponds to one visit. Other mechanisms that directly remove relics or change sale eligibility need separate consideration; see [[relic-shop-consumption]]. Treasure rooms use a separate shared bag, explained in [[relic-chests]].

## The Premise I Use

Including Fake Merchant and restocks would require knowing whether the event appears, whether you obtain Courier, what you buy, and how often slots refill. These are reasonable player needs, but the current Shop filter does not take them as route inputs.

The current colorless query therefore computes **successive initial normal-shop inventories with no extra Shops consumption between them**. Under that premise, colorless cards have a stable correspondence with visit count. If those extra processes occur in play, later results need to be reconsidered. Shop-exclusive relics are usually more stable, which is why I moved their filter from the general relic sequences to the Shop page. See [[shop]] for the entry point and visit numbering.
