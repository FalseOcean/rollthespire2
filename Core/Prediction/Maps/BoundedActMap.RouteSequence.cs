namespace RolltheSpire2.Core.Prediction.Maps;

internal sealed partial class BoundedActMap
{
    // A Final-only adapter for the existing pure route matcher. Replay, pruning,
    // postprocess, child ordering and RNG ownership remain in the numerical generator.
    internal (MapNode Start, MapNode Boss) SnapshotFinalRouteGraph(CancellationToken cancellationToken = default)
    {
        if (!_complete) throw new InvalidOperationException("Route matching requires a complete Final replay.");
        return SnapshotRouteGraph(cancellationToken);
    }
    // Private checkpoint adapter for bounded parity checks; never an Exact witness.
    internal (MapNode Start, MapNode Boss) SnapshotAssignmentRouteGraph(CancellationToken token = default)
    {
        if (_complete) throw new InvalidOperationException("Expected private Assignment checkpoint.");
        return SnapshotRouteGraph(token);
    }
    private (MapNode Start, MapNode Boss) SnapshotRouteGraph(CancellationToken cancellationToken)
    {
        var nodes = new MapNode?[Slots];
        for (int slot = 0; slot < Slots; slot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Active(slot)) nodes[slot] = new MapNode(_col[slot], slot / 7)
            {
                PointType = (MapPointType)_type[slot],
                CanBeModified = (_modifiable & Bit(slot)) != 0
            };
        }
        for (int slot = 0; slot < Slots; slot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes[slot] is not { } node) continue;
            for (int lane = 0; lane < _degree[slot]; lane++)
                node.Children.Add(nodes[_children[slot * 7 + lane]] ??
                    throw new InvalidOperationException("Final map edge points to an inactive node."));
        }
        return (nodes[Start]!, nodes[_boss]!);
    }
}
