using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class SmallCandidateExactChecks
{
    private static void CheckAdaptiveGpuRootWindow()
    {
        const int small = 1 << 24;
        const long longRun = 1L << 36;
        var adaptive = new AdaptiveGpuRootWindow(8 * small);
        var seen = new HashSet<int>();
        for (int i = 0; i < 90; i++)
        {
            int window = adaptive.Choose(longRun, 100);
            bool first = seen.Add(window);
            // Binding is expensive on every promotion. Sustained costs improve.
            double cost = window switch { small => 30, 2 * small => 50, 4 * small => 80, _ => 120 };
            adaptive.Observe(window, first ? 3000 : cost, 0);
        }
        Check(seen.SetEquals([small, 2 * small, 4 * small, 8 * small]) &&
            adaptive.Choose(longRun, 100) == 8 * small, "adjacent promotions exclude cold and binding windows");
        for (int i = 0; i < 8; i++) adaptive.Observe(8 * small, 210, 1);
        Check(adaptive.Choose(longRun, 100) == 4 * small, "sustained delivery slowdown returns to the previous accepted window");
        adaptive.Observe(4 * small, 10000, 0);
        for (int i = 0; i < 12; i++) adaptive.Observe(4 * small, 95, 0);
        Check(adaptive.Choose(longRun, 100) == 4 * small, "new binding spike and slower delivery that still beats 32M retain 64M");
        for (int i = 0; i < 12; i++) adaptive.Observe(4 * small, 130, 0);
        Check(adaptive.Choose(longRun, 100) == 2 * small, "fresh independent 64M samples can justify the next step down");

        var refused = new AdaptiveGpuRootWindow(8 * small);
        for (int i = 0; i < 80 && refused.Choose(longRun, 100) < 8 * small; i++)
            refused.Observe(refused.Current, 64, 0);
        Check(refused.Current == 8 * small && refused.Selected == 4 * small,
            "128M trial follows accepted 16M, 32M and 64M");
        foreach (int fallback in new[] { 4 * small, 2 * small, small })
        {
            refused.Refuse();
            for (int i = 0; i < 20; i++)
            {
                Check(refused.Choose(longRun, 100) == fallback && refused.Selected == fallback &&
                    refused.State == "BindingRefused", "storage fallback retains the last proven smaller window");
                refused.Observe(fallback, 64, 0);
            }
        }

        foreach (string mode in new[] { "short", "target", "tiny-samples", "partial", "tie", "cap" })
        {
            var trial = new AdaptiveGpuRootWindow(mode == "cap" ? 2 * small : 8 * small);
            int largest = 0;
            for (int i = 0; i < 100; i++)
            {
                int window = trial.Choose(mode == "short" ? 5L * small : longRun, mode == "target" ? 3 : 1000);
                largest = Math.Max(window, largest);
                double ms = mode == "tiny-samples" ? 0.01 :
                    window == small ? 30 : mode == "tie" ? 59 : 40;
                trial.Observe(mode == "partial" ? window - 1 : window, ms, mode == "target" ? 1 : 0);
            }
            int final = trial.Choose(longRun, 1000);
            Check(mode switch
            {
                "tie" => largest == 2 * small && final == small,
                "cap" => largest == 2 * small && final == largest,
                _ => largest == small && final == small
            }, "adaptive window samples and bounds: " + mode);
            trial.Refuse();
            Check(trial.Choose(longRun, 1000) == small, "storage refusal locks the ordinary window");
        }
    }

    private static void CheckPublicGpuPlanCoverage()
    {
        string[] ids = [VariantOpeningFamily.Id, "N.Neow", WorldFamily.Id];
        var bodies = ids.Select(id => new FilterBodyDescription(
            new PreparedFilter(id) { Coverage = [id, id + ".detail"], Remainders = [id + ".remaining"] },
            id, RuntimeProfileId.Beta111)
        {
            UsesGpu = true, SupportsStandaloneLargeWindow = true,
            LargeWindowRole = GpuLargeWindowRole.Dense | GpuLargeWindowRole.Compact
        }).ToArray();
        var selected = FamilyPlanner.InOrder(bodies) with { FilterRemainders = ["party-shared", "exact-only"] };
        var automatic = FamilyExecutionCoordinator.WithGpuRootBatchSize(selected, 0);
        var large = FamilyExecutionCoordinator.WithGpuRootBatchSize(selected, 1 << 26);
        Check(ReferenceEquals(selected, automatic) && large.Stages[0].Family.WindowCapacity == 1 << 25 &&
            large.Stages[0].Family.WindowCapacity != automatic.Stages[0].Family.WindowCapacity,
            "manual public window promotes the first descriptor of the three-stage fixture");
        Check(large.OrderedFamilies.Select(body => body.FamilyId).SequenceEqual(ids) &&
            large.Stages.Skip(1).SequenceEqual(selected.Stages.Skip(1)) &&
            large.Stages.Zip(selected.Stages).All(pair =>
                pair.First.Coverage.SequenceEqual(pair.Second.Coverage) &&
                pair.First.Family.Coverage.SequenceEqual(pair.Second.Family.Coverage) &&
                pair.First.Family.Preparation.Remainders.SequenceEqual(pair.Second.Family.Preparation.Remainders)) &&
            large.FilterRemainders.SequenceEqual(selected.FilterRemainders),
            "manual public V/N/W root window retains every stage, its coverage and remaining obligations");

        var cpu = bodies[2] with { UsesGpu = false, PreferredRootWindowSize = 65536 };
        var mixed = FamilyPlanner.InOrder([bodies[0], cpu]);
        var expanded = FamilyExecutionCoordinator.WithGpuRootBatchSize(mixed, 1 << 25);
        Check(expanded.Stages.Count == 2 && expanded.Stages[0].Family.WindowCapacity == 1 << 25 &&
            ReferenceEquals(expanded.Stages[1].Family, cpu),
            "ordinary CPU suffix retains its own chunk preference alongside the enlarged GPU entry");
        var bounded = FamilyPlanner.InOrder([bodies[0], cpu with { MaximumRootWindowSize = 65536 }]);
        Check(ReferenceEquals(FamilyExecutionCoordinator.WithGpuRootBatchSize(bounded, 1 << 25), bounded) &&
            bounded.Stages[1].Family.MaximumRootWindowSize == 65536,
            "a CPU suffix with a hard root limit keeps its original bounded plan");
    }

    private static async Task CheckPublicGpuTailAsync(PreparedSearch template)
    {
        CheckAdaptiveGpuRootWindow();
        CheckPublicGpuPlanCoverage();
        await CheckPartyPublicTailAsync(template);
        const int window = PrivateOrdinalBuffer.Capacity;
        const ulong start = 37;
        var wide = new SearchBatch(start, 3 * window + 4);
        ulong[] ordinals = [0, window - 1, window, 3 * window, 3 * window + 3];
        var visited = new List<SearchBatch>();
        var echoBody = PublicStage("echo", (_, input, services) =>
        {
            Check(services.BudgetPublicTailBindings && !input.IsDense, "tail budgets its sparse binding");
            visited.Add(input.Batch);
            return input;
        });
        await using (var services = new FilterRuntimeServices(requiresGpu: false))
        {
            var echo = echoBody.CreateExecution!(echoBody, services);
            var result = await GpuRootWindow.FilterPublicTailAsync(echo, services,
                FamilyCandidateSet.FromSortedAbi1(wide, ordinals), CancellationToken.None);
            Check(result.Batch == wide && result.ExportAbi1().Span.SequenceEqual(ordinals) &&
                visited.SequenceEqual([new(start, window), new(start + window, window), new(start + 3 * window, 4)]) &&
                !services.BudgetPublicTailBindings, "sparse tail restores ordered root offsets and skips the empty third window");
            await echo.DisposeAsync(services);
        }

        foreach (bool failCleanup in new[] { false, true })
        {
            var calls = new List<(string Stage, int Generation, SearchBatch Batch)>();
            var lifecycle = new List<string>();
            var first = PublicStage("producer", (generation, input, _) =>
            {
                calls.Add(("producer", generation, input.Batch));
                return FamilyCandidateSet.FromSortedAbi1(input.Batch, [0]);
            }, lifecycle, failCleanup);
            var second = PublicStage("suffix", (generation, input, services) =>
            {
                calls.Add(("suffix", generation, input.Batch));
                if (generation == 1 && services.BudgetPublicTailBindings)
                    throw new GpuRootWindowBindingRefused("suffix", new GpuStorageAllocationRefused("injected"));
                return input;
            }, lifecycle, failCleanup);
            var request = SearchPreparationCompiler.Compile(template.Model.CompiledSearch, template.RunOptions with
            { StartSeed = Beta110SeedCodec.FormatOrdinal(start), ScanCount = 2 * window, GpuRootBatchSize = 2 * window }).Plan!;
            var plan = FamilyPlanner.InOrder([first, second]) with { AllowSmallCandidateExact = false };
            await using var session = new ProductionSearchSession(request, plan,
                FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(request, plan), plan),
                2 * window, 16, null, "", executionContextFactory: () => new FilterRuntimeServices(requiresGpu: false));
            await session.Completion.WaitAsync(PairTimeout);
            var progress = session.GetProgress();
            string[] initial = ["create producer1", "create suffix1", "release suffix1", "release producer1"];
            Check(lifecycle.Take(4).SequenceEqual(initial) && calls[0] == ("producer", 1, new SearchBatch(start, 2 * window)) &&
                calls[1] == ("suffix", 1, new SearchBatch(start, window)), "suffix refusal releases both slots before any retry");
            var hits = Drain(session);
            if (failCleanup)
            {
                Check(lifecycle.Count == 4 && calls.Count == 2 && progress.State == SearchRunState.Faulted &&
                    progress.ScannedCount == 0 && hits.Length == 0 && session.CaptureContinuation()!.NextOrdinal == start &&
                    progress.FailureCode.Contains("PublicGpuWindow.ReleaseFailed"), "both cleanup attempts run and prevent rebind or cursor advancement");
                continue;
            }
            Check(lifecycle.Skip(4).Take(2).SequenceEqual(["create producer2", "create suffix2"]) &&
                calls.Skip(2).SequenceEqual(new (string, int, SearchBatch)[] {
                    ("producer", 2, new(start, window)), ("suffix", 2, new(start, window)),
                    ("producer", 2, new(start + window, window)), ("suffix", 2, new(start + window, window)) }) &&
                progress.State == SearchRunState.Completed && progress.ActiveGpuRootBatchSize == window &&
                progress.ScannedCount == 2 * window && hits.Select(hit => hit.Seed).Order().SequenceEqual(
                    new[] { Beta110SeedCodec.FormatOrdinal(start), Beta110SeedCodec.FormatOrdinal(start + window) }.Order()),
                "both GPU stages rebuild at the same frontier and finish in ordinary windows");
            foreach (var hit in hits) session.AcknowledgeCandidate(hit.Seed);
            Check(session.CaptureContinuation()!.NextOrdinal == start + 2 * window, "retried public suffix commits only after ACK");
        }
    }

    private static FilterBodyDescription PublicStage(string id,
        Func<int, FamilyCandidateSet, FilterRuntimeServices, FamilyCandidateSet> match,
        List<string>? events = null, bool failCleanup = false)
    {
        int generation = 0;
        return new(new PreparedFilter(id), id, RuntimeProfileId.Beta111)
        {
            UsesGpu = true, WindowCapacity = 1 << 25, MaximumGpuRootWindow = 1 << 25,
            CreateExecution = (body, _) =>
            {
                events?.Add("create " + id + ++generation);
                return new PublicGpuFixture(body, generation, match, events, failCleanup);
            }
        };
    }

    private sealed class PublicGpuFixture(FilterBodyDescription body, int generation,
        Func<int, FamilyCandidateSet, FilterRuntimeServices, FamilyCandidateSet> match,
        List<string>? events, bool failCleanup) : IFilterExecution
    {
        public FilterBodyDescription Body => body;
        public bool ExecutionFaulted => false;
        public ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
            => ValueTask.FromResult(match(generation, input, services));
        public ValueTask DisposeAsync(FilterRuntimeServices services)
        {
            events?.Add("release " + body.FamilyId + generation);
            if (failCleanup) throw new IOException("release failure " + body.FamilyId);
            return ValueTask.CompletedTask;
        }
    }
}
