# Event Transformations: Why Assume Starter Cards?

Like New Leaf, transformation events let you choose legal non-starter cards in the game. Different target sources can have different transformation pools. To simplify computation, judgment and input, I have currently chosen to predict starter-card transformations in RT2, avoiding a detailed deck input for these outcomes.

Starter cards include character-specific starting cards. With the same legal transformation pool, you need not select the first starting Strike. Keep the agreed legal targets available and select them at the event.

| Event | Current transformation results |
| --- | --- |
| Symbiote | One result from the transformation option |
| Aroma of Chaos | One result from Let Go |
| Whispering Hollow | One result from Hug |
| Morphic Grove | Two results from the two-card transformation option |
| Trial | Two results from Innocent in the Nondescript case |

For two results, you can require a target card or a specified pair without fixing their order. Calculation preserves each event's necessary internal random steps, such as Whispering Hollow's gold calculation and Trial's case draw.

See [[multiplayer-shared|Shared party conditions]] for Morphic Grove in multiplayer. Other outcomes are listed in [[event-results|Event outcomes]], and goals across transformation sources in [[transform|Combined transformations]].
