# Why Can the Future Be Predicted?

For seeds and hashes, the difference between filtering and prediction, and version history, start with [[seed-history|Seeds: What Are We Searching?]].

Much of the Spire's randomness starts from a seed and follows fixed generation rules. The same seed, relevant state and operation order can reproduce the same outcomes. RT2 replays the process for a target result, then checks whether it meets your conditions.

## The game has multiple random streams

All random actions do not share one sequence. Player rewards use `Rewards`, normal shops have `Shops`, and ordinary question-mark categories use a separate stream. Many events also have local randomness initialized from the seed, event identity and player slot.

Whether an action affects a prediction depends on the stream it actually uses. Fake Merchant's inventory identities use event randomness, for example, while its prices use Shops. Pricing can advance later normal shop randomness without advancing Trash Heap's own event stream.

Each stream has continuing progress. An option that generates a reward using Rewards changes where later combat rewards start. Generation and collection are separate stages: skipping an already generated reward does not undo its generation draws. Whether collecting it changes later outcomes depends on its acquisition effects and relevant state.

## The seed is not the only input

The same random number can select different objects from different candidate pools. Character, unlocks, mode, pool order, remaining relic-bag contents and actual choices are also inputs.

An event transform needs the selected card and its transformation pool. Event appearance also depends on eligibility at entry. A seed without these inputs may not give one unique answer.

Think of prediction as **seed + relevant random progress + required state and choices → target outcome**. An unrelated action does not change every outcome merely because it uses randomness. Changing relevant rules or candidates can also change the answer without making extra draws.

## Predictability and available features differ

RT2 prioritizes replaying a defined piece of mechanics with known inputs, rather than automatically simulating the entire run. Checking many seeds and retaining matches is seed searching.

Distant outcomes may require routes, event choices, acquisition effects and state changes. Specifying these inputs reduces branches, but theoretical analysis does not establish an available RT2 feature.

See [[combat|Combat rewards]], [[shop-stability|Shop state]], [[events|Events]] and [[event-transform-results|Event transforms]] for specific premises, and [[reading-guide|Reading guide]] for the encyclopedia's scope.
