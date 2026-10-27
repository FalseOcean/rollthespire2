# Bones' Curse and Your Actual Choices

Asking {{relic-icon:NEOWS_BONES}} for a particular curse, such as {{card-icon:GUILTY}}, is straightforward to express. Combine that requirement with earlier relics and card picks, however, and an unusual question appears: taking one extra card can even change the final curse.

This was one starting point for my thinking about assumptions, choices, and judgments. I do not want players to fill in a long list of actions before searching. But without asking what they intend to choose, what should “this seed meets your requirements” mean? A rare opening makes the dilemma concrete.

## What Happens If You Take Another Skill?

Suppose you require Bones to contain {{relic-icon:LARGE_CAPSULE}} and {{relic-icon:KALEIDOSCOPE}}, the Capsule to produce {{relic-icon:WAR_PAINT}}, Kaleidoscope to offer a particular Skill, and Bones to finish with Guilty. RT2 needs a seed where these requirements can hold together in one opening.

Suppose a candidate uses this pickup order: take Kaleidoscope and the specified Skill, then take Large Capsule. Kaleidoscope has two reward groups; normally you can take one card from each or skip either group. With only one card specified, RT2 calculates taking it and skipping the other group. Large Capsule then directly obtains two relics in sequence. Among them, War Paint immediately upgrades up to two upgradable Skills in the current deck. Bones draws its final curse after both internal relics and their effects have been resolved.

During actual play, though, you might see another appealing Skill in Kaleidoscope's other group and take that as well. The game allows this, but it adds a choice your search conditions did not express.

The key is that War Paint shuffles all upgradable Skills in the current deck, then upgrades the first two. That shuffle uses the random stream named Niche. If the extra Skill is unupgraded and can still be upgraded, it joins the candidate set and may make the shuffle consume more random numbers.

Bones uses that same Niche stream for its final curse. It now reads a different position, so the predicted Guilty may become another curse. The curse **may change**; taking another card does not necessarily cost you Guilty. But your extra choice has affected the process that determines the final curse.

## Why Does RT2 Treat Requirements as Choices?

This leaves me with two approaches to seed searching. One is to find seeds that satisfy the requirements however the player chooses. That gives the player more freedom, but requires checking many combinations of card picks and their consequences. For a tool intended to search quickly, that computation is expensive.

The other is the approach RT2 takes: **treat your search conditions as the choices you intend to make.** Specify this Skill from Kaleidoscope, and calculation continues after taking it; the optional card from the unspecified group is skipped. If you want a second card too, that choice also needs to be expressed. This avoids separately asking whether you will take something from every reward group, while giving later calculations a definite deck to work from.

“If you do not do it, RT2 will not do it for you” means RT2 will not invent optional actions you have not expressed. Consequences that necessarily follow from a declared pickup still belong in the calculation: after taking War Paint, its automatic upgrades and random consumption are part of that opening. Otherwise, search would produce several individually correct-looking results that the player could not obtain together by following the same choices.

The situation is rare, but the tradeoff runs through RT2: I want to make more requirements searchable while asking players for as few additional inputs as possible. Treating requirements as choices is one way to balance the two, and gives each result premises the player can follow. See [[neow-offer]] for pickup order, [[neow-kaleidoscope]] for reward groups and picks, and [[neow-capsule]] for Capsule acquisition.
