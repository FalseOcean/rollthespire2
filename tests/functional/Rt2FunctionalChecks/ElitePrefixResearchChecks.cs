using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;

internal static class ElitePrefixResearchChecks
{
    private static int _checks;
    private static void Check(bool ok, string why)
    { _checks++; if (!ok) throw new InvalidDataException("ElitePrefixResearch:" + why); }
    private static string Seed(ulong ordinal) => VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);

    internal static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        GraphCases();
        FloorSevenGraphCases();
        NaturalMaps();
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        CompiledSearch Compile(ElitePrefixResearchCondition? condition) =>
            SearchCompiler.Compile(SearchQuery.Empty with { ElitePrefixResearch = condition }, context);
        PreparedSearch Prepare(ElitePrefixResearchCondition condition, bool skip = false) =>
            SearchPreparationCompiler.Compile(Compile(condition), new(Seed(0), 16, 17, 1, SkipExactValidation: skip)).Plan
            ?? throw new InvalidDataException("ElitePrefixResearch:Admission");
        Check(!JsonSerializer.Serialize(SearchQuery.Empty).Contains("ElitePrefixResearch", StringComparison.Ordinal), "null omitted from historical JSON shape");
        Check(Compile(null).SemanticFingerprint != Compile(new(0, 0)).SemanticFingerprint, "zero counts still active");
        Check(Compile(new(0, 4)).SemanticFingerprint != Compile(new(1, 3)).SemanticFingerprint, "counts change semantic identity");
        Check(Compile(new(int.MaxValue, int.MaxValue)).NormalizedQuery.ElitePrefixResearch == new ElitePrefixResearchCondition(int.MaxValue, int.MaxValue),
            "nonnegative Int32 inputs have no arbitrary semantic cap");
        foreach (var invalid in new[] { new ElitePrefixResearchCondition(-1, 0), new ElitePrefixResearchCondition(0, -1) })
        {
            bool rejected = false;
            try { Compile(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "negative counts rejected as malformed");
        }
        var authored = SearchQuery.Empty with { ElitePrefixResearch = new(1, 3) };
        var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(authored, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
        Check(SearchCompiler.Compile(restored, context).SemanticFingerprint == Compile(new(1, 3)).SemanticFingerprint, "query persistence identity");
        var legacy = JsonSerializer.Deserialize<ElitePrefixResearchCondition>("{\"ExtraMonsters\":1,\"Unknowns\":3}")!;
        Check(legacy == new ElitePrefixResearchCondition(1, 3) && legacy.Mode == ElitePrefixResearchMode.FixedCounts,
            "historical no-Mode JSON remains the strict count request");
        Check(!JsonSerializer.Serialize(legacy).Contains("Mode", StringComparison.Ordinal), "default mode omitted from historical JSON and continuation payload");
        Check(JsonSerializer.Serialize(legacy) == "{\"ExtraMonsters\":1,\"Unknowns\":3,\"IsValid\":true}",
            "legacy condition JSON property shape and order are byte-for-byte unchanged");
        Check(SearchContinuationIdentity.For(Compile(legacy)) == SearchContinuationIdentity.For(Compile(new(1, 3))),
            "historical query continuation identity preserved");
        var floorSeven = ElitePrefixResearchCondition.FloorSeven;
        Check(JsonSerializer.Deserialize<ElitePrefixResearchCondition>(JsonSerializer.Serialize(floorSeven)) == floorSeven,
            "floor-seven mode round-trips explicitly");
        Check(Compile(floorSeven).SemanticFingerprint != Compile(new(0, 0)).SemanticFingerprint &&
            SearchContinuationIdentity.For(Compile(floorSeven)) != SearchContinuationIdentity.For(Compile(new(0, 0))),
            "new mode cannot continue a strict-count search");
        bool unknownModeRejected = false;
        try { Compile(new(0, 0) { Mode = (ElitePrefixResearchMode)2 }); } catch (ArgumentException) { unknownModeRejected = true; }
        Check(unknownModeRejected, "unknown serialized mode is malformed rather than a fallback");
        foreach (var condition in new[] { new ElitePrefixResearchCondition(0, 4), new ElitePrefixResearchCondition(1, 3), new ElitePrefixResearchCondition(int.MaxValue, int.MaxValue) })
        {
            var request = Prepare(condition);
            var physical = FamilyExecutionCoordinator.Plan(request, false);
            Check(physical.OrderedFamilies.Count == 1 && physical.OrderedFamilies[0].FamilyId == StandardMapFamily.Id &&
                physical.OrderedFamilies[0].Preparation.EffectiveObligations.Any(o => o.Domain == "ElitePrefixResearch") &&
                !physical.FilterRemainders.Any(r => r.StartsWith("ElitePrefixResearch:", StringComparison.Ordinal)),
                "existing complete M replay covers research before independent Exact");
            Check(!physical.FinalValidationSkipped && FamilyExactTimingDomains.HasQueryWork(request.Model.CompiledSearch.NormalizedQuery),
                "query-only still owns normal Exact work");
            var probability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request.Model));
            Check(probability.Probability is null && probability.UnknownComponents.Any(s => s.Contains("ElitePrefixResearch", StringComparison.Ordinal)),
                "unmodeled research probability remains unknown");
            var eta = FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(request, physical), physical);
            Check(eta.Status == FamilySearchEtaStatus.Unavailable && eta.AcceptedResultProbability is null, "ETA cannot fabricate certainty");
            var expected = new List<string>();
            for (ulong ordinal = 0; ordinal < 16; ordinal++)
            {
                string seed = Seed(ordinal);
                var map = Beta111StandardMapGenerator.GenerateStandardMap(seed, StandardMapFamily.Context(1, context.Ascension));
                bool truth = Oracle(map, condition.ExtraMonsters, condition.Unknowns);
                var exact = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
                Check(exact.Evaluation.Disposition == (truth ? SearchDisposition.Match : SearchDisposition.NoMatch), "whole Exact agrees with independent full-prefix oracle");
                if (truth)
                {
                    expected.Add(seed);
                    var proof = exact.Evaluation.Evidence.Single(e => e.ConditionId == "ElitePrefixResearch");
                    Check(proof.Act == 1 && proof.RouteId is null && proof.OutcomeId!.Contains("allPaths=true", StringComparison.Ordinal) &&
                        proof.OutcomeId.Contains($"extraMonsters={condition.ExtraMonsters};unknowns={condition.Unknowns}", StringComparison.Ordinal),
                        "universal evidence carries Act and counts without a single-route claim");
                }
            }
            await using var session = FamilyExecutionCoordinator.Start(request, gpuAvailable: false);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            var outputs = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate)) { outputs.Add(candidate!); session.AcknowledgeCandidate(candidate!.Seed); }
            Check(outputs.Select(c => c.Seed).SequenceEqual(expected) && outputs.All(c => !c.IsUnverified && !c.IsUnfiltered),
                "normal Search emits only confirmed Exact matches, never raw seeds");
            Check(session.GetProgress().State == SearchRunState.Completed && session.CaptureContinuation()!.PendingCandidates.Count == 0,
                "normal M plus Exact search completes and commits its cursor");
        }
        var skipped = Prepare(new(0, 4), skip: true);
        var skipPlan = FamilyExecutionCoordinator.Plan(skipped, false);
        Check(!skipPlan.AllConditionsUnfiltered && skipPlan.FinalValidationSkipped &&
            skipPlan.OrderedFamilies.Single().Preparation.EffectiveObligations.Any(o => o.Domain == "ElitePrefixResearch") &&
            !skipPlan.FilterRemainders.Any(r => r.StartsWith("ElitePrefixResearch:", StringComparison.Ordinal)),
            "skipping final validation retains actual M filtering without a final Match claim");
        await MapFamilyCases(context);
        await FloorSevenSearchCases(context);
        PartyCases(context);
        Console.WriteLine($"ELITE_PREFIX_RESEARCH=PASS;checks={_checks};universalBranchMerge=true;floorSevenNaturalPositives=11;legacyModeMigration=true;naturalSPMP=true;MCompleteCpuDenseCompact=true;IndependentExact=true;noGpuAlgorithm=true");
        return 0;
    }

    private static void GraphCases()
    {
        static MapNode Node(int row, MapPointType type, int col = 0) => new(col, row) { PointType = type };
        static void Edge(MapNode parent, MapNode child) { parent.Children.Add(child); child.Parents.Add(parent); }
        GeneratedStandardMap Chain(params MapPointType[] rooms)
        {
            var start = Node(0, MapPointType.Ancient); var cursor = start;
            foreach (var room in rooms) { var next = Node(cursor.Row + 1, room); Edge(cursor, next); cursor = next; }
            return Map(start);
        }
        foreach (var rooms in new[]
        {
            new[] { MapPointType.Monster, MapPointType.Monster, MapPointType.Unknown, MapPointType.Unknown, MapPointType.Unknown, MapPointType.Elite },
            new[] { MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.Unknown, MapPointType.Unknown, MapPointType.Elite },
            new[] { MapPointType.Monster, MapPointType.Unknown, MapPointType.Unknown, MapPointType.Unknown, MapPointType.Monster, MapPointType.Elite }
        })
        {
            var map = Chain(rooms);
            Check(ElitePrefixResearchEvaluator.Evaluate(map, 1, 3).Matches, "extra Monster and Unknown permutation");
            Check(ElitePrefixResearchEvaluator.Evaluate(map.Start, 1, 3).Matches,
                "complete graph entry preserves positive unordered prefix semantics");
        }
        Check(ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, MapPointType.Elite, MapPointType.Shop, MapPointType.Boss), 0, 0).Matches,
            "zero extras and everything after first Elite ignored");
        Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Unknown, MapPointType.Monster, MapPointType.Elite), 1, 1).Matches,
            "first room must be Monster");
        Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, MapPointType.Unknown, MapPointType.Elite), 1, 1).Matches,
            "fixed first Monster does not count as extra");
        foreach (var forbidden in new[] { MapPointType.RestSite, MapPointType.Shop, MapPointType.Treasure, MapPointType.Ancient, MapPointType.Boss, MapPointType.Unassigned })
            Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, forbidden, MapPointType.Elite), 0, 0).Matches,
                "other room type cannot occur before first Elite");
        Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, MapPointType.Unknown), 0, 1).Matches, "path without Elite fails");
        Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, MapPointType.Elite, MapPointType.Unknown, MapPointType.Elite), 0, 1).Matches,
            "early first Elite cannot be skipped");
        Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, MapPointType.Unknown, MapPointType.Unknown, MapPointType.Elite), 0, 1).Matches,
            "extra prefix room fails");
        Check(!ElitePrefixResearchEvaluator.Evaluate(Chain(MapPointType.Monster, MapPointType.Elite), int.MaxValue, int.MaxValue).Matches,
            "very large counts terminate without allocation by requested bound");

        var start = Node(0, MapPointType.Ancient); var first = Node(1, MapPointType.Monster);
        var good = Node(2, MapPointType.Monster); var bad = Node(2, MapPointType.Unknown, 1);
        var shared = Node(3, MapPointType.Unknown); var elite = Node(4, MapPointType.Elite);
        Edge(start, first); Edge(first, good); Edge(first, bad); Edge(good, shared); Edge(bad, shared); Edge(shared, elite);
        Check(!ElitePrefixResearchEvaluator.Evaluate(Map(start), 1, 1).Matches,
            "merge visited with different counts cannot reuse a passing node-only result");
        Check(!ElitePrefixResearchEvaluator.Evaluate(start, 1, 1).Matches,
            "complete graph entry preserves distinct counts before merge");
        bad.PointType = MapPointType.Monster;
        Check(ElitePrefixResearchEvaluator.Evaluate(Map(start), 1, 1).Matches, "same-count branches merging pass");
        Check(ElitePrefixResearchEvaluator.Evaluate(start, 1, 1).Matches, "complete graph entry accepts all same-count branches");
        var secondStart = Node(1, MapPointType.Monster, 2); Edge(start, secondStart); Edge(secondStart, Node(2, MapPointType.Shop, 2));
        Check(!ElitePrefixResearchEvaluator.Evaluate(Map(start), 1, 1).Matches, "every legal start branch must pass");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool threw = false;
        try { ElitePrefixResearchEvaluator.Evaluate(Map(start), 1, 1, cancelled.Token); } catch (OperationCanceledException) { threw = true; }
        Check(threw, "cancellation honored");

        static GeneratedStandardMap Map(MapNode start) => new("synthetic", new(ActKind.Overgrowth, 0),
            new ReferenceRng(0, default, MapReplayMode.Reference), new MapNode?[0, 0], start,
            new MapNode(0, 99) { PointType = MapPointType.Boss }, null, start.Children, new(0, 0, 0));
    }

    private static void FloorSevenGraphCases()
    {
        static void Edge(MapNode a, MapNode b) { a.Children.Add(b); b.Parents.Add(a); }
        static MapNode Start()
        {
            var start = new MapNode(0, 0) { PointType = MapPointType.Ancient };
            for (int col = 0; col < 2; col++)
            {
                var previous = start;
                for (int row = 1; row <= 6; row++)
                {
                    var node = new MapNode(col, row) { PointType = row == 6 ? MapPointType.Elite :
                        row == 1 || col == 0 ? MapPointType.Monster : MapPointType.Unknown };
                    Edge(previous, node); previous = node;
                }
                Edge(previous, new MapNode(col, 7) { PointType = MapPointType.RestSite });
            }
            return start;
        }
        var graph = Start();
        var result = ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph);
        Check(result.Matches && result.FirstElites.Count == 2 && result.FirstElites.All(p => p.Row == 6),
            "floor seven accepts different counts across paths and multiple blocking Elites");
        Check(!ElitePrefixResearchEvaluator.Evaluate(graph, 0, 4).Matches && !ElitePrefixResearchEvaluator.Evaluate(graph, 4, 0).Matches,
            "new positive is not reinterpreted as a shared fixed count");
        var alternate = graph.Children.Last().Children.Single();
        foreach (var forbidden in new[] { MapPointType.Shop, MapPointType.RestSite, MapPointType.Treasure,
            MapPointType.Elite, MapPointType.Ancient, MapPointType.Boss, MapPointType.Unassigned })
        {
            alternate.PointType = forbidden;
            Check(!ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph).Matches, "one forbidden/early-Elite branch rejects every-path request");
        }
        alternate.PointType = MapPointType.Unknown;
        var elite = alternate;
        while (elite.Row < 6) elite = elite.Children.Single();
        elite.PointType = MapPointType.Monster;
        Check(!ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph).Matches, "one route with no floor-seven Elite rejects even with later rooms");
        elite.PointType = MapPointType.Elite; elite.Row = 7;
        Check(!ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph).Matches, "later first Elite cannot replace floor-seven requirement");
        elite.Row = 6;
        var first = graph.Children.First(); first.PointType = MapPointType.Unknown;
        Check(!ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph).Matches, "fixed first Monster remains required");
        first.PointType = MapPointType.Monster;
        // Different-count paths may merge: the new rule has no count state to preserve.
        var shared = first.Children.Single().Children.Single();
        alternate.Children.Clear(); Edge(alternate, shared);
        Check(ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph).Matches, "variable-count branches can share later prefix nodes");
        alternate.Children.Clear();
        Check(!ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph).Matches, "dead-end prefix is a failed legal path");
        Check(!ElitePrefixResearchEvaluator.EvaluateFloorSeven(new MapNode(0, 0)).Matches, "empty opening has no universal positive witness");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool threw = false;
        try { ElitePrefixResearchEvaluator.EvaluateFloorSeven(Start(), cancelled.Token); } catch (OperationCanceledException) { threw = true; }
        Check(threw, "floor-seven traversal honors cancellation");
    }

    private static async Task FloorSevenSearchCases(SearchContext context)
    {
        // H03's bounded real-map scan independently checked these 11 natural
        // positives (73728 seeds, A10). Keep fixtures in source, not a dependency
        // on a local evidence directory or a fresh broad scan.
        ulong[] positives = [2279, 5579, 8410, 9502, 19929, 20956, 24937, 31355, 46866, 67309, 71850];
        var query = SearchQuery.Empty with { ElitePrefixResearch = ElitePrefixResearchCondition.FloorSeven };
        PreparedSearch Request(ulong start, int count = 3, bool skip = false) => SearchPreparationCompiler.Compile(
            SearchCompiler.Compile(query, context), new(Seed(start), count, count + 1, 1, SkipExactValidation: skip)).Plan!;
        var request = Request(2278);
        var body = StandardMapFamily.Prepare(request.Model, FilterExecutionPreferences.From(request.RunOptions));
        var legacyRequest = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query with { ElitePrefixResearch = new(0, 4) }, context),
            request.RunOptions).Plan!;
        var legacyBody = StandardMapFamily.Prepare(legacyRequest.Model, FilterExecutionPreferences.From(request.RunOptions));
        Check(body.PhysicalRevision == legacyBody.PhysicalRevision + ".FloorSeven.v1" &&
            FamilyExactTimingDomains.ImplementationRevision(request.Model) == FamilyExactTimingDomains.ImplementationRevision(legacyRequest.Model) + ".FloorSeven.v1",
            "new physical implementations are distinct without changing old strict mode identity");
        await using var services = new FilterRuntimeServices(requiresGpu: false);
        var family = (StandardMapExecution)body.CreateExecution!(body, services);
        try
        {
            foreach (ulong ordinal in positives.Concat(new ulong[] { 0, 1, 2278, 2280 }))
            {
                string seed = Seed(ordinal);
                var reference = Beta111StandardMapGenerator.GenerateStandardMap(seed, StandardMapFamily.Context(1, context.Ascension), mode: MapReplayMode.Reference);
                bool expected = FloorSevenOracle(reference);
                Check(expected == positives.Contains(ordinal), "H03 saved positive and adjacent-negative fixtures retain their independent path truth");
                Check(ElitePrefixResearchEvaluator.EvaluateFloorSeven(reference).Matches == expected,
                    "new pure predicate agrees with independent all-path enumeration on natural maps");
                Check(family.MatchesRoot(Beta111Profile.Instance.ComputeRootSeed(seed)) == expected,
                    "bounded complete M agrees with independently generated path oracle including real survivors");
                var exact = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
                Check(exact.Evaluation.Disposition == (expected ? SearchDisposition.Match : SearchDisposition.NoMatch),
                    "independent production Exact agrees for natural positive and negative maps");
                if (expected)
                {
                    var proof = exact.Evaluation.Evidence.Single(e => e.ConditionId == "ElitePrefixResearch");
                    Check(proof.Act == 1 && proof.RouteId is null && proof.OutcomeId!.Contains("allPaths=true;firstEliteFloor=7;prefixIcons=Monster,Unknown;counts=unrestricted;", StringComparison.Ordinal) &&
                        !proof.OutcomeId.Contains("extraMonsters=", StringComparison.Ordinal),
                        "Exact evidence declares floor, universal scope and unrestricted counts");
                    Check(!Oracle(reference, 0, 4), "saved natural new-mode positive does not silently satisfy legacy strict counts");
                }
            }
            foreach (bool compact in new[] { false, true })
            {
                var batch = new SearchBatch(2278, 3);
                var input = compact ? FamilyCandidateSet.FromAbi1(batch, [1, 2]) : FamilyCandidateSet.Dense(batch);
                var filtered = await family.FilterAsync(services, input, default);
                Check(filtered.ExportAbi1().Span.SequenceEqual(new ulong[] { 1 }), "new-mode dense/compact M preserves positive batch-local ordinal");
            }
        }
        finally { await family.DisposeAsync(services); }
        foreach (bool skip in new[] { false, true })
        {
            await using var session = FamilyExecutionCoordinator.Start(Request(2278, skip: skip), gpuAvailable: false);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            var results = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate)) { results.Add(candidate!); session.AcknowledgeCandidate(candidate!.Seed); }
            Check(results.Count == 1 && results[0].Seed == Seed(2279) && results[0].IsUnverified == skip && !results[0].IsUnfiltered,
                "normal search returns real new-mode survivor through Exact or explicit skipped-Exact confidence");
            Check(session.GetProgress().State == SearchRunState.Completed && session.CaptureContinuation()!.PendingCandidates.Count == 0,
                "positive search publishes and acknowledges its complete cursor without pending work");
        }
        var probability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request.Model));
        Check(probability.Probability is null && probability.UnknownComponents.Any(s => s.Contains("ElitePrefixResearch", StringComparison.Ordinal)),
            "new mode retains honest unknown probability");
    }

    // Independent H03-style oracle enumerates whole route lists, including their
    // row sequence, before applying the new rule. No production predicate call.
    private static bool FloorSevenOracle(GeneratedStandardMap map)
    {
        var prefixes = new List<MapNode[]>();
        var path = new List<MapNode>();
        void Walk(MapNode node)
        {
            path.Add(node);
            if (node.PointType == MapPointType.Elite || node.Children.Count == 0) prefixes.Add(path.ToArray());
            else foreach (var child in node.Children) Walk(child);
            path.RemoveAt(path.Count - 1);
        }
        foreach (var first in map.Start.Children) Walk(first);
        return prefixes.Count > 0 && prefixes.All(p => p.Length == 6 &&
            p.Select(n => n.Row).SequenceEqual(Enumerable.Range(1, 6)) && p[0].PointType == MapPointType.Monster &&
            p[^1].PointType == MapPointType.Elite && p.Take(5).All(n => n.PointType is MapPointType.Monster or MapPointType.Unknown));
    }

    private static void NaturalMaps()
    {
        foreach (bool multiplayer in new[] { false, true })
        foreach (bool swarm in new[] { false, true })
        for (ulong ordinal = 0; ordinal < 16; ordinal++)
        {
            var context = new ActContext(ActKind.Overgrowth, 0, multiplayer, swarm);
            var map = Beta111StandardMapGenerator.GenerateStandardMap(Seed(ordinal), context, mode: MapReplayMode.Reference);
            var bounded = new BoundedActMap(context);
            bounded.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(ordinal)));
            var graph = bounded.SnapshotFinalRouteGraph();
            int calls = bounded.RngCalls;
            var rng = bounded.RngState;
            string before = Graph(map);
            var underdocks = Beta111StandardMapGenerator.GenerateStandardMap(Seed(ordinal), context with { Act = ActKind.Underdocks }, mode: MapReplayMode.Reference);
            Check(before == Graph(underdocks) && map.MapRngCalls == underdocks.MapRngCalls, "Overgrowth and Underdocks full map parity");
            foreach (var (monsters, unknowns) in new[] { (0, 0), (0, 4), (1, 3), (3, 2), (int.MaxValue, int.MaxValue) })
            {
                bool expected = Oracle(map, monsters, unknowns);
                Check(ElitePrefixResearchEvaluator.Evaluate(map, monsters, unknowns).Matches == expected,
                    "natural SP/MP and ascension maps agree with path enumeration");
                Check(ElitePrefixResearchEvaluator.Evaluate(graph.Start, monsters, unknowns).Matches == expected,
                    "bounded full-map snapshot agrees with independent reference prefix enumeration");
            }
            bool floorSeven = FloorSevenOracle(map);
            Check(ElitePrefixResearchEvaluator.EvaluateFloorSeven(map).Matches == floorSeven &&
                ElitePrefixResearchEvaluator.EvaluateFloorSeven(graph.Start).Matches == floorSeven,
                "new mode reference/bounded maps agree across SP/MP and ascension variants");
            Check(bounded.RngCalls == calls && bounded.RngState == rng, "prefix reads preserve bounded map RNG");
            Check(before == Graph(map), "pure evaluator leaves full map unchanged");
        }
    }

    private static async Task MapFamilyCases(SearchContext context)
    {
        PreparedSearch Request(SearchQuery query, bool lossy = false, bool skip = false) =>
            SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context),
                new(Seed(0), 16, 17, 1, SkipExactValidation: skip, MapLossy: new(lossy))).Plan
            ?? throw new InvalidDataException("ElitePrefixResearch:MAdmission");
        static StandardMapRouteCondition Route(int act, params StandardMapRoomType[] rooms) =>
            new(act, rooms.Select(room => new StandardMapRouteGroup([room])).ToArray());
        var scalar = SearchQuery.Empty with { StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 0)] };
        var drawing = SearchQuery.Empty with { StandardMapRoutes = [Route(1, StandardMapRoomType.Unknown)] };
        var combined = scalar with { StandardMapRoutes = drawing.StandardMapRoutes };
        var otherActs = combined with
        {
            StandardMaps = [..scalar.StandardMaps, new(2, StandardMapMetric.GuaranteedElite, StandardMapComparison.AtLeast, 0),
                new(0, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 0, true)],
            StandardMapRoutes = [..combined.StandardMapRoutes, Route(3, StandardMapRoomType.Shop)]
        };
        SearchQuery[] queries = [scalar, drawing, combined,
            SearchQuery.Empty with { ElitePrefixResearch = new(0, 4) },
            SearchQuery.Empty with { ElitePrefixResearch = new(1, 3) },
            combined with { ElitePrefixResearch = new(1, 3) },
            otherActs with { ElitePrefixResearch = new(0, 4) },
            combined with { ElitePrefixResearch = ElitePrefixResearchCondition.FloorSeven },
            otherActs with { ElitePrefixResearch = ElitePrefixResearchCondition.FloorSeven },
            combined with { ElitePrefixResearch = new(int.MaxValue, int.MaxValue) }];
        foreach (var query in queries)
        {
            var request = Request(query);
            var body = StandardMapFamily.Prepare(request.Model, FilterExecutionPreferences.From(request.RunOptions));
            bool research = query.ElitePrefixResearch is not null;
            Check(body.PhysicalRevision.Contains("ElitePrefixResearch", StringComparison.Ordinal) == research &&
                (research || body.PhysicalRevision == "M.StandardMap.Cpu.Final.EndpointReach.RouteSequence.20261028.v3"),
                "only active research gets its new physical implementation identity");
            Check(body.Preparation.EffectiveObligations.Any(o => o.Domain == "ElitePrefixResearch") == research,
                "M declares research exactly when it participates");
            await using var services = new FilterRuntimeServices(requiresGpu: false);
            var family = (StandardMapExecution)body.CreateExecution!(body, services);
            try
            {
                bool[] truth = Enumerable.Range(0, 16).Select(i => FullOracle(query, Seed((ulong)i), context.Ascension)).ToArray();
                if (ReferenceEquals(query, scalar)) Check(truth.All(t => t), "absent-research scalar control has nonempty candidates");
                for (int i = 0; i < 16; i++)
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Seed((ulong)i));
                    Check(family.MatchesRoot(root) == truth[i], "complete M preserves every scalar, drawing and research obligation");
                    Check(StandardMapExactEvaluator.Evaluate(request.Model, Seed((ulong)i)).Disposition ==
                        (truth[i] ? SearchDisposition.Match : SearchDisposition.NoMatch), "independent map Exact agrees with full combined oracle");
                }
                var batch = new SearchBatch(0, 16);
                foreach (bool compact in new[] { false, true })
                {
                    var input = compact ? FamilyCandidateSet.FromAbi1(batch, [0, 2, 5, 8, 15]) : FamilyCandidateSet.Dense(batch);
                    ulong[] expected = input.EnumerateLogicalOrdinals().Where(i => truth[(int)i]).ToArray();
                    var output = await family.FilterAsync(services, input, default);
                    Check(output.Batch == batch && output.ExportAbi1().Span.SequenceEqual(expected),
                        "M dense and compact output is sorted batch-local ABI1 and agrees with full reference oracle");
                }
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                bool threw = false;
                try { await family.FilterAsync(services, FamilyCandidateSet.Dense(batch), cancelled.Token); }
                catch (OperationCanceledException) { threw = true; }
                Check(threw && !family.ExecutionFaulted, "cancelled M call is not a permanently faulted workspace");
                var retried = await family.FilterAsync(services, FamilyCandidateSet.FromAbi1(batch, [1, 4, 9]), default);
                Check(retried.ExportAbi1().Span.SequenceEqual(new ulong[] { 1, 4, 9 }.Where(i => truth[(int)i]).ToArray()),
                    "same M execution remains reusable after cancellation and previous seed replays");
            }
            finally { await family.DisposeAsync(services); }
        }
        var onlyResearch = Request(SearchQuery.Empty with { ElitePrefixResearch = new(0, 4) }, lossy: true);
        var researchBody = StandardMapFamily.Prepare(onlyResearch.Model, FilterExecutionPreferences.From(onlyResearch.RunOptions), preferGpu: true);
        await using (var services = new FilterRuntimeServices(requiresGpu: false))
        {
            var family = (StandardMapExecution)researchBody.CreateExecution!(researchBody, services);
            try
            {
                Check(!researchBody.UsesGpu && !family.UsesLossy && family.LossyDescription is { Requested: true, Applied: false },
                    "lossy toggle adds neither proposal nor GPU execution to research-only full replay");
                for (ulong ordinal = 0; ordinal < 16; ordinal++)
                    Check(family.MatchesLossyRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(ordinal))) &&
                        family.MatchesRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(ordinal))) == FullOracle(onlyResearch.Model.CompiledSearch.NormalizedQuery, Seed(ordinal), context.Ascension),
                        "research-only lossy setting remains lossless full-map filtering");
            }
            finally { await family.DisposeAsync(services); }
        }
        // A permissive ordinary M predicate gives real survivors so the existing
        // skipped-Exact delivery contract is tested without inventing a natural
        // positive research fixture (the earlier bounded scan found none).
        var skipped = Request(scalar, skip: true);
        await using var session = FamilyExecutionCoordinator.Start(skipped, gpuAvailable: false);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var candidates = new List<SearchCandidate>();
        while (session.TryReadCandidate(out var candidate)) { candidates.Add(candidate!); session.AcknowledgeCandidate(candidate!.Seed); }
        Check(candidates.Count == 16 && candidates.All(c => c.IsUnverified && !c.IsUnfiltered),
            "M survivors with Exact skipped remain filtered and explicitly unverified");
    }

    private static bool FullOracle(SearchQuery query, string seed, int ascension)
    {
        var maps = new Dictionary<int, GeneratedStandardMap>();
        GeneratedStandardMap Observe(int act)
        {
            if (!maps.TryGetValue(act, out var map)) maps.Add(act, map = Beta111StandardMapGenerator.GenerateStandardMap(seed,
                StandardMapFamily.Context(act, ascension), mode: MapReplayMode.Reference));
            return map;
        }
        bool scalars = StandardMapFamily.Matches(query.StandardMaps, act => MapBasisScalars.Evaluate(Observe(act)));
        bool drawings = query.StandardMapRoutes.All(route => MapRouteSequenceEvaluator.Evaluate(Observe(route.Act), route.ToSequenceQuery()).Matches);
        bool research = query.ElitePrefixResearch is not { } condition || (condition.Mode == ElitePrefixResearchMode.FloorSeven
            ? FloorSevenOracle(Observe(1)) : Oracle(Observe(1), condition.ExtraMonsters, condition.Unknowns));
        return scalars && drawings && research;
    }

    // Independent oracle enumerates every branch's room list through its first
    // Elite, then counts the list. It deliberately uses no matcher memo/state.
    private static bool Oracle(GeneratedStandardMap map, int monsters, int unknowns)
    {
        bool all = map.Start.Children.Count > 0;
        var prefix = new List<MapPointType>();
        void Visit(MapNode node)
        {
            prefix.Add(node.PointType);
            if (node.PointType == MapPointType.Elite || node.Children.Count == 0)
            {
                var middle = prefix.Skip(1).SkipLast(1).ToArray();
                all &= prefix.Count >= 2 && prefix[0] == MapPointType.Monster && prefix[^1] == MapPointType.Elite &&
                    middle.All(t => t is MapPointType.Monster or MapPointType.Unknown) &&
                    middle.Count(t => t == MapPointType.Monster) == monsters && middle.Count(t => t == MapPointType.Unknown) == unknowns;
            }
            else foreach (var child in node.Children) Visit(child);
            prefix.RemoveAt(prefix.Count - 1);
        }
        foreach (var child in map.Start.Children) Visit(child);
        return all;
    }

    private static string Graph(GeneratedStandardMap map) => string.Join("|", map.AllNodes()
        .OrderBy(n => n.Row).ThenBy(n => n.Col).Select(n => $"{n.Row}:{n.Col}:{n.PointType}:{n.CanBeModified}->" +
            string.Join(",", n.Children.OrderBy(c => c.Row).ThenBy(c => c.Col).Select(c => $"{c.Row}:{c.Col}"))));

    private static void PartyCases(SearchContext captured)
    {
        var factory = typeof(CombinationRegressionChecks).GetMethod("PartyContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (int players in new[] { 2, 4 })
        foreach (var condition in new[] { new ElitePrefixResearchCondition(1, 3), ElitePrefixResearchCondition.FloorSeven })
        {
            var context = (SearchContext)factory.Invoke(null, [captured, players, null, null])!;
            var table = SearchQuery.Empty with { Players = Enumerable.Range(0, players).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                { Conditions = slot == players - 1 ? SearchQuery.Empty with { ElitePrefixResearch = condition } : SearchQuery.Empty }).ToArray() };
            var compiled = SearchCompiler.Compile(table, context);
            Check(compiled.NormalizedQuery.ElitePrefixResearch == condition &&
                compiled.NormalizedQuery.Players.All(p => p.Conditions.ElitePrefixResearch is null), "per-seat map research binds once as shared");
            var request = SearchPreparationCompiler.Compile(compiled, new(Seed(0), 1, 1, 1)).Plan!;
            var map = Beta111StandardMapGenerator.GenerateStandardMap(Seed(0), StandardMapFamily.Context(1, context.Ascension) with { IsMultiplayer = true });
            bool expected = condition.Mode == ElitePrefixResearchMode.FloorSeven ? FloorSevenOracle(map) : Oracle(map, 1, 3);
            Check(ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Seed(0))).Evaluation.Disposition ==
                (expected ? SearchDisposition.Match : SearchDisposition.NoMatch), "whole-table Exact retains shared universal obligation");
            var conflict = table with { ElitePrefixResearch = new(0, 3) };
            Check(SearchCompiler.Compile(conflict, context).Status == QueryNormalizationStatus.Impossible, "conflicting shared counts are a valid contradiction");
            if (condition.Mode == ElitePrefixResearchMode.FloorSeven)
            {
                foreach (var compatible in new[] { new ElitePrefixResearchCondition(0, 4), new ElitePrefixResearchCondition(1, 3) })
                {
                    var mixed = table with { ElitePrefixResearch = compatible };
                    var joined = SearchCompiler.Compile(mixed, context);
                    Check(joined.Status == QueryNormalizationStatus.Legal && joined.NormalizedQuery.ElitePrefixResearch == compatible &&
                        joined.NormalizedQuery.Players.All(p => p.Conditions.ElitePrefixResearch is null),
                        "legacy count request implying floor seven preserves both compatible shared obligations");
                    Check(joined.Query == mixed, "shared reduction leaves authored mixed-mode request intact");
                }
                Check(SearchCompiler.Compile(table with { ElitePrefixResearch = new(int.MaxValue, int.MaxValue) }, context).Status == QueryNormalizationStatus.Impossible,
                    "large legacy count sum cannot overflow into compatible floor-seven conjunction");
            }
            var probability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
            Check(probability.Probability is null && probability.UnknownComponents.Any(s => s.Contains("ElitePrefixResearch", StringComparison.Ordinal)),
                "party research probability stays unknown");
            Check(PartyQueryProbability.Estimate(compiled).Probability is null &&
                StandardMapProbabilityEstimator.EstimateParty(SearchSelectivityInput.From(compiled)).Probability is null,
                "direct party probability consumers retain unknown research factor");
        }
    }
}
