# Seeds: what are we actually searching?

> This article primarily discusses **Slay the Spire 2 Beta 0.111.0**.

When we talk about “a seed,” what we usually see is a string like:

> `001W48N6QUWB`

It is easy to get the impression that:

> A different string means a different run;
> Keep trying different strings, and you can always get more results.

But once you actually start building a seed finder, things turn out to be a little stranger.

## How large is a seed?

STS2 currently generates seeds with **12 characters** by default. RT2 uses the same standard format for its current search.

Each position has **34 canonical characters** to choose from. To avoid confusing characters such as:

> `O` and `0`, or `I` and `1`,

the game uppercases input, replaces `O` with `0` and `I` with `1`, and trims whitespace at either end.

However, **the default generation format is not the entire set of accepted inputs**. The game's custom seed entry also accepts text of different lengths; it does not require exactly these 12 characters.

If we consider only the standard format used by default generation and RT2's current search, the text space contains:

> **34¹² ≈ 2.386 × 10¹⁸**

or roughly:

> **2.386 quintillion strings.**

That is already enormous. This counts format combinations; automatic generation also filters inappropriate words.

But the game does not take those 12 characters and directly use them to produce every random result.

There is another step.

## Behind a Seed, there is a Hash

The game converts normalized seed text into an internal number through a Hash.

For ordinary seeds in 0.111.0, this number is **64 bits** wide, using xxHash64.

The internal root value's numeric space therefore has:

> **2⁶⁴ ≈ 1.845 × 10¹⁹**

possible values.

There is an interesting relationship here:

> **The 64-bit root space is actually larger than the text space of standard 12-character seeds.**

`34¹²` is only about **12.94%** of `2⁶⁴`. Even if every standard string mapped to a different root, that would be the most it could cover. Hash collisions would reduce the actual coverage further.

So not every possible 64-bit root has a corresponding standard 12-character seed.

In other words:

> **Inside the game is a “universe of random starting points” larger than the standard seed text space. This standard format can reach only part of it.**

This does not mean that every kind of text accepted by custom seed entry is also restricted to that 12.94%. But the sizes alone establish that many roots have no corresponding standard 12-character seed.

RT2 currently searches **standard seed text that players can actually enter and share**, rather than the abstract set of all 64-bit numbers. A search ordinal first becomes text, which is then hashed. It does not directly specify the game's internal root value.

Different strings can still produce the same Hash. With the same version, configuration, and relevant actions, they can therefore start at the same random starting point and reproduce the same relevant results. See [[why-predictable]] for more about random state and actions.

## What does a filter actually do?

I like to picture the entire seed text space as a sky full of stars.

Every Seed you can enter is a star. Some stars have different names but may lead to the same starting point.

Most of the time, we are not asking:

> “What is everything on this planet?”

We just have a request:

> “Find me stars in this sky that have a particular feature.”

That is a **filter**.

For example:

> You must acquire a particular Neow relic at the opening;
> {{relic:NEOWS_BONES}} must contain two particular relics;
> {{relic:LEAFY_POULTICE}} must transform a card into a particular card;
> Act 1 must have a particular Boss.

A filter keeps taking Seeds, reconstructing the portion of random state that its condition actually needs, and asking:

> **Does it match?**

If it can establish that the answer is no, it immediately moves on. Anything not yet resolved stays for later verification.

This is why some conditions that look very complicated can actually be filtered very quickly.

For example:

> Neow → {{relic:NEOWS_BONES}} → {{relic:LEAFY_POULTICE}} → a particular transformation

That already looks like a long chain to a player.

But it all happens very early, and the random history that needs reconstructing is short. See [[neow-offer]] and [[neow-transform]] for the specific offers and transformation premises.

From the computer's perspective, it barely has to “land” on this star before knowing whether it is the one we want.

## A predictor does something else

A predictor starts with:

> **One Seed that has already been chosen.**

The question is no longer:

> “Does it qualify?”

Instead, we begin asking:

> “What is actually inside this Seed?”

If filtering means finding stars in the sky, prediction is more like:

> **Actually landing on one of them and exploring.**

When you first land, plenty of things are easy to see.

For example:

> What Neow offers;
> Who the Boss is;
> How the initial relic bag is ordered;
> What the map looks like.

These facts are largely determined at the start, so predicting them is relatively shallow.

But suppose you keep asking:

> **What will the rare card reward be after defeating the Act 1 Boss?**

Now things are very different.

