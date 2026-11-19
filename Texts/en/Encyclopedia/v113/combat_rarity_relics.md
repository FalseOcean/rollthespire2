# Combat Rewards: Rare Cards and Reward-Changing Relics

Why are Rare cards so scarce in the first few normal combats, and why does picking up Prayer Wheel change what comes later? Each normal reward card changes the game's odds for the next one.

## The Rarity Adjustment

| Rule | A0–A6 | A7+ |
| --- | --- | --- |
| Base Rare rate for normal combat | 3% | 1.49% |
| Increase after each non-Rare card | +1 percentage point | +0.5 percentage points |
| Starting adjustment / reset after a Rare | -5% | -5% |
| Maximum adjustment | +40% | +40% |

For a normal reward card, the game rolls rarity first, then picks a card. The Rare rate is the base rate plus the current adjustment. Take A0 right after a reset. Adding -5% to 3% leaves the rate below zero, so a fresh group of three normal reward cards can't contain a Rare. Each non-Rare card adds one percentage point. After three cards, the next one has a 1% chance. If the first card of a group is Rare, the second and third use the reset adjustment right away. Skipping the group leaves those changes in place.

Bosses that give combat card rewards have their own rule. Their cards roll Rare outright and reset the adjustment to -5%.

## {{relic-icon:PRAYER_WHEEL}} and Lasting Candy

{{relic:PRAYER_WHEEL}} gives you a whole extra group of three cards after a normal Monster fight. The extra group follows the usual rules. It picks up where Rewards left off and reads and updates the same rarity adjustment.

When {{relic-icon:LASTING_CANDY}} triggers, it adds a Power to the group, taking it from three cards to four. In normal combat, that Power uses the base Rare rate, 3% at A0–A6 and 1.49% at A7+. It can roll Rare even when the ordinary three cards have no chance. It uses Rewards and leaves the normal rarity adjustment where it was.

{{relic:WING_CHARM}} picks one eligible reward card to enchant with Swift. If there's a card it can enchant, the choice uses your own Rewards stream, so the next reward starts from a different position.

## {{relic-icon:WHITE_BEAST_STATUE}} and Potion Drops

The potion chance in normal combat starts at 40%. Miss the first drop and the next chance is 50%. Get a potion in the first fight and the next chance is 30%. The chance keeps moving by ten percentage points after each roll.

{{relic:WHITE_BEAST_STATUE}} gives you a potion outright. The game skips the drop check and leaves the drop chance where it was. It still rolls the potion's rarity and identity.

## Which Effects Does RT2 Use?

Start with what you actually got from the chosen Neow opening. A {{relic-icon:SMALL_CAPSULE}} might yield {{relic:PRAYER_WHEEL}}. You might get {{relic-icon:NEOWS_BONES}}, take a {{relic-icon:LARGE_CAPSULE}}, and find {{relic:LASTING_CANDY}} in it. Or a Capsule might yield {{relic:WHITE_BEAST_STATUE}}.

Seed search applies the modeled reward effects of the relics you end up holding. In a single-player search, for instance, if a Capsule gives you Prayer Wheel, its extra reward counts even when you left the Capsule's contents unspecified. Rewards that depend on an opening effect RT2 can't establish stay unconfirmed. See [[neow-capsule]] for effects that trigger when you first take a relic.

Putting Prayer Wheel at a future position in the relic queue tells you about a later chance to get it. The early fights still use the relics you actually hold at the opening.

## {{card:THE_HUNT}} Depends on the Fight

{{card:THE_HUNT}} also adds a three-card reward when Fatal triggers. We know how the game would generate that group. Whether you draw The Hunt, play it and get the kill comes down to how you fight. RT2's combat reward filter calculates the sequence without this extra group.
