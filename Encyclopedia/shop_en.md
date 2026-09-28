# Shops: What Will the Nth Shop Sell?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Shop filtering asks:

> **Will the Nth normal shop I enter contain what I want?**

RT2 currently focuses on three results:

- Shop-exclusive relics;
- Uncommon colorless cards;
- Rare colorless cards.

You can require a result in a **specific shop**, or simply **somewhere among the first few shops**. These are different requirements.

The target is each normal shop's **initial inventory before restocking**. The colorless sequence begins from the player's unused Shops RNG and assumes no extra consumption between shops, such as Fake Merchant prices or Courier restocks; see [[shop-stability]]. In multiplayer, inventories follow each player's personal state, rather than a single party-wide result.

# What Does “First Shop” Mean?

The ordinal is not the first or second shop drawn on the map. It is **the order in which the player actually enters normal shops**.

> First normal shop entered → Shop 1
> Next normal shop entered → Shop 2

A skipped earlier shop generates no normal inventory for that visit and does not consume an ordinal.

The filter asks what those shops would offer if entered in sequence. It does not prove that the map contains a route through them.

# Shop-Exclusive Relics Come from a Queue

**A shop-exclusive relic is not independently rerolled from scratch on every shop visit.**

At the start of the run, the game establishes separate ordered sequences for Common, Uncommon, Rare, and Shop relics. Shop-exclusive relics form a **pre-shuffled queue** consumed by later shops.

Shop 1, Shop 2, and Shop 3 therefore read different positions of the same sequence.

Both the Relics page and Shop page discuss relics, but ask different questions:

> **Relics:** complete initial queues, without subtracting later pickups.
> **Shop:** which shop-exclusive relics the first few shops display.

## Why Is This Queue Easy to Predict?

The sequence is established very early from the seed. RT2 can recover it without simulating the player's journey to a shop.

Restoring the initial sequence reveals the first, second, third, and later shop relics.

These positions assume the initial queue and known shop eligibility. If another mechanism removes relics or changes eligibility, the sequence must be reconsidered.

A shop relic may appear on a late floor while its identity remains relatively shallow seed information:

> **Appearing late in gameplay does not necessarily mean requiring deep prediction.**

# Colorless Cards Work Differently

Normal shops have two relevant colorless positions:

> **One Uncommon colorless card**
> **One Rare colorless card**

They do not come from a relic queue. Inventory generation uses the player's **shop random state** when the shop is entered:

> Generate Shop 1's colorless cards
> → Advance the random state
> → Generate Shop 2
> → Continue into Shop 3

The second shop's Rare colorless card therefore depends on the first shop already having been generated. It does not have an independent “Shop 2 seed.”

## The Two Colorless Slots Are Not Independent Either

Within one shop, the Uncommon colorless card is generated before the Rare one, using the same random sequence.

Intermediate steps, including prices that seem unrelated to card identity, advance it too. The two card identities are not unrelated draws.

RT2 may show only identities, but replay must still follow the game's actual order.

# A Specific Shop or Any of the First Few?

### A Specified Position

> **Shop 2 must contain a particular relic.**

The target is tied to that ordinal. Finding it in Shop 1 does not satisfy the condition.

### Anywhere in the First N Shops

> **One of the first three shops must contain it.**

The exact ordinal does not matter. With several targets, the corresponding results must all be found within the range.

This suits “I just need to buy it during my first few visits,” while a fixed position suits a planned timing requirement.

# How Does RT2 Treat Shops Today?

The Shop page uses one shared first-N-shops range for shop-exclusive relics, Uncommon colorless cards, and Rare colorless cards.

Each category independently chooses **specified shop positions** or **anywhere within the first N shops**. The interface currently supports up to **5 normal shops**.

A query can require a particular Shop relic within the first three shops while fixing a Rare colorless card to Shop 2. RT2 checks each using its actual random mechanism.

Remember the distinction:

> **Shop relics come from an initially ordered relic queue.**
> **Colorless cards come from a persistent shop random state that advances as shops are generated.**

Both appear as shop inventory to the player, but their random sources differ. See [[shop-stability]] for the effects of Fake Merchant and The Courier.
