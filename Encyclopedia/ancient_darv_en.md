# Darv: The Shared Ancient Pool and Ancient Relics

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Darv is currently the base game's only **shared-pool Ancient**. The two usual questions are:

> **Can I meet Darv in this run?**
> **Which Ancient relics will he offer?**

Both his appearance rules and his relic generation are distinctive.

## What Makes Darv a Shared-Pool Ancient?

Acts 2 and 3 each have local Ancients. Darv does not belong permanently to either Act. He starts in a separate **shared Ancient pool**, which contains only him in base-game 0.111.0.

World generation processes the later Acts in order:

> Decide whether to assign Darv to Act 2;
> if he remains unassigned, decide whether to assign him to Act 3.

With full unlocks, he may be assigned to Act 2, assigned to Act 3, or remain unassigned. Once assigned, he is unavailable to later Acts.

## Assignment Is Not an Encounter

If assigned to Act 2, Darv joins its original pool of Orobas, Pael, and Tezcatara as a fourth candidate. Only afterward does the game select this Act's actual Ancient.

Assignment gives Darv eligibility; the identity draw must still select him.

In the default fully unlocked environment:

> Assigned to Act 2: **1/2**
> Assigned to Act 3: **1/4**
> Assigned to neither: **1/4**

After the Act's identity selection:

> Actually meet Darv in Act 2: about **1/8**
> Actually meet Darv in Act 3: about **1/16**

He is therefore naturally more common in Act 2 in this version.

# Darv's Ancient Relic Pool

On entering Darv's encounter, the game builds the currently legal candidates. Seven base candidates can appear in both Acts:

> {{relic:ASTROLABE}}
> {{relic:BLACK_STAR}}
> {{relic:CALLING_BELL}}
> {{relic:EMPTY_CAGE}}
> {{relic:PANDORAS_BOX}}
> {{relic:RUNIC_PYRAMID}}
> {{relic:SNECKO_EYE}}

{{relic:PANDORAS_BOX}} has an additional restriction: it is excluded if the current Modifier clears the player's deck.

## Different Candidates in Acts 2 and 3

Act 2 adds:

> {{relic:ECTOPLASM}}
> {{relic:SOZU}}
> {{relic:PHILOSOPHERS_STONE}}
> {{relic:VELVET_CHOKER}}

This normally gives **11 regular candidates**.

In Act 3, {{relic:ECTOPLASM}} and {{relic:SOZU}} are no longer legal, while {{relic:PHILOSOPHERS_STONE}} and {{relic:VELVET_CHOKER}} remain. That normally gives **9 regular candidates**.

A specified Ancient relic therefore has different appearance probabilities in the two Acts.

## Each Candidate Set Currently Contains One Relic

Darv's code organizes candidates into `ValidRelicSet` collections. In base-game 0.111.0, each contains **one relic**.

The {{relic:ASTROLABE}} set can only produce {{relic:ASTROLABE}}, the {{relic:BLACK_STAR}} set only {{relic:BLACK_STAR}}, and so on.

The later **shuffle** is the main random step determining the offer. However, the game still makes a random-selection call for each one-member set. The identity is unchanged, but Darv's RNG advances.

Those seemingly nonrandom random calls cannot be omitted when predicting a particular seed.

## The Offer Is Not Simply the First Three

After shuffling all legal candidates, the game makes a `50 / 50` choice:

> **Take the first three Ancient relics.**

or:

> **Take the first two and add {{relic:DUSTY_TOME}}.**

Darv always offers three options, either three regular candidates or two regular candidates plus {{relic:DUSTY_TOME}}.

## {{relic:DUSTY_TOME}} Is Added Separately

{{relic:DUSTY_TOME}} does not participate in the regular shuffle. It is not shuffled alongside eleven other relics hoping to reach the top three.

**A final 50% branch decides whether it occupies the third position.** Its appearance probability is **1/2**.

This imposes clear combination limits. {{relic:DUSTY_TOME}} and two specified regular Ancient relics can appear together. {{relic:DUSTY_TOME}} and three regular Ancient relics cannot: only two regular positions remain when {{relic:DUSTY_TOME}} appears.

## How Common Is a Specified Regular Relic?

Let `N` be the number of legal regular candidates.

Without {{relic:DUSTY_TOME}}, a specified relic reaches the top three with probability **3 / N**. With {{relic:DUSTY_TOME}}, only two are kept, giving **2 / N**.

The branches are equally likely, so the overall probability is **5 / (2N)**:

> Act 2, `N = 11`: about **22.7%**
> Act 3, `N = 9`: about **27.8%**

The same regular Ancient relic is thus more likely to be offered in Act 3.

## {{relic:DUSTY_TOME}} Has Another Result Layer

When {{relic:DUSTY_TOME}} enters the offer, its subsequent content is prepared for the current player using the player's Rewards RNG and the current legal Ancient card pool.

“Does Darv offer {{relic:DUSTY_TOME}}?” and “What does {{relic:DUSTY_TOME}} contain?” remain separate layers.

## What Can RT2 Filter?

RT2 can require Darv in a specified Act, a particular Ancient relic among his three options, or several specified relics in the same offer.

His 0.111.0 structure can be remembered as:

> **Assign Darv to an Act;**
> **select whether that Act actually uses him;**
> **shuffle the current 11 or 9 legal regular candidates;**
> **with 50% probability, replace the third position with {{relic:DUSTY_TOME}}.**
