# Map Generation: Why Are Computation and Probability Difficult?

A standard map starts with paths, then assigns room types to their nodes. A randomly placed Rest Site can appear one row before the first row that allows Elites. For each node, the game looks down the queue for the first room type that fits. The types it passes over keep their place. Rest Sites have few eligible rows, so a Rest Site that can't go here keeps its priority at the next suitable node.

Once rooms are assigned, **Prune** removes duplicate path segments and **Repair** converts eligible normal combat nodes to fill missing room types. Repair can create duplicates of its own and send the map through another round of pruning. The game then tidies up the layout. Only after those steps do you know which paths you can take and how many Rest Sites they visit.

Generating a map for one seed and estimating how common a map is are separate jobs. RT2 can generate the map under the current rules and check your conditions. Those adjustments make the odds of a particular combination hard to calculate directly. The available map frequencies come from samples of base-game 0.111.0 maps. Even in that sample, an unseen map is simply one those draws didn't turn up.

By default, map conditions use the finished map. In solo search, **Enable lossy map prefilter** lets the intermediate map do a first pass. Supported conditions are checked after the first room assignment, and the surviving seeds get a full map check. Suppose an intermediate map is short a Rest Site that Repair would add. The prefilter may discard that seed before Repair gets there. The final check can catch false hits, but seeds discarded early are gone.
