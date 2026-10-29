using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static partial class FamilyPhysicalMatrix
{
    // Real factory, production packing and production shader. Repro mode records
    // all mismatches before the production fix; check mode requires full parity.
    // Synthetic ordered pools preserve real captured identities and draw weights.
    private static void RunMerchantIdentityChecks(ExactSearchExecutionRequest basis, string output, bool requireParity)
    {
        if (basis.Authority.PlayersCount != 1 || basis.Authority.PlayerSlotIndex != 0)
            throw new InvalidOperationException("SIdentity.SoloFixtureRequired");
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var assembly = typeof(FamilyCandidateSet).Assembly;
        Type T(string name) => assembly.GetType("RolltheSpire2.Search.FamilyExecution." + name, true)!;
        var create = T("MerchantShopColorlessGpuExecutor").GetMethods(statics).Single(m =>
            m.Name == "Create" && m.GetParameters().Length == 3);
        var configureEarly = T("MerchantShopColorlessGpuExecutor").GetMethod("ConfigureEarlyRejection", statics)!;
        string rawShader = FamilyGpuComputeUtility.LoadEmbeddedShader("MerchantShopColorless.comp.glsl");
        SrCheck(rawShader.Contains("#ifndef RT2_S_EARLY_REJECTION\n#define RT2_S_EARLY_REJECTION 0", StringComparison.Ordinal) ||
            rawShader.Contains("#ifndef RT2_S_EARLY_REJECTION\r\n#define RT2_S_EARLY_REJECTION 0", StringComparison.Ordinal),
            "SIdentity.RawEarlyRejectionDisabled");
        var pathType = T("MerchantShopColorlessCompactionPath");
        var format = T("VisibleSeedCandidateCodec").GetMethod("FormatOrdinal", statics)!;
        var effects = basis.Authority.EffectAuthority ?? throw new InvalidDataException("SIdentity.EffectsMissing");
        var source = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool
            ?? throw new InvalidDataException("SIdentity.PoolMissing");
        var u = source.Where(c => c.Rarity == EffectCardRarity.Uncommon).DistinctBy(c => c.CardKey).Take(3).ToArray();
        var r = source.Where(c => c.Rarity == EffectCardRarity.Rare).DistinctBy(c => c.CardKey).Take(3).ToArray();
        SrCheck(u.Length == 3 && r.Length == 3, "SIdentity.DonorIdentities");
        var poolU = new[] { u[0], u[1], u[0], u[1], u[0] }.Select((c, i) => c with { PoolOrder = i }).ToArray();
        var poolR = new[] { r[0], r[0], r[1], r[0], r[1] }.Select((c, i) => c with { PoolOrder = i }).ToArray();
        var context = basis.CompiledSearch.Context with { Authority = basis.Authority.WithEffectAuthority(effects with {
            MerchantColorlessOrderedPool = poolU.Concat(poolR).ToArray(),
            MerchantColorlessOrderedPoolExact = true, MerchantColorlessPoolIdentityHooksExact = true,
            MerchantColorlessRarityIdentityHooksExact = true, MerchantInitialInventoryShapeExact = true }) };
        var authority = Beta111MerchantColorlessAuthority.From(context.Authority);
        SrCheck(authority.HasExactV1Inputs && authority.UncommonPool.Count == 5 && authority.RarePool.Count == 5,
            "SIdentity.DuplicateAuthorityRetained");
        var batch = new SearchBatch((1UL << 40) + 12345UL, 1027);
        var dense = FamilyCandidateSet.Dense(batch);
        var compact = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, 343).Select(i => (ulong)i * 3).ToArray());
        var roots = Enumerable.Range(0, batch.BatchCandidateCount).Select(i => Beta111Profile.Instance.ComputeRootSeed(
            (string)format.Invoke(null, [Beta111Profile.Instance, batch.BatchBase + (ulong)i])!)).ToArray();
        var uniqueContext = context with { Authority = basis.Authority.WithEffectAuthority(effects with {
            MerchantColorlessOrderedPool = u.Select((c, i) => c with { PoolOrder = i })
                .Concat(r.Select((c, i) => c with { PoolOrder = i })).ToArray(),
            MerchantColorlessOrderedPoolExact = true, MerchantColorlessPoolIdentityHooksExact = true,
            MerchantColorlessRarityIdentityHooksExact = true, MerchantInitialInventoryShapeExact = true }) };
        var uniqueAuthority = Beta111MerchantColorlessAuthority.From(uniqueContext.Authority);
        var witness = Beta111NormalMerchantColorlessSequenceProjector.Project(roots[0], uniqueAuthority, 5);
        var cases = new List<(string Name, SearchQuery Query, bool Unique, bool EarlyExpected)>();
        foreach (var lane in new[] { MerchantColorlessSlot.Uncommon, MerchantColorlessSlot.Rare })
        {
            ModelKey target = lane == MerchantColorlessSlot.Uncommon ? u[0].CardKey : r[0].CardKey;
            ModelKey missing = lane == MerchantColorlessSlot.Uncommon ? u[2].CardKey : r[2].CardKey;
            void Sequence(string name, CombatRewardSequenceOrderMode order, ModelKey?[] slots, bool early = true) => cases.Add((lane + name,
                SearchQuery.Empty with { MerchantColorlessSequenceConditions = [new(slots.Length, order, lane, slots)] }, false, early));
            cases.Add((lane + "DuplicateFixed", SearchQuery.Empty with { MerchantColorlessConditions = [new(1, lane, target)] }, false, false));
            Sequence("DuplicateOrdered", CombatRewardSequenceOrderMode.Ordered, [target, null, target]);
            Sequence("DuplicateUnordered", CombatRewardSequenceOrderMode.Unordered, [target, target, null]);
            Sequence("MissingOrdered", CombatRewardSequenceOrderMode.Ordered, [missing, null]);
            Sequence("MissingUnordered", CombatRewardSequenceOrderMode.Unordered, [missing, null], early: false);
            Sequence("Wildcard", CombatRewardSequenceOrderMode.Ordered, [null, target, null]);
            void UniqueSequence(string name, CombatRewardSequenceOrderMode order, ModelKey?[] slots, bool early = true) => cases.Add((lane + name,
                SearchQuery.Empty with { MerchantColorlessSequenceConditions = [new(slots.Length, order, lane, slots)] }, true, early));
            var observed = witness.Merchants.Select(m => (ModelKey?)(lane == MerchantColorlessSlot.Uncommon
                ? m.UncommonColorless : m.RareColorless)).ToArray();
            var pool = lane == MerchantColorlessSlot.Uncommon ? uniqueAuthority.UncommonPool : uniqueAuthority.RarePool;
            UniqueSequence("UniqueOrderedPositive", CombatRewardSequenceOrderMode.Ordered, observed);
            var firstFailure = observed.ToArray(); firstFailure[0] = pool.First(k => k != observed[0]);
            UniqueSequence("UniqueOrderedFirstFailure", CombatRewardSequenceOrderMode.Ordered, firstFailure);
            var lastFailure = observed.ToArray(); lastFailure[4] = pool.First(k => k != observed[4]);
            UniqueSequence("UniqueOrderedLastFailure", CombatRewardSequenceOrderMode.Ordered, lastFailure);
            UniqueSequence("UniqueUnorderedPositive", CombatRewardSequenceOrderMode.Unordered, observed);
            for (int required = 1; required <= 5; required++)
            {
                var prefix = new ModelKey?[5]; Array.Fill(prefix, target, 0, required);
                UniqueSequence("UniquePrefixDeficit" + required, CombatRewardSequenceOrderMode.Unordered, prefix, early: required > 1);
            }
            cases.Add((lane + "UniqueMixedHorizons", SearchQuery.Empty with {
                MerchantColorlessConditions = [new(5, lane, observed[4]!.Value)],
                MerchantColorlessSequenceConditions = [
                    new(2, CombatRewardSequenceOrderMode.Unordered, lane, observed.Take(2).ToArray()),
                    new(5, CombatRewardSequenceOrderMode.Ordered, lane, [observed[0], null, null, null, observed[4]])] }, true, true));
        }
        var rows = new List<object>();
        int duplicateLosses = 0, missingExtras = 0, totalMismatches = 0;
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("SIdentity.DeviceUnavailable");
        foreach (var test in cases)
        {
            var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(test.Query, test.Unique ? uniqueContext : context), basis.RunOptions);
            var request = compiled.Plan ?? throw new InvalidDataException("SIdentity.Factory:" + test.Name + ":" + compiled.Issue);
            object?[] admission = [request, null];
            SrCheck((bool)T("MerchantShopColorlessFamily").GetMethod("TryCreate", statics)!.Invoke(null, admission)!,
                "SIdentity.FamilyAdmission:" + test.Name);
            string configured = (string)configureEarly.Invoke(null, [rawShader, request.Evaluation, request.Authority.PlayersCount])!;
            SrCheck(configured.StartsWith("#version 450\n#define RT2_S_EARLY_REJECTION " +
                (test.EarlyExpected ? "1" : "0") + "\n", StringComparison.Ordinal),
                "SIdentity.SoloEarlyRejectionQualification:" + test.Name);
            var expected = roots.Select(root => MerchantShopColorlessQueryEvaluator.Evaluate(request.Evaluation, root, request.Authority).Disposition)
                .Select(d => d switch { SearchDisposition.Match => true, SearchDisposition.NoMatch => false,
                    _ => throw new InvalidDataException("SIdentity.NonExactOracle:" + test.Name) }).ToArray();
            if (test.Name.Contains("Positive") || test.Name.Contains("MixedHorizons"))
                SrCheck(expected[0], "SIdentity.ExplicitPositiveWitness:" + test.Name);
            if (test.Name.Contains("FirstFailure") || test.Name.Contains("LastFailure"))
                SrCheck(!expected[0], "SIdentity.ExplicitFailureWitness:" + test.Name);
            foreach (string path in new[] { "AtomicAppendHostSort", "StableOrderedCompaction" })
            {
                using var gpu = (IDisposable)create.Invoke(null, [rd, request, Enum.Parse(pathType, path)])!;
                foreach (var input in path == "AtomicAppendHostSort" ? new[] { dense, compact } : new[] { dense })
                {
                    var (actual, metrics) = SrPublicExecute(gpu, input, CancellationToken.None);
                    SrCheck(actual.Batch == batch && (int)Get(metrics, "ProcessedCandidates")! == input.Count,
                        "SIdentity.Processed:" + test.Name);
                    var inputOrdinals = input.EnumerateLogicalOrdinals().ToArray();
                    var survivors = actual.EnumerateLogicalOrdinals().ToHashSet();
                    var losses = inputOrdinals.Where(i => expected[(int)i] && !survivors.Contains(i)).ToArray();
                    var extras = inputOrdinals.Where(i => !expected[(int)i] && survivors.Contains(i)).ToArray();
                    int mismatches = losses.Length + extras.Length;
                    totalMismatches += mismatches;
                    if (test.Name.Contains("Duplicate")) duplicateLosses += losses.Length;
                    if (test.Name.Contains("Missing")) missingExtras += extras.Length;
                    rows.Add(new { test.Name, test.EarlyExpected, Path = path, InputMode = input.IsDense ? "Dense" : "Compact", Input = input.Count,
                        Expected = inputOrdinals.Count(i => expected[(int)i]), Actual = actual.Count,
                        FalseNegatives = losses.Length, FalsePositives = extras.Length,
                        FirstLost = losses.Take(1).ToArray(), FirstExtra = extras.Take(1).ToArray(), Digest = Digest(actual) });
                    Console.WriteLine($"S_IDENTITY_ROW name={test.Name};path={path};dense={input.IsDense};lost={losses.Length};extra={extras.Length}");
                }
            }
        }
        Console.WriteLine("S_IDENTITY_EARLY_SCOPE_PASS raw=0;soloOpportunity=1;soloNoOpportunity=0;partyScope=RealContextNativeFixture;sourceContractOnly=true");
        File.WriteAllText(Path.Combine(output, "s-identity.json"), JsonSerializer.Serialize(new {
            Source = "CurrentProductionFactoryPackingShader", Scope = "Solo", RequireParity = requireParity,
            Device = rd.GetDeviceName(), batch.BatchBase, batch.BatchCandidateCount, DuplicateLosses = duplicateLosses,
            MissingExtras = missingExtras, TotalMismatches = totalMismatches, Rows = rows
        }, ProductionSearchReplay.JsonOptions));
        if (requireParity) SrCheck(totalMismatches == 0, "SIdentity.FullPopulationParity");
        else SrCheck(duplicateLosses > 0 && missingExtras > 0, "SIdentity.ExpectedStaticRisksNotReproduced");
        Console.WriteLine($"S_IDENTITY_{(requireParity ? "PARITY" : "REPRO")}_PASS mismatches={totalMismatches};duplicateLosses={duplicateLosses};missingExtras={missingExtras};throughputClaim=false");
    }

    // Fixed transport boundaries only, not a pricing sweep. The two real S/R
    // predicates use captured pools; the separately labelled dense S case uses
    // an empty predicate solely to prove output capacity at 100% survival.
    private static void RunSrReliability(ExactSearchExecutionRequest basis, string output)
    {
        const int transferCapacity = 1 << 22;
        const int population = transferCapacity + 67;
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        if (basis.Authority.PlayersCount != 1) throw new InvalidOperationException("SrReliability.SingleplayerFixtureRequired");
        var assembly = typeof(FamilyCandidateSet).Assembly;
        Type T(string name) => assembly.GetType("RolltheSpire2.Search.FamilyExecution." + name, true)!;
        var merchant = Beta111MerchantColorlessAuthority.From(basis.Authority);
        var relic = basis.Authority.WorldAuthority?.Beta109Generation?.PlayerRelicBuckets
            .First(bucket => bucket.BucketId.Split(':')[^1].Equals("Common", StringComparison.OrdinalIgnoreCase))
            .OrderedEntries.First().RelicKey ?? throw new InvalidOperationException("SrReliability.CommonPoolMissing");
        var query = SearchQuery.Empty with
        {
            MerchantColorlessConditions = [new(1, MerchantColorlessSlot.Uncommon, merchant.UncommonPool.First())],
            RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([relic], [], []))]
        };
        var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, basis.CompiledSearch.Context), basis.RunOptions);
        var request = compiled.Plan ?? throw new InvalidOperationException("SrReliability.Compile:" + compiled.Issue);
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request);
        if (families.Count != 2 || !families.Any(f => f.FamilyId.StartsWith("S.")) || !families.Any(f => f.FamilyId.StartsWith("R.")))
            throw new InvalidOperationException("SrReliability.ExpectedOnlySR");
        var bind = typeof(IFamilyInvocation).GetMethod("BindPrivateSerial", Instance)!;
        Type bufferType = T("PrivateOrdinalBuffer"), pathType = T("MerchantShopColorlessCompactionPath");
        var createS = T("MerchantShopColorlessGpuExecutor").GetMethods(statics)
            .Single(m => m.Name == "Create" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == pathType);
        Type relicPlanType = T("RelicFamilyPlan");
        var createR = T("RelicFamilyGpuExecutor").GetMethods(statics)
            .Single(m => m.Name == "Create" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == relicPlanType);
        object?[] planArgs = [request, null, null];
        if (!(bool)T("RelicFamilyPlanCompiler").GetMethod("TryCompile", statics)!.Invoke(null, planArgs)!)
            throw new InvalidOperationException("SrReliability.RPlan:" + planArgs[2]);

        // A nonzero/high base and sparse ordinals expose both rebasing mistakes
        // and accidentally treating compact offset as the original root ordinal.
        var batch = new SearchBatch((1UL << 40) + 12345UL, RootCapacity);
        var ordinals = new ulong[population];
        var privateOrdinals = new uint[population];
        for (int i = 0; i < population; i++) privateOrdinals[i] = checked((uint)(ordinals[i] = (ulong)i * 3UL + 17UL));
        var input = FamilyCandidateSet.FromAbi1(batch, ordinals);
        var samplePositions = Enumerable.Range(0, 32)
            .Concat(Enumerable.Range(transferCapacity - 32, 64))
            .Concat(Enumerable.Range(population - 32, 32)).Distinct().Order().ToArray();
        var sample = FamilyCandidateSet.FromAbi1(batch, samplePositions.Select(i => ordinals[i]).ToArray());
        var rows = new List<object>();
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("SrReliability.DeviceUnavailable");
        foreach (var family in families)
        {
            bool shop = family.FamilyId.StartsWith("S.");
            using var gpu = (IDisposable)(shop
                ? createS.Invoke(null, [rd, request, Enum.Parse(pathType, "AtomicAppendHostSort")])!
                : createR.Invoke(null, [rd, request, planArgs[1]])!);
            var (actual, metrics) = SrPublicExecute(gpu, input, CancellationToken.None);
            SrCheck(actual.Batch == batch && (int)Get(metrics, "InputCandidates")! == population &&
                (int)Get(metrics, "ProcessedCandidates")! == population && (int)Get(metrics, "Survivors")! == actual.Count,
                family.FamilyId + ".PublicCountsAndBatch");
            if (shop) SrCheck((int)Get(metrics, "Dispatches")! == 2, "S.TwoCompactWindows");
            else SrCheck((uint)Get(metrics, "DispatchGroups")! == transferCapacity / 512 + 1, "R.DispatchGroupsIncludeTail");

            using (var inputBuffer = (IDisposable)Activator.CreateInstance(bufferType, Instance, null, [rd], null)!)
            {
                byte[] bytes = MemoryMarshal.AsBytes(privateOrdinals.AsSpan()).ToArray();
                if (rd.BufferUpdate((Rid)Get(inputBuffer, "Buffer")!, 0, checked((uint)bytes.Length), bytes) != Error.Ok)
                    throw new InvalidOperationException("SrReliability.PrivateUpload");
                using var execution = (IDisposable)bind.Invoke(family, [rd, inputBuffer, null])!;
                object outcome = Call(execution, "Execute", batch, population, CancellationToken.None)!;
                var expected = (FamilyCandidateSet)Get(outcome, "PublicOutput")!;
                SrCheck(expected.Batch == batch && expected.ExportAbi1().Span.SequenceEqual(actual.ExportAbi1().Span),
                    family.FamilyId + ".FullPublicPrivateParity");
            }

            var cpu = ((IEnumerable<IFamilyInvocation>)typeof(IFamilyInvocation).GetProperty("CpuRealizations", Instance)!.GetValue(family)!).First();
            var cpuContext = new FamilyExecutionContext(requiresGpu: false);
            try
            {
                FamilyCandidateSet expected = cpu.InvokeAsync(cpuContext, new(batch, request), sample, CancellationToken.None).GetAwaiter().GetResult();
                var cpuSurvivors = expected.ExportAbi1().ToArray().ToHashSet();
                foreach (ulong ordinal in sample.ExportAbi1().Span)
                    SrCheck(cpuSurvivors.Contains(ordinal) == (actual.ExportAbi1().Span.BinarySearch(ordinal) >= 0),
                        family.FamilyId + ".CpuBoundary:" + ordinal);
            }
            finally
            {
                try { cpu.DisposeAsync(cpuContext).GetAwaiter().GetResult(); }
                finally { cpuContext.DisposeAsync().GetAwaiter().GetResult(); }
            }
            var (empty, emptyMetrics) = SrPublicExecute(gpu, FamilyCandidateSet.FromAbi1(batch, []), CancellationToken.None);
            SrCheck(empty.Batch == batch && empty.Count == 0 && (int)Get(emptyMetrics, "ProcessedCandidates")! == 0,
                family.FamilyId + ".Empty");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try
            {
                SrPublicExecute(gpu, input, canceled.Token);
                throw new InvalidDataException("SrReliability.CancellationMissing:" + family.FamilyId);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { }
            rows.Add(new { family.FamilyId, Shape = "SparseCompact4MPlus67", Input = population, Output = actual.Count,
                CpuBoundaryCandidates = sample.Count, FullPrivateParity = true, Empty = true, PreCanceled = true,
                Digest = Digest(actual), BatchBase = batch.BatchBase, BatchSize = batch.BatchCandidateCount });
            Console.WriteLine($"SR_RELIABILITY family={family.FamilyId};shape=SparseCompact4MPlus67;input={population};output={actual.Count};privateParity=true;cpuBoundary={sample.Count}");
        }

        // Deliberately bypass condition admission only for this transport fixture.
        // The production S shader and executors remain unchanged; an empty S
        // predicate is mathematically all-pass and supplies an exact expectation.
        var allPass = request with { Evaluation = ExactSearchEvaluationProjection.Empty,
            SnapshotFingerprint = request.SnapshotFingerprint + "|transport-all-pass" };
        var denseBatch = new SearchBatch(batch.BatchBase, population);
        foreach (string path in new[] { "AtomicAppendHostSort", "StableOrderedCompaction" })
        {
            using var gpu = (IDisposable)createS.Invoke(null, [rd, allPass, Enum.Parse(pathType, path)])!;
            var (actual, metrics) = SrPublicExecute(gpu, FamilyCandidateSet.Dense(denseBatch), CancellationToken.None);
            SrCheck(actual.Batch == denseBatch && actual.Count == population &&
                (int)Get(metrics, "ProcessedCandidates")! == population &&
                (int)Get(metrics, "Dispatches")! == (path == "AtomicAppendHostSort" ? 2 : 4), "S.AllPassCounts:" + path);
            ReadOnlySpan<ulong> denseOrdinals = actual.ExportAbi1().Span;
            for (int i = 0; i < denseOrdinals.Length; i++)
                if (denseOrdinals[i] != (ulong)i) throw new InvalidDataException("SrReliability.S.AllPassOrdinal:" + i);
            rows.Add(new { FamilyId = "S.MerchantShopColorless", Shape = "TransportAllPassDense4MPlus67", Path = path,
                Input = population, Output = actual.Count, Digest = Digest(actual), RealQueryOrPricingClaim = false });
            Console.WriteLine($"SR_RELIABILITY family=S;shape=TransportAllPassDense4MPlus67;path={path};output={actual.Count};fullOrdinalParity=true");
        }
        File.WriteAllText(Path.Combine(output, "sr-reliability.json"), JsonSerializer.Serialize(new
        {
            Source = "ExistingProductionExecutors", Device = rd.GetDeviceName(), request.SnapshotFingerprint,
            TransferCapacity = transferCapacity, Population = population, TimingsArePricingEvidence = false, Rows = rows
        }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine("SR_RELIABILITY_PASS publicCompact=true;highSurvivalDense=true;cpuBoundary=true;noThroughputClaim=true");
    }

    private static (FamilyCandidateSet Result, object Metrics) SrPublicExecute(object executor, FamilyCandidateSet input, CancellationToken token)
    {
        object?[] args = [input, token, null];
        var result = (FamilyCandidateSet)executor.GetType().GetMethod("Execute", Instance)!.Invoke(executor, args)!;
        return (result, args[2]!);
    }

    private static void SrCheck(bool condition, string label)
    {
        if (!condition) throw new InvalidDataException("SrReliability." + label);
    }
}
