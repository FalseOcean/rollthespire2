# {{relic:KALEIDOSCOPE}}: Two Groups of Other-Character Cards

{{relic-icon:KALEIDOSCOPE}} generates **two card reward groups** at the opening. Each contains one card from each of three other characters' pools. You may take at most one card from each group or skip it. In the base game, all character card pools must be unlocked for it to be eligible at Neow or inside Bones.

## Generating the Cards

For each group, **Niche RNG** shuffles other-character pools and selects three. The player's **Rewards RNG** generates their cards, rolling rarity before identity within the relevant character and rarity pool. Both groups are generated before the reward screen appears. Earlier Bones effects using either random state require continuation from its advanced position. See [[why-predictable]].

A specified Rare card requires the character pool, Rare roll, and card identity to match. Regular generation has a base Rare rate of **3% at A0–A6** and **1.49% at A7+**; these are not fixed odds for every reward source and state. Two specified Rare cards also need to fit the two-group pickup limit. The opening can be extremely rare while each seed still needs only a short replay.

## Writing the Filter

RT2 supports specified cards and, when needed, the order of the two groups. Two targets must be obtainable from separate groups; seeing two cards in one group does not mean you can take both.

**Specified cards are commitments to take them; unspecified groups are skipped.** One target means taking it and skipping the other group. RT2 does not automatically choose another card to improve later results. If {{relic:WHETSTONE}} or {{relic:WAR_PAINT}} follows, the changed deck can affect Bones' final curse; see [[neow-bones-curse]].
