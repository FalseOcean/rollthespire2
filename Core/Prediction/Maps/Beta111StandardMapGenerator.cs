using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Core.Prediction.Maps;

internal enum MapReplayMode { Reference, PackedSegmentDedup }

internal enum ActKind
{
    Overgrowth,
    Underdocks,
    Hive,
    Glory
}

// Numeric order mirrors MegaCrit.Sts2.Core.Map.MapPointType.
internal enum MapPointType
{
    Unassigned,
    Unknown,
    Shop,
    Treasure,
    RestSite,
    Monster,
    Elite,
    Boss,
    Ancient
}

/// <summary>
/// Inputs consumed by StandardActMap itself. Act identity selection and Unknown resolution are deliberately out of scope.
/// </summary>
internal sealed record ActContext(
    ActKind Act,
    int ActIndex,
    bool IsMultiplayer = false,
    bool HasSwarmingElites = false,
    bool HasSecondBoss = false,
    bool ReplaceTreasureWithElites = false);

internal sealed class MapNode : IComparable<MapNode>
{
    internal MapNode(int col, int row)
    {
        RawCol = col;
        RawRow = row;
        Col = col;
        Row = row;
        SyntheticLogicalKey = $"r{row}:c{col}";
    }

    // Harness-only capture: Beta111 MapPoint has no equivalent immutable field.
    internal string SyntheticLogicalKey { get; }
    internal int RawRow { get; }
    internal int RawCol { get; }
    internal int FinalRow => Row;
    internal int FinalCol => Col;
    internal int Row { get; set; }
    internal int Col { get; set; }
    internal MapPointType PointType { get; set; }
    internal bool CanBeModified { get; set; } = true;
    internal HashSet<MapNode> Parents { get; } = new();
    internal HashSet<MapNode> Children { get; } = new();

    public int CompareTo(MapNode? other)
    {
        if (other is null) return 1;
        int byCol = Col.CompareTo(other.Col);
        return byCol != 0 ? byCol : Row.CompareTo(other.Row);
    }

    public override string ToString() => $"({Row},{Col}) {PointType}";
}

internal sealed class GeneratedStandardMap
{
    internal GeneratedStandardMap(string seed, ActContext context, ReferenceRng rng, MapNode?[,] grid,
        MapNode start, MapNode boss, MapNode? secondBoss, HashSet<MapNode> startMapPoints, MapPointTypeCounts counts)
    {
        Seed = seed;
        Context = context;
        MapRngCalls = rng.Counter;
        Grid = grid;
        Start = start;
        Boss = boss;
        SecondBoss = secondBoss;
        StartMapPoints = startMapPoints;
        Counts = counts;
    }

    internal string Seed { get; }
    internal ActContext Context { get; }
    internal int MapRngCalls { get; }
    internal MapNode?[,] Grid { get; }
    internal MapNode Start { get; }
    internal MapNode Boss { get; }
    internal MapNode? SecondBoss { get; }
    internal IReadOnlySet<MapNode> StartMapPoints { get; }
    internal MapPointTypeCounts Counts { get; }

    internal IEnumerable<MapNode> GridNodes()
    {
        for (int col = 0; col < Grid.GetLength(0); col++)
            for (int row = 0; row < Grid.GetLength(1); row++)
                if (Grid[col, row] is { } node)
                    yield return node;
    }

    internal IEnumerable<MapNode> AllNodes()
    {
        yield return Start;
        foreach (MapNode node in GridNodes()) yield return node;
        yield return Boss;
        if (SecondBoss is not null) yield return SecondBoss;
    }

}

internal sealed class MapPointTypeCounts
{
    internal int NumOfElites { get; init; }
    internal int NumOfShops { get; } = 3;
    internal int NumOfUnknowns { get; }
    internal int NumOfRests { get; }
    internal HashSet<MapPointType> PointTypesThatIgnoreRules { get; init; } = new();

