using System.Security.Cryptography;
using Godot;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Random;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class InitialRoomGpuChecks
{
    private static readonly GpuNumericalVariant[] NumericalVariants = [GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne];

    internal static async Task RunAsync(ExactSearchExecutionRequest basis, string output)
    {
        var context = basis.CompiledSearch.Context;
        if (context.Party is not null) throw new ArgumentException("InitialRoomGpuChecks.RequiresSolo");
        Directory.CreateDirectory(output);
        int checks = 0;
        void Check(bool condition, string name) { checks++; if (!condition) throw new InvalidDataException("InitialRoomGpuChecks:" + name); }
        var profile = Beta111Profile.Instance;
        ulong Root(ulong ordinal) => profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
        ExactSearchExecutionRequest Request(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(query, context), new("000000000000", 32771, int.MaxValue, 1)).Plan
            ?? throw new InvalidDataException("InitialRoomGpuChecks.Compile");
        // Actual game RNG and UnstableShuffle/rarity entry points, immutable DTO
        // input. No game RunState is created or mutated by this oracle.
        ModelKey[] SourceTreasure(ulong root)
        {
            var buckets = new Dictionary<string, List<ModelKey>>(StringComparer.Ordinal);
            var rng = new Rng(root, "up_front");
            foreach (var bucket in context.Authority.WorldAuthority!.Beta109Generation!.SharedRelicBuckets)
            {
                var values = bucket.OrderedRelics.ToList(); values.UnstableShuffle(rng);
                buckets[bucket.BucketId[(bucket.BucketId.LastIndexOf(':') + 1)..]] = values;
            }
            var treasureRng = new Rng(root, "treasure_room_relics");
            string[] rarities = ["Common", "Uncommon", "Rare"];
            var result = new ModelKey[2];
            for (int i = 0; i < 2; i++)
            {
                int lane = Array.IndexOf(rarities, RelicFactory.RollRarity(treasureRng).ToString());
                while (lane < 3)
                {
                    buckets[rarities[lane]].RemoveAll(key => key.Entry == "MASSIVE_SCROLL");
                    if (buckets[rarities[lane]].Count != 0) break;
                    lane++;
                }
                result[i] = lane == 3 ? new("RELIC", "CIRCLET") : buckets[rarities[lane]][0];
                if (lane < 3) buckets[rarities[lane]].RemoveAt(0);
            }
            return result;
        }
        var source = SourceTreasure(Root(0));
        var fixtures = new (string Name, SearchQuery Query)[] {
            ("Unknown1", SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1 }),
            ("Unknown3", SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 }),
            ("OrderedTwo", SearchQuery.Empty with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Ordered, [source[0], source[1]]) }),
            ("UnorderedTwo", SearchQuery.Empty with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Unordered, [source[1], source[0]]) }),
            ("SecondWindow", SearchQuery.Empty with { TreasureRelics = new(2, 1, TreasureRelicOrderMode.Unordered, [source[1], null]) }),
            ("DuplicateConsumption", SearchQuery.Empty with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Unordered, [source[0], source[0]]) }),
            ("Joint", SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3,
                TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered, [source[0], null]) }) };
        var receipts = new List<object>();
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (var fixture in fixtures)
        {
            var request = Request(fixture.Query);
            var plan = InitialRoomsPlan.TryCompile(request) ?? throw new InvalidDataException("InitialRoomGpuChecks.Admission:" + fixture.Name);
            Check(plan.CoversTreasure == (fixture.Query.TreasureRelics is not null), "complete treasure coverage:" + fixture.Name);
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                ulong root = Root(ordinal);
                Check(plan.Matches(root) == plan.Reference(root), "CPU/reference:" + fixture.Name);
                if (fixture.Query.TreasureRelics is { } condition)
                {
                    var actual = SourceTreasure(root); int used = 0; bool expected = true;
                    for (int slot = 0; slot < condition.Count; slot++)
                    {
                        if (condition.Slots[slot] is not { } target) continue;
                        bool found = false;
                        for (int i = condition.StartOrdinal - 1; i < condition.StartOrdinal + condition.Count - 1; i++)
                            if ((used & (1 << i)) == 0 && actual[i] == target &&
                                (condition.OrderMode == TreasureRelicOrderMode.Unordered || i == condition.StartOrdinal - 1 + slot))
                            { used |= 1 << i; found = true; break; }
                        expected &= found;
                    }
                    if (fixture.Query.FirstActUnknownEventPrefixCount > 0)
                        expected &= InitialRoomsPlan.MatchesUnknown(root, fixture.Query.FirstActUnknownEventPrefixCount);
                    Check(plan.Matches(root) == expected, "gameRngSharedShuffleAndRarity:" + fixture.Name);
                }
            }
            var population = Enumerable.Range(0, 32771).Where(i => plan.Matches(Root((ulong)i))).Select(i => (ulong)i).ToArray();
            foreach (var variant in NumericalVariants)
            {
            var actualPlan = InitialRoomsPlan.TryCompile(request, numericalVariant: variant)
                ?? throw new InvalidDataException("InitialRoomGpuChecks.VariantAdmission:" + fixture.Name + ":" + variant);
            ExportPlanSource(actualPlan, fixture.Name, output, receipts, Check);
            Check((actualPlan.ActualRevision != plan.ActualRevision) == (variant == GpuNumericalVariant.IntegerRne), "distinct variant revision:" + fixture.Name);
            await execution.ExecuteGpuAsync(rd =>
            {
                using var gpu = new InitialRoomsGpuExecutor(rd, actualPlan);
                foreach (int count in new[] { 0, 1, 63, 64, 65, 32771 })
                foreach (bool compact in new[] { false, true })
                {
                    var batch = new SearchBatch(0, count);
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch,
                        Enumerable.Range(0, count).Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                    var expected = population.Where(i => i < (ulong)count && (!compact || i % 3 == 0)).ToArray();
                    var actual = gpu.Execute(input, default, out var metrics);
                    ulong[] actualOrdinals = actual.EnumerateLogicalOrdinals().ToArray();
                    Check(actualOrdinals.SequenceEqual(expected), "complete GPU population:" + fixture.Name + ":" + variant);
                    receipts.Add(new { fixture.Name, Variant = variant.ToString(), actualPlan.ActualRevision,
                        Requirements = actualPlan.NumericRequirements.ToString(), Device = gpu.Device, count, compact, input = input.Count, survivors = actual.Count,
                        ExpectedAbi1 = expected, ActualAbi1 = actualOrdinals,
                        digest = Convert.ToHexString(SHA256.HashData(FamilyGpuComputeUtility.ToBytes(expected.Select(i => checked((uint)i)).ToArray()))), metrics,
                        oracle = "FullNumericPopulation;First128CoreReferenceAndGameRngSharedShuffleRarity" });
                }
                return true;
            }, default);
            }
            if (fixture.Name == "Unknown1")
            {
                await CheckWideUnknownPopulation(execution, request, output, receipts, Check);
                await CheckTwoWindowHeaderBank(execution, request, output, receipts, Check);
            }
            ProductionSearchReplay.Write(Path.Combine(output, fixture.Name + "-workload.json"),
                new ProductionSearchReplayWorkload(1, fixture.Query, request.RunOptions));
        }
        ProductionSearchReplay.Write(Path.Combine(output, "context.json"), new ProductionSearchReplayContext(1, "InitialRoomsCapturedSolo", context));
        await CheckOwnedPublicRoutes(basis, output, execution, receipts, Check);
        var arguments = OS.GetCmdlineUserArgs();
        int contextsArg = Array.IndexOf(arguments, "--party-unknown-context-root");
        if (contextsArg >= 0)
        {
            if (contextsArg + 1 >= arguments.Length) throw new ArgumentException("Missing --party-unknown-context-root value");
            await CheckCapturedPartyUnknown(Path.GetFullPath(arguments[contextsArg + 1]), output, execution, receipts, Check);
        }
        CompatibilityChecks.WriteEvidence(Path.Combine(output, "initial-rooms-gpu-checks.json"), new { checks, receipts,
            ActualDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(), CompleteOrderedAbi1Compared = true,
            TargetMetalExecutionClaimed = false, CalibrationSubmitted = false });
        GD.Print($"INITIAL_ROOMS_GPU_PASS checks={checks};fixtures={fixtures.Length};nativeIntegerParity=true;denseCompact=true;productionPrivateDisabled=true;sourceRngAndReference=true;targetMetalClaimed=false");
    }

    private static async Task CheckOwnedPublicRoutes(ExactSearchExecutionRequest basis, string output, FamilyExecutionContext execution,
        List<object> receipts, Action<bool, string> check)
    {
        var context = basis.CompiledSearch.Context;
        var profile = Beta111Profile.Instance;
        ulong Root(ulong ordinal) => profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
        ulong witness = Enumerable.Range(0, 1024).Where(i => i % 3 == 0).Select(i => (ulong)i)
            .First(i => InitialRoomsPlan.MatchesUnknown(Root(i), 3));
        ExactSearchExecutionRequest Request(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(query, context), new("000000000000", 4097, int.MaxValue, 1)).Plan
            ?? throw new InvalidDataException("OwnedInitialRoomsGpu.Compile");
        var initial = RelicSequencePredictor.Predict(profile, Beta110SeedCodec.FormatOrdinal(witness), context.Authority.WorldAuthority);
        var observed = TreasureRelicPredictor.Predict(profile, Root(witness), initial, []);
        var treasure = new TreasureRelicSearchCondition(1, 2, TreasureRelicOrderMode.Ordered,
            [observed.Entries[0].RelicKey, observed.Entries[1].RelicKey]);
        var compoundTreasure = new TreasureRelicSearchCondition(1, 1, TreasureRelicOrderMode.Ordered,
            [observed.Entries[0].RelicKey, null]);
        var ordinary = SearchQuery.Empty with { RelicSequenceConstraints = [new(RelicSequenceKind.Common,
            SearchSequenceRangeMode.FirstN, 3, new([initial.Lanes.Single(lane => lane.Kind == RelicSequenceKind.Common).Entries[0].RelicKey], [], []))] };
        var shop = SearchQuery.Empty with { RelicShopSequenceConditions = [new(1, CombatRewardSequenceOrderMode.Ordered,
            [initial.Lanes.Single(lane => lane.Kind == RelicSequenceKind.Shop).Entries[0].RelicKey])] };
        var worldSource = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(ProjectSettings.GlobalizePath("res://Corpus/family-pricing-matrix-v1/W-K4.json")).Query;
        var world = SearchQuery.Empty with { EventSequenceConstraints = [worldSource.EventSequenceConstraints.First(condition => condition.Act == 1)] };
        var fixtures = new (string Name, SearchQuery Query)[] {
            ("OwnedTreasure", SearchQuery.Empty with { TreasureRelics = treasure }),
            ("OwnedTreasureOrdinaryShop", ordinary with { TreasureRelics = compoundTreasure,
                RelicShopSequenceConditions = shop.RelicShopSequenceConditions }),
            ("OwnedUnknownWorld", world with { FirstActUnknownEventPrefixCount = 3 }),
            ("OwnedBoth", SearchQuery.Empty with { TreasureRelics = treasure, FirstActUnknownEventPrefixCount = 3 })
        };
        foreach (var fixture in fixtures)
        {
            var request = Request(fixture.Query);
            ProductionSearchReplay.Write(Path.Combine(output, fixture.Name + "-workload.json"),
                new ProductionSearchReplayWorkload(ProductionSearchReplay.SchemaVersion, fixture.Query, request.RunOptions));
            var rooms = InitialRoomsPlan.TryCompile(request)!;
            RelicFamily.TryCreate(Request(fixture.Query with { TreasureRelics = null }), out var legacyR);
            WorldFamily.TryCreate(Request(fixture.Query with { FirstActUnknownEventPrefixCount = 0 }), out var legacyW, false);
            ulong[] oracle = Enumerable.Range(0, 4097).Select(i => (ulong)i).Where(i => rooms.Reference(Root(i)) &&
                (legacyR is not RelicFamily relic || relic.MatchesCpuReference(Root(i))) &&
                (legacyW is not WorldFamily worldFamily || worldFamily.Replay.Matches(Root(i)))).ToArray();
            check(oracle.Length > 0, fixture.Name + ":nonempty oracle population");
            if (fixture.Name == "OwnedTreasureOrdinaryShop")
            {
                check(oracle.Contains(witness), fixture.Name + ":known shared positive");
                var old = (RelicFamily)legacyR!;
                check(Enumerable.Range(0, 4097).Select(i => (ulong)i).Any(i => old.MatchesCpuReference(Root(i)) && !rooms.Reference(Root(i))),
                    fixture.Name + ":treasure gate rejects legacy survivors");
                check(Enumerable.Range(0, 4097).Select(i => (ulong)i).Any(i => rooms.Reference(Root(i)) && !old.MatchesCpuReference(Root(i))),
                    fixture.Name + ":ordinary and shop gate rejects treasure survivors");
            }
            foreach (var mode in new[] { "CPU", "NativeFloat64", "IntegerRne" })
            foreach (bool compact in new[] { false, true })
            {
                bool gpu = mode != "CPU";
                var selected = FamilyExecutionCoordinator.Plan(request, gpu, gpuFloat64Available: mode != "IntegerRne");
                try
                {
                    var coverage = selected.Stages.SelectMany(stage => stage.Coverage).ToHashSet(StringComparer.Ordinal);
                    var variantOwner = selected.ExecutionOrderUnits.Select(family => family is FamilyCpuExecution cpu
                        ? cpu.RealizationOwner ?? family : family).OfType<VariantOpeningFamily>().SingleOrDefault();
                    check(coverage.Contains(InitialRoomsInvocation.TreasureCoverage) == (fixture.Query.TreasureRelics is not null) &&
                        coverage.Contains(VariantOpeningFamily.Id) == (fixture.Query.FirstActUnknownEventPrefixCount > 0) &&
                        (variantOwner?.Plan.UnknownPrefixCount ?? 0) == fixture.Query.FirstActUnknownEventPrefixCount &&
                        !coverage.Contains(InitialRoomsInvocation.UnknownCoverage),
                        fixture.Name + ":actual default registration:" + gpu);
                    check(selected.OrderedFamilies.OfType<InitialRoomsInvocation>().All(f => !((IFamilyInvocation)f).CanBindPrivateSerial &&
                        f.ConditionPerformance.UsesGpu == gpu &&
                        (mode != "IntegerRne" || f.QuotePhysicalWork(new(false, false, 4097)) is not { } quote ||
                            FamilyCoarsePricing.IsEstimate(quote)) &&
                        f.QuotePhysicalWork(new(false, false, 4097) { PrivateInput = true }) is null &&
                        f.QuotePhysicalWork(new(false, false, 4097) { PrivateOutput = true }) is null),
                        fixture.Name + ":requested public CPU/GPU path and honest compatibility estimate or unknown price");
                    check(selected.OrderedFamilies.All(f => !gpu || mode != "IntegerRne" ||
                        (f.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) == 0),
                        fixture.Name + ":actual compatibility requirements");
                    var batch = new SearchBatch(0, 4097);
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, 4097)
                        .Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                    var actual = input;
                    // The production serial wrapper admits dense root windows;
                    // arbitrary compact fixtures exercise its public units.
                    var invocations = compact ? selected.ExecutionOrderUnits : selected.OrderedFamilies;
                    foreach (var family in invocations)
                        actual = await family.InvokeAsync(execution, new(batch, request), actual, default);
                    ulong[] expected = oracle.Where(i => !compact || i % 3 == 0).ToArray();
                    ulong[] actualOrdinals = actual.EnumerateLogicalOrdinals().ToArray();
                    check(actualOrdinals.SequenceEqual(expected), fixture.Name + ":default full CPU/GPU population:" + mode + ":" + compact);
                    ulong positive = oracle[0];
                    var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(profile, Beta110SeedCodec.FormatOrdinal(positive)));
                    check(exact.IsMatch, fixture.Name + ":original whole Exact positive:" + mode);
                    receipts.Add(new { fixture.Name, Mode = mode, gpu, compact, input = input.Count, survivors = actual.Count,
                        ExpectedAbi1 = expected, ActualAbi1 = actualOrdinals, WholeExactPositive = positive, WholeExactDisposition = exact.Evaluation.Disposition.ToString(),
                        Bodies = invocations.Select(f => new { f.FamilyId,
                            Revision = f.ResolveConditionPerformance(compact).PhysicalImplementationRevision,
                            Requirements = f.GpuNumericRequirements.ToString(), Device = f.CapturePerformanceObservation().DeviceName }).ToArray(),
                        coverage, oracle = "DefaultPlannerR/V/WOwnedBodies;FullCoreReferencePopulation;LegacyOrdinaryShopAndWorldPreserved" });
                }
                finally
                {
                    foreach (var family in selected.OrderedFamilies) await family.DisposeAsync(execution);
                }
            }
        }
    }

    private static async Task CheckCapturedPartyUnknown(string contextsRoot, string output, FamilyExecutionContext execution,
        List<object> receipts, Action<bool, string> check)
    {
        var profile = Beta111Profile.Instance;
        ulong Root(ulong ordinal) => profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
        bool Source(ulong root, int count)
        {
            // Real game RNG, preserving ordinary Roll's float additions/order.
            var rng = new Rng(root, "unknown_map_point");
            float monster = .1f, treasure = .02f, shop = .03f;
            for (int i = 0; i < count; i++)
            {
                float total = monster + treasure; total += shop;
                if (rng.NextFloat() <= total) return false;
                monster += .1f; treasure += .02f; shop += .03f;
            }
            return true;
        }
        ulong positive = Enumerable.Range(0, 1024).Select(i => (ulong)i).First(i => Source(Root(i), 3));
        ulong negative = Enumerable.Range(0, 1024).Select(i => (ulong)i).First(i => !Source(Root(i), 3));
        foreach (int count in new[] { 2, 3, 4 })
        {
            var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(contextsRoot, count + "p", "context.json"));
            var context = captured.Context;
            check(context.Party?.Players.Count == count, "captured party roster:" + count);
            ExactSearchExecutionRequest Request(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                new(Beta110SeedCodec.FormatOrdinal(positive), 32771, int.MaxValue, 4)).Plan ?? throw new InvalidDataException("PartyUnknownGpu.Compile");
            ProductionExactSearchResult Exact(ExactSearchExecutionRequest request, ulong ordinal) => ProductionExactSearchEvaluator.Evaluate(request,
                TrustedRootHashInput.FromCanonicalSeed(profile, Beta110SeedCodec.FormatOrdinal(ordinal)));
            var query = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1,
                Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                    Conditions = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = slot == count - 1 ? 3 : slot == 0 ? 2 : 0 } }).ToArray() };
            var request = Request(query);
            var plan = InitialRoomsPlan.TryCompile(request) ?? throw new InvalidDataException("PartyUnknownGpu.Admission");
            check(plan.UnknownPrefixCount == 3 && plan.CoversUnknownPrefix && !plan.CoversTreasure, "one shared max prefix:" + count);
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                ulong root = Root(ordinal); bool expected = Source(root, 3);
                check(plan.Matches(root) == expected && plan.Reference(root) == expected &&
                    Exact(request, ordinal).Evaluation.Disposition == (expected ? SearchDisposition.Match : SearchDisposition.NoMatch),
                    "full Exact/source-Rng/CPU shared prefix:" + count + ":" + ordinal);
            }
            var mixed = query with { Players = query.Players.Select(player => {
                var owner = context.Party!.Players[player.Slot];
                var offers = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(
                    Beta110SeedCodec.FormatOrdinal(positive), owner, profile, true).RelicKeys;
                var events = Beta111EventResultProjector.Project(Root(positive), player.Slot, Beta111EventResultAuthority.From(owner));
                return player with { Offers = new([], offers, []), Conditions = player.Conditions with {
                    EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, events.TrashHeapGrabCard)] } };
            }).ToArray() };
            var mixedResult = Exact(Request(mixed), positive);
            check(mixedResult.IsMatch && mixedResult.Evaluation.Evidence.Count(e => e.StreamDomain == "unknown_map_point") == 3 &&
                mixedResult.Evaluation.Evidence.Where(e => e.StreamDomain == "unknown_map_point").All(e => e.RouteId is null),
                "whole-table mixed offer/E positive without fictitious route:" + count);
            var independent = mixed with { FirstActUnknownEventPrefixCount = 0, Players = mixed.Players.Select(player =>
                player with { Conditions = player.Conditions with { FirstActUnknownEventPrefixCount = 0 } }).ToArray() };
            var independentResult = Exact(Request(independent), positive);
            var publishedEvidence = mixedResult.Evaluation.Evidence.Select(e => StablePartyMetadata(e)).ToHashSet(StringComparer.Ordinal);
            check(independentResult.IsMatch && independentResult.Evaluation.Evidence.All(e => publishedEvidence.Contains(StablePartyMetadata(e))) &&
                StablePartyMetadata(independentResult.Request) == StablePartyMetadata(mixedResult.Request) &&
                StablePartyMetadata(independentResult.Document) == StablePartyMetadata(mixedResult.Document) &&
                StablePartyMetadata(independentResult.Evaluation.Witnesses) == StablePartyMetadata(mixedResult.Evaluation.Witnesses),
                "published original domain metadata preserved except generated request ID:" + count);
            var treasure = query with { Players = query.Players.Select(player => player.Slot == 0 ? player with {
                Conditions = player.Conditions with { TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered,
                    [new ModelKey("RELIC", "JUZU_BRACELET"), null]) } } : player).ToArray() };
            check(Exact(Request(treasure), positive).Evaluation.Disposition == SearchDisposition.Unsupported &&
                Exact(Request(treasure), negative).Evaluation.Disposition == SearchDisposition.NoMatch,
                "disabled party chest independent of shared mismatch:" + count);
            var intervention = mixed with { Players = mixed.Players.Select(player => player.Slot == count - 1 ? player with {
                Conditions = player.Conditions with { OpeningRoute = new(player.Offers.All[0]) } } : player).ToArray() };
            var interventionRequest = Request(intervention);
            check(InitialRoomsPlan.TryCompile(interventionRequest) is null && Exact(interventionRequest, positive).Evaluation.Disposition == SearchDisposition.Unknown &&
                PartyInitialRooms.Evaluate(interventionRequest, Root(positive)).FailureCode == "FirstActUnknownEventPrefix.PartyOpeningInterventionUnresolved",
                "last seat opening leaves shared unknown unresolved:" + count);
            var contradiction = intervention with { Players = intervention.Players.Select(player => player.Slot == 0
                ? player with { Offers = new([], [], [player.Offers.All[0]]) } : player).ToArray() };
            var rejected = Exact(Request(contradiction), positive);
            check(rejected.Evaluation.Disposition == SearchDisposition.NoMatch && rejected.FailureCode.StartsWith("Party.InitialOffers", StringComparison.Ordinal),
                "original independent NoMatch cause wins over unknown:" + count);
            var population = Enumerable.Range(0, 32771).Where(i => Source(Root((ulong)i), 3)).Select(i => (ulong)i).ToArray();
            foreach (var variant in NumericalVariants)
            {
            var actualPlan = InitialRoomsPlan.TryCompile(request, numericalVariant: variant)
                ?? throw new InvalidDataException("PartyUnknownGpu.VariantAdmission:" + count + ":" + variant);
            ExportPlanSource(actualPlan, "PartyUnknown" + count, output, receipts, check);
            await execution.ExecuteGpuAsync(rd =>
            {
                using var gpu = new InitialRoomsGpuExecutor(rd, actualPlan);
                foreach (bool compact in new[] { false, true })
                {
                    var batch = new SearchBatch(0, 32771);
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch,
                        Enumerable.Range(0, 32771).Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                    var expected = population.Where(i => !compact || i % 3 == 0).ToArray();
                    var actual = gpu.Execute(input, default, out var metrics);
                    ulong[] actualOrdinals = actual.EnumerateLogicalOrdinals().ToArray();
                    check(actualOrdinals.SequenceEqual(expected), "captured party complete GPU/source population:" + count + ":" + variant + ":" + compact);
                    receipts.Add(new { Name = "CapturedPartySharedUnknown", Variant = variant.ToString(), actualPlan.ActualRevision,
                        Requirements = actualPlan.NumericRequirements.ToString(), Device = gpu.Device, players = count, compact, prefix = 3,
                        ExpectedAbi1 = expected, ActualAbi1 = actualOrdinals,
                        input = input.Count, survivors = actual.Count, sourceDigest = Convert.ToHexString(SHA256.HashData(
                            FamilyGpuComputeUtility.ToBytes(expected.Select(i => checked((uint)i)).ToArray()))), metrics,
                        oracle = "ActualCapturedRoster;FullGameRngOrdinaryFloatPopulation;First128CompleteExactAndCore;OneSharedStream" });
                }
                return true;
            }, default);
            }
            foreach (var mode in new[] { "CPU", "NativeFloat64", "IntegerRne" })
            foreach (bool compact in new[] { false, true })
            {
                bool gpu = mode != "CPU";
                var selected = FamilyExecutionCoordinator.Plan(request, gpu, gpuFloat64Available: mode != "IntegerRne");
                try
                {
                    check(selected.Stages.SelectMany(stage => stage.Coverage).Count(atom => atom == VariantOpeningFamily.Id) == 1 &&
                        selected.ExecutionOrderUnits.Count(family => family.FamilyId == VariantOpeningFamily.Id) == 1 &&
                        selected.ExecutionOrderUnits.All(family => family.FamilyId != "W.World") &&
                        selected.ExecutionOrderUnits.Single(family => family.FamilyId == VariantOpeningFamily.Id).ConditionPerformance.UsesGpu == gpu &&
                        selected.ExecutionOrderUnits.Select(family => family is FamilyCpuExecution cpu ? cpu.RealizationOwner ?? family : family)
                            .OfType<VariantOpeningFamily>().Single().Plan.UnknownPrefixCount == 3,
                        "captured party actual default one shared V owner:" + count + ":" + gpu);
                    var batch = new SearchBatch(0, 32771);
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, 32771)
                        .Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                    var actual = input;
                    var invocations = compact ? selected.ExecutionOrderUnits : selected.OrderedFamilies;
                    foreach (var family in invocations)
                        actual = await family.InvokeAsync(execution, new(batch, request), actual, default);
                    ulong[] expected = population.Where(i => !compact || i % 3 == 0).ToArray();
                    ulong[] actualOrdinals = actual.EnumerateLogicalOrdinals().ToArray();
                    check(actualOrdinals.SequenceEqual(expected),
                        "captured party actual default V full CPU/GPU population:" + count + ":" + mode + ":" + compact);
                    check(mode != "IntegerRne" || selected.OrderedFamilies.All(f =>
                        (f.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) == 0),
                        "captured party actual wrapper requirements:" + count);
                    receipts.Add(new { Name = "CapturedPartyDefaultSharedUnknown", players = count, Mode = mode, compact,
                        ExpectedAbi1 = expected, ActualAbi1 = actualOrdinals,
                        Bodies = invocations.Select(f => new { f.FamilyId,
                            Revision = f.ResolveConditionPerformance(compact).PhysicalImplementationRevision,
                            Requirements = f.GpuNumericRequirements.ToString(), Device = f.CapturePerformanceObservation().DeviceName }).ToArray(),
                        OriginalPositiveDisposition = Exact(request, positive).Evaluation.Disposition.ToString(),
                        TreasurePositiveDisposition = Exact(Request(treasure), positive).Evaluation.Disposition.ToString(),
                        TreasureNegativeDisposition = Exact(Request(treasure), negative).Evaluation.Disposition.ToString(),
                        InterventionDisposition = Exact(interventionRequest, positive).Evaluation.Disposition.ToString()
                    });
                }
                finally
                {
                    foreach (var family in selected.OrderedFamilies) await family.DisposeAsync(execution);
                }
            }
            await CheckMixedPartyPublic(Request(mixed), positive, count, execution, receipts, check);
            string folder = Path.Combine(output, "party-unknown", count + "p");
            Directory.CreateDirectory(folder);
            ProductionSearchReplay.Write(Path.Combine(folder, "context.json"), captured);
            ProductionSearchReplay.Write(Path.Combine(folder, "prefix-workload.json"), new ProductionSearchReplayWorkload(1, query, request.RunOptions));
            ProductionSearchReplay.Write(Path.Combine(folder, "mixed-workload.json"), new ProductionSearchReplayWorkload(1, mixed, request.RunOptions));
        }
        GD.Print("PARTY_UNKNOWN_GPU_PASS players=2,3,4;actualCaptured=true;nativeIntegerParity=true;FullPopulation=true;ExactAndOriginalMetadata=true;partyTreasure=Unsupported");
    }

    private static string StablePartyMetadata(object? value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value, ProductionSearchReplay.JsonOptions));
        void RemoveCorrelation(System.Text.Json.Nodes.JsonNode? current)
        {
            if (current is System.Text.Json.Nodes.JsonObject obj)
                foreach (string key in obj.Select(pair => pair.Key).ToArray())
                    if (key.Equals("RequestId", StringComparison.OrdinalIgnoreCase)) obj.Remove(key); else RemoveCorrelation(obj[key]);
            else if (current is System.Text.Json.Nodes.JsonArray array) foreach (var item in array) RemoveCorrelation(item);
        }
        RemoveCorrelation(node); return node?.ToJsonString() ?? "null";
    }

    private static async Task CheckMixedPartyPublic(ExactSearchExecutionRequest request, ulong positive, int players,
        FamilyExecutionContext execution, List<object> receipts, Action<bool, string> check)
    {
        // The CPU call is the complete same registered predicate, including all
        // requested seats and the shared prefix; it is not an injected outcome.
        foreach (bool compact in new[] { false, true })
        {
            var batch = new SearchBatch(0, 4099);
            var sparse = Enumerable.Range(0, 4099).Where(i => i % 3 == 0 || (ulong)i == positive).Select(i => (ulong)i).ToArray();
            var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, sparse) : FamilyCandidateSet.Dense(batch);
            ulong[]? cpuOrdinals = null;
            foreach (var mode in new[] { "CPU", "NativeFloat64", "IntegerRne" })
            {
                bool gpu = mode != "CPU";
                var selected = FamilyExecutionCoordinator.Plan(request, gpu, gpuFloat64Available: mode != "IntegerRne");
                try
                {
                    check(selected.Stages.SelectMany(stage => stage.Coverage).Count(atom => atom == VariantOpeningFamily.Id) == 1 &&
                        selected.ExecutionOrderUnits.Select(family => family is FamilyCpuExecution cpu ? cpu.RealizationOwner ?? family : family)
                            .OfType<VariantOpeningFamily>().Single().Plan.UnknownPrefixCount ==
                                PartyInitialRooms.Prefixes(request).Max(source => source.Count),
                        "mixed party owns shared prefix once:" + players + ":" + mode);
                    check(!gpu || selected.OrderedFamilies.All(f => f.ConditionPerformance.UsesGpu),
                        "mixed party actual all GPU selected:" + players + ":" + mode);
                    check(mode != "IntegerRne" || selected.OrderedFamilies.All(f =>
                        (f.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) == 0),
                        "mixed party actual compatible wrapper requirements:" + players);
                    var actual = input;
                    var invocations = compact ? selected.ExecutionOrderUnits : selected.OrderedFamilies;
                    foreach (var family in invocations)
                        actual = await family.InvokeAsync(execution, new(batch, request), actual, default);
                    ulong[] ordinals = actual.EnumerateLogicalOrdinals().ToArray();
                    cpuOrdinals ??= ordinals;
                    check(cpuOrdinals.SequenceEqual(ordinals) && ordinals.Contains(positive),
                        "mixed party complete ordered ABI1 and whole Exact witness retained:" + players + ":" + mode + ":" + compact);
                    var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(
                        Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(positive)));
                    check(exact.IsMatch, "mixed party original whole Exact positive:" + players);
                    receipts.Add(new { Name = "CapturedPartyMixedPublic", players, Mode = mode, compact,
                        ExpectedAbi1 = cpuOrdinals, ActualAbi1 = ordinals, InputCount = input.Count, WholeExactPositive = positive,
                        WholeExactDisposition = exact.Evaluation.Disposition.ToString(),
                        QueryFingerprint = request.CompiledSearch.SemanticFingerprint,
                        Bodies = invocations.Select(f => new { f.FamilyId, Coverage = f.Coverage.ToArray(),
                            Revision = f.ResolveConditionPerformance(compact).PhysicalImplementationRevision,
                            Requirements = f.GpuNumericRequirements.ToString(), Device = f.CapturePerformanceObservation().DeviceName }).ToArray(),
                        Scope = "SameActualCapturedPartyOriginalQuery;OffersAndEventResultsAndSharedOrdinaryUnknown;NoOpeningIntervention"
                    });
                }
                finally { foreach (var family in selected.OrderedFamilies) await family.DisposeAsync(execution); }
            }
        }
    }

    private static void ExportPlanSource(InitialRoomsPlan plan, string label, string output,
        List<object> receipts, Action<bool, string> check)
    {
        string source = plan.ShaderSource();
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string numerical = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..donor.IndexOf("bool evaluate_event_results", StringComparison.Ordinal)];
        string priorNative = FamilyGpuComputeUtility.LoadEmbeddedShader("InitialRoomsFamily.comp.glsl").Replace("/*__RNG_HASH__*/", numerical, StringComparison.Ordinal);
        bool native = plan.NumericalVariant == GpuNumericalVariant.NativeFloat64;
        check(native ? source == priorNative : plan.NumericRequirements == GpuNumericRequirements.Int64,
            label + ":native byte source identity or actual compatible requirements");
        if (!native) GpuIntegerNumerics.RequireNoFloat64(source, "InitialRooms.Check");
        string file = label + "." + plan.NumericalVariant + ".glsl";
        File.WriteAllText(Path.Combine(output, file), source);
        receipts.Add(new { Name = "InitialRoomsActualSource", Label = label, Variant = plan.NumericalVariant.ToString(),
            plan.ActualRevision, Requirements = plan.NumericRequirements.ToString(), SourceFile = file,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))),
            PriorNativeSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(priorNative))),
            NativeMatchesCapturedMainAssembler = native ? (bool?)(source == priorNative) : null,
            PriorAssemblerScope = "main41e952cb-plus-WIP captured InitialRooms assembler expression; same production resources",
            PricingBodyRevision = plan.DescribePricingShape().BodyRevision });
    }

    private static void WriteCompleteAbi1(string path, IEnumerable<ulong> ordinals)
    {
        using var writer = new BinaryWriter(File.Create(path));
        foreach (ulong ordinal in ordinals) writer.Write(ordinal);
    }

    // This receipt specifically owns the K2 group with different slot counts;
    // the separate Wide population remains a three-window transport check.
    private static async Task CheckTwoWindowHeaderBank(FamilyExecutionContext execution, ExactSearchExecutionRequest request,
        string output, List<object> receipts, Action<bool, string> check)
    {
        const int count = InitialRoomsPlan.Capacity + 67;
        const ulong batchBase = (1UL << 32) + 701;
        var cpu = InitialRoomsPlan.TryCompile(request) ?? throw new InvalidDataException("InitialRooms.K2Admission");
        var seed = new byte[Beta110SeedCodec.SeedLength];
        foreach (bool compact in new[] { false, true })
        {
            var batch = new SearchBatch(batchBase, compact ? checked(count * 2 + 3) : count);
            var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch,
                Enumerable.Range(0, count).Select(index => (ulong)index * 2 + 1).ToArray()) : FamilyCandidateSet.Dense(batch);
            var matches = new List<ulong>(count);
            foreach (ulong ordinal in input.EnumerateLogicalOrdinals())
            {
                Beta110SeedCodec.WriteOrdinal(batch.GlobalCandidate(ordinal), seed);
                if (cpu.Matches(XxHash64.Hash(seed, 0))) matches.Add(ordinal);
            }
            ulong[] expected = matches.ToArray();
            ulong secondSlotFirst = compact ? InitialRoomsPlan.Capacity * 2UL + 1 : InitialRoomsPlan.Capacity;
            check(input.Count == count && expected.Length > 0 && expected.Any(ordinal => ordinal >= secondSlotFirst),
                "K2 Unknown1 has positive complete CPU population including second-slot tail:" + compact);
            string prefix = "Unknown1K2HeaderBank." + (compact ? "compact" : "dense");
            string expectedFile = prefix + ".expected.abi1.bin";
            WriteCompleteAbi1(Path.Combine(output, expectedFile), expected);
            string expectedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, expectedFile))));
            foreach (var variant in NumericalVariants)
            {
                var plan = InitialRoomsPlan.TryCompile(request, numericalVariant: variant)
                    ?? throw new InvalidDataException("InitialRooms.K2VariantAdmission:" + variant);
                await execution.ExecuteGpuAsync(rd =>
                {
                    using var gpu = new InitialRoomsGpuExecutor(rd, plan);
                    var actual = gpu.Execute(input, default, out var metrics);
                    ulong[] ordinals = actual.EnumerateLogicalOrdinals().ToArray();
                    check(actual.Batch == batch && ordinals.SequenceEqual(expected),
                        "K2 Unknown1 complete ordered ABI1 CPU oracle:" + variant + ":" + compact);
                    check(metrics.Dispatches == 2 && metrics.SubmissionGroups == 1 && metrics.HeaderReadCalls == 1,
                        "K2 Unknown1 two dispatches share one submit and one header-bank read:" + variant + ":" + compact);
                    long headerBytes = metrics.ReadbackBytes - actual.Count * sizeof(uint);
                    check(headerBytes == 64, "K2 Unknown1 validates two 32-byte slot headers:" + variant + ":" + compact);
                    string actualFile = prefix + "." + variant + ".actual.abi1.bin";
                    WriteCompleteAbi1(Path.Combine(output, actualFile), ordinals);
                    string actualDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, actualFile))));
                    check(actualDigest == expectedDigest, "K2 Unknown1 complete ABI1 file digest:" + variant + ":" + compact);
                    receipts.Add(new { Name = "Unknown1K2HeaderBankTwoWindows", Variant = variant.ToString(), plan.ActualRevision,
                        Requirements = plan.NumericRequirements.ToString(), Device = gpu.Device, compact, batch.BatchBase, batch.BatchCandidateCount,
                        input = input.Count, survivors = actual.Count, FirstSlotInput = InitialRoomsPlan.Capacity, SecondSlotInput = 67,
                        metrics.Dispatches, metrics.SubmissionGroups, metrics.HeaderReadCalls, HeaderReadBytes = headerBytes, HeaderSlotsValidated = 2,
                        ExpectedAbi1File = expectedFile, ActualAbi1File = actualFile, Abi1Encoding = "LittleEndianUInt64OrderedLogicalOrdinals",
                        expectedDigest, actualDigest, metrics,
                        oracle = "FullIndependentCpuPlanMatchesPerOriginalRoot;Unknown1SmallCoreOracleReused;CapacityPlus67;CompactOddOriginalOrdinals;TwoHeadersOneBankRead" });
                    return true;
                }, CancellationToken.None);
            }
        }
        GD.Print("INITIAL_ROOMS_K2_HEADER_BANK_PASS input=Capacity+67;dispatches=2;submissionGroups=1;headerReadCalls=1;headerReadBytes=64;headerSlotsValidated=2;denseCompact=true;nativeInteger=true;fullCpuAbi1Parity=true");
    }

    // One high-survival gate crosses both the input and survivor windows. The
    // existing small fixtures own game/source parity; this owns transport bounds.
    private static async Task CheckWideUnknownPopulation(FamilyExecutionContext execution, ExactSearchExecutionRequest request, string output,
        List<object> receipts, Action<bool, string> check)
    {
        const int population = InitialRoomsPlan.Capacity * 2 + 65;
        const ulong batchBase = (1UL << 32) + 17;
        string Digest(FamilyCandidateSet values) => Convert.ToHexString(SHA256.HashData(
            FamilyGpuComputeUtility.ToBytes(values.EnumerateLogicalOrdinals().Select(i => checked((uint)i)).ToArray())));
        var seed = new byte[Beta110SeedCodec.SeedLength];
        var plan = InitialRoomsPlan.TryCompile(request) ?? throw new InvalidDataException("InitialRooms.WideAdmission");
        foreach (bool compact in new[] { false, true })
        {
            var batch = new SearchBatch(batchBase, compact ? (1 << 24) : population);
            ulong[] ordinals = compact ? Enumerable.Range(0, population)
                .Select(i => (ulong)i * (1 << 24) / (uint)population).ToArray() : [];
            var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, ordinals) : FamilyCandidateSet.Dense(batch);
            var matches = new List<ulong>(population);
            foreach (ulong ordinal in input.EnumerateLogicalOrdinals())
            {
                Beta110SeedCodec.WriteOrdinal(batch.GlobalCandidate(ordinal), seed);
                if (plan.Matches(XxHash64.Hash(seed, 0))) matches.Add(ordinal);
            }
            var expected = FamilyCandidateSet.FromSortedAbi1(batch, matches.ToArray());
            string expectedDigest = Digest(expected);
            check(input.Count == population && expected.Count > InitialRoomsPlan.Capacity,
                "wide Unknown1 crosses input and survivor capacity:" + compact);
            string expectedFile = "Unknown1Wide." + (compact ? "compact" : "dense") + ".expected.abi1.bin";
            WriteCompleteAbi1(Path.Combine(output, expectedFile), expected.EnumerateLogicalOrdinals());
            foreach (var variant in NumericalVariants)
            {
            var actualPlan = InitialRoomsPlan.TryCompile(request, numericalVariant: variant)
                ?? throw new InvalidDataException("InitialRooms.WideVariantAdmission");
            await execution.ExecuteGpuAsync(rd =>
            {
                using var gpu = new InitialRoomsGpuExecutor(rd, actualPlan);
                var actual = gpu.Execute(input, default, out var publicMetrics);
                string publicDigest = Digest(actual);
                check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected.EnumerateLogicalOrdinals()) &&
                    actual.Count == expected.Count && publicDigest == expectedDigest, "wide Unknown1 public full population:" + compact);
                check(publicMetrics.Dispatches == 3, "wide Unknown1 public three windows:" + compact);
                string actualFile = "Unknown1Wide." + (compact ? "compact" : "dense") + "." + variant + ".actual.abi1.bin";
                WriteCompleteAbi1(Path.Combine(output, actualFile), actual.EnumerateLogicalOrdinals());
                receipts.Add(new { Name = "Unknown1WidePublicTransport", Variant = variant.ToString(), actualPlan.ActualRevision,
                    Requirements = actualPlan.NumericRequirements.ToString(), Device = gpu.Device, ExpectedAbi1File = expectedFile,
                    ActualAbi1File = actualFile, Abi1Encoding = "LittleEndianUInt64OrderedLogicalOrdinals", compact, batch.BatchBase, batch.BatchCandidateCount,
                    input = input.Count, survivors = expected.Count, expectedDigest, publicDigest, publicMetrics,
                    oracle = "FullNumericPopulation;SmallGameSourceOracleReused;TwoCapacityPlus65;HighWordBatchBase" });
                return true;
            }, default);
            }
        }
    }
}
