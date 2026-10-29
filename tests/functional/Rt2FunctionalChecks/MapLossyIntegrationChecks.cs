using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class MapLossyIntegrationChecks
{
    internal static int Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<int> RunAsync()
    {
        int checks = MapDrawingProgramChecks.Run() + MapPropertyProgramChecks.Run();
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidDataException("MapLossy:" + name); }
        string Seed(ulong i) => VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i);
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 1, 0, 1);
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, BaseGameModelKeys.Characters.Ironclad, 1,
            authority, new("0.111.0", "0.111.0", "beta", "MapLossyChecks", true, ""), AncientOptionConditionProfile.BroadDefault);
        ExactSearchExecutionRequest Request(SearchQuery q, bool enabled = true) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(q, context), new(Seed(1146000000000), 65, 65, 1, MapLossy: new(enabled))).Plan!;
        StandardMapRouteCondition Route(int act, params StandardMapRoomType[][] groups) => new(act,
            groups.Select(g => new StandardMapRouteGroup(g)).ToArray());
        SearchQuery Query(params StandardMapRouteCondition[] routes) => SearchQuery.Empty with { StandardMapRoutes = routes };
        var shape = Query(Route(1, [StandardMapRoomType.Monster, StandardMapRoomType.Unknown],
            [StandardMapRoomType.Shop, StandardMapRoomType.Elite, StandardMapRoomType.RestSite]));
        Check(!MapLossyPlan.Create(Request(shape, false)).Enabled, "default off remains complete");
        Check(MapLossyPlan.Create(Request(shape)).Enabled, "five-type ordered/unordered groups admitted");
        foreach (var room in Enum.GetValues<StandardMapRoomType>())
            Check(MapLossyPlan.Create(Request(Query(Route(1, [room])))).Enabled, "all authored types admitted: " + room);
        Check(MapLossyPlan.Create(Request(Query(Route(1, Enumerable.Repeat(StandardMapRoomType.Elite, 32).ToArray()))))
            .Predicates.Single().Program.AlwaysReject, "proven room budget rejects without truncating authored request");
        var oversized = MapRouteSequenceQuery.FromGroups([new(Enum.GetValues<StandardMapRoomType>().SelectMany(room =>
            Enumerable.Repeat(Enum.Parse<MapPointType>(room.ToString()), 3)).ToArray())]);
        Check(!MapLossyPlan.TryProgram(oversized, 20, out _, out var capacityReason) && capacityReason == "DrawingProgramExceeds512States",
            "physical table capacity remains unsupported, never a logical impossibility");
        var aggregateQuery = SearchQuery.Empty with { StandardMaps = [new(0, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 4, true)] };
        var aggregate = MapLossyPlan.Create(Request(aggregateQuery));
        Check(aggregate.Enabled && aggregate.Predicates.Count == 0 && aggregate.Properties.Single() is { Scope: 0, Metric: 5, Value: 4 } &&
            aggregate.Acts.Select(a => a.Act).SequenceEqual([1, 2, 3]) && aggregate.Acts.All(a => a.PropertyMetrics.SequenceEqual([5])),
            "legal sum remains one property over three same-seed Act contexts");
        var daily = MapLossyPlan.Create(Request(SearchQuery.Empty with { StandardMaps = [new(1, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtLeast, 3, true)] }));
        Check(daily.Predicates.Count == 0 && daily.Properties.Single() is { Scope: 1, Metric: 6, Value: 3 },
            "daily Rest uses the original property threshold including fixed rooms");
        foreach (var metric in Enum.GetValues<StandardMapMetric>()) foreach (var comparison in Enum.GetValues<StandardMapComparison>())
        {
            var condition = new StandardMapSearchCondition(1, metric, comparison, 2);
            var plan = MapLossyPlan.Create(Request(SearchQuery.Empty with { StandardMaps = [condition] }));
            Check(plan.Properties.Single() is var property && property.Metric == (int)metric && property.Comparison == comparison &&
                property.Value == 2 && plan.Acts.Single().PropertyMetrics.SequenceEqual([(int)metric]),
                "all nine property bounds preserve authored comparison: " + metric + "/" + comparison);
        }
        Check(!MapLossyPlan.TryProgram(MapRouteSequenceQuery.FromGroups([], [new(MapPointType.RestSite, 0, 0)]),
            12, out _, out var countReason) && countReason == "AdditionalCountsFullFinalOnly",
            "unsupported extra route counts remain an explicit Full obligation");
        var supportedRequest = Request(aggregateQuery);
        var unsupportedRequest = supportedRequest with { CompiledSearch = supportedRequest.CompiledSearch with
            { Context = supportedRequest.CompiledSearch.Context with { ProfileId = RuntimeProfileId.Beta110 } } };
        var unsupported = MapLossyPlan.Describe(unsupportedRequest, false, false);
        Check(unsupported is { Requested: true, Applied: false, Backend: "FullFinal" } &&
            unsupported.FullFinalRemainders.SequenceEqual(["Beta111StandardMapsOnly"]) &&
            unsupportedRequest.CompiledSearch.NormalizedQuery.StandardMaps.SequenceEqual(supportedRequest.CompiledSearch.NormalizedQuery.StandardMaps),
            "unavailable proposal keeps the complete original query and named Full remainder");
        // Every general DFA is checked against the established graph predicate on
        // the exact same private checkpoint, including mixed ordered groups.
        MapRouteSequenceQuery[] sequences = [new([MapPointType.Monster]), new([MapPointType.Unknown, MapPointType.Shop]),
            MapRouteSequenceQuery.FromGroups([new([MapPointType.Monster, MapPointType.Unknown]), new([MapPointType.Shop, MapPointType.Elite, MapPointType.RestSite])]),
            MapRouteSequenceQuery.FromGroups([new([MapPointType.Elite, MapPointType.Elite, MapPointType.RestSite,
                MapPointType.Unknown, MapPointType.Unknown, MapPointType.Monster, MapPointType.Shop])])];
        foreach (int ascension in new[] { 0, 1 }) foreach (int act in new[] { 1, 2, 3 })
        {
            var map = new BoundedActMap(StandardMapFamily.Context(act, ascension));
            foreach (ulong i in new ulong[] { 0, 37, 1146000000031 })
            {
                map.ReplayAssignmentRound1Root(Beta111Profile.Instance.ComputeRootSeed(Seed(i)));
                var graph = map.SnapshotAssignmentRouteGraph();
                var rng = map.RngState; int rngCalls = map.RngCalls;
                int[] properties = MapPropertyProgramChecks.Observe(graph.Start, graph.Boss, true);
                string before = MapPropertyProgramChecks.Fingerprint(graph.Start);
                for (int metric = 0; metric < MapPropertyProgram.MetricCount; metric++)
                    Check(map.EvaluateAssignmentProperty(metric) == properties[metric], "same-root graph property parity " + metric);
                Check(map.RngState == rng && map.RngCalls == rngCalls &&
                    MapPropertyProgramChecks.Fingerprint(map.SnapshotAssignmentRouteGraph().Start) == before,
                    "Round1 property reads preserve raw Unassigned types, topology and RNG");
                // The snapshot owns independent nodes. Project only that oracle
                // copy; never rewrite the numerical assignment or its RNG state.
                var pending = new Stack<MapNode>(); var seen = new HashSet<MapNode>(); pending.Push(graph.Start);
                while (pending.TryPop(out var node))
                {
                    if (!seen.Add(node)) continue;
                    if (node.CanBeModified && node.PointType == MapPointType.Unassigned) node.PointType = MapPointType.Monster;
                    foreach (var child in node.Children) pending.Push(child);
                }
                foreach (var sequence in sequences)
                {
                    int budget = Beta111StandardMapGenerator.GetNumberOfRooms(StandardMapFamily.Context(act, ascension).Act, false) - 3;
                    Check(MapLossyPlan.TryProgram(sequence, budget, out var program, out _), "drawing program compilation");
                    Check(map.MatchAssignmentProgram(program) == MapRouteSequenceEvaluator.Evaluate(graph.Start, graph.Boss, sequence).Matches,
                        "Round1 projected same-route graph parity");
                }
                bool refusesFinal = false;
                try { map.SnapshotFinalRouteGraph(); } catch (InvalidOperationException) { refusesFinal = true; }
                Check(refusesFinal, "checkpoint cannot masquerade as Final");
                refusesFinal = false;
                try { map.EvaluateFinalScalars(); } catch (InvalidOperationException) { refusesFinal = true; }
                Check(refusesFinal, "property observations cannot bypass the complete Final scalar guard");
                if (i == 37)
                {
                    var lawAuthority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, ascension, 0, 1);
                    var lawContext = SearchContextFactory.From(RuntimeProfileId.Beta111, BaseGameModelKeys.Characters.Ironclad, ascension,
                        lawAuthority, context.Detection, AncientOptionConditionProfile.BroadDefault);
                    ExactSearchExecutionRequest LawRequest(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(
                        SearchCompiler.Compile(query, lawContext), new(Seed(i), 1, 1, 1, MapLossy: new(true))).Plan!;
                    var bounds = Enum.GetValues<StandardMapMetric>().SelectMany(metric => new[]
                    {
                        new StandardMapSearchCondition(act, metric, StandardMapComparison.AtLeast, properties[(int)metric]),
                        new StandardMapSearchCondition(act, metric, StandardMapComparison.AtMost, properties[(int)metric])
                    }).ToArray();
                    var mixed = MapPropertyProgramChecks.MixedDrawing(graph.Start, graph.Boss, act);
                    var conjunction = Query(mixed) with { StandardMaps = bounds };
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Seed(i));
                    var combinedFamily = new StandardMapFamily(LawRequest(conjunction));
                    await using var lawExecution = new FamilyExecutionContext(requiresGpu: false);
                    try
                    {
                        Check(combinedFamily.LossyPlan.Acts.Single() is var actPlan && actPlan.Act == act &&
                            actPlan.Drawings.Count == 1 && actPlan.PropertyMetrics.SequenceEqual(Enumerable.Range(0, 9)),
                            "one Act owns all nine independent metrics and the mixed drawing");
                        Check(combinedFamily.MatchesLossyRoot(root), "all inclusive lower/upper bounds AND mixed drawing accept their graph witness");
                        Check(combinedFamily.MatchesRoot(root) == (StandardMapExactEvaluator.Evaluate(LawRequest(conjunction), Seed(i)).Disposition == SearchDisposition.Match),
                            "complete M keeps every combined obligation even when the candidate differs from Final");
                    }
                    finally { await combinedFamily.DisposeAsync(lawExecution); }
                    foreach (var impossibleBound in new[]
                    {
                        new StandardMapSearchCondition(act, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, properties[0] + 1),
                        new StandardMapSearchCondition(act, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtMost, properties[0] - 1)
                    })
                    {
                        var rejectedFamily = new StandardMapFamily(LawRequest(conjunction with
                            { StandardMaps = bounds.Where(c => c.Metric != StandardMapMetric.GuaranteedMonster).Append(impossibleBound).ToArray() }));
                        try { Check(!rejectedFamily.MatchesLossyRoot(root), "one opposite bound rejects the entire property/drawing AND"); }
                        finally { await rejectedFamily.DisposeAsync(lawExecution); }
                    }
                }
            }
        }
        var aggregateMaps = Enumerable.Range(1, 3).Select(act => new BoundedActMap(StandardMapFamily.Context(act, 1))).ToArray();
        ulong aggregateRoot = Beta111Profile.Instance.ComputeRootSeed(Seed(37));
        foreach (var map in aggregateMaps) map.ReplayAssignmentRound1Root(aggregateRoot);
        var aggregateGraphs = aggregateMaps.Select(map => map.SnapshotAssignmentRouteGraph()).ToArray();
        var aggregateValues = aggregateGraphs.Select(graph => MapPropertyProgramChecks.Observe(graph.Start, graph.Boss, true)).ToArray();
        int restSum = aggregateValues.Sum(values => values[6]);
        var multiAct = Query(MapPropertyProgramChecks.MixedDrawing(aggregateGraphs[0].Start, aggregateGraphs[0].Boss, 1),
            MapPropertyProgramChecks.MixedDrawing(aggregateGraphs[2].Start, aggregateGraphs[2].Boss, 3)) with { StandardMaps =
        [new(0, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtLeast, restSum, true),
            new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtMost, aggregateValues[0][0]),
            new(2, StandardMapMetric.ReachableMaxUnknown, StandardMapComparison.AtLeast, aggregateValues[1][7]),
            new(3, StandardMapMetric.ForcedMonsterPrefix, StandardMapComparison.AtMost, aggregateValues[2][8])] };
        var multiFamily = new StandardMapFamily(Request(multiAct));
        await using (var multiExecution = new FamilyExecutionContext(requiresGpu: false))
        {
            try
            {
                Check(aggregateValues.All(values => values[6] < restSum) && multiFamily.MatchesLossyRoot(aggregateRoot),
                    "Scope0 sum accepts even though no individual Act reaches the aggregate threshold");
                foreach (ulong ordinal in new ulong[] { 0, 37, 1146000000031 })
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Seed(ordinal));
                    Check(multiFamily.MatchesLossyRoot(root) == MapPropertyProgramChecks.ProposalOracle(multiAct, root, 1),
                        "multi-Act same root preserves the matching context and all AND obligations");
                }
            }
            finally { await multiFamily.DisposeAsync(multiExecution); }
        }
        foreach (int count in new[] { 1, 31, 32, 33, 65 })
        {
            var bytes = new byte[16]; var expected = new List<int>();
            for (int i = 0; i < count; i += 3) { bytes[i / 8] |= (byte)(1 << (i % 8)); expected.Add(i); }
            Check(MapLossyGpuExecutor.DecodeBitmap(bytes, count).SequenceEqual(expected), "bitmap sparse sorted decoding");
            bytes[count / 8] |= (byte)(1 << (count % 8)); bool rejected = false;
            try { MapLossyGpuExecutor.DecodeBitmap(bytes, count); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "stale tail rejected");
        }
        var offRequest = Request(shape, false);
        var legacyOff = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(shape, context),
            offRequest.RunOptions with { MapLossy = null }).Plan!;
        Check(legacyOff.SnapshotFingerprint == offRequest.SnapshotFingerprint, "missing legacy option preserves default-off snapshot identity");
        Check(SearchContinuationIdentity.For(legacyOff) == SearchContinuationIdentity.For(legacyOff.CompiledSearch) &&
            SearchContinuationIdentity.For(legacyOff) == SearchContinuationIdentity.For(offRequest), "off continuation preserves pre-lossy model identity");
        Check(Request(shape).SnapshotFingerprint != offRequest.SnapshotFingerprint &&
            SearchContinuationIdentity.For(Request(shape)) != SearchContinuationIdentity.For(offRequest), "opt-in snapshot and continuation differ from complete search");
        var currentPolicy = Request(shape);
        var oldStrategy = new SearchContinuationSnapshot(currentPolicy.CompiledSearch.SemanticFingerprint, "old-attempt", 64, [], [])
        {
            ContinuationFingerprint = SearchContinuationIdentity.For(currentPolicy.CompiledSearch, currentPolicy.CombatRewardRoutePolicy) +
                ":MapLossy:AssignmentRound1.v1:batch=16384",
            MapSearch = new(new(true), true, "Cpu", "AssignmentRound1.20261028.v1", "", "Act1.Drawing", [], 0, 64, "old-attempt")
        };
        Check(!SearchContinuationIdentity.Matches(oldStrategy, currentPolicy, SearchContinuationIdentity.For(currentPolicy)) &&
            oldStrategy.MapSearch.Strategy == "AssignmentRound1.20261028.v1", "new drawing scope cannot borrow or rewrite old E/R interval");
        var oldDrawing = oldStrategy with
        {
            ContinuationFingerprint = SearchContinuationIdentity.For(currentPolicy.CompiledSearch, currentPolicy.CombatRewardRoutePolicy) +
                ":MapLossy:AssignmentRound1.Drawing5.Multiword.20261028.v2:batch=16384",
            PendingCandidates = [new(65, Seed(65), "Exact", "CandidateCheckFailed:IOException", "original property-independent failure",
                currentPolicy.CompiledSearch.SemanticFingerprint, "old-attempt", DateTimeOffset.UnixEpoch)],
            MapSearch = oldStrategy.MapSearch with { Strategy = "AssignmentRound1.Drawing5.Multiword.20261028.v2" }
        };
        var restoredOld = JsonSerializer.Deserialize<SearchContinuationSnapshot>(JsonSerializer.Serialize(oldDrawing))!;
        Check(!SearchContinuationIdentity.Matches(restoredOld, currentPolicy, SearchContinuationIdentity.For(currentPolicy)) &&
            restoredOld.NextOrdinal == 64 && restoredOld.PendingCandidates.Single() == oldDrawing.PendingCandidates.Single() &&
            restoredOld.MapSearch!.Strategy == oldDrawing.MapSearch.Strategy && restoredOld.MapSearch.EndOrdinalExclusive == 64,
            "old drawing strategy retains frontier, provenance and original pending cause after save");
        var offFamily = new StandardMapFamily(offRequest);
        await using (var offExecution = new FamilyExecutionContext(requiresGpu: false))
        {
            try
            {
                var offBatch = new SearchBatch(1146000000000, 9);
                var offInput = FamilyCandidateSet.Dense(offBatch);
                var actual = await offFamily.InvokeAsync(offExecution, new(offBatch, offRequest), offInput, CancellationToken.None);
                var expected = offInput.EnumerateLogicalOrdinals().Where(i => offFamily.MatchesRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(offBatch.GlobalCandidate(i)))));
                Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "disabled actual M executes complete Final");
            }
            finally { await offFamily.DisposeAsync(offExecution); }
        }
        StandardMapSearchCondition[] broadProperties = Enum.GetValues<StandardMapMetric>().SelectMany(metric => new[]
        {
            new StandardMapSearchCondition(1, metric, StandardMapComparison.AtLeast, 0),
            new StandardMapSearchCondition(1, metric, StandardMapComparison.AtMost, 20)
        }).Concat([new(2, StandardMapMetric.GuaranteedRest, StandardMapComparison.AtLeast, 1),
            new(0, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtLeast, 3, true)]).ToArray();
        var combinedShape = shape with { StandardMaps = broadProperties };
        var request = Request(combinedShape); var batch = new SearchBatch(1146000000000, 65);
        var family = new StandardMapFamily(request); await using var execution = new FamilyExecutionContext(requiresGpu: false);
        try
        {
            foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromAbi1(batch, new ulong[] { 2, 8, 31, 32, 64 }) })
            {
                var output = await family.InvokeAsync(execution, new(batch, request), input, CancellationToken.None);
                var expected = input.EnumerateLogicalOrdinals().Where(i =>
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Seed(batch.GlobalCandidate(i)));
                    return MapPropertyProgramChecks.ProposalOracle(combinedShape, root, request.Ascension) &&
                        StandardMapExactEvaluator.Evaluate(request, Seed(batch.GlobalCandidate(i))).Disposition == SearchDisposition.Match;
                }).ToArray();
                Check(output.EnumerateLogicalOrdinals().SequenceEqual(expected), "CPU Dense/Compact keeps original ABI1");
                foreach (ulong ordinal in output.EnumerateLogicalOrdinals())
                    Check(StandardMapExactEvaluator.Evaluate(request, Seed(batch.GlobalCandidate(ordinal))).Disposition == SearchDisposition.Match,
                        "candidate survivor independently verified Final");
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
            try { await family.InvokeAsync(execution, new(batch, request), FamilyCandidateSet.Dense(batch), cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cancel before invocation publishes no candidates");
        }
        finally { await family.DisposeAsync(execution); }
        // A real bounded production run owns the Exact, delivery receipt and safe
        // cursor lifecycle. Disable only the test plan's small-tail handoff so M
        // actually proposes rather than sending this tiny fixture straight to Exact.
        var simple = Query(Route(1, [StandardMapRoomType.Monster, StandardMapRoomType.Unknown, StandardMapRoomType.Shop])) with
            { StandardMaps = broadProperties };
        var production = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(simple, context),
            new(Seed(1146000000000), 65, 2, 1, MapLossy: new(true))).Plan!;
        var skipRequest = ExactSearchExecutionRequestFactory.Compile(production.CompiledSearch,
            production.RunOptions with { SkipExactValidation = true }).Plan!;
        var restrictedPlan = FamilyExecutionCoordinator.Plan(skipRequest, gpuAvailable: true, gpuFloat64Available: false);
        var restrictedMap = restrictedPlan.ExecutionOrderUnits.OfType<StandardMapFamily>().Single();
        Check(skipRequest.RunOptions.SkipExactValidation && restrictedPlan.FinalValidationSkipped && !restrictedMap.ConditionPerformance.UsesGpu &&
            restrictedMap.UsesLossy && restrictedMap.LossyDescription.Reason == "Float64Unavailable",
            "map-only FP64 absence selects equivalent CPU Round1+Final and preserves explicit SkipExact");
        await using (var restrictedExecution = new FamilyExecutionContext(requiresGpu: false))
        {
            try
            {
                var smallInput = FamilyCandidateSet.FromAbi1(batch, new ulong[] { 0, 31, 64 });
                var output = await restrictedMap.InvokeAsync(restrictedExecution, new(batch, skipRequest), smallInput, CancellationToken.None);
                var expected = smallInput.EnumerateLogicalOrdinals().Where(i =>
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Seed(batch.GlobalCandidate(i)));
                    return restrictedMap.MatchesLossyRoot(root) && restrictedMap.MatchesRoot(root);
                });
                Check(output.EnumerateLogicalOrdinals().SequenceEqual(expected) && restrictedMap.ActualProposalCalls == 1,
                    "restricted CPU body actually executes unchanged Round1 proposal followed by complete Final");
            }
            finally { await restrictedMap.DisposeAsync(restrictedExecution); }
        }
        var productionFamily = new StandardMapFamily(production);
        Check(production.CompiledSearch.NormalizedQuery.StandardMaps.Count == broadProperties.Length &&
            productionFamily.LossyPlan.Properties.Count == broadProperties.Length && productionFamily.LossyPlan.Acts.Count == 3,
            "real production request retains every authored property together with the mixed drawing and aggregate");
        var executionPlan = FamilyPlanner.InOrder([productionFamily]) with { AllowSmallCandidateExact = false };
        await using (var session = new ProductionSearchSession(production, executionPlan,
            FamilySearchEtaProjectorV1.Project(production, executionPlan), 65, 16, null, ""))
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            var candidates = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate))
            { candidates.Add(candidate!); session.AcknowledgeCandidate(candidate!.Seed); }
            Check(candidates.Count == 2 && candidates.All(c => c.ValidationDisposition == SearchDisposition.Match && !c.IsUnverified),
                "real lossy ProductionSearchSession publishes target two verified matches");
            Check(productionFamily.ActualProposalCalls == 1 && productionFamily.ActualProposalInputs == 65,
                "real session executed one CPU proposal over its bounded source window");
            foreach (var candidate in candidates)
            {
                var receipt = candidate.MapSearch;
                Check(receipt is { Applied: true, Backend: "Cpu", ObservedProposalCalls: 1, ObservedProposalInputs: 65 } &&
                    receipt.RequestedOptions.Enabled && receipt.Strategy == MapLossyPlan.Strategy,
                    "candidate retains actual CPU lossy strategy and proposal observations");
                Check(receipt!.StartOrdinal == 1146000000000 && receipt.EndOrdinalExclusive == 1146000000065,
                    "candidate provenance names source window without claiming full scan completion");
                Check(receipt.Scope.Contains("Scope0.ReachableMaxRest", StringComparison.Ordinal) &&
                    receipt.Scope.Contains("Scope1.ForcedMonsterPrefix", StringComparison.Ordinal) && receipt.Scope.Contains("Act1.Drawing", StringComparison.Ordinal),
                    "actual provenance records properties, aggregate and drawing together");
                var exact = StandardMapExactEvaluator.Evaluate(production, candidate.Seed);
                Check(exact.Disposition == SearchDisposition.Match, "independent Final confirms production candidate");
                var proof = candidate.MatchEvidence.Single(e => e.ConditionId == "M.StandardMap.RouteSequence.Act1");
                var independentProof = exact.Evidence.Single(e => e.ConditionId == proof.ConditionId);
                Check(proof.RouteId == independentProof.RouteId && proof.OutcomeId == independentProof.OutcomeId,
                    "production witness is the deterministic independent Final route, never Assignment");
                static MapPosition[] Parse(string text) => text.Split('>', StringSplitOptions.RemoveEmptyEntries).Select(piece =>
                {
                    var pair = piece.Split(':'); return new MapPosition(int.Parse(pair[1].AsSpan(1)), int.Parse(pair[0].AsSpan(1)));
                }).ToArray();
                var map = Beta111MapPredictor.Predict(candidate.Seed, RuntimeProfileId.Beta111, new("ACT", "OVERGROWTH"), 0, 1, 1, false, default);
                var path = Parse(proof.RouteId!["act-1:".Length..]);
                var selected = Parse(proof.OutcomeId!.Trim('[', ']'));
                Check(new MapRouteSet(map).IsLegal(path) && selected.Length == 3 && selected.All(path.Contains) &&
                    selected.Select(p => map.Points.Single(point => point.Position == p)).All(point => point.CanBeModified) &&
                    selected.Select(p => map.Points.Single(point => point.Position == p).Type).ToHashSet()
                        .SetEquals([MapPointType.Monster, MapPointType.Unknown, MapPointType.Shop]),
                    "independently generated complete Start-Boss path contains the authored nonfixed Monster/Unknown/Shop");
            }
            var continuation = session.CaptureContinuation()!;
            Check(session.GetProgress().State == SearchRunState.Completed && continuation.PendingCandidates.Count == 0 &&
                continuation.UnreceivedOrdinals is { Count: 0 }, "real production delivery ACK leaves no pending/unreceived work");
            Check(continuation.NextOrdinal > 1146000000000 && continuation.NextOrdinal <= 1146000000065 &&
                continuation.MapSearch!.StartOrdinal == 1146000000000 && continuation.MapSearch.EndOrdinalExclusive == continuation.NextOrdinal,
                "continuation records its actual safe frontier within source window");
            Check(candidates.All(c => c.MapSearch!.AttemptFingerprint == continuation.MapSearch!.AttemptFingerprint) &&
                continuation.MapSearch is { Backend: "Cpu", ObservedProposalCalls: 1, ObservedProposalInputs: 65 },
                "candidate and continuation share one observed attempt identity");
            var restored = JsonSerializer.Deserialize<SearchContinuationSnapshot>(JsonSerializer.Serialize(continuation))!;
            Check(restored.MapSearch!.AttemptFingerprint == continuation.MapSearch!.AttemptFingerprint &&
                SearchContinuationIdentity.Matches(restored, production, SearchContinuationIdentity.For(production)),
                "persisted continuation keeps lossy attempt and resumes only under same policy");
            var completePolicy = ExactSearchExecutionRequestFactory.Compile(production.CompiledSearch,
                production.RunOptions with { MapLossy = MapLossyOptions.Default }).Plan!;
            Check(!SearchContinuationIdentity.Matches(restored, completePolicy, SearchContinuationIdentity.For(completePolicy)),
                "a lossy frontier cannot become a complete-search frontier");
        }
        Console.WriteLine($"MapLossyIntegration checks={checks}; CPU/DFA/propertyAND/realProductionSession; no claim of lossless recall"); return 0;
    }
}

