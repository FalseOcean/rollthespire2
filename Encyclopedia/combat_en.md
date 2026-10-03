# Combat Rewards: What Does “Consecutive” Mean?

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

RT2 filters cards and potions from **1–6 normal combats**: a card in the first reward, a target among the first three, or a specified potion in combat two, for example.

## Query Premise

Rewards are generated in order from an established opening state. Each combat continues the previous combat's `Rewards` RNG, rarity adjustment, and potion odds. The query assumes no extra Rewards consumption or changes to reward rules or available pools between combats. It does not replay every shop, event, and choice along a route.

“Consecutive” describes the reward state, so combat rooms need not be adjacent. Combat → Rest Site → Combat fits the premise if the rest action leaves those states unchanged. Conversely, obtaining {{relic:PRAYER_WHEEL}} adds later card rewards even if its pickup consumes no Rewards RNG.

## Which Event Actions Change the Sequence?

Entering a question mark does not inherently change combat rewards. Events usually have their own RNG, but an option that calls the ordinary reward system can consume the player's Rewards:

- Some {{event:BRAIN_LEECH}} options, the rewards after selecting a color in {{event:COLORFUL_PHILOSOPHERS}}, and some {{event:TRIAL}} results generate card rewards. Without an independent RNG override, they use current Rewards.
- Some {{event:POTION_COURIER}} results, the bottling option in {{event:WELLSPRING}}, and some {{event:THE_LEGENDS_WERE_TRUE}} results use Rewards to draw potions.

Combat 1 → event card reward → Combat 2 can therefore differ from Combat 1 → an action that leaves reward state unchanged → Combat 2. The executed option matters, not just the event name. Even one extra potion advances Rewards; the affected outcomes are later consumers of that state, rather than every random system.

See [[combat-neow]] for the opening state and [[combat-rarity-relics]] for rarity, potion odds, and reward-changing relics.
