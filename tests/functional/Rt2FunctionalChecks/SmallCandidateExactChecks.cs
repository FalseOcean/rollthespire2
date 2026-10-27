using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class SmallCandidateExactChecks
{
    private static int _checks;
    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException("SmallCandidateExact: " + reason);
        _checks++;
    }
    internal static int Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<int> RunAsync()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            Path.Combine(Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/v1/context.json")).Context;
        ExactSearchExecutionRequest Compile(SearchQuery query, int count, bool skip = false)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                new("000000000000", count, 100, 2, SkipExactValidation: skip));
            Check(result.Success && result.Plan is not null, "captured-context request compiles");
            return result.Plan!;
        }
        var plan = Compile(SearchQuery.Empty, 64);
        foreach (int count in new[] { 0, 1, 8, 9 })
        {
            var first = new FixtureFamily("prefix", count);
            var second = new FixtureFamily("tail", count == 9 ? 8 : count);
            var last = new FixtureFamily("last", 0);
            var policy = new SmallCandidateExactPolicy();
            await using var execution = new FamilyExecutionContext(requiresGpu: false, cpuWorkerCount: 2);
            var actual = await LinearFamilyPipeline.FilterAsync(execution,
                new(new SearchBatch(0, 16), plan), [first, second, last], policy, CancellationToken.None);
            Check(first.Calls == 1 && second.Calls == (count == 9 ? 1 : 0) && last.Calls == 0,
                "0/1/8 stop; 9 runs the next filter before stopping at 8");
            Check(actual.EnumerateLogicalOrdinals().SequenceEqual(Enumerable.Range(0, Math.Min(count, 8)).Select(i => (ulong)i)),
                "handoff preserves the ordered original logical ordinals");
            Check(policy.GetSummary().Contains(count == 0 ? "zeroCandidateTailBatches=1" : "smallCandidateExactBatches=1"),
                "summary records the actual early boundary");
        }
        {
            var first = new FixtureFamily("prefix", 1);
            var tail = new FixtureFamily("tail", 0);
            await using var execution = new FamilyExecutionContext(requiresGpu: false, cpuWorkerCount: 2);
            var actual = await LinearFamilyPipeline.FilterAsync(execution, new(new SearchBatch(0, 16), plan),
                [first, tail], CancellationToken.None);
            Check(tail.Calls == 1 && actual.Count == 0, "existing explicit pipeline without policy still runs the tail");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            bool cancelled = false;
            try { await LinearFamilyPipeline.FilterAsync(execution, new(new SearchBatch(0, 16), plan),
                [first], new SmallCandidateExactPolicy(), cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && first.Calls == 1, "cancelled work never starts another filter");
        }

        var query = LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with
        { NeowRelics = new([BaseGameModelKeys.Relics.GoldenPearl], [], []) });
        var actualPlan = Compile(query, 512);
        ulong? match = null, reject = null;
        for (ulong ordinal = 0; ordinal < 512 && (match is null || reject is null); ordinal++)
        {
            var exact = ProductionExactSearchEvaluator.Evaluate(actualPlan,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(ordinal)));
            if (exact.IsMatch) match ??= ordinal;
            else if (exact.Evaluation.Disposition == SearchDisposition.NoMatch) reject ??= ordinal;
        }
        Check(match.HasValue && reject.HasValue, "real Production Exact fixture includes both Match and NoMatch");
        ulong[] selected = new[] { match!.Value, reject!.Value }.Order().ToArray();
        var prefix = new FixtureFamily("prefix", selected.Length, selected);
        var forbiddenTail = new FixtureFamily("not-run", 0);
        var executionPlan = FamilyPlanner.InOrder([prefix, forbiddenTail]) with { AllowSmallCandidateExact = true };
        await using (var session = Start(actualPlan, executionPlan, 512))
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            var outputs = Drain(session);
            Check(outputs.Length == 1 && outputs[0].Seed == Beta110SeedCodec.FormatOrdinal(match.Value) && !outputs[0].IsUnverified,
                "early handoff validates every original condition and publishes only the real Match");
            Check(prefix.Calls == 1 && forbiddenTail.Calls == 0 && session.GetProgress().ScannedCount == 512,
                "full batch accounting survives an unexecuted fast-filter tail");
            session.AcknowledgeCandidate(outputs[0].Seed);
            Check(session.CaptureContinuation()!.NextOrdinal == 512, "acknowledged early result commits its actual batch frontier");
        }

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelPrefix = new FixtureFamily("prefix", 1);
        var cancelTail = new FixtureFamily("tail", 0);
        var cancelExecution = FamilyPlanner.InOrder([cancelPrefix, cancelTail]) with { AllowSmallCandidateExact = true };
        SearchContinuationSnapshot continuation;
        await using (var session = Start(plan, cancelExecution, 16, beforePublish: async (_, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }))
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            session.Cancel();
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            continuation = session.CaptureContinuation()!;
            Check(continuation.NextOrdinal == 0 && session.GetProgress().State == SearchRunState.Cancelled,
                "cancellation before delivery retains the unreceived candidate frontier");
        }
        var resumeTail = new FixtureFamily("tail", 0);
        var resumeExecution = FamilyPlanner.InOrder([new FixtureFamily("prefix", 1), resumeTail]) with { AllowSmallCandidateExact = true };
        await using (var resumed = Start(plan, resumeExecution, 16, continuation))
        {
            await resumed.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            var outputs = Drain(resumed);
            Check(outputs.Select(c => c.Seed).SequenceEqual(new ulong[] { 0, 16, 32, 48 }.Select(Beta110SeedCodec.FormatOrdinal)) &&
                outputs.All(c => !c.IsUnverified) && resumeTail.Calls == 0, "resume delivers all four original Exact results once");
            foreach (var candidate in outputs) resumed.AcknowledgeCandidate(candidate.Seed);
            Check(resumed.CaptureContinuation()!.NextOrdinal == 64, "resumed ACK frontier reaches the full range");
        }
        var skipped = Compile(SearchQuery.Empty, 16, skip: true);
        var skippedTail = new FixtureFamily("tail", 0);
        var skippedExecution = FamilyPlanner.InOrder([new FixtureFamily("prefix", 1), skippedTail]) with { AllowSmallCandidateExact = true };
        await using (var session = Start(skipped, skippedExecution, 16))
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Check(skippedTail.Calls == 1 && Drain(session).Length == 0, "SkipExact cannot bypass a selected tail or secretly run Exact");
        }
        // Real complete-request Exact, including the whole party and map domain.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        foreach (int players in new[] { 1, 2, 4 })
        {
            var source = players == 1 ? context : (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
                .Invoke(null, [context, players, null, null])!;
            const string knownSeed = "000000000000";
            var knownOffers = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(knownSeed,
                source.Authority, Beta111Profile.Instance, true).RelicKeys;
            var baseQuery = players == 1
                ? LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with { NeowRelics = new([], knownOffers, []) })
                : SearchQuery.Empty with { Players = source.Party!.Players.Select(owner => new PlayerOfferQuery(owner.PlayerSlotIndex,
                    new([], RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(knownSeed, owner,
                        Beta111Profile.Instance, true).RelicKeys, []))).ToArray() };
            foreach (bool map in new[] { false, true })
            {
                var authored = map ? baseQuery with { StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 1)] } : baseQuery;
                ExactSearchExecutionRequest Request(bool skip = false) => ExactSearchExecutionRequestFactory.Compile(
                    SearchCompiler.Compile(authored, source), new(knownSeed, 64, 100, 2, SkipExactValidation: skip)).Plan
                    ?? throw new InvalidOperationException("SmallCandidateExact.VariantCompile");
                var request = Request();
                var positive = ProductionExactSearchEvaluator.Evaluate(request,
                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, knownSeed));
                Check(positive.IsMatch, $"authored known-positive whole request players={players};map={map}");
                ulong? mismatch = null;
                for (ulong ordinal = 1; ordinal < 64 && mismatch is null; ordinal++)
                {
                    var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(
                        Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(ordinal)));
                    if (exact.Evaluation.Disposition == SearchDisposition.NoMatch) mismatch = ordinal;
                }
                Check(mismatch.HasValue, "whole request includes a demonstrated Exact reject");
                var gate = new FixtureFamily("E.EventResult", 2, [0, mismatch!.Value]);
                var innerTails = new List<FixtureFamily>();
                IFamilyInvocation selectedGate = gate;
                if (players > 1)
                {
                    var children = request.CompiledSearch.PlayerSearches.Take(players).Select((child, index) =>
                    {
                        var family = index == 0 ? gate : new FixtureFamily("E.EventResult", 0);
                        if (index > 0) innerTails.Add(family);
                        return (ExactSearchExecutionRequestFactory.ForPlayer(request, child), Family: (IFamilyInvocation)family);
                    }).ToArray();
                    selectedGate = new PartyFamily(request, children);
                }
                var outerTail = new FixtureFamily("outer-tail", 0);
                var execution = FamilyPlanner.InOrder([selectedGate, outerTail]) with { AllowSmallCandidateExact = true };
                await using (var session = Start(request, execution, 64))
                {
                    await session.Completion.WaitAsync(TimeSpan.FromSeconds(20));
                    var outputs = Drain(session);
                    Check(outputs.Length == 1 && outputs[0].Seed == knownSeed && !outputs[0].IsUnverified &&
                        innerTails.All(tail => tail.Calls == 0) && outerTail.Calls == 0,
                        "public party inner and outer handoffs retain every original predicate for whole-table Exact");
                    session.AcknowledgeCandidate(knownSeed);
                    Check(session.CaptureContinuation()!.NextOrdinal == 64, "whole-party/map ACK commits the trustworthy full batch frontier");
                }
                foreach (int population in new[] { 0, 1, 8, 9 })
                {
                    var first = new FixtureFamily("prefix", population);
                    var tail = new FixtureFamily("tail", 0);
                    var executionPlanVariant = FamilyPlanner.InOrder([first, tail]) with { AllowSmallCandidateExact = true };
                    await using var session = Start(request, executionPlanVariant, 64);
                    await session.Completion.WaitAsync(TimeSpan.FromSeconds(20));
                    Check(first.Calls == 1 && tail.Calls == (population == 9 ? 1 : 0),
                        $"real session boundary 0/1/8/9 players={players};map={map};population={population}");
                }
                var skippedTailVariant = new FixtureFamily("tail", 0);
                var skippedPlanVariant = FamilyPlanner.InOrder([new FixtureFamily("prefix", 1), skippedTailVariant]) with { AllowSmallCandidateExact = true };
                await using (var session = Start(Request(skip: true), skippedPlanVariant, 64))
                {
                    await session.Completion.WaitAsync(TimeSpan.FromSeconds(20));
                    Check(skippedTailVariant.Calls == 1 && Drain(session).Length == 0,
                        $"SkipExact preserves selected filtering players={players};map={map}");
                }
            }
        }
        await CheckDenseBatchGroupingAsync(plan);
        Console.WriteLine($"SMALL_CANDIDATE_EXACT_PASS checks={_checks};realMatchAndReject=true;cancelResume=true;skipExactPreserved=true;partyAndMap=true");
        return 0;
    }
    private static ProductionSearchSession Start(ExactSearchExecutionRequest request, FamilyExecutionPlan execution,
        int window, SearchContinuationSnapshot? continuation = null,
        Func<int, CancellationToken, ValueTask>? beforePublish = null) =>
        new(request, execution, FamilySearchEtaProjectorV1.Project(request, execution), window, 100, null, "",
            beforeCandidatePublish: beforePublish, continuation: continuation);
    private static SearchCandidate[] Drain(ProductionSearchSession session)
    {
        var result = new List<SearchCandidate>();
        while (session.TryReadCandidate(out var candidate)) result.Add(candidate!);
        return result.ToArray();
    }
    private sealed class FixtureFamily(string id, int count, ulong[]? selected = null) : IFamilyInvocation
    {
        internal int Calls;
        public string FamilyId => id;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(id, 512, [], "TestOnly");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Resolved(id, .5, "TestOnly");
        public FamilyConditionPerformanceProjection ConditionPerformance => new(id, id, "TestOnly", 1, "TestOnly", usesGpu: false);
        public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, "", 0, 0, 0, Calls,
            0, 0, 0, 0, false, "TestOnly");
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
            FamilyCandidateSet input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return ValueTask.FromResult(FamilyCandidateSet.FromAbi1(input.Batch,
                selected ?? input.EnumerateLogicalOrdinals().Take(count).ToArray()));
        }
    }
}
