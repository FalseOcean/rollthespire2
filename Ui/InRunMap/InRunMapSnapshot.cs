using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using MapType = RolltheSpire2.Core.Prediction.Maps.MapPointType;

namespace RolltheSpire2.Ui.InRunMap;

// Freeze graph values only. No native MapPoint, ActMap or RNG reference escapes
// into the draft, and no game hooks execute during capture or future projection.
internal static class InRunMapSnapshot
{
    internal static MapPrediction Actual(IRunState run)
    {
        var points = run.Map.GetAllMapPoints().Append(run.Map.StartingMapPoint).Append(run.Map.BossMapPoint);
        if (run.Map.SecondBossMapPoint is { } second) points = points.Append(second);
        return new(run.Rng.StringSeed, Key(run.Act.Id), run.CurrentActIndex, run.Map.GetColumnCount(),
            Array.AsReadOnly(points.DistinctBy(p => p.coord).Select(p => new MapPredictionPoint(Position(p.coord),
                (MapType)(int)p.PointType, p.CanBeModified,
                Array.AsReadOnly(p.Children.Select(c => Position(c.coord)).ToArray()))).ToArray()), 0);
    }

    internal static MapPrediction Projected(IRunState run, int actIndex)
    {
        var act = run.Acts[actIndex];
        bool golden = run.Players.SelectMany(p => p.Relics).OfType<GoldenCompass>()
            .Any(r => !r.IsMelted && r.GoldenPathAct == actIndex);
        bool spoils = run.Players.SelectMany(p => p.Deck.Cards).OfType<SpoilsMap>()
            .Any(c => c.SpoilsActIndex == actIndex);
        bool bigGame = run.Modifiers.Any(m => m is BigGameHunter);
        var map = PredictorMapProjection.Generate(run.Rng.StringSeed, Key(act.Id), actIndex,
            run.AscensionLevel, run.Players.Count, act.HasSecondBoss, golden, spoils, bigGame, CancellationToken.None);
        // Without Neow, the native first map starts in a Monster room.
        return actIndex == 0 && !run.ExtraFields.StartedWithNeow
            ? map with { Points = map.Points.Select(p => p.Type == MapType.Ancient ? p with { Type = MapType.Monster } : p).ToArray() }
            : map;
    }

    internal static PredictorMapTravelAllowance Travel(IRunState run) => new(
        Math.Clamp(run.Players.SelectMany(p => p.Relics).OfType<WingedBoots>()
            .Where(r => !r.IsMelted).Select(r => 3 - r.TimesUsed).DefaultIfEmpty(0).Max(), 0, 3),
        run.Modifiers.Any(m => m is Flight));
    internal static MapPosition Position(MapCoord coord) => new(coord.col, coord.row);
    private static ModelKey Key(MegaCrit.Sts2.Core.Models.ModelId id) => new(id.Category, id.Entry);
    internal static bool SameMap(MapPrediction a, MapPrediction b) => a.Act == b.Act && a.ActIndex == b.ActIndex &&
        a.Columns == b.Columns && a.Points.Count == b.Points.Count && a.Points.OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column)
            .Zip(b.Points.OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column))
            .All(pair => pair.First.Position == pair.Second.Position && pair.First.Type == pair.Second.Type &&
                pair.First.Children.SequenceEqual(pair.Second.Children));
}
