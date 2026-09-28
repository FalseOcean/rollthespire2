# Neow: Why Is the Opening So Easy to Predict?

Neow is one of the first random processes in a run, and one of the easiest things for RT2 to predict.

It can answer a simple question:

> Does Neow offer the relic I want in this run?

Or go a step further:

> If I take that relic, what random results will it produce?

Neow is especially suitable for seed searching because **it happens when the run has almost no history yet**, rather than because it lacks randomness.

## What Do You Want to Know?

Neow filters primarily address two layers.

The first is the **offer**:

> Which options does Neow provide in this run?

The second is the **result** after taking an option:

> What happens when that option is executed?

Some Neow relics require no further randomness after being obtained. Others generate cards, relics, potions, transformations, or other random results.

A Neow condition can therefore require an option to appear, or go deeper into the results it produces.

## Why Can We Determine This?

Neow's greatest advantage is how little prior history needs to be reconstructed.

For a result later in the game, we often need to know:

> Where the player has been;
> which choices they made;
> which effects have already consumed the relevant RNG;
> and how the current game state has changed.

At Neow, almost none of that has happened yet.

Neow's offer has a clear random starting point. Given the seed and required inputs such as character and unlock state, its generation can be replayed directly.

If the chosen option needs additional random results, its effect then uses the appropriate RNG stream: rewards, transformations, or another special random process.

The prediction is usually short:

> Seed
> → Generate Neow's offer
> → If needed, execute the chosen relic's internal random effects
> → Check the conditions

RT2 does not need to simulate an entire run to answer an opening question.

## Why Is It Usually Cheap?

**How rare a result is** and **how much computation it takes to check** are different questions.

A Neow condition may be extremely rare while requiring only a few random operations per seed. RT2 can quickly conclude:

> This seed does not meet the requirement.

An exceptionally unlikely Neow condition can therefore be a very effective filter.

An ordinary Neow filter is like quickly checking an opening feature across many seeds, rather than simulating the subsequent run in depth.

RT2 also need not check everything in gameplay order. When a condition can be recovered independently and correctly from the seed, cheaper checks with stronger rejection power can run first.

Every seed that remains must still satisfy all the player's conditions.

## What Can RT2 Do Today?

Neow filters can describe:

- Options you want to see or take;
- random results produced by some options;
- ordering, target, or combination requirements for certain results.

RT2 replays only the random processes needed to answer the actual query.

This does not mean every Neow relic is predicted in the same way. Some have special random processes or input requirements, covered in their own articles.

For example:

> Why do {{relic:NEOWS_BONES}} involve two internal relics and pickup order?
> Why do Capsules lead into the relic bags?
> How are the reward groups from {{relic:KALEIDOSCOPE}} generated?
> Why do transformation relics need specified targets?

These are special cases within Neow, rather than prerequisites for understanding Neow filters.

See [[neow-offer]] for Bones' draws and pickup order, or [[neow-bones-curse]] for its final curse.

Neow is relatively straightforward because it sits at the cleanest point in a run:

**There is very little prior history, the random starting point is clear, and many conditions need only a short replay.**
