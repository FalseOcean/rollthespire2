# Multiplayer: What Changes from Single-Player?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

Multiplayer prediction is more than repeating single-player calculations. **Each player has personal random results, but everyone belongs to the same run.**

Neow, combat rewards, and shops are often tied to the player's **slot**, so P1 and P2 can see different results on the same seed.

RT2 records each player's slot, character, and conditions, requiring **one seed to satisfy all players simultaneously**.

Other content is shared by the party: Act variants, Bosses, maps, and other world information. These exist once per run, not four times for four players.

Some mechanisms also use shared random state. **An earlier player's actions can change the state a later player uses.** Independent calculations cannot always simply be combined afterward.

# How Is Player Order Handled?

RT2 uses an explicit multiplayer opening order:

> **P1 → P2 → P3 → P4**

Each player's specified opening actions finish before the next player's begin. Shared-state changes therefore carry into later players' calculations.

Players without specified opening actions are skipped.

The search seeks **one multiplayer opening history satisfying the whole party**, rather than several independent good single-player results.

# Why Do Slots Matter?

Multiplayer randomness is tied to **P1 / P2 / P3 / P4**, not the character's name or the player's account.

The same character in P1 can receive different results from that character in P2. RT2 uses actual player slots and does not automatically swap seats to find more qualifying seeds.

# Current Boundaries

Many single-player filters can already be applied to multiplayer, combining personal conditions with shared world conditions.

This does not mean every single-player feature has automatically become a multiplayer feature. **Combined transformation filtering is not currently offered for multiplayer.**

Map prediction currently concerns the shared standard map itself. It does not simulate the party's later route, room visits, and every action throughout the run.

The basic model is:

> **Calculate personal randomness per player; keep one copy of shared facts; follow the specified player order when shared state links their actions.**

Every condition must hold **on the same seed, in the same run**.
