# Event Outcomes: What Can RT2 Filter?

Many events have their own independent Event random stream, so some of their outcomes can be predicted and filtered before encountering them. This lets me offer more useful information without first reconstructing an entire run. Each kind of outcome still follows its own rules; not every random result within events is determined by one stream.

| Event | Current filter |
| --- | --- |
| Colorful Philosophers | Whether the initial color offers include a target character |
| Fake Merchant | Whether the six-item inventory includes a target fake relic, in any position |
| Trash Heap | The card or relic obtained |
| Trial | The case drawn after accepting the trial |
| Tinker Time | Offered card types and the rider effects available after choosing a type |

See [[event-stable-results|Stable event outcomes]] for the first three events' pools and probabilities. Trial's cosmetic entrant number is outside the case prediction.

Transformation results are also available: one each from Symbiote, Aroma of Chaos and Whispering Hollow, and two from the relevant options in Morphic Grove and Trial. The game allows legal non-starter targets, but to simplify computation, judgment and input, RT2 currently calculates under a starter-card assumption. This includes character-specific starter cards; you need not select the first starting Strike. See [[event-transform-results|Event transformations]] for details, and [[transform|Combined transformations]] for goals across sources.

These are the outcomes currently offered; Endless Conveyor, for example, is not a formal event filter. See [[event-appearance|Event appearance]] for queue information, and [[crystal-sphere|Crystal Sphere]] for the in-run tool.
