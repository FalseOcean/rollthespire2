using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class FamilyPhysicalMatrix
{
    // Factory/quote evidence only. No Family invocation, E2E run or calibration.
    private static void RunPartyOrderPricingProbe(ExactSearchExecutionRequest basis, string output, ulong? rootOrdinal = null)
    {
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("PartyOrderPricing.RealContextRequired");
        ulong ordinal = rootOrdinal ?? 0;
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
        ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
        Directory.CreateDirectory(output);
        double? Finite(double? value) => value.HasValue && double.IsFinite(value.Value) ? value : null;
        var rows = new List<object>();
        for (int merchant = 1; merchant <= 5; merchant++)
        {
            var query = SearchQuery.Empty with { Players = party.Players.Select(owner =>
            {
                var offers = ModernNeowIdentityPredictor.PredictModernCore(seed, owner, Beta111Profile.Instance, true).RelicKeys;
                var shops = Beta111NormalMerchantColorlessSequenceProjector.Project(root,
                    Beta111MerchantColorlessAuthority.From(owner), merchant);
                return new PlayerOfferQuery(owner.PlayerSlotIndex, new([], offers, [])) {
                    Conditions = SearchQuery.Empty with { MerchantColorlessConditions = [
                        new(merchant, MerchantColorlessSlot.Uncommon, shops.Merchants[merchant - 1].UncommonColorless)] } };
            }).ToArray() };
            var options = basis.RunOptions with { StartSeed = seed, ScanCount = 1L << 34,
                TargetMatchCount = int.MaxValue, IncludeDiagnostics = true };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options).Plan
                ?? throw new InvalidDataException("PartyOrderPricing.Compile");
            // One whole-table Exact call per authored query, never an Exact scan.
            bool positive = ProductionExactSearchEvaluator.Evaluate(request,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch;
            if (!positive) throw new InvalidDataException($"PartyOrderPricing.SourcePositiveMissing:seed={seed};ordinal={ordinal};m={merchant}");
            var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true);
            var n = registered.Single(f => f.FamilyId == "N.Neow");
            var s = registered.Single(f => f.FamilyId == "S.MerchantShopColorless");
            if (registered.Count != 2 || !n.ConditionPerformance.UsesGpu || !s.ConditionPerformance.UsesGpu)
                throw new InvalidDataException("PartyOrderPricing.ExpectedCompleteRegisteredGpuNS");
            var reference = FamilyPlanner.Plan(registered);
            object Describe(FamilyExecutionPlan plan)
            {
                int window = FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, plan.OrderedFamilies);
                double population = Math.Min(request.ScanCount, window), prefix = 1;
                var passed = new HashSet<string>(StringComparer.Ordinal);
                var stages = new List<object>();
                foreach (var stage in plan.Stages)
                {
                    var family = stage.Family;
                    var survival = family.ResolveSurvival(passed);
                    double reached = RolltheSpire2.Search.Predictability.SearchPredictabilityMath.ProbabilityAtLeastOne(prefix, population);
                    double? conditional = reached > 0 ? Finite(population * prefix / reached) : null;
                    var quote = conditional.HasValue ? family switch {
                        PartyFamily p => p.QuoteUnit(conditional.Value, stages.Count > 0, passed),
                        NeowFamily.PartyInitial p => p.QuoteUnit(conditional.Value, stages.Count > 0),
                        _ => null } : null;
                    stages.Add(new { family.FamilyId, stage.CompactAbi1Input,
                        stage.ConditionPerformance.UsesGpu, stage.ConditionPerformance.PhysicalImplementationRevision,
                        PrefixFastPass = Finite(prefix), WindowReachProbability = Finite(reached),
                        ConditionalMeanInputs = conditional, ConditionalMeanIsNotPopulationLimit = true,
                        Survival = Finite(survival.SurvivalProbability), SurvivalEvidence = survival.Evidence,
                        CanonicalMsGivenReach = Finite(quote?.Milliseconds), QuoteEvidence = quote?.Evidence,
                        MissingQuoteReason = quote is null ? "ExistingCompleteStageQuoteUnavailable" : null,
                        ReferencePerInputMs = Finite(stage.PerInputTimeMs),
                        ReferenceWorkPerInput = Finite(stage.ExpectedFilteringWorkUnitsPerInput) });
                    if (survival.SurvivalProbability is double probability) prefix *= probability;
                    else prefix = double.NaN;
                    passed.UnionWith(family.Coverage);
                }
                return new { plan.PlanId, plan.SelectionPolicyId, RankingAuthority = plan.RankingAuthority.ToString(),
                    RootWindow = window, RootPopulation = population,
                    ReferenceMsPerRoot = Finite(plan.ExpectedFamilyPipelineMsPerRoot),
                    plan.DecisionEvidence, plan.MissingEvidence, Stages = stages };
            }
            var selected = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true);
            var slots = request.CompiledSearch.PlayerSearches.Take(party.Players.Count).Select(player =>
            {
                var child = ExactSearchExecutionRequestFactory.ForPlayer(request, player);
                var authority = Beta111MerchantColorlessAuthority.From(child.Authority);
                var childFamily = ((PartyFamily)s).PersonalStages.Single(p => p.Slot == child.Authority.PlayerSlotIndex).Family;
                return new { Slot = child.Authority.PlayerSlotIndex, UncommonPool = authority.UncommonPool,
                    RarePool = authority.RarePool, UncommonPoolCount = authority.UncommonPool.Count,
                    RarePoolCount = authority.RarePool.Count, childFamily.AnalyticalCost,
                    ShopSurvival = childFamily.Survival, child.CompiledSearch.NormalizedQuery };
            }).ToArray();
            string workload = $"party-order-pricing-m{merchant}.json";
            ProductionSearchReplay.Write(Path.Combine(output, workload), new ProductionSearchReplayWorkload(1, query, options));
            rows.Add(new { Merchant = merchant, Players = party.Players.Count, KnownPositive = seed, RootOrdinal = ordinal,
                WholeTableExactMatch = positive, Workload = workload, Slots = slots,
                ReferenceSelected = Describe(reference),
                ExplicitGpuNThenS = Describe(FamilyPlanner.InOrder([n, s])),
                ExplicitGpuSThenN = Describe(FamilyPlanner.InOrder([s, n])),
                FinalProductionSelector = Describe(selected),
                FinalChangesPhysicalAllocation = selected.Stages.Any(stage => !stage.ConditionPerformance.UsesGpu),
                ComparisonScope = "SingleUnitDiagnosticsOnly;NoCompleteChainPrice;NoMeasuredTimingOrFit" });
        }
        ProductionSearchReplay.Write(Path.Combine(output, "party-order-pricing-context.json"),
            new ProductionSearchReplayContext(1, $"CapturedPartyWholeOffersAndRealMerchantUncommonTargets;Seed={seed};RootOrdinal={ordinal}", context));
        File.WriteAllText(Path.Combine(output, "party-order-pricing.json"), JsonSerializer.Serialize(rows,
            new JsonSerializerOptions(ProductionSearchReplay.JsonOptions) { WriteIndented = true }));
        Console.WriteLine($"PARTY_ORDER_PRICING_READY players={party.Players.Count};seed={seed};ordinal={ordinal};queries=5;wholeTableExactCalls=5;execution=false;modelOnly=true");
    }

    // An authored diagnostic: tight whole-table offer gates feed a sparse S tail.
    // All conditions derive from one known root, with no selected opening effects.
    private static void ExportPartyCostFixture(ExactSearchExecutionRequest basis, string output)
    {
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("PartyCostFixture.RealContextRequired");
        const string seed = "000000000000";
        ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
        var query = SearchQuery.Empty with {
            Players = party.Players.Select(owner =>
            {
                var offers = ModernNeowIdentityPredictor.PredictModernCore(seed, owner, Beta111Profile.Instance, true).RelicKeys;
                var shops = Beta111NormalMerchantColorlessSequenceProjector.Project(root,
                    Beta111MerchantColorlessAuthority.From(owner), 5);
                // Two offer gates give repeated sparse arrivals in a 16 Mi root
                // window. Requiring every 4p offer would mostly measure setup.
                return new PlayerOfferQuery(owner.PlayerSlotIndex, new([], owner.PlayerSlotIndex < 2 ? offers : [], [])) { Conditions = SearchQuery.Empty with {
                    MerchantColorlessConditions = [new(5, MerchantColorlessSlot.Uncommon, shops.Merchants[4].UncommonColorless)] } };
            }).ToArray() };
        var options = new SearchRunOptions(seed, 1L << 34, int.MaxValue, 16);
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options).Plan
            ?? throw new InvalidDataException("PartyCostFixture.Compile");
        if (!ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch)
            throw new InvalidDataException("PartyCostFixture.WholeTablePositiveMissing");
        var plan = FamilyExecutionCoordinator.Plan(request, true);
        ProductionSearchReplay.Write(Path.Combine(output, "cost-control.json"), new ProductionSearchReplayWorkload(1, query, options));
        ProductionSearchReplay.Write(Path.Combine(output, "cost-context.json"), new ProductionSearchReplayContext(1,
            "CapturedPartyTwoSlotWholeOfferConjunctionPlusEverySlotFinalShop;KnownWholeTableRootZero", context));
        File.WriteAllText(Path.Combine(output, "cost-fixture-plan.txt"), plan.FormatSummary());
        Console.WriteLine($"PARTY_COST_FIXTURE_PASS players={party.Players.Count};positive={seed};wholeTableExact=true;plan={plan.PlanId}");
    }

    // Preserve the N fixture's known whole-table positive and add independent
    // fixed-final-shop checks. S's early-rejection body is disabled for this shape
    // in both baseline and candidate, so a combined E2E pair isolates the N change.
    private static void ExportPartyRareControl(ExactSearchExecutionRequest basis, string output)
    {
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("PartyRare.RealContextRequired");
        var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, basis.CanonicalStartSeed);
        if (!ProductionExactSearchEvaluator.Evaluate(basis, input).IsMatch)
            throw new InvalidDataException("PartyRare.NFixturePositiveMissing");
        ulong root = Beta111Profile.Instance.ComputeRootSeed(basis.CanonicalStartSeed);
        var query = basis.CompiledSearch.NormalizedQuery with {
            Players = basis.CompiledSearch.NormalizedQuery.Players.Select(p =>
            {
                if (p.Conditions.MerchantColorlessConditions.Count != 0 || p.Conditions.MerchantColorlessSequenceConditions.Count != 0)
                    throw new InvalidDataException("PartyRare.ExistingShopObligations");
                var authority = Beta111MerchantColorlessAuthority.From(party.Players[p.Slot]);
                var observed = Beta111NormalMerchantColorlessSequenceProjector.Project(root, authority, 5);
                return p with { Conditions = p.Conditions with {
                    MerchantColorlessConditions = [new(5, MerchantColorlessSlot.Uncommon, observed.Merchants[4].UncommonColorless)] } };
            }).ToArray() };
        var options = basis.RunOptions with { StartSeed = basis.CanonicalStartSeed, ScanCount = 1L << 34,
            TargetMatchCount = int.MaxValue, WorkerCount = 16, IncludeDiagnostics = true };
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options).Plan
            ?? throw new InvalidDataException("PartyRare.Compile");
        if (!ProductionExactSearchEvaluator.Evaluate(request, input).IsMatch)
            throw new InvalidDataException("PartyRare.CombinedPositiveMissing");
        foreach (var p in request.CompiledSearch.PlayerSearches.Take(party.Players.Count))
        {
            var child = ExactSearchExecutionRequestFactory.ForPlayer(request, p);
            if (!MerchantShopColorlessGpuExecutor.ConfigureEarlyRejection("#version 450\n", child.Evaluation,
                child.Authority.PlayersCount).Contains("#define RT2_S_EARLY_REJECTION 0", StringComparison.Ordinal))
                throw new InvalidDataException("PartyRare.ShopMustRemainControlBody");
        }
        ProductionSearchReplay.Write(Path.Combine(output, "rare-control.json"), new ProductionSearchReplayWorkload(1, query, options));
        ProductionSearchReplay.Write(Path.Combine(output, "rare-context.json"), new ProductionSearchReplayContext(1,
            "CapturedPartyNKnownPositivePlusFinalShopControls;NoShopEarlyRejection", context));
        Console.WriteLine($"PARTY_RARE_CONTROL_PASS players={party.Players.Count};positive={basis.CanonicalStartSeed};wholeTableExact=true;shopEarlyRejection=false");
    }

    // C2 E2E control: retain the complete source-authored C query and its known
    // positive. Only slot 0 gets an independent final-shop gate, so the other
    // players' shop predicates do not collapse C's arriving population.
    private static void ExportPartyCombatE2eFixture(ExactSearchExecutionRequest basis, string output)
    {
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("PartyCombatE2e.RealPartyContextRequired");
        if (party.Players.Count is not (2 or 4))
            throw new InvalidDataException("PartyCombatE2e.TwoOrFourPlayersRequired");
        var sourceQuery = basis.CompiledSearch.Query;
        bool HasShop(SearchQuery q) => q.MerchantColorlessConditions.Count != 0 ||
            q.MerchantColorlessSequenceConditions.Count != 0 || q.Players.Any(p => HasShop(p.Conditions));
        if (HasShop(sourceQuery) || HasShop(basis.CompiledSearch.NormalizedQuery))
            throw new InvalidDataException("PartyCombatE2e.ExistingShopObligations");
        if (!sourceQuery.HasCombatRewardConstraints && !sourceQuery.Players.Any(p => p.Conditions.HasCombatRewardConstraints))
            throw new InvalidDataException("PartyCombatE2e.SourceCombatQueryRequired");
        if (sourceQuery.Players.Count(p => p.Slot == 0) != 1)
            throw new InvalidDataException("PartyCombatE2e.ExistingSlotZeroRequired");
        // C source fixtures export their independently authored positive as the
        // workload's StartSeed. Do not substitute root zero or find a new hit.
        string seed = basis.CanonicalStartSeed;
        var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
        var sourceExact = ProductionExactSearchEvaluator.Evaluate(basis, input);
        if (!sourceExact.IsMatch)
            throw new InvalidDataException("PartyCombatE2e.SourcePositiveMissing:" + sourceExact.Evaluation.Disposition + ":" + sourceExact.FailureCode);
        var owner = party.Players.Single(p => p.PlayerSlotIndex == 0);
        var authority = Beta111MerchantColorlessAuthority.From(owner);
        var observed = Beta111NormalMerchantColorlessSequenceProjector.Project(input.RootHash, authority, 5);
        var target = observed.Merchants[4].UncommonColorless;
        var query = sourceQuery with { Players = sourceQuery.Players.Select(p => p.Slot == 0
            ? p with { Conditions = p.Conditions with {
                MerchantColorlessConditions = [new(5, MerchantColorlessSlot.Uncommon, target)] } }
            : p).ToArray() };
        var options = basis.RunOptions with { StartSeed = seed, ScanCount = 1L << 32,
            TargetMatchCount = int.MaxValue, IncludeDiagnostics = true, SkipExactValidation = false };
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options).Plan
            ?? throw new InvalidDataException("PartyCombatE2e.Compile");
        var combinedExact = ProductionExactSearchEvaluator.Evaluate(request, input);
        if (!combinedExact.IsMatch)
            throw new InvalidDataException("PartyCombatE2e.CombinedPositiveMissing:" + combinedExact.Evaluation.Disposition + ":" + combinedExact.FailureCode);
        Directory.CreateDirectory(output);
        ProductionSearchReplay.Write(Path.Combine(output, "c-e2e-control.json"), new ProductionSearchReplayWorkload(1, query, options));
        ProductionSearchReplay.Write(Path.Combine(output, "c-e2e-control-long.json"), new ProductionSearchReplayWorkload(1, query,
            options with { ScanCount = 1L << 34 }));
        ProductionSearchReplay.Write(Path.Combine(output, "c-e2e-context.json"), new ProductionSearchReplayContext(1,
            "CapturedPartyCompleteSourceCQueryPlusSlotZeroFinalShopUncommon;OriginalSourcePositiveRetained", context));
        File.WriteAllText(Path.Combine(output, "c-e2e-fixture.json"), JsonSerializer.Serialize(new {
            SourcePositive = seed, SourcePositiveProvenance = "BasisCanonicalStartSeedFromSourceAuthoredCFixture;NoPositiveSearchHere",
            Players = party.Players.Count, SourceWholeTableExactMatch = sourceExact.IsMatch,
            CombinedWholeTableExactMatch = combinedExact.IsMatch, AddedShopSlot = 0, AddedShopOrdinal = 5,
            AddedTarget = target, UncommonPoolCount = authority.UncommonPool.Count,
            PreservedOriginalCombatAndOpeningObligations = true, DefaultScanCount = 1L << 32, LongScanCount = 1L << 34,
            Workload = "c-e2e-control.json", LongWorkload = "c-e2e-control-long.json", Context = "c-e2e-context.json"
        }, new JsonSerializerOptions(ProductionSearchReplay.JsonOptions) { WriteIndented = true }));
        Console.WriteLine($"PARTY_COMBAT_E2E_FIXTURE_PASS players={party.Players.Count};positive={seed};sourceAndCombinedWholeTableExact=true;addedShopSlot=0;roots={1L << 32};longRoots={1L << 34}");
    }

    // Existing complete party Family invocations, not synthesized personal pools.
    // Measures recurring public ABI1 work after a warm invocation. No repricing.
    private static void RunPartyCostChecks(ExactSearchExecutionRequest request, string output)
    {
        if (request.CompiledSearch.Context.Party is not { Players.Count: >= 2 and <= 4 } party)
            throw new InvalidDataException("PartyCost.RealPartyContextRequired");
        var cpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: false);
        var gpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true);
        if (!cpu.Select(f => f.FamilyId).Order().SequenceEqual(gpu.Select(f => f.FamilyId).Order()))
            throw new InvalidDataException("PartyCost.CoverageMismatch");
        // Capture all quotes before invoking either implementation. Observations
        // remain evidence, never a source for this request's offline allocation.
        var plans = new[] { false, true }.Select(available =>
        {
            var plan = FamilyExecutionCoordinator.Plan(request, available);
            return new { GpuAvailable = available, plan.PlanId, plan.SelectionPolicyId,
                Stages = plan.Stages.Select(s => new { s.Family.FamilyId, s.ConditionPerformance, s.Survival, s.PerInputTimeMs }).ToArray() };
        }).ToArray();
        var quotes = new Dictionary<(IFamilyInvocation Family, bool Compact, int Count), FamilyPhysicalQuote?>();
        foreach (var family in cpu.Concat(gpu))
        foreach (bool compact in new[] { false, true })
        foreach (int count in new[] { 64, 1024, 8192 })
            quotes[(family, compact, count)] = family.QuotePhysicalWork(new(compact, false, count));
        var smallShop = gpu.OfType<PartyFamily>().Where(f => f.FamilyId == "S.MerchantShopColorless")
            .SelectMany(f => ((IFamilyInvocation)f).CpuRealizations).OfType<PartyFamily>().FirstOrDefault();
        var smallShopQuotes = new Dictionary<(bool Compact, int Count), FamilyPhysicalQuote?>();
        foreach (bool compact in new[] { false, true })
        foreach (int count in new[] { 64, 1024, 8192 })
            smallShopQuotes[(compact, count)] = smallShop?.QuotePhysicalWork(new(compact, false, count));
        var rows = new List<object>();
        var batch = new SearchBatch(0, 1 << 24);
        var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true,
            cpuWorkerCount: request.WorkerCount);
        try
        {
            foreach (var gpuFamily in gpu)
            {
                var cpuFamily = cpu.Single(f => f.FamilyId == gpuFamily.FamilyId);
                if (cpuFamily.ConditionPerformance.UsesGpu)
                    throw new InvalidDataException("PartyCost.CpuImplementationNotSelected");
                foreach (bool compact in new[] { false, true })
                foreach (int count in new[] { 64, 1024, 8192 })
                {
                    var input = compact
                        ? FamilyCandidateSet.FromSortedAbi1(batch,
                            Enumerable.Range(0, count).Select(i => (ulong)i * (1u << 24) / (uint)count).ToArray())
                        : FamilyCandidateSet.Dense(new SearchBatch(0, count));
                    var window = new FamilyObservationWindow(input.Batch, request);
                    var cpuQuote = quotes[(cpuFamily, compact, count)];
                    var gpuQuote = quotes[(gpuFamily, compact, count)];
                    string? expected = null;
                    for (int round = -1; round < 5; round++)
                    foreach (bool useGpu in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
                    {
                        var family = useGpu ? gpuFamily : cpuFamily;
                        var watch = Stopwatch.StartNew();
                        var result = family.InvokeAsync(execution, window, input, default).GetAwaiter().GetResult();
                        double ms = watch.Elapsed.TotalMilliseconds;
                        string digest = Digest(result);
                        expected ??= digest;
                        if (digest != expected || result.Batch != input.Batch)
                            throw new InvalidDataException($"PartyCost.FullPopulationMismatch:{family.FamilyId}:{compact}:{count}");
                        if (round < 0) continue;
                        rows.Add(new { family.FamilyId, Players = party.Players.Count, Workers = request.WorkerCount,
                            Mode = useGpu ? "GpuAvailable" : "CpuOnly", ActualUsesGpu = family.ConditionPerformance.UsesGpu,
                            Compact = compact, Input = count, Output = result.Count, Round = round,
                            CanonicalMs = ms, Digest = digest, Quote = useGpu ? gpuQuote : cpuQuote,
                            SmallShopCpuEnvelope = gpuFamily.FamilyId == "S.MerchantShopColorless" && count <= 1024
                                ? smallShopQuotes[(compact, count)] : null,
                            Revision = family.ResolveConditionPerformance(compact).PhysicalImplementationRevision });
                    }
                }
            }
        }
        finally
        {
            try { foreach (var family in gpu.Concat(cpu)) family.DisposeAsync(execution).GetAwaiter().GetResult(); }
            finally { execution.DisposeAsync().GetAwaiter().GetResult(); }
        }
        File.WriteAllText(Path.Combine(output, "party-cost.json"), JsonSerializer.Serialize(new {
            Source = "CurrentProductionPartyFamilyInvocations", request.SnapshotFingerprint,
            Players = party.Players.Count, Roster = party.Players.Select(p => new { p.PlayerSlotIndex, p.Character.CharacterKey }),
            Boundary = "WarmInvokeAsyncCanonicalAbi1;NoExactOrSetup;FullCpuGpuPopulationParity",
            SameRunRepricing = false, Plans = plans, Rows = rows
        }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"PARTY_COST_PASS rows={rows.Count};players={party.Players.Count};fullPopulationParity=true;calibration=false");
    }
}
