# Combat Rewards: What Does “Consecutive” Mean?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

RT2 can filter rewards from the next few normal combats:

> A particular card in the first combat;
> a card somewhere in the first three;
> a specified potion in the second.

The current range is **1–6 combats**. These are **not six independent random draws**. They share an advancing reward state: the second continues after the first reward is generated, and the third continues after the second.

# “Consecutive” Does Not Mean Adjacent Monsters on the Map

Consider:

> Combat → Rest Site → Combat

If the Rest Site changes none of the state required by combat rewards, the two rewards can still be calculated consecutively.

For C, consecutive combat rewards mean:

> **Nothing between normal combat rewards changes their random history, reward rules, or available reward pools.**

Other rooms between combats are not inherently the issue. What matters is whether their effects change **Rewards, reward rules, or available reward pools**.

# Why Does This Matter?

Post-combat cards, potions, and some other rewards use the player's persistent `Rewards` state. It does not restart from the seed after every combat.

> Opening
> → First reward consumes part of the sequence
> → Second reward continues
> → Third reward continues…

If something else uses Rewards between combats:

> Combat 1 → **Extra Rewards consumption** → Combat 2

the second reward starts at a different random position, and later results can change.

# Events Can Enter This Sequence Too

Events usually have their own randomness, so **entering a question-mark room does not necessarily disrupt combat rewards**.

Many event results are separate from Rewards. But some event options call the normal card, potion, or relic reward systems and consume the player's Rewards state.

## Event Card Rewards

Some **{{event:BRAIN_LEECH}}** options open normal card rewards. **{{event:COLORFUL_PHILOSOPHERS}}** generates rewards after a color is selected. Some **{{event:TRIAL}}** results generate multiple card reward groups.

Without an explicitly independent RNG, these rewards use the player's current Rewards state.

Thus Combat 1 → event card reward → Combat 2 differs from Combat 1 → no Rewards consumption → Combat 2.

## Potions Work the Same Way

An event need not generate an entire card group to matter. Some directly grant a random potion, including paths in:

> {{event:POTION_COURIER}};
> the bottling option in {{event:WELLSPRING}};
> {{event:THE_LEGENDS_WERE_TRUE}}.

These use the player's Rewards to select the potion. Even a single potion advances the stream and can change later combat rewards.

# Different Choices in the Same Event Can Change the Future

It is too broad to say that predictions become wrong after any question-mark room. **What the room actually executes** is what matters.

Within one event, an option might leave Rewards untouched, another generate three cards, and another generate a potion or relic. Those choices leave different continuation states.

Both **which event you encounter** and **what you do there** help determine later results.

# RT2's Current Consecutive-Combat Contract

C does not simulate an entire route of maps, events, choices, shops, chests, combats, and further events.

It assumes:

> **Start from the established opening reward state and generate normal combat rewards consecutively, with no extra Rewards consumption or changes to reward rules or pools between them.**

“Combat 1, 2, 3…” therefore means the first, second, and third **consecutive normal combat rewards**.

You can pass through content that leaves those states unchanged. Conversely, newly obtaining Prayer Wheel changes reward quantity even if its pickup consumes no Rewards RNG.

After extra Rewards consumption, **later C predictions no longer match the original consecutive baseline**.

# A Practical Check

Ask of an intermediate room or action:

> **Does it use Rewards, change reward rules, or change the available reward pool?**

If not, it may leave the sequence unaffected. If so, its exact effect needs to be known.

“I passed through a question mark” is insufficient information. “I chose the event option that generates a card reward” may already explain a later shift.

# Why Start with Consecutive Combats?

Under this premise, the calculation is clear: begin from a definite Rewards state, generate one reward and its continuation, then the next.

Adding all event choices, shop behavior, and chest rewards would turn reward prediction into a full route-history problem. The rewards do not become unknowable; the required inputs increase.

C currently answers:

> **What will these normal combats give me if nothing between them adds Rewards consumption or changes reward rules or pools?**
