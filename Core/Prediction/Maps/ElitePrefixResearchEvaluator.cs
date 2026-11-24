namespace RolltheSpire2.Core.Prediction.Maps;

internal sealed record ElitePrefixResearchResult(bool Matches, int VisitedStates,
    IReadOnlyList<MapPosition> FirstElites);

/// <summary>
/// Universal prefix predicates on a full generated map. Fixed-count evaluation
/// retains each path's counts across merges; floor-seven evaluation has no count
/// state. Nothing after a first Elite participates.
/// This evaluator neither changes the graph nor resolves Unknown room contents.
/// </summary>
internal static class ElitePrefixResearchEvaluator
{
    internal static ElitePrefixResearchResult EvaluateFloorSeven(GeneratedStandardMap map,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Context.ActIndex != 0) throw new ArgumentException("Act 1 map required.", nameof(map));
        return EvaluateFloorSeven(map.Start, token);
    }

    internal static ElitePrefixResearchResult EvaluateFloorSeven(MapNode start, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        token.ThrowIfCancellationRequested();
        var visited = new HashSet<MapNode>();
        var elites = new HashSet<MapPosition>();
        bool Visit(MapNode node)
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(node)) return true;
            // Map floor = row + 1. The graph is the final, complete Act 1 map.
            if (node.Row == 6)
            {
                if (node.PointType != MapPointType.Elite) return false;
                elites.Add(new(node.FinalCol, node.FinalRow));
                return true; // The map after the first Elite is unrestricted.
            }
            if (node.Row is < 1 or >= 6 ||
                node.PointType is not (MapPointType.Monster or MapPointType.Unknown) ||
                node.Children.Count == 0) return false;
            foreach (var child in node.Children)
                if (!Visit(child)) return false;
            return true;
        }

        bool all = start.Children.Count > 0;
        foreach (var first in start.Children)
        {
            token.ThrowIfCancellationRequested();
            if (first.PointType != MapPointType.Monster || !Visit(first)) { all = false; break; }
        }
        return new(all, visited.Count, all ? elites.OrderBy(p => p.Row).ThenBy(p => p.Column).ToArray() : []);
    }

    internal static ElitePrefixResearchResult Evaluate(GeneratedStandardMap map,
        int extraMonsters, int unknowns, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Context.ActIndex != 0) throw new ArgumentException("Act 1 map required.", nameof(map));
        return Evaluate(map.Start, extraMonsters, unknowns, token);
    }

    // The caller supplies a complete Act 1 graph. M and Exact own their
    // independent generators and use the same universal predicate.
    internal static ElitePrefixResearchResult Evaluate(MapNode start,
        int extraMonsters, int unknowns, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (extraMonsters < 0) throw new ArgumentOutOfRangeException(nameof(extraMonsters));
        if (unknowns < 0) throw new ArgumentOutOfRangeException(nameof(unknowns));
        token.ThrowIfCancellationRequested();

        var memo = new Dictionary<(MapNode Node, int Monsters, int Unknowns), bool>();
        var elites = new HashSet<MapPosition>();
        bool Visit(MapNode node, int monsters, int questionMarks)
        {
            token.ThrowIfCancellationRequested();
            var state = (node, monsters, questionMarks);
            if (memo.TryGetValue(state, out bool cached)) return cached;
            bool matches;
            if (node.PointType == MapPointType.Elite)
            {
                matches = monsters == extraMonsters && questionMarks == unknowns;
                if (matches) elites.Add(new(node.FinalCol, node.FinalRow));
            }
            else
            {
                // Test the bound before incrementing, including Int32.MaxValue
                // inputs. A finite graph, rather than a semantic input cap, bounds work.
                matches = node.PointType switch
                {
                    MapPointType.Monster when monsters < extraMonsters => Children(node, monsters + 1, questionMarks),
                    MapPointType.Unknown when questionMarks < unknowns => Children(node, monsters, questionMarks + 1),
                    _ => false
                };
            }
            memo.Add(state, matches);
            return matches;
        }
        bool Children(MapNode node, int monsters, int questionMarks)
        {
            if (node.Children.Count == 0) return false; // Every path must reach a first Elite.
            foreach (var child in node.Children)
            {
                token.ThrowIfCancellationRequested();
                if (!Visit(child, monsters, questionMarks)) return false;
            }
            return true;
        }

        bool all = start.Children.Count > 0;
        foreach (var first in start.Children)
        {
            token.ThrowIfCancellationRequested();
            // The initial fixed Monster is excluded exactly once per path.
            if (first.PointType != MapPointType.Monster || !Children(first, 0, 0))
            { all = false; break; }
        }
        return new(all, memo.Count, all ? elites.OrderBy(p => p.Row).ThenBy(p => p.Column).ToArray() : []);
    }
}
