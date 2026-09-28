using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Prediction.Maps;

internal readonly record struct MapPosition(int Column, int Row);
internal sealed record MapPredictionPoint(MapPosition Position, MapPointType Type, bool CanBeModified,
    IReadOnlyList<MapPosition> Children);
internal sealed record MapPrediction(string Seed, ModelKey Act, int ActIndex, int Columns,
    IReadOnlyList<MapPredictionPoint> Points, int RngCalls)
{
    public string ObservationLayer => "StandardActMap/pre-hook";
}

/// <summary>On-demand standard map domain. No live RunState, Search or UI dependencies.</summary>
internal static class Beta111MapPredictor
{
    internal static MapPrediction Predict(string seed, RuntimeProfileId profile, ModelKey act,
        int actIndex, int ascension, int players, bool secondBoss, CancellationToken token)
    {
        if (profile != RuntimeProfileId.Beta111 || players < 1)
            throw new NotSupportedException("Map preview requires Beta111 standard maps and a valid party size.");
        if (ascension is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(ascension));
        if (act.Category != "ACT") throw new NotSupportedException("Unsupported Act identity: " + act);
        var kind = act.Entry switch
        {
            "OVERGROWTH" => ActKind.Overgrowth, "UNDERDOCKS" => ActKind.Underdocks,
            "HIVE" => ActKind.Hive, "GLORY" => ActKind.Glory,
            _ => throw new NotSupportedException("Unsupported standard Act: " + act.Entry)
        };
        var context = new ActContext(kind, actIndex, HasSwarmingElites: ascension >= 1, HasSecondBoss: secondBoss, IsMultiplayer: players > 1);
        var generated = Beta111StandardMapGenerator.GenerateStandardMap(seed, context, token);
        MapPosition Position(MapNode n) => new(n.FinalCol, n.FinalRow);
        var nodes = generated.AllNodes().Select(n => new MapPredictionPoint(Position(n), n.PointType, n.CanBeModified,
            Array.AsReadOnly(n.Children.Select(Position).ToArray()))).ToArray();
        return new(generated.Seed, act, actIndex, 7, Array.AsReadOnly(nodes), generated.MapRngCalls);
    }
}
