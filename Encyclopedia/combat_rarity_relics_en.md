# Combat Rewards: Rare Cards and Reward-Changing Relics

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Common reward goals include:

> A specified Rare card in the first few combats;
> a target in the extra reward from {{relic-icon:PRAYER_WHEEL}};
> a specified potion guaranteed by {{relic-icon:WHITE_BEAST_STATUE}}.

These cannot be reduced to a fixed Rare chance per card. Normal combat rarity remembers earlier generated cards, while some relics change reward quantity or generation. One combat therefore affects the next.

# Rare Chance Is Not Fixed

A normal Monster card reward generates three cards. Each first receives a rarity—Common, Uncommon, or Rare—then an identity from that rarity.

The base Rare rate is:

> **A0–A6: 3%**
> **A7+: 1.49%**

The actual rate includes a changing **rarity adjustment**, which starts at **-5%**. At the very beginning, a Rare card is therefore impossible through this normal roll.

# The Longer Without a Rare, the Higher the Chance

Each **non-Rare card** increases the adjustment:

> **A0–A6: +1%**
> **A7+: +0.5%**

It can accumulate to **+40%**. Generating a Rare immediately resets it to **-5%**, then accumulation starts again.

> No Rare cards
> → Increasing chance
> → A Rare appears
> → Reset to -5%

Higher Ascension reduces both the base rate, from 3% to 1.49%, and accumulation speed, from +1% to +0.5% per card.

# The Adjustment Changes Within a Three-Card Group

It updates **after each generated card**, not just after the combat:

> First card → Update → Second card → Update → Third card

If the first is Rare, the next two immediately face the reset rate. If none is Rare, the accumulated increase carries into the next combat.

**Card rewards across combats form a continuous rarity history.**

# Prayer Wheel: An Entire Extra Reward Group

**{{relic:PRAYER_WHEEL}}** adds **another three-card reward group** after a normal Monster combat.

Instead of three cards, you see three ordinary reward cards plus three extra ones.

The extra group uses normal Monster card-reward rules. It reads the current rarity adjustment, generates Common / Uncommon / Rare cards, and updates the adjustment again.

Prayer Wheel therefore changes the later reward history as well as showing three additional cards.

# Lasting Candy: A Power That Ignores Accumulated Rarity

**{{relic-icon:LASTING_CANDY}}** tracks combat card rewards and, at the corresponding interval, adds **one Power** to the normal three cards. The group becomes **four cards**.

That fourth Power does not use normal combat rarity accumulation.

## It Can Be Rare Immediately

The extra Power uses the **base Rare rate**:

> A0–A6: **3%**
> A7+: **1.49%**

It does not read the accumulated adjustment. Even if the normal adjustment has just reset to -5%, making Rare impossible for the ordinary three cards, **the extra Power can still be Rare**.

## It Does Not Change Normal Rare Accumulation

The extra Power neither adds +1% / +0.5% when non-Rare nor resets the adjustment to -5% when Rare.

**It consumes Rewards to generate a Power while bypassing the normal rarity-accumulation system.**

Distinguish the position of Rewards RNG from the current normal-combat rarity adjustment. Lasting Candy changes the former, not the latter.

# White Beast Statue: Bypassing the Normal Potion Drop Roll

**{{relic:WHITE_BEAST_STATUE}}** affects potion rewards.

Normal potion drops also have continuous state. The initial chance is **40%**. Failure adds **10%** for next time; success subtracts **10%**. It is not an independent 40% roll every combat.

White Beast Statue does more than replace 40% with 100%. It **directly guarantees a potion reward**, skipping the normal drop check.

That combat consumes no original drop-check randomness and does not subtract 10% for the forced drop. It then rolls potion rarity and identity.

The relic changes both whether a potion appears and where Rewards continues afterward.

# When Do These Relics Participate in Prediction?

RT2 **does not automatically include a relic's reward effects merely because it could theoretically be obtained somewhere on the seed**.

First establish **whether it is actually obtained in the selected opening**. For example:

> {{relic-icon:SMALL_CAPSULE}} contains {{relic:PRAYER_WHEEL}};

or obtain {{relic-icon:LARGE_CAPSULE}} through {{relic-icon:NEOWS_BONES}} and require:

> {{relic:LASTING_CANDY}};

or require a Capsule to contain:

> {{relic:WHITE_BEAST_STATUE}}.

Final verification (Exact) follows the selected opening and continues combat rewards using the modeled reward effects of actually obtained relics.

Requiring {{relic:PRAYER_WHEEL}} at a future position in an ordinary relic queue does not mean the combat query assumes it is already owned.

Single-player early filtering (Fast) still includes only explicitly specified reward influences. Seeds that qualify only because of an unspecified Capsule relic may be missed before reaching Exact. Specifying the needed contents makes that premise available to early filtering.

Current multiplayer filtering replays actual Capsule draws and includes modeled held effects of the relics drawn, even when not individually specified. Held effects and immediate on-obtain effects are separate; see [[neow-capsule]] for the latter's scope.

# Why Distinguish Queues from Pickups?

“This seed contains this relic” and **“the player actually obtained it before the first combat”** are different facts. Combat rewards need the latter.

A queue position cannot replace actual opening acquisition. As with Neow:

> **Later rewards continue from the state produced by the selected opening.**

# {{card:THE_HUNT}}: Known Reward Rules, Unknown Combat Outcome

**{{card:THE_HUNT}}** provides a useful contrast. A successful Fatal adds **a three-card reward group** to that combat.

Once that group exists, it follows normal reward generation. The unresolved question is **whether Fatal succeeded in this combat**.

That depends on drawing the card, playing it, its target, and whether it actually kills. The current combat reward query does not predict combat play.

It therefore does not assume an extra group every combat simply because {{card:THE_HUNT}} is in the deck. C does not treat it as a stable reward modifier.

# Why Can a Relic Count When {{card:THE_HUNT}} Does Not?

The distinction is the certainty of the condition, not relics versus cards.

If {{relic:PRAYER_WHEEL}} was obtained during the opening, the next normal Monster combat has an extra card reward group. Merely having {{card:THE_HUNT}} in the deck does not establish a successful Fatal.

The former is known before combat; the latter depends on combat execution. Current prediction stops at:

> **Predict established reward rules without additionally simulating the entire fight.**

The three main points are:

> **Normal Rare chance accumulates: +1% per non-Rare card at A0–A6, +0.5% at A7+, resetting to -5% after a Rare.**
> **Lasting Candy's extra Power neither reads nor changes that adjustment, so it can be Rare at the base rate even when normal Rare chance is zero.**
> **Reward relics must actually be obtained in the selected opening; single-player Fast coverage of unspecified effects differs from Exact's final verification scope.**
