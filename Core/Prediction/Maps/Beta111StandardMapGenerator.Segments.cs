namespace RolltheSpire2.Core.Prediction.Maps;

internal static partial class Beta111StandardMapGenerator
{
    private readonly record struct GraphSegmentKey(MapNode Start, MapNode End, UInt128 Types);

    // Discover the same segments without enumerating every complete Start-to-Boss
    // path. A segment's key includes its start coordinates, so different starts
    // cannot affect each other's retained lists. For one start, depth-first suffix
    // traversal encounters unique segments in exactly the reference traversal's
    // first-occurrence order; complete-path prefixes and extensions only repeat
    // those segments. Such repeats always overlap an already retained segment.
    // Keep ordinal string-key sorting and retained segment order: both determine
    // the later pruning shuffles and therefore the RNG continuation/final graph.
    private static SortedDictionary<string, List<MapNode[]>> FindGraphSegmentDictionary(
        MapNode start, ReferenceRng rng)
    {
        var groups = new Dictionary<GraphSegmentKey, List<MapNode[]>>();
        var reachesBoss = new Dictionary<MapNode, bool>();
        var starts = new List<MapNode>();

        bool ReachesBoss(MapNode node)
        {
            rng.CheckCancellation();
            if (reachesBoss.TryGetValue(node, out bool cached)) return cached;
            if (node.PointType == MapPointType.Boss) return reachesBoss[node] = true;
            bool reachable = false;
            foreach (MapNode child in node.Children) reachable |= ReachesBoss(child);
            reachesBoss[node] = reachable;
            if (reachable && IsValidSegmentStart(node)) starts.Add(node);
            return reachable;
        }

        // Dead ends do not occur in any complete reference path. Filter them at
        // discovery too, including after a pruning pass disconnects a branch.
        ReachesBoss(start);
        var segmentPath = new List<MapNode>();
        // Standard SP's largest Act has 15 room rows: Start + 15 rooms + first
        // Boss = 17 nodes. MP is shorter and discovery stops at the first Boss.
        // Types are 0..8, so four bits each plus a leading length sentinel use
        // at most 69 bits. The temporary key is exact, not a hash/fingerprint.
        void Visit(MapNode node, UInt128 types)
        {
            rng.CheckCancellation();
            segmentPath.Add(node);
            types = (types << 4) | (uint)node.PointType;
            if (segmentPath.Count >= 3 && IsValidSegmentEnd(node))
            {
                // Before postprocessing, each coordinate has one graph node.
                // Row zero has only Start, so the original string key's omission
                // of its column cannot merge different reference endpoints.
                var key = new GraphSegmentKey(segmentPath[0], node, types);
                if (!groups.TryGetValue(key, out List<MapNode[]>? retained))
                    groups[key] = retained = new List<MapNode[]>();
                bool overlaps = false;
                for (int i = 0; i < retained.Count && !overlaps; i++)
                {
                    MapNode[] previous = retained[i];
                    for (int j = 1; j < previous.Length - 1; j++)
                        if (ReferenceEquals(previous[j], segmentPath[j]))
                        {
                            overlaps = true;
                            break;
                        }
                }
                if (!overlaps) retained.Add(segmentPath.ToArray());
            }
            if (node.PointType != MapPointType.Boss)
                foreach (MapNode child in node.Children)
                    if (reachesBoss[child]) Visit(child, types);
            segmentPath.RemoveAt(segmentPath.Count - 1);
        }
        foreach (MapNode segmentStart in starts) Visit(segmentStart, 1);

        // String allocation and ordinal comparison are needed only once per
        // duplicate group. Singleton groups cannot participate in PrunePaths.
        var sorted = new SortedDictionary<string, List<MapNode[]>>(StringComparer.Ordinal);
        foreach (List<MapNode[]> retained in groups.Values)
            if (retained.Count > 1) sorted.Add(GenerateSegmentKey(retained[0]), retained);
        return sorted;
    }
}
