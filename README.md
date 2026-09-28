# RolltheSpire2

English | [简体中文](README.zh-CN.md)

**Find the seed you want, then see what it has in store.**

RolltheSpire2 (RT2) is an in-game seed search and analysis mod for **Slay the Spire 2**. Combine your desired conditions, search for matching seeds, and inspect their predicted openings, rewards, maps and more.

This source tree is version **1.3.0**. The primary tested environment is **Windows, game version 0.111.0**.

## What you can do

- Search for Neow starts, shop contents, Bosses, Ancient options, events, relics, combat rewards, map conditions and transformation combinations.
- Configure single-player or **2–4 player** searches, with personal conditions and shared party conditions.
- Inspect a seed's predicted results, preview maps, draw routes and browse routes tied for the best value of a selected metric.
- See condition rarity, search speed and the estimated time for **one result**, with CPU/GPU search selected automatically where supported.
- Save search presets and favorite seeds with their context and notes. Read the in-game encyclopedia in English or Chinese.

## Download and get started

[Steam Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3755166888)

Download `RolltheSpire2-1.3.0.zip` from the matching GitHub Release. The manual package contains the DLL and manifest; no PCK is required. Avoid loading duplicate Workshop and manual copies. Older versions will be kept as frozen downloads, without ongoing maintenance.

For a manual installation package, place its `RolltheSpire2` folder inside the game's `mods` folder. Source archives need to be built first.

1. Open **RolltheSpire2** at the upper left of the game's main menu.
2. Choose single-player or multiplayer, set your characters and ascension, and add the conditions you want.
3. Start searching. Open a result in **Seed Analysis**, or enter a seed there directly.

The in-game encyclopedia explains individual conditions and prediction assumptions. Settings lets you change the language, open the log folder and adjust the search starting point.

## Multiplayer and compatibility

Multiplayer assumes full unlocks by default; you can explicitly read the actual lobby context. Seed Analysis has its own multiplayer configuration. Its Neow pickups follow **P1 → P2 → P3 → P4**, including the displayed default choices, so earlier choices can affect later players' predictions.

Multiplayer transformation combinations are not included. Map filtering currently uses the CPU; drawing a route does not simulate every action in a complete run.

Game updates and mods that change game rules can affect prediction accuracy. Vanilla single-player has the most testing; combinations of multiplayer, mods and partial unlocks still need broader coverage. Rarity and ETA are estimates, and complex queries may have incomplete estimates.

## Feedback

Report problems in the Workshop comments or email **rollthespire2@outlook.com**. A seed, the selected conditions, game/mod versions and a relevant log make bugs much easier to investigate. You can open the RT2 log folder from Settings.

## Build from source

You need the **.NET 9 SDK** and a local Slay the Spire 2 installation. See [BUILDING.md](BUILDING.md) for dependencies, build commands and output locations. Game binaries are not included in the source distribution.

## License

RolltheSpire2's original source code and accompanying project documentation are licensed under [MPL-2.0](LICENSE), except where otherwise noted. When distributing a modified version, provide the corresponding MPL-covered source, including your modifications. Independent files containing no MPL-covered code may use other licenses.

This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

Game and third-party materials retain their respective rights and licenses. RT2 is a community project and is not affiliated with Mega Crit.

`Localization/game_content/*.json` contains game content names, which retain their respective rights and are not relicensed as original RT2 content.
