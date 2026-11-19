# Mods: Compatibility Depends on the Specific Changes

With a mod, I start by looking at which part of the game it changes. Suppose it adds a few cards to an existing pool and keeps the vanilla draw rules. When RT2 reads that pool, it can include the added candidates. Results from that pool may differ from vanilla while other pools can still work as usual.

Other changes affect the rules themselves. A mod that adds random draws to a shop may change where the next shop starts generating its stock. Reorder a transformation pool and the same random number may pick a different card. To understand either difference, look at the stream and pool the result actually uses.

Finding a modded relic in the picker means RT2 knows its identity. Predicting what follows when you obtain it also requires a rule for its effect. Some filters compute a specific list of outcomes, so start with the result you want to look up.

If a prediction differs from the run, the game version, enabled mods, seed, and actions you took are much more useful than the seed alone. See the [[faq|FAQ]] for how to report it.
