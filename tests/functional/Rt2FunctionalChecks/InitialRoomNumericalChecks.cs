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
        PreparedSearch Request(SearchQuery query, SearchContext context)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context), new("000000000000", 65536, 1000, 1));
            return result.Plan ?? throw new InvalidDataException("InitialRoomNumerical.Compile:" + result.Issue);
        }
        for (ulong ordinal = 0; ordinal < 4096; ordinal++)
        {
            ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            for (int count = 1; count <= 3; count++)
            foreach (bool juzu in new[] { false, true })
                Check(InitialRoomsPlan.MatchesUnknown(root, count, juzu) == OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, juzu)
                    .Steps.All(step => step.Room == UnknownRoomCategory.Event), "UnknownFloatPrefix:" + count + ":Juzu=" + juzu);
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
                var plan = InitialRoomsPlan.TryCompile(request.Model);
                Check(plan is { CoversTreasure: true }, fixture.Name + ":TreasureAdmission");
                for (ulong ordinal = 0; ordinal < 256; ordinal++)
                {
                    ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                    Check(plan!.Matches(root) == plan.Reference(root), fixture.Name + ":CompleteReference:" + ordinal);
                }
            }
        }
        var unknownOnly = InitialRoomsPlan.TryCompile(Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 },
            captured with { Authority = captured.Authority.WithWorldAuthority(null) }).Model);
        Check(unknownOnly is { CoversUnknownPrefix: true, CoversTreasure: false }, "UnknownIndependentOfMissingBags");
        var retainedUnknown = InitialRoomsPlan.TryCompile(Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3,
            TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered, [common.OrderedRelics[0], null]) },
            captured with { Authority = captured.Authority.WithWorldAuthority(null) }).Model);
        Check(retainedUnknown is { CoversUnknownPrefix: true, CoversTreasure: false } && retainedUnknown.TreasureRemainder.Length > 0,
            "MissingTreasureAuthorityKeepsUnknownAndOriginalObligation");
        ModelKey juzuKey = new("RELIC", "JUZU_BRACELET");
        var prefixQuery = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 };
        var capsule = prefixQuery with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule) };
        var declaredCases = new (string Name, SearchQuery Query, bool Juzu)[] {
            ("Direct", prefixQuery with { OpeningRoute = new(juzuKey) }, true),
            ("BonesUnspecified", prefixQuery with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) }, false),
            ("SmallCapsuleUnspecified", capsule, false),
            ("LargeCapsuleUnspecified", prefixQuery with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule) }, false),
            ("BonesRequiredCapsuleNested", prefixQuery with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [BaseGameModelKeys.Relics.SmallCapsule], BonesRouteOrderMode.AnyOrder),
                LegacyNeow = prefixQuery.LegacyNeow with { CapsuleContainedRelics = new([], [juzuKey], []) } }, true),
            ("CapsuleAll", capsule with { LegacyNeow = capsule.LegacyNeow with { CapsuleContainedRelics = new([], [juzuKey], []) } }, true),
            ("CapsuleSingletonAny", capsule with { LegacyNeow = capsule.LegacyNeow with { CapsuleContainedRelics = new([juzuKey], [], []) } }, true),
            ("CapsuleMultiAny", capsule with { LegacyNeow = capsule.LegacyNeow with {
                CapsuleContainedRelics = new([juzuKey, common.OrderedRelics.First(key => key != juzuKey)], [], []) } }, false),
            ("FutureRelic", prefixQuery with { RelicSequenceConstraints = [new(RelicSequenceKind.Common,
                SearchSequenceRangeMode.FirstN, 3, new([juzuKey], [], []))] }, false) };
        foreach (var fixture in declaredCases)
        {
            var plan = InitialRoomsPlan.TryCompile(Request(fixture.Query, captured).Model);
            Check(plan is { CoversUnknownPrefix: true, CoversTreasure: false } && plan.UnknownJuzuAcquired == fixture.Juzu,
                "DeclaredOpeningAdmission:" + fixture.Name);
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                bool expected = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, 3, fixture.Juzu).Steps.All(step => step.Room == UnknownRoomCategory.Event);
                Check(plan!.Matches(root) == expected && plan.Reference(root) == expected,
                    "DeclaredOpeningReference:" + fixture.Name + ":" + ordinal);
            }
        }
        var committedTreasure = InitialRoomsPlan.TryCompile(Request(capsule with {
            TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered, [common.OrderedRelics[0], null]) }, captured).Model);
        Check(committedTreasure is { CoversUnknownPrefix: true, CoversTreasure: false } &&
            committedTreasure.TreasureRemainder == "InitialRooms.OpeningTreasureRetainedByExact",
            "CommittedTreasureRetainsActualAcquisitionDutyWithoutBlockingUnknown");
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
        PreparedSearch Request(SearchQuery query, SearchContext? context = null)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context ?? captured),
                new("000000000000", 1024, int.MaxValue, 4));
            return result.Plan ?? throw new InvalidDataException("OwnedInitialRooms.Compile:" + result.Issue);
        }
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
        await using var execution = new FilterRuntimeServices(requiresGpu: false, cpuWorkerCount: 4);
        foreach (var fixture in fixtures)
        {
            var request = Request(fixture.Query, fixture.Context);
            var selected = FamilyExecutionCoordinator.Plan(request, false);
            var cover = selected.Stages.SelectMany(stage => stage.Coverage).ToHashSet(StringComparer.Ordinal);
            bool hasTreasure = fixture.Query.TreasureRelics is { IsEmpty: false };
            bool hasUnknown = fixture.Query.FirstActUnknownEventPrefixCount > 0;
            VariantOpeningFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var variant, false);
            check(cover.Contains(InitialRoomsInvocation.TreasureCoverage) == hasTreasure &&
                cover.Contains(VariantOpeningFamily.Id) == hasUnknown &&
                (variant?.Plan.UnknownPrefixCount ?? 0) == fixture.Query.FirstActUnknownEventPrefixCount &&
                !cover.Contains(InitialRoomsInvocation.UnknownCoverage), fixture.Name + ":DefaultRegistrationCoverage");
            check(selected.OrderedFamilies.All(family => !family.FamilyId.StartsWith("I.", StringComparison.Ordinal)), fixture.Name + ":NoIndependentIFamily");
            if (fixture.Name == "UnknownMissingWorld")
                check(!cover.Contains("W.World") && selected.FilterRemainders.Contains("W.World:NoSelectedFilter"),
                    "MissingWorldDoesNotBorrowLegacyCoverage");
            if (fixture.Name == "TreasureMissingOrdinary")
                check(!cover.Contains("R.Relic") && selected.FilterRemainders.Contains("R.Relic:NoSelectedFilter"),
                    "MissingOrdinaryDoesNotBorrowLegacyCoverage");
            var running = selected.OrderedFamilies.ToDictionary(body => body, body => body.CreateExecution!(body, execution));
            foreach (bool compact in new[] { false, true })
            {
                var batch = new SearchBatch(0, 1024);
                var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, 1024)
                    .Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                var current = input;
                foreach (var family in selected.OrderedFamilies)
                {
                    if (family.Coverage.Contains(InitialRoomsInvocation.TreasureCoverage))
                        check(!family.CanBindPrivateSerial &&
                            family.QuotePhysicalWork(new(compact, false, input.Count) { PrivateInput = true }) is null &&
                            family.QuotePhysicalWork(new(compact, false, input.Count) { PrivateOutput = true }) is null,
                            fixture.Name + ":InitialRoomsPublicCompleteCallsCannotBorrowPrivatePrice");
                    current = await running[family].FilterAsync(execution, current, default);
                }
                InitialRoomsPlan? rooms = InitialRoomsPlan.TryCompile(request.Model);
                var relicRequest = Request(fixture.Query with { TreasureRelics = null }, fixture.Context);
                RelicFamily.TryPrepare(relicRequest.Model, FilterExecutionPreferences.From(relicRequest.RunOptions), out var legacyR, false);
                var relicReference = legacyR is null ? null : new RelicReferencePlan(RelicReferencePlan.ProjectEvaluation(relicRequest.Model),
                    relicRequest.Model.Authority.WorldAuthority, profile);
                var worldRequest = Request(fixture.Query with { FirstActUnknownEventPrefixCount = 0 }, fixture.Context);
                WorldFamily.TryCreate(worldRequest.Model, FilterExecutionPreferences.From(worldRequest.RunOptions), out var legacyW, false);
                var worldReference = legacyW is null ? null : new WorldFamilyReplay(worldRequest.Model);
                var expected = input.EnumerateLogicalOrdinals().Where(i => (rooms?.Reference(Root(i)) ?? true) &&
                    (relicReference?.Matches(Root(i)) ?? true) &&
                    (worldReference?.Matches(Root(i)) ?? true));
                var expectedOrdinals = expected.ToArray();
                check(current.EnumerateLogicalOrdinals().SequenceEqual(expectedOrdinals), fixture.Name + ":CompleteCpuPopulation:" + compact);
                if (fixture.Name == "TreasureOrdinaryAndShop")
                {
                    check(expectedOrdinals.Contains(witness), "MixedOrdinaryShopTreasureHasKnownPositive");
                    var old = relicReference!;
                    check(Enumerable.Range(0, 4097).Select(i => (ulong)i).Any(i => old.Matches(Root(i)) && !rooms!.Reference(Root(i))),
                        "MixedTreasureGateActuallyRejectsLegacySurvivors");
                    check(Enumerable.Range(0, 4097).Select(i => (ulong)i).Any(i => rooms!.Reference(Root(i)) && !old.Matches(Root(i))),
                        "MixedOrdinaryShopGateActuallyRejectsTreasureSurvivors");
                }
                if (fixture.Name == "UnknownExistingWorld")
                    check(expectedOrdinals.Length > 0, "MixedWorldUnknownHasPositivePopulation");
            }
            foreach (var family in running.Values) await family.DisposeAsync(execution);
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
            PreparedSearch Request(SearchQuery query)
            {
                var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context),
                    new(Beta110SeedCodec.FormatOrdinal(positive), 1, 1, 1));
                return result.Plan ?? throw new InvalidDataException("PartyUnknownFixture.Compile:" + result.Issue);
            }
            ProductionExactSearchResult Exact(PreparedSearch request, ulong ordinal) => ProductionExactSearchEvaluator.Evaluate(request.Model,
                TrustedRootHashInput.FromCanonicalSeed(profile, Beta110SeedCodec.FormatOrdinal(ordinal)));
            var query = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1,
                Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                    Conditions = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = slot == count - 1 ? 3 : slot == 0 ? 2 : 0 } }).ToArray() };
            var request = Request(query);
            string originalFingerprint = request.SnapshotFingerprint;
            string originalQuery = System.Text.Json.JsonSerializer.Serialize(request.Model.CompiledSearch.Query, ProductionSearchReplay.JsonOptions);
            var plan = InitialRoomsPlan.TryCompile(request.Model);
            check(plan is { UnknownPrefixCount: 3, CoversTreasure: false }, "PartySharedMaximum:" + count);
            var selected = FamilyExecutionCoordinator.Plan(request, false);
            check(selected.Stages.SelectMany(stage => stage.Coverage).Count(atom => atom == VariantOpeningFamily.Id) == 1 &&
                selected.ExecutionOrderUnits.Count(family => family.FamilyId == VariantOpeningFamily.Id) == 1 &&
                selected.ExecutionOrderUnits.All(family => family.FamilyId != "W.World") &&
                selected.ExecutionOrderUnits.Single(family => family.FamilyId == VariantOpeningFamily.Id).Preparation.Obligations
                    .Any(obligation => obligation.Domain == InitialRoomsInvocation.UnknownCoverage && obligation.ThroughOrdinal == 3),
                "PartyDefaultOneVOwnsWholeTableMaximum:" + count);
            var execution = new FilterRuntimeServices(requiresGpu: false, cpuWorkerCount: request.WorkerCount);
            var running = selected.OrderedFamilies.ToDictionary(body => body, body => body.CreateExecution!(body, execution));
            try
            {
                var batch = new SearchBatch(0, 128);
                var actual = FamilyCandidateSet.Dense(batch);
                foreach (var family in selected.OrderedFamilies)
                    actual = running[family].FilterAsync(execution, actual, default).GetAwaiter().GetResult();
                var expected = Enumerable.Range(0, 128).Select(i => (ulong)i).Where(i => InitialRoomsPlan.MatchesUnknown(
                    profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i)), 3));
                check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "PartyActualVPublicCpuPopulation:" + count);
                foreach (var family in running.Values) family.DisposeAsync(execution).GetAwaiter().GetResult();
            }
            finally { execution.DisposeAsync().GetAwaiter().GetResult(); }
            check(ReferenceEquals(PartyInitialRooms.IndependentDomains(request.Model), PartyInitialRooms.IndependentDomains(request.Model)),
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
                request.Model.CompiledSearch.Query, ProductionSearchReplay.JsonOptions) && ReferenceEquals(result.Authority, request.Model.Authority),
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
            check(InitialRoomsPlan.TryCompile(treasureRequest.Model) is { CoversUnknownPrefix: true, CoversTreasure: false },
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
            check(StableMetadata(mixedResult.Facts) == StableMetadata(baseline.Facts) &&
                StableMetadata(mixedResult.Request) == StableMetadata(baseline.Request) &&
                StableMetadata(mixedResult.Evaluation.Witnesses) == StableMetadata(baseline.Evaluation.Witnesses),
                "PublishedMixedMetadataUnchangedExceptGeneratedRequestId:" + count);
            var intervention = mixed with { Players = mixed.Players.Select(player => player.Slot == count - 1 ? player with {
                Conditions = player.Conditions with { OpeningRoute = new(player.Offers.All[0]) } } : player).ToArray() };
            var interventionRequest = Request(intervention);
            check(InitialRoomsPlan.TryCompile(interventionRequest.Model) is { CoversUnknownPrefix: true } &&
                PartyInitialRooms.Evaluate(interventionRequest.Model, rootPositive).Disposition == SearchDisposition.Match &&
                Exact(interventionRequest, positive).Evaluation.Disposition ==
                    ProductionExactSearchEvaluator.Evaluate(PartyInitialRooms.IndependentDomains(interventionRequest.Model), TrustedRootHashInput.FromCanonicalSeed(profile, Beta110SeedCodec.FormatOrdinal(positive))).Evaluation.Disposition,
                "LastSeatOpeningDoesNotBlockSharedPrefixOrOverrideIndependentN:" + count);
            var juzu = query with { Players = query.Players.Select(player => player.Slot == count - 1 ? player with {
                Conditions = player.Conditions with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule), LegacyNeow = player.Conditions.LegacyNeow with {
                    CapsuleContainedRelics = new([new ModelKey("RELIC", "JUZU_BRACELET")], [], []) } } } : player).ToArray() };
            var juzuRequest = Request(juzu);
            var juzuPlan = InitialRoomsPlan.TryCompile(juzuRequest.Model);
            check(juzuPlan is { CoversUnknownPrefix: true, UnknownJuzuAcquired: true }, "LastSeatDeclaredJuzuControlsSharedPrefix:" + count);
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                ulong root = profile.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                bool expected = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, 3, true).Steps.All(step => step.Room == UnknownRoomCategory.Event);
                check(juzuPlan!.Matches(root) == expected && juzuPlan.Reference(root) == expected &&
                    PartyInitialRooms.Evaluate(juzuRequest.Model, root).Disposition == (expected ? SearchDisposition.Match : SearchDisposition.NoMatch),
                    "PartyDeclaredJuzuSameSharedPopulation:" + count + ":" + ordinal);
            }
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
