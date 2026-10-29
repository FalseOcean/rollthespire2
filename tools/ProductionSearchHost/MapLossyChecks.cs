using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;

internal static class MapLossyChecks
{
    internal static async Task RunAsync(ExactSearchExecutionRequest basis, string output)
    {
        Directory.CreateDirectory(output); int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidDataException("MapLossyGpu:" + name); }
        var batch = new SearchBatch(1146000000000, 16385);
        ulong Root(ulong ordinal) => Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, batch.GlobalCandidate(ordinal)));
        await using var execution = new FamilyExecutionContext(requiresGpu: true);
        MapLossyGpuExecutor? gpu = null;
        try
        {
            gpu = await execution.ExecuteGpuAsync(rd => new MapLossyGpuExecutor(rd, 16384), CancellationToken.None);
            bool widePositive = false;
            long bufferBytes = gpu.RetainedBufferBytes;
            foreach (int ascension in new[] { 0, 1 }) foreach (int act in new[] { 1, 2, 3 })
            {
                var map = new BoundedActMap(StandardMapFamily.Context(act, ascension));
                int budget = Beta111StandardMapGenerator.GetNumberOfRooms(StandardMapFamily.Context(act, ascension).Act, false) - 3;
                var wide = new[] { MapPointType.Monster, MapPointType.Unknown, MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop }
                    .SelectMany((room, i) => Enumerable.Repeat(room, i < 2 ? 3 : 2)).ToArray();
                var queries = new[] { new MapRouteSequenceQuery([MapPointType.Monster]), new([MapPointType.Unknown]), new([MapPointType.Shop]),
                    MapRouteSequenceQuery.FromGroups([new MapRouteGroup([MapPointType.Monster, MapPointType.Unknown]), new MapRouteGroup([MapPointType.Shop, MapPointType.Elite, MapPointType.RestSite])]),
                    MapRouteSequenceQuery.FromGroups([new MapRouteGroup([MapPointType.Elite, MapPointType.Elite, MapPointType.RestSite,
                        MapPointType.Unknown, MapPointType.Unknown, MapPointType.Monster, MapPointType.Shop])]),
                    MapRouteSequenceQuery.FromGroups([new MapRouteGroup(wide)]), new([MapPointType.Shop]) };
                foreach (var query in queries)
                {
                    Check(MapLossyPlan.TryProgram(query, budget, out var program, out _), "compile");
                    var predicate = new MapLossyPredicate(act, program, "Parity");
                    foreach (int length in new[] { 33, 1 })
                    {
                        var input = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, length).Select(i => (ulong)(i * 3 + 7)).ToArray());
                        var actual = await execution.ExecuteGpuAsync(_ => gpu.Propose(input, predicate, ascension, CancellationToken.None, poisonBitmap: true), CancellationToken.None);
                        var expected = input.EnumerateLogicalOrdinals().Where(i => { map.ReplayAssignmentRound1Root(Root(i)); return map.MatchAssignmentProgram(program); }).ToArray();
                        Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "six-context five-type Round1 projected sparse ABI1/long-short table/poison-tail parity");
                        if (program.StateCount == 432 && actual.Count > 0) widePositive = true;
                    }
                }
            }
            Check(widePositive, "actual GPU 432-state candidates reach accepting word");
            Check(gpu.RetainedBufferBytes == bufferBytes, "shader word growth and program changes retain buffer payload");
            // One full window plus one root verifies persistent reuse and chunk
            // boundaries beyond the configured batch without a long matrix.
            var simple = new MapRouteSequenceQuery([MapPointType.Monster]);
            MapLossyPlan.TryProgram(simple, 12, out var simpleProgram, out _);
            var p = new MapLossyPredicate(1, simpleProgram, "DenseBoundary");
            var dense = FamilyCandidateSet.Dense(batch);
            var denseResult = await execution.ExecuteGpuAsync(_ => gpu.Propose(dense, p, 1, CancellationToken.None, poisonBitmap: true), CancellationToken.None);
            var denseMap = new BoundedActMap(StandardMapFamily.Context(1, 1));
            var denseExpected = dense.EnumerateLogicalOrdinals().Where(i => { denseMap.ReplayAssignmentRound1Root(Root(i)); return denseMap.MatchAssignmentProgram(simpleProgram); }).ToArray();
            Check(denseResult.EnumerateLogicalOrdinals().SequenceEqual(denseExpected), "Dense 16384+1 boundary parity");
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
            try { await execution.ExecuteGpuAsync(_ => gpu.Propose(dense, p, 1, cancel.Token), cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cancelled call has no output");
            await CheckPropertyPlansAsync(basis, gpu, execution, batch, Check);
            // Actual selected Family includes complete M after the GPU proposer.
            var route = new StandardMapRouteCondition(1, [new StandardMapRouteGroup([StandardMapRoomType.Monster, StandardMapRoomType.Unknown, StandardMapRoomType.Shop])]);
            var q = SearchQuery.Empty with { StandardMapRoutes = [route], StandardMaps =
                Enum.GetValues<StandardMapMetric>().Select(metric => new StandardMapSearchCondition(1, metric,
                    StandardMapComparison.AtLeast, 0)).Concat(new[] {
                        new StandardMapSearchCondition(1, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtMost, 20),
                        new StandardMapSearchCondition(0, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 0, true)
                    }).ToArray() };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, basis.CompiledSearch.Context),
                basis.RunOptions with { MapLossy = new(true, 16384) }).Plan!;
            var selected = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: true);
            var family = selected.ExecutionOrderUnits.OfType<StandardMapFamily>().Single();
            try
            {
                Check(family.ConditionPerformance.UsesGpu, "actual M GPU selected");
                var input = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, 33).Select(i => (ulong)(7 + i * 3)).ToArray());
                var actual = await family.InvokeAsync(execution, new(batch, request), input, CancellationToken.None);
                var expected = input.EnumerateLogicalOrdinals().Where(i => family.MatchesLossyRoot(Root(i)) && family.MatchesRoot(Root(i))).ToArray();
                Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "GPU candidate -> FullFinal ordered ABI1");
                Check(actual.Count > 0, "new Monster/Unknown/Shop family has positive candidate witnesses");
                foreach (ulong ordinal in actual.EnumerateLogicalOrdinals())
                    Check(StandardMapExactEvaluator.Evaluate(request, VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, batch.GlobalCandidate(ordinal)))
                        .Disposition == SearchDisposition.Match, "GPU survivors independently satisfy all original properties and drawing");
            }
            finally { await family.DisposeAsync(execution); }
            File.WriteAllText(Path.Combine(output, "map-lossy-gpu.json"), JsonSerializer.Serialize(new { checks, gpu.Dispatches,
                gpu.RetainedBufferBytes, Meaning = "Bounded same-input GPU/CPU candidate parity; Final verified separately; no lossless recall or throughput claim" }));
        }
        finally
        {
            if (gpu is not null) await execution.ExecuteGpuAsync(_ => { gpu.Dispose(); return true; }, CancellationToken.None);
        }
    }

    private static async Task CheckPropertyPlansAsync(ExactSearchExecutionRequest basis, MapLossyGpuExecutor gpu,
        FamilyExecutionContext execution, SearchBatch batch, Action<bool, string> check)
    {
        ulong Root(ulong ordinal) => Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(
            Beta111Profile.Instance, batch.GlobalCandidate(ordinal)));
        var input = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, 33).Select(i => (ulong)(7 + i * 3)).ToArray());
        int[] first = new int[27];
        for (int act = 1; act <= 3; act++)
        {
            var map = new BoundedActMap(StandardMapFamily.Context(act, basis.Ascension));
            map.ReplayAssignmentRound1Root(Root(7));
            for (int metric = 0; metric < 9; metric++) first[(act - 1) * 9 + metric] = map.EvaluateAssignmentProperty(metric);
        }
        var equality = Enum.GetValues<StandardMapMetric>().SelectMany(metric => new[] {
            new StandardMapSearchCondition(1, metric, StandardMapComparison.AtLeast, first[(int)metric]),
            new StandardMapSearchCondition(1, metric, StandardMapComparison.AtMost, first[(int)metric]) }).ToArray();
        var mixed = new StandardMapRouteCondition(1, [new StandardMapRouteGroup([
            StandardMapRoomType.Monster, StandardMapRoomType.Unknown, StandardMapRoomType.Shop])]);
        var propertyOnly = SearchQuery.Empty with { StandardMaps = equality };
        var combined = propertyOnly with { StandardMapRoutes = [mixed] };
        var aggregate = SearchQuery.Empty with { StandardMaps = [
            new(0, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, first[5] + first[14] + first[23], true),
            new(3, StandardMapMetric.GuaranteedRest, StandardMapComparison.AtMost, 20) ], StandardMapRoutes = [mixed] };
        StandardMapRouteCondition Wide(int act)
        {
            int count = Beta111StandardMapGenerator.GetNumberOfRooms(StandardMapFamily.Context(act, basis.Ascension).Act, false) - 3;
            var rooms = Enum.GetValues<StandardMapRoomType>();
            return new(act, [new StandardMapRouteGroup(Enumerable.Range(0, count).Select(i => rooms[i % rooms.Length]).ToArray())]);
        }
        var largeTables = SearchQuery.Empty with { StandardMapRoutes = [Wide(1), Wide(2)] };
        var wideThenNarrow = SearchQuery.Empty with { StandardMapRoutes = [
            new(1, [new StandardMapRouteGroup([
                StandardMapRoomType.Monster, StandardMapRoomType.Monster, StandardMapRoomType.Monster,
                StandardMapRoomType.Unknown, StandardMapRoomType.Unknown, StandardMapRoomType.Unknown,
                StandardMapRoomType.Elite, StandardMapRoomType.Elite, StandardMapRoomType.RestSite,
                StandardMapRoomType.RestSite, StandardMapRoomType.Shop, StandardMapRoomType.Shop])]),
            new(2, [new StandardMapRouteGroup([StandardMapRoomType.Monster])])] };
        long grown = 0;
        foreach (var (query, name) in new[] { (propertyOnly, "property-only-nine-equalities"), (combined, "properties-and-drawing"),
            (aggregate, "same-seed-three-act-sum-and-drawing"), (largeTables, "multiple-drawing-tables"),
            (wideThenNarrow, "wide-then-narrow-drawings"),
            (propertyOnly, "shorter-plan-after-growth"), (SearchQuery.Empty with { StandardMaps = [
                new(2, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtMost, 1)] }, "zero-extra-rest") })
        {
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, basis.CompiledSearch.Context),
                basis.RunOptions with { MapLossy = new(true) }).Plan!;
            var plan = MapLossyPlan.Create(request);
            var cpu = new StandardMapFamily(request);
            try
            {
                if (name == "multiple-drawing-tables") check(plan.Predicates.Sum(p => p.Program.StateCount) > 512,
                    "several independently valid drawing tables exceed the old single-table capacity");
                if (name == "wide-then-narrow-drawings") check(plan.Predicates.Select(p => p.Program.StateCount).SequenceEqual(new[] { 432, 2 }),
                    "one invocation evaluates a 14-word drawing followed by a 1-word drawing");
                foreach (int length in new[] { 33, 1 })
                {
                    var slice = FamilyCandidateSet.FromAbi1(batch, input.EnumerateLogicalOrdinals().Take(length).ToArray());
                    var expected = slice.EnumerateLogicalOrdinals().Where(i => cpu.MatchesLossyRoot(Root(i))).ToArray();
                    if (name == "wide-then-narrow-drawings" && length == 33) check(expected.Length > 0,
                        "positive whole-plan witness forces both wide and narrow drawings to execute");
                    var actual = await execution.ExecuteGpuAsync(_ => gpu.Propose(slice, plan, request.Ascension,
                        CancellationToken.None, poisonBitmap: true), CancellationToken.None);
                    check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "whole-plan CPU/GPU property/drawing AND sparse/tail parity: " + name);
                    if (name == "property-only-nine-equalities") check(actual.Count > 0, "all nine properties have an actual positive combined candidate");
                }
                if (name == "multiple-drawing-tables") grown = gpu.RetainedBufferBytes;
                if (name is "shorter-plan-after-growth" or "zero-extra-rest") check(gpu.RetainedBufferBytes == grown,
                    "short property plans reuse the largest observed config/transition buffers");
            }
            finally { await cpu.DisposeAsync(execution); }
        }
    }
}
