using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.FamilyExecution;

// Small correctness corpus only. No production registration, timing or actual-game runner.
internal static class MapRouteSequenceChecks
{
    private static readonly MapPointType[] RoomTypes =
    [MapPointType.Monster, MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop, MapPointType.Unknown];

    internal static int Run()
    {
        var cases = new List<string>();
        Adversarial(cases);
        SnapshotAdversarial(cases);
        int maps = 0, queries = 0;
        long completePaths = 0;
        foreach (bool multiplayer in new[] { false, true })
        foreach (ActKind act in Enum.GetValues<ActKind>())
        foreach (bool swarm in new[] { false, true })
        foreach (ulong ordinal in new[] { 0UL, 1UL })
        {
            int index = act is ActKind.Overgrowth or ActKind.Underdocks ? 0 : act == ActKind.Hive ? 1 : 2;
            CheckNatural(new ActContext(act, index, multiplayer, swarm), ordinal, ref queries, ref completePaths);
            maps++;
        }
        // Glory's second Boss is part of the complete witness, while both Bosses remain non-selectable.
        foreach (bool multiplayer in new[] { false, true })
            CheckNatural(new ActContext(ActKind.Glory, 2, multiplayer, true, true), 1, ref queries, ref completePaths);
        Console.WriteLine($"PASS map route sequence: {cases.Count} adversarial cases; {maps} natural SP/MP maps + 2 second-Boss maps; {queries} queries against {completePaths} complete paths; independent group-window/count oracle and MapPrediction/MapRouteSet witnesses.");
        return 0;
    }

    internal static void RunSemantics()
    {
        var cases = new List<string>();
        Adversarial(cases);
        SnapshotAdversarial(cases);
        var start = Node(0, MapPointType.Ancient); var boss = Node(2, MapPointType.Boss);
        Link(start, Node(1, MapPointType.Elite, 1), boss);
        Link(start, Node(1, MapPointType.Unknown, 5), boss);
        var signature = MapBasisScalars.Evaluate(start, boss);
        var drawing = MapRouteSequenceEvaluator.Evaluate(start, boss, new([MapPointType.Unknown]));
        Require(drawing.Matches && !drawing.Path.Contains(new MapPosition(1, 1)) && StandardMapFamily.Matches(
            [new(1, RolltheSpire2.Search.Semantics.StandardMapMetric.ReachableMaxElite,
                RolltheSpire2.Search.Semantics.StandardMapComparison.AtLeast, 1),
             new(0, RolltheSpire2.Search.Semantics.StandardMapMetric.ReachableMaxElite,
                RolltheSpire2.Search.Semantics.StandardMapComparison.AtLeast, 3, true)], _ => signature),
            "Independent property/total maxima must not require the drawing witness");
        Console.WriteLine($"MAP_ROUTE_SEMANTICS_PASS cases={cases.Count};naturalGenerationMatrix=false");
    }

