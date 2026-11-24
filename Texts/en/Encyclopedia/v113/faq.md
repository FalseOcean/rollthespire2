# FAQ: Common Questions and Result Checks

## Why can the same seed produce different results?

Start with the game version, character, and unlocks. Check the seat in multiplayer. In a run, look at how far the relevant stream has advanced, what's left in its candidate pool, and what choices you've made. See [[why-predictable|Prediction principles]].

## How much of the run does a confirmed result reproduce?

“Confirmed” refers to the conditions in your query. If you searched for Bones and a particular card, the pickup order and card choices in the result are the premises under which those conditions hold in one opening. A candidate stays unconfirmed when you skip the final check or the model can't finish it. Saving the seed keeps that label. See [[default-environment|Default environment]].

## Why did a filtered event not appear at a question mark?

The event queue gives you candidate order after known exclusions. When you enter a question mark, the game first determines the room type. If it's an event, the game checks which candidates are eligible at that point and which you've already seen. See [[event-appearance|Event appearance]].

## Do consecutive combat rewards require adjacent fights?

What matters is whether you can continue from the same reward rules and stream. A Rest Site between two fights can leave those intact. An event that generates a card reward along the way can advance the stream the next fight uses. See [[combat|Combat rewards]] and [[combat-neow|Opening reward state]].

## Do shop colorless cards and shop-exclusive relics change together?

They depend on different kinds of progress. Colorless cards use the shop stream, and shop-exclusive relics come from their bag. Fake Merchant pricing advances the stream. A regular shop uses its shop-exclusive relic slot when it generates its stock. See [[shop-stability|Shop state]] and [[relic-shop-consumption|Shop relic bags]].

## Can I transform a different card at an event?

To reproduce the card from your search, keep the starter the condition assumes and choose that card at the relevant event branch. Substitute a card you picked up later and even the candidate pool can change. See [[event-transform-results|Event transforms]] and [[transform|Combined transformations]].

## Does changing multiplayer slots or pickup order matter?

Changing seats changes where personal streams start. If two players choose Bones, each can claim their own independently. Which of your own two relics you take first can change what the next one gives you. Shared state, such as the treasure bag, reflects what the party has picked up. See [[multiplayer|Multiplayer openings]] and [[multiplayer-shared|Shared conditions]].

## Can I use different unlocks, versions, or mods?

Set the query to match your unlocks and discoveries. A different version or a mod can affect the result if it changes a rule the query uses. See [[default-environment|Default environment]] and [[mods|Mods]].

## If Crystal Sphere has not shown a target, is it impossible?

Check whether the search has finished. You can resume it if paused, and changing a target starts a new check. See [[crystal-sphere|Crystal Sphere]].

## When can I report a program problem?

You can report crashes, interface or search problems, and results that differ from your run. Use Feedback to export a diagnostic bundle. Include the seed, settings, and what you did if you have them, so it's easier to find the step that went wrong.

## What are Developer Notes?

Developer Notes accompany the current mod release. For the full history, see the Steam release notes or the project changelog. For game mechanics, find the relevant topic in the [[reading-guide|Reading guide]].
