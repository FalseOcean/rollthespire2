# Combined Transformations: Filtering Several Changes Together

> Applies to: **Slay the Spire 2 Beta 0.111.0**

A run can contain multiple random transformation opportunities, from Neow relics, combinations inside {{relic:NEOWS_BONES}}, or events such as Morphic Grove, Aroma of Chaos, and Whispering Hollow.

Predicting one transformation is not difficult. The more useful question is often:

> **What can all of these transformations give me together?**

# Why Combine Them?

Suppose a seed has six predictable transformation opportunities. You may not care whether the first becomes A and the second B.

Instead, you may want **at least four Rare results among the six**, or **several specified cards anywhere among all results**.

Separate conditions for every transformation make that goal awkward. RT2 combines the opportunities and checks the final collection.

# Two Main Goals

### A Rare-Card Count

> **At least five Rare cards from seven transformations.**

The individual positions do not matter. Only the total Rare count matters.

### A Specified Card Combination

> **All transformation results together must contain the specified cards.**

For A + B + C, the combined results need only contain those three cards. This is an **unordered target collection**, not fixed requirements for the first, second, and third transformations.

# Which Sources Can Be Combined?

Supported sources primarily include transformations already predictable on their own:

> {{relic:LEAFY_POULTICE}};
> {{relic:NEW_LEAF}};
> some {{relic:NEOWS_BONES}} combinations;
> Morphic Grove;
> Aroma of Chaos;
> Whispering Hollow;
> {{event:SYMBIOTE}};
> the corresponding double-transformation branch of {{event:TRIAL}}.

Sources provide different counts: {{relic:LEAFY_POULTICE}} gives two, {{relic:NEW_LEAF}} gives one, and Morphic Grove's Group gives two.

When selected together, RT2 collects all their results before checking the overall goal.

# Event Transformations Still Use Starting Cards by Default

The event premise remains **transforming starting cards**. You need not describe the full deck at every event, but must retain the agreed cards and choose them for transformation.

Event appearance remains separate. Including an event as a transformation source does not prove that the route can encounter it.

# Neow Identity and Transformation Results Remain Separate

“Obtain {{relic:LEAFY_POULTICE}} and include its two transformations” contains two questions:

> **Can I obtain {{relic:LEAFY_POULTICE}}?**
> **What do its transformations produce?**

The combination filter handles the latter. Wanting {{relic:LEAFY_POULTICE}}'s results does not remove the requirement to obtain {{relic:LEAFY_POULTICE}}.

The same applies to {{relic:NEOWS_BONES}}. Opening conditions still specify which relics are required and how they may be obtained; the aggregate collects the transformation results.

# Why Not Combine Every Kind of Random Result?

Every member of a transformation combination resolves to **the same kind of object: a transformed card**.

Results from different sources therefore naturally form a collection of card identities. Combining a relic, potion, reward card, and event into a universal combination system would be a different problem.

RT2 currently combines transformation results that genuinely describe the same type of outcome.

# How Does RT2 Do It?

The implementation calls this Transformation Aggregate. To the player:

> **Choose the transformation opportunities you intend to use.**
> **Specify what their combined results must satisfy.**

RT2 computes each source using its actual random rules, then checks the Rare count or specified card combination.

It answers not just “What will this one transformation produce?” but:

> **If I use all these transformation opportunities, can their results form the collection I want?**
