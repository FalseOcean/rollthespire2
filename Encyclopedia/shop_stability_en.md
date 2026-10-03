# Shops: Which Results Can Change?

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

A shop looks like one inventory, but its items depend on different states. RT2 currently filters Shop-exclusive relics and the two colorless cards. Ordinary character cards and the first two relic slots require more run history.

## Shop-Exclusive Relics

A normal initial inventory has three relic slots. The first two request Common, Uncommon, or Rare; the third requests Shop rarity. That slot takes the next sellable relic from the back of the Shop bag shuffled at the opening. See [[relic-shop-consumption]].

Fake Merchant's six items come from a fixed fake-relic collection, rather than the player's bags. {{relic:THE_COURIER}} restocks with Common, Uncommon, or Rare relics, even when replacing a purchased Shop-exclusive relic. Neither action draws another `Shop` relic. Other effects that remove relics or change shop eligibility still need separate consideration.

## Colorless Cards and Shops RNG

Uncommon and Rare colorless identities use the player's persistent `Shops` RNG when a normal shop's initial inventory is generated. The entire inventory is generated in order, and price rolls advance that stream too.

A successful normal base-game 0.111.0 inventory consumes **28 Shops outputs**. Output **13** selects the Uncommon colorless card; output **15** selects the Rare one. Output 14 prices the Uncommon card and must still be counted. The next shop continues after the complete inventory.

RT2's colorless sequence assumes consecutive initial normal inventories with no extra Shops consumption between them. Two common changes are:

- **Fake Merchant prices:** event randomness selects the fake relics, but their six prices use the player's Shops stream, shifting the starting position of the next normal shop.
- **Courier restocks:** the 20% discount itself consumes no randomness. Restocking a purchased card, relic, or potion generates new identities or prices with Shops. Restocking other items can change future colorless cards even if no colorless card was purchased.

These actions affect later consumers of Shops; they do not reshuffle the existing Shop relic bag.

## Ordinary Cards and Relics

Shops selects the identities of the five character cards. Rarity reads the player's current rarity adjustment and rolls with `Rewards`, without updating that adjustment. Upgrade checks also use Rewards. Predicting Shop 3's first Attack therefore requires the earlier Rewards state, rarity adjustment, and available pools, rather than simply generating three shops.

The first two relic slots use Rewards to roll Common, Uncommon, or Rare, then take a sellable relic from the back of the corresponding personal bag. Both the RNG position and remaining bag contents matter.

For a personal Rare bag A → B → C → D → E, a front-drawing reward takes A while a shop begins looking at E. Front rewards, earlier shops, and direct relic removals change the remaining bag. Treasure rooms use separate shared bags, explained in [[relic-chests]], rather than consuming this personal bag's front.

Ordinary items follow deterministic generation rules, but current Shop filters do not promise to recover the complete route history before arrival. See [[shop]] for shop ordinals and filtering modes.
