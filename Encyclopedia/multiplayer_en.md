# Multiplayer: One Seed for the Whole Party

A multiplayer search requires **one seed and one run to satisfy every player's conditions**. Act variants, bosses, the standard map and event queues belong to the common world. Neow offers, rewards, shops and ordinary event outcomes usually also depend on each player's slot, character and state.

## Why slots matter

The player position used in random initialization is P1 / P2 / P3 / P4, rather than an account or character name. Moving the same character to another slot can change personal results. RT2 predicts the configured slots and does not automatically swap players to find more seeds.

Set common conditions once and personal conditions separately. See [[multiplayer-shared|Shared party conditions]] for how common world facts relate to event outcomes.

## Opening pickup order

RT2 processes configured opening actions in **P1 → P2 → P3 → P4** order, completing one player's agreed actions before the next. Players without configured actions are skipped.

When pickups use shared relic bags, earlier players remove candidates available to later players. When a process uses persistent randomness, later results must continue from its actual advanced state. Follow the player order and internal Bones pickup order shown by the prediction.

This is not a universal rule for every multiplayer event. For example, the tested Morphic Grove seed does not require a particular pickup order between players; see the shared conditions article.

## Current boundaries

RT2 can combine personal conditions with common world conditions. However, a player's transform result within one event differs from the cross-source Transform Combination editor: **multiplayer transform combinations are not currently offered as a formal capability.** One event's shared result does not establish a complete chain of transforms across the run.

Map prediction describes the common standard map. Ordinary question-mark route categories have explicit premises including single-player; see [[event-appearance|Event appearance]]. This does not mean RT2 simulates every later party action, deck change and route history.
