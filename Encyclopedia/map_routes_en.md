# Map Routes: Minimum and Maximum Visits

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

Route counts cover complete legal paths from the start to the Boss. The number of Elites on a map is not necessarily the number one route can visit.

## Guaranteed and Reachable Max

**Guaranteed** is the minimum count across complete routes. A Guaranteed Elite count of 4 means even the route with the fewest Elites visits at least four. A value of at most 2 only establishes that some route visits at most two.

**Reachable Max** is the maximum count across complete routes. A maximum of one Rest Site means no route can visit two. A maximum of at least four Elites means some route visits at least four.

| Condition | Meaning |
| --- | --- |
| `Guaranteed ≥ K` | Every route visits at least K |
| `Guaranteed ≤ K` | Some route visits at most K |
| `Reachable Max ≥ K` | Some route visits at least K |
| `Reachable Max ≤ K` | Every route visits at most K |

Different maxima may belong to different routes. Four reachable Elites and five reachable question marks do not establish a route containing both.

Open [[seed:001W48N6QUWB]] or [[seed:001W48LRVYMR]] to inspect route properties with the predictor's current character and Ascension settings. The four-Elite and one-Rest-Site counts above illustrate the conditions; they are not claims about these seeds under every configuration.

## Unavoidable Opening Combats

This property counts the consecutive normal combat nodes every route must visit at the beginning, rather than all normal combats in the Act. A value of six means every branch starts with six consecutive normal combats.

Open [[seed:001W48N78QTT]] to inspect this property. Six combats is also an illustrative condition; use the predictor's current configuration for the actual value.
