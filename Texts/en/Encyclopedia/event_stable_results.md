# Stable Event Outcomes: Colorful Philosophers, Fake Merchant and Trash Heap

These predictions do not continue the random progress of earlier normal rewards or shops. They still require the same event, player slot and relevant candidates. “Stable” describes the outcome's premises; it does not guarantee the event appears.

## Colorful Philosophers: initial color offers

The game excludes your character from the unlocked character pools and offers at most three others. With all five vanilla characters unlocked, it randomly removes one of the four remaining characters. Any specified other character therefore appears with probability **3/4 (75%)**. If there are three or fewer eligible others, all are offered.

RT2 checks whether the target character is among these colors. Inputs are the seed, player slot, your character and unlocked pools. Earlier normal rewards, shops, deck and relic history do not advance this event draw. The actual cards generated after choosing a color belong to a later reward process and are not guaranteed by the color condition.

## Fake Merchant: inventory identities

Fake Merchant shuffles a fixed set of **9 fake relics** and sells the first **6**. Any specified fake relic has a **6/9 (2/3)** chance to appear. RT2 checks inclusion anywhere in the inventory.

Inventory uses event randomness rather than normal relic bags; earlier normal shops do not alter the six identities. Prices, however, use the player's `Shops` random stream and can affect its later progress. An inventory condition does not guarantee prices.

Vanilla Fake Merchant is single-player-only. Its inventory initializes randomness using the shared-event rule, but that does not make the event eligible in multiplayer.

## Trash Heap: a card or a relic

The card branch chooses one of **10 fixed cards**, giving each a **10%** chance. The relic branch chooses one of **5 fixed relics**, giving each a **20%** chance.

The card branch does not use normal card-reward rarity rolls or select anew from your character pool. The relic branch does not consume the Common, Uncommon or Rare relic bags.

A draw occurs only when you choose a branch. RT2 predicts both alternatives separately from the same initial Trash Heap random state. You can compare what taking a card or a relic would yield without first executing the other branch.

## Whose outcome in multiplayer?

Colorful Philosophers and Trash Heap include the player slot in their event random starting point, so players can get different outcomes from one seed. Shared events follow a different starting-point rule; see [[multiplayer-shared|Shared party conditions]]. See [[event-results|Event outcomes]] for other available filters.
