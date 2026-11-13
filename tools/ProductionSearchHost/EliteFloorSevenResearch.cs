using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.FamilyExecution;

// One-off H03 research entry. Room counts are observations, never predicates.
internal static class EliteFloorSevenResearch
{
    private const int EliteRow = 6;
    private static readonly ActContext Context = new(ActKind.Overgrowth, 0, HasSwarmingElites: true);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Point(int Floor, int Column, string Room);
    private sealed record Prefix(Point[] Nodes, int Monsters, int Unknowns);
    private sealed record Candidate(string Seed, ulong Ordinal, int RngCalls, Point[] FirstElites,
        int PathCount, int MinMonsters, int MaxMonsters, int MinUnknowns, int MaxUnknowns,
        Prefix[] Paths, object[] Map, string GraphSha256);

    internal static void Run(string output, ulong begin, int count, int target, int workers)
    {
        if (count <= 0 || target <= 0 || workers is < 1 or > 32) throw new ArgumentOutOfRangeException();
        ulong end = checked(begin + (ulong)count);
        if (end > VisibleSeedCandidateCodec.SpaceSize(Beta111Profile.Instance)) throw new ArgumentOutOfRangeException(nameof(count));
        Directory.CreateDirectory(output);
        CheckPredicate();
        string productMvid = typeof(BoundedActMap).Assembly.ManifestModule.ModuleVersionId.ToString();
        var timer = Stopwatch.StartNew();
        var found = new List<Candidate>();
        ulong next = begin;
        var maps = new ConcurrentBag<BoundedActMap>();
        using var rows = new StreamWriter(Path.Combine(output, "candidates.jsonl")) { AutoFlush = true };
        while (next < end && found.Count < target)
        {
            ulong batchBegin = next;
            int size = (int)Math.Min(8192UL, end - next);
            var hits = new ConcurrentBag<ulong>();
            Parallel.For(0, size, new ParallelOptions { MaxDegreeOfParallelism = workers },
                () => maps.TryTake(out var reused) ? reused : new BoundedActMap(Context),
                (index, _, map) =>
                {
                    ulong ordinal = batchBegin + (ulong)index;
                    string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                    map.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(seed));
                    if (Matches(map.SnapshotFinalRouteGraph().Start)) hits.Add(ordinal);
                    return map;
                }, map => maps.Add(map));
            next += (ulong)size;
            foreach (ulong ordinal in hits.Order())
            {
                Candidate candidate = Verify(ordinal);
                found.Add(candidate);
                rows.WriteLine(JsonSerializer.Serialize(candidate));
                Console.WriteLine($"EliteFloorSevenHit seed={candidate.Seed};paths={candidate.PathCount};monsters={candidate.MinMonsters}..{candidate.MaxMonsters};unknowns={candidate.MinUnknowns}..{candidate.MaxUnknowns}");
            }
            WriteSummary();
            Console.WriteLine($"EliteFloorSevenProgress scanned={next-begin};hits={found.Count};elapsedMs={timer.Elapsed.TotalMilliseconds:F1}");
        }
        WriteSummary();

