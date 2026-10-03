namespace RolltheSpire2.Core.Prediction.Maps;

/// <summary>
/// Experimental physical specialization of Prune discovery, not early rejection.
/// DFS preserves source path order and each path's (i,j) segment order. An identical
/// sequence of node identities can never change a segment group on a later visit:
/// it overlaps its earlier retained occurrence, or the earlier occurrence was
/// already suppressed by a retained segment that remains present. Therefore only
/// its first encounter needs string-key generation and overlap testing.
/// </summary>
internal static class PackedPruneDiscovery
{
    internal static SortedDictionary<string, List<MapNode[]>> Find(MapNode start, ReferenceRng rng)
    {
        var groups = new SortedDictionary<string, List<MapNode[]>>(StringComparer.Ordinal);
        var seen = new HashSet<UInt128>();
        // Standard SP/MP paths fit the SP maximum of 17 nodes including Start and first Boss.
        // Seven bits per raw coordinate plus a leading sentinel fits in UInt128.
        var path = new MapNode[17];
        void Visit(MapNode node, int depth)
        {
            rng.CheckCancellation();
            if (depth >= path.Length) throw new InvalidOperationException("Packed map path bound exceeded.");
            path[depth] = node;
            if (node.PointType != MapPointType.Boss)
            {
                foreach (var child in node.Children) Visit(child, depth + 1);
                return;
            }
            int count = depth + 1;
            for (int i = 0; i < count - 1; i++)
            {
                if (path[i].Children.Count <= 1 && path[i].Row != 0) continue;
                UInt128 identity = 1;
                for (int k = i; k < count; k++)
                {
                    int coordinate = path[k].RawRow * 7 + path[k].RawCol;
                    if ((uint)coordinate >= 128) throw new InvalidOperationException("Packed node identity bound exceeded.");
                    identity = (identity << 7) | (uint)coordinate;
                    if (k - i < 2 || path[k].Parents.Count < 2 || !seen.Add(identity)) continue;
                    var segment = path.AsSpan(i, k - i + 1).ToArray();
                    var first = segment[0]; var last = segment[^1];
                    string key = (first.Row == 0 ? $"{first.Row}-{last.Col},{last.Row}-"
                        : $"{first.Col},{first.Row}-{last.Col},{last.Row}-") +
                        string.Join(",", segment.Select(n => (int)n.PointType));
                    if (!groups.TryGetValue(key, out var retained)) groups[key] = retained = new();
                    bool overlap = retained.Any(other => Enumerable.Range(1, segment.Length - 2)
                        .Any(j => ReferenceEquals(other[j], segment[j])));
                    if (!overlap) retained.Add(segment);
                }
            }
        }
        Visit(start, 0);
        return groups;
    }
}
