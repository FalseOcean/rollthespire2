using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class InitialRoomNumericalChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidDataException("InitialRoomNumerical:" + name); }
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var profile = Beta111Profile.Instance;
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext context) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new("000000000000", 65536, 1000, 1)).Plan
            ?? throw new InvalidDataException("InitialRoomNumerical.Compile");
        for (ulong ordinal = 0; ordinal < 4096; ordinal++)
        {
            ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            for (int count = 1; count <= 3; count++)
                Check(InitialRoomsPlan.MatchesUnknown(root, count) == OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, false)
                    .Steps.All(step => step.Room == UnknownRoomCategory.Event), "UnknownFloatPrefix:" + count);
        }
        var world = captured.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        Beta109RelicBucketSnapshot Bucket(Beta109RelicBucketSnapshot source, params ModelKey[] keys) => source with {
            OrderedRelics = keys, OrderedEntries = keys.Select(key => new Beta109RelicBucketEntrySnapshot(key, true, true)).ToArray() };
        var common = generation.SharedRelicBuckets.Single(b => b.BucketId.EndsWith(":Common", StringComparison.Ordinal));
        var uncommon = generation.SharedRelicBuckets.Single(b => b.BucketId.EndsWith(":Uncommon", StringComparison.Ordinal));
        var rare = generation.SharedRelicBuckets.Single(b => b.BucketId.EndsWith(":Rare", StringComparison.Ordinal));
        var artificial = generation.SharedRelicBuckets.Select(bucket => bucket == common
            ? Bucket(bucket, new("RELIC", "MASSIVE_SCROLL"), common.OrderedRelics[0], common.OrderedRelics[1])
            : bucket == uncommon ? Bucket(bucket) : bucket == rare ? Bucket(bucket) : bucket).ToArray();
        var exhausted = generation.SharedRelicBuckets.Select(bucket => bucket == common || bucket == uncommon || bucket == rare
            ? Bucket(bucket) : bucket).ToArray();
        (string Name, SearchContext Context)[] contexts = [ ("Captured", captured),
            ("ArtificialRemovedAndEmptyLanes", captured with { Authority = captured.Authority.WithWorldAuthority(world with {
                Beta109Generation = generation with { SharedRelicBuckets = artificial } }) }),
            ("ArtificialExhaustedCirclet", captured with { Authority = captured.Authority.WithWorldAuthority(world with {
                Beta109Generation = generation with { SharedRelicBuckets = exhausted } }) }) ];
        foreach (var fixture in contexts)
        {
            var bags = RelicSequencePredictor.Predict(profile, "000000000000", fixture.Context.Authority.WorldAuthority);
            var observed = TreasureRelicPredictor.Predict(profile, profile.ComputeRootSeed("000000000000"), bags, []);
            ModelKey first = observed.Entries[0].RelicKey!.Value, second = observed.Entries[1].RelicKey!.Value;
            TreasureRelicSearchCondition[] conditions = [
                new TreasureRelicSearchCondition(1, 2, TreasureRelicOrderMode.Ordered, [first, second]),
                new(1, 2, TreasureRelicOrderMode.Ordered, [second, first]),
                new(1, 2, TreasureRelicOrderMode.Unordered, [second, first]),
                new(1, 2, TreasureRelicOrderMode.Unordered, [first, first]),
                new(1, 2, TreasureRelicOrderMode.Ordered, [null, second]),
                new(2, 1, TreasureRelicOrderMode.Unordered, [second, first]),
                new(1, 1, TreasureRelicOrderMode.Ordered, [new("RELIC", "MASSIVE_SCROLL"), null]),
                new(1, 1, TreasureRelicOrderMode.Ordered, [new("RELIC", "CIRCLET"), null]) ];
            foreach (var condition in conditions)
            foreach (int prefix in new[] { 0, 3 })
            {
                var request = Request(SearchQuery.Empty with { TreasureRelics = condition, FirstActUnknownEventPrefixCount = prefix }, fixture.Context);
                var plan = InitialRoomsPlan.TryCompile(request);
                Check(plan is { CoversTreasure: true }, fixture.Name + ":TreasureAdmission");
                for (ulong ordinal = 0; ordinal < 256; ordinal++)
                {
                    ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                    Check(plan!.Matches(root) == plan.Reference(root), fixture.Name + ":CompleteReference:" + ordinal);
                }
            }
        }
        var unknownOnly = InitialRoomsPlan.TryCompile(Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 },
            captured with { Authority = captured.Authority.WithWorldAuthority(null) }));
        Check(unknownOnly is { CoversUnknownPrefix: true, CoversTreasure: false }, "UnknownIndependentOfMissingBags");
        var retainedUnknown = InitialRoomsPlan.TryCompile(Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3,
            TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered, [common.OrderedRelics[0], null]) },
            captured with { Authority = captured.Authority.WithWorldAuthority(null) }));
        Check(retainedUnknown is { CoversUnknownPrefix: true, CoversTreasure: false } && retainedUnknown.TreasureRemainder.Length > 0,
            "MissingTreasureAuthorityKeepsUnknownAndOriginalObligation");
        var committed = Request(SearchQuery.Empty with { OpeningRoute = new(new("RELIC", "JUZU_BRACELET")),
            FirstActUnknownEventPrefixCount = 3 }, captured);
        Check(InitialRoomsPlan.TryCompile(committed) is null, "CommittedOpeningAndJuzuRemainExact");
        CheckOwnedInvocations(captured, Check).GetAwaiter().GetResult();
        CheckPartyUnknown(captured, Check);
        Console.WriteLine($"InitialRoomNumerical checks={checks};PASS"); return 0;
    }

    private static async Task CheckOwnedInvocations(SearchContext captured, Action<bool, string> check)
    {
        var profile = Beta111Profile.Instance;
        ulong Root(ulong ordinal) => profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
        ulong witness = Enumerable.Range(0, 1024).Where(i => i % 3 == 0).Select(i => (ulong)i)
            .First(i => InitialRoomsPlan.MatchesUnknown(Root(i), 3));
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? context = null) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context ?? captured),
                new("000000000000", 1024, int.MaxValue, 4)).Plan ?? throw new InvalidDataException("OwnedInitialRooms.Compile");
        var initial = RelicSequencePredictor.Predict(profile, Beta110SeedCodec.FormatOrdinal(witness), captured.Authority.WorldAuthority);
        var observed = TreasureRelicPredictor.Predict(profile, Root(witness), initial, []);
        var treasure = new TreasureRelicSearchCondition(1, 2, TreasureRelicOrderMode.Ordered,
            [observed.Entries[0].RelicKey, observed.Entries[1].RelicKey]);
        var compoundTreasure = new TreasureRelicSearchCondition(1, 1, TreasureRelicOrderMode.Ordered,
            [observed.Entries[0].RelicKey, null]);
        var ordinary = SearchQuery.Empty with { RelicSequenceConstraints = [new(RelicSequenceKind.Common,
            SearchSequenceRangeMode.FirstN, 3, new([initial.Lanes.Single(lane => lane.Kind == RelicSequenceKind.Common).Entries[0].RelicKey], [], []))] };
        var shop = SearchQuery.Empty with { RelicShopSequenceConditions = [new(1, CombatRewardSequenceOrderMode.Ordered,
            [initial.Lanes.Single(lane => lane.Kind == RelicSequenceKind.Shop).Entries[0].RelicKey])] };
        var worldSource = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/family-pricing-matrix-v1/W-K4.json").Query;
        var world = SearchQuery.Empty with { EventSequenceConstraints = [worldSource.EventSequenceConstraints.First(condition => condition.Act == 1)] };
        var fixtures = new (string Name, SearchQuery Query, SearchContext Context)[] {
            ("TreasureOnly", SearchQuery.Empty with { TreasureRelics = treasure }, captured),
            ("TreasureOrdinaryAndShop", ordinary with { TreasureRelics = compoundTreasure,
                RelicShopSequenceConditions = shop.RelicShopSequenceConditions }, captured),
            ("TreasureMissingOrdinary", ordinary with { TreasureRelics = treasure,
                RelicSequenceConstraints = ordinary.RelicSequenceConstraints.Select(condition => condition with {
                    RangeMode = SearchSequenceRangeMode.ExactSlot, RangeValue = 255 }).ToArray() }, captured),
            ("UnknownOnly", SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 }, captured),
            ("UnknownExistingWorld", world with { FirstActUnknownEventPrefixCount = 3 }, captured),
            ("UnknownMissingWorld", world with { FirstActUnknownEventPrefixCount = 3 },
                captured with { Authority = captured.Authority.WithWorldAuthority(null) }),
            ("BothOwners", SearchQuery.Empty with { TreasureRelics = treasure, FirstActUnknownEventPrefixCount = 3 }, captured)
        };
        await using var execution = new FamilyExecutionContext(requiresGpu: false, cpuWorkerCount: 4);
        foreach (var fixture in fixtures)
        {
            var request = Request(fixture.Query, fixture.Context);
            var selected = FamilyExecutionCoordinator.Plan(request, false);
            var cover = selected.Stages.SelectMany(stage => stage.Coverage).ToHashSet(StringComparer.Ordinal);
            bool hasTreasure = fixture.Query.TreasureRelics is { IsEmpty: false };
            bool hasUnknown = fixture.Query.FirstActUnknownEventPrefixCount > 0;
            check(cover.Contains(InitialRoomsInvocation.TreasureCoverage) == hasTreasure &&
                cover.Contains(InitialRoomsInvocation.UnknownCoverage) == hasUnknown, fixture.Name + ":DefaultRegistrationCoverage");
            check(selected.OrderedFamilies.All(family => !family.FamilyId.StartsWith("I.", StringComparison.Ordinal)), fixture.Name + ":NoIndependentIFamily");
            if (fixture.Name == "UnknownMissingWorld")
                check(!cover.Contains("W.World") && selected.FilterRemainders.Contains("W.World:NoSelectedFilter"),
                    "MissingWorldDoesNotBorrowLegacyCoverage");
            if (fixture.Name == "TreasureMissingOrdinary")
                check(!cover.Contains("R.Relic") && selected.FilterRemainders.Contains("R.Relic:NoSelectedFilter"),
                    "MissingOrdinaryDoesNotBorrowLegacyCoverage");
            foreach (bool compact in new[] { false, true })
            {
                var batch = new SearchBatch(0, 1024);
                var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, 1024)
                    .Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                var current = input;
                foreach (var family in selected.OrderedFamilies)
                {
                    if (family is InitialRoomsInvocation)
                        check(!family.CanBindPrivateSerial &&
                            family.QuotePhysicalWork(new(compact, false, input.Count) { PrivateInput = true }) is null &&
                            family.QuotePhysicalWork(new(compact, false, input.Count) { PrivateOutput = true }) is null,
                            fixture.Name + ":InitialRoomsPublicCompleteCallsCannotBorrowPrivatePrice");
                    current = await family.InvokeAsync(execution, new(batch, request), current, default);
                }
                InitialRoomsPlan? rooms = InitialRoomsPlan.TryCompile(request);
                RelicFamily.TryCreate(Request(fixture.Query with { TreasureRelics = null }, fixture.Context), out var legacyR);
                WorldFamily.TryCreate(Request(fixture.Query with { FirstActUnknownEventPrefixCount = 0 }, fixture.Context), out var legacyW, false);
                var expected = input.EnumerateLogicalOrdinals().Where(i => (rooms?.Reference(Root(i)) ?? true) &&
                    (legacyR is not RelicFamily relic || relic.MatchesCpuReference(Root(i))) &&
                    (legacyW is not WorldFamily worldFamily || worldFamily.Replay.Matches(Root(i))));
                var expectedOrdinals = expected.ToArray();
                check(current.EnumerateLogicalOrdinals().SequenceEqual(expectedOrdinals), fixture.Name + ":CompleteCpuPopulation:" + compact);
                if (fixture.Name == "TreasureOrdinaryAndShop")
                {
                    check(expectedOrdinals.Contains(witness), "MixedOrdinaryShopTreasureHasKnownPositive");
                    var old = (RelicFamily)legacyR!;
                    check(Enumerable.Range(0, 4097).Select(i => (ulong)i).Any(i => old.MatchesCpuReference(Root(i)) && !rooms!.Reference(Root(i))),
                        "MixedTreasureGateActuallyRejectsLegacySurvivors");
                    check(Enumerable.Range(0, 4097).Select(i => (ulong)i).Any(i => rooms!.Reference(Root(i)) && !old.MatchesCpuReference(Root(i))),
                        "MixedOrdinaryShopGateActuallyRejectsTreasureSurvivors");
                }
                if (fixture.Name == "UnknownExistingWorld")
                    check(expectedOrdinals.Length > 0, "MixedWorldUnknownHasPositivePopulation");
            }
            foreach (var family in selected.OrderedFamilies) await family.DisposeAsync(execution);
        }
    }

    private static void CheckPartyUnknown(SearchContext captured, Action<bool, string> check)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var contextFactory = typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!;
        var profile = Beta111Profile.Instance;
        ulong positive = Enumerable.Range(0, 1024).Select(i => (ulong)i).First(i => InitialRoomsPlan.MatchesUnknown(
            profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i)), 3));
        ulong negative = Enumerable.Range(0, 1024).Select(i => (ulong)i).First(i => !InitialRoomsPlan.MatchesUnknown(
            profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i)), 3));
        foreach (int count in new[] { 2, 3, 4 })
        {
            var context = (SearchContext)contextFactory.Invoke(null, [captured, count, null, null])!;
            ExactSearchExecutionRequest Request(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                new(Beta110SeedCodec.FormatOrdinal(positive), 1, 1, 1)).Plan ?? throw new InvalidDataException("PartyUnknownFixture.Compile");
            ProductionExactSearchResult Exact(ExactSearchExecutionRequest request, ulong ordinal) => ProductionExactSearchEvaluator.Evaluate(request,
                TrustedRootHashInput.FromCanonicalSeed(profile, Beta110SeedCodec.FormatOrdinal(ordinal)));
            var query = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1,
                Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                    Conditions = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = slot == count - 1 ? 3 : slot == 0 ? 2 : 0 } }).ToArray() };
            var request = Request(query);
            string originalFingerprint = request.SnapshotFingerprint;
            string originalQuery = System.Text.Json.JsonSerializer.Serialize(request.CompiledSearch.Query, ProductionSearchReplay.JsonOptions);
            var plan = InitialRoomsPlan.TryCompile(request);
            check(plan is { UnknownPrefixCount: 3, CoversTreasure: false }, "PartySharedMaximum:" + count);
            var selected = FamilyExecutionCoordinator.Plan(request, false);
            check(selected.Stages.SelectMany(stage => stage.Coverage).Count(atom => atom == InitialRoomsInvocation.UnknownCoverage) == 1 &&
                selected.OrderedFamilies.Count(family => family.FamilyId == "W.World") == 1,
                "PartyDefaultOneWOwnsWholeTableMaximum:" + count);
            var execution = new FamilyExecutionContext(requiresGpu: false, cpuWorkerCount: request.WorkerCount);
            try
            {
                var batch = new SearchBatch(0, 128);
                var actual = FamilyCandidateSet.Dense(batch);
                foreach (var family in selected.OrderedFamilies)
                    actual = family.InvokeAsync(execution, new(batch, request), actual, default).GetAwaiter().GetResult();
                var expected = Enumerable.Range(0, 128).Select(i => (ulong)i).Where(i => InitialRoomsPlan.MatchesUnknown(
                    profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i)), 3));
                check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "PartyActualWPublicCpuPopulation:" + count);
                foreach (var family in selected.OrderedFamilies) family.DisposeAsync(execution).GetAwaiter().GetResult();
            }
            finally { execution.DisposeAsync().GetAwaiter().GetResult(); }
            check(ReferenceEquals(PartyInitialRooms.IndependentDomains(request), PartyInitialRooms.IndependentDomains(request)),
                "IndependentProjectionReusedWithoutRootOrRngState:" + count);
            var result = Exact(request, positive);
            check(result.IsMatch && result.Evaluation.Evidence.Count(e => e.StreamDomain == "unknown_map_point") == 3,
                "PartySharedExactPositiveKeepsThreeAuthoredSources:" + count);
            check(result.Evaluation.Evidence.Where(e => e.StreamDomain == "unknown_map_point").All(e =>
                e.RouteId is null && e.AcquisitionOrder is null && e.AuthorityFingerprint == context.Party!.Fingerprint),
                "PartySharedObservationDoesNotInventRouteOrSeatRng:" + count);
            check(result.Evaluation.Witnesses.Count == 0 && result.Evaluation.MatchedRouteIds.Count == 0,
                "PartyUnknownAloneHasNoOpeningWitness:" + count);
            check(request.SnapshotFingerprint == originalFingerprint && originalQuery == System.Text.Json.JsonSerializer.Serialize(
                request.CompiledSearch.Query, ProductionSearchReplay.JsonOptions) && ReferenceEquals(result.Authority, request.Authority),
                "InternalDomainProjectionPreservesOriginalIdentity:" + count);
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                bool expected = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, 3, false).Steps.All(step => step.Room == UnknownRoomCategory.Event);
                check(plan!.Matches(root) == expected && plan.Reference(root) == expected &&
                    Exact(request, ordinal).Evaluation.Disposition == (expected ? SearchDisposition.Match : SearchDisposition.NoMatch),
                    "PartyExactNumericSameSharedPopulation:" + count + ":" + ordinal);
            }
            var treasure = query with { Players = query.Players.Select(player => player.Slot == 0 ? player with {
                Conditions = player.Conditions with { TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered,
                    [new ModelKey("RELIC", "JUZU_BRACELET"), null]) } } : player).ToArray() };
            var treasureRequest = Request(treasure);
            check(Exact(treasureRequest, positive).Evaluation.Disposition == SearchDisposition.Unsupported &&
                Exact(treasureRequest, negative).Evaluation.Disposition == SearchDisposition.NoMatch,
                "DisabledPartyTreasureCannotHideIndependentUnknownMismatch:" + count);
            check(InitialRoomsPlan.TryCompile(treasureRequest) is { CoversUnknownPrefix: true, CoversTreasure: false },
                "PartyTreasureRemainderDoesNotBlockUnknownCpuGpu:" + count);
            string seed = Beta110SeedCodec.FormatOrdinal(positive); ulong rootPositive = profile.ComputeRootSeed(seed);
            var mixed = query with { Players = query.Players.Select(player => {
                var owner = context.Party!.Players[player.Slot];
                var offers = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(seed, owner, profile, true).RelicKeys;
                var events = Beta111EventResultProjector.Project(rootPositive, player.Slot, Beta111EventResultAuthority.From(owner));
                return player with { Offers = new([], offers, []), Conditions = player.Conditions with {
                    EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, events.TrashHeapGrabCard)] } };
            }).ToArray() };
            var mixedRequest = Request(mixed);
            var mixedResult = Exact(mixedRequest, positive);
            var baselineQuery = mixed with { FirstActUnknownEventPrefixCount = 0, Players = mixed.Players.Select(player =>
                player with { Conditions = player.Conditions with { FirstActUnknownEventPrefixCount = 0 } }).ToArray() };
            var baseline = Exact(Request(baselineQuery), positive);
            var mixedEvidence = mixedResult.Evaluation.Evidence.Select(item => StableMetadata(item)).ToHashSet(StringComparer.Ordinal);
            check(mixedResult.IsMatch && baseline.IsMatch && baseline.Evaluation.Evidence.All(item => mixedEvidence.Contains(StableMetadata(item))),
                "MixedOffersAndEKeepExistingEvidence:" + count);
            check(StableMetadata(mixedResult.Document) == StableMetadata(baseline.Document) &&
                StableMetadata(mixedResult.Request) == StableMetadata(baseline.Request) &&
                StableMetadata(mixedResult.Evaluation.Witnesses) == StableMetadata(baseline.Evaluation.Witnesses),
                "PublishedMixedMetadataUnchangedExceptGeneratedRequestId:" + count);
            var intervention = mixed with { Players = mixed.Players.Select(player => player.Slot == count - 1 ? player with {
                Conditions = player.Conditions with { OpeningRoute = new(player.Offers.All[0]) } } : player).ToArray() };
            var interventionRequest = Request(intervention);
            check(InitialRoomsPlan.TryCompile(interventionRequest) is null &&
                Exact(interventionRequest, positive).Evaluation.Disposition == SearchDisposition.Unknown &&
                PartyInitialRooms.Evaluate(interventionRequest, rootPositive).FailureCode == "FirstActUnknownEventPrefix.PartyOpeningInterventionUnresolved",
                "LastSeatOpeningIsSharedUnknownIntervention:" + count);
            var juzu = query with { Players = query.Players.Select(player => player.Slot == count - 1 ? player with {
                Conditions = player.Conditions with { LegacyNeow = player.Conditions.LegacyNeow with {
                    CapsuleContainedRelics = new([new ModelKey("RELIC", "JUZU_BRACELET")], [], []) } } } : player).ToArray() };
            check(InitialRoomsPlan.TryCompile(Request(juzu)) is null, "LastSeatJuzuAcquisitionNotIgnored:" + count);
            var contradicted = intervention with { Players = intervention.Players.Select(player => player.Slot == 0
                ? player with { Offers = new([], [], [player.Offers.All[0]]) } : player).ToArray() };
            var contradiction = Exact(Request(contradicted), positive);
            check(contradiction.Evaluation.Disposition == SearchDisposition.NoMatch && contradiction.FailureCode.StartsWith("Party.InitialOffers", StringComparison.Ordinal),
                "PreexistingIndependentMismatchKeepsOriginalCauseBeforeSharedUnknown:" + count);
        }
    }

    // Request IDs are freshly generated correlation, not query/domain identity.
    // Every other published request/document/evidence field participates.
    internal static string StableMetadata(object? value)
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
}
