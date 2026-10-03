# Events: Candidate Order and Event Outcomes

Event filters answer two questions: **which events may appear**, and **what you can get after an event appears**. These use different random processes and need separate conditions.

## Candidate order

During world generation, the game combines each Act's local events with the cross-Act shared pool and shuffles them into that Act's queue. RT2 reconstructs this order, uses the supplied opening cursor information for that Act, and removes entries known to fail Act, mode or unlock requirements.

This gives a more useful candidate order than the raw shuffle, but it is not a record of a completed route. Your gold, health, deck or relics at entry can cause the game to skip a candidate; your route may also contain too few event rooms to reach it. See [[event-appearance|Event appearance]].

## Event outcomes

Many events have their own random state. Colorful Philosophers' color offers, Fake Merchant's inventory and Trash Heap's fixed-pool results do not continue the random progress of earlier normal combat rewards or shops. Transformation events also require you to preserve the agreed starter cards, choose the relevant branch and keep the same transformation pool.

Other event options use reward randomness, shop randomness or relic bags. An event's identity alone cannot guarantee all its outcomes. See [[event-results|Event outcomes]] for the results RT2 currently offers and their premises.

To filter for both an event and its reward, add both a candidate condition and an outcome condition. **Outcome conditions assume the event occurs; they do not guarantee it will appear at a particular question mark.**
