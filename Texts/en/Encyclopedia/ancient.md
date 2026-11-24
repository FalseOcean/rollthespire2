# Ancients: Why Declare Eligibility?

When filtering Ancients, it is easy to think of just two questions: who appears in this Act, and will they offer the relic I want? Many Ancient options also have eligibility conditions. Your deck, relics and Event Pets when you arrive determine which options enter the candidate pool.

This has two effects. The first is straightforward: if a required relic is ineligible, the Ancient cannot offer it. The second is easier to miss: even a relic you do not want can change your desired options when it is absent from the pool.

## Think of shuffling a deck

Tanx provides a useful analogy: shuffle a deck, then take its top three cards. In vanilla 0.111.0, he has nine base candidates. A tenth, {{relic-icon:TRI_BOOMERANG}}, joins them only if the current deck contains at least three cards that can receive Instinct.

If your desired card is absent from the deck, no shuffle can produce it. That is the first effect. For the second, consider shuffling nine cards and ten cards with the same seed: the shuffle process differs, so the top three identities may differ too. Even if you only want one of the nine base relics, Tri-Boomerang's eligibility still matters.

The analogy illustrates how **eligibility affects the whole generation process**. Ancients do not all shuffle one large pool: some select one option from each of several fixed positions, while others use weights or branches. Their procedures differ, but they still need the eligible candidates before generating the offer.

## Why ask players about a future state?

During a seed search, RT2 cannot infer your actual state upon reaching an Ancient from the seed alone. You might remove Basic Strikes, transform a signature starter card, lose a starting relic or obtain an Event Pet. Those facts depend on choices along the way; the starting deck cannot stand in for them.

Many options remain eligible in ordinary play. Yet a seed finder may be looking for an unusual combination, so rarely changing eligibility still matters. Otherwise, RT2 could correctly generate an offer from one pool while your actual run uses another.

I could instead try to find seeds that provide the target regardless of whether these conditions hold. That would require checking many states and combinations, which is expensive for a fast seed search. My choice is to ask the player for this judgment: **declare the state you expect when you reach the Ancient, and RT2 filters under that premise.** Eligibility does not mean you want the relic or want to avoid it. Marking it unavailable excludes it from the relevant candidate pool.

To reduce input, RT2 defaults the eligibility conditions it exposes to satisfied. This is an agreed premise, not a guarantee that your eventual route will preserve it. Adjust the declaration if you know a condition will fail. In multiplayer, consider each player's eligibility separately.

## Offers and individual Ancient rules

You can specify an Ancient's identity, required relics, or several relics **together in the same offer**. Two targets from one position that selects only one relic cannot both appear. Requiring a relic to be offered also differs from requiring a particular effect after obtaining it.

For Act 1, see [[neow]]. The detailed Act 2 and Act 3 rules remain in these articles:

- [[ancient-darv]]: Darv's Act assignment, ordinary relics and Dusty Tome branch.
- [[ancient-pael]]: Pael's three positions, candidate weights and Event Pet eligibility.
- [[ancient-tezcatara]]: Tezcatara's fixed positions and Basic Strike eligibility.
- [[ancient-orobas]]: Orobas's branches, Sea Glass target and starting-item eligibility.
- [[ancient-act3]]: Local Act 3 Ancients, including Tanx's nine-card/ten-card example.
