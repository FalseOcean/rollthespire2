using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class FamilyWorkEstimateChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException("FamilyWorkEstimate: " + message); }
        var old = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/owner-nwae-v1/context.json").Context;
        var variantContext = VariantOpeningPreparationChecks.CreateContext();
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? actual = null) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(query, actual ?? context), new("000000000000", 65536, 16, 4)).Plan!
            ?? throw new InvalidOperationException("FamilyWorkEstimate.Compile");
        SearchQuery Read(string path) => ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path).Query;
        var cases = new List<(string Id, SearchQuery Query)> {
            ("N.Neow", Read("n-cost-v1/Leafy.json")),
            ("R.Relic", Read("pricing-hole-v1/R-seven-tracked.json")),
            ("R.Relic", Read("family-cost-closure-v1/Shop-U3.json")),
            ("R.Relic", Read("v1/cases/NR-small-capsule.json")),
            ("S.MerchantShopColorless", Read("pricing-hole-v1/S-mixed.json")),
            ("C.CombatReward", Read("pricing-hole-v1/C-two-targets.json")),
            ("E.EventResult", Read("pricing-hole-v1/E-three-operators.json")),
            ("A.AncientOption", Read("pricing-hole-v1/A-two-generators.json")),
            ("W.World", Read("pricing-hole-v1/W-event-mix.json")),
            (VariantOpeningFamily.Id, SearchQuery.Empty with { VariantBossBranches = [
                new(1, VariantOpeningPreparationChecks.A, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false),
                new(1, VariantOpeningPreparationChecks.B, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] }),
            (VariantOpeningFamily.Id, SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 2 }),
            ("T.TransformationAggregate", SearchQuery.Empty with {
                OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
                TransformationAggregate = new(TransformationOpening.LeafyPoultice, TransformationPickupOrder.Any,
                    false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, []) })
        };
        try
        {
            // No measured key for this device. Models must serve the actual
            // compiler/registration without asserting a current-device measure.
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("UnmeasuredEstimateDevice", "vulkan");
            foreach (var c in cases)
            {
                var request = Request(c.Query, c.Id == VariantOpeningFamily.Id ? variantContext : null);
                var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request);
                var family = families.SingleOrDefault(f => f.FamilyId == c.Id)
                    ?? throw new InvalidOperationException("FamilyWorkEstimate.MissingFixtureFamily:" + c.Id +
                        ";registered=" + string.Join(',', families.Select(f => f.FamilyId)));
                foreach (bool compact in new[] { false, true })
                {
                    var geometry = new FamilyPhysicalQuoteRequest(compact, false, 65536);
                    var quote = family.QuotePhysicalWork(geometry);
                    Check(quote is { NanosecondsPerInput: > 0 } && FamilyCoarsePricing.IsEstimate(quote),
                        c.Id + " compiled " + family.GetType().Name + " has honest device/shape-independent estimate " + compact);
                    var cpu = FamilyCpuExecution.ConfiguredRealizations(family).OfType<FamilyCpuExecution>().FirstOrDefault();
                    if (cpu is not null)
                    {
                        var cpuQuote = cpu.Quote(geometry, new HashSet<string>());
                        Check(cpuQuote is { NanosecondsPerInput: > 0 } &&
                            (FamilyCoarsePricing.IsEstimate(cpuQuote) || FamilyCanonicalMeasuredCost.IsCanonicalQuote(cpuQuote)),
                            c.Id + " configured CPU and actual Compact role retain measured priority or honest estimate");
                    }
                }
                var plan = FamilyExecutionCoordinator.Plan(request, true, gpuFloat64Available: false);
                Check(family.Coverage.All(plan.ExecutionOrderUnits.SelectMany(f => f.Coverage).Contains) && plan.AllowSmallCandidateExact,
                    c.Id + " restricted backend retains its obligation and ordinary Exact handoff");
                Check(plan.Stages.Where(stage => stage.Coverage.Any(family.Coverage.Contains)).All(stage => stage.PerInputTimeMs > 0),
                    c.Id + " actual restricted-body plan consumes a usable time reference");
            }
            var effects = context.Authority.EffectAuthority!;
            var pool = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool!;
            var expanded = context with { Authority = context.Authority.WithEffectAuthority(effects with {
                MerchantColorlessOrderedPool = [..pool, pool[0]], SnapshotFingerprint = effects.SnapshotFingerprint + ":price-expanded-weighted-shop" }) };
            var expandedRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(
                cases.Single(c => c.Id == "S.MerchantShopColorless").Query, expanded), new("000000000000", 65536, 16, 4)).Plan!;
            var expandedShop = FamilyExecutionCoordinator.CreateRegisteredFamilies(expandedRequest).Single(f => f.FamilyId == "S.MerchantShopColorless");
            Check(FamilyCoarsePricing.IsEstimate(expandedShop.QuotePhysicalWork(new(false, false, 65536))),
                "an executable weighted shop-pool expansion does not need another exact pool-layout price");
            var leafy = Read("n-cost-v1/Leafy.json");
            var ordinaryN = FamilyExecutionCoordinator.CreateRegisteredFamilies(Request(leafy)).Single(f => f.FamilyId == "N.Neow");
            var expandedNRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(leafy, expanded),
                new("000000000000", 65536, 16, 4)).Plan!;
            var expandedN = FamilyExecutionCoordinator.CreateRegisteredFamilies(expandedNRequest).Single(f => f.FamilyId == "N.Neow");
            Check(ordinaryN.QuotePhysicalWork(new(false, false, 65536))!.NanosecondsPerInput ==
                expandedN.QuotePhysicalWork(new(false, false, 65536))!.NanosecondsPerInput,
                "unread merchant content cannot split the N Leafy work estimate");

            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
            var party = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
                .Invoke(null, [captured, 2, null, null])!;
            const string seed = "000000000000";
            var table = SearchQuery.Empty with { Players = party.Party!.Players.Select(owner =>
                new PlayerOfferQuery(owner.PlayerSlotIndex, new([], RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(
                    seed, owner, RolltheSpire2.Compatibility.Beta111Profile.Instance, true).RelicKeys, [])) {
                    Conditions = cases.Single(c => c.Id == "S.MerchantShopColorless").Query }).ToArray() };
            var tableRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(table, party), new(seed, 65536, 16, 4)).Plan!;
            foreach (bool gpu in new[] { false, true })
            {
                var plan = FamilyExecutionCoordinator.Plan(tableRequest, gpu);
                foreach (var unit in plan.ExecutionOrderUnits)
                    Check(FamilyCoarsePricing.IsEstimate(unit.QuotePhysicalWork(new(false, false, 65536))),
                        "party same-family wrapper is quoted as an estimate for current CPU/GPU " + unit.FamilyId);
                Check(plan.ExecutionOrderUnits.Any(unit => unit is NeowFamily.PartyInitial) &&
                    plan.ExecutionOrderUnits.Any(unit => unit is PartyFamily), "both shared N and personal S are actually present");
            }
            var ratio = FamilyCanonicalMeasuredCost.CpuWorkerRatio("S.MerchantShopColorless", 4, false);
            Check(ratio.Ratio > 0 && double.IsFinite(ratio.Ratio) && ratio.Evidence.Contains("SameFamilyHistoricalMedian", StringComparison.Ordinal),
                "CPU budget estimate is derived from paired same-family anchors");
            Check(FamilyCanonicalMeasuredCost.CpuWorkerRatio("S.MerchantShopColorless", 32, false).Ratio ==
                FamilyCanonicalMeasuredCost.CpuWorkerRatio("S.MerchantShopColorless", 16, false).Ratio,
                "unmeasured larger budgets do not invent additional linear speedup");
            var n = (NeowFamily)FamilyExecutionCoordinator.CreateRegisteredFamilies(Request(Read("n-cost-v1/Leafy.json")))
                .Single(f => f.FamilyId == "N.Neow");
            Check(NeowWorkEstimate.Describe(n.PricingReplayPlan).PoolVisits == 0,
                "direct-index Leafy does not acquire a whole transform-pool traversal cost");
            var source = FamilyCoarsePricing.Gpu("S", "Fixture", 1, 1024, new(false, false, 2048), "fixture")!;
            var full = source with { PublicTransportClass = "CompleteCanonicalAbi1", OutputAlreadyOrdered = true };
            Check(FamilyCoarsePricing.PublicMilliseconds(full, new(false, false, 2048), .5) == 2048d / 1e6,
                "an already complete wrapper does not pay output/submit twice");

            checks += FamilyWorkEstimateReplayChecks.Run();
            checks += FamilyWorkEstimateCombatPoolChecks.Run();
            checks += FamilyWorkEstimateWorldPoolChecks.Run();
            checks += FamilyComputationBoundaryChecks.Run();
            checks += FamilyComputationWrapperChecks.Run();

            // Actual Production session + JSON continuation, using the ordinary
            // multi-queue CPU choice. Cancellation is driven by received matches,
            // not a timing assumption, and bounded delivery forces a partial run.
            var merchant = RolltheSpire2.Core.Merchant.Beta111MerchantColorlessAuthority.From(context.Authority);
            var resumeQuery = SearchQuery.Empty with { MerchantColorlessSequenceConditions = [
                new(3, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Uncommon, [merchant.UncommonPool[0], null, null]),
                new(3, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Rare, [merchant.RarePool[0], null, null])] };
            var resumeRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(resumeQuery, context),
                new("000000000000", 16384, int.MaxValue, 4)).Plan!;
            async Task<(Dictionary<string, string> Records, SearchContinuationSnapshot? Continuation, ulong Cursor)> Collect(
                SearchContinuationSnapshot? continuation, bool cancel)
            {
                // The production caller compiles the saved frontier into the
                // next request; the continuation owns pending/ACK, not StartSeed.
                var activeRequest = continuation is null ? resumeRequest : ExactSearchExecutionRequestFactory.Compile(
                    SearchCompiler.Compile(resumeQuery, context), new(Beta110SeedCodec.FormatOrdinal(continuation.NextOrdinal),
                        16384 - (long)continuation.NextOrdinal, int.MaxValue, 4)).Plan!;
                var session = FamilyExecutionCoordinator.Start(activeRequest, queueCapacity: 2,
                    failOnPhysicalRecovery: true, gpuAvailable: false, continuation: continuation);
                var records = new Dictionary<string, string>(StringComparer.Ordinal);
                bool cancelled = false;
                void Drain()
                {
                    while (session.TryReadCandidate(out var candidate))
                        if (candidate is not null)
                        {
                            Check(candidate.SnapshotFingerprint == activeRequest.SnapshotFingerprint ||
                                candidate.SnapshotFingerprint.StartsWith(activeRequest.SnapshotFingerprint + "|", StringComparison.Ordinal),
                                "each delivered record retains its actual attempt/window identity");
                            string row = System.Text.Json.JsonSerializer.Serialize(new {
                                candidate.Seed, candidate.MatchEvidence, candidate.MatchedRouteIds, candidate.Witnesses,
                                candidate.ProfileId, candidate.CharacterKey, candidate.Ascension,
                                // Only the separately checked scan-window digest
                                // is normalized for comparison across attempts.
                                SnapshotFingerprint = resumeRequest.SnapshotFingerprint + candidate.SnapshotFingerprint[activeRequest.SnapshotFingerprint.Length..],
                                candidate.ValidationDisposition, candidate.ValidationIssue, candidate.ExecutionWarnings,
                                candidate.SupportScope, candidate.IsUnverified, candidate.IsUnfiltered }, ProductionSearchReplay.JsonOptions);
                            Check(records.TryAdd(candidate.Seed, row), "one session publishes each received record once");
                            session.AcknowledgeCandidate(candidate.Seed);
                            if (cancel && !cancelled && records.Count >= 8) { cancelled = true; session.Cancel(); }
                        }
                }
                try
                {
                    while (!session.Completion.IsCompleted) { Drain(); await Task.WhenAny(session.Completion, Task.Delay(1)); }
                    await session.Completion;
                    Drain();
                    Check(session.GetProgress().State != SearchRunState.Faulted, "real multi-queue session completes without physical recovery/fault");
                    return (records, session.CaptureContinuation(), session.GetSafeNextOrdinal());
                }
                finally { await session.DisposeAsync(); }
            }
            var uninterrupted = Collect(null, false).GetAwaiter().GetResult();
            var partial = Collect(null, true).GetAwaiter().GetResult();
            Check(partial.Records.Count >= 8 && partial.Records.Count < uninterrupted.Records.Count && partial.Continuation is { NextOrdinal: < 16384 },
                "cancelled real search retains a nonempty partial result set and continuation");
            string saved = System.Text.Json.JsonSerializer.Serialize(partial.Continuation, ProductionSearchReplay.JsonOptions);
            var restored = System.Text.Json.JsonSerializer.Deserialize<SearchContinuationSnapshot>(saved, ProductionSearchReplay.JsonOptions)!;
            var resumed = Collect(restored, false).GetAwaiter().GetResult();
            foreach (var row in resumed.Records) Check(partial.Records.TryAdd(row.Key, row.Value), "ACK survives JSON save/resume without duplicate delivery");
            Check(partial.Records.Count == uninterrupted.Records.Count && partial.Records.All(row =>
                uninterrupted.Records.TryGetValue(row.Key, out string? record) && record == row.Value) && resumed.Cursor == uninterrupted.Cursor,
                "JSON resume preserves complete semantic result fields, checked attempt identity and final safe frontier");
            Console.WriteLine($"FamilyWorkEstimate actualResumeRecords={uninterrupted.Records.Count};safeNext={resumed.Cursor};partialAckPreserved=true");
        }
        finally
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(old.GpuIdentity.Length == 0 ? "Unknown" : old.GpuIdentity,
                old.RenderingBackend.Length == 0 ? "unknown" : old.RenderingBackend);
        }
        Console.WriteLine($"FamilyWorkEstimate checks={checks};PASS");
        return 0;
    }
}
