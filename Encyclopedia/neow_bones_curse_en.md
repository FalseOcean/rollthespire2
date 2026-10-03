# {{relic:NEOWS_BONES}}' Final Curse: Earlier Choices Matter

After both internal relics are obtained and their effects resolve, {{relic-icon:NEOWS_BONES}} uses **Niche RNG** to generate its final curse. You can filter for a specified curse, but the answer corresponds to a particular opening history: pickup order, cards taken, and how later effects change the deck and random state.

## Capsule Upgrades Can Change the Curse

{{relic:WHETSTONE}} selects upgradable Attacks; {{relic:WAR_PAINT}} selects upgradable Skills. Both use Niche RNG. The sequence is **first internal relic and its effects → second relic and its effects → final Bones curse**. These Capsule relics can advance Niche before the curse is generated.

For example, take **{{relic:KALEIDOSCOPE}} → a Capsule** containing Whetstone. Taking an upgradable Attack from Kaleidoscope changes Whetstone's candidate set and may change its random consumption and the final curse. Knowing the displayed cards is insufficient; the cards actually taken also matter. The relevant state is the Niche state read by the curse; see [[why-predictable]].

## Card Targets Are Pickup Commitments

For optional rewards such as {{relic:KALEIDOSCOPE}} and {{relic:LOST_COFFER}}, **specified cards are taken; unspecified optional rewards are skipped**. One Kaleidoscope target means taking it and skipping the other group.

RT2 checks whether the specified actions produce the target. It does not automatically search whether any possible card-pick sequence could succeed. That would require branching over the different decks produced by every pickup and skip.

## Current Support and Missed Seeds

For supported single-player opening combinations, final verification replays the specified order, card picks, deck changes, legal upgrade candidates, and random consumption before checking the curse. The result corresponds to those query premises.

Multiplayer filtering accounts for the random consumption needed by later results when Whetstone or War Paint is explicitly required. It currently does not filter specific upgrade targets; predicting the curse does not imply prediction of the entire final deck.

Early filtering can sometimes use only the legal upgrade-candidate count and Niche advancement. If the upgrade relic is required but prior conditions or the Capsule source do not determine that advancement, candidates are retained for final verification. **Hidden Whetstone or War Paint absent from the conditions may still be ignored during early filtering, causing missed seeds. Final verification cannot recover seeds already missed.**

Specifying Kaleidoscope picks, Bones order, and Capsule targets makes the history explicit and can strengthen early filtering. It completes the premises; it does not make final verification “more accurate.”
