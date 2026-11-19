# Map Routes: Some Route or Every Route?

“Give me a route with five Elites” and “Whichever way I go, I can visit at most one Rest Site and three Elites” ask for different things. For the first, finding one route is enough. For the second, every route has to meet the limits. Counts cover complete paths from the start to the Boss. The five Elites can be spread along the way.

## Minimum, Maximum, and Comparison Direction

**Guaranteed** is the minimum across all complete routes. **Reachable Max** is the maximum. Pair either one with “at least” or “at most” to say whether the rule needs to hold for some route or every route.

| Condition | Meaning |
| --- | --- |
| `Guaranteed ≥ K` | Every route visits at least K of the specified nodes |
| `Guaranteed ≤ K` | Some route visits at most K of the specified nodes |
| `Reachable Max ≥ K` | Some route visits at least K of the specified nodes |
| `Reachable Max ≤ K` | Every route visits at most K of the specified nodes |

For a five-Elite route, set Reachable Max Elites to at least five. To cap every route at one Rest Site and three Elites, set their respective Reachable Max values to at most one and at most three.

Daily mode lets you set one route count target per Act for Elites, Rest Sites, or question marks. The three-Act total adds the maxima for the same room type across all three Acts on that seed. You can set a total alongside individual Act targets.

## Several Rooms on the Same Route

A map can have one path with five Elites and another with four Rest Sites. Both maxima can pass while the rooms you want sit on different paths. To put them on a single route, choose an Act and open **Advanced drawing**. The drawing becomes that Act's route objective. You can still set separate map properties.

Say I want a Shop and two Elites before a Rest Site, and I'm happy to visit the Shop either before or after the Elites. Add the Shop and both Elite nodes, box-select them into one group, then put a Rest Site after the group. The route has to finish the first group before a Rest Site can satisfy the second. Other rooms can fall in between, and two Elites take two distinct nodes. The fixed starting combat and pre-Boss Rest Site don't fill rooms you drew, and treasure is ignored in the sequence. Open a match to see a route that fits.

## Unavoidable Opening Combats

This property counts the consecutive normal combats that every route has to go through at the start. A value of six means your first six rooms are normal combats whichever opening branch you take. The streak ends as soon as the route reaches a different room type.
