# Reading Guide

I want RT2 to do more than find seeds that reliably meet a set of conditions. I want as many of your requirements for a seed as possible to become searchable conditions. With more of your choices specified, there is more information to predict and filter. That is why RT2 sometimes asks for extra input, your judgment, or an agreement about how you will play. I need to balance answering requirements strictly and correctly, accommodating broader requirements, and keeping computation affordable.

## The seed finder's limits

A seed finder translates what players want into conditions, then searches quickly under those conditions. What it can offer also depends on the cost of computation. One question matters to me here: what kind of seed are we actually looking for?

If we want an outcome to hold however we play, outcomes affected by action history require checking the consequences of different routes, options and pickups. Finding one set of choices that achieves an outcome can be much cheaper than proving every choice achieves it. This does not make every “all routes” condition expensive: properties such as map node counts can be checked directly on the map. See [[map-routes|Map routes]].

At the other extreme, we could ask players to enter every action in advance. That would establish more information, but finding a seed could become a long questionnaire. Some decisions require seeing the actual map, nodes and rewards; sometimes even the player cannot decide beforehand. A predefined route strategy is one approach, but I do not want every such case to add more input and make both use and implementation cumbersome.

I therefore want the seed finder to provide information it can compute economically, with the predictor carrying forward processes that depend heavily on player choices: choose a route after seeing the map, choose an event option when facing it, then understand the consequences of those choices. This describes the intended division of work. The processes currently available remain those actually offered by the tools; it does not mean the predictor already covers every route, state and event.

There are many excellent seed finders online. My aim is to include as much of what players want from a seed as can be expressed at a reasonable computational cost; on that foundation, simple requirements should also be well served.

The encyclopedia explains the thinking behind these choices; the filtering interface explains the controls. Start with the [[default-environment|Default environment]] to find your entry. [[multiplayer|Multiplayer]], [[mods|Mods]], and [[faq|FAQ]] have further explanations. For background, read [[seed-history|Seeds & search]] and [[why-predictable|Why prediction works]].

Seed Information inspects results for one seed. During play, operation prediction previews the current option in its live context. For information along your current Act's route, see [[path-prediction|In-run path prediction]]; for the Crystal Sphere board and reward guide, see [[crystal-sphere|Crystal Sphere]].
