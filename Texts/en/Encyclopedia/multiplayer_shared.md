# Shared Party Conditions: What Is Set Once or Per Player?

Bosses, Ancient identities, Act variants, the standard map and event queues belong to the party's common world and only need setting once in RT2. An Ancient's relic offers to you, your card rewards or your shop results also depend on your character, slot and state. Put those conditions under the corresponding player. The same seed must satisfy them together.

For example, requiring Morphic Grove among the first few event candidates is one common condition. Requiring it to transform a card into a particular result belongs to an individual player. A queue condition does not guarantee which question mark contains the event; see [[event-appearance]].

## Shared events still process individual decks

An ordinary non-shared event includes the player's slot in its event random starting point, so P1 and P2 may receive different results. A shared event omits that slot difference. Morphic Grove, for example, transforms cards using each event instance's own random stream; matching inputs and pools can produce matching results. Each player still processes their own deck. Different characters, unlocks or original cards may yield different outcomes.

The seed library's Developer picks retains a tested example: **R93J1P8CQA34, Two Ironclads: Dark Embrace, Stoke & Feel No Pain**. Its recorded environment is game 0.111.0 with full unlocks. Both Ironclads play at Ascension 10 and choose Leafy Poultice for Stoke. They then transform starter cards at Morphic Grove in the first question mark, receiving the same Dark Embrace and Feel No Pain. The developer-provided play record requires no particular pickup order between the players; that conclusion is specific to this example. The seed library contains its complete configuration and instructions.

Morphic Grove's transformations use event randomness, unlike New Leaf using the run's shared Niche stream. An option using event-local randomness also does not make every reward in that event independent: some effects use personal reward streams or relic bags.

The event picker's “Event source → Shared” means the **cross-Act shared event pool**. That is a different concept from multiplayer events where players vote on a common option.

## Acquisition can affect the next player

With shared Niche or shared relic bags, an earlier player's actions can change the later player's random position or candidates. Follow the displayed slots and complete acquisition order for these results. Having no Niche target alone does not establish that every acquisition can be freely reordered.

See [[multiplayer]] for the New Leaf and Pandora's Box examples, currently connected features and multiplayer transformation limits. Set common world conditions once and personal outcomes separately, then check the choices and order that this particular result requires.
