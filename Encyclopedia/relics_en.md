# Relic Queues: Common, Uncommon, Rare, and Shop

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

You may want to know the order of the next Common, Uncommon, Rare, or Shop relics.

RT2 represents the player's initial relic bags as four separate queues:

> **Common**
> **Uncommon**
> **Rare**
> **Shop**

You can require a specified relic among the first N entries or at exactly position N.

# Why Can Relic Order Be Predicted?

At the start of a run, the game groups the player's available relics by rarity and shuffles each group.

For the same seed, character, and unlock state, **all four initial orders are determined**.

Later relic acquisition usually determines the requested rarity and takes the next relic from that bag, rather than independently rerolling identity.

RT2 can therefore recover the initial queues directly.

# Common, Uncommon, and Rare

These three queues are displayed in **front-to-back order**.

If a Rare queue is:

> A → B → C → D

a source that normally takes from its front encounters A first, then B.

This does not mean the first relic obtained in the run must be A. A reward generally has its own earlier rarity determination: Common, Uncommon, or Rare.

The queue predicts **identity order within one rarity's bag**, not a combined chronological sequence of every relic reward.

# The Shop Queue

Shop relics have their own bag, read from the **back**. RT2's first, second, and third Shop entries are already ordered in the direction shops actually consume them.

Not every Shop-rarity relic is necessarily allowed in a shop. Retrieval skips currently ineligible items.

The displayed Shop queue therefore means **the visible order after applying the actual read direction and shop eligibility filtering**.

# Separate Queues, One Opening Generation Process

Each rarity is shuffled separately. “First Common relic” and “first Rare relic” are different queue questions.

However, these shuffles occur within the same opening world-generation process. RT2 replays their actual order, rather than inventing an independent random generator for each queue.

# What Does RT2 Do?

RT2 predicts **the initial order of the player's four relic bags**.

You can ask for a relic among the first three Rare entries or at the second Shop position. These conditions concern **identity positions within the bags**.

They do not prove when the player will obtain a relic of that rarity. Special events, fixed relic rewards, and later run history are not automatically inserted into the queue.

Think of it as:

> **If a source next takes something from this bag, what will it see first?**
