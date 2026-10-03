# Maps: How Does the Seed Determine Routes?

> Mechanic reference: Slay the Spire 2 Beta 0.111.0

RT2 can show whether an Act offers a desired route, such as one with more Elites, Rest Sites, or question marks, and how many rooms no route can avoid.

## Why Can the Map Be Known in Advance?

Standard maps are generated as a whole. Each Act has a dedicated RNG stream—`act_1_map`, `act_2_map`, or `act_3_map`—derived from the seed. With the Act and required run conditions established, nodes, room types, and connections are determined. Following a route in an earlier Act does not advance the next Act's standard-map RNG.

## Room Placement Rules

The game generates branching and merging paths, then assigns room types. The first floor is a normal combat, the last pre-Boss floor is a Rest Site, and there is a fixed treasure floor.

Other Rest Sites, shops, Elites, and question marks follow the Act's required counts and placement restrictions. Some cannot be placed too early or late, consecutively, or alongside the same type on adjacent branches. Generation then removes duplicate route segments and restores room types lost during that pruning.

## Act and Mode Differences

Under default single-player rules, Act 1 is longest, Act 2 shorter, and Act 3 shorter again. Ascension and multiplayer can alter generation parameters: Swarming Elites adds Elites, while multiplayer removes one floor.

In 0.111.0, {{act:OVERGROWTH}} and {{act:UNDERDOCKS}} use the same standard-map length and room-count rules, despite different Bosses, events, and enemies.

## Route Counts

**Reachable Max** is the most of a node type visited on any complete route. An Elite maximum of four establishes a route visiting four Elites. RT2 supports these conditions for Elites, Rest Sites, and question marks.

**Guaranteed** is the minimum across complete routes. A Guaranteed Elite count of two means every complete route visits at least two. Normal combats, Elites, Rest Sites, and question marks have corresponding guaranteed counts.

Different maxima can come from different routes. Four reachable Elites and five reachable question marks do not prove that one route contains both. See [[map-routes]] for comparison directions.

## Question Marks and Confirmed Routes

A map's `?` node can become an event, combat, chest, or shop. Its resolution uses separate persistent randomness and depends on earlier question marks, previous room results, route position, and consecutive-shop restrictions. Six question marks do not guarantee six events.

The map workspace lets you browse routes, find routes maximizing a node type, sketch a route, and confirm a complete path. A selected route establishes the order of question marks, previous room types, shop restrictions, and earlier Acts' Unknown RNG consumption. RT2 can then continue predicting ordinary question-mark room types. The specific event still requires its event queue and current eligibility; see [[event-appearance]].

## Effects That Modify Maps

Relics, cards, or modifiers can regenerate a map, replace its type, alter Elite counts, or add node markers. Current map filters target the standard map before additional map-changing effects; they do not pre-apply effects the player might obtain later.
