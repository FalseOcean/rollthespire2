# Event Results: Which Outcomes Are Worth Predicting?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Beyond encountering an event, you may require **a specified result from it**. Outcomes vary greatly in how much prior state they need. RT2 roughly distinguishes three useful categories.

# Stable Results

These require only **the seed, event identity, and a little fixed information**.

For example, **Junk Heap** selects results from fixed candidates after the event occurs. Earlier normal combats, shops, and route state generally do not change that draw.

Such results are particularly suitable for direct filtering.

# Results That Are Stable Under a Simple Premise

These can be predicted accurately once the player supplies a straightforward premise. **Transformation events** are a typical example.

If the player commits to transforming a specified starting card at the event, preserving that target is usually easy and the premise is simple. RT2 can then determine what it becomes.

The result is not inherently unstable: **it is stable as long as the player maintains the premise**.

# Results Useful Only Under Exceptional Conditions

Some event results have theoretical predictive value but are worth filtering only with unusual prior states or play constraints. **Endless Conveyor** is an example.

Its results are not completely beyond analysis, but making the prediction useful requires more specialized conditions. This category is not currently offered as a formal filtering capability.

# Other Results

Many remaining outcomes strongly depend on the deck, relics, potions, earlier choices, persistent random states, or a longer route history.

They are still governed by the seed, but reliable prediction may require reconstructing too much prior gameplay. RT2 does not currently build a full run simulation simply to cover every event outcome.

In practical terms:

> **Stable: predict directly.**
> **Stable under simple conditions: predict after the player supplies those premises.**
> **Useful only under extreme conditions: not currently offered.**

Other highly history-dependent outcomes are outside current event filtering. In every category:

> **Predicting an event's result does not prove the event will occur.**
