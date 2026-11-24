# Map Routes: Some Route or Every Route?

“Give me a route with five Elites” and “Whichever way I go, I can visit at most one Rest Site and three Elites” are different requests. The first needs some route to satisfy the condition; the second puts a limit on every route. Counts cover complete legal paths from the start to the Boss. Five Elites means five in total along a route, without requiring adjacent Elite nodes.

## Minimum, Maximum, and Comparison Direction

The current interface expresses route counts through two extrema: **Guaranteed** is the minimum across all complete routes; **Reachable Max** is the maximum. Each must be read together with “at least” or “at most.” The metric name alone does not determine whether a condition concerns some route or every route.

| Condition | Meaning |
| --- | --- |
| `Guaranteed ≥ K` | Every route visits at least K of the specified nodes |
| `Guaranteed ≤ K` | Some route visits at most K of the specified nodes |
| `Reachable Max ≥ K` | Some route visits at least K of the specified nodes |
| `Reachable Max ≤ K` | Every route visits at most K of the specified nodes |

A five-Elite route can therefore be expressed as “Reachable Max Elites at least five.” The second example limits Reachable Max Rest Sites to one and Reachable Max Elites to three. These numbers illustrate the conditions; they do not promise that every Act, Ascension, or player-count configuration can realize them.

Open [[seed:001W48N6QUWB]] or [[seed:001W48LRVYMR]] to inspect route properties using the predictor's current character, Ascension, and other settings.

## Why Allow Only One Route Objective?

I want to avoid presenting two independent existence claims as a promise about one route. A map may offer a five-Elite route and a four-Rest-Site route without any route visiting both five Elites and four Rest Sites. Different node types' minima and maxima can also belong to different routes.

Currently, each Act can have at most one route objective: an “at least” count for Elites, Rest Sites, or question marks. Alternatively, one whole-run objective sums the three Acts' route maxima for the same node type. Whole-run and individual-Act route objectives are mutually exclusive. This restriction concerns route objectives; it does not limit the entire query to one map condition. Multiple map properties can still be set, but each describes its own extremum and does not automatically establish a route satisfying them together.

This is a deliberate limit in the current organization. It also does not provide an impossibility verdict or an accurate probability for every combination when you enter it. Suggestions for more specific requirements about a shared route are welcome.

## Unavoidable Opening Combats

This property counts the consecutive normal combats that every complete route must visit at the beginning, rather than all normal combats in the Act. A value of six means every opening branch requires six normal combat nodes first.

Open [[seed:001W48N78QTT]] to inspect this property. Six combats is an illustrative condition too; the actual value depends on your current configuration.
