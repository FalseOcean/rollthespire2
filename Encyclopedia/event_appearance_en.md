# Event Appearance: What Is Behind a Question Mark?

A question mark must first become an event room; only then does the game select a legal event from the current Act's queue. **A question mark is not always an event, and the next queue entry is not always eligible.**

## The room category

Under ordinary rules, an Act starts with a 10% monster chance, 2% treasure chance and 3% shop chance, leaving about 85% for events. Elites do not participate in this ordinary roll.

Non-event types accumulate probability when they do not appear: monsters gain 10 percentage points per roll, treasures 2 and shops 3. A type that appears resets to its base chance. A blocked shop does not gain probability on that roll.

A question mark cannot become a shop immediately after a shop, or when all exits from the current question mark lead to fixed shops. Relics, cards, custom rules and other special effects can also change room resolution.

A new Act resets the odds, but continues the random stream used to resolve question-mark room categories. Later Acts therefore still depend on how many question marks you actually entered earlier.

## Selecting the event

Each Act has its own shuffled queue. Starting at the current cursor, the game skips events that fail their current requirements or were already visited this run. Skipping does not reshuffle the queue or make an extra random draw. In A → B → C, for example, C is selected if A and B are ineligible.

Requirements can involve gold, health, deck, relics, potions, event pets or floor. Special effects can also replace the selected event. Under RT2's normal Neow opening premise, the Ancient room counts as one event position without drawing a normal event. The first question-mark event therefore cannot simply be equated with the first raw entry. Other openings and Acts require their own cursor information rather than a universal offset.

Visited-event history persists across Acts. The game normally avoids repeats, with a fallback allowing repetition when the queue can no longer supply a new legal event.

## The cross-Act shared pool

A shared event pool supplies candidates to multiple Acts. It does not give all Acts one queue, and it does not mean players share choices in multiplayer.

Each Act combines and shuffles its own candidates. Shared events can still have Act restrictions. An Act-1-only event is absent from Act 2's queue. An event allowed in both Acts will normally be skipped in Act 2 if you actually visited it in Act 1; merely being listed in Act 1's queue does not prevent it from appearing later.

## What RT2 currently predicts

Event filtering reconstructs candidate order, uses that Act's opening cursor information and removes known static exclusions. Candidates requiring future player state remain in the list. This does not prove their actual appearance.

The map view can predict ordinary question-mark categories along a committed route under these premises: single-player, non-tutorial, fresh Act odds, and no special room effects, event side effects or saved-run continuation. Later Acts also require earlier Act routes to establish random progress.

Even when a question mark is predicted to become an event, RT2 does not directly map the nth event room to the nth candidate. Proving a specific event's appearance also requires visited history, dynamic eligibility and route state. See [[events|Event candidates and outcomes]].