    private static void CheckNatural(ActContext context, ulong ordinal, ref int queries, ref long completePaths)
    {
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
        var generated = Beta111StandardMapGenerator.GenerateStandardMap(seed, context, mode: MapReplayMode.Reference);
        var snapshot = Beta111MapPredictor.Predict(seed, RuntimeProfileId.Beta111,
            new ModelKey("ACT", context.Act.ToString().ToUpperInvariant()), context.ActIndex,
            context.HasSecondBoss ? 10 : context.HasSwarmingElites ? 1 : 0,
            context.IsMultiplayer ? 2 : 1, context.HasSecondBoss, default);
        var terminal = generated.SecondBoss ?? generated.Boss;
        string beforeGraph = Graph(generated.Start), beforeSnapshot = SnapshotGraph(snapshot);
        Require(beforeGraph == beforeSnapshot && generated.MapRngCalls == snapshot.RngCalls,
            $"Reference/snapshot graph and RNG parity {context} {seed}");
        FixedMarkers(generated);
        var paths = EnumeratePaths(generated.Start, terminal);
        Require(paths.Count > 0, "Natural map has no complete paths");
        var routeSet = new MapRouteSet(snapshot);
        Require(routeSet.Count == paths.Count, "MapRouteSet complete route count differs from independent enumeration");
        completePaths += paths.Count;
        var workloads = Workloads(paths[0], generated.Start, terminal).ToArray();
        Require(workloads.Length == 30, "Natural workload count changed");
        foreach (var query in workloads)
        {
            bool expected = paths.Any(path => Oracle(path, generated.Start, terminal, query));
            var nodeResult = MapRouteSequenceEvaluator.Evaluate(generated.Start, terminal, query);
            Validate(generated.Start, terminal, query, expected, nodeResult.Matches,
                nodeResult.Path, nodeResult.MatchedNodes, nodeResult.MatchedGroups, nodeResult.VisitedStates);
            var result = MapRouteSequenceEvaluator.Evaluate(generated, query);
            Validate(generated.Start, terminal, query, expected, result.Matches,
                result.Path, result.MatchedNodes, result.MatchedGroups, result.VisitedStates);
            var snapshotResult = MapRouteSequenceEvaluator.Evaluate(snapshot, query);
            Validate(generated.Start, terminal, query, expected, snapshotResult.Matches,
                snapshotResult.Path, snapshotResult.MatchedNodes, snapshotResult.MatchedGroups, snapshotResult.VisitedStates);
            Require(result.Matches == snapshotResult.Matches, "Generated/snapshot result mismatch");
            Require(result.Path.SequenceEqual(snapshotResult.Path) && result.MatchedNodes.SequenceEqual(snapshotResult.MatchedNodes),
                "Generated/snapshot witnesses must use the same deterministic coordinate order");
            if (context.HasSecondBoss)
            {
                // The terminal extension must preserve the sequence/count answer for the first-Boss prefix.
                var firstBossResult = MapRouteSequenceEvaluator.Evaluate(generated.Start, generated.Boss, query);
                Validate(generated.Start, generated.Boss, query, expected, firstBossResult.Matches,
                    firstBossResult.Path, firstBossResult.MatchedNodes, firstBossResult.MatchedGroups, firstBossResult.VisitedStates);
            }
            if (result.Matches)
            {
                Require(routeSet.IsLegal(result.Path) && routeSet.IsLegal(snapshotResult.Path), "Sequence witness is not a legal MapRouteSet route");
                if (context.HasSecondBoss)
                {
                    Require(result.Path[^1] == Position(terminal) && result.Path.Contains(Position(generated.Boss))
                        && snapshotResult.Path[^1] == Position(terminal) && snapshotResult.Path.Contains(Position(generated.Boss)),
                        "Second-Boss witness stops at first Boss");
                }
            }
            queries++;
        }
        Require(beforeGraph == Graph(generated.Start) && beforeSnapshot == SnapshotGraph(snapshot), "Evaluation mutated input graph/snapshot");
    }

    private static string Graph(MapNode start) => string.Join("\n", Reachable(start)
        .OrderBy(n => n.FinalRow).ThenBy(n => n.FinalCol).Select(n =>
            $"{n.FinalRow},{n.FinalCol}:{(int)n.PointType}:{n.CanBeModified}->" + string.Join(";",
                n.Children.OrderBy(c => c.FinalRow).ThenBy(c => c.FinalCol).Select(c => $"{c.FinalRow},{c.FinalCol}"))));

