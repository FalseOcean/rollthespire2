# Multiplayer: One Seed and the Party's Choices

Multiplayer is difficult for me to test comprehensively, and it cannot simply be split into several independent single-player calculations. Everyone shares a seed. Some results belong to the common world, some belong to individuals, and others change because of an earlier player's actions. A party search seeks to satisfy everyone's conditions **in the same seed and the same run**.

## Give RT2 each player's context first

Experienced players often play with newcomers. Locked content can change candidate pools and predictions; one fully unlocked player does not make everyone's personal card pool identical. RT2 therefore provides a way to read each player's unlock data by slot from the connected lobby. **You still set characters and the run's shared Ascension manually.** Read and check the data again when the roster or slots change.

You can also explicitly assume full unlocks, provided actual play follows that premise. “Full unlocks” means every vanilla Epoch is unlocked; discovered Acts and seen encounters are separate facts. P1, P2 and so on correspond to the current lobby slots and query positions. Moving the same character to another slot can change personal results.

## Three categories and shared Niche

Party conditions broadly fall into three categories. The first is explicitly common: boss and Ancient identities, for example, only need setting once. The second is personal: some rewards, shops and ordinary event outcomes use each player's random state or inputs and need individual settings.

The third category causes me the most trouble: **all players share one Niche random stream**. An earlier player advances it, and the next continues from there; they cannot each start from the beginning. Random transformations from New Leaf and Pandora's Box both use it. Kaleidoscope, among the currently connected multiplayer Neow options, also uses Niche to order other characters' card pools.

Consider two Ironclads taking New Leaf to transform one starter card each. Suppose a particular seed under the relevant premises produces Offering on the first transformation and Iron Wave on the second. Whoever completes the first transformation gets the first result. Reversing the actions may exchange who receives those results. The card names are a hypothetical illustration. Even matching characters may have different unlocks, input cards or transformation pools, so reversing order need not simply exchange two output cards.

Darv's Pandora's Box presents the same issue. It transforms the removable Basic Strikes and Basic Defends in the current deck, in sequence. If both players obtain it, the first consumes a stretch of Niche before the second. Reversing acquisition can change their results. Other Niche consumption before reaching Darv also changes the starting point for these transformations.

## Why agree on complete acquisition order?

I could simplify things by forbidding every multiplayer result filter involving Niche. Instead, I want to retain that ability: an agreement players can follow gives us more demands we can express.

For the multiplayer opening acquisitions it currently supports, RT2 uses **P1 → P2 → P3 → P4** slot order. One participating player completes the entire agreed sequence, including reward choices or skips and automatic effects, before the next begins; everyone else waits. Starting the next player after only the first click may still interleave random consumption. RT2 does not invent acquisitions for players with no declared participation.

Multiplayer Neow transformations such as New Leaf, including their acquisition premises, are not yet connected, and the cross-source Transform Combination editor remains single-player only. Some individual multiplayer event transformations can be predicted under their own premises; see [[multiplayer-shared]].

Observing initial offers does not require obtaining anything. But having no Niche result condition does not make all order irrelevant: shared relic bags may also change because of earlier players' acquisitions.

RT2 attempts to execute multiplayer requests. Its default correctness promise still covers vanilla single-player with every Epoch unlocked and the declared operations followed; see [[default-environment]] for the specific environment.