// Kept in the existing functional entry point. Expected values enumerate whole
// graph paths, independently of the compact scalar DP and drawing state table.
internal static class MapPropertyProgramChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string label)
        { checks++; if (!ok) throw new InvalidDataException("MapProperty:" + label); }
        var fixedLine = new Fixture(31);
        fixedLine.Add(7, MapPointType.Monster, false);
        fixedLine.Add(14, MapPointType.Unassigned);
        fixedLine.Add(21, MapPointType.RestSite, false);
        fixedLine.Edge(3, 7); fixedLine.Edge(7, 14); fixedLine.Edge(14, 21); fixedLine.Edge(21, 31);
        Check(Observe(fixedLine.Start, fixedLine.End, true).SequenceEqual([2, 0, 1, 0, 2, 0, 1, 0, 2]),
            "hand-counted fixed Monster/Rest and initial projected Monster prefix");
        Check(fixedLine.Property(6, true) == 1 && !fixedLine.Drawing([MapPointType.RestSite]),
            "fixed Rest participates in properties and is excluded from drawing");
        byte[] original = fixedLine.Types.ToArray();
        Check(fixedLine.Property(0, false) == 1 && fixedLine.Property(0, true) == 2 &&
            fixedLine.Property(8, false) == 1 && fixedLine.Property(8, true) == 2 && fixedLine.Types.SequenceEqual(original),
            "Round1 Monster projection is explicit and read-only");
        Check(fixedLine.Property(6, true) <= 1, "zero extra Rest uses the fixed-inclusive candidate maximum <=1");

        var branches = new Fixture(38);
        branches.Add(7, MapPointType.Monster); branches.Add(14, MapPointType.Elite); branches.Add(21, MapPointType.RestSite);
        branches.Add(8, MapPointType.Unknown); branches.Add(15, MapPointType.RestSite); branches.Add(22, MapPointType.Unknown);
        branches.Edge(3, 7); branches.Edge(7, 14); branches.Edge(14, 21); branches.Edge(21, 38);
        branches.Edge(3, 8); branches.Edge(8, 15); branches.Edge(15, 22); branches.Edge(22, 38);
        // A reachable dead end and an unreachable Boss-reaching island cannot
        // contribute to either an extremum or a drawing witness.
        branches.Add(9, MapPointType.RestSite); branches.Edge(3, 9);
        branches.Add(16, MapPointType.Elite); branches.Edge(16, 38);
        Check(Observe(branches.Start, branches.End, true).SequenceEqual([0, 0, 1, 0, 1, 1, 1, 2, 0]),
            "hand-counted independent extrema ignore dead ends and islands");
        Check(branches.Property(5, true) >= 1 && branches.Property(7, true) >= 2 &&
            branches.Property(0, true) <= 0 && branches.Property(3, true) <= 0 &&
            !Paths(branches.Start, branches.End).Any(path => path.All(n => n.PointType is not MapPointType.Monster and not MapPointType.Unknown)),
            "different maxima and minima keep their independent complete-route witnesses");
        Check(!branches.Drawing([MapPointType.Elite, MapPointType.Unknown]) &&
            branches.Drawing([MapPointType.Unknown, MapPointType.RestSite]),
            "properties may combine branches while a mixed drawing still needs one actual route");

        var direct = new Fixture(17); direct.Edge(3, 17);
        Check(Observe(direct.Start, direct.End, true).All(value => value == 0), "Start and Boss never contribute");
        foreach (var fixture in new[] { fixedLine, branches, direct }) foreach (bool project in new[] { false, true })
        {
            int[] expected = Observe(fixture.Start, fixture.End, project);
            for (int metric = 0; metric < 9; metric++)
                Check(fixture.Property(metric, project) == expected[metric], "compact DP versus whole-route oracle " + metric);
        }
        // Reusing scratch after a differently shaped graph must not retain an
        // earlier reachable lane or property value.
        int[] scratch = new int[MapPropertyProgram.ScratchLength];
        foreach (var fixture in new[] { branches, direct, fixedLine, branches })
            Check(MapPropertyProgram.Evaluate(fixture.Types, fixture.Degrees, fixture.Children, fixture.Boss, 6, scratch, true) ==
                Observe(fixture.Start, fixture.End, true)[6], "scalar scratch reuse clears old graph state");
        var disconnected = new Fixture(17);
        for (int metric = 0; metric < 9; metric++)
        {
            bool rejected = false;
            try { disconnected.Property(metric, true); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "no complete route never manufactures zero property truth");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool propagated = false;
        try { MapPropertyProgram.Evaluate(branches.Types, branches.Degrees, branches.Children, branches.Boss, 0, scratch, true, cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        Check(propagated, "property cancellation remains cancellation");
        fixedLine.Types[14] = (byte)MapPointType.RestSite;
        Check(fixedLine.Property(6, true) == 2 && fixedLine.Property(6, true) > 1 && fixedLine.Drawing([MapPointType.RestSite]),
            "one mutable extra Rest fails the zero-extra candidate threshold");
        return checks;
    }

    internal static int[] Observe(MapNode start, MapNode boss, bool project)
    {
        MapPointType[] targets = [MapPointType.Monster, MapPointType.Elite, MapPointType.RestSite, MapPointType.Unknown];
        int[] result = [int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 0, 0, 0, 0, int.MaxValue];
        bool complete = false;
        foreach (var path in Paths(start, boss))
        {
            complete = true;
            var rooms = path.Select(n => project && n.PointType == MapPointType.Unassigned ? MapPointType.Monster : n.PointType).ToArray();
            for (int metric = 0; metric < 4; metric++)
            {
                int count = rooms.Count(type => type == targets[metric]);
                result[metric] = Math.Min(result[metric], count);
                result[metric + 4] = Math.Max(result[metric + 4], count);
            }
            result[8] = Math.Min(result[8], rooms.TakeWhile(type => type == MapPointType.Monster).Count());
        }
        if (!complete) throw new InvalidDataException("No complete oracle route.");
        return result;
    }

    internal static StandardMapRouteCondition MixedDrawing(MapNode start, MapNode boss, int act)
    {
        foreach (var path in Paths(start, boss))
        {
            var rooms = path.Where(n => n.CanBeModified).Select(n =>
                n.PointType == MapPointType.Unassigned ? MapPointType.Monster : n.PointType)
                .Where(MapRouteSequenceQuery.Selectable).Distinct().Take(2).ToArray();
            if (rooms.Length == 2)
                return new(act, [new(rooms.Select(room => Enum.Parse<StandardMapRoomType>(room.ToString())).ToArray())]);
        }
        throw new InvalidDataException("Representative checkpoint has no complete mixed drawing route.");
    }

    internal static bool ProposalOracle(SearchQuery query, ulong root, int ascension)
    {
        var values = new Dictionary<int, int[]>();
        foreach (int act in query.StandardMaps.SelectMany(c => c.Scope == 0 ? new[] { 1, 2, 3 } : new[] { c.Scope })
            .Concat(query.StandardMapRoutes.Select(c => c.Act)).Distinct())
        {
            var map = new BoundedActMap(StandardMapFamily.Context(act, ascension)); map.ReplayAssignmentRound1Root(root);
            var graph = map.SnapshotAssignmentRouteGraph();
            values.Add(act, Observe(graph.Start, graph.Boss, true));
            var seen = new HashSet<MapNode>(); var pending = new Stack<MapNode>(); pending.Push(graph.Start);
            while (pending.TryPop(out var node))
            {
                if (!seen.Add(node)) continue;
                if (node.PointType == MapPointType.Unassigned) node.PointType = MapPointType.Monster;
                foreach (var child in node.Children) pending.Push(child);
            }
            foreach (var drawing in query.StandardMapRoutes.Where(c => c.Act == act))
                if (!MapRouteSequenceEvaluator.Evaluate(graph.Start, graph.Boss, drawing.ToSequenceQuery()).Matches) return false;
        }
        foreach (var condition in query.StandardMaps)
        {
            int value = condition.Scope == 0 ? values.Values.Sum(v => v[(int)condition.Metric]) : values[condition.Scope][(int)condition.Metric];
            if (condition.Comparison == StandardMapComparison.AtLeast ? value < condition.Value : value > condition.Value) return false;
        }
        return true;
    }

    internal static string Fingerprint(MapNode start)
    {
        var seen = new HashSet<MapNode>(); var pending = new Stack<MapNode>(); pending.Push(start);
        while (pending.TryPop(out var node))
        { if (seen.Add(node)) foreach (var child in node.Children) pending.Push(child); }
        return string.Join(';', seen.OrderBy(n => n.RawRow).ThenBy(n => n.RawCol).Select(n =>
            $"{n.RawRow}:{n.RawCol}:{n.PointType}:{n.CanBeModified}:" + string.Join(',', n.Children.Select(c => $"{c.RawRow}:{c.RawCol}").Order())));
    }

    private static IEnumerable<MapNode[]> Paths(MapNode start, MapNode boss)
    {
        var path = new List<MapNode>();
        IEnumerable<MapNode[]> Visit(MapNode node)
        {
            if (node == boss) { yield return path.ToArray(); yield break; }
            if (node != start) path.Add(node);
            foreach (var child in node.Children) foreach (var route in Visit(child)) yield return route;
            if (node != start) path.RemoveAt(path.Count - 1);
        }
        return Visit(start);
    }

    private sealed class Fixture
    {
        internal readonly byte[] Types = new byte[119], Degrees = new byte[119], Children = new byte[119 * 7];
        private readonly Dictionary<int, MapNode> _nodes = new();
        private UInt128 _modifiable;
        internal int Boss { get; }
        internal MapNode Start => _nodes[3];
        internal MapNode End => _nodes[Boss];
        internal Fixture(int boss) { Boss = boss; Add(3, MapPointType.RestSite); Add(boss, MapPointType.Monster); }
        internal void Add(int slot, MapPointType type, bool modifiable = true)
        {
            Types[slot] = (byte)type;
            _nodes.Add(slot, new MapNode(slot % 7, slot / 7) { PointType = type, CanBeModified = modifiable });
            if (modifiable) _modifiable |= (UInt128)1 << slot;
        }
        internal void Edge(int parent, int child)
        { Children[parent * 7 + Degrees[parent]++] = (byte)child; _nodes[parent].Children.Add(_nodes[child]); }
        internal int Property(int metric, bool project) =>
            MapPropertyProgram.Evaluate(Types, Degrees, Children, Boss, metric, new int[MapPropertyProgram.ScratchLength], project);
        internal bool Drawing(MapPointType[] rooms)
        {
            var program = MapDrawingProgram.Compile(new MapRouteSequenceQuery(rooms), 12);
            return program.Match(Types, Degrees, Children, _modifiable, Boss, new uint[119 * program.WordCount], true);
        }
    }
}
