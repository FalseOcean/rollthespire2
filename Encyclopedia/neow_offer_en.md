## How Is Neow's Offer Generated?

At the beginning of a run, Neow does not simply pick three random relics from all opening relics.

In the current version, a normal offer can roughly be understood as:

> **2 positive options + 1 curse option**

The curse option follows different candidate rules from the other two. This is why a Neow relic's appearance probability is not simply:

> 3 ÷ the number of all Neow relics.

They do not all belong to the same pool.

## What Is the Curse Pool?

The term does not mean a collection of curse cards. It means:

> **The relics that can appear as Neow's curse-exchange option.**

This group currently contains ten relics:

- {{relic:CURSED_PEARL}}
- {{relic:DOWSING_ROD}}
- {{relic:HEFTY_TABLET}}
- {{relic:LARGE_CAPSULE}}
- {{relic:LEAFY_POULTICE}}
- {{relic-icon:NEOWS_BONES}}
- {{relic:NEOWS_SACRIFICE}}
- {{relic:PRECARIOUS_SHEARS}}
- {{relic:SILKEN_TRESS}}
- {{relic:SILVER_CRUCIBLE}}

Neow selects one from the currently legal curse pool for this offer's curse branch.

In the default single-player, fully unlocked environment, all ten are available. A specified member's probability of occupying the curse slot is therefore:

> **1 / 10**

{{relic:NEOWS_BONES}} is one of them. Checking whether **Neow's curse slot is {{relic:NEOWS_BONES}}** is a very simple, inexpensive random check.

## Why Are Positive Options Different?

The two positive options are not drawn uniformly from a single fixed pool. Generation is also affected by the curse option already drawn.

Some relics are mutually exclusive. If the curse branch contains a particular relic, its corresponding positive relic may be excluded from the positive pool.

Several relic pairs also undergo an initial either-or choice:

> {{relic:LAVA_ROCK}} / {{relic:SMALL_CAPSULE}}
> {{relic:NUTRITIOUS_OYSTER}} / {{relic:STONE_HUMIDIFIER}}
> {{relic:NEOWS_TALISMAN}} / {{relic:POMANDER}}

For each pair, the game first decides which member enters this run's candidate pool. It then shuffles the resulting positive pool and takes the first two.

A positive relic's probability therefore cannot be calculated from pool size alone. It depends on:

> Which curse option was drawn;
> the outcomes of the paired choices;
> which relics the character, mode, and unlock state allow;
> and which relics end up first after shuffling.

RT2 uses the actual generation rules rather than a fixed probability table.

## An Offer and a Pickup Result Are Different

Neow's offer answers:

> **What is offered in this run?**

Selecting an opening relic in a Neow filter usually also expresses:

> **I actually take it.**

These are different questions. Seeing {{relic:NEOWS_BONES}} in an offer only means you can choose {{relic:NEOWS_BONES}}. Its effects begin only when you actually choose {{relic:NEOWS_BONES}}.

And {{relic:NEOWS_BONES}} has a particularly unusual effect.

# What Makes {{relic:NEOWS_BONES}} Special?

For an ordinary Neow relic, the first result is the relic itself:

> Neow
> → {{relic:NEW_LEAF}}

{{relic:NEOWS_BONES}} adds another layer:

> Neow
> → {{relic:NEOWS_BONES}}
> → Two internal Neow relics

Its result is not a card, a number, or an ordinary drop. It is **the identities of more Neow relics**: a kind of nested Neow choice.

## What Can Bones Draw?

In the default single-player, fully unlocked environment, {{relic:NEOWS_BONES}} cannot draw {{relic:NEOWS_BONES}} again, and multiplayer-only Neow relics are excluded.

There are **28 Neow relics eligible as internal results of {{relic:NEOWS_BONES}}**. It takes two different relics from them.

Given that **you are taking {{relic:NEOWS_BONES}}**, requiring one specified relic inside it has probability:

> **1 / 14**

Either position can contain it.

Requiring two specified, different relics in either order has probability:

> **1 / 378**

This is a specified pair drawn without replacement from 28 candidates.

Including the 1 / 10 chance that Neow's curse slot is {{relic:NEOWS_BONES}}:

> {{relic:NEOWS_BONES}} + one specified internal relic: about **1 / 140**

> {{relic:NEOWS_BONES}} + two specified internal relics: about **1 / 3,780**

Even a simple opening identity condition can eliminate a large share of seeds.

## Why Does Pickup Order Matter?

{{relic:NEOWS_BONES}} does not simply tell the game, “The player now owns A and B.” The relics are actually obtained one after the other.

If neither has an immediate random effect, A → B and B → A may be equivalent. But an obtained relic might:

- Consume an RNG stream;
- generate cards, relics, or potions;
- change an input the other relic will use;
- affect shared state.

Then **A followed by B** and **B followed by A** may produce different results.

RT2's {{relic:NEOWS_BONES}} filter therefore distinguishes an unrestricted pickup order from a specified order. It exposes an order already present in the game's execution of {{relic:NEOWS_BONES}}, rather than adding a gameplay rule.

## Why Is Bones Still Suitable for Seed Searching?

Although {{relic:NEOWS_BONES}} is more complex than an ordinary Neow relic, it still happens at the start of the run.

No map route, first ten floors of choices, or long game history needs to be reconstructed. The process remains local:

> Seed
> → Neow offer
> → {{relic:NEOWS_BONES}}
> → Two internal relics
> → Their immediate effects, if needed

Checking whether {{relic:NEOWS_BONES}} contains a particular pair is still cheap. The complexity usually appears when asking:

> **What do those two relics produce in turn?**

Capsules lead into relic bags, and {{relic:KALEIDOSCOPE}} generates multiple card groups. Transformation relics also have their own random sources: {{relic:LEAFY_POULTICE}} uses Transformations RNG, while {{relic:NEW_LEAF}} uses Niche RNG.

Those details belong in their individual articles.

The opening structure can be remembered as:

> **The Neow offer determines which choices exist.**

> **The curse pool determines one special branch.**

> **{{relic:NEOWS_BONES}} expands one choice into two more Neow relics.**

We enter deeper random processes only when asking what those relics do internally. See [[neow-bones-curse]] for how player choices affect the final curse.
