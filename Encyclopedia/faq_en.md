# FAQ: Common Questions and Result Checks

## Why can the same seed produce different results?

Version, character, slot, unlocks, relevant random progress, candidate pools and actual choices also matter. Changes affect the streams or state involved; a random action does not automatically change everything afterward. See [[why-predictable|Prediction principles]] and [[reading-guide|Scope guide]].

## Does a search result prove the entire run is reproducible?

A verified result confirms the current query's specific conditions and premises, rather than a complete route and every later choice. In 1.3.3, Settings offers “Output unverified candidates”, off by default, which skips final validation. Candidates marked “Unverified” are not confirmed results and may fail some conditions. You can validate them individually after searching; viewing predictions does not confirm the original filters. See [[reading-guide|Scope guide]] and [[why-predictable|Prediction principles]].

## Why did a filtered event not appear at a question mark?

The filter describes candidate order after known exclusions. A question mark can become another room, and an event can be skipped because of entry requirements or visited history. Outcome conditions separately assume the event occurs. See [[event-appearance|Event appearance]].

## Do consecutive combat rewards require adjacent fights?

“Consecutive” concerns Rewards state and reward rules, not adjacent map rooms. Generating an extra reward using Rewards, or changing pools or reward counts, can require a different starting state. Skipping an already generated reward does not undo its draws; collection effects are a separate question. See [[combat|Combat rewards]] and [[combat-neow|Opening reward state]].

## Do shop colorless cards and shop-exclusive relics change together?

Not necessarily. Colorless cards depend on Shops progress; shop-exclusive relics depend on their bag. Fake Merchant prices or Courier restocks can advance Shops without taking another shop-exclusive relic. Entering a shop and generating its inventory consumes relevant state even if you buy nothing. See [[shop-stability|Shop state]] and [[relic-shop-consumption|Shop relic bags]].

## Can I transform any card instead of the agreed starter?

Current starter-card conditions require preserving legal targets and choosing the relevant branch. An acquired card is not automatically an equivalent input. Different sources or pools can change results; upgrades, enchantments and other deck-entry modifications need separate consideration. See [[event-transform-results|Event transforms]] and [[transform|Combined transformations]].

## Does changing multiplayer slots or pickup order matter?

Slots initialize many personal random processes. Shared relic bags and persistent randomness can also depend on actual pickup order. Follow the prediction's configuration, but do not apply this order requirement to every event: Morphic Grove has a tested shared-starting-point example. See [[multiplayer|Multiplayer openings]] and [[multiplayer-shared|Shared conditions]].

## Can I use different unlocks, versions or mods?

Distinguish query configuration from the actual environment. Unlocks and added content can change pools; versions and mods can change rules. Unverified combinations cannot be guaranteed accurate, but that does not establish blanket incompatibility. See [[reading-guide|Scope guide]] and [[mods|Mods]].

## If Crystal Sphere has not shown a target, is it impossible?

Not yet found does not mean excluded. Paused results may also be incomplete; you can continue computing. Selecting another target triggers a new compatibility check. Use existing plans according to the page's instructions. See [[crystal-sphere|Crystal Sphere]].

## When can I report a program problem?

Crashes, interface problems and search startup or runtime failures can be reported directly, without completing these checks first. Results that differ despite matching premises are also worth reporting. Use Feedback to export a diagnostic bundle and include the seed, configuration, actual actions and symptoms. These checks help investigation; they are not a reporting requirement. See [[reading-guide|Scope guide]] for the premises.

## What are Developer Notes?

Developer Notes are developer-written notes bundled with the current Mod version. Full history belongs in Steam Release Notes, the project CHANGELOG and other historical records. This FAQ covers usage questions; follow [[reading-guide|Reading guide]] to the encyclopedia for mechanics.
