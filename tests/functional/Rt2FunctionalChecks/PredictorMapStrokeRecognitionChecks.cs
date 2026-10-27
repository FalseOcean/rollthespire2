using System.Numerics;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorMapStrokeRecognitionChecks
{
    private static readonly MapPosition Start = new(3, 0), Left1 = new(1, 1), Right1 = new(5, 1),
        Left2 = new(1, 2), Right2 = new(5, 2), Left3 = new(1, 3), Right3 = new(5, 3), Boss = new(3, 4);

    private static MapPrediction Fixture() => new("MAP_STROKE", new("ACT", "OVERGROWTH"), 0, 7,
    [
        new(Start, MapPointType.Ancient, true, [Left1, Right1]),
        new(Left1, MapPointType.Monster, true, [Left2]), new(Right1, MapPointType.Monster, true, [Right2]),
        new(Left2, MapPointType.Monster, true, [Left3]), new(Right2, MapPointType.Monster, true, [Right3]),
        new(Left3, MapPointType.RestSite, true, [Boss]), new(Right3, MapPointType.RestSite, true, [Boss]),
        new(Boss, MapPointType.Boss, false, [])
    ], 0);

    private static PredictorMapStrokeTarget[] Targets(float separation = 200, float radius = 12) => Fixture().Points.Select(p =>
        new PredictorMapStrokeTarget(p.Position, new Vector2(p.Position.Column switch { 1 => 0, 5 => separation, _ => separation / 2 },
            400 - p.Position.Row * 100), radius)).ToArray();

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Map stroke recognition: " + message); }

    private static void Expect(IReadOnlyList<Vector2> stroke, MapPosition[] expected, string message,
        MapPosition? previous = null, PredictorMapTravelAllowance travel = default,
        PredictorMapStrokeTarget[]? targets = null)
    {
        var result = PredictorMapStrokeRecognition.Recognize(Fixture(), targets ?? Targets(), stroke, previous, travel);
        Check(result.SequenceEqual(expected), message + $"; actual=[{string.Join(",", result)}]");
        // The parser never owns route legality. Check its returned consensus
        // against the independent draft contract, including travel admission.
        var draft = new PredictorMapRouteDraft(Fixture(), previous, travel);
        Check(draft.ApplyStroke(result, PredictorMapStrokeMode.Draw) != PredictorMapDraftEditResult.Invalid,
            message + ": returned recognized points share one legal route");
    }

    internal static void Run()
    {
        ExactOffsetReverseAndFragments();
        AmbiguityAndGraphEvidence();
        ActualProgressAndTravel();
        LocalGeometryAndPurity();
        Console.WriteLine("PREDICTOR_MAP_STROKE_RECOGNITION_PASS exact/offset/reverse/taps; growing stroke; middle fragments; ambiguous scribbles; shared-route graph evidence; factual exclusion; Boots/Flight; local scale; purity");
    }

    private static void ExactOffsetReverseAndFragments()
    {
        Vector2[] stroke = [new(0, 300), new(0, 200), new(0, 100)];
        Expect(stroke, [Left1, Left2, Left3], "sparse segments recognize row landings without synthesized endpoints", Start);
        Expect(Enumerable.Reverse(stroke).ToArray(), [Left1, Left2, Left3], "reverse stroke has identical interpretation", Start);
        Expect([new(18, 300), new(18, 100)], [Left1, Left2, Left3],
            "parallel offset line outside icon radius still lands horizontally", Start);
        Expect([new(0, 200), new(0, 100)], [Left2, Left3], "middle segment can be authored first and does not fill lower gap", Start);
        Expect([new(4, 204)], [Left2], "single point near an icon is an authored tap", Start);
        Expect([new(0, 300)], [Left1], "growing stroke begins with its clear first landing", Start);
        Expect(stroke[..2], [Left1, Left2], "same growing parser advances highlights as row evidence appears", Start);
        Expect([], [], "empty gesture is a no-op", Start);
    }

    private static void AmbiguityAndGraphEvidence()
    {
        Expect([new(0, 200), new(200, 200)], [], "horizontal scribble touches competing branches without choosing either", Start);
        Expect([new(0, 300), new(200, 200)], [],
            "equally clear disconnected nearest points do not concatenate or fabricate connecting branches", Start);
        Expect([new(0, 300), new(200, 300), new(200, 200), new(0, 200), new(0, 100), new(200, 100), new(100, 0)],
            [Boss], "messy branch loops drop unclear rows and retain the clearly touched common boss", Start);
        Expect([new(1000, 320), new(1000, 0), new(900, 150)], [], "far scribble supplies no map intent", Start);

        // Both candidates are locally near the first row, where Right1 is
        // closer. Clear following left-row evidence resolves that landing to
        // the only graph-supported two-hit fragment, rather than nearest拼接.
        Expect([new(21, 300), new(21, 250), new(0, 200)], [Left1, Left2],
            "graph-supported nearby alternative repairs disconnected nearest landing", Start, targets: Targets(40, 20));
        Expect([new(20, 300)], [], "equal horizontal landing remains ambiguous when graph cannot decide", Start,
            targets: Targets(40, 20));
        Expect([new(0, 200), new(0, 300), new(200, 300), new(0, 300)], [Left1, Left2],
            "scribble direction never changes clear legal fragment supported by later evidence", Start);
    }

    private static void ActualProgressAndTravel()
    {
        Expect([new(100, 400), new(0, 300), new(0, 200), new(0, 100)], [Left2, Left3],
            "actual completed rows are excluded from growing and ended interpretation", Left1);
        Expect([new(200, 200), new(200, 100)], [], "unreachable future drawing cannot author a branch", Left1);
        Expect([new(200, 200), new(200, 100)], [Right2, Right3], "one Boots use permits one jump then actual graph edge",
            Left1, new(1));
        Expect([new(200, 200), new(200, 100)], [Right2, Right3], "Flight supplies legal adjacent row travel without Boots",
            Left1, new(0, true));
        Vector2[] jumps = [new(0, 300), new(200, 200), new(0, 100)];
        var one = PredictorMapStrokeRecognition.Recognize(Fixture(), Targets(), jumps, Start, new(1));
        Check(!one.SequenceEqual([Left1, Right2, Left3]), "one Boots cannot authorize a two-jump gesture");
        var draft = new PredictorMapRouteDraft(Fixture(), Start, new(1));
        Check(draft.ApplyStroke(one, PredictorMapStrokeMode.Draw) != PredictorMapDraftEditResult.Invalid,
            "partial one-Boot consensus remains legal");
        Expect(jumps, [Left1, Right2, Left3], "two Boots support all clearly authored jump landings", Start, new(2));
    }

    private static void LocalGeometryAndPurity()
    {
        var targets = Targets();
        var stroke = new[] { new Vector2(18, 300), new Vector2(18, 100) };
        var map = Fixture();
        var before = map.Points.Select(p => (p.Position, Children: p.Children.ToArray())).ToArray();
        var originalStroke = stroke.ToArray();
        var originalTargets = targets.ToArray();
        var original = PredictorMapStrokeRecognition.Recognize(map, targets, stroke, Start);
        Vector2 offset = new(1500, -700);
        var scaledTargets = targets.Select(t => t with { Center = t.Center * 3 + offset, Radius = t.Radius * 3 }).ToArray();
        var scaledStroke = stroke.Select(p => p * 3 + offset).ToArray();
        Check(PredictorMapStrokeRecognition.Recognize(map, scaledTargets, scaledStroke, Start).SequenceEqual(original),
            "translation and scale preserve interpretation through visual-relative thresholds");
        Check(stroke.SequenceEqual(originalStroke) && targets.SequenceEqual(originalTargets) &&
            before.All(p => map.Points.Single(n => n.Position == p.Position).Children.SequenceEqual(p.Children)),
            "recognition does not mutate caller stroke, visual geometry or prediction graph");
        Expect([new(float.NaN, 300), new(0, 200)], [], "malformed sample cannot join unrelated surviving pieces", Start);
        Expect([new(100, 200)], [], "row snapping cannot reach untouchable branch icons", Start);
        Expect([new(0, 250)], [], "tiny point between row bands does not author an intervening icon", Start);
        var missing = Targets().Where(t => t.Position == Left1 || t.Position == Left3).ToArray();
        Expect([new(0, 300), new(0, 100)], [Left1, Left3], "missing visual row cannot be synthesized by graph traversal", Start,
            targets: missing);
    }
}
