# Pael: Three Pools and Unequal Weights

Pael offers one relic from each of three positions. The first uses a fixed pool, the second depends on the deck and uses weights, and the third depends on Event Pet ownership. These pools and odds describe base-game 0.111.0; see [[reading-guide]] for the environment.

## First Position: A Fixed Choice of Three

{{relic:PAELS_FLESH}}, {{relic:PAELS_HORN}}, and {{relic:PAELS_TEARS}} always participate, each at **1/3**.

## Second Position: Eligibility and Weights

{{relic:PAELS_WING}} and {{relic:PAELS_GROWTH}} always participate. The other two require:

- {{relic:PAELS_CLAW}}: at least **3 Defend cards legally enchantable with Goopy** in the current deck. A Defend tag alone is insufficient; enchantment rules also apply.
- {{relic:PAELS_TOOTH}}: at least **5 normally removable cards** in the current deck.

{{relic:PAELS_WING}} and eligible {{relic:PAELS_CLAW}} and {{relic:PAELS_TOOTH}} each have **2 units of weight**; {{relic:PAELS_GROWTH}} has **1**. This is not a uniform draw of four:

| Conditional relic eligibility | {{relic:PAELS_WING}} | {{relic:PAELS_CLAW}} | {{relic:PAELS_TOOTH}} | {{relic:PAELS_GROWTH}} |
| --- | --- | --- | --- | --- |
| Neither | 2/3 | 0 | 0 | 1/3 |
| {{relic:PAELS_CLAW}} only | 2/5 | 2/5 | 0 | 1/5 |
| {{relic:PAELS_TOOTH}} only | 2/5 | 0 | 2/5 | 1/5 |
| Both | 2/7 | 2/7 | 2/7 | 1/7 |

Even a {{relic:PAELS_WING}}-only target requires accurate premises for the other two relics: their participation changes its odds from **2/3** to **2/7**.

## Third Position: Event Pets

Without an Event Pet, {{relic:PAELS_EYE}}, {{relic:PAELS_BLOOD}}, and {{relic:PAELS_LEGION}} each have **1/3** probability. With an Event Pet, {{relic:PAELS_LEGION}} is excluded and {{relic:PAELS_EYE}} and {{relic:PAELS_BLOOD}} each have **1/2**.

Act 1's {{event:BYRDONIS_NEST}} in Overgrowth can give {{card:BYRDONIS_EGG}}. **The egg already counts as an Event Pet while in the deck, before hatching**, excluding {{relic:PAELS_LEGION}} upon reaching Pael. Hatching at a Rest Site gives {{relic:BYRDPIP}}, which also counts, preserving that state.

## Premises Describe the Current State

RT2 targets say which relics you want offered; {{relic:PAELS_CLAW}}, {{relic:PAELS_TOOTH}}, and {{relic:PAELS_LEGION}} eligibility describes the state upon reaching Pael. An eligibility premise does not require that relic to be drawn; it determines the actual pool. The egg matters even if {{relic:PAELS_LEGION}} is not a target. See [[ancient]] for targets versus premises and [[why-predictable]] for the shared random principle.
