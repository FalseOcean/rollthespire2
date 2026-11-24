# Map Generation: Why Are Computation and Probability Difficult?

A standard map is not finished once rooms have been randomly placed. The game generates routes and assigns rooms, then uses **Prune** to remove duplicate route segments and **Repair** to convert eligible normal combat nodes into missing room types. Repair can create new duplicate segments, so pruning may run again.

These steps change the available routes and node types. Computing a complete map must do this work, making map conditions relatively expensive to evaluate. These complex adjustments also make analytical probabilities difficult to derive; RT2 currently estimates map probabilities mainly from recorded experiments. A map absent from a sample is not proven impossible, and data that does not cover a condition combination cannot supply an accurate probability for it.

I have considered a shortcut: skip pruning and repair, judge conditions on the intermediate map, and compute the finished map only for survivors. Because the intermediate and finished maps differ, this could reject seeds that actually satisfy the conditions. Final verification can remove false hits, but it cannot recover solutions already discarded. This makes the idea lossy.

The idea aims to reduce the work of generating complete maps, but its actual speed benefit still needs measurement, and lost solutions are a cost to consider. Current RT2 map search evaluates maps after pruning, repair, and final cleanup. Computation optimizations that preserve results, or early rejection proven not to lose solutions, are different from this lossy idea.
