# Neow: How Does the Opening Change Later Combat Rewards?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

When filtering for a card in the first combat, a potion in the first three, or a combination of early rewards, those outcomes do not necessarily start from an identical opening state.

Neow happens before the first combat. Different choices can leave later rewards at entirely different starting positions.

If both matter, the question is:

> **After completing this opening, what rewards follow?**

Neow and combat rewards are not unrelated results.

# Why Can Neow Change Combat Rewards?

Combat rewards use the player's persistent **Rewards** state. Some Neow relics generate cards, potions, or relic rarities when obtained, using that same stream.

The first combat therefore need not use Rewards' initial position. It uses **the position left after Neow**.

## Some Relics Advance Rewards Directly

For example:

- {{relic-icon:ARCANE_SCROLL}} generates cards;
- {{relic-icon:HEFTY_TABLET}} generates a Rare card group;
- {{relic-icon:KALEIDOSCOPE}} generates other characters' cards;
- {{relic-icon:LOST_COFFER}} generates cards and potions;
- {{relic-icon:LEAD_PAPERWEIGHT}}, {{relic-icon:MASSIVE_SCROLL}}, and {{relic-icon:SCROLL_BOXES}} also generate opening cards;
- {{relic-icon:SMALL_CAPSULE}} / {{relic-icon:LARGE_CAPSULE}} use Rewards to determine the rarities of their relics.

Rewards has already advanced before the first combat. On the same seed, choosing {{relic:KALEIDOSCOPE}} can therefore produce a different first reward from an opening that does not consume Rewards.

# Not All Opening Randomness Uses Rewards

Randomly producing something does not automatically mean changing combat rewards. STS2 separates random uses into different states:

- {{relic-icon:LEAFY_POULTICE}} primarily uses Transformations for its transformations;
- {{relic-icon:NEW_LEAF}} uses Niche in this version;
- {{relic-icon:PHIAL_HOLSTER}} uses Combat Potion Generation for its potions.

Those processes do not advance Rewards merely by being random. The key question is **whether they use Rewards**, not how much opening randomness occurs.

# Another Effect: Changing Reward Rules

Neow's influence is not limited to random-call counts. Some relics change reward generation itself.

**{{relic-icon:SILKEN_TRESS}}** need not generate a card group first, but affects the next card reward group.

Two histories ending at the same Rewards position can still produce different rewards depending on whether {{relic:SILKEN_TRESS}} is present.

Likewise, a relic obtained from a Capsule may add card rewards, force potion drops, or alter reward contents.

The full post-opening state includes at least:

> **Where Rewards is now**
> **Which held effects will continue changing reward rules**

# When the Player Specifies Neow

Requiring {{relic:KALEIDOSCOPE}} and a specified card in the first combat describes one history:

> Obtain {{relic:KALEIDOSCOPE}}
> → Generate its opening cards
> → Advance Rewards
> → Generate the first combat reward from that state

RT2 cannot use one opening to satisfy the Kaleidoscope condition and another, non-consuming opening to satisfy the combat condition.

For **{{relic-icon:NEOWS_BONES}}**, the specified internal pickup history also carries forward.

> **Once prior conditions are specified, later rewards must continue from that same history.**

# What If Neow Is Not Specified?

Suppose the query only requires a card in the first combat and says nothing about Neow.

RT2 does not interpret this as “try every Neow relic and every internal choice until something produces my target.” That would silently add a large search over player choices.

Instead:

> **With no Neow specified, combat rewards assume no extra opening disruption to Rewards.**

The player has not requested a special Neow effect to alter later rewards.

# This Does Not Pretend Neow Is Absent

An unspecified Neow does not mean nothing happens at the opening. Neow still exists and offers real choices.

RT2 ultimately requires **an actual opening available on this seed that is consistent with the no-extra-Rewards-disruption premise**.

An opening that clearly consumes Rewards cannot be treated as neutral. Nor can one whose impact cannot be reliably determined.

Unspecified Neow means no requested special opening to help later rewards; it does not mean ignoring the real Neow offer.

# How Is It Done?

RT2 passes the post-Neow reward state into combat prediction:

> Seed
> → Neow choices and opening effects
> → Post-opening Rewards state
> → First combat reward
> → Second
> → Third…

A specified Neow follows the specified history. An unspecified Neow uses the ordinary no-extra-disruption premise and requires a compatible opening to exist on that seed.

Although they occupy different UI areas, Neow and combat conditions describe the same run.

> **The first combat reward starts from the state Neow leaves behind.**

Different opening relics can advance Rewards or change reward rules. If Neow is unspecified, RT2 does not automatically select a favorable special opening; it continues under the ordinary no-extra-Rewards-disruption premise.
