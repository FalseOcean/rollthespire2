# Capsules: From Neow into the Relic Bags

## What You Want

You may obtain a Capsule directly from Neow or through {{relic:NEOWS_BONES}}. Usually, the real question is:

> **Which relics will the Capsule contain?**

{{relic-icon:SMALL_CAPSULE}} and {{relic-icon:LARGE_CAPSULE}} have different effects, but share one feature:

**The Capsule belongs to Neow, while its contents come from the game's actual relic bags.**

Seeing a Capsule offered and knowing what is inside it are two layers of the problem.

## Why Is This More Complex?

Checking whether Neow offers a Capsule is simple. Opening it, however, requires generating ordinary relics.

The game first uses reward-related RNG to determine rarity, then retrieves a relic from the current ordered bag of that rarity.

The process extends from:

> Seed → Neow offer

to:

> Seed
> → Neow offer
> → Capsule
> → Determine relic rarity
> → Inspect the current relic bag
> → Obtain the corresponding relic

RT2 needs both Neow's random results and **the contents of this run's relic bags**.

Fortunately, this is still the opening. The bags have just been created, without a long history of consumption, so they can still be replayed directly from the seed.

The Relics page shows the **complete initial bags before pickups such as Capsules consume them**. A Capsule actually removes relics. An initial bag position is not the same as the ordinal of a later combat or chest pickup.

## Taking a Capsule Directly

When taking a Capsule directly from Neow, the state is relatively simple:

> The Capsule is known;
> the relic bags are in their opening state;
> and the Rewards RNG position can be determined directly.

RT2 can continue calculating its contents. This is a typical case where:

> **Neow identity is cheap to check, but its internal result leads into another random system.**

## A Capsule Inside Bones

Suppose {{relic:NEOWS_BONES}} gives **{{relic:KALEIDOSCOPE}} + Capsule**. The two relics are obtained in sequence.

If you take {{relic:KALEIDOSCOPE}} first, it generates cards and consumes Rewards RNG. The Capsule then uses the advanced Rewards state to determine relic rarity.

In the opposite order, the Capsule determines and obtains its relics before {{relic:KALEIDOSCOPE}} generates cards.

Therefore **{{relic:KALEIDOSCOPE}} → Capsule** and **Capsule → {{relic:KALEIDOSCOPE}}** may produce entirely different results.

This is a concrete reason pickup order matters for {{relic:NEOWS_BONES}}. Not every {{relic:NEOWS_BONES}} combination is order-sensitive. The key question is:

> **Does the earlier relic consume or alter random state needed by the later relic?**

## Capsule Contents May Have Effects of Their Own

The complexity need not stop when the relic identity is known.

An ordinary relic obtained from a Capsule may have an immediate effect, just like the same relic obtained elsewhere. Some immediately upgrade cards or change another state.

The full chain may continue:

> Capsule
> → Retrieve a relic from its bag
> → Obtain the relic
> → Execute its immediate effect

This is why Capsule contents are a deeper question than an ordinary Neow result.

## What RT2 Can Do Today

RT2 can filter for specified relics inside Capsules, including Capsules from {{relic:NEOWS_BONES}}, using the actual opening history.

If another {{relic:NEOWS_BONES}} relic affects the required RNG or state, RT2 considers pickup order rather than treating the two relics as an unordered set.

Current multiplayer filtering has an explicit assumption: the immediate on-obtain effects of Capsule relics not listed in the conditions are ignored in prediction. Modeled effects of actually held relics still affect later rewards. The game itself still executes real immediate effects, so this assumption does not mean all Capsule effects have been fully replayed.

Capsules illustrate an important boundary:

> **A question can begin at Neow, continue into relic bags, and then enter the effects of the relics obtained.**

The condition may only say, “I want this Capsule to contain this relic.” Answering it requires following the actual generation chain until the result is reached.
