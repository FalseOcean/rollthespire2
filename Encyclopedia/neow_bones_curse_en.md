# {{relic:NEOWS_BONES}}' Final Curse: Why Player Choices Matter

## What You Want

After both internal relics have been obtained and resolved, {{relic-icon:NEOWS_BONES}} generates a final curse.

You can ask:

> **I want {{relic:NEOWS_BONES}} to give a specified final curse.**

For most opening combinations this is straightforward. Special cases arise when an earlier relic changes **Niche RNG**, particularly **{{relic:WHETSTONE}} / {{relic:WAR_PAINT}} (W/WP)** from a Capsule.

## Why Does W/WP Change the Final Curse?

{{relic:NEOWS_BONES}} generates the final curse with Niche RNG after resolving both internal relics:

> Obtain the first relic
> → Execute its effect
> → Obtain the second relic
> → Execute its effect
> → Generate the final curse using Niche RNG

{{relic:WHETSTONE}} and {{relic:WAR_PAINT}} immediately select upgradable Attacks or Skills from the current deck. That selection also uses Niche RNG.

If a Capsule produces W/WP:

> W/WP consumes Niche RNG
> → The stream advances
> → {{relic:NEOWS_BONES}} generates its curse from the new position

The curse therefore depends on the seed and **which upgradable cards are present when W/WP is obtained**.

## Earlier Card Picks Become Part of the Question

Suppose the {{relic:NEOWS_BONES}} pickup order is:

> {{relic:KALEIDOSCOPE}} → Capsule

The Capsule contains {{relic:WHETSTONE}}. Taking an upgradable Attack from {{relic:KALEIDOSCOPE}} changes the candidate set seen by {{relic:WHETSTONE}}.

That can change:

> {{relic:WHETSTONE}}'s random consumption
> → Niche RNG's position
> → {{relic:NEOWS_BONES}}' final curse

Knowing what {{relic:KALEIDOSCOPE}} generated is no longer enough. We also need to know **what the player actually took**.

## RT2's “Selection Is Commitment” Rule

For optional card rewards such as {{relic:KALEIDOSCOPE}} and {{relic:LOST_COFFER}}, RT2 uses a clear rule:

> **A card specified in the filter is a card you commit to taking.**

Unspecified optional rewards are skipped. They do not mean “try every possible choice and find something that makes the later result succeed.”

For example, if {{relic:KALEIDOSCOPE}} has two groups but you specify one card, the query means:

> **Take that card and skip the other group.**

It does not mean taking that card and then choosing whichever card from the other group best helps a later result.

This distinction may be unobtrusive in an ordinary query, but can directly change the final curse when W/WP follows.

## Why Not Try Every Player Choice Automatically?

That would change the question from:

> **Does this seed produce the target when I follow the actions I specified?**

to:

> **Is there any sequence of player actions that could produce the target?**

The latter requires branching over choices. Taking or skipping either {{relic:KALEIDOSCOPE}} group produces different decks; each deck can then produce different W/WP targets and RNG continuations.

A single opening history quickly becomes a tree of possible player actions. RT2 does not silently search that tree. It replays the choices you actually specified.

## How Does RT2 Handle W/WP?

For currently supported single-player opening combinations, Production Exact replays the agreed pickup order:

> What the player takes
> → How the deck changes
> → Which upgrade targets are legal for W/WP
> → How automatic upgrading executes
> → Where Niche RNG ends
> → Which curse {{relic:NEOWS_BONES}} generates

**Exact's result corresponds only to the opening history you described.** If a key choice is missing, RT2 does not pretend to know what you will choose.

Multiplayer filtering accounts for the RNG consumption needed by later results when W/WP is explicitly required, but does not currently offer filtering for specific upgrade targets. Predicting the final curse does not mean predicting the entire resulting deck.

## Fast Can Use a Simpler Calculation

Across many seeds, it is often unnecessary to know exactly which two cards {{relic:WHETSTONE}} upgrades. For a final-curse target, the important quantity is often:

> **How far does W/WP advance Niche RNG?**

When prior choices are sufficiently specified, RT2 can calculate the number of legal upgrade candidates and reduce the full upgrade process to its RNG advancement before checking the curse.

This is much cheaper than storing and replaying a full deck for every seed.

If W/WP is required but the prior conditions or Capsule source do not determine that advancement, Fast retains the relevant candidates for Exact.

This does not cover hidden W/WP relics that were not required in the conditions. Fast may still ignore their effects and miss seeds that would actually qualify. Exact checks the candidates that reach it; it cannot recover seeds already missed. **Returned results satisfying the query's premises** and **finding every qualifying seed** are different guarantees.

## Why Are Complete Conditions Often More Valuable?

More conditions do not just make the desired seed rarer. They can tell RT2 more about **how you intend to play the opening**.

For example, specifying:

> Which {{relic:KALEIDOSCOPE}} card to take;
> the pickup order for {{relic:NEOWS_BONES}};
> and a Capsule containing {{relic:WHETSTONE}};

makes the history more definite. RT2 can more readily determine the deck, W/WP's legal candidate count, Niche advancement, and the starting position for the curse.

For interdependent queries, **more complete conditions can improve early filtering and make the replayed history more explicit**.

This does not make Exact “more accurate.” Exact still checks only the conditions actually described. You have made the question more complete, allowing a more complete replay.
