# Workspace fonts

Settings offers two persistent choices: `game-sans` (default, C: the game’s
NotoSansMonoCJKsc-Regular) and `game-serif` (B: SourceHanSerifSC-Medium/Bold).
Both load existing `res://fonts/zhs/` resources; RT2 redistributes no font files.
Missing, invalid and legacy `source-han` selections migrate to `game-sans`.
An explicit `game-serif` preference is retained. Color reset does not change fonts.

`MainPanelTheme.Fonts.cs` caches font wrappers and updates marked RT2 controls
and themes on selection/open, preserving UI instances and independent game/map
font boundaries. There is no periodic scan. Game resources are never modified.

Owner retired the embedded Source Han Sans option on 2026-10-19 after the native
font comparison. Its former asset and license remain recoverable in Git history
(introduced in `253e661`), not in the current package.
