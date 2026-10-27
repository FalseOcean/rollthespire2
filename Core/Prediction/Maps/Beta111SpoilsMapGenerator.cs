using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Prediction.Maps;

// Source-order port of SpoilsActMap. The common map node, shuffle, point-type
// rules, and path pruning are shared with the existing pure standard-map port.
internal static partial class Beta111StandardMapGenerator
{
    internal static MapPrediction GenerateSpoilsMap(string seed, ModelKey act, int ascension,
        CancellationToken token = default, MapPointTypeCounts? overrideCounts = null, bool multiplayer = false)
    {
        if (act != new ModelKey("ACT", "HIVE"))
            throw new ArgumentException("SpoilsMap only generates the second Act map", nameof(act));
        var profile = Beta111Profile.Instance;
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(profile, seed, out string canonical, out string issue))
            throw new ArgumentException(issue, nameof(seed));
        var context = new ActContext(ActKind.Hive, 1, IsMultiplayer: multiplayer, HasSwarmingElites: ascension >= 1);
        var rng = new ReferenceRng(profile.DeriveNamedStreamSeed(profile.ComputeRootSeed(canonical), "spoils_map"),
            token, MapReplayMode.PackedSegmentDedup);
        MapPointTypeCounts counts = overrideCounts ?? GetMapPointTypes(context, rng);
        int mapLength = GetNumberOfRooms(context.Act, multiplayer) + 1;
        int treasureRow = mapLength - 7;
        var grid = new MapNode?[7, mapLength];
        MapNode start = new(3, 0);
        MapNode boss = new(3, mapLength);
        var starts = new HashSet<MapNode>();
        for (int i = 0; i < 7; i++)
        {
            MapNode first = GetOrCreatePoint(grid, rng.NextInt(0, 7), 1);
            if (i == 1)
                while (starts.Contains(first)) first = GetOrCreatePoint(grid, rng.NextInt(0, 7), 1);
            starts.Add(first);
            MapNode current = first;
            while (current.Row < mapLength - 1)
            {
                (int col, int row) = SpoilsNextCoord(grid, current, treasureRow, mapLength, rng);
                MapNode next = GetOrCreatePoint(grid, col, row);
                AddChildPoint(current, next);
                current = next;
            }
        }
        MapNode treasure = GetOrCreatePoint(grid, 3, treasureRow);
        treasure.PointType = MapPointType.Treasure;
        treasure.CanBeModified = false;
        foreach (MapNode stray in Enumerable.Range(0, 7).Select(c => grid[c, treasureRow])
            .Where(n => n != null && n != treasure).Cast<MapNode>().ToList())
        {
            foreach (MapNode parent in stray.Parents.ToList())
            {
                RemoveChildPoint(parent, stray);
                AddChildPoint(parent, treasure);
            }
            foreach (MapNode child in stray.Children.ToList())
            {
                RemoveChildPoint(stray, child);
                AddChildPoint(treasure, child);
            }
            grid[stray.Col, stray.Row] = null;
        }
        ForEachInRow(grid, mapLength - 1, p => AddChildPoint(p, boss));
        ForEachInRow(grid, 1, p => AddChildPoint(start, p));
        AssignSpoilsTypes(grid, mapLength, counts, start, boss, rng);
        PruneAndRepair(grid, starts, start, boss, counts, rng);
        token.ThrowIfCancellationRequested();
        var nodes = new List<MapPredictionPoint>();
        nodes.Add(ToPrediction(start));
        nodes.AddRange(GridNodes(grid).Select(ToPrediction));
        nodes.Add(ToPrediction(boss));
        return new(canonical, act, 1, 7, nodes.AsReadOnly(), rng.Counter);

