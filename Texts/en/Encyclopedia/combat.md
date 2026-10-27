# Combat Rewards: What Does “Consecutive” Mean?

I want players to filter for “this card in combat one,” “the target within the first three combats,” or “this potion in combat two.” RT2 currently queries card and potion rewards from the first **1–6 normal combats**. A stable answer to “which combat?” requires explaining what happens between them.

Consecutive chiefly means **continuity of Rewards RNG**: continue from the computed Neow result and generate normal-combat rewards in order, with no extra Rewards consumption or unaccounted changes to reward rules along the way. Reward relics from the explicitly specified opening participate in the calculation. Consecutive rewards with {{relic-icon:PRAYER_WHEEL}} and without it are different premises, but both can be calculated.

Combat rooms need not be adjacent on the map. Combat → Rest Site → Combat can fit the premise if the rest action leaves relevant reward state unchanged. Conversely, an extra reward-generating action between two normal combats can shift the position read by the later one. See [[combat-neow]] for the opening.

## What Can Interrupt This Premise?

Shop character-card rarity, upgrade checks, and ordinary relic rarity use Rewards. Elite combats also generate rewards. A question mark depends on the actual room and option: many events use their own RNG, but an option that generates ordinary card or potion rewards can consume Rewards. Passing a question mark does not necessarily change the sequence, and independent event RNG does not make every option harmless to it.

Some changes happen inside combat and are invisible on the map:

- {{card-icon:THE_HUNT}} adds a three-card reward group when its kill effect triggers. Generating that group consumes randomness and updates rarity state, affecting subsequent rewards.
- {{card-icon:ROYALTIES}} adds its post-combat gold as an extra gold reward. Even though the amount is known, populating this original reward still calls Rewards; a fixed amount does not imply no RNG consumption.
- Killing a thief to recover stolen gold also adds an extra gold reward. This can likewise advance Rewards and affect the next combat's starting position.

Having The Hunt does not mean every combat supplies another reward group. You must draw it, play it, and perform the qualifying kill. Asking players to commit to triggering it every combat, or having the filter calculate combat in detail, is much harder than accounting for a consistently held reward relic. The seed and deck list alone do not establish the combat's course.

Reward relics obtained later, unspecified reward effects inside Capsules, and taking extra Neow cards or declining an agreed result can also change the premise. A player changing an optional action is different from a model failing to cover a necessary consequence of a declared action. The latter is an unresolved implementation issue, rather than the player accepting a weaker answer.

## Why Use This Premise?

Stability describes common situations, and there are many counterexamples. The current filter does not promise these rewards regardless of everything that happens along the way. I first offer a normal-combat sequence whose calculation is affordable and whose premise players can understand and follow.

That is often useful already: establish the opening, include declared reward relics, and find targets in successive normal combats. Adding extra card rewards triggered in individual combats requires more combat information. Such requests are reasonable; a gap in the current filter model does not make them irrelevant. See [[combat-rarity-relics]] for reward relics, rarity, and potion odds.
