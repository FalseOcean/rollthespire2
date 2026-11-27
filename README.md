# RolltheSpire2

English · [简体中文](README.zh-CN.md)

**Find the seed you want, then see what it has in store.**

RolltheSpire2 (RT2) is an in-game seed search and analysis mod for **Slay the Spire 2**. Search for a seed that matches what you want to play, inspect predictions for an existing seed, or plan your reveals with the Crystal Sphere assistant.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3755166888) · [Download the latest release](https://github.com/FalseOcean/rollthespire2/releases/latest)

On Windows, the same RT2 installation automatically selects the rules for stable **0.111.1** and beta **0.114.0**.

**RT2 1.4.12**　Improved seed search performance and brought common controls together in Search settings on the right side of the search page.

## Seed search

Looking for a specific Neow start, a combination of relics and cards, or particular shops, events, or maps? Combine your conditions in-game and let RT2 search for matching seeds.

RT2 supports single-player and **2–4 player** searches. Load the current multiplayer lobby in one step or set up the party manually, with personal conditions for each seat and shared conditions for the party.

RT2 selects CPU or GPU search based on your query and hardware. See how rare the conditions are, how fast the search is running, and, where an estimate is available, how long it may take to find one result. Save frequently used conditions as presets.

Choose single-player or multiplayer on the search page, set the characters and ascension, add your conditions, then start the search. Open **Search settings** at the upper right to adjust the result target, CPU/GPU mode, execution order, GPU batch limit, CPU threads, or search starting point.

To keep a search running after closing the panel, turn on **Enable persistent search** in RT2’s main Settings. Start the search from the main menu. It continues at reduced load while you play. Open RT2 during a run to check progress and results or stop the search.

## Seed Analysis

Open **Seed Analysis** from a search result or enter an existing seed directly. Inspect predicted opening choices, maps, shops, events, and combat rewards. Save a seed with its configuration and notes in the Library to come back to it later.

## Crystal Sphere assistant

Enable **Show tools button** in RT2 Settings. When you reach Crystal Sphere, select the Bones icon near the upper-right run information. The assistant reads the current event state so you can pick the rewards you want, compare gold plans, and see which tiles to reveal.

## Install and get started

### Steam Workshop

1. Open the [RolltheSpire2 Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3755166888) and select **Subscribe**.
2. Wait for Steam to finish downloading, then launch the game. If the game is already running, exit and relaunch.
3. Open **Mods** from the game's main menu and enable **RolltheSpire2**. Restart if the game prompts you to.
4. Select **RolltheSpire2** at the upper left of the main menu.

### Manual installation from GitHub (Windows)

1. Exit the game. Open the [latest GitHub release](https://github.com/FalseOcean/rollthespire2/releases/latest) and download the `RolltheSpire2-<version>.zip` file under **Assets**. The `-source` ZIP and the **Source code** archives are for building the mod.
2. In your Steam Library, right-click **Slay the Spire 2** and select **Manage → Browse local files**. Find the `mods` folder in the game directory, or create it if needed.
3. Extract the ZIP and place its `RolltheSpire2` folder in `mods`. You should see `RolltheSpire2.dll` and `RolltheSpire2.json` directly inside `mods/RolltheSpire2`.
4. Launch the game and enable **RolltheSpire2** under **Mods** on the main menu. Restart if prompted, then open RT2 from the upper left of the main menu.

Steam updates Workshop installations. For a manual update, exit the game and replace the old RT2 folder in `mods` with the new one. Keep one copy of RT2 when switching methods. Move the manual copy out of `mods` before switching to the Workshop, or unsubscribe before switching to a manual installation.

### Reuse presets and search history

Open a preset in Library → Search presets and select **Copy share code**. To use a code someone sent you, copy it and select **Import from clipboard** to preview, use, or save the preset. The code carries the ordered roster, ascension, and active search conditions. You can search from the main menu with full unlocks for every seat.

In Library → Search history, select a search and choose **Reload and check saved seeds**. RT2 loads the conditions and checks each saved seed under the current rules. You can open or favorite the results. The original search record and scan position are preserved.

## Before you use RT2

- Multiplayer predictions match each player to their lobby seat. Reload the lobby after a player or seat changes. Some predictions depend on your actual choices or pickup order. See the in-game encyclopedia for the relevant rules.
- Game updates and mods that change RNG, card pools, events, or reward rules can affect search and prediction results. Supported conditions also differ between single-player and multiplayer.
- Search speed varies by conditions and hardware. Rarity and time estimates are approximate. Unverified candidates may fail your search conditions. The optional lossy map prefilter may miss matching seeds.

## Feedback

If you encounter a crash, a failed search, an unusual slowdown, or an incorrect prediction, open **Feedback** in RT2 and export a diagnostic ZIP. Open the prefilled issue, add a description and steps to reproduce the problem, and attach the ZIP manually before submitting. Issues and attachments are public, so review the contents before uploading. You can also email the diagnostic ZIP and a description to **rollthespire2@outlook.com**. [Open a GitHub issue](https://github.com/FalseOcean/rollthespire2/issues/new)

## Build from source

You need the **.NET 9 SDK** and a local Slay the Spire 2 installation. See [BUILDING.md](BUILDING.md) for dependencies, build commands, and output locations.

## Acknowledgements

- Thanks to **James Zhou (@JamesZhou233)** for GPU execution and data transfer ideas and comparisons that helped improve batch submission and buffer reuse. [PR #4](https://github.com/FalseOcean/rollthespire2/pull/4)
- Thanks to **Egoistic 今宵 (@shion0528)** for CPU/GPU filtering and opening simulation implementations and validation that helped improve batch prefiltering and computation reuse. [Research release](https://github.com/shion0528/rollthespire2/releases/tag/v1.3.5-resource.17)

## License

Unless otherwise noted, RT2's original source code and accompanying project documentation are licensed under [MPL-2.0](LICENSE). Game and third-party materials retain their respective rights and licenses. See [Third-party sources and notices](THIRD_PARTY_NOTICES.md).

This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

RT2 is an unofficial, fan-made mod and is not affiliated with Mega Crit.
