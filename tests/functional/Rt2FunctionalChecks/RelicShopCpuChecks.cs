using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.FamilyExecution;

internal static class RelicShopCpuChecks
{
    internal static int Run()
    {
        string? prepare = Environment.GetEnvironmentVariable("RT2_CPU_R02_PREPARE_DIR");
        if (!string.IsNullOrWhiteSpace(prepare)) return PrepareMixedFixtures(prepare);
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var w = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/family-pricing-matrix-v1/R-shop.json");
        ExactSearchExecutionRequest Compile(SearchQuery q) => ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, context.Context), w.Options).Plan
            ?? throw new InvalidDataException("shop query compile");
        var basis = Compile(w.Query);
        RelicFamilyPlanCompiler.TryCompilePool(basis, out _, out var dense, out var lanes, out _);
        var keys = dense.Where(p => lanes[3].ContainsKey(p.Value)).Take(3).Select(p => p.Key).ToArray();
        var queries = new List<SearchQuery> { w.Query };
        foreach (int depth in new[] { 1, 2, 3, 5 })
        {
            ModelKey?[] slots = new ModelKey?[depth]; slots[^1] = keys[0];
            queries.Add(w.Query with { RelicShopSequenceConditions = [new(depth, CombatRewardSequenceOrderMode.Ordered, slots)] });
            if (depth >= 2)
            {
                slots = new ModelKey?[depth]; slots[0] = keys[0]; slots[1] = keys[1];
                queries.Add(w.Query with { RelicShopSequenceConditions = [new(depth, CombatRewardSequenceOrderMode.Unordered, slots)] });
            }
            foreach (var mode in new[] { SearchSequenceRangeMode.FirstN, SearchSequenceRangeMode.ExactSlot })
            foreach (var filter in new[] { new ModelKeySetFilter(keys, [], []), new ModelKeySetFilter([], [], [keys[1]]) })
                queries.Add(w.Query with { RelicShopSequenceConditions = [], RelicSequenceConstraints = [new(RelicSequenceKind.Shop, mode, depth, filter)] });
        }
        var ordinary = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/R-common.json");
        queries.Add(w.Query with { RelicSequenceConstraints = ordinary.Query.RelicSequenceConstraints });
        queries.Add(w.Query with { RelicShopSequenceConditions = [], RelicSequenceConstraints =
            [new(RelicSequenceKind.Shop, SearchSequenceRangeMode.FirstN, 3, new ModelKeySetFilter([], keys.Take(2).ToArray(), []))] });
        var rare = w.Query with { RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Ordered, keys.Select(k => (ModelKey?)k).ToArray())] };
        queries.Add(rare);
        ProductionSearchReplay.Write(".build-evidence/cpu-n-shop/shop-three.json", w with { Query = rare,
            Options = w.Options with { ScanCount = 1048576, TargetMatchCount = int.MaxValue, WorkerCount = 4 } });
        int checks = 0;
        foreach (var q in queries)
        {
            var request = Compile(q);
            if (!RelicFamilyPlanCompiler.TryCompile(request, out var plan, out string issue)) throw new InvalidDataException(issue);
            var fast = RelicShopCpuPlan.TryCompile(plan!) ?? throw new InvalidDataException("shop not admitted");
            RelicFamily.TryCreate(request, out var family);
            var reference = typeof(RelicFamily).GetMethod("MatchesCpuReference", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<ulong, bool>>(family);
            var random = new Random(77);
            int kept = 0;
            for (int i = 0; i < 9217; i++)
            {
                ulong root = i < 8193 ? Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, (ulong)i))
                    : (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 32);
                bool actual = fast.Matches(root);
                if (actual != reference(root)) throw new InvalidDataException($"shop population mismatch:{queries.IndexOf(q)}/{i}");
                checks++; if (actual) kept++;
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (ulong root = 0; root < 32768; root++) if (fast.Matches(root)) kept++;
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            if (bytes != 0) throw new InvalidDataException("shop numerical allocation");
            Console.WriteLine($"SHOP_CPU_CASE PASS index={queries.IndexOf(q)};kept={kept};allocatedBytes={bytes}");
        }
        // Confront filtered eligibility independently of the captured all-eligible
        // shop pool: shuffle the entire bag, filter, reverse, then inspect its head.
        RelicFamilyPlanCompiler.TryCompile(basis, out var original, out _);
        int bucket = Enumerable.Range(0, original!.Pool.BucketCount).Single(i => original.Pool.BucketScopes[i] == 1 && original.Pool.BucketKinds[i] == 4);
        int offset = original.Pool.BucketOffsets[bucket], length = original.Pool.BucketLengths[bucket];
        ushort target = original.ShopPredicates[0].TargetIds[0];
        foreach (bool singleton in new[] { false, true })
        {
            byte[] flags = (byte[])original.Pool.EntryFlags.Clone();
            for (int i = 0; i < length; i++) flags[offset + i] = original.Pool.DenseRelicIds[offset + i] == target || (!singleton && i % 3 == 0) ? (byte)1 : (byte)0;
            var plan = original with { Pool = original.Pool with { EntryFlags = flags } };
            var fast = RelicShopCpuPlan.TryCompile(plan)!;
            for (ulong root = 0; root < 8193; root++)
            {
                var rng = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
                for (int b = 0; b < bucket; b++) rng.ConsumeUnstableShuffle(plan.Pool.BucketLengths[b]);
                int[] entries = Enumerable.Range(offset, length).ToArray(); rng.UnstableShuffle(entries.AsSpan());
                ushort first = plan.Pool.DenseRelicIds[entries.AsEnumerable().Reverse().First(i => flags[i] != 0)];
                if (fast.Matches(root) != (first == target)) throw new InvalidDataException("filtered full-shuffle parity");
                checks++;
            }
        }
        RelicFamily.TryCreate(basis, out var referenceFamily);
        var basisReference = typeof(RelicFamily).GetMethod("MatchesCpuReference", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<ulong, bool>>(referenceFamily);
        checks += RelicCpuChecks.CheckEnvelope(basis, RelicShopCpuPlan.TryCompile(original)!.Matches, basisReference);
        // No-replacement unordered multiplicity: two copies of one identity cannot occur.
        var duplicateDepth = (byte[])original.PositiveDepthByLane.Clone(); duplicateDepth[3] = 2;
        var duplicates = RelicShopCpuPlan.TryCompile(original with { ShopPredicates = [new(2, 1, [target, target])], PositiveDepthByLane = duplicateDepth })!;
        for (ulong root = 0; root < 8193; root++) { if (duplicates.Matches(root)) throw new InvalidDataException("shop duplicate multiplicity"); checks++; }
        Console.WriteLine($"SHOP_CPU_CHECKS PASS cases={queries.Count};checks={checks};filteredSynthetic=true");
        return 0;
    }

    private static int PrepareMixedFixtures(string directory)
    {
        const string seed = "000000000000";
        string output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        var rows = new List<Dictionary<string, object?>>();
        var failures = new List<Exception>();
        int checks = 0;
        var profile = Beta111Profile.Instance;
        foreach (var specimen in new[] {
            (Name: "SP", Slot: 0, Players: 1, Path: "tools/ProductionSearchHost/Corpus/v1/context.json"),
            (Name: "2p-slot1", Slot: 1, Players: 2, Path: ".build-evidence/cpu-audit-followthrough-20261015/party-corpus-v3/2p/context.json"),
            (Name: "4p-slot3", Slot: 3, Players: 4, Path: ".build-evidence/cpu-audit-followthrough-20261015/party-corpus-v3/4p/context.json") })
        foreach (bool high in new[] { true, false })
        {
            string id = "R02-" + specimen.Name + (high ? "-high" : "-low");
            var row = new Dictionary<string, object?> {
                ["Id"] = id, ["Mode"] = specimen.Players == 1 ? "SP" : "MP",
                ["Context"] = Path.Combine(output, id + "-context.json"),
                ["Workload"] = Path.Combine(output, id + ".json"), ["SourceContext"] = specimen.Path,
                ["ActiveSlot"] = specimen.Slot, ["Players"] = specimen.Players,
                ["RequestedWorkers"] = 16, ["ExpectedFamilies"] = new[] { "R.Relic" },
                ["PositiveSeeds"] = new[] { seed }, ["NeedsNativePositiveAuthoring"] = false,
                ["IndependentBenchmark"] = true, ["PositiveExactValidated"] = false, ["positiveReady"] = false,
                ["PositiveRequirement"] = "Whole-table Production Exact Match and actual CPU-only plan survival; CPU parity alone is not Exact validation",
                ["Role"] = high ? "Mixed ordinary high survival plus ordered Shop3" : "Mixed ordinary low survival plus ordered Shop3"
            };
            rows.Add(row);
            try
            {
                var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(specimen.Path);
                var context = captured.Context;
                if ((context.Party?.Players.Count ?? 1) != specimen.Players)
                    throw new InvalidDataException("R02 captured party count mismatch");
                var owner = context.Party is null ? context.Authority : context.Party.Players[specimen.Slot];
                var generation = owner.WorldAuthority?.Beta109Generation ?? throw new InvalidDataException("R02 owner world authority missing");
                if (owner.PlayerSlotIndex != specimen.Slot || owner.PlayersCount != specimen.Players ||
                    specimen.Players > 1 && (!generation.HasExactPartyRelicBags || generation.PersonalPlayerSlot != specimen.Slot))
                    throw new InvalidDataException("R02 requires the captured active owner and its real party buckets");
                ProductionSearchReplay.Write((string)row["Context"]!, captured);
                var projected = RelicSequencePredictor.Predict(profile, seed, owner.WorldAuthority, previewCount: 3);
                if (projected.Status != SeedDomainEvaluationStatus.Evaluated)
                    throw new InvalidDataException("R02 real pool projection unavailable: " + projected.IssueCode);
                var common = projected.Lanes.Single(l => l.Kind == RelicSequenceKind.Common);
                var shop = projected.Lanes.Single(l => l.Kind == RelicSequenceKind.Shop);
                if (common.Precision != PredictionPrecision.Exact || shop.Precision != PredictionPrecision.Exact ||
                    common.Entries.Count != 3 || shop.Entries.Count != 3 || common.Entries[1].RelicKey == common.Entries[0].RelicKey)
                    throw new InvalidDataException("R02 real Common/Shop projection lacks exact distinct entries");
                var personal = SearchQuery.Empty with {
                    RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, high ? 1 : 3,
                        high ? new ModelKeySetFilter([], [], [common.Entries[1].RelicKey]) :
                            new ModelKeySetFilter([], common.Entries.Select(e => e.RelicKey).ToArray(), []))],
                    RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Ordered,
                        shop.Entries.Select(e => (ModelKey?)e.RelicKey).ToArray())]
                };
                var query = context.Party is null ? personal : SearchQuery.Empty with {
                    Players = Enumerable.Range(0, specimen.Players).Select(slot => new PlayerOfferQuery(slot,
                        ModelKeySetFilter.Empty) { Conditions = slot == specimen.Slot ? personal : SearchQuery.Empty }).ToArray()
                };
                var workload = new ProductionSearchReplayWorkload(ProductionSearchReplay.SchemaVersion, query,
                    new(seed, 1L << 34, int.MaxValue, 16));
                ProductionSearchReplay.Write((string)row["Workload"]!, workload);
                var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), workload.Options);
                var parent = compiled.Plan ?? throw new InvalidDataException("R02 compile: " + compiled.Issue);
                var request = context.Party is null ? parent : ExactSearchExecutionRequestFactory.ForPlayer(parent,
                    parent.CompiledSearch.PlayerSearches[specimen.Slot]);
                if (!RelicFamilyPlanCompiler.TryCompile(request, out var candidate, out string issue) || candidate is null)
                    throw new InvalidDataException("R02 personal R plan: " + issue);
                var plan = candidate;
                var expectedPrefix = generation.SharedRelicBuckets.Concat(specimen.Players > 1
                    ? generation.PartyRelicBuckets.Take(specimen.Slot).SelectMany(b => b) : []).ToArray();
                var expectedBuckets = expectedPrefix.Concat(generation.PlayerRelicBuckets).ToArray();
                if (plan.Pool.BucketCount != expectedBuckets.Length ||
                    !plan.Pool.BucketLengths.SequenceEqual(expectedBuckets.Select(b => b.OrderedEntries.Count)) ||
                    !plan.Pool.BucketScopes.SequenceEqual(Enumerable.Range(0, expectedBuckets.Length)
                        .Select(b => b < expectedPrefix.Length ? (byte)0 : (byte)1)))
                    throw new InvalidDataException("R02 compiled pool lost real shared/preceding-player bucket geometry");
                int shopBucket = Enumerable.Range(0, plan.Pool.BucketCount)
                    .Single(b => plan.Pool.BucketScopes[b] == 1 && plan.Pool.BucketKinds[b] == 4);
                int last = Enumerable.Range(0, plan.Pool.BucketCount).Where(b => plan.Pool.BucketScopes[b] == 1 &&
                    plan.Pool.BucketKinds[b] is >= 1 and <= 3 && plan.TrackedCountsByLane[plan.Pool.BucketKinds[b] - 1] > 0).Max();
                var ordinary = RelicCpuPlan.TryCompile(plan with {
                    Predicates = plan.Predicates.Where(p => p.Lane != 3).ToArray(), ShopPredicates = [], LastRequiredBucket = last
                }) ?? throw new InvalidDataException("R02 ordinary plan missing");
                var fast = RelicShopCpuPlan.TryCompile(plan) ?? throw new InvalidDataException("R02 mixed plan not admitted");
                if (!RelicFamily.TryCreate(request, out var family)) throw new InvalidDataException("R02 reference Family missing");
                var reference = typeof(RelicFamily).GetMethod("MatchesCpuReference", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate<Func<ulong, bool>>(family);
                var roots = Enumerable.Range(0, 1025).Select(i => profile.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(profile, (ulong)i))).ToList();
                var random = new Random(2077);
                for (int i = 0; i < 32; i++) roots.Add((ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 32));
                int passed = 0, rejected = 0, kept = 0;
                foreach (ulong root in roots)
                {
                    bool actual = fast.Matches(root);
                    if (actual != reference(root)) throw new InvalidDataException("R02 full mixed population mismatch: " + id + "/" + root);
                    checks++; if (actual) kept++;
                    if (!ordinary.Matches(root, out var continuation)) { rejected++; continue; }
                    passed++;
                    var oracle = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
                    for (int b = 0; b <= last; b++) oracle.ConsumeUnstableShuffle(plan.Pool.BucketLengths[b]);
                    if (ordinary.NextBucket != last + 1 || continuation.CaptureCheckpoint() != oracle.CaptureCheckpoint() ||
                        continuation.NextDouble() != oracle.NextDouble())
                        throw new InvalidDataException("R02 successful ordinary continuation checkpoint mismatch: " + id);
                    checks++;
                }
                if (passed == 0 || !fast.Matches(profile.ComputeRootSeed(seed)) || !reference(profile.ComputeRootSeed(seed)))
                    throw new InvalidDataException("R02 projected seed is not a positive mixed CPU witness: " + id);
                row["PreparationStatus"] = "prepared-cpu-parity-pending-exact";
                row["PopulationRoots"] = roots.Count; row["CpuKept"] = kept;
                row["OrdinaryPassed"] = passed; row["OrdinaryRejected"] = rejected;
                row["PrefixBucketCount"] = expectedPrefix.Length; row["NextBucket"] = ordinary.NextBucket;
                row["ShopBucket"] = shopBucket; row["ReusesContinuation"] = ordinary.NextBucket <= shopBucket;
                row["CpuPopulationReferenceParity"] = true; row["SuccessfulCheckpointParity"] = true;
                row["CompileEvidence"] = "Prepared with complete captured context and real personal R pool; whole-table Exact remains pending";
            }
            catch (Exception ex)
            {
                row["PreparationStatus"] = "failed"; row["Exception"] = ex.ToString();
                failures.Add(new InvalidDataException("R02 fixture preparation failed: " + id, ex));
            }
            File.WriteAllText(Path.Combine(output, "validation-manifest.json"), JsonSerializer.Serialize(new {
                SchemaVersion = 1, SourceHead = (string?)null, Producer = "R02 invocation-local prefix experiment; RT2_CPU_R02_PREPARE_DIR opt-in",
                Cases = rows
            }, ProductionSearchReplay.JsonOptions));
            Console.WriteLine($"R02_FIXTURE id={id};status={row["PreparationStatus"]};positiveExact=false");
        }
        if (failures.Count > 0) throw new AggregateException("R02 fixture preparation remains incomplete", failures);
        Console.WriteLine($"R02_FIXTURES PASS cases={rows.Count};checks={checks};wholeTableExactPending=true;performanceMeasured=false");
        return 0;
    }
}