You cannot determine that reward just from:

> Seed + Boss

Before reaching the Boss, the player may have generated many combat rewards, continuously advancing the relevant Rewards random state. Ordinary Boss rare card rewards use fixed rare-card odds, so the rarity adjustment accumulated by ordinary rewards should not be applied here. But which particular rare card is drawn still depends on earlier random consumption.

Which route you took, how many fights you had, whether additional rewards were triggered, whether you acquired relics that change rewards…

All of these can affect the cards after the Boss. See [[combat]] for the relevant reward premises.

To answer that question, a predictor cannot simply “jump to the end of Act 1 and take a look.”

It needs to know:

> **How did you get here?**

### The deeper you predict, the expensive part is “history”

How difficult a result is to predict does not depend entirely on:

> How far it is from the opening.

What matters more is:

> **How much earlier state must be established to obtain that result?**

A result that happens very late but uses an independent RNG may be easy to predict.

Conversely, even an Act 1 reward can be difficult to answer directly if it depends on state accumulated along the way.

This is why RT2's current seed information prediction mainly makes:

> **Relatively shallow observations of one Seed, under premises that are already clear.**

It lets us stand on that star and look around, but it does not yet mean:

> “Explore every possible future route on this entire planet.”

Full long-range route prediction is a considerably larger problem.

## Can a filter always find every answer?

When RT2 searches seeds, the goal is naturally:

> **Find every Seed in the search range that satisfies the conditions.**

The principle for formal filtering is therefore to use methods that do not discard valid answers:

> Cheap stages first rule out impossible Seeds;
> The remaining Seeds receive more complete verification.

But I do not want to describe RT2 as:

> “If it found nothing, that is mathematical proof that there is no answer.”

Those are different claims.

A search has a specific range, configuration, and supported premises. Special mechanics or boundaries not yet covered can limit the questions we can answer. Those limits should be visible, rather than silently discarding possible answers.

And as a product goal, I care more about:

> **Helping players quickly find the Seed they actually want.**

Than spending enormous effort proving:

> “I have searched this entire universe, and there is definitely not a single one here.”

In practice:

> **Normal confirmed results have completed final exact verification under the query's premises;**
> **If final verification is skipped, the output is an unverified candidate that still needs confirmation.**

But either way:

> **Finding nothing should not casually be read as proof that the entire seed space has no solution.**

This matters especially as conditions become more extreme. See [[reading-guide]] and [[faq]] for configuration and usage boundaries.

## Why am I no longer adapting RT2 to 0.107?

There are both engineering reasons and reasons involving the seed space itself.

In the versions checked, 0.107.1 and 0.108.0, seed text ultimately becomes a **32-bit root value**.

Its theoretical limit is just:

> **2³² ≈ 4.29 billion**

In 0.109.0, ordinary seeds switched to the 64-bit Hash mechanism, which 0.111.0 continues to use.

The internal root space expanded from:

> 4.29 billion

To something vastly larger.

For ordinary play, 4.29 billion is already a lot.

But for a seed finder like RT2, where players keep stacking conditions, it can soon feel small.

Suppose a condition occurs, on average:

> Once in ten million distinct starting points.

Add more rare requirements, and there may be very few opportunities left. Conditions are not necessarily independent, so we cannot simply multiply their probabilities. Still, in an old space with at most about 4.29 billion roots:

> **There may be no matching result anywhere in the space.**

That is not hard to imagine. Longer seed text cannot break this limit. Newer versions give us more opportunities to explore, but they do not guarantee a solution for every combination either.

Meanwhile, the features RT2 now needs to maintain include:

> GPU search;
> More complex Neow and relic prediction;
> Multiplayer;
> Maps;
> Compatibility handling for modded environments;
> An increasing number of conditions combined across different mechanics.

Bringing all of this back to 0.107 with full compatibility is difficult. It is not just a matter of changing a Hash algorithm.

The older game's interfaces, pools, random consumption, and mechanics would all need checking, adaptation, and continuing maintenance.

Taking on that compatibility and maintenance cost for a much smaller seed space, where extreme combinations may have no solution at all:

> **I no longer think the return is high enough.**

So RT2 now puts its main effort into later versions.

This does not mean that 0.107 seeds have no value.

It is simply that, for a tool whose goal increasingly focuses on:

> **“Help players find extremely specific Seeds with very unusual combinations,”**

the larger seed space in newer versions is itself an important part of the picture.