    private static string SnapshotGraph(MapPrediction map) => string.Join("\n", map.Points
        .OrderBy(n => n.Position.Row).ThenBy(n => n.Position.Column).Select(n =>
            $"{n.Position.Row},{n.Position.Column}:{(int)n.Type}:{n.CanBeModified}->" + string.Join(";",
                n.Children.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => $"{c.Row},{c.Column}"))));

    private static IEnumerable<MapNode> Reachable(MapNode start)
    {
        var seen = new HashSet<MapNode>(); var pending = new Stack<MapNode>(); pending.Push(start);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            yield return node;
            foreach (var child in node.Children) pending.Push(child);
        }
    }

    private static List<MapNode[]> EnumeratePaths(MapNode start, MapNode boss)
    {
        var paths = new List<MapNode[]>(); var prefix = new List<MapNode>();
        void Visit(MapNode node)
        {
            prefix.Add(node);
            if (node == boss) paths.Add(prefix.ToArray());
            else foreach (var child in node.Children) Visit(child);
            prefix.RemoveAt(prefix.Count - 1);
        }
        Visit(start); return paths;
    }

    private static MapPosition Position(MapNode node) => new(node.FinalCol, node.FinalRow);

    private static void SnapshotAdversarial(List<string> completed)
    {
        var graph = Chain((MapPointType.Elite, true), (MapPointType.Treasure, false), (MapPointType.RestSite, true));
        var snapshot = new MapPrediction("000000000000", new ModelKey("ACT", "OVERGROWTH"), 0, 7,
            Array.AsReadOnly(Reachable(graph.Start).Select(n => new MapPredictionPoint(Position(n), n.PointType, n.CanBeModified,
                Array.AsReadOnly(n.Children.Select(Position).ToArray()))).ToArray()), 0);
        var query = Grouped([MapPointType.RestSite, MapPointType.Elite]);
        var result = MapRouteSequenceEvaluator.Evaluate(snapshot, query);
        Validate(graph.Start, graph.Boss, query, true, result.Matches, result.Path, result.MatchedNodes, result.MatchedGroups, result.VisitedStates);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Expect<OperationCanceledException>(() => MapRouteSequenceEvaluator.Evaluate(snapshot, query, cancelled.Token));
        var duplicate = snapshot with { Points = snapshot.Points.Append(snapshot.Points[0]).ToArray() };
        Expect<ArgumentException>(() => MapRouteSequenceEvaluator.Evaluate(duplicate, query));
        var backward = snapshot with { Points = snapshot.Points.Select(p => p.Position == Position(graph.Boss)
            ? p with { Children = new[] { Position(graph.Start) } } : p).ToArray() };
        Expect<ArgumentException>(() => MapRouteSequenceEvaluator.Evaluate(backward, query));
        var missing = snapshot with { Points = snapshot.Points.Select(p => p.Position == Position(graph.Start)
            ? p with { Children = new[] { new MapPosition(99, 99) } } : p).ToArray() };
        Expect<ArgumentException>(() => MapRouteSequenceEvaluator.Evaluate(missing, query));
        completed.Add("snapshot fixed-node crossing/cancellation/duplicate/backward/missing DAG validation");
    }

    private static IEnumerable<MapRouteSequenceQuery> Workloads(MapNode[] witness, MapNode start, MapNode boss)
    {
        yield return new([]);
        foreach (MapPointType type in RoomTypes)
        {
            yield return new([type]);
            yield return new([type, type]);
        }
        yield return new([MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop]);
        yield return new([MapPointType.Shop, MapPointType.RestSite, MapPointType.Elite]);
        yield return new([MapPointType.Elite], [new(MapPointType.RestSite, 2), new(MapPointType.Shop, 0, 1)]);
        yield return new([], [new(MapPointType.Elite, 1, 3), new(MapPointType.Elite, 2, 4)]);
        yield return new([MapPointType.Unknown], [new(MapPointType.Unknown, 2, 1)]);
        yield return new(Enumerable.Repeat(MapPointType.Elite, 32));
        var eligible = witness.Where(n => Participates(n, start, boss)).ToArray();
        // Guaranteed positive witness: both ordered rooms and exact counts come from one complete path.
        var exact = RoomTypes.Select(type =>
        {
            int count = eligible.Count(n => n.PointType == type);
            return new MapRouteCountBounds(type, count, count);
        }).ToArray();
        yield return new(eligible.Select(n => n.PointType), exact);
        yield return new(eligible.Where((_, i) => i % 2 == 0).Select(n => n.PointType), exact);
        yield return new([], RoomTypes.Select(type => new MapRouteCountBounds(type, 0, 0)));
        yield return Grouped([MapPointType.Elite, MapPointType.RestSite]);
        yield return Grouped([MapPointType.Elite, MapPointType.RestSite], [MapPointType.Shop, MapPointType.Unknown]);
        yield return Grouped([MapPointType.RestSite, MapPointType.Elite], [MapPointType.Elite]);
        yield return Grouped([MapPointType.Elite, MapPointType.Elite, MapPointType.RestSite]);
        yield return Grouped([MapPointType.Elite, MapPointType.RestSite], [], [MapPointType.Shop]);
        yield return Grouped([], [], []);
        yield return MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite]), new([MapPointType.Shop])],
            [new(MapPointType.Elite, 0, 2), new(MapPointType.Shop, 1)]);
        yield return MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite])],
            [new(MapPointType.Elite, 2, 1)]);
        yield return MapRouteSequenceQuery.FromGroups(eligible.Chunk(3).Select(chunk => new MapRouteGroup(chunk.Select(n => n.PointType))), exact);
        yield return MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop])],
            [new(MapPointType.Unknown, 1)]);
    }

    private static void FixedMarkers(GeneratedStandardMap map)
    {
        int bossRow = map.Boss.FinalRow;
        foreach (var (row, type) in new[] { (1, MapPointType.Monster), (bossRow - 7, MapPointType.Treasure),
            (bossRow - 1, MapPointType.RestSite) })
        {
            var nodes = map.GridNodes().Where(n => n.FinalRow == row).ToArray();
            Require(nodes.Length > 0 && nodes.All(n => n.PointType == type && !n.CanBeModified), $"Fixed room markers {map.Context} row {row}");
        }
        Require(map.GridNodes().Where(n => n.FinalRow != 1 && n.FinalRow != bossRow - 7 && n.FinalRow != bossRow - 1)
            .All(n => n.CanBeModified), "Ordinary rooms unexpectedly fixed");
    }

    private static void Adversarial(List<string> completed)
    {
        void Case(string name, Action test) { test(); completed.Add(name); }
        var fixedOnly = Chain((MapPointType.Monster, false), (MapPointType.Treasure, false), (MapPointType.RestSite, false));
        Case("fixed-first-monster/chest/last-rest excluded", () =>
        {
            Check(fixedOnly.Start, fixedOnly.Boss, new([MapPointType.Monster]), false);
            Check(fixedOnly.Start, fixedOnly.Boss, new([MapPointType.RestSite]), false);
            Check(fixedOnly.Start, fixedOnly.Boss, new([], [new(MapPointType.Monster, 0, 0), new(MapPointType.RestSite, 0, 0)]), true);
            Check(fixedOnly.Start, fixedOnly.Boss, new([], [new(MapPointType.Monster, 1)]), false);
        });
        Case("modifiable special room types remain non-selectable", () =>
        {
            var graph = Chain((MapPointType.Treasure, true), (MapPointType.Ancient, true),
                (MapPointType.Boss, true), (MapPointType.Unassigned, true));
            Check(graph.Start, graph.Boss, new([], RoomTypes.Select(type => new MapRouteCountBounds(type, 0, 0))), true);
            foreach (var type in RoomTypes) Check(graph.Start, graph.Boss, new([type]), false);
        });
        Case("modifiable mandatory rest/elite participate", () =>
        {
            var graph = Chain((MapPointType.RestSite, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, new([MapPointType.RestSite, MapPointType.Elite],
                [new(MapPointType.RestSite, 1, 1), new(MapPointType.Elite, 1, 1)]), true);
            Check(graph.Start, graph.Boss, new([MapPointType.Elite, MapPointType.RestSite]), false);
        });
        Case("repeated rooms require distinct nodes", () =>
        {
            var graph = Chain((MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, new([MapPointType.Elite, MapPointType.Elite]), false);
            graph = Chain((MapPointType.Elite, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, new([MapPointType.Elite, MapPointType.Elite]), true);
        });
        Case("extra same-type and interleaved rooms allowed", () =>
        {
            var graph = Chain((MapPointType.Elite, true), (MapPointType.Shop, true), (MapPointType.Elite, true),
                (MapPointType.Monster, true), (MapPointType.RestSite, true), (MapPointType.Unknown, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, new([MapPointType.Elite, MapPointType.RestSite, MapPointType.Elite]), true);
            Check(graph.Start, graph.Boss, new([MapPointType.Shop, MapPointType.Unknown]), true);
            Check(graph.Start, graph.Boss, new([MapPointType.RestSite, MapPointType.Shop]), false);
        });
        Case("empty sequence and missing sequence", () =>
        {
            Check(fixedOnly.Start, fixedOnly.Boss, new([]), true);
            Check(fixedOnly.Start, fixedOnly.Boss, new([MapPointType.Shop]), false);
        });
        Case("start/boss identities excluded even with eligible room types", () =>
        {
            var graph = Chain(); graph.Start.PointType = MapPointType.Monster; graph.Boss.PointType = MapPointType.Elite;
            Check(graph.Start, graph.Boss, new([MapPointType.Monster]), false);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 0, 0)]), true);
        });
        Case("dead-end sequence/count witnesses excluded", () =>
        {
            var graph = Chain((MapPointType.Monster, true));
            Link(graph.Start, Node(1, MapPointType.Elite, 1));
            Check(graph.Start, graph.Boss, new([MapPointType.Elite]), false);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 1)]), false);
        });
        Case("ordered matches cannot splice branches", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(3, MapPointType.Boss);
            var elite = Node(1, MapPointType.Elite, 1); var rest = Node(1, MapPointType.RestSite, 5);
            Link(start, elite, boss); Link(start, rest, boss);
            Check(start, boss, new([MapPointType.Elite, MapPointType.RestSite]), false);
            Check(start, boss, new([], [new(MapPointType.Elite, 1), new(MapPointType.RestSite, 1)]), false);
        });
        Case("sequence and counts share one route across merge", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(5, MapPointType.Boss);
            var merge = Node(3, MapPointType.Unknown); var rest = Node(4, MapPointType.RestSite);
            Link(start, Node(1, MapPointType.Elite, 1), Node(2, MapPointType.Elite, 1), merge, rest, boss);
            Link(start, Node(1, MapPointType.Shop, 5), Node(2, MapPointType.Monster, 5), merge);
            Check(start, boss, new([MapPointType.Elite, MapPointType.Elite], [new(MapPointType.Shop, 1)]), false);
            Check(start, boss, new([MapPointType.Elite, MapPointType.Elite], [new(MapPointType.Elite, 0, 1)]), false);
        });
        Case("maximum progress does not dominate count tradeoffs", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(5, MapPointType.Boss);
            var merge = Node(3, MapPointType.Unknown); var finalElite = Node(4, MapPointType.Elite);
            Link(start, Node(1, MapPointType.Elite, 1), Node(2, MapPointType.Elite, 1), merge, finalElite, boss);
            Link(start, Node(1, MapPointType.Elite, 5), Node(2, MapPointType.Monster, 5), merge);
            Check(start, boss, new([MapPointType.Elite, MapPointType.Elite], [new(MapPointType.Elite, 2, 2)]), true);
        });
        Case("lower/upper/contradictory and duplicate bounds", () =>
        {
            var graph = Chain((MapPointType.Elite, true), (MapPointType.Monster, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 2, 2)]), true);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 3)]), false);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 0, 1)]), false);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 3, 2)]), false);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 1, 3), new(MapPointType.Elite, 2, 2)]), true);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, 2), new(MapPointType.Elite, 0, 1)]), false);
            Check(graph.Start, graph.Boss, new([], [new(MapPointType.Elite, int.MaxValue)]), false);
        });
        Case("DTO snapshots inputs and exposes read-only collections", () =>
        {
            MapPointType[] sequence = [MapPointType.Elite]; MapRouteCountBounds[] counts = [new(MapPointType.Elite, 1, 1)];
            var query = new MapRouteSequenceQuery(sequence, counts);
            sequence[0] = MapPointType.Shop; counts[0] = new(MapPointType.Shop, 1);
            Require(query.Groups.Count == 1 && query.Groups[0].Nodes.SequenceEqual(new[] { MapPointType.Elite })
                && query.AdditionalCounts.SequenceEqual(new[] { new MapRouteCountBounds(MapPointType.Elite, 1, 1) }), "DTO aliases authored arrays");
            ReadOnly(query.Groups); ReadOnly(query.Groups[0].Nodes); ReadOnly(query.AdditionalCounts);
            var graph = Chain((MapPointType.Elite, true)); Check(graph.Start, graph.Boss, query, true);
        });
        GroupAdversarial(Case);
        Case("malformed sequence/count inputs rejected", () =>
        {
            Expect<ArgumentException>(() => new MapRouteSequenceQuery(null!));
            foreach (MapPointType type in new[] { MapPointType.Unassigned, MapPointType.Treasure, MapPointType.Boss, MapPointType.Ancient, (MapPointType)99 })
            {
                Expect<ArgumentException>(() => new MapRouteSequenceQuery([type]));
                Expect<ArgumentException>(() => new MapRouteSequenceQuery([], [new(type)]));
            }
            Expect<ArgumentException>(() => new MapRouteSequenceQuery([], [new(MapPointType.Elite, -1)]));
            Expect<ArgumentException>(() => new MapRouteSequenceQuery([], [new(MapPointType.Elite, 0, -1)]));
        });
        Case("cancelled/no-complete-route/backward-edge rejected", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Expect<OperationCanceledException>(() => MapRouteSequenceEvaluator.Evaluate(fixedOnly.Start, fixedOnly.Boss, new([]), cancelled.Token));
            Expect<ArgumentException>(() => MapRouteSequenceEvaluator.Evaluate(Node(0, MapPointType.Ancient), Node(2, MapPointType.Boss), new([])));
            var graph = Chain((MapPointType.Elite, true));
            Link(graph.Start.Children.Single(), graph.Start);
            Expect<ArgumentException>(() => MapRouteSequenceEvaluator.Evaluate(graph.Start, graph.Boss, new([])));
        });
    }

    private static void GroupAdversarial(Action<string, Action> addCase)
    {
        addCase("group members accept arbitrary order and extras", () =>
        {
            var graph = Chain((MapPointType.Shop, true), (MapPointType.Unknown, true), (MapPointType.RestSite, true),
                (MapPointType.Elite, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop]), true);
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Shop, MapPointType.Elite, MapPointType.RestSite]), true);
        });
        addCase("groups reject reversed and interleaved requirements", () =>
        {
            var graph = Chain((MapPointType.RestSite, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite], [MapPointType.RestSite]), false);
            graph = Chain((MapPointType.Elite, true), (MapPointType.RestSite, true), (MapPointType.Elite, true), (MapPointType.RestSite, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.Elite], [MapPointType.RestSite, MapPointType.RestSite]), false);
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.RestSite], [MapPointType.Elite, MapPointType.RestSite]), true);
        });
        addCase("group repetitions and cross-group nodes cannot be reused", () =>
        {
            var graph = Chain((MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.Elite]), false);
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite], [MapPointType.Elite]), false);
            graph = Chain((MapPointType.Elite, true), (MapPointType.Elite, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.Elite]), true);
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite], [MapPointType.Elite]), true);
        });
        addCase("early later-group rooms cannot be saved for later", () =>
        {
            var graph = Chain((MapPointType.Shop, true), (MapPointType.Elite, true), (MapPointType.RestSite, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.RestSite], [MapPointType.Shop]), false);
            graph = Chain((MapPointType.Shop, true), (MapPointType.Elite, true), (MapPointType.RestSite, true), (MapPointType.Shop, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.RestSite], [MapPointType.Shop]), true);
        });
        addCase("groups span fixed chest and count modifiable mandatory nodes", () =>
        {
            var graph = Chain((MapPointType.Elite, true), (MapPointType.Treasure, false), (MapPointType.RestSite, true),
                (MapPointType.RestSite, false), (MapPointType.Shop, true));
            Check(graph.Start, graph.Boss, Grouped([MapPointType.RestSite, MapPointType.Elite], [MapPointType.Shop]), true);
            Check(graph.Start, graph.Boss, Grouped([MapPointType.Elite, MapPointType.RestSite, MapPointType.RestSite]), false);
        });
        addCase("merged branches cannot synthesize an unordered group", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(3, MapPointType.Boss); var merge = Node(2, MapPointType.Unknown);
            Link(start, Node(1, MapPointType.Elite, 1), merge, boss);
            Link(start, Node(1, MapPointType.RestSite, 5), merge);
            Check(start, boss, Grouped([MapPointType.Elite, MapPointType.RestSite]), false);
        });
        addCase("equal-sized partial groups retain their different room types", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(4, MapPointType.Boss); var merge = Node(2, MapPointType.Unknown);
            Link(start, Node(1, MapPointType.Elite, 1), merge, Node(3, MapPointType.Elite), boss);
            Link(start, Node(1, MapPointType.RestSite, 5), merge);
            Check(start, boss, Grouped([MapPointType.Elite, MapPointType.RestSite]), true);
            Check(start, boss, Grouped([MapPointType.Elite, MapPointType.Elite]), true);
        });
        addCase("empty groups are aligned no-ops and retain additional counts", () =>
        {
            var graph = Chain((MapPointType.Elite, true), (MapPointType.RestSite, true));
            Check(graph.Start, graph.Boss, Grouped([], [MapPointType.Elite], [], [MapPointType.RestSite], []), true);
            Check(graph.Start, graph.Boss, Grouped([], [], []), true);
            Check(graph.Start, graph.Boss, MapRouteSequenceQuery.FromGroups([], [new(MapPointType.Shop, 1)]), false);
            Check(graph.Start, graph.Boss, MapRouteSequenceQuery.FromGroups([new([])], [new(MapPointType.Elite, 1, 1)]), true);
        });
        addCase("group/item/count input arrays are immutable snapshots", () =>
        {
            MapPointType[] items = [MapPointType.RestSite, MapPointType.Elite]; var group = new MapRouteGroup(items);
            MapRouteGroup[] groups = [group, new([])]; MapRouteCountBounds[] counts = [new(MapPointType.Elite, 1, 1)];
            var query = MapRouteSequenceQuery.FromGroups(groups, counts);
            items[0] = MapPointType.Shop; groups[0] = new([MapPointType.Unknown]); counts[0] = new(MapPointType.Shop, 1);
            Require(query.Groups.Count == 2 && query.Groups[0].Nodes.SequenceEqual(new[] { MapPointType.RestSite, MapPointType.Elite })
                && query.Groups[1].Nodes.Count == 0 && query.AdditionalCounts[0] == new MapRouteCountBounds(MapPointType.Elite, 1, 1), "Grouped DTO aliases inputs");
            ReadOnly(query.Groups); foreach (var item in query.Groups) ReadOnly(item.Nodes); ReadOnly(query.AdditionalCounts);
            var graph = Chain((MapPointType.Elite, true), (MapPointType.RestSite, true)); Check(graph.Start, graph.Boss, query, true);
        });
        addCase("null groups/items and non-selectable group rooms rejected", () =>
        {
            Expect<ArgumentException>(() => new MapRouteGroup(null!));
            Expect<ArgumentException>(() => MapRouteSequenceQuery.FromGroups(null!));
            Expect<ArgumentException>(() => MapRouteSequenceQuery.FromGroups([null!]));
            foreach (var type in new[] { MapPointType.Unassigned, MapPointType.Treasure, MapPointType.Boss, MapPointType.Ancient, (MapPointType)99 })
                Expect<ArgumentException>(() => new MapRouteGroup([type]));
        });
        addCase("whole-route counts retain group progress tradeoffs", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(5, MapPointType.Boss);
            var merge = Node(3, MapPointType.Unknown); var finalElite = Node(4, MapPointType.Elite);
            Link(start, Node(1, MapPointType.Elite, 1), Node(2, MapPointType.Elite, 1), merge, finalElite, boss);
            Link(start, Node(1, MapPointType.Elite, 5), Node(2, MapPointType.RestSite, 5), merge);
            Check(start, boss, MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.Elite])], [new(MapPointType.Elite, 2, 2)]), true);
            Check(start, boss, MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite])], [new(MapPointType.Elite, 0, 1)]), false);
            Check(start, boss, MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite])], [new(MapPointType.Elite, 2, 2)]), true);
            Check(start, boss, MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite])], [new(MapPointType.Elite, 2, 1)]), false);
        });
        addCase("group and whole counts cannot splice separate prefixes", () =>
        {
            var start = Node(0, MapPointType.Ancient); var boss = Node(5, MapPointType.Boss);
            var merge = Node(3, MapPointType.Unknown); var monster = Node(4, MapPointType.Monster);
            Link(start, Node(1, MapPointType.Elite, 1), Node(2, MapPointType.RestSite, 1), merge, monster, boss);
            Link(start, Node(1, MapPointType.Shop, 5), Node(2, MapPointType.Shop, 5), merge);
            Check(start, boss, MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.RestSite])], [new(MapPointType.Shop, 1)]), false);
            Check(start, boss, MapRouteSequenceQuery.FromGroups([new([MapPointType.Shop, MapPointType.Shop]), new([MapPointType.Monster])],
                [new(MapPointType.Shop, 2, 2)]), true);
        });
        addCase("one unordered group equals non-fixed count conjunction", () =>
        {
            foreach (var graph in new[] { Chain((MapPointType.Elite, true), (MapPointType.Elite, true), (MapPointType.RestSite, true)),
                Chain((MapPointType.RestSite, true), (MapPointType.Elite, true), (MapPointType.Elite, false)),
                Chain((MapPointType.Shop, true), (MapPointType.RestSite, false)), Chain() })
            {
                MapPointType[] items = [MapPointType.RestSite, MapPointType.Elite, MapPointType.Elite];
                var grouped = Grouped(items);
                var counts = new MapRouteSequenceQuery([], items.GroupBy(type => type).Select(g => new MapRouteCountBounds(g.Key, g.Count())));
                bool expected = EnumeratePaths(graph.Start, graph.Boss).Any(path => Oracle(path, graph.Start, graph.Boss, counts));
                Check(graph.Start, graph.Boss, grouped, expected); Check(graph.Start, graph.Boss, counts, expected);
            }
        });
        addCase("singleton group construction preserves ordered subsequence", () =>
        {
            var graph = Chain((MapPointType.RestSite, true), (MapPointType.Elite, true), (MapPointType.Shop, true), (MapPointType.Elite, true));
            foreach (MapPointType[] sequence in new[] { new[] { MapPointType.Elite, MapPointType.Shop }, new[] { MapPointType.Shop, MapPointType.RestSite },
                new[] { MapPointType.Elite, MapPointType.Elite }, Array.Empty<MapPointType>() })
            {
                var legacy = new MapRouteSequenceQuery(sequence); var singleton = Grouped(sequence.Select(type => new[] { type }).ToArray());
                Require(legacy.Groups.Count == sequence.Length && legacy.Groups.SelectMany(g => g.Nodes).SequenceEqual(sequence), "Sequence constructor lost singleton groups");
                var path = EnumeratePaths(graph.Start, graph.Boss).Single(); int progress = 0;
                foreach (var node in path.Where(n => Participates(n, graph.Start, graph.Boss)))
                    if (progress < sequence.Length && node.PointType == sequence[progress]) progress++;
                Check(graph.Start, graph.Boss, legacy, progress == sequence.Length); Check(graph.Start, graph.Boss, singleton, progress == sequence.Length);
            }
        });
    }

    private static MapRouteSequenceQuery Grouped(params MapPointType[][] groups) =>
        MapRouteSequenceQuery.FromGroups(groups.Select(nodes => new MapRouteGroup(nodes)));

    private static void Check(MapNode start, MapNode boss, MapRouteSequenceQuery query, bool expected)
    {
        var paths = EnumeratePaths(start, boss);
        Require(paths.Any(path => Oracle(path, start, boss, query)) == expected, "Synthetic oracle expectation disagrees");
        var result = MapRouteSequenceEvaluator.Evaluate(start, boss, query);
        Validate(start, boss, query, expected, result.Matches, result.Path, result.MatchedNodes, result.MatchedGroups, result.VisitedStates);
    }

    // Independent of the evaluator's greedy group state: try every disjoint window
    // boundary on a complete concrete path, accepting windows by plain room counts.
    private static bool Oracle(IEnumerable<MapNode> path, MapNode start, MapNode boss, MapRouteSequenceQuery query)
    {
        var rooms = path.Where(n => Participates(n, start, boss)).ToArray();
        // Every requested occurrence needs its own node, regardless of cuts.
        if (query.Groups.Sum(group => group.Nodes.Count) > rooms.Length) return false;
        if (!query.AdditionalCounts.All(bound =>
        {
            int count = rooms.Count(n => n.PointType == bound.Type);
            return count >= bound.Minimum && (bound.Maximum is null || count <= bound.Maximum.Value);
        })) return false;
        var requirements = query.Groups.Select(group => group.Nodes.GroupBy(type => type).ToArray()).ToArray();
        var memo = new Dictionary<(int Group, int Begin), bool>();
        bool Partition(int group, int begin)
        {
            if (group == query.Groups.Count) return true;
            if (memo.TryGetValue((group, begin), out bool cached)) return cached;
            for (int end = begin; end <= rooms.Length; end++)
            {
                bool enough = requirements[group].All(types => Enumerable.Range(begin, end - begin)
                    .Count(i => rooms[i].PointType == types.Key) >= types.Count());
                if (enough && Partition(group + 1, end)) return memo[(group, begin)] = true;
            }
            return memo[(group, begin)] = false;
        }
        return Partition(0, 0);
    }

    private static bool Participates(MapNode node, MapNode start, MapNode boss) =>
        node != start && node != boss && node.CanBeModified && RoomTypes.Contains(node.PointType);

    private static void Validate(MapNode start, MapNode boss, MapRouteSequenceQuery query, bool expected,
        bool matches, IReadOnlyList<MapPosition> pathPositions, IReadOnlyList<MapPosition> matchedPositions,
        IReadOnlyList<IReadOnlyList<MapPosition>> matchedGroupPositions, int visitedStates)
    {
        Require(matches == expected, "Sequence evaluator disagrees with complete-path oracle");
        Require(visitedStates >= 0, "Negative visited-state count");
        ReadOnly(pathPositions); ReadOnly(matchedPositions); ReadOnly(matchedGroupPositions);
        foreach (var group in matchedGroupPositions) ReadOnly(group);
        var nodes = Reachable(start).ToDictionary(Position);
        MapNode[] Resolve(IReadOnlyList<MapPosition> positions) => positions.Select(p => nodes.TryGetValue(p, out var node)
            ? node : throw new InvalidOperationException("Witness coordinate absent from source graph")).ToArray();
        IReadOnlyList<MapNode> path = Resolve(pathPositions), matchedNodes = Resolve(matchedPositions);
        IReadOnlyList<IReadOnlyList<MapNode>> matchedGroups = matchedGroupPositions.Select(g => (IReadOnlyList<MapNode>)Resolve(g)).ToArray();
        if (!matches)
        {
            Require(path.Count == 0 && matchedNodes.Count == 0 && matchedGroups.Count == 0, "NoMatch exposed partial witnesses");
            return;
        }
        Require(path.Count >= 2 && path[0] == start && path[^1] == boss, "Complete witness endpoints");
        for (int i = 1; i < path.Count; i++) Require(path[i - 1].Children.Contains(path[i]), "Witness edge absent");
        Require(Oracle(path, start, boss, query), "Witness fails sequence or counts");
        Require(matchedGroups.Count == query.Groups.Count, "Matched groups do not align with authored groups");
        Require(matchedNodes.SequenceEqual(matchedGroups.SelectMany(group => group)), "MatchedNodes is not the ordered group flattening");
        int cursor = 0;
        for (int group = 0; group < matchedGroups.Count; group++)
        {
            var required = query.Groups[group].Nodes;
            var evidence = matchedGroups[group];
            Require(evidence.Count == required.Count && RoomTypes.All(type =>
                evidence.Count(n => n.PointType == type) == required.Count(t => t == type)), "Group evidence multiset");
            foreach (MapNode matched in evidence)
            {
                while (cursor < path.Count && path[cursor] != matched) cursor++;
                Require(cursor < path.Count && Participates(matched, start, boss), "Matched-node order/reuse/fixed exclusion");
                cursor++;
            }
        }
    }

    private static void ReadOnly<T>(IReadOnlyList<T> list)
    {
        Require(list.Count == 0 || list is not T[], "Public collection exposes a mutable array");
        if (list is IList<T> writable)
        {
            Require(writable.IsReadOnly, "Public collection is writable");
            if (list.Count > 0) Expect<NotSupportedException>(() => writable[0] = list[0]);
        }
    }

    private static (MapNode Start, MapNode Boss) Chain(params (MapPointType Type, bool Modifiable)[] rooms)
    {
        var start = Node(0, MapPointType.Ancient); var boss = Node(rooms.Length + 1, MapPointType.Boss);
        var nodes = new[] { start }.Concat(rooms.Select((room, i) =>
            new MapNode(3, i + 1) { PointType = room.Type, CanBeModified = room.Modifiable })).Append(boss).ToArray();
        Link(nodes); return (start, boss);
    }

    private static MapNode Node(int row, MapPointType type, int col = 3) => new(col, row) { PointType = type };
    private static void Link(params MapNode[] nodes)
    {
        for (int i = 1; i < nodes.Length; i++) { nodes[i - 1].Children.Add(nodes[i]); nodes[i].Parents.Add(nodes[i - 1]); }
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
