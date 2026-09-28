# Ancients: Identity and Options

## What You Want

Ancient filters usually address two requirements:

> **Meet a particular Ancient in this Act.**

> **Have that Ancient offer a specified option.**

You may also want several options to appear together. A complete query therefore asks **who you meet** and **what they offer afterward**.

RT2 presents these as one Ancient encounter in the UI and encyclopedia, although they originate from different random processes.

## Identity and Options Are Separate

An Ancient's **identity** belongs to world generation. Which Ancient appears in Acts 2 and 3 sits alongside maps, Bosses, and Act variants, and is predicted by RT2's **W**.

Once identity is known, the options offered belong to **A**:

> Seed
> → This Act's Ancient identity
> → Currently legal options for that Ancient
> → Generate the actual offer

> **W determines whom you meet.**
> **A determines what they offer.**

This is an internal computation boundary. To the player, both belong to the same encounter and fit naturally on one page.

## Why Is the Pool Not Always Fixed?

An Ancient has a set of possible options, but some have appearance requirements. Before generating an offer, the game must determine **which options are currently legal**.

Only eligible options enter the candidate set used for this offer.

“This option belongs to this Ancient” and “this option is eligible in the current run” are therefore different statements.

## Why Does RT2 Need Eligibility Premises?

Eligibility cannot always be derived from the seed alone. It may depend on current player state or history that RT2 has not fully reconstructed.

RT2 lets the player specify **which special options are available here and which are not**. These are premises used to generate the offer, not additional search targets.

If an option cannot appear in your actual state, mark it unavailable. RT2 then regenerates the offer with that eligibility set.

This differs from excluding an unwanted result afterward. Removing an option from the pool can also change other options' probabilities and combinations.

## One Option Versus Several Together

Requiring one specified option asks whether the offer contains it.

Requiring **A, B, and C in the same offer** is much stricter than each having some chance of appearing. Some options cannot occur together; others can, but rarely do.

RT2 distinguishes a target appearing from multiple targets being offered simultaneously. Advanced mode's simultaneous-offer condition checks **one actual Ancient offer**, not three independent possibility checks.

## Act 1 Is Special

Act 1's Ancient is Neow. From the player's perspective, Neow still belongs to the same overall Ancient question, so the UI can present the three Acts together.

Neow's rules are more complex: curse and positive branches, {{relic:NEOWS_BONES}}, {{relic:KALEIDOSCOPE}}, Capsules, and many immediate opening effects.

The Ancient page brings the three Acts into one player-facing question. Neow's detailed mechanics remain in [[neow]].

## What Does RT2 Do Today?

A query can specify:

> An Ancient identity in a particular Act;
> required options;
> multiple options offered together;
> eligibility premises;
> and necessary inputs for a few options with additional results.

RT2 determines the world identity, then generates the actual offer using those premises. It is not just a static list of possible options.

The question is:

> **For this seed, this Ancient, and these eligibility conditions, what is actually offered?**

First determine whom you meet, then what they offer. Where eligibility matters, also ask whether an option may enter the pool at all.

Identity belongs to the world; options belong to the Ancient's generation rules. Together they answer **what this encounter will be like**.

See [[ancient-darv]] for a shared-pool Ancient, [[ancient-pael]] for eligibility and weights, [[ancient-tezcatara]] for three separate positions, and [[ancient-orobas]] for special branches and character targets. Act 3's local Ancients are covered in [[ancient-act3]].
