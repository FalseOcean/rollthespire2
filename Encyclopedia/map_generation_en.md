# Map Generation: Pruning and Repair

> Mechanic reference: **Slay the Spire 2 Beta 0.111.0**.

STS2 does not obtain a finished standard map from one random step. The process is roughly:

**Generate routes → Assign rooms → Prune → Repair → Final cleanup**

## First Generate Routes and Rooms

The game creates a network from the start to the Boss. Paths can branch and merge again.

Nodes then receive normal combat, Elite, Rest Site, Shop, Unknown, or other room types. Some positions have fixed rules, including the opening combat, the pre-Boss Rest Site, and the treasure floor.

The map now resembles the final result, but is not finished.

## Prune: Remove Duplicate Routes

The initial map may contain nearly identical segments: paths split at the same point, pass through the same sequence of room types, and merge again.

The game removes redundant routes. **Prune** deletes actual nodes and connections, changing which paths are available rather than merely cleaning up the display.

## Repair: Restore Lost Room Types

Prune can remove special rooms already assigned. For example, five Elites may become four.

The game checks whether **Shop, Elite, Rest, and Unknown** counts decreased. When a count is short, it selects normal combat nodes that can still be changed and converts them into missing room types.

That is **Repair**. A node that was a normal combat before Prune may end up an Elite, Rest Site, Shop, or Unknown.

## RT2 Uses the Finished Map

Map filters use the standard map after pruning, repair, and cleanup. A route with at most three Elites before pruning may gain an Elite through repair, so intermediate counts cannot replace the final route counts.
