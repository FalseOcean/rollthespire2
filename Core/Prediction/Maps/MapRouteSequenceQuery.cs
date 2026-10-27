namespace RolltheSpire2.Core.Prediction.Maps;

/// <summary>Counts only non-fixed rooms on the same route as the sequence.</summary>
internal readonly record struct MapRouteCountBounds(MapPointType Type, int Minimum = 0, int? Maximum = null);

/// <summary>An unordered multiset of required room occurrences in one ordered group.</summary>
internal sealed class MapRouteGroup
{
    public MapRouteGroup(IEnumerable<MapPointType> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var items = nodes.ToArray();
        if (items.Any(t => !MapRouteSequenceQuery.Selectable(t)))
            throw new ArgumentException("Groups support Monster, Elite, RestSite, Shop and Unknown.", nameof(nodes));
        Nodes = Array.AsReadOnly(items);
    }

    public IReadOnlyList<MapPointType> Nodes { get; }
}

/// <summary>
/// Existential query over one complete Act route. Groups are ordered;
/// rooms inside each group are unordered. Gaps and extra occurrences are allowed,
/// but selected nodes cannot overlap or interleave between groups. Generated fixed
/// rooms and Start/Boss do not contribute to either groups or additional counts.
/// Existing scalar objectives and three-Act totals keep their own semantics.
/// </summary>
internal sealed class MapRouteSequenceQuery
{
    public MapRouteSequenceQuery(IEnumerable<MapPointType> sequence,
        IEnumerable<MapRouteCountBounds>? additionalCounts = null)
        : this(SingletonGroups(sequence), additionalCounts)
    {
    }

    public static MapRouteSequenceQuery FromGroups(IEnumerable<MapRouteGroup> groups,
        IEnumerable<MapRouteCountBounds>? additionalCounts = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return new(groups.ToArray(), additionalCounts);
    }

    private MapRouteSequenceQuery(MapRouteGroup[] groups, IEnumerable<MapRouteCountBounds>? additionalCounts)
    {
        if (groups.Any(g => g is null))
            throw new ArgumentException("A route group cannot be null.", nameof(groups));
        var counts = additionalCounts?.ToArray() ?? Array.Empty<MapRouteCountBounds>();
        foreach (var count in counts)
        {
            if (!Selectable(count.Type))
                throw new ArgumentException("Count condition has a non-selectable room type.", nameof(additionalCounts));
            if (count.Minimum < 0 || count.Maximum is < 0)
                throw new ArgumentOutOfRangeException(nameof(additionalCounts), "Room counts cannot be negative.");
        }
        // Preserve contradictory and duplicate authored conditions. Contradictions
        // are valid requests with no matching route, not malformed input.
        Groups = Array.AsReadOnly(groups);
        AdditionalCounts = Array.AsReadOnly(counts);
    }

    private static MapRouteGroup[] SingletonGroups(IEnumerable<MapPointType> sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        return sequence.Select(t => new MapRouteGroup([t])).ToArray();
    }

    public IReadOnlyList<MapRouteGroup> Groups { get; }
    public IReadOnlyList<MapRouteCountBounds> AdditionalCounts { get; }

    internal static bool Selectable(MapPointType type) => type is MapPointType.Monster
        or MapPointType.Elite or MapPointType.RestSite or MapPointType.Shop or MapPointType.Unknown;
}

// Coordinates are immutable snapshots scoped to the supplied Act map. A witness
// is not a player commitment and must not be reused against a different map.
internal sealed record MapRouteSequenceResult(bool Matches, IReadOnlyList<MapPosition> Path,
    IReadOnlyList<MapPosition> MatchedNodes, IReadOnlyList<IReadOnlyList<MapPosition>> MatchedGroups, int VisitedStates);

/// <summary>
/// Pure graph predicate, not an authority for how a graph was generated. Callers
/// doing final verification must supply the full Final graph. Assignment graphs
/// may use the same predicate for lossy proposals, never as a final witness.
/// </summary>
internal static class MapRouteSequenceEvaluator
{
    private sealed record Step(MapNode Node, Step? Next);
    private readonly record struct Counts(int Monster, int Elite, int Rest, int Shop, int Unknown)
    {
        public int Get(MapPointType type) => type switch
        {
            MapPointType.Monster => Monster, MapPointType.Elite => Elite,
            MapPointType.RestSite => Rest, MapPointType.Shop => Shop,
            MapPointType.Unknown => Unknown, _ => 0
        };

        public Counts With(MapPointType type, int value) => type switch
        {
            MapPointType.Monster => this with { Monster = value },
            MapPointType.Elite => this with { Elite = value },
            MapPointType.RestSite => this with { Rest = value },
            MapPointType.Shop => this with { Shop = value },
            MapPointType.Unknown => this with { Unknown = value }, _ => this
        };
    }

