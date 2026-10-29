# Families: Bringing Related Computation Together

My long-term goal is for any requirement a player can describe for a seed to eventually have an efficient, correct, and convenient implementation. To that end, I will do my best to turn those requirements into explicit conditions, then compile them into filtering computations that can run at high speed. Reaching that goal means keeping up with game mechanics and player needs, and continually maintaining, extending, and optimizing the tool. Families are one way we organize this computation and work toward that goal.

## One Seed Has Many Different Questions to Answer

A player might want a particular relic at the start, specified cards in the first few combats, a desired outcome from an event, and a map that suits them. Together, these requirements form a seed-search request.

The seed finder reconstructs the random processes needed to answer those questions from the seed. Some results need only one draw; others need several rewards generated in sequence, a shuffle, or a scan through candidates. Maps also require generating and arranging routes. These computations differ in both method and cost.

Some of this work is closely connected: premises can be prepared together, random state can continue from where it left off, and one computation can check several targets. I organize these related observations and computations into a family so they can be handled together.

Family boundaries take shared premises, random consumption, computational cost, and actual game semantics into account, so interface sections and families need not correspond one to one. For example, what Neow offers, what a Capsule contains, and what transformations from several sources produce in total can belong to different families. Observations of shop relics and other relic bags, meanwhile, can be handled within the relic family.

## Giving Optimization a Clear Place to Happen

Families also provide a simple, clear way to organize optimization. We can examine the work a family needs to do, then organize and implement improvements within it.

Which premises can be prepared in advance? Which random computations can avoid repeated work? Which candidates can be rejected earlier? How should this work run on a CPU or GPU? Each question now has a concrete place to be addressed.

That makes the gains easier to see: what work a family no longer needs to do, and how much faster it has become. Different filter combinations that use the same computation can reuse the improvement. When a family becomes more economical, its new advantage can also be reflected in execution order, letting later searches benefit.

Long-term optimization can therefore keep progressing around these clear units of work. However complex the conditions become, we can first return to the relevant family and examine what the computation actually needs and where further improvements would help.

## Share Known Requirements Early; Keep Per-Seed Computation as Independent as Practical

Results across families are, of course, connected. Obtaining Prayer Wheel at the start changes later combat rewards; relics inside a Capsule may also affect later computation.

If the player explicitly requires actually obtaining Prayer Wheel at the start, that premise is known before the search begins. The combat reward family can prepare its computation assuming Prayer Wheel is held, while the Neow family checks whether the seed really meets the opening requirement. Each must still advance its required random consumption correctly.

Input preparation can thus consider the entire request, while computation for individual seeds stays as independent as practical. Each family reconstructs the state it needs from the same seed and the prepared premises. Ordinary families primarily pass along the identities of seeds that remain.

This also leaves room to change execution order. Since the combat reward computation already knows the premise that Prayer Wheel must be obtained, it need not always wait for the Neow family to finish each seed just to learn that fact. In the end, the same seed and one compatible set of choices must still satisfy the entire request.

## Unspecified Possibilities Have a Computational Cost Too

Consider another case: the player has not specified the relic inside a Capsule, but it might contain Prayer Wheel. Fully accounting for that possibility requires generating and interpreting unspecified outcomes, then continuing to compute their effects on later rewards.

This work grows. Passing earlier results forward requires maintaining state for each seed. Having later families replay the process themselves also changes the job: instead of merely advancing random consumption, they must generate outcomes, interpret effects, and track their consequences.

For some unspecified opening effects, I have chosen an explicit simplification: omit this full expansion and accept that some seeds which could actually satisfy the request will be missed. Final verification cannot recover seeds already rejected earlier. That cost needs to be clear.

I would rather the tool make full use of information the player has already provided: specifying key outcomes more clearly gives the computation more definite premises to use. More specifications may also make the target rarer and require more checking, so they do not guarantee a shorter total wait. This saving has a defined scope; explicitly requested conditions and necessary random consumption must still be handled correctly.

## What to Check First Depends on Both Cost and Rejection Power

With these families, a seed can pass through filtering in stages. Candidates known not to qualify can leave early, and later families handle only those that remain.

A strong filter is not necessarily worth running first. Suppose A costs one unit of computation per seed and rejects 90%, while B costs ten units and rejects 99%. In a simple case where the two are independent and cost per check is approximately stable, it is cheaper to let the inexpensive A remove most candidates, then have B handle the small remainder.

We can express this relationship as:

```text
c: cost of processing one input to a stage
s: proportion that survives
1-s: proportion rejected

Cost per candidate rejected = c / (1-s)
```

A lower value means less computation is expected to reject one candidate, making the check a better candidate to run early. The Planner can use this information to arrange the order step by step, while complex conditions and specific computational optimizations largely remain inside families.

Actual execution must also account for conditions already passed, the difference between complete inputs and sparse candidates, and limitations of existing combined implementations. This simple model aims for a reasonable, sufficient order; it does not promise a global optimum for every set of related conditions and every device.

## Estimation and Verification Serve the Actual Search

Estimating family costs should first help the search: give economical computations a sensible place in the order, and let new optimizations be used promptly. An approximate estimate with a sound basis is acceptable, and a missing cost estimate alone should not block a search that could otherwise run.

Condition probability, family survival rate, and ETA each have their own uses. How rare the full request is may differ from how many candidates a family can reject early. Estimating the remaining wait, meanwhile, requires considering the execution plan already chosen. ETA can keep improving, but it is first a reference; making it more precise should not require every combination of conditions and switches to be priced before it can run.

Seeds that survive the families' early filtering still need a final judgment against the full request and its declared premises. Only results that can be confirmed to satisfy it are marked confirmed; unconfirmed candidates retain their own status. Saving computation may affect which seeds we find. Deciding whether a found seed can be confirmed to meet the request is a separate responsibility.

Families give computation and optimization units around which progress can continue, and make these trade-offs possible to state clearly. As game mechanics and player needs continue to grow, I will extend and improve these units so that more requirements gradually become useful filtering conditions.
