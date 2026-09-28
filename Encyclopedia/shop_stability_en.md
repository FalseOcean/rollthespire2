# Stable and Unstable

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## Three Levels of Shop Prediction

A shop looks like one inventory, but its items have very different random sources. RT2 can reliably filter some results without trying to predict every item in the entire shop.

There are roughly three levels:

> **Shop-exclusive relics: the most stable.**
> **Colorless cards: predictable, but sensitive to extra shop-related actions.**
> **Ordinary cards and relics: strongly dependent on prior run state.**

# Level 1: Shop-Exclusive Relics Are Stable

A normal shop displays three relics. The first two are ordinary relics; the third always requests a **Shop-rarity relic**.

Shop relics are shuffled into their own bag at the start. Each normal inventory then takes the next legal relic from one end:

> Shop 1 → First shop-exclusive relic
> Shop 2 → Second shop-exclusive relic
> Shop 3 → Third shop-exclusive relic…

This is the relic result RT2 currently predicts directly on its Shop page.

## Fake Merchant Does Not Consume This Queue

Fake Merchant's relics do not come from the player's normal relic bags. His six items come from a fixed set of fake relics.

Meeting him does not remove a future shop-exclusive relic. His **prices** use Shops RNG, but this affects later consumers of that stream, not the pre-shuffled Shop bag.

## Courier Restocks Do Not Consume Later Shop Relics Either

The Courier restocks purchased items. A replacement relic rolls **Common / Uncommon / Rare**, never `Shop` rarity.

Even after buying the shop-exclusive relic:

> The initial Shop relic is taken normally
> → The Courier replaces it with an ordinary relic
> → The next Shop relic is not consumed

The next normal shop still takes the next item from the original Shop queue.

**Neither Fake Merchant nor Courier restocking disrupts the shop-exclusive relic sequence currently predicted by RT2.**

# Level 2: Colorless Cards Depend on Shop History

Uncommon and Rare colorless cards do not form a pre-shuffled item queue. They are generated on entry using the player's persistent **Shops RNG**.

A normal shop generates its whole inventory in order, rather than waiting for the player to inspect each section:

> Ordinary cards
> → Uncommon colorless card
> → Rare colorless card
> → Relic prices
> → Potions
> → …

Many price rolls also advance Shops RNG. The next shop's colorless cards depend on **where that stream ended after the previous shop**.

## One Normal Inventory Uses Many Shops RNG Outputs

In the normal base-game 0.111.0 inventory, a complete normal shop consumes **28 Shops RNG outputs**:

> Output 13 determines the Uncommon colorless card.
> Output 15 determines the Rare colorless card.

Output 14 only determines the Uncommon card's price, but must still occur. Even if RT2 displays only card identities, replay cannot skip seemingly unrelated price randomness.

# Why Does Fake Merchant Affect Later Colorless Cards?

His six fake relic identities use his own event randomness. Each still needs a price, and those six price calculations use **the player's Shops RNG**.

Thus:

> Normal Shop 1 → Normal Shop 2

and:

> Normal Shop 1 → Fake Merchant → Normal Shop 2

reach the second shop at different random positions. Its colorless cards may be completely different.

# Why Does The Courier Affect Colorless Cards?

The Courier's **20% discount itself consumes no randomness**. The disruption comes from **restocking after a purchase**.

A replacement card needs an identity and price; a replacement relic needs a price; a replacement potion needs an identity and price. These use Shops RNG.

Even without buying a colorless card, **restocking another item can change colorless cards in future shops**.

RT2's current colorless sequence assumes:

> **Consecutive normal shop visits with no extra Shops RNG consumption between them.**

Fake Merchant or Courier restocking breaks that premise for subsequent predictions.

# Level 3: Ordinary Cards Depend on the Wider Run

The five ordinary character cards might seem like just a few more cards to predict, but are substantially harder.

Generation must determine both **rarity** and **identity within that rarity**.

Shops RNG selects identity. Rarity comes from the player's card-rarity state, built on **Rewards RNG**. Upgrade determination also uses Rewards RNG.

Ordinary shop cards therefore depend on both the current Shops position and **how far Rewards has advanced throughout the run**.

Rewards is an active stream, used by combat card rewards, some Neow effects, and many other rewards.

Predicting the first Attack in Shop 3 requires more than generating three shops in sequence. We need the prior Rewards consumption along the way. This enters route and run-history prediction.

# Ordinary Relics Are More Complex Too

The first two relic slots are not Shop-rarity slots. The game first uses **Rewards RNG** to choose Common, Uncommon, or Rare, then takes an identity from the corresponding current bag.

The result depends on both:

> **Which rarity Rewards selects**
> **What remains in that rarity's bag**

## A Relic Bag Is a Double-Ended Queue

Common, Uncommon, Rare, and Shop relics are separately shuffled at the start.

Most normal relic reward sources, such as combats and chests, take from the **front** of the corresponding queue. Shops take from the **back**.

Conceptually, a Rare queue is:

> Ordinary rewards → `[ front ........ back ]` ← Shops

Both ends consume an already shuffled sequence. Even though ordinary shop relic identities are not freshly rolled on the spot, “Shop 2 is always this Rare relic” is not a valid general statement.

By then, other sources may have taken relics from the front, earlier shops may have taken from the back, and effects may have removed or changed bag contents. Rewards must also determine whether this slot requests Common, Uncommon, or Rare.

# Why Are Shop-Exclusive Relics Simpler?

### Shop-Exclusive Relics

> Always use the Shop bag
> → Normally consume one per normal shop
> → Fake Merchant does not touch it
> → Courier restocks do not draw further Shop relics

These are well suited to filtering by shop ordinal.

### Colorless Cards

> Identities come from persistent Shops RNG
> → Normal inventories can be predicted in sequence
> → Fake Merchant prices and Courier restocks advance the same stream

They are predictable under an explicit no-extra-shop-consumption premise.

### Ordinary Cards and Relics

More history must be recovered. Ordinary cards strongly depend on Rewards state. Ordinary relics depend on Rewards for rarity and on bag consumption from both ends.

They are not inherently unpredictable. The question has moved from **this seed's shop sequence** to **the shop after this actual player history**. Current Shop filters do not claim to cover that full scope.
