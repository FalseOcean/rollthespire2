# Act 3 Ancients: Candidate Lists and Fixed Positions

Act 3's local Ancients {{event:NONUPEIPE}}, {{event:TANX}}, and {{event:VAKUU}} use two structures: the first two shuffle one list and take three relics; Vakuu takes one from each of three fixed pools. These candidates and odds describe base-game 0.111.0; see [[reading-guide]] for the shared environment.

## {{event:NONUPEIPE}}: Nine Base Candidates

- {{relic:BLESSED_ANTLER}}
- {{relic:BRILLIANT_SCARF}}
- {{relic:DELICATE_FROND}}
- {{relic:DIAMOND_DIADEM}}
- {{relic:FUR_COAT}}
- {{relic:GLITTER}}
- {{relic:JEWELRY_BOX}}
- {{relic:LOOMING_FRUIT}}
- {{relic:SIGNET_RING}}

The game shuffles the pool and offers the first three. Each relic appears with probability **3/9 = 1/3, about 33.33%**.

At least **4 Swift-enchantable cards** in the current deck add {{relic:BEAUTIFUL_BRACELET}}. Ten candidates still yield only three options, so every candidate, including the new relic, has probability **3/10 = 30%**. Eligibility changes the entire pool's odds without adding a fourth option.

## {{event:TANX}}: Shuffle and Take Three

Its nine base candidates are:

- {{relic:CLAWS}}
- {{relic:CROSSBOW}}
- {{relic:IRON_CLUB}}
- {{relic:MEAT_CLEAVER}}
- {{relic:SAI}}
- {{relic:SPIKED_GAUNTLETS}}
- {{relic:TANXS_WHISTLE}}
- {{relic:THROWING_AXE}}
- {{relic:WAR_HAMMER}}

Each appears with probability **1/3**. At least **3 Instinct-enchantable cards** in the current deck add {{relic:TRI_BOOMERANG}}. Three of ten candidates are still taken, changing every candidate's odds to **3/10 = 30%**.

## {{event:VAKUU}}: Three Fixed Positions

| Position | Candidates | Probability each |
| --- | --- | --- |
| First | {{relic:BLOOD_SOAKED_ROSE}}, {{relic:WHISPERING_EARRING}}, {{relic:FIDDLE}} | 1/3 |
| Second | {{relic:PRESERVED_FOG}}, {{relic:SERE_TALON}}, {{relic:DISTINGUISHED_CAPE}} | 1/3 |
| Third | {{relic:CHOICES_PARADOX}}, {{relic:MUSIC_BOX}}, {{relic:LORDS_PARASOL}}, {{relic:JEWELED_MASK}} | 1/4 |

Vakuu has no additional eligibility conditions. It shuffles each small pool and takes its first relic. This has the same odds as a direct draw, but different random consumption for a particular seed. RT2 replays the actual shuffle order.

## Filter Premises

| Ancient | Generation | Extra eligibility |
| --- | --- | --- |
| {{event:NONUPEIPE}} | Shuffle 9 or 10 candidates, take 3 | Add {{relic:BEAUTIFUL_BRACELET}} with ≥4 Swift-eligible cards |
| {{event:TANX}} | Shuffle 9 or 10 candidates, take 3 | Add {{relic:TRI_BOOMERANG}} with ≥3 Instinct-eligible cards |
| {{event:VAKUU}} | Take 1 each from pools of 3 / 3 / 4 | None |

For the first two Ancients, RT2 needs enchantment eligibility upon reaching the encounter. Count cards that can actually be enchanted, rather than relying only on card type or name. Vakuu co-offer targets cannot require multiple relics from the same position. See [[ancient]] for targets and premises and [[why-predictable]] for the shared random principle.
