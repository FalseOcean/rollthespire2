# Why Can the Future Be Predicted?

Slay the Spire 2 is full of randomness.

The next card reward, the shop's inventory, the next relic in a bag, the outcome of an event—at the start of a run, none of these seem to have happened yet.

If they are random, how can we search for them in advance?

The answer is that **most randomness in the Spire is not truly unpredictable**. It begins with a seed and proceeds step by step under deterministic rules.

That is both why different seeds have different futures and why RT2 can look for those futures.

## The Seed Determines the Starting Point

Imagine a random number generator as a machine that keeps producing numbers. Give it a seed and it produces a seemingly disordered sequence:

> 37 → 81 → 12 → 64 → 5 → …

A different seed produces a different sequence. But with the same seed, using the machine in the same order produces the same results again.

This randomness has two properties:

**It is hard for the player to guess in advance.**

**It is repeatable for the game.**

That is why replaying a seed can show many of the same things. RT2 uses this property: it re-executes the process that generates a result rather than guessing the result.

## The Game Has More Than One Random Sequence

One random-number machine for an entire run would cause some strange interactions.

Suppose you drink a potion that randomly generates a card in combat. If rewards, shops, events, and relics all used the same sequence, that extra random call could shift everything afterward:

> Drink a potion
> → The next card reward changes
> → A later shop changes too
> → Further random results also shift

The Spire separates different kinds of random behavior to avoid many unrelated interactions. These relatively independent sequences are called **RNG streams**.

Think of them as rivers branching from the same seed:

> Combat randomness follows its own river.
> Rewards follow another.
> Shops have their own random process.
> Transformations have theirs.
> Different parts of world generation also have their own random states.

They all belong to the same run, but an unrelated random operation does not have to advance them all.

This matters because **predicting a card reward need not require simulating the whole run up to that point**. Often, we only need the random process that actually determines that result.

## RNG Streams Move Forward

Separate streams still have order within each stream. Suppose a reward RNG produces:

> A → B → C → D → E

The first three relevant rewards might normally use:

> First: A
> Second: B
> Third: C

But if an opening effect consumes two results from the same stream before the first combat reward:

> Opening effect: A, B
> First combat reward: C
> Second combat reward: D
> Third combat reward: E

Neither the seed nor the rules changed. **The RNG stream is now at a different position.**

This is the disruption often encountered in prediction. Whether an action affects later predictions depends on a specific question:

> **Did it consume the random stream that determines those later results?**

If it did not, it generally will not change them for this reason. If it did, prediction must continue from the new position.

## Knowing the Seed Is Not Always Enough

If a seed determines RNG, does knowing it reveal everything about the run? No. Random numbers are only part of the input.

A transformation result may also depend on:

- Which card is transformed;
- the current character;
- unlocked cards;
- the available card pool;
- the player's choice;
- earlier actions that changed relevant state.

A complete prediction is closer to:

**Seed + correct RNG state + current game state + player choices → game result**

If an important input is unspecified, the question may not have a unique answer. This is why RT2 sometimes needs additional premises.

An event may let you transform a card from various possible decks. Asking “What does this seed transform in this event?” first requires answering “Which card will you transform?”

RT2's use of explicit premises such as transforming a starting card does not imply that the game only permits that choice. A predictable question needs definite inputs.

## What Does RT2 Actually Do?

For each seed being checked, RT2 recovers the information needed for the target result as far as possible:

> Identify the relevant random process;
> start at the correct position;
> supply the character, pools, choices, and other inputs;
> regenerate the result using the game's rules;
> check whether it satisfies the player's conditions.

“Prediction” can even be a misleading word. RT2 is closer to **replaying a small piece of the game's future ahead of time**.

If we can ask the question of one seed, we can ask it of many:

> Do the first three rewards contain the card I want?
> Does a particular event produce the result I want?
> Does a particular relic occur early in its sequence?

Checking many seeds and keeping those that qualify is seed searching.

## Glancing at a Result Versus Following a Run

Predictability does not imply equal cost.

An opening outcome, a fixed event result, or the first few positions of a relic sequence can be cheap to check: collect a few inputs, find the random process, and replay a short piece of logic.

Computationally, this is like **taking a quick look at one feature of a seed**. We do not need to know the player's later route or simulate the entire run.

Such questions are well suited to searching a large seed space. Each individual check is inexpensive.

Deeper prediction changes the picture. In principle, with enough player input we could predict much further:

> Which Act 1 route is taken;
> which combats and events occur;
> what is chosen in shops;
> which rewards are collected;
> which state-changing effects actually happen.

Given a definite history, replay can continue along it. Even **the Rare card reward after the Act 1 Boss** is not inherently unpredictable.

But this is no longer a quick glance at a seed's feature. We are beginning to **follow the run inside that seed**.

## Why Do Deeper Routes Cost More?

The further a run goes, the more the player contributes:

> Left or right?
> Shop or Elite?
> Which question-mark event occurs?
> Which card is taken?
> Which relic is obtained?
> Which event option is chosen?

Those choices continually change later state. A distant prediction usually needs both **what the game offered** and **what the player did with it**.

When the player specifies the route and choices, many branches disappear and RT2 can follow one history. Otherwise, it may face a tree of possible futures:

```
               Opening
              /       \
          Route A     Route B
          /    \      /    \
       Choice1 Choice2 Choice3 Choice4
          ...    ...    ...    ...
```

Possible routes, choices, and states multiply. The computation is very different from a simple filter.

**Distance into the future is not the only source of cost.** We must also ask:

> How much state needs reconstructing first?
> How many player choices need specifying?
> If they are unspecified, how many alternatives need considering?

This is a fundamental performance difference between route prediction and ordinary seed searching.

## More Player Input Makes the Future More Definite

**The more the player specifies, the less RT2 must infer.**

“Predict the Rare card after the Act 1 Boss” leaves routes, rewards, events, and states unresolved. Specifying the route, rooms, reward picks, and event choices progressively shrinks that possibility space.

Eventually it becomes:

> **Continue deterministic RNG from a definite state.**

In theory, RT2 can predict far ahead. The limit is usually not that distant randomness is unknowable, but **whether the preceding history is sufficiently explicit and how expensive it is to reconstruct**.

## Why Are Some Results Easier Than Others?

A result is generally easier to predict reliably and cheaply when:

- Its random process is known;
- relevant prior RNG consumption is known;
- its required game state can be recovered accurately;
- player choices can be expressed clearly;
- reaching it does not require exploring many routes and states;
- its generation rules can be replayed.

Unspecified prior actions, dynamic state, routes, or incompletely understood random consumption make prediction harder and potentially far more expensive.

RT2's filters are not an arbitrary selection of game content. Each addresses the same questions:

> **Which random process determines this result?**
> **Do we know its inputs?**
> **Can we execute this part of the game correctly again?**
> **How far into the seed must we go to find the answer?**

The following articles answer those questions for Neow, combat rewards, shops, events, relics, maps, and more.

They appear to predict very different things, but share a starting point:

**The Spire's randomness is a future that can change—and a future that can be found again.**
