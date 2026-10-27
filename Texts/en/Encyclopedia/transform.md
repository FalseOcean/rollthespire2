# Combined Transformations: Filtering Results Together

Sometimes you care less about which transformation gives which card than whether several transformations together give the cards you want. RT2 offers combined transformations so you can express this broader requirement.

First specify the relics that provide transformations on the Neow page, and the participating events and their queue appearance conditions on the event page. Then select these configured sources in the combination interface and combine their results. Relics and pickup order must still agree with your opening choices. Each source generates results under its own random stream and pool rules before they are counted together. Including events does not prove that all of them will be encountered during play.

Cross-source combinations currently support Beta 0.111.0 single-player queries. See [[event-transform-results|Event transformations]] for the separate availability of multiplayer event transformations.

## Sources and goals

Opening sources include two transformations from {{relic:LEAFY_POULTICE}}, one from {{relic:NEW_LEAF}}, and some two-relic {{relic:NEOWS_BONES}} openings. Event sources include two each from the relevant options in Morphic Grove and {{event:TRIAL}}, and one each from Aroma of Chaos, Whispering Hollow and {{event:SYMBIOTE}}. Target assumptions are explained in [[neow-transform]] and [[event-transform-results]].

There are three combined goals: at least a specified number of Rare cards; a specified set of cards; or specified cards with all remaining results Rare. Specified cards need no fixed source or order. Requiring the same card more than once requires separate results for each copy.
