# Map Generation: Prune and Repair

> Applies to: **Slay the Spire 2 Beta 0.111.0**

STS2 does not obtain a finished standard map from one random step. The process is roughly:

> **Generate routes → Assign rooms → Prune → Repair → Final cleanup**

# First Generate Routes and Rooms

The game creates a network from the start to the Boss. Paths can branch and merge again.

Nodes then receive normal combat, Elite, Rest Site, Shop, Unknown, or other room types. Some positions have fixed rules, including the opening combat, the pre-Boss Rest Site, and the treasure floor.

The map now resembles the final result, but is not finished.

# Prune: Remove Duplicate Routes

The initial map may contain nearly identical segments: paths split at the same point, pass through the same sequence of room types, and merge again.

The game removes redundant routes. **Prune** deletes actual nodes and connections, changing which paths are available rather than merely cleaning up the display.

# Repair: Restore Lost Room Types

Prune can remove special rooms already assigned. For example, five Elites may become four.

The game checks whether **Shop, Elite, Rest, and Unknown** counts decreased. When a count is short, it selects normal combat nodes that can still be changed and converts them into missing room types.

That is **Repair**. A node that was a normal combat before Prune may end up an Elite, Rest Site, Shop, or Unknown.

# Why Does This Matter?

**An intermediate map is not the final map.** Finding a maximum of three Elites before Prune does not establish the same maximum afterward.

Repair may turn normal combats into Elites, while Prune has already changed route structure.

An optimization that generates only part of the map therefore faces three approaches:

> **Judge the intermediate map directly:** fast, but may miss seeds rescued by Repair.
> **Reject early only when failure on the final map is already provable:** safer, but rejects fewer seeds early.
> **Finish Prune and Repair before checking:** uses the final map, with the most complete computation.

This makes map-search performance more complicated than simply counting Elites.

Current formal map conditions are evaluated against **the final standard map after Prune and Repair**.
