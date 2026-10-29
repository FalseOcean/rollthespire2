# RolltheSpire2

English | [简体中文](README.zh-CN.md)

**Find the seed you want, then see what it has in store.**

RolltheSpire2 (RT2) is an in-game seed search and analysis mod for **Slay the Spire 2**. Combine your desired conditions, search for matching seeds, and inspect their predicted openings, rewards, maps and more.

This source tree is **1.4.0**. The primary tested environment is **Windows, game version 0.111.0**.

## What you can do

- Search for Neow starts, shop contents, Bosses, Ancient options, events, relics, combat rewards, map conditions and transformation combinations.
- Configure single-player or **2–4 player** searches, with personal conditions and shared party conditions.
- Inspect a seed's predicted results, preview maps, draw routes and browse routes tied for the best value of a selected metric.
- In single-player runs, draw a route on the current Act's map to forecast events, encounters and rewards from the live state, then adjust choices along the route.
- See condition rarity, search speed and the estimated time for **one result**, with CPU/GPU search selected automatically where supported.
- Keep favorite seeds with titles and notes, reuse search presets and revisit persistent search history in the Library. Create card collections across characters to exclude unwanted combat card rewards or highlight cards in route forecasts. Developer picks provide ready-to-use seeds.
- Click supported events in Seed Analysis to see their predicted outcomes. Read the reorganized in-game encyclopedia in English or Chinese.
- At Crystal Sphere, use the optional in-run shortcut to read the current event state, explore compatible rewards and get reveal guidance, including Driftwood rerolls and optional enchantment targets.

## Download and get started

[Steam Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3755166888)

Manual packages and matching source snapshots are available from [GitHub Releases](https://github.com/FalseOcean/rollthespire2/releases). Older versions will be kept as frozen downloads, without ongoing maintenance.

For a manual installation package, place its `RolltheSpire2` folder inside the game's `mods` folder. Source archives need to be built first.

1. Open **RolltheSpire2** at the upper left of the game's main menu.
2. Choose single-player or multiplayer, set your characters and ascension, and add the conditions you want.
3. Start searching. Open a result in **Seed Analysis**, or enter a seed there directly.

Enable **Show tools button** in Settings to show the in-run dice entry for the Crystal Sphere assistant and current-seed information. Crystal Sphere supports the local player in single-player or multiplayer, three, six or remaining reveals, and card upgrades. Include enchantments and the translucent mask are separate switches, both off by default. Operation prediction and map route forecasting have their own enable switches; showing the tools entry does not enable all prediction features or simulate a complete run.

Operation prediction, the in-run tool entry, the console and map drawing prediction are off by default for new configurations. Existing saved switches are retained. The interface uses the current blue-ink theme; the old color editor and saved palettes have been retired.

The in-game encyclopedia explains individual conditions and prediction assumptions. Settings lets you change the language and font, adjust the history limit and search starting point, and open the log folder. History defaults to 30 recent queries with 30 distinct results each; preset-linked records are retained.

## Multiplayer and compatibility

Multiplayer assumes full unlocks by default; you can explicitly read the actual lobby context. Seed Analysis has its own multiplayer configuration. Its Neow pickups follow **P1 → P2 → P3 → P4**, including the displayed default choices, so earlier choices can affect later players' predictions.

Multiplayer transformation combinations are not included. In-run route forecasting covers the current Act, follows vanilla rules and ignores mod-added relics and effects. Drawing a route does not simulate every action in a complete run.

Map filtering uses full evaluation by default. An optional lossy prefilter on the map page supports CPU/GPU execution for standard vanilla single-player maps: lower and upper bounds for all nine map properties, multiple properties together, and combinations with grouped Monster, Unknown-room, Shop, Elite and Rest-Site route requirements. Supported three-Act daily totals retain their original meaning. It can miss matching seeds; surviving candidates still receive full map evaluation, and the Exact-validation setting remains independent. Other conditions retain full evaluation. Advanced parameters are in Settings.

Game updates and mods that change game rules can affect prediction accuracy. Vanilla single-player has the most testing; combinations of multiplayer, mods and partial unlocks still need broader coverage. Rarity and ETA are estimates, and complex queries may have incomplete estimates.

GPU initialization failures show their stage and reason and offer an explicit retry. Better diagnostics do not guarantee compatibility with every GPU, driver or rendering backend.

The optional candidate-output mode is off by default and skips final Exact validation. With normal validation enabled, Exact can still return `Unknown` or `Unsupported` without a proven mismatch; these results are also delivered as explicitly unverified. Only a final `Match` confirms the modeled conditions. Unverified results can be checked individually later, but another check may remain inconclusive. Actual checking exceptions remain pending failures with their original cause, rather than ordinary unverified results or completed target progress.

## Feedback

Open **Feedback** in RT2 to export a diagnostic ZIP and open a prefilled [GitHub Issue](https://github.com/FalseOcean/rollthespire2/issues/new). Add a description and reproduction steps, attach the ZIP, then submit. You can also copy the report and email **rollthespire2@outlook.com**.

Workshop and Bilibili comments are better suited to discussion and simple questions. RT2 is a personal hobby project; a timely reply or fix cannot be guaranteed for every report.

## Build from source

You need the **.NET 9 SDK** and a local Slay the Spire 2 installation. See [BUILDING.md](BUILDING.md) for dependencies, build commands and output locations. Game binaries are not included in the source distribution.

## Contributors & acknowledgements

- **James Zhou (@JamesZhou233)** shared GPU execution and data-transfer optimization ideas, implementations and comparison evidence, helping improve batch submission, buffer reuse and candidate transfer. RT2 independently adapted the ideas; the implementation in [PR #4](https://github.com/FalseOcean/rollthespire2/pull/4) was not merged.
- **Egoistic今宵 (@shion0528)** shared CPU/GPU filtering and opening-simulation optimizations with validation material, helping improve CPU batch prescreening, computation reuse, reward-pool preparation and candidate-state reuse. See [resource.17](https://github.com/shion0528/rollthespire2/releases/tag/v1.3.5-resource.17).

## License

RolltheSpire2's original source code and accompanying project documentation are licensed under [MPL-2.0](LICENSE), except where otherwise noted. When distributing a modified version, provide the corresponding MPL-covered source, including your modifications. Independent files containing no MPL-covered code may use other licenses.

This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

Game and third-party materials retain their respective rights and licenses. RT2 is a community project and is not affiliated with Mega Crit.

See [Third-party sources and notices](THIRD_PARTY_NOTICES.md) for algorithm references, artwork and screenshot provenance, and attribution still being confirmed.
