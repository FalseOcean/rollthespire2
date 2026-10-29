using System.Globalization;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Workspace;

internal static class MapRouteWitnessProjection
{
    internal static MapPrediction CreateMap(string seed, int act, int ascension, int players, CancellationToken token)
    {
        // Use precisely the independent pre-hook graph observed by Exact. The
        // ordinary Analysis map can contain another Act variant or second Boss.
        var generated = Beta111StandardMapGenerator.GenerateStandardMap(seed,
            StandardMapFamily.Context(act, ascension) with { IsMultiplayer = players > 1 }, token);
        MapPosition Position(MapNode node) => new(node.FinalCol, node.FinalRow);
        var points = generated.AllNodes().Select(node => new MapPredictionPoint(Position(node), node.PointType,
            node.CanBeModified, Array.AsReadOnly(node.Children.Select(Position).ToArray()))).ToArray();
        return new(generated.Seed, new ModelKey("ACT", act switch { 1 => "OVERGROWTH", 2 => "HIVE", 3 => "GLORY", _ => throw new ArgumentOutOfRangeException(nameof(act)) }),
            act - 1, 7, Array.AsReadOnly(points), generated.MapRngCalls);
    }

    internal static IReadOnlyList<PersistedMapRouteWitness> FromCandidate(SearchCandidate candidate) =>
        candidate.ValidationDisposition == SearchDisposition.Match && !candidate.IsUnverified
            ? candidate.MatchEvidence.Where(e => e.Code == "StandardActMap.RouteSequence.Final")
                .Select(e => new PersistedMapRouteWitness(e.Act ?? 0, e.RouteId ?? "", e.OutcomeId ?? "")).ToArray()
            : [];

    internal static MapPosition[] Read(PersistedMapRouteWitness witness, string seed, MapPrediction map)
    {
        if (!string.Equals(seed, map.Seed, StringComparison.OrdinalIgnoreCase) || witness.Act != map.ActIndex + 1)
            throw new InvalidOperationException("MapWitness.SeedActMismatch");
        string expectedAct = witness.Act switch { 1 => "OVERGROWTH", 2 => "HIVE", 3 => "GLORY", _ => "" };
        if (map.Act.Category != "ACT" || map.Act.Entry != expectedAct || map.Points.Count(point => point.Type == MapPointType.Boss) != 1)
            throw new InvalidOperationException("MapWitness.NonStandardGraph");
        string prefix = $"act-{witness.Act}:";
        if (witness.Act is < 1 or > 3 || !witness.RouteId.StartsWith(prefix, StringComparison.Ordinal))
            throw new FormatException("MapWitness.RouteActInvalid");
        var path = Positions(witness.RouteId[prefix.Length..]);
        if (!new MapRouteSet(map).IsLegal(path))
            throw new FormatException("MapWitness.NotCompleteMapRoute");
        int last = -1;
        var used = new HashSet<MapPosition>();
        foreach (string group in witness.OutcomeId.Split('|'))
        {
            if (group.Length < 3 || group[0] != '[' || group[^1] != ']')
                throw new FormatException("MapWitness.GroupFormatInvalid");
            var positions = Positions(group[1..^1]);
            var indices = positions.Select(position => Array.IndexOf(path, position)).ToArray();
            if (indices.Any(index => index <= last) || positions.Any(position => !used.Add(position)))
                throw new FormatException("MapWitness.GroupOutsideRouteOrReused");
            last = indices.Max();
        }
        return path;
    }

    private static MapPosition[] Positions(string value) => value.Split('>').Select(part =>
    {
        int colon = part.IndexOf(":c", StringComparison.Ordinal);
        if (part.Length < 5 || part[0] != 'r' || colon < 2 ||
            !int.TryParse(part.AsSpan(1, colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
            !int.TryParse(part.AsSpan(colon + 2), NumberStyles.None, CultureInfo.InvariantCulture, out int column))
            throw new FormatException("MapWitness.CoordinateFormatInvalid: " + part);
        return new MapPosition(column, row);
    }).ToArray();
}
