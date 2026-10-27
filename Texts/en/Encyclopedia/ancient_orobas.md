# Orobas: Branches, {{relic:SEA_GLASS}}, and Conditional Options

Orobas has three relic positions: the first pool branches to choose a special candidate, the second is fixed, and the third depends on retained starting relics and cards. These odds use fully unlocked base-game 0.111.0; see [[reading-guide]].

## First Position: Choose a Special Candidate, Then Draw One of Three

{{relic:ELECTRIC_SHRYMP}} and {{relic:GLASS_EYE}} always participate. The third candidate is {{relic:PRISMATIC_GEM}} with probability **1/3**, or {{relic:SEA_GLASS}} with probability **2/3**. The offer then selects uniformly among the three candidates.

| Relic | Final offer probability |
| --- | --- |
| {{relic:ELECTRIC_SHRYMP}} | 1/3, about 33.33% |
| {{relic:GLASS_EYE}} | 1/3, about 33.33% |
| {{relic:SEA_GLASS}} | 2/9, about 22.22% |
| {{relic:PRISMATIC_GEM}} | 1/9, about 11.11% |

Four relics are possible overall, but they are not a uniform choice of four. Sea Glass is twice as common as Prismatic Gem.

### Sea Glass Also Chooses a Character

The game first draws an unlocked other character. If Sea Glass becomes the first option, it uses that target. With all five base-game characters unlocked, excluding the current character leaves four targets. **Sea Glass with a specified target** has probability **2/9 × 1/4 = 1/18, about 5.56%**.

RT2 supports a specified Sea Glass character target. A different unlock set changes the candidates and odds. Offer identity, character target, and effects after pickup are separate layers.

## Second Position: A Fixed Choice of Four

{{relic:ALCHEMICAL_COFFER}}, {{relic:DRIFTWOOD}}, {{relic:RADIANT_PEARL}}, and {{relic:SAND_CASTLE}} each have **25%** probability, with no additional eligibility requirement.

## Third Position: Retained Starting Relics and Signature Cards

{{relic:TOUCH_OF_OROBAS}} requires a retained starting relic it can improve. Base-game pairs are:

| Starting relic | Improved relic |
| --- | --- |
| {{relic:BURNING_BLOOD}} | {{relic:BLACK_BLOOD}} |
| {{relic:RING_OF_THE_SNAKE}} | {{relic:RING_OF_THE_DRAKE}} |
| {{relic:DIVINE_RIGHT}} | {{relic:DIVINE_DESTINY}} |
| {{relic:BOUND_PHYLACTERY}} | {{relic:PHYLACTERY_UNBOUND}} |
| {{relic:CRACKED_CORE}} | {{relic:INFUSED_CORE}} |

{{relic:ARCHAIC_TOOTH}} requires the corresponding signature starting card still in the deck:

| Starting card | Replacement |
| --- | --- |
| {{card:BASH}} | {{card:BREAK}} |
| {{card:NEUTRALIZE}} | {{card:SUPPRESS}} |
| {{card:UNLEASH}} | {{card:PROTECTOR}} |
| {{card:FALLING_STAR}} | {{card:METEOR_SHOWER}} |
| {{card:DUALCAST}} | {{card:QUADCAST}} |

Losing the relevant relic, or removing or transforming the relevant card, removes that option's legal target. When both are eligible, each has **50%** probability. With one eligible, it is guaranteed. With neither eligible, position three is locked instead of receiving another relic.

RT2 rebuilds this pool using the two eligibility premises upon reaching Orobas. Character identity cannot substitute for current ownership, and position three cannot offer both relics together. See [[ancient]] for premises and [[why-predictable]] for the shared random principle.