    private static readonly MapPointType[] RoomTypes =
        [MapPointType.Monster, MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop, MapPointType.Unknown];

    public static MapRouteSequenceResult Evaluate(GeneratedStandardMap map, MapRouteSequenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Evaluate(map.Start, map.SecondBoss ?? map.Boss, query, cancellationToken);
    }

    /// <summary>Evaluate the same predicate on an existing Predictor snapshot, without regenerating its map.</summary>
    public static MapRouteSequenceResult Evaluate(MapPrediction map, MapRouteSequenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        // Match MapRouteSet's full-Act endpoint, including the second Boss when present.
        var points = map.Points.OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column).ToArray();
        if (points.Length < 2 || points[0].Position.Row == points[1].Position.Row)
            throw new ArgumentException("Expected a unique map start.", nameof(map));
        var boss = points.LastOrDefault(p => p.Type == MapPointType.Boss);
        if (boss is null || boss.Children.Count != 0)
            throw new ArgumentException("Expected terminal Boss.", nameof(map));
        var nodes = new Dictionary<MapPosition, MapNode>();
        foreach (var point in points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodes.TryAdd(point.Position, new MapNode(point.Position.Column, point.Position.Row)
                { PointType = point.Type, CanBeModified = point.CanBeModified }))
                throw new ArgumentException("Duplicate map coordinate.", nameof(map));
        }
        foreach (var point in points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[point.Position];
            foreach (var position in point.Children)
            {
                if (!nodes.TryGetValue(position, out var child) || position.Row <= point.Position.Row)
                    throw new ArgumentException("Invalid map DAG edge.", nameof(map));
                node.Children.Add(child);
            }
        }
        return Evaluate(nodes[points[0].Position], nodes[boss.Position], query, cancellationToken);
    }

    public static MapRouteSequenceResult Evaluate(MapNode start, MapNode boss, MapRouteSequenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(boss);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        // Keep existing node identities and fixed-room provenance. In particular,
        // a coincidental dominator is not a generated fixed room.
        bool Countable(MapNode node) => !ReferenceEquals(node, start) && !ReferenceEquals(node, boss)
            && node.CanBeModified && MapRouteSequenceQuery.Selectable(node.PointType);

        var children = new Dictionary<MapNode, MapNode[]>();
        var reachesBoss = new Dictionary<MapNode, bool>();
        var order = new List<MapNode>();
        bool Reach(MapNode node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reachesBoss.TryGetValue(node, out bool known)) return known;
            if (ReferenceEquals(node, boss))
            {
                children[node] = Array.Empty<MapNode>();
                order.Add(node);
                return reachesBoss[node] = true;
            }
            // Stable coordinate order also makes an equal-quality witness stable.
            var next = node.Children.OrderBy(n => n.FinalRow).ThenBy(n => n.FinalCol).ToArray();
            children[node] = next;
            bool reaches = false;
            foreach (var child in next)
            {
                if (child.FinalRow <= node.FinalRow)
                    throw new ArgumentException("Expected strictly increasing map rows.");
                reaches |= Reach(child);
            }
            reachesBoss[node] = reaches;
            order.Add(node); // reverse topological order, including dead ends
            return reaches;
        }
        if (!Reach(start)) throw new ArgumentException("No legal Start to Boss path.");
        order.Reverse();

        var targets = query.Groups.Select(group =>
        {
            Counts target = default;
            foreach (var type in group.Nodes) target = target.With(type, target.Get(type) + 1);
            return target;
        }).ToArray();
        var nextGroup = new int[query.Groups.Count + 1];
        nextGroup[^1] = query.Groups.Count;
        for (int i = query.Groups.Count - 1; i >= 0; i--)
            nextGroup[i] = query.Groups[i].Nodes.Count == 0 ? nextGroup[i + 1] : i;

        bool AdvanceGroup(MapNode node, ref int group, ref Counts consumed)
        {
            if (!Countable(node) || group == targets.Length) return false;
            var target = targets[group];
            int have = consumed.Get(node.PointType), need = target.Get(node.PointType);
            if (have >= need) return false;
            consumed = consumed.With(node.PointType, have + 1);
            if (consumed == target)
            {
                group = nextGroup[group + 1];
                consumed = default;
            }
            // At most one group consumes this node, even when it completes a group.
            return true;
        }

        static MapPosition Position(MapNode node) => new(node.FinalCol, node.FinalRow);
        MapRouteSequenceResult Miss(int visited) => new(false, Array.Empty<MapPosition>(), Array.Empty<MapPosition>(),
            Array.Empty<IReadOnlyList<MapPosition>>(), visited);
        MapRouteSequenceResult Found(IEnumerable<MapNode> route, int visited)
        {
            var path = route.ToArray();
            var matched = new List<MapNode>();
            var groups = query.Groups.Select(_ => new List<MapNode>()).ToArray();
            int group = nextGroup[0];
            Counts consumed = default;
            foreach (var node in path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int selectedGroup = group;
                if (AdvanceGroup(node, ref group, ref consumed))
                {
                    matched.Add(node);
                    groups[selectedGroup].Add(node);
                }
            }
            var matchedGroups = groups.Select(g => (IReadOnlyList<MapPosition>)Array.AsReadOnly(g.Select(Position).ToArray())).ToArray();
            return new(true, Array.AsReadOnly(path.Select(Position).ToArray()), Array.AsReadOnly(matched.Select(Position).ToArray()),
                Array.AsReadOnly(matchedGroups), visited);
        }

        if (query.AdditionalCounts.Count == 0 && query.Groups.All(g => g.Nodes.Count <= 1))
        {
            var sequence = query.Groups.SelectMany(g => g.Nodes).ToArray();
            int Advance(MapNode node, int progress) => Countable(node) && progress < sequence.Length
                && node.PointType == sequence[progress] ? progress + 1 : progress;
            // For a single positive subsequence, greater progress at the SAME
            // vertex dominates lesser progress. No route enumeration is needed.
            var progress = new Dictionary<MapNode, int> { [start] = 0 };
            var previous = new Dictionary<MapNode, MapNode>();
            int visited = 0;
            foreach (var node in order)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reachesBoss[node] || !progress.TryGetValue(node, out int done)) continue;
                visited++;
                foreach (var child in children[node])
                {
                    if (!reachesBoss[child]) continue;
                    int next = Advance(child, done);
                    if (!progress.TryGetValue(child, out int existing) || next > existing)
                    {
                        progress[child] = next;
                        previous[child] = node;
                    }
                }
            }
            if (progress[boss] != sequence.Length) return Miss(visited);
            var path = new List<MapNode>();
            for (var node = boss; ; node = previous[node])
            {
                path.Add(node);
                if (ReferenceEquals(node, start)) break;
            }
            path.Reverse();
            return Found(path, visited);
        }

        var minimum = new int[9];
        var maximum = Enumerable.Repeat(int.MaxValue, 9).ToArray();
        foreach (var condition in query.AdditionalCounts)
        {
            int d = (int)condition.Type;
            minimum[d] = Math.Max(minimum[d], condition.Minimum);
            maximum[d] = Math.Min(maximum[d], condition.Maximum ?? int.MaxValue);
        }
        if (RoomTypes.Any(t => minimum[(int)t] > maximum[(int)t])) return Miss(0);

        // At a merge, partial groups with different room types cannot be combined
        // or reduced to the number of matched icons. Global counts can also trade
        // off against group progress. Memoize their complete joint state.
        var states = new Dictionary<(MapNode Node, int Group, Counts Consumed, Counts Counts), Step?>();
        Step? Visit(MapNode node, int group, Counts consumed, Counts counts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (node, group, consumed, counts);
            if (states.TryGetValue(key, out var cached)) return cached;
            // On a concrete path, finishing each positive lower-bound group as
            // early as possible leaves a superset of the suffix for later groups.
            // No permutations or alternative group boundaries need enumeration.
            AdvanceGroup(node, ref group, ref consumed);
            if (Countable(node))
            {
                int d = (int)node.PointType;
                int next = counts.Get(node.PointType) + 1;
                if (next > maximum[d]) return states[key] = null;
                // Lower-bound-only dimensions no longer distinguish excess rooms.
                int cap = maximum[d] == int.MaxValue ? minimum[d] : maximum[d];
                counts = counts.With(node.PointType, Math.Min(next, cap));
            }
            if (ReferenceEquals(node, boss))
                return states[key] = group == query.Groups.Count
                    && RoomTypes.All(t => counts.Get(t) >= minimum[(int)t]) ? new Step(node, null) : null;
            foreach (var child in children[node])
            {
                if (!reachesBoss[child]) continue;
                var next = Visit(child, group, consumed, counts);
                if (next is not null) return states[key] = new Step(node, next);
            }
            return states[key] = null;
        }
        var witness = Visit(start, nextGroup[0], default, default);
        if (witness is null) return Miss(states.Count);
        var resultPath = new List<MapNode>();
        for (var step = witness; step is not null; step = step.Next) resultPath.Add(step.Node);
        return Found(resultPath, states.Count);
    }
}
