# Tezcatara: Three Separate Relic Positions

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Tezcatara offers three Ancient relics. Each position selects one from its own candidate pool, making position-specific targets straightforward. Only the first position needs extra attention.

## First Position

It normally has three candidates:

- {{relic:VERY_HOT_COCOA}}
- {{relic:YUMMY_COOKIE}}
- {{relic:NUTRITIOUS_SOUP}}

{{relic:NUTRITIOUS_SOUP}} requires **a Basic Strike-type card still in the current deck**.

When that condition holds, each candidate has about **1/3** probability. If those starting Strikes have all been removed or transformed, {{relic:NUTRITIOUS_SOUP}} is excluded, leaving:

- {{relic:VERY_HOT_COCOA}}
- {{relic:YUMMY_COOKIE}}

Each then has probability **1/2**. The condition changes both Soup's eligibility and the other two probabilities.

## Second Position

A fixed, equally likely choice among:

- {{relic:BIIIG_HUG}}
- {{relic:STORYBOOK}}
- {{relic:TOASTY_MITTENS}}

There are no additional eligibility requirements.

## Third Position

A fixed, equally likely choice among:

- {{relic:GOLDEN_COMPASS}}
- {{relic:PUMPKIN_CANDLE}}
- {{relic:TOY_BOX}}
- {{relic:SEAL_OF_GOLD}}

There are no additional requirements here either.

## Why Is It Easy to Predict?

Tezcatara has neither complicated weights nor an internal random result like {{relic:SEA_GLASS}}.

Once {{relic:NUTRITIOUS_SOUP}}'s eligibility is specified, the game simply selects once from each of the three pools.

> **Deck state changes the first pool, but not the rules of the other two positions.**

RT2 builds the correct first pool using the player's stated current state, then replays the three results.