    internal MapPointTypeCounts(int unknownCount, int restCount, int eliteCount)
    {
        NumOfUnknowns = unknownCount;
        NumOfRests = restCount;
        NumOfElites = eliteCount;
    }

    internal bool ShouldIgnoreMapPointRulesForMapPointType(MapPointType type) =>
        PointTypesThatIgnoreRules.Contains(type);
}

internal static class Beta111StandardMapGenerator
{
    private const int MapWidth = 7;
    private const int PathIterations = 7;

    private static readonly HashSet<MapPointType> LowerRestrictions = new()
    {
        MapPointType.RestSite, MapPointType.Elite
    };

    private static readonly HashSet<MapPointType> UpperRestrictions = new()
    {
        MapPointType.RestSite
    };

    private static readonly HashSet<MapPointType> ParentRestrictions = new()
    {
        MapPointType.Elite, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop
    };

    private static readonly HashSet<MapPointType> ChildRestrictions = new()
    {
        MapPointType.Elite, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop
    };

    private static readonly HashSet<MapPointType> SiblingRestrictions = new()
    {
        MapPointType.RestSite, MapPointType.Monster, MapPointType.Unknown, MapPointType.Elite, MapPointType.Shop
    };

    internal static GeneratedStandardMap GenerateStandardMap(string seed, ActContext context,
        CancellationToken cancellationToken = default,
        MapReplayMode mode = MapReplayMode.PackedSegmentDedup)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateContext(context);
        var profile = Beta111Profile.Instance;
        if (!profile.TryCanonicalizeSeed(seed, out string canonical, out string issue))
            throw new ArgumentException(issue, nameof(seed));
        cancellationToken.ThrowIfCancellationRequested();
        ReferenceRng rng = new(profile.DeriveNamedStreamSeed(profile.ComputeRootSeed(canonical), $"act_{context.ActIndex + 1}_map"), cancellationToken, mode);
        int mapLength = GetNumberOfRooms(context.Act, context.IsMultiplayer) + 1;
        MapNode?[,] grid = new MapNode?[MapWidth, mapLength];
        MapNode boss = new(MapWidth / 2, mapLength);
        MapNode start = new(MapWidth / 2, 0);
        MapNode? secondBoss = context.HasSecondBoss ? new(MapWidth / 2, mapLength + 1) : null;
        HashSet<MapNode> startMapPoints = new();
        MapPointTypeCounts counts = GetMapPointTypes(context, rng);
        GenerateMap(grid, rng, startMapPoints, start, boss, secondBoss);
        AssignPointTypes(grid, mapLength, context, counts, start, boss, secondBoss, rng);
        PruneAndRepair(grid, startMapPoints, start, boss, counts, rng);
        CenterGrid(grid);
        SpreadAdjacentMapPoints(grid);
        StraightenPaths(grid);
        cancellationToken.ThrowIfCancellationRequested();
        return new GeneratedStandardMap(canonical, context, rng, grid, start, boss, secondBoss, startMapPoints, counts);
    }

    private static void ValidateContext(ActContext context)
    {
        int expected = context.Act switch
        {
            ActKind.Overgrowth or ActKind.Underdocks => 0,
            ActKind.Hive => 1,
            ActKind.Glory => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        if (context.ActIndex != expected)
            throw new ArgumentException($"{context.Act} requires ActIndex {expected} in Beta111.");
    }

    // Mirrors ActModel.GetNumberOfRooms and each Beta111 ActModel.BaseNumberOfRooms.
    internal static int GetNumberOfRooms(ActKind act, bool isMultiplayer)
    {
        int baseRooms = act switch
        {
            ActKind.Overgrowth or ActKind.Underdocks => 15,
            ActKind.Hive => 14,
            ActKind.Glory => 13,
            _ => throw new ArgumentOutOfRangeException(nameof(act))
        };
        return isMultiplayer ? baseRooms - 1 : baseRooms;
    }

    // Mirrors Overgrowth/Underdocks/Hive/Glory.GetMapPointTypes and StandardRandomUnknownCount.
    private static MapPointTypeCounts GetMapPointTypes(ActContext context, ReferenceRng rng)
    {
        int restCount;
        int unknownCount;
        switch (context.Act)
        {
            case ActKind.Overgrowth:
            case ActKind.Underdocks:
                restCount = rng.NextGaussianInt(7, 1, 6, 7);
                unknownCount = StandardRandomUnknownCount(rng);
                break;
            case ActKind.Hive:
                restCount = rng.NextGaussianInt(6, 1, 6, 7);
                unknownCount = StandardRandomUnknownCount(rng) - 1;
                break;
            case ActKind.Glory:
                restCount = rng.NextInt(5, 7);
                unknownCount = StandardRandomUnknownCount(rng) - 1;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        int elites = context.HasSwarmingElites ? (int)Math.Round(5f * 1.6f) : 5;
        return new MapPointTypeCounts(unknownCount, restCount, elites);
    }

    private static int StandardRandomUnknownCount(ReferenceRng rng) => rng.NextGaussianInt(12, 1, 10, 14);

    // Mirrors StandardActMap.GenerateMap.
    private static void GenerateMap(MapNode?[,] grid, ReferenceRng rng, HashSet<MapNode> startMapPoints,
        MapNode start, MapNode boss, MapNode? secondBoss)
    {
        for (int i = 0; i < PathIterations; i++)
        {
            MapNode pathStart = GetOrCreatePoint(grid, rng.NextInt(0, MapWidth), 1);
            if (i == 1)
            {
                while (startMapPoints.Contains(pathStart))
                    pathStart = GetOrCreatePoint(grid, rng.NextInt(0, MapWidth), 1);
            }
            startMapPoints.Add(pathStart);
            PathGenerate(grid, rng, pathStart);
        }

        ForEachInRow(grid, grid.GetLength(1) - 1, node => AddChildPoint(node, boss));
        if (secondBoss is not null) AddChildPoint(boss, secondBoss);
        ForEachInRow(grid, 1, node => AddChildPoint(start, node));
    }

    // Mirrors StandardActMap.PathGenerate.
    private static void PathGenerate(MapNode?[,] grid, ReferenceRng rng, MapNode startingPoint)
    {
        MapNode current = startingPoint;
        while (current.Row < grid.GetLength(1) - 1)
        {
            (int col, int row) = GenerateNextCoord(grid, rng, current);
            MapNode next = GetOrCreatePoint(grid, col, row);
            AddChildPoint(current, next);
            current = next;
        }
    }

    // Mirrors StandardActMap.GenerateNextCoord. StableShuffle consumes two RNG calls for the three directions.
    private static (int col, int row) GenerateNextCoord(MapNode?[,] grid, ReferenceRng rng, MapNode current)
    {
        int col = current.Col;
        int left = Math.Max(0, col - 1);
        int right = Math.Min(col + 1, MapWidth - 1);
        List<int> directions = new() { -1, 0, 1 };
        StableShuffle(directions, rng);

        foreach (int direction in directions)
        {
            int targetCol = direction switch
            {
                -1 => left,
                0 => col,
                1 => right,
                _ => throw new InvalidOperationException()
            };
            if (!HasInvalidCrossover(grid, current, targetCol))
                return (targetCol, current.Row + 1);
        }
        throw new InvalidOperationException("Cannot find next node");
    }

    // Mirrors StandardActMap.HasInvalidCrossover, including the source's num == 7 condition.
    private static bool HasInvalidCrossover(MapNode?[,] grid, MapNode current, int targetCol)
    {
        int delta = targetCol - current.Col;
        if (delta == 0 || delta == 7) return false;
        MapNode? other = grid[targetCol, current.Row];
        if (other is null) return false;
        return other.Children.Any(child => child.Col - other.Col == -delta);
    }

    // Mirrors StandardActMap.AssignPointTypes and AssignRemainingTypesToRandomPoints.
    private static void AssignPointTypes(MapNode?[,] grid, int mapLength, ActContext context, MapPointTypeCounts counts,
        MapNode start, MapNode boss, MapNode? secondBoss, ReferenceRng rng)
    {
        ForEachInRow(grid, mapLength - 1, node => { node.PointType = MapPointType.RestSite; node.CanBeModified = false; });
        ForEachInRow(grid, mapLength - 7, node =>
        {
            node.PointType = context.ReplaceTreasureWithElites ? MapPointType.Elite : MapPointType.Treasure;
            node.CanBeModified = false;
        });
        ForEachInRow(grid, 1, node => { node.PointType = MapPointType.Monster; node.CanBeModified = false; });

        List<MapPointType> placementQueue = new();
        placementQueue.AddRange(Enumerable.Repeat(MapPointType.RestSite, counts.NumOfRests));
        placementQueue.AddRange(Enumerable.Repeat(MapPointType.Shop, counts.NumOfShops));
        placementQueue.AddRange(Enumerable.Repeat(MapPointType.Elite, counts.NumOfElites));
        placementQueue.AddRange(Enumerable.Repeat(MapPointType.Unknown, counts.NumOfUnknowns));
        AssignRemainingTypesToRandomPoints(grid, mapLength, counts, new Queue<MapPointType>(placementQueue), rng);

        foreach (MapNode node in GridNodes(grid).Where(n => n.PointType == MapPointType.Unassigned))
            node.PointType = MapPointType.Monster;

        boss.PointType = MapPointType.Boss;
        start.PointType = MapPointType.Ancient;
        if (secondBoss is not null) secondBoss.PointType = MapPointType.Boss;
    }

    private static void AssignRemainingTypesToRandomPoints(MapNode?[,] grid, int mapLength, MapPointTypeCounts counts,
        Queue<MapPointType> queue, ReferenceRng rng)
    {
        for (int i = 0; i < 3; i++)
        {
            if (queue.Count <= 0) break;
            List<MapNode> candidates = GridNodes(grid).Where(n => n.PointType == MapPointType.Unassigned).ToList();
            StableShuffle(candidates, rng);
            foreach (MapNode node in candidates)
            {
                if (queue.Count == 0) break;
                node.PointType = GetNextValidPointType(queue, node, mapLength, counts);
            }
        }
    }

    // Mirrors StandardActMap.GetNextValidPointType.
    private static MapPointType GetNextValidPointType(Queue<MapPointType> queue, MapNode node, int mapLength,
        MapPointTypeCounts counts)
    {
        for (int i = 0; i < queue.Count; i++)
        {
            MapPointType candidate = queue.Dequeue();
            if (counts.ShouldIgnoreMapPointRulesForMapPointType(candidate) || IsValidPointType(node, mapLength, counts, candidate))
                return candidate;
            queue.Enqueue(candidate);
        }
        return MapPointType.Unassigned;
    }

    // Mirrors StandardActMap.IsValidPointType and all five predicates.
    private static bool IsValidPointType(MapNode node, int mapLength, MapPointTypeCounts counts, MapPointType candidate)
    {
        if (node.Row >= mapLength - 3 && UpperRestrictions.Contains(candidate)) return false;
        if (node.Row < 6 && LowerRestrictions.Contains(candidate)) return false;
        if (ParentRestrictions.Contains(candidate) && node.Parents.Concat(node.Children).Any(n => n.PointType == candidate)) return false;
        if (ChildRestrictions.Contains(candidate) && node.Children.Any(n => n.PointType == candidate)) return false;
        if (SiblingRestrictions.Contains(candidate) && GetSiblings(node).Any(n => n.PointType == candidate)) return false;
        return true;
    }

    private static IEnumerable<MapNode> GetSiblings(MapNode node) =>
        node.Parents.SelectMany(parent => parent.Children).Where(sibling => !ReferenceEquals(sibling, node));

    // Mirrors MapPathPruning.PruneAndRepair.
    private static void PruneAndRepair(MapNode?[,] grid, HashSet<MapNode> startMapPoints, MapNode start, MapNode boss,
        MapPointTypeCounts counts, ReferenceRng rng)
    {
        for (int i = 0; i < 3; i++)
        {
            rng.CheckCancellation();
            PruneDuplicateSegments(grid, startMapPoints, start, rng);
            bool repaired = RepairPrunedPointTypes(grid, counts, rng);
            if (!repaired) break;
        }
    }

    // Mirrors MapPathPruning.RepairPrunedPointTypes.
    private static bool RepairPrunedPointTypes(MapNode?[,] grid, MapPointTypeCounts counts, ReferenceRng rng)
    {
        bool repaired = false;
        repaired |= RepairPointType(grid, MapPointType.Shop, counts.NumOfShops, counts, rng);
        repaired |= RepairPointType(grid, MapPointType.Elite, counts.NumOfElites, counts, rng);
        repaired |= RepairPointType(grid, MapPointType.RestSite, counts.NumOfRests, counts, rng);
        return repaired | RepairPointType(grid, MapPointType.Unknown, counts.NumOfUnknowns, counts, rng);
    }

    // Mirrors MapPathPruning.RepairPointType.
    private static bool RepairPointType(MapNode?[,] grid, MapPointType target, int targetCount, MapPointTypeCounts counts,
        ReferenceRng rng)
    {
        int missing = targetCount - GridNodes(grid).Count(n => n.PointType == target);
        if (missing <= 0) return false;
        bool repaired = false;
        List<MapNode> candidates = GridNodes(grid)
            .Where(n => n.PointType == MapPointType.Monster && n.CanBeModified).ToList();
        StableShuffle(candidates, rng);
        int mapLength = grid.GetLength(1);
        foreach (MapNode node in candidates)
        {
            if (missing == 0) break;
            if (IsValidPointType(node, mapLength, counts, target))
            {
                node.PointType = target;
                missing--;
                repaired = true;
            }
        }
        return repaired;
    }

    // Mirrors MapPathPruning.PruneDuplicateSegments.
    private static void PruneDuplicateSegments(MapNode?[,] grid, HashSet<MapNode> startMapPoints, MapNode start, ReferenceRng rng)
    {
        int iterations = 0;
        List<List<MapNode[]>> matches = FindMatchingSegments(start, rng);
        while (PrunePaths(grid, startMapPoints, matches, rng))
        {
            rng.CheckCancellation();
            iterations++;
            if (iterations > 50)
                throw new InvalidOperationException($"Unable to prune matching segments in {iterations} iterations");
            matches = FindMatchingSegments(start, rng);
        }
    }

    // Mirrors MapPathPruning.FindMatchingSegments and FindAllPaths.
    private static List<List<MapNode[]>> FindMatchingSegments(MapNode start, ReferenceRng rng) =>
        (rng.Mode == MapReplayMode.PackedSegmentDedup
            ? PackedPruneDiscovery.Find(start, rng)
            : FindMatchingSegmentDictionary(start, rng))
            .Values
            .Where(segmentList => segmentList.Count > 1)
            .ToList();

    private static SortedDictionary<string, List<MapNode[]>> FindMatchingSegmentDictionary(MapNode start, ReferenceRng rng)
    {
        List<List<MapNode>> paths = FindAllPaths(start, rng);
        SortedDictionary<string, List<MapNode[]>> segments = new(StringComparer.Ordinal);
        foreach (List<MapNode> path in paths)
        {
            rng.CheckCancellation();
            AddSegmentsToDictionary(path, segments);
        }
        return segments;
    }

    private static List<List<MapNode>> FindAllPaths(MapNode current, ReferenceRng rng)
    {
        rng.CheckCancellation();
        if (current.PointType == MapPointType.Boss)
            return new List<List<MapNode>> { new() { current } };

        List<List<MapNode>> result = new();
        foreach (MapNode child in current.Children)
        {
            foreach (List<MapNode> childPath in FindAllPaths(child, rng))
            {
                List<MapNode> path = new() { current };
                path.AddRange(childPath);
                result.Add(path);
            }
        }
        return result;
    }

    private static void AddSegmentsToDictionary(IReadOnlyList<MapNode> path, IDictionary<string, List<MapNode[]>> segments)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            if (!IsValidSegmentStart(path[i])) continue;
            for (int j = 2; j < path.Count - i; j++)
            {
                MapNode end = path[i + j];
                if (!IsValidSegmentEnd(end)) continue;
                MapNode[] segment = path.Skip(i).Take(j + 1).ToArray();
                string key = GenerateSegmentKey(segment);
                if (!segments.TryGetValue(key, out List<MapNode[]>? existing))
                    segments[key] = existing = new List<MapNode[]>();
                if (!existing.Any(other => Overlapping(other, segment))) existing.Add(segment);
            }
        }
    }

    private static bool IsValidSegmentStart(MapNode node) => node.Children.Count > 1 || node.Row == 0;
    private static bool IsValidSegmentEnd(MapNode node) => node.Parents.Count >= 2;

    private static string GenerateSegmentKey(IReadOnlyList<MapNode> segment)
    {
        MapNode first = segment[0];
        MapNode last = segment[^1];
        string prefix = first.Row == 0
            ? $"{first.Row}-{last.Col},{last.Row}-"
            : $"{first.Col},{first.Row}-{last.Col},{last.Row}-";
        return prefix + string.Join(",", segment.Select(node => (int)node.PointType));
    }

    private static bool Overlapping(IReadOnlyList<MapNode> a, IReadOnlyList<MapNode> b)
    {
        if (a.Count < 3 || b.Count < 3) return false;
        for (int i = 1; i <= a.Count - 2; i++)
            if (ReferenceEquals(a[i], b[i])) return true;
        return false;
    }

    // Mirrors MapPathPruning.PrunePaths, PruneAllButLast, and PruneSegment.
    private static bool PrunePaths(MapNode?[,] grid, HashSet<MapNode> startMapPoints, IEnumerable<List<MapNode[]>> matches,
        ReferenceRng rng)
    {
        foreach (List<MapNode[]> matchingSegment in matches)
        {
            UnstableShuffle(matchingSegment, rng);
            if (PruneAllButLast(grid, startMapPoints, matchingSegment) != 0) return true;
            if (BreakAParentChildRelationshipInAnySegment(matchingSegment)) return true;
        }
        return false;
    }

    private static int PruneAllButLast(MapNode?[,] grid, HashSet<MapNode> startMapPoints, IReadOnlyList<MapNode[]> matches)
    {
        int removed = 0;
        foreach (MapNode[] match in matches)
        {
            if (removed == matches.Count - 1) return removed;
            if (PruneSegment(grid, startMapPoints, match)) removed++;
        }
        return removed;
    }

    private static bool PruneSegment(MapNode?[,] grid, HashSet<MapNode> startMapPoints, IReadOnlyList<MapNode> segment)
    {
        bool result = false;
        for (int i = 0; i < segment.Count - 1; i++)
        {
            MapNode node = segment[i];
            if (!IsInMap(grid, node)) return true;
            if (node.Children.Count > 1 || node.Parents.Count > 1 || node.Parents.Any(parent => parent.Children.Count == 1 && !IsRemoved(grid, parent)))
                continue;

            IEnumerable<MapNode> remaining = segment.Skip(i);
            if (remaining.Any(candidate => candidate.Children.Count > 1 && candidate.Parents.Count == 1)) continue;
            if (segment[^1].Parents.Count == 1) return false;
            if (!node.Children.Where(child => !segment.Contains(child)).Any(child => child.Parents.Count == 1))
            {
                RemovePoint(grid, startMapPoints, node);
                result = true;
            }
        }
        return result;
    }

    private static void RemovePoint(MapNode?[,] grid, HashSet<MapNode> startMapPoints, MapNode node)
    {
        grid[node.Col, node.Row] = null;
        startMapPoints.Remove(node);
        foreach (MapNode child in node.Children.ToList()) RemoveChildPoint(node, child);
        foreach (MapNode parent in node.Parents.ToList()) RemoveChildPoint(parent, node);
    }

    private static bool IsInMap(MapNode?[,] grid, MapNode node)
    {
        if (grid[node.Col, node.Row] is null && node.PointType != MapPointType.Ancient)
            return node.PointType == MapPointType.Boss;
        return true;
    }

    private static bool IsRemoved(MapNode?[,] grid, MapNode node) => grid[node.Col, node.Row] is null;

    private static bool BreakAParentChildRelationshipInAnySegment(IEnumerable<MapNode[]> matches)
    {
        foreach (MapNode[] segment in matches)
            if (BreakAParentChildRelationshipInSegment(segment)) return true;
        return false;
    }

    private static bool BreakAParentChildRelationshipInSegment(IReadOnlyList<MapNode> segment)
    {
        bool result = false;
        for (int i = 0; i < segment.Count - 1; i++)
        {
            MapNode node = segment[i];
            if (node.Children.Count >= 2)
            {
                MapNode child = segment[i + 1];
                if (child.Parents.Count != 1)
                {
                    RemoveChildPoint(node, child);
                    result = true;
                }
            }
        }
        return result;
    }

    // Mirrors MapPostProcessing.CenterGrid.
    private static void CenterGrid(MapNode?[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        bool leftEmpty = IsColumnEmpty(grid, 0) && IsColumnEmpty(grid, 1);
        bool rightEmpty = IsColumnEmpty(grid, width - 1) && IsColumnEmpty(grid, width - 2);
        int shift = leftEmpty && !rightEmpty ? -1 : !leftEmpty && rightEmpty ? 1 : 0;
        if (shift == 0) return;

        if (shift > 0)
        {
            for (int row = 0; row < height; row++)
                for (int col = width - 1; col >= 0; col--)
                    MoveGridCell(grid, col, row, col + shift, row);
        }
        else
        {
            for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                    MoveGridCell(grid, col, row, col + shift, row);
        }
    }

    private static void MoveGridCell(MapNode?[,] grid, int oldCol, int oldRow, int newCol, int newRow)
    {
        MapNode? node = grid[oldCol, oldRow];
        grid[oldCol, oldRow] = null;
        if (newCol < 0 || newCol >= grid.GetLength(0)) return;
        grid[newCol, newRow] = node;
        if (node is not null) node.Col = newCol;
    }

    // Mirrors MapPostProcessing.SpreadAdjacentMapPoints.
    private static void SpreadAdjacentMapPoints(MapNode?[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        for (int row = 0; row < height; row++)
        {
            List<MapNode> rowNodes = new();
            for (int col = 0; col < width; col++)
                if (grid[col, row] is { } node) rowNodes.Add(node);

            bool moved;
            do
            {
                moved = false;
                foreach (MapNode node in rowNodes)
                {
                    int oldCol = node.Col;
                    HashSet<int> allowed = GetAllowedPositions(node, width);
                    int bestCol = oldCol;
                    int bestGap = ComputeGap(oldCol, rowNodes, node);
                    foreach (int candidate in allowed)
                    {
                        if (candidate != oldCol && (grid[candidate, row] is null || ReferenceEquals(grid[candidate, row], node)))
                        {
                            int gap = ComputeGap(candidate, rowNodes, node);
                            if (gap > bestGap)
                            {
                                bestCol = candidate;
                                bestGap = gap;
                            }
                        }
                    }
                    if (bestCol != oldCol)
                    {
                        grid[oldCol, row] = null;
                        grid[bestCol, row] = node;
                        node.Col = bestCol;
                        moved = true;
                    }
                }
            }
            while (moved);
        }
    }

    // Mirrors MapPostProcessing.StraightenPaths.
    private static void StraightenPaths(MapNode?[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        for (int row = 0; row < height; row++)
        for (int col = 0; col < width; col++)
        {
            MapNode? node = grid[col, row];
            if (node is null || node.Parents.Count != 1 || node.Children.Count != 1) continue;
            MapNode parent = node.Parents.First();
            MapNode child = node.Children.First();
            bool leftOfBoth = node.Col < child.Col && node.Col < parent.Col;
            bool rightOfBoth = node.Col > child.Col && node.Col > parent.Col;
            if (leftOfBoth && col < width - 1 && grid[col + 1, row] is null)
            {
                node.Col = col + 1;
                grid[col, row] = null;
                grid[col + 1, row] = node;
            }
            if (rightOfBoth && col > 0 && grid[col - 1, row] is null)
            {
                node.Col = col - 1;
                grid[col, row] = null;
                grid[col - 1, row] = node;
            }
        }
    }

    private static HashSet<int> GetAllowedPositions(MapNode node, int width)
    {
        HashSet<int> allowed = Enumerable.Range(0, width).ToHashSet();
        foreach (MapNode parent in node.Parents) allowed.IntersectWith(NeighborPositions(parent.Col, width));
        foreach (MapNode child in node.Children) allowed.IntersectWith(NeighborPositions(child.Col, width));
        return allowed;
    }

    private static HashSet<int> NeighborPositions(int col, int width) =>
        Enumerable.Range(-1, 3).Select(offset => col + offset).Where(candidate => candidate >= 0 && candidate < width).ToHashSet();

    private static int ComputeGap(int candidateCol, IEnumerable<MapNode> rowNodes, MapNode current)
    {
        int gap = int.MaxValue;
        foreach (MapNode node in rowNodes)
            if (!ReferenceEquals(node, current)) gap = Math.Min(gap, Math.Abs(candidateCol - node.Col));
        return gap;
    }

    private static bool IsColumnEmpty(MapNode?[,] grid, int col)
    {
        for (int row = 0; row < grid.GetLength(1); row++)
            if (grid[col, row] is not null) return false;
        return true;
    }

    private static MapNode GetOrCreatePoint(MapNode?[,] grid, int col, int row)
    {
        if (grid[col, row] is { } existing) return existing;
        MapNode node = new(col, row);
        grid[col, row] = node;
        return node;
    }

    private static void AddChildPoint(MapNode parent, MapNode child)
    {
        parent.Children.Add(child);
        child.Parents.Add(parent);
    }

    private static void RemoveChildPoint(MapNode parent, MapNode child)
    {
        parent.Children.Remove(child);
        child.Parents.Remove(parent);
    }

    private static IEnumerable<MapNode> GridNodes(MapNode?[,] grid)
    {
        for (int col = 0; col < grid.GetLength(0); col++)
            for (int row = 0; row < grid.GetLength(1); row++)
                if (grid[col, row] is { } node)
                    yield return node;
    }

    private static void ForEachInRow(MapNode?[,] grid, int row, Action<MapNode> action)
    {
        for (int col = 0; col < grid.GetLength(0); col++)
            if (grid[col, row] is { } node)
                action(node);
    }

    // Mirrors ListExtensions.StableShuffle then UnstableShuffle.
    private static void StableShuffle<T>(List<T> list, ReferenceRng rng) where T : IComparable<T>
    {
        List<T> sorted = list.ToList();
        sorted.Sort();
        for (int i = 0; i < list.Count; i++) list[i] = sorted[i];
        UnstableShuffle(list, rng);
    }

    // Mirrors ListExtensions.UnstableShuffle.
    private static void UnstableShuffle<T>(IList<T> list, ReferenceRng rng)
    {
        int count = list.Count;
        while (count > 1)
        {
            count--;
            int chosen = rng.NextInt(count + 1);
            (list[chosen], list[count]) = (list[count], list[chosen]);
        }
    }
}
