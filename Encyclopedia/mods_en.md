# Mods: How Much Can RT2 Support?

> Prediction currently relies primarily on the base-game mechanics of **Slay the Spire 2 Beta 0.111.0**.

RT2's default environment is **the base game, single-player, with everything unlocked**.

Installing a mod does not automatically make it unusable. The important distinction is **whether the mod changes content or changes rules**.

# Added Content Is Often Easier to Accommodate

A mod may add cards, relics, or a character's ordinary card and relic pools while retaining the base game's rarity, pool, draw, and RNG rules.

For many predictions, RT2 need not understand every relic's effect. It needs to know **that the object exists in the actual runtime candidate pool**.

RT2 therefore tries to read current card pools, relic pools, characters, and other runtime content rather than rejecting an entire search merely because it encounters a non-base-game object.

# Changed Mechanics Are Harder

A mod may alter randomness itself by:

> Consuming additional RNG;
> changing card or relic generation;
> changing Neow pickup order or flow;
> modifying event eligibility;
> replacing map generation;
> adding special on-obtain side effects;
> using custom state to determine later results.

This is more than an extra candidate in a pool. **The rules underlying prediction have changed.** Correct prediction then requires understanding that mod's implementation.

# Why Not Assume Base-Game Behavior?

Seed prediction depends on **how many times random state advances**, not just the visible result.

Even a simple mod relic can change every later dependent result by making one extra relevant RNG call on pickup.

Likewise, a custom character that reimplements opening, reward, relic, pool, or random logic cannot safely inherit base-game assumptions merely because it looks similar.

# RT2's Approach

RT2 aims to **accommodate mods where possible without inventing compatibility**.

When runtime supplies the real pool and generation still follows understood base-game mechanisms, added objects can participate as ordinary candidates.

If correctness requires understanding third-party relic effects, character mechanics, opening flow, RNG consumption, or hidden state, **that part is outside the currently guaranteed prediction scope**.

RT2 does not silently treat unknown behavior as base-game behavior or substitute a different result just to keep running.

The distinction is:

> **Added content can often be accommodated; changed random rules need specific understanding.**

The goal is not to claim support for every mod. It is **to keep predicting where the real mechanics are established, while retaining clear boundaries where third-party behavior needs to be understood**.
