using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

// Shared pure geometry for detached route simulation and the in-run display.
// Inputs are captured values; this never invokes hooks on a real RunState.
internal static class PredictorMapProjection
{
    internal static MapPrediction Generate(string seed, ModelKey act, int actIndex, int ascension,
        int playersCount, bool hasSecondBoss, bool goldenCompass, bool spoilsMap, bool bigGameHunter,
        CancellationToken token = default)
    {
        var standard = Beta111MapPredictor.Predict(seed, RuntimeProfileId.Beta111,
            act, actIndex, ascension, playersCount, hasSecondBoss, token);
        if (goldenCompass) return GoldenPath(standard, playersCount > 1);
        bool multiplayer = playersCount > 1;
        var map = spoilsMap ? Beta111StandardMapGenerator.GenerateSpoilsMap(seed, act, ascension, token,
            multiplayer: multiplayer) : standard;
        if (!bigGameHunter) return map;
        var counts = new MapPointTypeCounts(map.Points.Count(p => p.Type == MapPointType.Unknown),
            map.Points.Count(p => p.Type == MapPointType.RestSite),
            (int)Math.Round(map.Points.Count(p => p.Type == MapPointType.Elite) * 2.5f))
        { PointTypesThatIgnoreRules = [MapPointType.Elite] };
        return spoilsMap ? Beta111StandardMapGenerator.GenerateSpoilsMap(seed, act, ascension, token, counts, multiplayer)
            : Beta111StandardMapGenerator.GenerateBigGameStandardMap(seed, act, actIndex, ascension,
                hasSecondBoss, counts, token, multiplayer);
    }

    private static MapPrediction GoldenPath(MapPrediction standard, bool multiplayer)
    {
        var rooms = new List<MapPointType>
        {
            MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.RestSite,
            MapPointType.Monster, MapPointType.RestSite, MapPointType.Unknown, MapPointType.Treasure,
            MapPointType.Unknown, MapPointType.Treasure, MapPointType.Unknown, MapPointType.Shop,
            MapPointType.Elite, MapPointType.RestSite, MapPointType.Elite, MapPointType.RestSite
        };
        // GoldenPathActMap removes its third room in multiplayer.
        if (multiplayer) rooms.RemoveAt(2);
        var points = new List<MapPredictionPoint>();
        for (int row = 0; row <= rooms.Count + 1; row++)
            points.Add(new(new(3, row), row == 0 ? MapPointType.Ancient :
                row == rooms.Count + 1 ? MapPointType.Boss : rooms[row - 1], true,
                row == rooms.Count + 1 ? [] : [new MapPosition(3, row + 1)]));
        return standard with { Points = points, Columns = 7 };
    }
}
