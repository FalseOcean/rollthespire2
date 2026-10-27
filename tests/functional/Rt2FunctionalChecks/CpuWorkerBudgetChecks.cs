using System.Text.Json;
using System.Buffers.Binary;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static class CpuWorkerBudgetChecks
{
    private static int _checks;
    private static readonly int[] Workers = [1, 3, 7, 31];
    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException("CPU worker budget: " + reason);
        _checks++;
    }

    internal static int Run()
    {
        _checks = 0;
        CheckPackedSeeds();
        Check(FamilyExecutionCoordinator.MaximumCpuSearchWorkers(32)==31, "32 logical processors reserve one");
        Check(FamilyExecutionCoordinator.MaximumCpuSearchWorkers(1)==1, "single logical processor retains usable execution");
        Check(FamilyExecutionCoordinator.MaximumCpuSearchWorkers(64)==32, "current execution surface stays within 32 workers");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        // Bounded representative populations, including formerly P1-only local bodies.
        foreach (string path in new[] {
            "v1/cases/N-entry.json", "family-cost-closure-v1/NN-NewLeaf.json",
            "v1/cases/S-three-shops.json", "family-cost-closure-v1/S-U3.json",
            "family-pricing-matrix-v1/R-three-common.json", "cpu-main-v1/C-three-potions.json",
            "family-cost-closure-v1/W-boss-2.json", "v1/cases/A-pael.json",
            "cpu-numerical-closure-v1/E-Color.json", "family-cost-closure-v1/E-Dive.json" })
        {
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path);
            CheckFamilyPopulation(context, workload.Query, path, 8193);
        }
        CheckFamilyPopulation(context, SearchQuery.Empty with {
            StandardMaps = [new(1, StandardMapMetric.GuaranteedElite, StandardMapComparison.AtLeast, 2)]
        }, "M.StandardMap", 47);

        using var captured = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var targets = captured.RootElement.GetProperty("Evidence")[0].GetProperty("Targets")
            .Deserialize<MorphicGroveTarget[]>(ProductionSearchReplay.JsonOptions)!
            .Select((t, i) => t with { InstanceId = "basic:" + i }).ToArray();
        var scenario = new MorphicGroveScenario(context.Authority,
            new("Existing authored initial-Basic fixture; occurrence not proven", true, false, null), targets);
        CheckFamilyPopulation(context, SearchQuery.Empty with {
            TransformationAggregate = new(TransformationOpening.None, TransformationPickupOrder.Any,
                true, true, true, TransformationAggregatePredicate.RareCountAtLeast, 2, []) { EventScenario = scenario }
        }, "T.TransformationAggregate", 8193);
        CheckPartyPopulation(context);
        var request = Compile(context, SearchQuery.Empty, 7);
        CheckPackedExecution(request);
        CheckWindowAndTail(request);
        CheckSharedBudget(request).GetAwaiter().GetResult();
        Console.WriteLine($"CPU_WORKER_BUDGET_PASS checks={_checks};workers=1,3,7,31;completeAbi1=true;denseCompact=true;splitTail=true;sharedFamilyExactPermits=true;cancelReuse=true;gpu=false");
        return 0;
    }

    private static void CheckPackedSeeds()
    {
        var starts = new HashSet<ulong> { 0, 8, 16, 21, 31, (1UL << 32) - 3,
            (1UL << 53) + 7, Beta110SeedCodec.SpaceSize - 70 };
        ulong power = 1;
        for (int digit = 1; digit < 12; digit++)
        {
            power *= 34;
            starts.Add(power - 3);
        }
        // Deterministic non-contiguous full-width ordinals exercise Compact's
        // independent decode without deriving an oracle from packed state.
        ulong random = 0x123456789abcdef0UL;
        for (int i = 0; i < 128; i++)
        {
            random = unchecked(random * 6364136223846793005UL + 1442695040888963407UL);
            starts.Add(random % (Beta110SeedCodec.SpaceSize - 70));
        }
        Span<byte> oracle = stackalloc byte[12];
        Span<byte> bytes = stackalloc byte[12];
        foreach (ulong start in starts)
        {
            var seed = CpuPackedSeed12.FromOrdinal(start);
            Beta110SeedCodec.WriteOrdinal(start, oracle);
            for (int offset = 0; offset < 70; offset++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, seed.First8);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], seed.Tail4);
                Check(bytes.SequenceEqual(oracle), "packed preserves all twelve visible bytes and radix34 carry");
                Check(seed.Hash() == XxHash64.Hash(oracle, 0), "packed hash matches general xxHash64 seed0");
                if (offset + 1 < 70)
                    Check(seed.Advance() == Beta110SeedCodec.Advance(oracle, 1), "packed carry agrees with byte codec");
            }
        }
        var last = CpuPackedSeed12.FromOrdinal(Beta110SeedCodec.SpaceSize - 1);
        Check(!last.Advance(), "packed end-of-space carry reports overflow");
        Check(last.First8 == 0x3030303030303030UL && last.Tail4 == 0x30303030U,
            "packed overflow preserves byte codec wrap representation");
        bool rejected = false;
        try { CpuPackedSeed12.FromOrdinal(Beta110SeedCodec.SpaceSize); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "packed ordinal range matches codec");
    }

    private static void CheckPackedExecution(ExactSearchExecutionRequest request)
    {
        foreach (int count in new[] { 1, 2, 3, 4, 5, 7, 17, 1025 })
        foreach (ulong start in new[] { 34UL * 34 * 34 * 34 - 2, Beta110SeedCodec.SpaceSize - (ulong)count })
        foreach (bool compact in new[] { false, true })
        {
            var batch = new SearchBatch(start, count);
            var input = compact ? FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, count)
                .Where(i => i % 5 is 0 or 3 or 4).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
            var expected = new FamilyCpuExecution(request, "Check", "PackedOracle")
                .Execute(new(batch, request), input, default, root => root % 5 < 3).ExportAbi1().ToArray();
            foreach (int workers in new[] { 1, 3, 7 })
            foreach (bool packed in new[] { false, true })
            {
                var cpu = new FamilyCpuExecution(request, "Check", "PackedTail", workers, numericalRoots: true,
                    packedSeedRoots: packed);
                Check(cpu.Condition(compact).PhysicalImplementationRevision.Contains(packed ? ".RootMode3." : ".RootMode2.", StringComparison.Ordinal),
                    "explicit root implementation has its own physical identity");
                var actual = cpu.Execute(new(batch, request), input, default, root => root % 5 < 3).ExportAbi1();
                Check(actual.Span.SequenceEqual(expected), "byte/packed dense/irregular compact/short tail/high-bit complete ordered ABI1");
            }
        }
    }

    private static ExactSearchExecutionRequest Compile(SearchContext context, SearchQuery query, int workers)
    {
        var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
            new("000000000000", 65539, int.MaxValue, workers));
        Check(compiled.Success, "compile: " + compiled.Issue);
        return compiled.Plan!;
    }

    private static FamilyCandidateSet Input(SearchBatch batch, bool compact) => compact
        ? FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, batch.BatchCandidateCount)
            .Where(i => i % 3 != 1).Select(i => (ulong)i).ToArray())
        : FamilyCandidateSet.Dense(batch);

    private static string Body(FamilyCpuExecution cpu) => cpu.Condition(false).PhysicalImplementationRevision.Split(".RootMode", StringSplitOptions.None)[0];

    private static void CheckFamilyPopulation(SearchContext source, SearchQuery query, string name, int count)
    {
        var batch = new SearchBatch(0, count);
        var expected = new Dictionary<(string Family, bool Compact), ulong[]>();
        var bodies = new Dictionary<string, string>();
        foreach (int workers in Workers)
        {
            var request = Compile(source, query, workers);
            var plan = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false);
            Check(plan.OrderedFamilies.Count > 0, name + " has a CPU filter");
            var context = new FamilyExecutionContext(false, failOnPhysicalRecovery: true, cpuWorkerCount: workers);
            try
            {
                foreach (var family in plan.OrderedFamilies)
                {
                    Check(!family.ConditionPerformance.UsesGpu, name + " CPU-only selection");
                    string revision = family.ConditionPerformance.PhysicalImplementationRevision;
                    Check(revision.Contains($".P{workers}.B", StringComparison.Ordinal), name + " selected requested P" + workers + ": " + revision);
                    bool packedNeow = family is FamilyCpuExecution { PricingOwner: NeowFamily n } &&
                        NeowCpuFourWayPreGate.TryCompile(n.PricingReplayPlan) is not null;
                    Check(revision.Contains(family.FamilyId == TransformationAggregateFamily.Id || packedNeow ? ".RootMode3." : ".RootMode2.", StringComparison.Ordinal),
                        name + " only T and a proven four-way-eligible solo N shape use packed scalar roots");
                    string body = revision.Split(".RootMode", StringSplitOptions.None)[0];
                    if (workers == 1) bodies.Add(family.FamilyId, body);
                    else Check(bodies[family.FamilyId] == body, name + " preserves the selected numerical body across worker counts");
                    foreach (bool compact in new[] { false, true })
                    {
                        var input = Input(batch, compact);
                        var actual = family.InvokeAsync(context, new(batch, request), input, default).GetAwaiter().GetResult().EnumerateLogicalOrdinals().ToArray();
                        if (workers == 1) expected.Add((family.FamilyId, compact), actual);
                        else Check(expected.TryGetValue((family.FamilyId, compact), out var baseline) && actual.SequenceEqual(baseline),
                            name + "/" + family.FamilyId + " complete ordered ABI1 P" + workers + ";compact=" + compact);
                    }
                }
                Check(plan.OrderedFamilies.Count * 2 == expected.Count, name + " same Family coverage at P" + workers);
                Check(context.PeakCpuWorkers <= workers, name + " session cap P" + workers);
            }
            finally
            {
                foreach (var family in plan.OrderedFamilies) family.DisposeAsync(context).GetAwaiter().GetResult();
                context.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        // Admission is checked independently from selection and quote availability.
        // Every numerical body must retain all integer configurations, not just old atlas anchors.
        var wideRequest = Compile(source, query, 31);
        foreach (var family in FamilyExecutionCoordinator.CreateRegisteredFamilies(wideRequest, false))
        {
            if (family.FamilyId == TransformationAggregateFamily.Id)
                Check(family.ConditionPerformance.PhysicalImplementationRevision.Contains(".RootMode3.", StringComparison.Ordinal),
                    "T embedded CPU and registered realizations bind the same packed implementation");
            foreach (var body in family.CpuRealizations.OfType<FamilyCpuExecution>().GroupBy(Body))
            {
                Check(body.Select(c => c.Workers).Order().SequenceEqual(Enumerable.Range(1, 31)), name + " all integer realizations " + body.Key);
                bool packedNeow = family is NeowFamily n && NeowCpuFourWayPreGate.TryCompile(n.PricingReplayPlan) is not null;
                Check(body.All(cpu => cpu.Condition(false).PhysicalImplementationRevision.Contains(
                    family.FamilyId == TransformationAggregateFamily.Id || packedNeow ? ".RootMode3." : ".RootMode2.", StringComparison.Ordinal)),
                    name + " every registered worker retains the owning Family root implementation");
                foreach (var cpu in body.Where(c => c.Workers is 3 or 7 or 31))
                    Check(((IFamilyInvocation)cpu).QuotePhysicalWork(new(false, false, 65536)) is null,
                        name + " unmeasured odd worker configuration stays unpriced: " + cpu.Workers);
            }
        }
        Console.WriteLine($"CPU_WORKER_CASE name={name};roots={count};workers=1,3,7,31;completeAbi1=true");
    }

    private static void CheckWindowAndTail(ExactSearchExecutionRequest request)
    {
        // Cross a seed carry boundary and two internal windows; preserve original ordinals.
        foreach (int count in new[] { 2 * 8192 + 3, 49157 })
        foreach (bool compact in new[] { false, true })
        {
            var batch = new SearchBatch((1UL << 32) - 5, count);
            int capacity = count == 49157 ? 65536 : 8192;
            var input = Input(batch, compact);
            var baseline = new FamilyCpuExecution(request, "Check", "ProfileRoot", windowCapacity: capacity)
                .Execute(new(batch, request), input, default, root => (root & 3) != 0).ExportAbi1().ToArray();
            Check(baseline.Length > 100, "split-tail comparison covers the full survivor population");
            foreach (int workers in Workers)
            foreach (int rootMode in new[] { 0, 2, 3 })
            {
                var cpu = new FamilyCpuExecution(request, "Check", "WindowTail", workers, capacity,
                    numericalRoots: rootMode != 0, packedSeedRoots: rootMode == 3);
                var actual = cpu.Execute(new(batch, request), input, default, root => (root & 3) != 0);
                Check(actual.ExportAbi1().Span.SequenceEqual(baseline), $"split tail/carry complete ABI1 P{workers};rootMode={rootMode};compact={compact}");
            }
        }
    }

    private static async Task CheckSharedBudget(ExactSearchExecutionRequest request)
    {
        await using var context = new FamilyExecutionContext(false, cpuWorkerCount: 3);
        var batch = new SearchBatch(0, 8193);
        var input = FamilyCandidateSet.Dense(batch);
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int active = 0, peak = 0;
        bool BlockedMatcher(ulong root)
        {
            int current = Interlocked.Increment(ref active);
            int previous;
            do { previous = Volatile.Read(ref peak); if (previous >= current) break; }
            while (Interlocked.CompareExchange(ref peak, current, previous) != previous);
            // Only the first two callbacks announce saturation; subsequent candidates pass the open gate.
            lock (entered) { if (entered.CurrentCount > 0) entered.Signal(); }
            try { release.Wait(timeout.Token); return (root & 1) == 0; }
            finally { Interlocked.Decrement(ref active); }
        }
        Task<FamilyCandidateSet> Start(FamilyCpuExecution cpu) => Task.Factory.StartNew(() =>
            cpu.Execute(new(batch, request), input, timeout.Token, BlockedMatcher, context),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Reserve one slot as Exact does; two simultaneous Family invocations share the remaining two.
        context.EnterCpu(timeout.Token);
        var first = Start(new(request, "Check.First", "SharedBudget", workers: 7, numericalRoots: true));
        var second = Start(new(request, "Check.Second", "SharedBudget", workers: 7, numericalRoots: true));
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Family callbacks saturate remaining Exact-shared permits");
            Check(context.PeakCpuWorkers == 3 && Volatile.Read(ref peak) == 2, "three permits include one Exact and two Family workers");
            using var cancelled = new CancellationTokenSource();
            Task waiter = context.EnterCpuAsync(cancelled.Token).AsTask();
            Check(!waiter.IsCompleted, "async Exact waiter cannot bypass saturated Family budget");
            cancelled.Cancel();
            bool observed = false;
            try { await waiter; }
            catch (OperationCanceledException) { observed = true; }
            Check(observed, "cancelled CPU permit waiter propagates cancellation");
        }
        finally
        {
            release.Set();
            context.ExitCpu();
            // Drain controllable workers even if a cap assertion fails.
            await Task.WhenAll(first, second);
        }
        var outputs = await Task.WhenAll(first, second);
        var expected = new FamilyCpuExecution(request, "Check.P1", "SharedBudgetReference")
            .Execute(new(batch, request), input, default, root => (root & 1) == 0).ExportAbi1().ToArray();
        Check(outputs.All(output => output.ExportAbi1().Span.SequenceEqual(expected)), "concurrent Family budget preserves complete ABI1");
        Check(context.PeakCpuWorkers == 3 && peak <= 3, "concurrent invocations never exceed session CPU cap");

        // Cancellation while waiting must neither leak a permit nor poison the invocation.
        for (int i = 0; i < 3; i++) context.EnterCpu(timeout.Token);
        var reusable = new FamilyCpuExecution(request, "Check.Cancel", "CancelReuse", workers: 7, numericalRoots: true);
        using var started = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        int callbacks = 0;
        var waiting = Task.Factory.StartNew(() => {
            started.Set();
            return reusable.Execute(new(batch, request), input, cancellation.Token, _ => { Interlocked.Increment(ref callbacks); return true; }, context);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Check(started.Wait(TimeSpan.FromSeconds(10)), "blocked Family invocation started");
            cancellation.Cancel();
            bool observed = false;
            try { await waiting; }
            catch (OperationCanceledException) { observed = true; }
            Check(observed && callbacks == 0, "Family cancelled before permit does no computation");
        }
        finally { for (int i = 0; i < 3; i++) context.ExitCpu(); }
        Check(reusable.Execute(new(batch, request), input, timeout.Token, _ => true, context).Count == input.Count,
            "cancelled invocation reuses released budget");

        var faulted = new FamilyCpuExecution(request, "Check.Fault", "FaultRelease", workers: 7, numericalRoots: true);
        bool faultObserved = false;
        try { faulted.Execute(new(batch, request), input, timeout.Token, _ => throw new InvalidOperationException("WorkerBudgetInjected"), context); }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { faultObserved = true; }
        Check(faultObserved, "matcher fault propagates");
        // Acquire the full capacity after cancellation and fault; a leaked permit times out.
        for (int i = 0; i < 3; i++) context.EnterCpu(timeout.Token);
        for (int i = 0; i < 3; i++) context.ExitCpu();
        Check(context.PeakCpuWorkers == 3, "all permits returned after cancellation and fault");
    }

    private static void CheckPartyPopulation(SearchContext captured)
    {
        var source = captured.Authority;
        var world = source.WorldAuthority!;
        var generation = world.Beta109Generation!;
        const int count = 2;
        // Same controlled slot-binding fixture used by CombinationRegressionChecks.
        var players = Enumerable.Range(0, count).Select(slot => new RuntimeContextAuthoritySnapshot(
            source.Character, source.Ascension, slot, count, source.NoRunModifiers, source.VanillaNeowCatalogExact,
            source.AllCharacterCardPoolsUnlocked, source.UnlockedCommonCards, source.UnlockedUncommonCards,
            source.GameVersion, source.ProfileId, source.IsVanilla, source.SourceModId, source.SourceAssembly,
            source.SourceAuthority, source.Completeness, source.ResolutionStatus, source.EffectAuthority,
            world with { Beta109Generation = generation with {
                GameMode = WorldGameMode.Multiplayer, IsMultiplayer = true, PlayerCount = count, PersonalPlayerSlot = slot,
                PartyBossDiscoveryExact = true,
                LobbyPlayers = Enumerable.Range(0, count).Select(i => new Beta109LobbyPlayerSnapshot(i, source.Character.CharacterKey, false, true)).ToArray(),
                PartyRelicBuckets = Enumerable.Repeat(generation.PlayerRelicBuckets, count).ToArray()
            } }, WorldGameMode.Multiplayer, PredictionGameModeAuthority.ExplicitRequest)).ToArray();
        var party = new OrderedPartyAuthority(players, Enumerable.Repeat("CapturedSoloPools.FixedSlotFixture", count).ToArray(), players[0].WorldAuthority!);
        var context = captured with { Authority = players[0], Party = party };
        CheckPartyNeowWindow(context);
        var shop = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/S-three-shops.json").Query;
        var combat = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/cpu-main-v1/C-three-potions.json").Query;
        var first = combat with { LegacyNeow = combat.LegacyNeow with { NeowRelics = new([], [BaseGameModelKeys.Relics.GoldenPearl], []) } };
        var observation = PartySeedInformation.Project("000000000000", party);
        var boss = observation.Bosses.First(b => b.Act == 1 && b.Ordinal == 1).BossKey;
        var query = SearchQuery.Empty with {
            Players = [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty) { Conditions = shop }],
            VariantBossBranches = [new(1, observation.Acts[0], new([boss], [], []), ModelKeySetFilter.Empty, false)]
        };
        var batch = new SearchBatch(0, 257);
        var expected = new Dictionary<(string Family, bool Compact), ulong[]>();
        foreach (int workers in Workers)
        {
            var request = Compile(context, query, workers);
            var plan = FamilyExecutionCoordinator.Plan(request, false);
            Check(new[] { "N.Neow", "S.MerchantShopColorless", "C.CombatReward", "W.World" }
                .All(id => plan.OrderedFamilies.Any(f => f.FamilyId == id)), "party retains N/S/C/W filters");
            Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, plan.OrderedFamilies) == 8192,
                "mixed Party CPU allocation retains its existing root window");
            var execution = new FamilyExecutionContext(false, cpuWorkerCount: workers);
            try
            {
                foreach (var family in plan.OrderedFamilies)
                {
                    if (family is PartyFamily wrapper)
                        foreach (var (_, child) in wrapper.PersonalStages)
                            Check(child.ConditionPerformance.PhysicalImplementationRevision.Contains($".P{workers}.B", StringComparison.Ordinal),
                                "party child selected request workers " + child.FamilyId);
                    else Check(family.ConditionPerformance.PhysicalImplementationRevision.Contains($".P{workers}.B", StringComparison.Ordinal),
                        "party direct selected request workers " + family.FamilyId);
                    foreach (bool compact in new[] { false, true })
                    {
                        var output = family.InvokeAsync(execution, new(batch, request), Input(batch, compact), default)
                            .GetAwaiter().GetResult().EnumerateLogicalOrdinals().ToArray();
                        if (workers == 1) expected.Add((family.FamilyId, compact), output);
                        else Check(output.SequenceEqual(expected[(family.FamilyId, compact)]), $"party {family.FamilyId} complete ABI1 P{workers};compact={compact}");
                    }
                }
                Check(execution.PeakCpuWorkers <= workers, "party session cap");
            }
            finally
            {
                foreach (var family in plan.OrderedFamilies) family.DisposeAsync(execution).GetAwaiter().GetResult();
                execution.DisposeAsync().GetAwaiter().GetResult();
            }
        }
    }

    private static void CheckPartyNeowWindow(SearchContext context)
    {
        var query = SearchQuery.Empty with {
            Players = context.Party!.Players.Select((_, slot) => new PlayerOfferQuery(slot,
                new([], [BaseGameModelKeys.Relics.GoldenPearl], []))).ToArray()
        };
        // Cross a visible-seed carry and two complete private windows plus a tail.
        var batch = new SearchBatch((1UL << 32) - 5, 2 * 65536 + 3);
        var expected = new Dictionary<bool, ulong[]>();
        foreach (int workers in new[] { 1, 7, 16, 31 })
        {
            var request = Compile(context, query, workers);
            var plan = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false);
            Check(plan.OrderedFamilies.Count == 1 && plan.OrderedFamilies[0] is NeowFamily.PartyInitial,
                "Party N-only check selects the real PartyInitial wrapper");
            var family = (NeowFamily.PartyInitial)plan.OrderedFamilies[0];
            Check(family.CpuWindowCapacity == 65536 && family.AnalyticalCost.InputCapacityPerInvocation == 65536 &&
                FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, plan.OrderedFamilies) == 65536,
                "Party N-only execution and analytical windows agree");
            Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, []) == 8192,
                "Party allocation without N retains its existing root window");
            foreach (bool compact in new[] { false, true })
                Check(family.ResolveConditionPerformance(compact).PhysicalImplementationRevision.Contains(
                    $".P{workers}.B65536.", StringComparison.Ordinal), "Party N requested workers and geometry identity");
            if (family.QuotePhysicalWork(new(false, false, 65536)) is { } quote)
                Check(quote.WindowCapacity == 65536, "Party N available CPU quote matches execution window");
            var execution = new FamilyExecutionContext(false, cpuWorkerCount: workers);
            NeowFamily.PartyInitial? reference = workers == 1 ? new(request, optimize: false) : null;
            try
            {
                foreach (bool compact in new[] { false, true })
                {
                    var input = Input(batch, compact);
                    if (reference is not null)
                        expected.Add(compact, reference.InvokeAsync(execution, new(batch, request), input, default)
                            .GetAwaiter().GetResult().ExportAbi1().ToArray());
                    var output = family.InvokeAsync(execution, new(batch, request), input, default)
                        .GetAwaiter().GetResult();
                    var abi1 = output.ExportAbi1().ToArray();
                    Check(output.Batch == batch && abi1.SequenceEqual(expected[compact]),
                        $"Party N full reference ABI1/carry/two-window tail P{workers};compact={compact}");
                    Check(abi1.Length > 0 && abi1.All(o => o < (ulong)batch.BatchCandidateCount) &&
                        abi1.Zip(abi1.Skip(1)).All(pair => pair.First < pair.Second),
                        "Party N survivors retain nonempty strictly ordered batch-local ordinals");
                }
                Check(execution.PeakCpuWorkers <= workers, "Party N large-window execution shares the session CPU cap");
            }
            finally
            {
                if (reference is not null) reference.DisposeAsync(execution).GetAwaiter().GetResult();
                foreach (var item in plan.OrderedFamilies) item.DisposeAsync(execution).GetAwaiter().GetResult();
                execution.DisposeAsync().GetAwaiter().GetResult();
            }
        }
        Console.WriteLine("CPU_PARTY_N_WINDOW_PASS roots=131075;workers=1,7,16,31;window=65536;denseCompact=true;fullReferenceAbi1=true");
    }
}
