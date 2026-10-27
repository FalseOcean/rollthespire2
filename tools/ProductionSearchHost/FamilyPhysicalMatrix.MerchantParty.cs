using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
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
    // Diagnostic only: two independently registered wrappers retain the actual
    // child request/slot/pools and differ only in which complete tuple runs first.
    // No production ordering or cost coefficients are changed by this check.
    internal static void RunPartyMerchantOrderChecks(ExactSearchExecutionRequest basis, string output)
        => RunPartyMerchantOrderCore(basis, output, holdout: false);

    internal static void RunPartyMerchantOrderHoldoutChecks(ExactSearchExecutionRequest basis, string output)
        => RunPartyMerchantOrderCore(basis, output, holdout: true);

    private static void RunPartyMerchantOrderCore(ExactSearchExecutionRequest basis, string output, bool holdout)
    {
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("MpP3.RealPartyContextRequired");
        int players = party.Players.Count, checks = 0;
        void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidDataException("MpP3." + label);
            checks++;
        }
        Check(players is >= 2 and <= 4 && party.Players.All(p => p.PlayersCount == players), "CapturedPlayerCount");
        ulong firstOrdinal = holdout ? (1UL << 38) + 987654321UL : 0;
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, firstOrdinal);
        const int oraclePopulation = 257;
        int measuredPopulation = holdout ? 1 << 22 : 1 << 24;
        int measuredRounds = holdout ? 3 : 5;
        string outputPrefix = holdout ? "mp-s-order-holdout" : "mp-s-order";
        var options = new SearchRunOptions(seed, measuredPopulation, int.MaxValue, 16);
        var oracleBatch = new SearchBatch(firstOrdinal, oraclePopulation);
        var roots = Enumerable.Range(0, oraclePopulation).Select(i => Beta111Profile.Instance.ComputeRootSeed(
            VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, firstOrdinal + (ulong)i))).ToArray();
        var observed = party.Players.ToDictionary(p => p.PlayerSlotIndex, p =>
        {
            var authority = Beta111MerchantColorlessAuthority.From(p);
            Check(authority.HasExactV1Inputs, "CapturedAuthority:" + p.PlayerSlotIndex);
            var projection = Beta111NormalMerchantColorlessSequenceProjector.Project(roots[0], authority, 5);
            Check(projection.Merchants.Count == 5, "FiveMerchantObservations:" + p.PlayerSlotIndex);
            return projection.Merchants.Select(m => m.UncommonColorless).ToArray();
        });
        SearchQuery Personal(int slot, bool ordered) => ordered
            ? SearchQuery.Empty with { MerchantColorlessSequenceConditions = [new(5,
                CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon,
                observed[slot].Select(k => (ModelKey?)k).ToArray())] }
            : SearchQuery.Empty with { MerchantColorlessConditions = [new(5,
                MerchantColorlessSlot.Uncommon, observed[slot][4])] };
        var assembly = typeof(FamilyCandidateSet).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".MerchantShopColorless.comp.glsl", StringComparison.Ordinal));
        string rawShader;
        using (var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!)) rawShader = reader.ReadToEnd();
        string SourceDigest(string source) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(source)));
        var oracleRows = new List<object>();
        var warmRows = new List<object>();
        var measuredRows = new List<object>();
        var quoteRows = new List<object>();
        var producerRows = new List<object>();
        var shapes = new List<object>();
        foreach (string shape in new[] { "WideThenRare", "RareThenWide", "AllOrderedSymmetric" })
        {
            var query = SearchQuery.Empty with { Players = party.Players.Select(p => new PlayerOfferQuery(
                p.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = Personal(p.PlayerSlotIndex,
                    shape == "AllOrderedSymmetric" || p.PlayerSlotIndex == (shape == "WideThenRare" ? 1 : 0)) }).ToArray() };
            var parent = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options).Plan
                ?? throw new InvalidDataException("MpP3.FactoryRejected:" + shape);
            var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(parent, false);
            var originalFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(parent, true);
            var movedFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(parent, true);
            var cpu = cpuFamilies.Single(f => f.FamilyId == "S.MerchantShopColorless");
            var original = originalFamilies.OfType<PartyFamily>().Single(f => f.FamilyId == "S.MerchantShopColorless");
            var donor = movedFamilies.OfType<PartyFamily>().Single(f => f.FamilyId == "S.MerchantShopColorless");
            // Copy actual tuples from a fresh registration, not newly projected
            // requests or children shared with the reference wrapper. The donor
            // has never invoked/bound resources; the new wrapper owns its children.
            var originalStages = ((ExactSearchExecutionRequest Request, IFamilyInvocation Family)[])
                typeof(PartyFamily).GetField("_stages", Instance)!.GetValue(original)!;
            var donorStages = ((ExactSearchExecutionRequest Request, IFamilyInvocation Family)[])
                typeof(PartyFamily).GetField("_stages", Instance)!.GetValue(donor)!;
            Check(originalStages.Length == players && donorStages.Length == players &&
                originalStages.Select(s => s.Request.Authority.PlayerSlotIndex).SequenceEqual(Enumerable.Range(0, players)) &&
                donorStages.Select(s => s.Request.Authority.PlayerSlotIndex).SequenceEqual(Enumerable.Range(0, players)),
                "OriginalSlotOrder:" + shape);
            Check(originalStages.All(a => donorStages.All(b => !ReferenceEquals(a.Family, b.Family))), "IndependentChildren:" + shape);
            var movedStages = new[] { donorStages[1], donorStages[0] }.Concat(donorStages.Skip(2)).ToArray();
            var moved = new PartyFamily(parent, movedStages);
            Check((bool)Get(original, "UsesPrivate")! && (bool)Get(moved, "UsesPrivate")!, "CompletePrivateChains:" + shape);
            var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true, cpuWorkerCount: 16);
            IFamilyInvocation? compactProducer = null;
            try
            {
                ExactSearchExecutionRequest? producerRequest = null;
                if (holdout)
                {
                    // A separately registered real N offer gate produces Compact
                    // input. Do not prefilter using an S child whose predicate would
                    // make this wrapper's unconditional child quote inapplicable.
                    var offer = ModernNeowIdentityPredictor.PredictModernCore(seed, party.Players[0], Beta111Profile.Instance, true).RelicKeys[0];
                    var producerQuery = SearchQuery.Empty with { Players = party.Players.Select(p =>
                        new PlayerOfferQuery(p.PlayerSlotIndex, p.PlayerSlotIndex == 0
                            ? new ModelKeySetFilter([offer], [], []) : ModelKeySetFilter.Empty)).ToArray() };
                    producerRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(producerQuery, context), options).Plan
                        ?? throw new InvalidDataException("MpP3.CompactProducerCompile");
                    compactProducer = FamilyExecutionCoordinator.CreateRegisteredFamilies(producerRequest, true)
                        .Single(f => f.FamilyId == "N.Neow");
                    if (!compactProducer.ConditionPerformance.UsesGpu)
                        throw new InvalidDataException("MpP3.CompactProducerUnavailable");
                }
                FamilyCandidateSet ActualCompact(SearchBatch batch)
                {
                    var value = compactProducer!.InvokeAsync(execution, new(batch, producerRequest!),
                        FamilyCandidateSet.Dense(batch), default).GetAwaiter().GetResult();
                    Check(value.Batch == batch && !value.IsDense && value.EnumerateLogicalOrdinals().Contains(0UL), "RealCompactProducerPopulation:" + shape);
                    producerRows.Add(new { Shape = shape, Input = batch.BatchCandidateCount, Output = value.Count,
                        Digest = Digest(value), Slot = producerRequest!.Authority.PlayerSlotIndex,
                        Provenance = "ActualIndependentNOfferGateGpuOutput;GeneratedOutsideConsumerTimers" });
                    return value;
                }
                var cpuPlans = originalStages.Select(s => MerchantShopColorlessCpuPlan.TryCompile(s.Request)
                    ?? throw new InvalidDataException("MpP3.CpuPlanMissing")).ToArray();
                var sourceMatches = originalStages.Select(s => roots.Select(root =>
                    MerchantShopColorlessQueryEvaluator.Evaluate(s.Request.Evaluation, root, s.Request.Authority).Disposition switch {
                        SearchDisposition.Match => true, SearchDisposition.NoMatch => false,
                        _ => throw new InvalidDataException("MpP3.OracleNotExact") }).ToArray()).ToArray();
                for (int child = 0; child < players; child++)
                    Check(roots.Select(cpuPlans[child].Matches).SequenceEqual(sourceMatches[child]), "CpuSourceFullPopulation:" + shape + ":" + child);
                var expected = Enumerable.Range(0, oraclePopulation).Select(i => sourceMatches.All(matches => matches[i])).ToArray();
                Check(expected[0] && ProductionExactSearchEvaluator.Evaluate(parent,
                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch, "WholeTableExactPositive:" + shape);
                foreach (var input in new[] { FamilyCandidateSet.Dense(oracleBatch), holdout ? ActualCompact(oracleBatch) :
                    FamilyCandidateSet.FromSortedAbi1(oracleBatch, Enumerable.Range(0, 129).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var window = new FamilyObservationWindow(input.Batch, parent);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var cpuResult = cpu.InvokeAsync(execution, window, input, default).GetAwaiter().GetResult();
                    double cpuMs = watch.Elapsed.TotalMilliseconds;
                    var wanted = input.EnumerateLogicalOrdinals().Where(i => expected[(int)i]).ToArray();
                    Check(cpuResult.Batch == input.Batch && cpuResult.EnumerateLogicalOrdinals().SequenceEqual(wanted), "CpuWrapperOracle:" + shape);
                    oracleRows.Add(new { Shape = shape, Mode = "CpuOnly", Dense = input.IsDense, Input = input.Count,
                        Output = cpuResult.Count, CanonicalMs = cpuMs, Digest = Digest(cpuResult), FullPopulationParity = true });
                    foreach (var variant in new[] { (Name: "Original", Family: original), (Name: "Slot1MovedFirst", Family: moved) })
                    {
                        var actual = variant.Family.InvokeAsync(execution, window, input, default).GetAwaiter().GetResult();
                        Check(actual.Batch == input.Batch && actual.EnumerateLogicalOrdinals().SequenceEqual(wanted), "GpuOracle:" + shape + ":" + variant.Name);
                        oracleRows.Add(new { Shape = shape, Mode = variant.Name, Dense = input.IsDense, Input = input.Count,
                            Output = actual.Count, Digest = Digest(actual), FullPopulationParity = true });
                    }
                }
                var denseMeasured = FamilyCandidateSet.Dense(new SearchBatch(firstOrdinal, measuredPopulation));
                var measuredInputs = holdout ? new[] { denseMeasured, ActualCompact(denseMeasured.Batch) } : new[] { denseMeasured };
                int rootWindow = FamilyExecutionCoordinator.ResolveExecutionWindowSize(parent, [original]);
                foreach (var measuredInput in measuredInputs)
                {
                    if (holdout)
                    {
                        foreach (var variant in new[] { (Name: "Original", Family: original), (Name: "Slot1MovedFirst", Family: moved) })
                        {
                            FamilyUnitCost? price = null;
                            string? reason = null;
                            try
                            {
                                price = variant.Family.QuoteUnit(measuredInput.Count, !measuredInput.IsDense,
                                    new HashSet<string>(StringComparer.Ordinal));
                                if (price is null) reason = "WholeWrapperQuoteUnavailable;NoSoloChildQuoteSubstitution";
                                else if (!double.IsFinite(price.Milliseconds) || price.Milliseconds < 0)
                                { reason = "WholeWrapperQuoteInvalid"; price = null; }
                            }
                            catch (Exception error) when (error is not OutOfMemoryException)
                            { reason = "AuxiliaryQuoteFailed:" + error; }
                            quoteRows.Add(new { Shape = shape, Mode = variant.Name, Dense = measuredInput.IsDense,
                                Input = measuredInput.Count, RootWindow = rootWindow, CanonicalMs = price?.Milliseconds,
                                Reason = reason, Evidence = price?.Evidence,
                                Boundary = "ExistingWholeWrapperModel;NoCoefficientsFitted;SingleUnitReferenceBeforeMeasuredRounds;SetupExcluded" });
                        }
                    }
                    var measuredWindow = new FamilyObservationWindow(measuredInput.Batch, parent);
                    string? stableDigest = null;
                    for (int round = -1; round < measuredRounds; round++)
                    {
                        FamilyCandidateSet? first = null;
                        foreach (bool moveFirst in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
                        {
                            var family = moveFirst ? moved : original;
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            var actual = family.InvokeAsync(execution, measuredWindow, measuredInput, default).GetAwaiter().GetResult();
                            double ms = watch.Elapsed.TotalMilliseconds;
                            string digest = Digest(actual); // Full ordered ABI1; outside the timed boundary.
                            Check(actual.Batch == measuredInput.Batch && actual.EnumerateLogicalOrdinals().Contains(0UL), "MeasuredKnownPositive:" + shape);
                            stableDigest ??= digest;
                            Check(digest == stableDigest && (first is null || actual.ExportAbi1().Span.SequenceEqual(first.ExportAbi1().Span)),
                                "MeasuredCompleteAbi1Parity:" + shape + ":" + round);
                            first = actual;
                            Console.WriteLine($"MP_P3_ROUND holdout={holdout};shape={shape};dense={measuredInput.IsDense};mode={(moveFirst ? "Slot1MovedFirst" : "Original")};round={round};input={measuredInput.Count};output={actual.Count};canonicalMs={ms:G9};digest={digest}");
                            if (round < 0)
                            {
                                warmRows.Add(new { Shape = shape, Dense = measuredInput.IsDense, Mode = moveFirst ? "Slot1MovedFirst" : "Original", Round = round,
                                    Input = measuredInput.Count, Output = actual.Count, CanonicalMs = ms, Digest = digest, ExcludedFromMeasurements = true });
                                continue; // One full-population warm invocation per variant.
                            }
                            measuredRows.Add(new { Shape = shape, Dense = measuredInput.IsDense, Mode = moveFirst ? "Slot1MovedFirst" : "Original", Round = round,
                                Input = measuredInput.Count, Output = actual.Count, CanonicalMs = ms, Digest = digest,
                                FullAbi1Parity = true, Revision = family.ResolveConditionPerformance(!measuredInput.IsDense).PhysicalImplementationRevision });
                        }
                    }
                }
                shapes.Add(new { Shape = shape, PositiveOrdinal = firstOrdinal, PositiveSeed = seed, WholeTableExactPositive = true,
                    OriginalSlots = originalStages.Select(s => s.Request.Authority.PlayerSlotIndex).ToArray(),
                    MovedSlots = movedStages.Select(s => s.Request.Authority.PlayerSlotIndex).ToArray(),
                    OriginalSetupMs = original.CapturePerformanceObservation().SetupMs,
                    MovedSetupMs = moved.CapturePerformanceObservation().SetupMs,
                    Children = originalStages.Select(s => new { Slot = s.Request.Authority.PlayerSlotIndex,
                        s.Request.SnapshotFingerprint,
                        PoolUncommon = Beta111MerchantColorlessAuthority.From(s.Request.Authority).UncommonPool.Count,
                        PoolRare = Beta111MerchantColorlessAuthority.From(s.Request.Authority).RarePool.Count,
                        s.Family.Survival,
                        Revision = s.Family.ConditionPerformance.PhysicalImplementationRevision,
                        EarlyConfiguredSourceSha256 = SourceDigest(MerchantShopColorlessGpuExecutor.ConfigureEarlyRejection(rawShader,
                            s.Request.Evaluation, s.Request.Authority.PlayersCount)) }).ToArray() });
            }
            finally
            {
                try
                {
                    if (compactProducer is not null) compactProducer.DisposeAsync(execution).GetAwaiter().GetResult();
                    // donor is an unbound container only. Dispose its children
                    // exactly once via moved, never through both wrappers.
                    foreach (var family in originalFamilies.Concat(cpuFamilies).Concat(movedFamilies.Where(f => !ReferenceEquals(f, donor))).Append(moved))
                        family.DisposeAsync(execution).GetAwaiter().GetResult();
                }
                finally { execution.DisposeAsync().GetAwaiter().GetResult(); }
            }
            File.WriteAllText(Path.Combine(output, outputPrefix + "-" + shape + ".json"), JsonSerializer.Serialize(
                new ProductionSearchReplayWorkload(1, query, options), ProductionSearchReplay.JsonOptions));
        }
        File.WriteAllText(Path.Combine(output, outputPrefix + "-context.json"), JsonSerializer.Serialize(
            new ProductionSearchReplayContext(1, "SavedNativePartyContext;MP-P3;Players=" + players, context), ProductionSearchReplay.JsonOptions));
        File.WriteAllText(Path.Combine(output, outputPrefix + ".json"), JsonSerializer.Serialize(new {
            Players = players, Checks = checks, OraclePopulation = oraclePopulation, MeasuredPopulation = measuredPopulation,
            FirstOrdinal = firstOrdinal, PositiveSeed = seed, Holdout = holdout,
            Source = "OriginalFactory;IndependentRegistrations;ActualChildTuplePermutation;CurrentProductionShader",
            Boundary = $"CanonicalInvokeAsync;OneWarmThen{measuredRounds}AlternatingRounds;SetupAndDigestExcluded;NoExactInTimedBoundary",
            PopulationReduction = false, ProductionSelectionChanged = false, SameRunRepricing = false,
            CpuOracleIncludesColdInvocation = true,
            RawShaderSha256 = SourceDigest(rawShader), Shapes = shapes, CpuAndSourceOracleRows = oracleRows,
            WarmupRows = warmRows, Rows = measuredRows, ExistingModelQuotes = quoteRows, CompactProducers = producerRows
        }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"MP_P3_PASS players={players};checks={checks};rows={measuredRows.Count};population={measuredPopulation};fullAbi1Parity=true;productionSelectionChanged=false");
    }

    // Run separately with each saved 2/3/4-player native context. No authority,
    // pool, slot or character is synthesized from a single-player fixture.
    internal static void RunPartyMerchantChecks(ExactSearchExecutionRequest basis, string output)
    {
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("MpS1.RealPartyContextRequired");
        int players = party.Players.Count, checks = 0;
        void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidDataException("MpS1." + label);
            checks++;
        }
        Check(players is >= 2 and <= 4 && party.Players.All(p => p.PlayersCount == players), "CapturedPlayerCount");
        const int population = 257;
        var batch = new SearchBatch(0, population);
        var roots = Enumerable.Range(0, population).Select(i => Beta111Profile.Instance.ComputeRootSeed(
            VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, (ulong)i))).ToArray();
        var options = new SearchRunOptions("000000000000", population, 1, 1);
        SearchQuery WholeQuery(Func<int, SearchQuery> conditions) => SearchQuery.Empty with {
            Players = party.Players.Select(p => new PlayerOfferQuery(p.PlayerSlotIndex, ModelKeySetFilter.Empty) {
                Conditions = conditions(p.PlayerSlotIndex) }).ToArray() };
        ExactSearchExecutionRequest Compile(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(query, context), options).Plan ?? throw new InvalidDataException("MpS1.FactoryRejected");
        var rows = new List<object>();
        var chainRows = new List<object>();
        var rootZero = new Beta111ShopColorlessProjection[players];
        var assembly = typeof(FamilyCandidateSet).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".MerchantShopColorless.comp.glsl", StringComparison.Ordinal));
        string rawShader;
        using (var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!)) rawShader = reader.ReadToEnd();
        using (var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("MpS1.DeviceUnavailable"))
        for (int slot = 0; slot < players; slot++)
        {
            int currentSlot = slot;
            var authority = Beta111MerchantColorlessAuthority.From(party.Players[slot]);
            Check(authority.HasExactV1Inputs && authority.PlayersCount == players && authority.PlayerSlotIndex == slot,
                "CapturedAuthority:" + slot);
            var projections = roots.Select(root => Beta111NormalMerchantColorlessSequenceProjector.Project(root, authority, 5)).ToArray();
            Check(projections.All(p => p.Merchants.Count == 5), "FiveMerchantObservations:" + slot);
            rootZero[slot] = projections[0];
            ModelKey[] Lane(int rootIndex, MerchantColorlessSlot lane) => projections[rootIndex].Merchants.Select(m =>
                lane == MerchantColorlessSlot.Uncommon ? m.UncommonColorless : m.RareColorless).ToArray();
            var cases = new List<(string Name, SearchQuery Query, bool Early, ulong? Positive, bool RejectZero)>();
            foreach (var lane in new[] { MerchantColorlessSlot.Uncommon, MerchantColorlessSlot.Rare })
            {
                var observed = Lane(0, lane);
                var pool = lane == MerchantColorlessSlot.Uncommon ? authority.UncommonPool : authority.RarePool;
                Check(pool.Distinct().Count() >= 2, "AlternativeCapturedIdentity:" + slot + ":" + lane);
                void Sequence(string name, CombatRewardSequenceOrderMode order, ModelKey?[] targets, bool early,
                    ulong? positive = 0, bool rejectZero = false) => cases.Add((lane + name, SearchQuery.Empty with {
                        MerchantColorlessSequenceConditions = [new(targets.Length, order, lane, targets)] }, early, positive, rejectZero));
                cases.Add((lane + "FixedEarly", SearchQuery.Empty with { MerchantColorlessConditions = [
                    new(1, lane, observed[0]), new(5, lane, observed[4])] }, true, 0, false));
                cases.Add((lane + "FixedLastNegativeShape", SearchQuery.Empty with {
                    MerchantColorlessConditions = [new(5, lane, observed[4])] }, false, 0, false));
                Sequence("OrderedPositive", CombatRewardSequenceOrderMode.Ordered,
                    observed.Select(k => (ModelKey?)k).ToArray(), true);
                var firstFailure = observed.Select(k => (ModelKey?)k).ToArray();
                firstFailure[0] = pool.First(k => k != observed[0]);
                Sequence("OrderedFirstFailure", CombatRewardSequenceOrderMode.Ordered, firstFailure, true, null, true);
                var lastFailure = observed.Select(k => (ModelKey?)k).ToArray();
                lastFailure[4] = pool.First(k => k != observed[4]);
                Sequence("OrderedLastFailure", CombatRewardSequenceOrderMode.Ordered, lastFailure, true, null, true);
                int repeatedRoot = Enumerable.Range(0, population).First(i => Lane(i, lane).Distinct().Count() < 5);
                var repeated = Lane(repeatedRoot, lane).GroupBy(k => k).First(g => g.Count() >= 2).Key;
                Sequence("UnorderedMultiplicityPositive", CombatRewardSequenceOrderMode.Unordered,
                    [repeated, repeated, null, null, null], true, (ulong)repeatedRoot);
                Sequence("UnorderedSingleNegativeShape", CombatRewardSequenceOrderMode.Unordered,
                    [observed[0], null, null, null, null], false);
                var otherPool = lane == MerchantColorlessSlot.Uncommon ? authority.RarePool : authority.UncommonPool;
                var missing = otherPool.First(k => !pool.Contains(k));
                Sequence("MissingOrdered", CombatRewardSequenceOrderMode.Ordered, [missing, null], true, null, true);
                Sequence("MissingUnordered", CombatRewardSequenceOrderMode.Unordered, [missing, null], false, null, true);
            }
            var uncommon = Lane(0, MerchantColorlessSlot.Uncommon);
            var rare = Lane(0, MerchantColorlessSlot.Rare);
            cases.Add(("MixedLanesHorizons", SearchQuery.Empty with {
                MerchantColorlessConditions = [new(5, MerchantColorlessSlot.Uncommon, uncommon[4])],
                MerchantColorlessSequenceConditions = [
                    new(2, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Uncommon, [uncommon[0], uncommon[1]]),
                    new(5, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Rare, [rare[0], null, null, null, rare[4]])]
            }, true, 0, false));
            foreach (var test in cases)
            {
                var parent = Compile(WholeQuery(s => s == currentSlot ? test.Query : SearchQuery.Empty));
                var request = ExactSearchExecutionRequestFactory.ForPlayer(parent, parent.CompiledSearch.PlayerSearches[slot]);
                Check(request.Authority.PlayerSlotIndex == slot && request.Authority.PlayersCount == players &&
                    request.Authority.CatalogFingerprint == party.Players[slot].CatalogFingerprint, "FactorySlotBinding:" + test.Name);
                string configured = MerchantShopColorlessGpuExecutor.ConfigureEarlyRejection(rawShader, request.Evaluation, players);
                Check(configured.StartsWith("#version 450\n#define RT2_S_EARLY_REJECTION " + (test.Early ? "1" : "0") + "\n",
                    StringComparison.Ordinal), "StructureQualification:" + slot + ":" + test.Name);
                var cpu = MerchantShopColorlessCpuPlan.TryCompile(request) ?? throw new InvalidDataException("MpS1.CpuPlanMissing");
                var expected = roots.Select(root => MerchantShopColorlessQueryEvaluator.Evaluate(request.Evaluation, root, request.Authority).Disposition)
                    .Select(d => d switch { SearchDisposition.Match => true, SearchDisposition.NoMatch => false,
                        _ => throw new InvalidDataException("MpS1.OracleNotExact") }).ToArray();
                Check(roots.Select(cpu.Matches).SequenceEqual(expected), "CpuCanonicalFullPopulation:" + test.Name);
                if (test.Positive is ulong positive) Check(expected[(int)positive], "KnownPositive:" + test.Name);
                if (test.RejectZero) Check(!expected[0], "KnownFirstOrLastFailure:" + test.Name);
                var compactOrdinals = Enumerable.Range(0, 129).Select(i => (ulong)i * 2).Concat(
                    test.Positive is ulong keep ? new[] { keep } : Array.Empty<ulong>()).Distinct().Order().ToArray();
                var dense = FamilyCandidateSet.Dense(batch);
                var compact = FamilyCandidateSet.FromSortedAbi1(batch, compactOrdinals);
                foreach (var path in new[] { MerchantShopColorlessCompactionPath.AtomicAppendHostSort,
                    MerchantShopColorlessCompactionPath.StableOrderedCompaction })
                {
                    using var gpu = MerchantShopColorlessGpuExecutor.Create(rd, request, path);
                    foreach (var input in path == MerchantShopColorlessCompactionPath.AtomicAppendHostSort ? new[] { dense, compact } : new[] { dense })
                    {
                        var actual = gpu.Execute(input, default, out var metrics);
                        var wanted = input.EnumerateLogicalOrdinals().Where(i => expected[(int)i]).ToArray();
                        Check(actual.EnumerateLogicalOrdinals().SequenceEqual(wanted) && metrics.ProcessedCandidates == input.Count,
                            "GpuFullPopulation:" + slot + ":" + test.Name + ":" + path);
                        rows.Add(new { Players = players, Slot = slot, test.Name, test.Early, test.Positive,
                            Path = path.ToString(), Dense = input.IsDense, Input = input.Count, Output = actual.Count, FullPopulationParity = true });
                    }
                }
            }
            Console.WriteLine($"MP_S1_SLOT_PASS players={players};slot={slot};cases={cases.Count};nativePools=true;cpuCanonicalGpu=true");
        }

        ModelKey[] Observed(int slot, MerchantColorlessSlot lane) => rootZero[slot].Merchants.Select(m =>
            lane == MerchantColorlessSlot.Uncommon ? m.UncommonColorless : m.RareColorless).ToArray();
        SearchQuery OrderedAll() => WholeQuery(slot => SearchQuery.Empty with { MerchantColorlessSequenceConditions = [
            new(5, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon,
                Observed(slot, MerchantColorlessSlot.Uncommon).Select(k => (ModelKey?)k).ToArray())] });
        SearchQuery LastAll() => WholeQuery(slot => SearchQuery.Empty with { MerchantColorlessConditions = [
            new(5, MerchantColorlessSlot.Uncommon, Observed(slot, MerchantColorlessSlot.Uncommon)[4])] });
        SearchQuery HighOutputAll() => WholeQuery(slot => SearchQuery.Empty with { MerchantColorlessSequenceConditions = [
            new(5, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Uncommon,
                [Observed(slot, MerchantColorlessSlot.Uncommon)[0], null, null, null, null])] });
        foreach (var shape in new[] { (Name: "S-ordered5-all-slots", Query: OrderedAll()),
            (Name: "S-last-slot-all-slots", Query: LastAll()), (Name: "S-high-output-all-slots", Query: HighOutputAll()) })
        {
            var parent = Compile(shape.Query);
            var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(parent, false);
            var gpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(parent, true);
            var cpu = cpuFamilies.Single(f => f.FamilyId == "S.MerchantShopColorless");
            var gpu = gpuFamilies.Single(f => f.FamilyId == "S.MerchantShopColorless");
            Check(gpu is PartyFamily partyFamily && partyFamily.PersonalStages.Count() == players && (bool)Get(gpu, "UsesPrivate")!,
                "CompletePrivateChain:" + shape.Name);
            var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true, cpuWorkerCount: 1);
            try
            {
                var children = parent.CompiledSearch.PlayerSearches.Take(players).Select(p => ExactSearchExecutionRequestFactory.ForPlayer(parent, p)).ToArray();
                var expected = roots.Select(root => children.All(child => MerchantShopColorlessQueryEvaluator.Evaluate(
                    child.Evaluation, root, child.Authority).Disposition == SearchDisposition.Match)).ToArray();
                Check(expected[0], "WholeTableKnownPositive:" + shape.Name);
                Check(ProductionExactSearchEvaluator.Evaluate(parent, TrustedRootHashInput.FromCanonicalSeed(
                    Beta111Profile.Instance, "000000000000")).IsMatch, "ProductionExactKnownPositive:" + shape.Name);
                bool early = shape.Name == "S-ordered5-all-slots";
                foreach (var child in children)
                    Check(MerchantShopColorlessGpuExecutor.ConfigureEarlyRejection(rawShader, child.Evaluation, players)
                        .StartsWith("#version 450\n#define RT2_S_EARLY_REJECTION " + (early ? "1" : "0") + "\n", StringComparison.Ordinal),
                        "WholeTableNegativeShapeQualification:" + shape.Name);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 129).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var window = new FamilyObservationWindow(batch, parent);
                    var cpuResult = cpu.InvokeAsync(execution, window, input, default).GetAwaiter().GetResult();
                    var gpuResult = gpu.InvokeAsync(execution, window, input, default).GetAwaiter().GetResult();
                    var wanted = input.EnumerateLogicalOrdinals().Where(i => expected[(int)i]).ToArray();
                    Check(cpuResult.EnumerateLogicalOrdinals().SequenceEqual(wanted) && gpuResult.EnumerateLogicalOrdinals().SequenceEqual(wanted),
                        "WholeTableCpuGpuCanonical:" + shape.Name);
                    chainRows.Add(new { Shape = shape.Name, Players = players, Early = early, Dense = input.IsDense, Input = input.Count,
                        Output = gpuResult.Count, PositiveOrdinal = 0, ProductionExactPositive = true,
                        CompletePrivateChain = true, FullPopulationParity = true });
                }
            }
            finally
            {
                try { foreach (var family in gpuFamilies.Concat(cpuFamilies)) family.DisposeAsync(execution).GetAwaiter().GetResult(); }
                finally { execution.DisposeAsync().GetAwaiter().GetResult(); }
            }
            var workload = new ProductionSearchReplayWorkload(1, shape.Query,
                new("000000000000", 16 * 1024 * 1024, int.MaxValue, 16));
            File.WriteAllText(Path.Combine(output, shape.Name + ".json"), JsonSerializer.Serialize(workload, ProductionSearchReplay.JsonOptions));
        }
        File.WriteAllText(Path.Combine(output, "S-context.json"), JsonSerializer.Serialize(
            new ProductionSearchReplayContext(1, "SavedNativePartyContext;MP-S1;Players=" + players, context), ProductionSearchReplay.JsonOptions));
        File.WriteAllText(Path.Combine(output, "mp-s1.json"), JsonSerializer.Serialize(new {
            Players = players, Population = population, Checks = checks, Source = "SavedNativePartyContext;OriginalFactory;ProductionPackingShader",
            PositiveOrdinal = 0, Rows = rows, PrivateChains = chainRows, ThroughputClaim = false
        }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"MP_S1_PASS players={players};slots={players};rows={rows.Count};privateChains={chainRows.Count};checks={checks};positiveOrdinal=0;throughputClaim=false");
    }
}
