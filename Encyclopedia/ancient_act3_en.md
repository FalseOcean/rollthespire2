# Act 3 Ancients: Shared Candidate Lists and Fixed Positions

> Applies to: **Slay the Spire 2 Beta 0.111.0**

Act 3's three local Ancients are:

- {{event:NONUPEIPE}}
- {{event:TANX}}
- {{event:VAKUU}}

Their relic generation is simpler than Act 2's. {{event:NONUPEIPE}} and {{event:TANX}} **build one candidate list, shuffle it, and take the first three**. {{event:VAKUU}} **takes one from each of three fixed pools**.

The main special cases are two conditional relics that change pool membership.

# {{event:NONUPEIPE}}

{{event:NONUPEIPE}} has nine base candidates:

- {{relic:BLESSED_ANTLER}}
- {{relic:BRILLIANT_SCARF}}
- {{relic:DELICATE_FROND}}
- {{relic:DIAMOND_DIADEM}}
- {{relic:FUR_COAT}}
- {{relic:GLITTER}}
- {{relic:JEWELRY_BOX}}
- {{relic:LOOMING_FRUIT}}
- {{relic:SIGNET_RING}}

The game shuffles them and offers the first three. Without an additional condition, each has probability **3 / 9 = 1/3**, or about **33.33%**.

## {{relic:BEAUTIFUL_BRACELET}}

{{relic:BEAUTIFUL_BRACELET}} is an additional candidate when the deck contains at least **4 cards that can receive Swift**.

The pool grows from nine to ten, still taking three after shuffling. {{relic:BEAUTIFUL_BRACELET}} then has probability **3/10 = 30%**. Each original candidate also changes from **1/3** to **3/10**.

The condition affects the entire {{event:NONUPEIPE}} pool, not just the added relic.

# {{event:TANX}}

{{event:TANX}} uses almost the same structure, with nine base relics:

- {{relic:CLAWS}}
- {{relic:CROSSBOW}}
- {{relic:IRON_CLUB}}
- {{relic:MEAT_CLEAVER}}
- {{relic:SAI}}
- {{relic:SPIKED_GAUNTLETS}}
- {{relic:TANXS_WHISTLE}}
- {{relic:THROWING_AXE}}
- {{relic:WAR_HAMMER}}

Shuffle the whole pool and take three. Each base relic normally has **1/3** probability.

## {{relic:TRI_BOOMERANG}}

{{relic:TRI_BOOMERANG}} joins {{event:TANX}}'s pool when the deck has at least **3 cards that can receive Instinct**.

Nine base relics plus {{relic:TRI_BOOMERANG}} make ten candidates, of which three are still taken. Each then has probability **3/10 = 30%**.

Like {{event:NONUPEIPE}}'s {{relic:BEAUTIFUL_BRACELET}}, **the condition adds a candidate to the pool, not a fourth offered option**.

# {{event:VAKUU}}

{{event:VAKUU}} has three fixed positions rather than one large pool.

## First Position

- {{relic:BLOOD_SOAKED_ROSE}}
- {{relic:WHISPERING_EARRING}}
- {{relic:FIDDLE}}

Each has probability **1/3**.

## Second Position

- {{relic:PRESERVED_FOG}}
- {{relic:SERE_TALON}}
- {{relic:DISTINGUISHED_CAPE}}

Each has probability **1/3**.

## Third Position

- {{relic:CHOICES_PARADOX}}
- {{relic:MUSIC_BOX}}
- {{relic:LORDS_PARASOL}}
- {{relic:JEWELED_MASK}}

Each has probability **1/4**.

{{event:VAKUU}} has no additional eligibility conditions in base-game 0.111.0. Once its identity is known, all three positions are straightforward to predict.

# Comparing the Three Ancients

| Ancient | Generation | Special condition |
| --- | --- | --- |
| {{event:NONUPEIPE}} | Shuffle 9 base candidates, take 3 | Add {{relic:BEAUTIFUL_BRACELET}} with ≥4 Swift-eligible cards |
| {{event:TANX}} | Shuffle 9 base candidates, take 3 | Add {{relic:TRI_BOOMERANG}} with ≥3 Instinct-eligible cards |
| {{event:VAKUU}} | Take 1 each from pools of 3 / 3 / 4 | None |

For {{event:NONUPEIPE}} and {{event:TANX}}, the player only needs to supply whether the corresponding enchantment condition currently holds. {{event:VAKUU}} does not even need that.

For filtering, all three follow a simple principle:

> **Establish the correct candidate pools, then replay the offer from the seed.**
