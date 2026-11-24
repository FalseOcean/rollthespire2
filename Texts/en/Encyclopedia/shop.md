# Shops: What Will Each Visit Sell?

Each time you enter a normal shop, the game generates an inventory. If the seed determines random results, can we know what each visit will sell in advance? I want to offer that filter, but shop items are not all equally easy to predict.

Character-card rarity uses Rewards RNG, while identity uses Shops RNG. The first two ordinary relic slots also use Rewards to choose a rarity, then take an identity from the corresponding relic bag. To predict these accurately, we need the incoming Rewards position and the remaining bag contents. Combats, events, and player choices can change those premises. “Shop two” alone does not reconstruct that route, and doing so for every candidate would be expensive for a fast seed search.

Some inventory slots are more stable. The third relic slot always requests a **Shop-exclusive relic**, taking successive items from the back of the Shop bag shuffled at the opening. It does not need another Rewards roll for rarity or identity. The two colorless slots have fixed Uncommon and Rare rarities, with identities selected by Shops. Shops also rolls price variation, and base-game shop potions use it for rarity and identity. The whole inventory's randomness cannot be attributed to Rewards.

That is why I put Shop-exclusive relics and colorless cards in the Shop filter: under the usual premises, their identities are affordable to predict without reconstructing the entire route. “Shop relic” here means the exclusive slot, rather than every relic sold by a merchant.

## Count Shops You Actually Enter

RT2 currently filters the initial inventories of the first **five normal shops** for Shop-exclusive relics, Uncommon colorless cards, and Rare colorless cards. All three categories share the visit range; each can target a specific visit or require an appearance somewhere in that range. For example, require a relic within the first three shops and a Rare colorless card in shop two.

Ordinals follow **the normal shops you actually enter**. A shop you pass without entering generates no inventory and takes no ordinal. The filter itself does not prove that a route through those shops exists. Multiplayer inventories follow the corresponding player's personal state.

Each shop continues from the Shops state left by the previous shop's complete inventory. Colorless identities are relatively easy to predict, but still depend on that continuous stream. Fake Merchant and Courier restocks can change it; see [[shop-stability]]. For how shops take relic identities from bags, see [[relic-shop-consumption]].
