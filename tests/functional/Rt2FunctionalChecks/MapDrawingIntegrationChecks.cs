using System.Text.Json;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Tests.FastSearch;

// The existing functional runner, bounded authored semantics and transport corpus.
// Generation/RNG parity is unchanged and belongs to the prior map integration checks.
internal static class MapDrawingIntegrationChecks
{
    private static int _checks;
    private static string Seed(ulong ordinal) => VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
    private static void Check(bool ok, string why)
    { _checks++; if (!ok) throw new InvalidOperationException("Map drawing: " + why); }
    private static StandardMapRouteCondition Route(int act, params StandardMapRoomType[][] groups) =>
        new(act, groups.Select(nodes => new StandardMapRouteGroup(nodes)).ToArray());

    internal static int Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<int> RunAsync()
    {
        MapRouteSequenceChecks.RunSemantics();
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 1, 0, 1);
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, BaseGameModelKeys.Characters.Ironclad, 1,
            authority, new("0.111.0", "0.111.0", "beta", "MapDrawingIntegration", true, ""), AncientOptionConditionProfile.BroadDefault);
        CompiledSearch Compile(SearchQuery query) => SearchCompiler.Compile(query, context);
        ExactSearchExecutionRequest Request(SearchQuery query, ulong first = 0) =>
            ExactSearchExecutionRequestFactory.Compile(Compile(query), new(Seed(first), 9, 9, 1)).Plan
            ?? throw new InvalidOperationException("Map drawing fixture admission");
        SearchQuery Query(params StandardMapRouteCondition[] routes) => SearchQuery.Empty with { StandardMapRoutes = routes };
        string Fingerprint(StandardMapRouteCondition route) => Compile(Query(route)).SemanticFingerprint;
        var grouped = Route(1, [StandardMapRoomType.Elite, StandardMapRoomType.RestSite], [StandardMapRoomType.Shop]);
        Check(Fingerprint(grouped) == Fingerprint(Route(1, [StandardMapRoomType.RestSite, StandardMapRoomType.Elite], [StandardMapRoomType.Shop])),
            "group multiset order is semantically canonical");
        Check(Fingerprint(grouped) != Fingerprint(Route(1, [StandardMapRoomType.Shop], [StandardMapRoomType.Elite, StandardMapRoomType.RestSite])),
            "axis group order changes identity");
        Check(Fingerprint(grouped) != Fingerprint(Route(1, [StandardMapRoomType.Elite, StandardMapRoomType.Elite, StandardMapRoomType.RestSite], [StandardMapRoomType.Shop])),
            "duplicate occurrence changes identity");
        Check(Compile(Query(Route(1, []))).SemanticFingerprint == Compile(SearchQuery.Empty).SemanticFingerprint,
            "empty drawing does not manufacture an active condition");
        foreach (int act in new[] { 0, 4 })
        {
            bool rejected = false;
            try { Compile(Query(Route(act, [StandardMapRoomType.Elite]))); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid drawing Act rejected");
        }
        var total = new StandardMapSearchCondition(0, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 1, true);
        var daily = total with { Scope = 2 };
        var mixed = Query(Route(1, [StandardMapRoomType.Unknown])) with { StandardMaps = [total, daily,
            new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 0)] };
        Check(Compile(mixed).NormalizedQuery.StandardMaps.Count == 3 && Compile(mixed).NormalizedQuery.StandardMapRoutes.Count == 1,
            "total independent maxima + other Act daily + drawing + independent properties coexist");
        bool conflict = false;
        try { Compile(mixed with { StandardMaps = [daily with { Scope = 1 }] }); }
        catch (ArgumentException) { conflict = true; }
        Check(conflict, "one Act cannot execute both route modes");
        Check(Compile(SearchQuery.Empty with { StandardMaps = [total] }).SemanticFingerprint ==
            Compile(SearchQuery.Empty with { StandardMaps = [total], StandardMapRoutes = [] }).SemanticFingerprint,
            "empty route extension preserves scalar-only query identity");
        var estimate = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(Compile(mixed)));
        Check(estimate.Probability is null && estimate.UnknownComponents.Any(s => s.Contains("RouteSequenceJointMissing")),
            "missing drawing joint probability remains explicitly unknown");
        CheckPartyRouteBinding();

        // Guaranteed authored witness from a complete source-faithful map; skip
        // generated fixed rooms exactly as the player-facing drawing contract says.
        var map = Beta111StandardMapGenerator.GenerateStandardMap(Seed(0), StandardMapFamily.Context(1, 1), mode: MapReplayMode.Reference);
        var path = new List<MapNode>(); var node = map.Start;
        while (true) { path.Add(node); if (node == map.Boss) break; node = node.Children.First(); }
        var rooms = path.Where(n => n.CanBeModified && n.PointType is MapPointType.Monster or MapPointType.Elite or MapPointType.RestSite or MapPointType.Shop or MapPointType.Unknown)
            .Take(4).Select(n => Enum.Parse<StandardMapRoomType>(n.PointType.ToString())).ToArray();
        Check(rooms.Length > 0, "positive fixture has authored rooms");
        var positive = Query(Route(1, rooms.Take(2).ToArray(), rooms.Skip(2).ToArray()));
        SearchQuery[] queries = [positive, Query(grouped), mixed,
            Query(Route(2, [StandardMapRoomType.Elite, StandardMapRoomType.Elite])),
            Query(Route(3, [StandardMapRoomType.Shop], [StandardMapRoomType.Unknown])),
            Query(Route(1, Enumerable.Repeat(StandardMapRoomType.Monster, 40).ToArray()))];
        Check(StandardMapExactEvaluator.Evaluate(Request(positive), Seed(0)).Disposition == SearchDisposition.Match,
            "complete natural path supplies positive drawing witness");
        foreach (var query in queries)
        {
            var roundTrip = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(query, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
            Check(Compile(query).SemanticFingerprint == Compile(roundTrip).SemanticFingerprint, "query JSON retains authored route identity");
            var request = Request(roundTrip);
            var batch = new SearchBatch(0, 9);
            bool[] truth = Enumerable.Range(0, 9).Select(i => StandardMapExactEvaluator.Evaluate(request, Seed((ulong)i)).Disposition == SearchDisposition.Match).ToArray();
            var family = new StandardMapFamily(request);
            var execution = new FamilyExecutionContext(requiresGpu: false);
            try
            {
                for (int i = 0; i < 9; i++)
                {
                    var exact = ProductionExactSearchEvaluator.Evaluate(request,
                        TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Seed((ulong)i)));
                    Check(exact.IsMatch == truth[i], "Production Exact agrees with independent map Exact");
                    if (exact.IsMatch) ValidateWitnesses(request, Seed((ulong)i), exact.Evaluation.Evidence);
                }
                foreach (bool compact in new[] { false, true })
                {
                    var input = compact ? FamilyCandidateSet.FromAbi1(batch, [0, 2, 5, 8]) : FamilyCandidateSet.Dense(batch);
                    ulong[] expected = input.EnumerateLogicalOrdinals().Where(i => truth[(int)i]).ToArray();
                    var actual = await family.InvokeAsync(execution, new(batch, request), input, default);
                    Check(actual.Batch == batch && actual.ExportAbi1().Span.SequenceEqual(expected), "M full CPU ordered batch-local dense/compact ABI1 agrees with independent Exact");
                }
            }
            finally { await family.DisposeAsync(execution); await execution.DisposeAsync(); }
        }
        var plan = Request(positive);
        var executionPlan = FamilyPlanner.InOrder([new StandardMapFamily(plan)]);
        await using (var session = new ProductionSearchSession(plan, executionPlan,
            FamilySearchEtaProjectorV1.Project(plan, executionPlan), 9, 16, null, ""))
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            var candidates = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate)) { candidates.Add(candidate!); session.AcknowledgeCandidate(candidate!.Seed); }
            Check(candidates.Any(c => c.Seed == Seed(0)) && candidates.All(c => !c.IsUnverified), "real small Search publishes confirmed positive drawing seed");
            Check(candidates.All(c => c.MatchEvidence.Any(e => e.ConditionId == "M.StandardMap")), "Search preserves map final evidence");
            foreach (var candidate in candidates) ValidateWitnesses(plan, candidate.Seed, candidate.MatchEvidence);
            Check(session.GetProgress().State == SearchRunState.Completed && session.CaptureContinuation()!.PendingCandidates.Count == 0,
                "small Search finishes without pending work");
        }
        Console.WriteLine($"MAP_DRAWING_INTEGRATION_PASS checks={_checks};queries={queries.Length};seeds=9;M=Dense+Compact;Exact=Independent+Production;Search=realSmallSession");
        return 0;
    }

    private static void CheckPartyRouteBinding()
    {
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var factory = typeof(CombinationRegressionChecks).GetMethod("PartyContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (int players in new[] { 2, 4 })
        {
            var context = (SearchContext)factory.Invoke(null, [captured, players, null, null])!;
            var route = Route(1, Enumerable.Repeat(StandardMapRoomType.Monster, 40).ToArray());
            var table = SearchQuery.Empty with { Players = Enumerable.Range(0, players).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                { Conditions = slot == players - 1 ? SearchQuery.Empty with { StandardMapRoutes = [route] } : SearchQuery.Empty }).ToArray() };
            var bound = PartyInitialQuery.BindSharedConditions(table);
            Check(bound.StandardMapRoutes.Count == 1 && bound.Players.All(p => p.Conditions.StandardMapRoutes.Count == 0),
                "party seat route is retained once at shared level " + players);
            var compiled = SearchCompiler.Compile(table, context);
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(Seed(0), 1, 1, 1)).Plan
                ?? throw new InvalidOperationException("Party map fixture admission");
            Check(request.CompiledSearch.NormalizedQuery.StandardMapRoutes.Single().Groups[0].Nodes.Count == 40,
                "party request keeps original route multiset " + players);
            Check(StandardMapExactEvaluator.Evaluate(request, Seed(0)).Disposition == SearchDisposition.NoMatch &&
                ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Seed(0))).Evaluation.Disposition == SearchDisposition.NoMatch,
                "independent contradiction rejects through whole-table Exact " + players);
            var estimate = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
            Check(estimate.Probability is null && estimate.UnknownComponents.Any(s => s.Contains("RouteSequenceJointMissing")),
                "party drawing probability is explicitly unknown " + players);
            var preview = RolltheSpire2.Ui.Pages.Search.WorkbenchProbabilityPreview.Compute(compiled, 1, 1, true);
            Check(preview.Map is { Probability: null } && preview.Map.EvidenceCode.Contains("RouteSequenceJointMissing"),
                "actual Party workbench preview preserves unknown drawing probability without nested envelope " + players);
            Check(FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false).All(f => f is not StandardMapFamily),
                "party does not acquire a new M registration " + players);
        }
    }

    private static void ValidateWitnesses(ExactSearchExecutionRequest plan, string seed, IReadOnlyList<SearchMatchEvidence> evidence)
    {
        static MapPosition[] Parse(string value) => value.Split('>', StringSplitOptions.RemoveEmptyEntries).Select(piece =>
        {
            string[] pair = piece.Split(':');
            return new MapPosition(int.Parse(pair[1].AsSpan(1)), int.Parse(pair[0].AsSpan(1)));
        }).ToArray();
        foreach (var route in plan.CompiledSearch.NormalizedQuery.StandardMapRoutes)
        {
            var proof = evidence.Single(e => e.ConditionId == $"M.StandardMap.RouteSequence.Act{route.Act}");
            var map = Beta111MapPredictor.Predict(seed, RuntimeProfileId.Beta111,
                new ModelKey("ACT", StandardMapFamily.Context(route.Act, plan.Ascension).Act.ToString().ToUpperInvariant()),
                route.Act - 1, plan.Ascension, 1, false, default);
            string prefix = $"act-{route.Act}:";
            Check(proof.RouteId?.StartsWith(prefix) == true, "drawing proof identifies its Act");
            var path = Parse(proof.RouteId![prefix.Length..]);
            Check(new MapRouteSet(map).IsLegal(path), "drawing proof is one complete legal independently generated path");
            var nodes = map.Points.ToDictionary(p => p.Position);
            var groups = proof.OutcomeId!.Split('|').Select(g => Parse(g.Trim('[', ']'))).ToArray();
            Check(groups.Length == route.Groups.Count, "drawing proof retains each authored group");
            int previous = -1;
            for (int i = 0; i < groups.Length; i++)
            {
                int[] indices = groups[i].Select(p => Array.IndexOf(path, p)).ToArray();
                Check(indices.All(index => index > previous) && indices.Distinct().Count() == indices.Length,
                    "groups cannot reuse or interleave witness rooms");
                Check(groups[i].All(p => nodes[p].CanBeModified), "generated fixed endpoints never pay authored occurrences");
                Check(groups[i].Select(p => nodes[p].Type.ToString()).Order().SequenceEqual(route.Groups[i].Nodes.Select(t => t.ToString()).Order()),
                    "each proof group preserves authored multiset including duplicates");
                if (indices.Length > 0) previous = indices.Max();
            }
        }
    }
}
