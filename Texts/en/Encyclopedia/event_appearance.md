# Event Appearance: A Queue Is Not an Encounter Record

Predicting a seed's event queue for each Act is relatively inexpensive and useful. Asking whether the first question mark will contain a particular event requires a different calculation. The question mark must first become an event room, and the game skips queued events that are currently ineligible or already visited. The nth queue entry is therefore not the event actually encountered at the nth question mark.

The route matters too. Some routes have no question marks; others have only a few. An event in the queue is not an event you will encounter however you travel. Events affected by earlier actions also require knowledge of eligibility at entry, such as gold, health and deck, as well as visited-event history. Including all this in a fast seed search adds a different computational burden.

For particular event identities, I have therefore chosen to provide queue information in the seed finder. It reconstructs candidate order under the current configuration, accounts for the relevant opening event position, and removes known static exclusions. Eligibility that depends on future state still needs your judgment. I think this information is enough to help players find opportunities, while actual encounters need to be considered alongside the map and play history. Each Act has its own queue; a shared candidate pool across Acts does not give them one common queue.

## Filtering the question-mark room type first

Sometimes you do not need to guarantee a particular event; you simply want the opening question marks not to become combats or chests. The Events page can therefore require **the first one, first two, or first three question marks in Act 1 all to be event rooms**.

This condition covers normal, non-tutorial single-player runs and counts question marks in encounter order, ignoring room restrictions from adjacent shops. {{relic:JUZU_BRACELET}} is included only according to its known acquisition in this query's opening; acquiring or losing it later is not simulated.

This answers which room type a question mark becomes first. You still need to consider whether the map has enough question marks, and whether your route and event eligibility allow the particular event you want.

See [[events|Events]] for filtering and [[map|Map]] for out-of-run map information. For information along your current Act's route during play, see [[path-prediction|In-run path prediction]].