        static MapPredictionPoint ToPrediction(MapNode node) => new(new(node.Col, node.Row),
            node.PointType, node.CanBeModified,
            node.Children.Select(c => new MapPosition(c.Col, c.Row)).OrderBy(p => p.Column)
                .ThenBy(p => p.Row).ToArray());
    }

    internal static MapPrediction GenerateBigGameStandardMap(string seed, ModelKey act, int actIndex,
        int ascension, bool secondBoss, MapPointTypeCounts counts, CancellationToken token = default, bool multiplayer = false)
    {
        var profile = Beta111Profile.Instance;
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(profile, seed, out string canonical, out string issue))
            throw new ArgumentException(issue, nameof(seed));
        ActKind kind = act.Entry switch
        {
            "OVERGROWTH" => ActKind.Overgrowth, "UNDERDOCKS" => ActKind.Underdocks,
            "HIVE" => ActKind.Hive, "GLORY" => ActKind.Glory,
            _ => throw new ArgumentException("Invalid Act", nameof(act))
        };
        var context = new ActContext(kind, actIndex, HasSwarmingElites: ascension >= 1,
            HasSecondBoss: secondBoss, IsMultiplayer: multiplayer);
        var rng = new ReferenceRng(profile.DeriveNamedStreamSeed(profile.ComputeRootSeed(canonical),
            $"act_{actIndex + 1}_map"), token, MapReplayMode.PackedSegmentDedup);
        int mapLength = GetNumberOfRooms(kind, multiplayer) + 1;
        var grid = new MapNode?[7, mapLength];
        MapNode start = new(3, 0);
        MapNode boss = new(3, mapLength);
        MapNode? second = secondBoss ? new(3, mapLength + 1) : null;
        var starts = new HashSet<MapNode>();
        GenerateMap(grid, rng, starts, start, boss, second);
        AssignPointTypes(grid, mapLength, context, counts, start, boss, second, rng);
        PruneAndRepair(grid, starts, start, boss, counts, rng);
        CenterGrid(grid);
        SpreadAdjacentMapPoints(grid);
        StraightenPaths(grid);
        var all = new List<MapNode> { start };
        all.AddRange(GridNodes(grid));
        all.Add(boss);
        if (second != null) all.Add(second);
        return new(canonical, act, actIndex, 7, all.Select(n => new MapPredictionPoint(
            new(n.Col, n.Row), n.PointType, n.CanBeModified,
            n.Children.Select(c => new MapPosition(c.Col, c.Row)).OrderBy(p => p.Column)
                .ThenBy(p => p.Row).ToArray())).ToArray(), rng.Counter);
    }

    private static (int Col, int Row) SpoilsNextCoord(MapNode?[,] grid, MapNode current,
        int treasureRow, int mapLength, ReferenceRng rng)
    {
        int row = current.Row + 1;
        int fromCenter = Math.Abs(row - treasureRow);
        int fromTop = mapLength - 1 - row;
        int width = Math.Min(3, Math.Min(fromCenter, Math.Min(3, Math.Max(0, fromTop) + 1)));
        int min = Math.Max(0, 3 - width);
        int max = Math.Min(6, 3 + width);
        List<int> directions = [-1, 0, 1];
        int distance = treasureRow - current.Row;
        if (distance > 3 || distance <= 0) StableShuffle(directions, rng);
        else
        {
            directions = [];
            int toward = Math.Sign(3 - current.Col);
            if (toward != 0) directions.Add(toward);
            directions.Add(0);
            if (toward != 0) directions.Add(-toward);
            if (!directions.Contains(-1)) directions.Add(-1);
            if (!directions.Contains(1)) directions.Add(1);
        }
        foreach (int direction in directions)
        {
            int col = Math.Clamp(current.Col + direction, 0, 6);
            if (col < min || col > max || SpoilsHasCrossover(grid, current, col)) continue;
            MapNode? point = grid[col, row];
            if ((point == null || point.Parents.Contains(current) || point.Parents.Count < 3) &&
                (current.Row == 0 || current.Children.Count < 3 || point != null && current.Children.Contains(point)))
                return (col, row);
        }
        int fallback = Math.Clamp(3, min, max);
        if (Math.Abs(fallback - current.Col) > 1)
            fallback = Math.Clamp(current.Col + Math.Sign(fallback - current.Col), min, max);
        if (SpoilsHasCrossover(grid, current, fallback)) fallback = Math.Clamp(current.Col, min, max);
        if (Math.Abs(fallback - current.Col) > 1)
            throw new InvalidOperationException("Spoils fallback exceeds adjacency");
        return (fallback, row);
    }

    private static bool SpoilsHasCrossover(MapNode?[,] grid, MapNode current, int target)
    {
        int delta = target - current.Col;
        if (delta == 0 || grid[target, current.Row] is not { } neighbor) return false;
        return neighbor.Children.Any(child => child.Col - neighbor.Col == -delta);
    }

    private static void AssignSpoilsTypes(MapNode?[,] grid, int mapLength,
        MapPointTypeCounts counts, MapNode start, MapNode boss, ReferenceRng rng)
    {
        ForEachInRow(grid, mapLength - 1, p => { p.PointType = MapPointType.RestSite; p.CanBeModified = false; });
        ForEachInRow(grid, 1, p => { p.PointType = MapPointType.Monster; p.CanBeModified = false; });
        var queue = new Queue<MapPointType>();
        for (int i = 0; i < counts.NumOfRests; i++) queue.Enqueue(MapPointType.RestSite);
        for (int i = 0; i < counts.NumOfShops; i++) queue.Enqueue(MapPointType.Shop);
        for (int i = 0; i < counts.NumOfElites; i++) queue.Enqueue(MapPointType.Elite);
        for (int i = 0; i < counts.NumOfUnknowns; i++) queue.Enqueue(MapPointType.Unknown);
        for (int pass = 0; pass < 3 && queue.Count > 0; pass++)
        {
            List<MapNode> candidates = GridNodes(grid).Where(p => p.PointType == MapPointType.Unassigned).ToList();
            StableShuffle(candidates, rng);
            foreach (MapNode point in candidates)
            {
                if (queue.Count == 0) break;
                MapPointType next = GetNextValidPointType(queue, point, mapLength, counts);
                if (next != MapPointType.Unassigned) point.PointType = next;
            }
        }
        foreach (MapNode point in GridNodes(grid).Where(p => p.PointType == MapPointType.Unassigned))
            point.PointType = MapPointType.Monster;
        boss.PointType = MapPointType.Boss;
        start.PointType = MapPointType.Ancient;
    }
}
