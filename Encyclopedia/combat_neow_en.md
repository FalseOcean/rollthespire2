# Neow: How Does the Opening Change Combat Rewards?

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

The first combat starts from **the reward state left after Neow**. Opening effects can advance Rewards RNG or change later reward rules.

## Opening Effects That Use Rewards

- {{relic-icon:ARCANE_SCROLL}} generates cards, {{relic-icon:HEFTY_TABLET}} generates a Rare reward group, and {{relic-icon:KALEIDOSCOPE}} generates other characters' cards.
- {{relic-icon:LOST_COFFER}} generates cards and potions. {{relic-icon:LEAD_PAPERWEIGHT}}, {{relic-icon:MASSIVE_SCROLL}}, and {{relic-icon:SCROLL_BOXES}} also generate opening cards.
- {{relic-icon:SMALL_CAPSULE}} and {{relic-icon:LARGE_CAPSULE}} use Rewards to roll the rarities of their relics.

These effects advance Rewards before combat one. On the same seed, choosing {{relic:KALEIDOSCOPE}} can yield a different first reward from an opening that does not consume Rewards.

Randomness alone does not imply using Rewards. {{relic-icon:LEAFY_POULTICE}} primarily transforms with Transformations, {{relic-icon:NEW_LEAF}} uses Niche, and {{relic-icon:PHIAL_HOLSTER}} generates potions with Combat Potion Generation. Those random processes do not themselves advance Rewards.

## Rules Can Change Without Consuming Rewards

{{relic-icon:SILKEN_TRESS}} affects the next card reward group. Having {{relic:SILKEN_TRESS}} can therefore change a reward even at the same Rewards position. Reward relics actually obtained through a Capsule may also add card groups, force potion drops, or change contents; see [[combat-rarity-relics]].

Continuation requires both random state and held reward effects, rather than just a call count.

## Which Opening Does the Query Use?

When a query requires {{relic:KALEIDOSCOPE}} and a target in combat one, RT2 generates that opening's cards before continuing from its resulting Rewards state. A specified internal pickup order for {{relic-icon:NEOWS_BONES}} also carries forward. The opening and combat conditions must match the same history.

With Neow unspecified, RT2 uses the ordinary premise of no extra opening reward disturbance and requires an actual offered opening that can be confirmed compatible. It does not automatically search every special opening to produce the target, or treat known-consuming or uncertain openings as neutral.

See [[combat]] for the consecutive-combat premise and [[neow]] for individual opening results.
