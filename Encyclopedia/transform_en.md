# Combined Transformations: Filtering Results Together

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

Combine results from selected transformation sources without assigning each target to a particular transformation. Cross-source filtering currently supports 0.111.0 single-player queries. Personal event transformations in multiplayer are a separate capability and do not enable multiplayer combinations.

## Three Goals

- **Rare count:** for example, at least five Rare results from seven transformations.
- **Specified cards:** require A, B, and C anywhere in the results. Requiring A twice needs two separate A results.
- **Specified cards plus a Rare remainder:** with three transformations, require A and two other Rare results. A itself can be Common or Rare. Every specified target occupies a separate result slot; all remaining slots must be Rare.

## Sources

Opening sources include two transformations from {{relic:LEAFY_POULTICE}}, one from {{relic:NEW_LEAF}}, and some two-relic {{relic:NEOWS_BONES}} openings. Event sources include two from Morphic Grove, one each from Aroma of Chaos, Whispering Hollow, and {{event:SYMBIOTE}}, and two from the corresponding {{event:TRIAL}} branch.

Each source retains its own RNG and pool rules before results are collected. For example, {{relic:LEAFY_POULTICE}}, {{relic:NEW_LEAF}}, and Morphic Grove together provide five results.

## Event and Opening Premises

Events assume the agreed starting cards are transformed. Those cards must remain in the deck and be selected at the event. Adding an event does not establish that it appears or is reachable. See [[neow-transform]] and [[event-transform-results]] for pools and selection premises.

A Neow source commits the combination to that opening. Acquisition requirements and pickup order must agree with other opening conditions; using {{relic:LEAFY_POULTICE}} still requires an opening on the seed that can obtain it.

Neow combinations cannot freely be stacked with relic-queue or combat-reward conditions. The current combat continuation exception uses only the three transformations from {{relic:LEAFY_POULTICE}} and {{relic:NEW_LEAF}} obtained through {{relic:NEOWS_BONES}}, with specified-card targets or specified cards plus a Rare remainder, and that two-relic opening explicitly committed.
