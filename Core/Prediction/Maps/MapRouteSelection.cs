using System.Numerics;

namespace RolltheSpire2.Core.Prediction.Maps;

internal readonly record struct MapStrokePoint(double Column, double Row);

/// <summary>Explicit UI-authored commitment tied to one immutable standard-map snapshot.</summary>
internal sealed class MapRouteCommitment
{
    public MapPrediction Map { get; }
    public IReadOnlyList<MapPosition> Coordinates { get; }
    public MapRouteCommitment(MapPrediction map, IEnumerable<MapPosition> coordinates)
    {
        var copy = coordinates.ToArray();
        if (!new MapRouteSet(map).IsLegal(copy)) throw new ArgumentException("Not a complete map route.");
        Map = map;
        Coordinates = Array.AsReadOnly(copy);
    }
}

/// <summary>
/// Map-local DAG matching and scalar extrema. Adapted from the research evaluator's
/// reverse min-path DP and dead-end exclusion, without its Search query surface.
/// Counts and ranks optimal paths instead of materializing an exponential route list.
/// Start is the unique lowest-row node; destination is the terminal Boss (including second Boss).
/// </summary>
internal sealed class MapRouteSet
{
    private readonly MapPredictionPoint[] _nodes;
    private readonly Dictionary<MapPosition, int> _indices;
    private readonly int[][] _next;
    private readonly BigInteger[] _counts;
    private readonly int _end;
    public BigInteger Count => _counts[0];
    public int Value { get; }

    public MapRouteSet(MapPrediction map, MapPointType? metric = null, bool minimize = false)
    {
        _nodes = map.Points.OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column).ToArray();
        if (_nodes.Length < 2 || _nodes[0].Position.Row == _nodes[1].Position.Row)
            throw new ArgumentException("Expected a unique map start.");
        _indices = _nodes.Select((p, i) => (p.Position, i)).ToDictionary(p => p.Position, p => p.i);
        _end = Array.FindLastIndex(_nodes, p => p.Type == MapPointType.Boss);
        if (_end < 0 || _nodes[_end].Children.Count != 0) throw new ArgumentException("Expected terminal Boss.");
        _next = new int[_nodes.Length][];
        _counts = new BigInteger[_nodes.Length];
        var scores = new int[_nodes.Length];
        for (int i = _nodes.Length - 1; i >= 0; i--)
        {
            var children = _nodes[i].Children.Distinct().Select(p => _indices.TryGetValue(p, out int j)
                && p.Row > _nodes[i].Position.Row ? j : throw new ArgumentException("Invalid map DAG edge."))
                .OrderBy(j => _nodes[j].Position.Row).ThenBy(j => _nodes[j].Position.Column).ToArray();
            int own = _nodes[i].Type == metric ? 1 : 0;
            if (i == _end) { _counts[i] = 1; scores[i] = own; _next[i] = Array.Empty<int>(); continue; }
            var reachable = children.Where(j => _counts[j] > 0).ToArray();
            if (reachable.Length == 0) { _next[i] = Array.Empty<int>(); continue; }
            int best = minimize ? reachable.Min(j => scores[j]) : reachable.Max(j => scores[j]);
            scores[i] = own + best;
            _next[i] = metric is null ? reachable : reachable.Where(j => scores[j] == best).ToArray();
            foreach (int j in _next[i]) _counts[i] += _counts[j];
        }
        if (Count == 0) throw new ArgumentException("No complete Start to Boss route.");
        Value = scores[0];
    }

    public MapPosition[] At(BigInteger rank)
    {
        if (rank < 0 || rank >= Count) throw new ArgumentOutOfRangeException(nameof(rank));
        var path = new List<MapPosition>();
        int i = 0;
        while (true)
        {
            path.Add(_nodes[i].Position);
            if (i == _end) return path.ToArray();
            foreach (int j in _next[i])
                if (rank < _counts[j]) { i = j; break; } else rank -= _counts[j];
        }
    }

    public bool IsLegal(IReadOnlyList<MapPosition> path)
    {
        if (path.Count < 2 || path[0] != _nodes[0].Position || path[^1] != _nodes[_end].Position) return false;
        for (int p = 1; p < path.Count; p++)
            if (!_indices.TryGetValue(path[p - 1], out int i) || !_indices.TryGetValue(path[p], out int j) || !_next[i].Contains(j)) return false;
        return true;
    }

    public BigInteger Rank(IReadOnlyList<MapPosition> path)
    {
        if (!IsLegal(path)) throw new ArgumentException("Route not in this set.");
        BigInteger rank = 0;
        for (int p = 1; p < path.Count; p++)
            foreach (int j in _next[_indices[path[p - 1]]])
                if (_nodes[j].Position == path[p]) break; else rank += _counts[j];
        return rank;
    }

    public MapPosition[] Match(IReadOnlyList<MapStrokePoint> stroke)
    {
        if (stroke.Count < 2 || stroke.Any(p => !double.IsFinite(p.Column) || !double.IsFinite(p.Row)))
            throw new ArgumentException("A finite stroke with at least two points is required.");
        var desired = _nodes.Select(p => p.Position.Row).Distinct().ToDictionary(r => r, r => DesiredColumn(stroke, r));
        var cost = Enumerable.Repeat(double.PositiveInfinity, _nodes.Length).ToArray();
        var chosen = new int[_nodes.Length];
        for (int i = _nodes.Length - 1; i >= 0; i--)
        {
            if (_counts[i] == 0) continue;
            double own = Math.Pow(_nodes[i].Position.Column - desired[_nodes[i].Position.Row], 2);
            if (i == _end) { cost[i] = own; continue; }
            foreach (int j in _next[i])
                if (own + cost[j] < cost[i] - 1e-9) { cost[i] = own + cost[j]; chosen[i] = j; }
        }
        var path = new List<MapPosition>();
        for (int i = 0; ; i = chosen[i]) { path.Add(_nodes[i].Position); if (i == _end) return path.ToArray(); }
    }

    // Row intersections, averaged for loops. Reversing the stroke has the same samples.
    // Partial strokes extend the nearest endpoint/row sample to the remaining map rows.
    internal static double DesiredColumn(IReadOnlyList<MapStrokePoint> stroke, double row)
    {
        var intersections = new List<double>();
        for (int i = 1; i < stroke.Count; i++)
        {
            var a = stroke[i - 1]; var b = stroke[i];
            if (row < Math.Min(a.Row, b.Row) || row > Math.Max(a.Row, b.Row)) continue;
            intersections.Add(Math.Abs(a.Row - b.Row) < 1e-9 ? (a.Column + b.Column) / 2
                : a.Column + (b.Column - a.Column) * (row - a.Row) / (b.Row - a.Row));
        }
        if (intersections.Count > 0) return intersections.OrderBy(x => x).Average();
        double nearest = stroke.Min(p => Math.Abs(p.Row - row));
        return stroke.Where(p => Math.Abs(Math.Abs(p.Row - row) - nearest) < 1e-9).Select(p => p.Column).OrderBy(x => x).Average();
    }
}
