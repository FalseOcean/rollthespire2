# Opening Transformations: {{relic:LEAFY_POULTICE}} and {{relic:NEW_LEAF}}

{{relic-icon:LEAFY_POULTICE}} loses 12 Max HP and transforms the first Basic Strike and first Basic Defend in deck order. A missing target does not create an extra transformation. {{relic-icon:NEW_LEAF}} instead lets you choose one transformable card.

## Different Random States

{{relic:LEAFY_POULTICE}} uses the player's **Transformations RNG**, processing Strike before Defend. In an ordinary opening, that stream is unused, so the results can be recovered independently from the seed and target pools. RT2 can check those results before whether Neow offers the relic. All conditions still have to hold; gameplay execution order is unchanged.

{{relic:NEW_LEAF}} uses shared **Niche RNG**. Earlier opening effects using Niche, or a different chosen target, require the corresponding state and pool for continuation. Similar-looking transformations need not share a random stream; see [[why-predictable]].

## Targets and Combined Results

RT2 supports transformation-result filters. **Current ordinary prediction and Search calculate New Leaf for the character's first remaining Basic Strike, rather than exploring every legal target.** Choosing another card requires that target's pool and cannot be assumed to match this result. The target convention also affects the later deck and combined transformations.

If {{relic:NEOWS_BONES}} provides {{relic:LEAFY_POULTICE}} and {{relic:NEW_LEAF}}, there are three relevant transformation opportunities when all required targets exist. Combined transformations can require specified cards or a minimum number of Rare results across them, without assigning each result to a particular relic. The opening sources and required pickup order must still hold; the combined condition checks their collective results.
