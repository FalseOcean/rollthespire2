# Neow: How Does the Opening Change Combat Rewards?

Combat rewards do not begin from a separate “first-combat seed” independent of your opening. They continue along Rewards RNG, and Neow is a likely consumer before combat one.

For example, {{relic-icon:KALEIDOSCOPE}} generates other characters' cards, {{relic-icon:HEFTY_TABLET}} generates a Rare card reward, and {{relic-icon:SMALL_CAPSULE}} and {{relic-icon:LARGE_CAPSULE}} roll relic rarities. These can advance Rewards before combat starts. Choosing another opening on the same seed can change the position read by combat one.

I want the combat filter to continue from your opening requirements. When you already require {{relic-icon:PRAYER_WHEEL}}, the useful question is: “With Prayer Wheel, which normal combat can give me the card I need?” It adds card rewards, so the answer cannot use the rules for an opening without it. Effects such as {{relic-icon:SILKEN_TRESS}} also show why counting RNG draws is not enough: we need the reward rules left by the opening.

## Continue From the Opening You Specify

Your specified Neow relics and results become inputs to later combat rewards. Relevant generation draws, modeled reward effects, and permitted Bones pickup orders must continue from the same opening. Opening and combat requirements cannot each use a different explanation and then be combined into a confirmed result.

“Do not choose unspecified extra results for you” refers to optional actions. If you require one Kaleidoscope card, RT2 proceeds by taking it and skipping the unspecified other group, rather than choosing an extra card for you. **Necessary consequences of a declared pickup are a different matter.** Automatic effects and reward changes caused by obtaining a relic do not cease to exist because you did not add another condition. See [[neow-capsule]] for the current immediate-effect coverage. Relevant effects the model cannot establish must remain uncertain, rather than weakening your original request. See [[neow-bones-curse]] for a concrete choice example.

## What If You Do Not Specify Neow?

One approach would examine every opening and choice to find seeds that satisfy the combat requirement regardless of what you take. Another would search special openings for a favorable route on your behalf. Neither is the current combat filter's default task: the calculation is expensive, and you may not intend to make those choices.

RT2 first computes an opening with **no preceding Rewards consumption and no changes to combat reward rules**. Final confirmation must then establish that an actual offered Neow choice meets this premise. Merely not consuming Rewards is insufficient: a relic such as Prayer Wheel also changes reward rules and cannot count as a neutral opening.

This can miss seeds that need a special opening to satisfy the target. I would rather have the player supply that opening intent. If final checking cannot establish a compatible neutral choice, the result cannot be confirmed.

See [[neow]] for individual opening results, [[combat]] for the premise between combats, and [[combat-rarity-relics]] for reward relics.
