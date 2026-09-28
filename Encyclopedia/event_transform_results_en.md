# Transformation Events: Starting Cards by Default

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Some events transform cards randomly. A common goal is:

> **Turn a starting card, such as Strike or Defend, into a specified card.**

These results can be predicted in advance.

# Why Is a Premise Needed?

Besides the seed, the result depends on **which card the player actually chooses to transform**.

Allowing arbitrary picks after acquiring, removing, and replacing cards along the route would require knowing the full deck at the event. RT2 narrows the question to avoid that:

> **By default, transform a Basic card present at the start.**

This usually means a starting Strike or Defend.

# The Premise Is Simple

Keep the specified starting card until the event, then actually choose it for transformation.

As long as premises such as the available transformation pool remain unchanged, RT2 need not reconstruct the entire deck. The condition is usually straightforward to maintain.

# Different Events Transform Different Numbers of Cards

Some supported events transform **one starting card**; others transform **two in sequence**.

Morphic Grove's Group branch processes two cards, while Aroma of Chaos and Whispering Hollow can be handled as single starting-card transformations.

If an event has fixed random steps before the transformation, RT2 includes them. It does not assume transformation is always the event's first random call.

# Why Is the Result Still Stable?

These transformations use the event's own random state. The result can be replayed from the seed when:

> Event identity is known;
> the specified starting card still exists;
> the player takes the agreed branch;
> no extra action changes the relevant premises.

There is no need to prove every action throughout the run. The player accepts the commitment to transform that starting card at the event.

# What Does RT2 Do?

RT2 currently filters these explicit starting-card transformations. The player specifies the desired result and accepts the corresponding premise:

> **A suitable starting card is still available on arrival, and the corresponding event branch is chosen.**

RT2 then checks the event's actual transformation rules. With two transformations, you can require a specified card among the pair or a specified combination across both.

The condition concerns **card identities drawn from the agreed transformation pool**. It does not also guarantee later upgrades, enchantments, or other modifications after those cards enter the deck.

It also does not prove the event will appear or that the starting card survives the route. Those remain player-supplied premises.

> **The result is stably predictable if the player commits to transforming the agreed starting card.**

Restricting the default targets to starting cards makes this much simpler than recovering the entire deck at the event.