        void WriteSummary()
        {
            var summary = new
            {
                Config = new { Game = "0.111.0", Character = "CHARACTER:SILENT", Ascension = 10, Players = 1,
                    FirstEliteFloor = 7, AllowedPrefixIcons = new[] { "Monster", "Unknown" }, RoomCounts = "UnrestrictedPerPath",
                    AllPaths = true, MapModifiers = "Standard pre-hook map", Rewards = "Not evaluated", workers, target },
                BeginOrdinal = begin, RequestedEndExclusive = end, ScannedEndExclusive = next, Scanned = next - begin,
                BeginSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, begin),
                LastScannedSeed = next > begin ? VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, next - 1) : null,
                ElapsedMs = timer.Elapsed.TotalMilliseconds, Found = found.Count,
                TargetReached = found.Count >= target, RangeExhausted = next == end,
                ProductMvid = productMvid,
                Verification = "Every hit independently regenerated with Reference mode, full graph/RNG compared and every first-Elite path enumerated",
                Candidates = found.Select(c => new { c.Seed, c.Ordinal, c.FirstElites, c.PathCount,
                    c.MinMonsters, c.MaxMonsters, c.MinUnknowns, c.MaxUnknowns, c.GraphSha256 }).ToArray()
            };
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Json));
        }
    }

    private static bool Matches(MapNode start)
    {
        if (start.Children.Count == 0) return false;
        var visited = new HashSet<MapNode>();
        var pending = new Stack<MapNode>(start.Children);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (node.Row == EliteRow)
            {
                if (node.PointType != MapPointType.Elite) return false;
                continue;
            }
            if (node.Row is < 1 or >= EliteRow || node.PointType is not (MapPointType.Monster or MapPointType.Unknown)
                || node.Children.Count == 0) return false;
            foreach (var child in node.Children) pending.Push(child);
        }
        return true;
    }

    private static Candidate Verify(ulong ordinal)
    {
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
        var reference = Beta111StandardMapGenerator.GenerateStandardMap(seed, Context, mode: MapReplayMode.Reference);
        var bounded = new BoundedActMap(Context);
        bounded.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(seed));
        string expected = Graph(reference.Start);
        if (expected != Graph(bounded.SnapshotFinalRouteGraph().Start) || reference.MapRngCalls != bounded.RngCalls)
            throw new InvalidDataException("EliteFloorSevenResearch.FullGraphMismatch:" + seed);

        var paths = new List<Prefix>();
        var path = new List<MapNode>();
        void Walk(MapNode node)
        {
            path.Add(node);
            if (node.PointType == MapPointType.Elite || node.Children.Count == 0)
            {
                if (node.PointType != MapPointType.Elite || node.Row != EliteRow || path.Count != EliteRow ||
                    !path.Select(p => p.Row).SequenceEqual(Enumerable.Range(1, EliteRow)) ||
                    path.Take(EliteRow - 1).Any(p => p.PointType is not (MapPointType.Monster or MapPointType.Unknown)) ||
                    path[0].PointType != MapPointType.Monster)
                    throw new InvalidDataException("EliteFloorSevenResearch.ReferencePathRejected:" + seed);
                paths.Add(new(path.Select(ToPoint).ToArray(), path.Count(p => p.PointType == MapPointType.Monster),
                    path.Count(p => p.PointType == MapPointType.Unknown)));
            }
            else foreach (var child in node.Children) Walk(child);
            path.RemoveAt(path.Count - 1);
        }
        foreach (var first in reference.Start.Children) Walk(first);
        if (paths.Count == 0) throw new InvalidDataException("EliteFloorSevenResearch.NoPaths:" + seed);
        return new(seed, ordinal, reference.MapRngCalls,
            paths.Select(p => p.Nodes[^1]).Distinct().OrderBy(p => p.Column).ToArray(), paths.Count,
            paths.Min(p => p.Monsters), paths.Max(p => p.Monsters), paths.Min(p => p.Unknowns), paths.Max(p => p.Unknowns),
            paths.ToArray(), Nodes(reference.Start).Select(n => (object)new { Point = ToPoint(n), n.CanBeModified,
                Children = n.Children.OrderBy(c => c.Row).ThenBy(c => c.Col).Select(ToPoint).ToArray() }).ToArray(),
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected))));
    }

    private static Point ToPoint(MapNode node) => new(node.Row + 1, node.Col + 1, node.PointType.ToString());
    private static IEnumerable<MapNode> Nodes(MapNode start)
    {
        var seen = new HashSet<MapNode>();
        var pending = new Stack<MapNode>(); pending.Push(start);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            yield return node;
            foreach (var child in node.Children) pending.Push(child);
        }
    }
    private static string Graph(MapNode start) => string.Join("|", Nodes(start).OrderBy(n => n.Row).ThenBy(n => n.Col)
        .Select(n => $"{n.Row},{n.Col}:{n.PointType}:{n.CanBeModified}>" + string.Join(";", n.Children
            .OrderBy(c => c.Row).ThenBy(c => c.Col).Select(c => $"{c.Row},{c.Col}"))));

    private static void CheckPredicate()
    {
        var start = new MapNode(3, 0) { PointType = MapPointType.Ancient };
        var first = new MapNode(3, 1) { PointType = MapPointType.Monster };
        start.Children.Add(first);
        for (int col = 2; col <= 3; col++)
        {
            var previous = first;
            for (int row = 2; row <= EliteRow; row++)
            {
                var node = new MapNode(col, row) { PointType = row == EliteRow ? MapPointType.Elite :
                    col == 2 ? MapPointType.Monster : MapPointType.Unknown };
                previous.Children.Add(node); previous = node;
            }
        }
        if (!Matches(start)) throw new InvalidDataException("EliteFloorSevenResearch.VariableCountsSanityFailed");
        var alternate = first.Children.First();
        foreach (var forbidden in new[] { MapPointType.Shop, MapPointType.RestSite, MapPointType.Elite })
        {
            alternate.PointType = forbidden;
            if (Matches(start)) throw new InvalidDataException("EliteFloorSevenResearch.ForbiddenBranchSanityFailed");
        }
        Console.WriteLine("EliteFloorSevenPredicateChecks=PASS;variableCounts=true;everyBranch=true;earlyEliteRejected=true");
    }
}
