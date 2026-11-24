# Darv: Act Assignment and Relic Offers

Darv is base-game 0.111.0's only shared-pool Ancient, eligible for Act 2 or Act 3. **Assignment to an Act makes him a candidate, not a guaranteed encounter.** These odds use full base-game unlocks; see [[reading-guide]].

## Assignment Precedes Identity Selection

World generation processes Acts 2 and 3 in order: decide whether to assign Darv to Act 2, then, if still unassigned, decide for Act 3. Assignment removes him from the shared pool, preventing assignment to a later Act.

| Outcome | Assignment probability | Actual encounter probability |
| --- | --- | --- |
| Act 2 | 1/2 | About 1/8 |
| Act 3 | 1/4 | About 1/16 |
| Neither Act | 1/4 | — |

For example, assignment to Act 2 adds Darv alongside Orobas, Pael, and Tezcatara, then the game draws the actual Ancient from those four candidates.

## Relic Pools by Act

Seven base candidates participate in both Acts:

- {{relic:ASTROLABE}}
- {{relic:BLACK_STAR}}
- {{relic:CALLING_BELL}}
- {{relic:EMPTY_CAGE}}
- {{relic:PANDORAS_BOX}}
- {{relic:RUNIC_PYRAMID}}
- {{relic:SNECKO_EYE}}

{{relic:PANDORAS_BOX}} is excluded when a game modifier clears the player's deck.

Act 2 adds {{relic:ECTOPLASM}}, {{relic:SOZU}}, {{relic:PHILOSOPHERS_STONE}}, and {{relic:VELVET_CHOKER}}, normally giving **11 regular candidates**. Act 3 excludes Ectoplasm and Sozu while retaining Philosopher's Stone and Velvet Choker, normally giving **9**.

## Shuffling and the {{relic:DUSTY_TOME}} Branch

The game first selects one relic from each candidate group, then shuffles the selected relics. Each base-game group contains only one relic, but selection still advances Darv's event random state; prediction must preserve those calls.

Two equally likely branches follow: **keep the first three**, or **keep the first two and add {{relic:DUSTY_TOME}} in position three**. Dusty Tome is outside the regular shuffle and appears with probability **1/2**. It can coexist with two specified regular relics, but not a third.

With n legal regular candidates, a specified relic appears with probability **(3/n + 2/n) ÷ 2 = 5/(2n)**. Default Act 2 odds are about **22.7%**, compared with **27.8%** in Act 3. Removing candidates such as Pandora's Box requires recalculation.

## What RT2 Checks

RT2 can require Darv in an Act, a particular offered relic, or several relics in the same offer. When Dusty Tome enters the offer, its contents are prepared using the current player's Rewards RNG and legal Ancient card pool. **Filtering its appearance does not imply filtering its internal cards.** See [[why-predictable]] for the shared random principle and [[ancient]] for identity, eligibility, and offers.
