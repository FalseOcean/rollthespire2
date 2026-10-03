# Shared Party Conditions: Common World and Personal Outcomes

In multiplayer, “shared” can refer to the world, common choices or pickup state that affects another player. These relationships need separate consideration.

## One common world

Act variants, bosses, the standard map and event queues belong to the common world and only need setting once in RT2. Requiring Morphic Grove among the first few candidates constrains this queue; it neither specifies a player's transforms nor guarantees which question mark contains the event. See [[event-appearance|Event appearance]].

The event picker's “Event source → Shared” means the **cross-Act shared pool**. This differs from multiplayer events where players vote on a common option.

## Ordinary events use player slots

Non-shared events normally allow independent choices and include the player slot in their event random starting point. P1 and P2 can therefore have different, reproducible outcomes. Character, unlocks, available pools and actual choices still matter.

Some options also use a player's reward randomness or relic bags and require the corresponding history and state. Event-local stability does not guarantee every reward within an event.

## Morphic Grove's shared transforms

**Morphic Grove is a shared multiplayer event. Each player's event randomness starts at the same point, without a slot difference.** Matching characters choosing the same branch can receive matching transforms when their input cards, delivered processing order, pools and relevant effects agree.

Common choices and a common random starting point do not create a common deck: each player processes their own cards and pools. Different characters or premises can produce different outcomes.

**Developer-tested example: Two Ironclads: Dark Embrace, Stoke & Feel No Pain, seed R93J1P8CQA34.** Both Ironclads play at Ascension 10 and choose Leafy Poultice for Stoke. In the developer's test, the first question mark contained Morphic Grove, where starter transforms yielded Dark Embrace and Feel No Pain. No particular pickup order between players was required.

Seed library's Developer picks includes the full configuration and instructions, recorded for game 0.111.0 with full unlocks. This test does not establish that all multiplayer events give identical rewards or ignore action order.

## Pickup order is a separate question

Neow, relics inside Bones and later rewards can involve persistent random progress or shared relic-bag changes. A relic taken by an earlier player can change the candidates for a later player. Follow the displayed player order and internal Bones pickup order for these processes.

Set common conditions once, personal results per player, and follow the page's instructions where order matters. See [[multiplayer|Multiplayer openings and slots]] and [[event-transform-results|Starter-card transform premises]].
