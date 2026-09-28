# How Do Shops Consume Relic Queues?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

A normal initial shop inventory contains **three relic slots**:

> The first two roll Common, Uncommon, or Rare;
> the third always requests Shop rarity.

All three take the actual relic identity from the **back** of the corresponding player bag.

# Ordinary Relics: Shops Take from the Other End

RT2 displays Common, Uncommon, and Rare queues **front to back**. Many ordinary sources also retrieve from the front, but shops retrieve from the back.

For a Rare bag:

> A → B → C → D → E

a front-taking source first sees A. A shop that rolls Rare starts looking from E.

**The same bag can be consumed from both ends.** A position in the Rare queue is therefore not directly equivalent to an ordinal among all Rare relic pickups in the run.

# Shops Skip Relics That Cannot Be Sold

A shop does not unconditionally take the last relic. It checks whether that relic is allowed in shop inventory. If not, it searches backward for the next legal one.

In base-game 0.111.0, explicit exclusions include:

> {{relic:LUCKY_FYSH}}
> {{relic:AMETHYST_AUBERGINE}}
> {{relic:BOWLER_HAT}}
> {{relic:OLD_COIN}}
> {{relic:THE_COURIER}}

The first four directly concern gold income; {{relic:THE_COURIER}} changes discounts and restocking.

**Normal shops skip these even if they are at the back of the bag.**

# Shop Relics Also Come from the Back

The third slot always requests **Shop rarity**, then reads from the back of the Shop bag.

RT2 already displays the Shop queue in consumption order with shop-ineligible relics filtered out. Shop position 1 is therefore the first Shop relic a normal shop can take.

# What Happens After Retrieval?

**The retrieved relic is removed from the current bag.** Retrieving its identity does not make another random choice.

For the first two slots, randomness happens earlier:

> Roll Common / Uncommon / Rare
> → Search from that bag's back for the first sellable relic

Rarity selection and identity retrieval are separate steps.

# The Courier Is a Special Case

With **{{relic:THE_COURIER}}**, buying a relic triggers a replacement:

> Roll Common / Uncommon / Rare again
> → Continue taking from the back of the corresponding ordinary bag

This does not consume another Shop-exclusive relic.

The Shop queue describes **normal shops' fixed Shop-rarity slots**, not an endless Courier restock sequence.

Remember:

> **Ordinary rewards often take from the front of Common, Uncommon, and Rare bags.**
> **Normal shops search from the back for the first sellable relic.**
> **Fixed Shop relics also come from the back of their bag.**

A bag can thus have both a front-consumption history and a back-consumption history, which complicates a full timeline of relic acquisition.
