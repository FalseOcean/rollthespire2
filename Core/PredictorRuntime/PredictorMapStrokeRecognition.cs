using System.Collections.Immutable;
using System.Numerics;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal readonly record struct PredictorMapStrokeTarget(MapPosition Position, Vector2 Center, float Radius);

/// <summary>
/// Interprets only the supplied native gesture. Geometry supplies candidates;
/// a bounded graph pass establishes their shared route. Unselected rows are
/// never filled, and competing near-equivalent interpretations stay unselected.
/// Contains no Godot, draft mutation, or live game state.
/// </summary>
internal static class PredictorMapStrokeRecognition
{
    // A small difference in horizontal landing must not decide an unclear branch.
    // Each supported row contributes [0.65, 1], so losing a clear row costs more
    // than the ambiguity margin. Scores have no authority outside this gesture.
    private const double AmbiguityMargin = 0.12;

    private sealed class Node(MapPredictionPoint point, double reward, int states)
    {
        internal readonly MapPredictionPoint Point = point;
        internal readonly double Reward = reward;
        internal readonly double[] Forward = Enumerable.Repeat(double.NegativeInfinity, states).ToArray();
        internal readonly double[] Backward = Enumerable.Repeat(double.NegativeInfinity, states).ToArray();
    }

    internal static ImmutableArray<MapPosition> Recognize(MapPrediction map,
        IReadOnlyList<PredictorMapStrokeTarget> targets, IReadOnlyList<Vector2> stroke,
        MapPosition? previous = null, PredictorMapTravelAllowance travel = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(stroke);
        if (travel.WingedBootsUsesLeft is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(travel));
        if (stroke.Count == 0 || targets.Count == 0 || stroke.Any(p => !Finite(p))) return [];

        var visual = targets.Where(t => Finite(t.Center) && float.IsFinite(t.Radius) && t.Radius > 0)
            .GroupBy(t => t.Position).ToDictionary(g => g.Key, g => g.First());
        var visualRows = visual.Values.GroupBy(t => t.Position.Row).ToDictionary(g => g.Key, g => g.ToArray());
        var horizontalSpacings = visualRows.Values.SelectMany(row => row.OrderBy(t => t.Center.X)
            .Zip(row.OrderBy(t => t.Center.X).Skip(1), (a, b) => b.Center.X - a.Center.X))
            .Where(d => d > 0).Order().ToArray();
        float typicalHorizontal = horizontalSpacings.Length > 0 ? horizontalSpacings[horizontalSpacings.Length / 2] : 0;
        var rowCenters = visualRows.ToDictionary(g => g.Key, g => g.Value.Average(t => t.Center.Y));
        var rewards = new Dictionary<MapPosition, double>();
        foreach (var target in visual.Values)
        {
            if (previous is { } boundary && target.Position.Row <= boundary.Row) continue;
            float horizontal = visualRows[target.Position.Row].Where(t => t.Position != target.Position)
                .Select(t => Math.Abs(t.Center.X - target.Center.X)).Where(d => d > 0)
                .DefaultIfEmpty(typicalHorizontal).Min();
            // The capture width stays local to visual icon size and row spacing.
            // Neighboring capture regions may overlap, making ambiguous landings
            // available to graph evidence instead of arbitrarily rounding a row.
            float width = target.Radius * 2.5f;
            if (horizontal > 0) width = Math.Min(width, horizontal * 0.55f);
            float vertical = rowCenters.Where(r => r.Key != target.Position.Row)
                .Select(r => Math.Abs(r.Value - rowCenters[target.Position.Row])).Where(d => d > 0)
                .DefaultIfEmpty(float.PositiveInfinity).Min();
            float band = Math.Min(target.Radius * 1.25f, vertical * 0.3f);
            double distance = RowDistance(stroke, target.Center, band, width);
            if (distance <= 1) rewards[target.Position] = 1 - 0.35 * distance;
        }
        if (rewards.Count == 0) return [];

        int budget = travel.Flight ? 0 : travel.WingedBootsUsesLeft;
        var nodes = map.Points.ToDictionary(p => p.Position,
            p => new Node(p, rewards.GetValueOrDefault(p.Position), budget + 1));
        var rows = nodes.Values.GroupBy(n => n.Point.Position.Row).OrderBy(g => g.Key).ToArray();
        MapPosition start;
        if (previous is { } actual) start = actual;
        else
        {
            var initial = map.Points.Where(p => p.Position.Row == 0).ToArray();
            if (initial.Length != 1) throw new ArgumentException("Map must have exactly one initial point.", nameof(map));
            start = initial[0].Position;
        }
        if (!nodes.TryGetValue(start, out var first))
            throw new ArgumentException("Factual starting point is absent from the map.", nameof(previous));
        first.Forward[0] = first.Reward;
        var byRow = rows.ToDictionary(g => g.Key, g => g.ToArray());

        int StepCost(Node from, Node to) => from.Point.Children.Contains(to.Point.Position) || travel.Flight ? 0 : 1;
        bool Terminal(Node n) => n.Point.Type == MapPointType.Boss && n.Point.Children.Count == 0;

        // Each state is (map point, spent Boots). Adjacent row transitions mirror
        // draft legality. Flight has one state and permits any adjacent landing.
        // Work is O(stroke samples * visual points + adjacent row pairs * 4),
        // independent of how many complete routes the graph contains.
        foreach (var row in rows.Where(g => g.Key >= start.Row))
        {
            if (!byRow.TryGetValue(row.Key + 1, out var next)) continue;
            foreach (var from in row)
            {
                if (Terminal(from)) continue;
                foreach (var to in next)
                {
                    int cost = StepCost(from, to);
                    for (int spent = 0; spent + cost <= budget; spent++)
                        to.Forward[spent + cost] = Math.Max(to.Forward[spent + cost], from.Forward[spent] + to.Reward);
                }
            }
        }
        double best = nodes.Values.Where(Terminal).SelectMany(n => n.Forward).DefaultIfEmpty(double.NegativeInfinity).Max();
        if (!double.IsFinite(best) || best <= 0) return [];
        foreach (var row in Enumerable.Reverse(rows).Where(g => g.Key >= start.Row))
            foreach (var from in row)
            {
                if (Terminal(from)) { Array.Fill(from.Backward, 0); continue; }
                if (!byRow.TryGetValue(row.Key + 1, out var next)) continue;
                foreach (var to in next)
                {
                    int cost = StepCost(from, to);
                    for (int spent = 0; spent + cost <= budget; spent++)
                        from.Backward[spent] = Math.Max(from.Backward[spent], to.Reward + to.Backward[spent + cost]);
                }
            }

        var recognized = ImmutableArray.CreateBuilder<MapPosition>();
        foreach (var row in rows.Where(g => g.Key >= start.Row))
        {
            // Consensus over all near-best legal interpretations includes the
            // possibility of traversing an untouched point at this row. That
            // possibility makes the row unchosen too; it cannot author a gap.
            var supported = row.Where(n => Enumerable.Range(0, budget + 1).Any(spent =>
                n.Forward[spent] + n.Backward[spent] >= best - AmbiguityMargin - 1e-9)).ToArray();
            if (supported.Length == 1 && supported[0].Reward > 0)
                recognized.Add(supported[0].Point.Position);
        }
        return recognized.ToImmutable();
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static double RowDistance(IReadOnlyList<Vector2> stroke, Vector2 center, float band, float width)
    {
        double best = double.PositiveInfinity;
        // Points support taps and endpoints. Clipped segments support sparse
        // input and reverse traces without requiring exact icon intersection.
        foreach (var point in stroke)
            if (Math.Abs(point.Y - center.Y) <= band)
                best = Math.Min(best, Math.Sqrt(Math.Pow((point.X - center.X) / width, 2) +
                    Math.Pow((point.Y - center.Y) / (band * 2), 2)));
        for (int index = 1; index < stroke.Count; index++)
        {
            var from = stroke[index - 1];
            var delta = stroke[index] - from;
            float lower = 0, upper = 1;
            if (Math.Abs(delta.Y) < float.Epsilon)
            { if (Math.Abs(from.Y - center.Y) > band) continue; }
            else
            {
                float a = (center.Y - band - from.Y) / delta.Y, b = (center.Y + band - from.Y) / delta.Y;
                lower = Math.Max(0, Math.Min(a, b)); upper = Math.Min(1, Math.Max(a, b));
                if (lower > upper) continue;
            }
            var scaledFrom = new Vector2((from.X - center.X) / width, (from.Y - center.Y) / (band * 2));
            var scaledDelta = new Vector2(delta.X / width, delta.Y / (band * 2));
            float length = scaledDelta.LengthSquared();
            float at = length > 0 ? Math.Clamp(-Vector2.Dot(scaledFrom, scaledDelta) / length, lower, upper) : lower;
            best = Math.Min(best, (scaledFrom + scaledDelta * at).Length());
        }
        return best;
    }
}
