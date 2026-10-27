using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class FamilyPhysicalMatrix
{
    // Capacity-only R3 checks use the actual factory; no 512 pipeline is created.
    internal static void RunPartyRelicCapacityChecks(ExactSearchExecutionRequest basis, string output,
        int measuredPopulation = 1 << 18) => RunPartyRelicCapacityCore(basis, output, measuredPopulation);

    // R3 production composition check. Captured rosters and the actual factory
    // plans are unchanged; no reflection override repairs a factory decision.
    internal static void RunPartyRelicCapacityWrapperChecks(ExactSearchExecutionRequest basis, string output)
    {
        const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        const string prefix = "mp-r3-wrapper-partial-bones";
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("MpR3Wrapper.RealPartyRequired");
        int players = party.Players.Count, checks = 0;
        void Check(bool ok, string label) { checks++; if (!ok) throw new InvalidDataException("MpR3Wrapper." + label); }
        Check(players is 2 or 4 && party.Players.Select(p => p.PlayerSlotIndex).SequenceEqual(Enumerable.Range(0, players)) &&
            party.Players.All(p => p.PlayersCount == players), "CapturedTwoOrFourPlayerRoster");
        Directory.CreateDirectory(output);
        void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name),
            JsonSerializer.Serialize(value, ProductionSearchReplay.JsonOptions));
        Save(prefix + "-context.json", new ProductionSearchReplayContext(1, "OriginalNativeRoster;R3WrapperPartialBonesLarge;FactoryCapacityOn", context));
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var large = BaseGameModelKeys.Relics.LargeCapsule;
        var options = new SearchRunOptions("000000000000", 257, int.MaxValue, 16);
        ExactSearchExecutionRequest Compile(int genericSlot, SearchQuery generic, SearchQuery peer, ulong ordinal = 0)
        {
            int peerSlot = genericSlot == 0 ? players - 1 : 0;
            var query = SearchQuery.Empty with { Players = party.Players.Select(p => new PlayerOfferQuery(p.PlayerSlotIndex,
                ModelKeySetFilter.Empty) { Conditions = p.PlayerSlotIndex == genericSlot ? generic :
                    p.PlayerSlotIndex == peerSlot ? peer : SearchQuery.Empty }).ToArray() };
            return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options with {
                StartSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal) }).Plan
                ?? throw new InvalidDataException("MpR3Wrapper.FactoryRejected");
        }
        ExactSearchExecutionRequest Child(ExactSearchExecutionRequest parent, int slot) => ExactSearchExecutionRequestFactory.ForPlayer(parent,
            parent.CompiledSearch.PlayerSearches.Take(players).Single(p => p.Context.Authority.PlayerSlotIndex == slot));
        // Independent new wrapper fixture. The old fixed Arcane+Large failures
        // remain in their original output directories and are not reclassified.
        SearchQuery pair = SearchQuery.Empty with { OpeningRoute = new(bones),
            OpeningRouteRelicRequirement = new(bones, [large], BonesRouteOrderMode.AnyOrder) };
        // Explicitly request the peer's actual obtain observation in the source
        // query; a route identity alone may leave its effect projection unrequested.
        SearchQuery direct = SearchQuery.Empty with { OpeningRoute = new(large),
            LegacyNeow = SearchQuery.Empty.LegacyNeow with { RequireLargeCapsule = true } };
        SearchQuery Target(SearchQuery query, ModelKey key) => query with { StructuredOpeningEffects = [new(large,
            NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [key])] };
        ModelKey[] Nested(PartyNeowResult transaction) => transaction.Results.SelectMany(g => g.OrderedItems)
            .Where(e => e.Kind == PredictedEffectKind.AddRelic && e.Relation == PredictedEffectRelation.NestedRelic &&
                // Bones annotates the producing relic explicitly. Direct obtain
                // leaves SourceRelicKey null; its owning transaction is the source.
                (e.SourceRelicKey == large || transaction.Option == large && e.SourceRelicKey is null && e.ParentEffectNodeId is null) &&
                e.Precision == PredictionPrecision.Exact &&
                e.TargetKey is { } key && VanillaRelicRewardEffects.TryGet(basis.ProfileId, key, out var effect) &&
                effect.NestedOnObtainPreservesRewardContinuation).Select(e => e.TargetKey!.Value).Distinct().ToArray();
        var rows = new List<object>();
        foreach (int genericSlot in new[] { 0, players - 1 })
        {
            int peerSlot = genericSlot == 0 ? players - 1 : 0;
            string role = genericSlot == 0 ? "GenericFirst" : "GenericLast";
            var probe = Compile(genericSlot, pair, direct);
            var genericProbe = NeowReplayPlan.Compile(Child(probe, genericSlot), false);
            var peerProbe = NeowReplayPlan.Compile(Child(probe, peerSlot), false);
            ExactSearchExecutionRequest? request = null;
            ulong positiveOrdinal = 0;
            var savedNonMatch = new HashSet<SearchDisposition>();
            var savedSourceNonMatch = new HashSet<SearchDisposition>();
            int scanned = 0, genericIdentity = 0, peerIdentity = 0, jointIdentity = 0, sourceMatch = 0, targetPairs = 0, genericPlans = 0;
            for (ulong ordinal = 0; ordinal < 65536 && request is null; ordinal++)
            {
                scanned++;
                var positive = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
                ulong root = positive.RootHash;
                bool genericPass = NeowFamilyReplay.Observe(root, genericProbe).IdentityPass;
                bool peerPass = NeowFamilyReplay.Observe(root, peerProbe).IdentityPass;
                if (genericPass) genericIdentity++;
                if (peerPass) peerIdentity++;
                if (!genericPass || !peerPass) continue;
                jointIdentity++;
                // Use the current whole-table source and its actual shared-stream
                // continuation. Never reset a later player's shared cursor locally.
                var source = ProductionExactSearchEvaluator.Evaluate(probe, positive);
                if (!source.IsMatch)
                {
                    if (savedSourceNonMatch.Add(source.Evaluation.Disposition))
                    {
                        Save($"{prefix}-{role}-source-{source.Evaluation.Disposition}-workload.json", new ProductionSearchReplayWorkload(1,
                            probe.CompiledSearch.NormalizedQuery, probe.RunOptions with { StartSeed = positive.CanonicalSeed }));
                        Save($"{prefix}-{role}-source-{source.Evaluation.Disposition}-exact.json", source);
                    }
                    continue;
                }
                sourceMatch++;
                if (sourceMatch == 1)
                {
                    Save($"{prefix}-{role}-first-source-match-workload.json", new ProductionSearchReplayWorkload(1,
                        probe.CompiledSearch.NormalizedQuery, probe.RunOptions with { StartSeed = positive.CanonicalSeed }));
                    Save($"{prefix}-{role}-first-source-match-exact.json", source);
                }
                var transactions = source.Document!.Party?.Transactions ?? [];
                var genericTarget = transactions.Where(t => t.Slot == genericSlot).SelectMany(t => Nested(t)).FirstOrDefault();
                var peerTarget = transactions.Where(t => t.Slot == peerSlot).SelectMany(t => Nested(t)).FirstOrDefault();
                if (!genericTarget.IsValid || !peerTarget.IsValid) continue;
                targetPairs++;
                var candidate = Compile(genericSlot, Target(pair, genericTarget), Target(direct, peerTarget), ordinal);
                var genericChild = Child(candidate, genericSlot);
                var peerChild = Child(candidate, peerSlot);
                if (!RelicFullGpuPlan.TryCreate(genericChild, NeowReplayPlan.Compile(genericChild, true), out var actualGeneric, out _) ||
                    actualGeneric is not { GenericReplay: true } ||
                    !RelicFullGpuPlan.TryCreate(peerChild, NeowReplayPlan.Compile(peerChild, true), out var actualPeer, out _) ||
                    actualPeer is not { GenericReplay: false }) continue;
                genericPlans++;
                var exact = ProductionExactSearchEvaluator.Evaluate(candidate, positive);
                if (!exact.IsMatch)
                {
                    if (savedNonMatch.Add(exact.Evaluation.Disposition))
                    {
                        Save($"{prefix}-{role}-original-{exact.Evaluation.Disposition}-workload.json", new ProductionSearchReplayWorkload(1,
                            candidate.CompiledSearch.NormalizedQuery, candidate.RunOptions));
                        Save($"{prefix}-{role}-original-{exact.Evaluation.Disposition}-exact.json", exact);
                    }
                    continue; // Preserve the actual Unknown/NoMatch; never weaken this query.
                }
                request = candidate; positiveOrdinal = ordinal;
                Save($"{prefix}-{role}-source-exact.json", source);
                Save($"{prefix}-{role}-exact.json", exact);
            }
            Save($"{prefix}-{role}-front-gates.json", new { Players = players, GenericSlot = genericSlot, PeerSlot = peerSlot,
                Scanned = scanned, GenericIdentity = genericIdentity, PeerIdentity = peerIdentity, JointIdentity = jointIdentity,
                SourceMatch = sourceMatch, TargetPairs = targetPairs, ActualGenericAndTrackedPlans = genericPlans,
                JointExactMatch = request is not null, PositiveGlobalOrdinal = request is not null ? (ulong?)positiveOrdinal : null,
                Shape = "PartialBonesLargeAndDirectLarge;WholeTableSourceTargets;SeparateFromFixedArcaneLarge" });
            Check(request is not null, "BoundedSameRootExactPositive:" + role + ";InspectOriginalNonmatchReceiptIfPresent");
            Save($"{prefix}-{role}-workload.json", new ProductionSearchReplayWorkload(1,
                request!.CompiledSearch.NormalizedQuery, request.RunOptions));
            var gpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true);
            var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
            var gpuContext = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true, cpuWorkerCount: 16);
            var cpuContext = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true, cpuWorkerCount: 16);
            try
            {
                var gpu = gpuFamilies.OfType<PartyFamily>().Single(f => f.FamilyId == "R.Relic");
                var cpu = cpuFamilies.OfType<PartyFamily>().Single(f => f.FamilyId == "R.Relic");
                var stages = gpu.PersonalStages.ToArray();
                Check(stages.Length == 2 && stages.Select(s => s.Slot).SequenceEqual(new[] { 0, players - 1 }) &&
                    stages.All(s => s.Family is CapsuleRelicFamily && s.Family.CanBindPrivateSerial && s.Family.ConditionPerformance.UsesGpu) &&
                    cpu.PersonalStages.Select(s => s.Slot).SequenceEqual(stages.Select(s => s.Slot)) && !cpu.ConditionPerformance.UsesGpu &&
                    stages.All(g => cpu.PersonalStages.All(c => !ReferenceEquals(g.Family, c.Family))),
                    "IndependentCompleteFactoryWrappers:" + role);
                var gpuPlanField = typeof(CapsuleRelicFamily).GetField("_gpuPlan", instance)!;
                var sourceRows = new List<object>();
                var bodyRevisions = new List<string>();
                foreach (var stage in stages)
                {
                    var full = (RelicFullGpuPlan?)gpuPlanField.GetValue(stage.Family) ?? throw new InvalidDataException("MpR3Wrapper.GpuPlanMissing");
                    Check(full.GenericReplay == (stage.Slot == genericSlot) &&
                        full.UseExactGenericBagCapacity == full.GenericReplay && full.CapsuleMetadata[14] == stage.Slot && full.CapsuleMetadata[15] == players,
                        "ActualFactoryPlanAndOriginalSlot:" + stage.Slot);
                    if (full.GenericReplay)
                    {
                        CheckPartyRelicCapacityBoundaries(full, Check);
                        CheckPartyRelicCapacityFactoryScope(request, stage.Slot, Check);
                        Check(full.ConfiguredGenericBagCapacity < 512, "ActualReducedCapacity");
                        Check(full.BagCapacityRevision == $".GenericBagCapacity.Slot{stage.Slot}.B{full.ConfiguredGenericBagCapacity}.20261016.v1" &&
                            ((CapsuleRelicFamily)stage.Family).GpuBagCapacityRevision == full.BagCapacityRevision &&
                            full.Revision(false).Contains(full.BagCapacityRevision, StringComparison.Ordinal) &&
                            full.Revision(true).Contains(full.BagCapacityRevision, StringComparison.Ordinal),
                            "ActualChildBagCapacityRevision");
                        bodyRevisions.Add(full.BagCapacityRevision);
                    }
                    else Check(full.ConfiguredGenericBagCapacity == 512 && full.BagCapacityRevision.Length == 0 && ((CapsuleRelicFamily)stage.Family).GpuBagCapacityRevision.Length == 0,
                        "TrackedControlRetains512Identity");
                    sourceRows.Add(new { stage.Slot, full.GenericReplay, Capacity = full.ConfiguredGenericBagCapacity,
                        full.BagCapacityRevision,
                        BucketScopes = full.SequencePlan.Pool.BucketScopes, BucketKinds = full.SequencePlan.Pool.BucketKinds,
                        BucketLengths = full.SequencePlan.Pool.BucketLengths, MetadataSlot = full.CapsuleMetadata[14], MetadataPlayers = full.CapsuleMetadata[15] });
                }
                Check(bodyRevisions.All(r => gpu.ResolveConditionPerformance(false).PhysicalImplementationRevision.Contains(r, StringComparison.Ordinal) &&
                    gpu.ResolveConditionPerformance(true).PhysicalImplementationRevision.Contains(r, StringComparison.Ordinal)) &&
                    !cpu.ConditionPerformance.PhysicalImplementationRevision.Contains(".GenericBagCapacity.", StringComparison.Ordinal), "ActualWrapperBagCapacityRevision");
                var quote = gpu.QuoteUnit(257, false, new HashSet<string>(StringComparer.Ordinal));
                if (quote is not null) Check(quote.Evidence.Contains("HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal) &&
                    quote.Evidence.Contains("CurrentPartyRelicBody=", StringComparison.Ordinal) && quote.Evidence.Contains("NotCurrentBodyMeasurement", StringComparison.Ordinal),
                    "AvailableQuoteMarksHistoricalCoefficients");
                Save($"{prefix}-{role}-plans.json", sourceRows);
                var replays = new[] { 0, players - 1 }.Select(slot => { var child = Child(request, slot);
                    return new CapsuleRelicReplay(child, NeowReplayPlan.Compile(child, true)); }).ToArray();
                var expected = Enumerable.Range(0, 257).Select(i => { ulong root = Beta111Profile.Instance.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positiveOrdinal + (ulong)i));
                    return replays.All(r => r.Matches(root)); }).ToArray();
                Check(expected[0], "IndependentCpuJointPositive:" + role);
                var batch = new SearchBatch(positiveOrdinal, 257);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 129).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var wanted = input.EnumerateLogicalOrdinals().Where(i => expected[(int)i]).ToArray();
                    var cpuResult = Task.Run(async () => await cpu.InvokeAsync(cpuContext, new(batch, request), input, default).ConfigureAwait(false)).GetAwaiter().GetResult();
                    var gpuResult = Task.Run(async () => await gpu.InvokeAsync(gpuContext, new(batch, request), input, default).ConfigureAwait(false)).GetAwaiter().GetResult();
                    Check(cpuResult.EnumerateLogicalOrdinals().SequenceEqual(wanted) && gpuResult.EnumerateLogicalOrdinals().SequenceEqual(wanted) &&
                        cpuResult.Batch == batch && gpuResult.Batch == batch && gpuResult.EnumerateLogicalOrdinals().Contains(0UL), "CompleteWrapperPopulationParity:" + role);
                    Check(typeof(PartyFamily).GetField("_chain", instance)!.GetValue(gpu) is not null, "ActualPrivateChainBound:" + role);
                    rows.Add(new { Players = players, Role = role, GenericSlot = genericSlot, PeerSlot = peerSlot,
                        PositiveGlobalOrdinal = positiveOrdinal, WholeTableExactPositive = true, Dense = input.IsDense,
                        WrapperInput = input.Count, Output = gpuResult.Count, Digest = Digest(gpuResult), FullCpuGpuPopulationParity = true,
                        CpuDigest = Digest(cpuResult), CapacityOnly = true,
                        GpuRevision = gpu.ResolveConditionPerformance(!input.IsDense).PhysicalImplementationRevision,
                        QuoteEvidence = quote?.Evidence, QuoteAvailable = quote is not null,
                        Scope = "OriginalRoster;PartialBonesLarge;WholeTableSourceTargets;TwoActiveRChildren;FactoryRegisteredPrivateChain;NotWholePipelineOrTimingEvidence" });
                    Save(prefix + ".partial.json", new { Complete = false, Players = players, Checks = checks, Rows = rows });
                }
            }
            finally
            {
                Task.Run(async () => {
                    try
                    {
                        try { foreach (var family in gpuFamilies) await family.DisposeAsync(gpuContext).ConfigureAwait(false); }
                        finally { await gpuContext.DisposeAsync().ConfigureAwait(false); }
                    }
                    finally
                    {
                        try { foreach (var family in cpuFamilies) await family.DisposeAsync(cpuContext).ConfigureAwait(false); }
                        finally { await cpuContext.DisposeAsync().ConfigureAwait(false); }
                    }
                }).GetAwaiter().GetResult();
            }
            Console.WriteLine($"MP_R3_WRAPPER_SHAPE_PASS players={players};role={role};positiveOrdinal={positiveOrdinal};twoActiveChildren=true;capacityOnly=true");
        }
        Save(prefix + ".json", new { Complete = true, Players = players, Checks = checks, Rows = rows,
            FactoryCapacityEnabledForEligible = true,
            OraclePopulation = 257, NoOriginalQueryObligationsRemoved = true });
        Console.WriteLine($"MP_R3_WRAPPER_PASS players={players};checks={checks};rows={rows.Count};factoryCapacityEnabled=true;capacityOnly=true");
    }

    private static void CheckPartyRelicCapacityBoundaries(RelicFullGpuPlan full, Action<bool, string> check)
    {
        // Static sizing-only inputs, never used as a game pool or GPU fixture.
        RelicFamilyPool Pool(int n) => new(new ushort[n], new byte[n], [0], [n], [1], [1], [1], n);
        int Capacity(RelicFamilyPool pool) => (full with { UseExactGenericBagCapacity = true,
            SequencePlan = full.SequencePlan with { Pool = pool } }).ConfiguredGenericBagCapacity;
        check(Capacity(Pool(511)) == 511, "Capacity511CountsRepeatedEntries");
        check(Capacity(Pool(512)) == 512, "CapacityExactly512");
        check(Capacity(Pool(513)) == 512, "Capacity513Retains512");
        var lastRange = Pool(511) with { BucketOffsets = [0, 257], BucketLengths = [256, 255],
            BucketScopes = [1, 1], BucketKinds = [1, 2], DrawDirections = [1, 1] };
        check(Capacity(lastRange) == 512, "CapacityLastSegmentOutOfRangeRetains512");
    }

    private static void CheckPartyRelicCapacityFactoryScope(ExactSearchExecutionRequest request, int slot, Action<bool, string> check)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        var personal = query.Players.Single(p => p.Slot == slot).Conditions;
        var originalChild = ExactSearchExecutionRequestFactory.ForPlayer(request,
            request.CompiledSearch.PlayerSearches.Take(request.CompiledSearch.Context.Party!.Players.Count)
                .Single(p => p.Context.Authority.PlayerSlotIndex == slot));
        var buckets = originalChild.Authority.WorldAuthority!.Beta109Generation!.PlayerRelicBuckets;
        // Match RelicFamilyPlanCompiler.ParseBucketKind, including its token
        // boundary: "Common" must not also select "Uncommon".
        string RarityToken(string id)
        {
            int separator = id.LastIndexOf(':');
            return separator >= 0 && separator < id.Length - 1 ? id[(separator + 1)..] : id;
        }
        var common = buckets.Single(b => string.Equals(RarityToken(b.BucketId), "Common", StringComparison.OrdinalIgnoreCase)).OrderedRelics.First();
        var shop = buckets.Single(b => string.Equals(RarityToken(b.BucketId), "Shop", StringComparison.OrdinalIgnoreCase)).OrderedEntries.First(e => e.IsAllowedInShops).RelicKey;
        foreach (var shape in new[] {
            (Name: "Ordinary", Query: personal with { RelicSequenceConstraints = [new(RolltheSpire2.Core.Relics.RelicSequenceKind.Common,
                SearchSequenceRangeMode.ExactSlot, 1, new([common], [], []))] }),
            (Name: "Shop", Query: personal with { RelicShopSequenceConditions = [new(1, CombatRewardSequenceOrderMode.Ordered, [shop])] }) })
        {
            // Actual authored queries over the same captured slot/pools; these
            // are packing checks, without claiming a joint Exact positive.
            var mixedQuery = query with { Players = query.Players.Select(p => p.Slot == slot ? p with { Conditions = shape.Query } : p).ToArray() };
            var mixed = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(mixedQuery, request.CompiledSearch.Context), request.RunOptions).Plan
                ?? throw new InvalidDataException("MpR3Wrapper.NonqualFactoryRejected:" + shape.Name);
            var child = ExactSearchExecutionRequestFactory.ForPlayer(mixed,
                mixed.CompiledSearch.PlayerSearches.Take(request.CompiledSearch.Context.Party.Players.Count)
                    .Single(p => p.Context.Authority.PlayerSlotIndex == slot));
            check(RelicFullGpuPlan.TryCreate(child, NeowReplayPlan.Compile(child, true), out var full, out _) && full is { GenericReplay: true },
                "ActualNonemptyGenericFactory:" + shape.Name);
            check(!full!.UseExactGenericBagCapacity &&
                full.ConfiguredGenericBagCapacity == 512 && full.BagCapacityRevision.Length == 0 &&
                (shape.Name == "Ordinary" ? full.SequencePlan.Predicates.Length > 0 : full.SequencePlan.ShopPredicates.Length > 0),
                "NonemptyPredicatesRetain512:" + shape.Name);
        }
        var soloContext = ProductionSearchReplay.Read<ProductionSearchReplayContext>(ProjectSettings.GlobalizePath("res://Corpus/v1/context.json")).Context;
        check(soloContext.Party is null && soloContext.Authority.PlayersCount == 1, "ActualCapturedSoloContext");
        var solo = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(personal, soloContext), request.RunOptions).Plan
            ?? throw new InvalidDataException("MpR3Wrapper.SoloFactoryRejected");
        bool admitted = RelicFullGpuPlan.TryCreate(solo, NeowReplayPlan.Compile(solo, true), out var soloPlan, out _);
        check(!admitted || soloPlan is { GenericReplay: false, UseExactGenericBagCapacity: false } &&
            soloPlan.ConfiguredGenericBagCapacity == 512 && soloPlan.BagCapacityRevision.Length == 0, "SoloDoesNotEnterGenericCapacityScope");
    }

    private static void RunPartyRelicCapacityCore(ExactSearchExecutionRequest basis, string output, int measuredPopulation)
    {
        if (measuredPopulation != 1024 && measuredPopulation != (1 << 18) && measuredPopulation != (1 << 20))
            throw new ArgumentOutOfRangeException(nameof(measuredPopulation));
        const string experiment = "mp-r3", logTag = "MP_R3";
        var context = basis.CompiledSearch.Context;
        var party = context.Party ?? throw new InvalidDataException("MpR3.RealPartyContextRequired");
        int players = party.Players.Count, checks = 0;
        void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidDataException(logTag + "." + label);
            checks++;
        }
        Check(players is >= 2 and <= 4 && party.Players.All(p => p.PlayersCount == players), "CapturedPlayerCount");
        const int oraclePopulation = 257;
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var large = BaseGameModelKeys.Relics.LargeCapsule;
        var arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        var options = new SearchRunOptions("000000000000", measuredPopulation, int.MaxValue, 16);
        SearchQuery Table(int slot, SearchQuery personal) => SearchQuery.Empty with {
            Players = party.Players.Select(p => new PlayerOfferQuery(p.PlayerSlotIndex, ModelKeySetFilter.Empty) {
                Conditions = p.PlayerSlotIndex == slot ? personal : SearchQuery.Empty }).ToArray() };
        ExactSearchExecutionRequest Compile(int slot, SearchQuery personal) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(Table(slot, personal), context), options).Plan ?? throw new InvalidDataException("MpR3.FactoryRejected");
        ExactSearchExecutionRequest Child(ExactSearchExecutionRequest parent, int slot) => ExactSearchExecutionRequestFactory.ForPlayer(parent,
            parent.CompiledSearch.PlayerSearches.Take(players).Single(p => p.Context.Authority.PlayerSlotIndex == slot));
        SearchQuery Pair(bool pinned = false) => SearchQuery.Empty with { OpeningRoute = new(bones),
            OpeningRouteRelicRequirement = new(bones, [arcane, large], pinned ? BonesRouteOrderMode.ExactOrder : BonesRouteOrderMode.AnyOrder) };
        SearchQuery WithTarget(SearchQuery query, ModelKey target) => query with { StructuredOpeningEffects = [new(large,
            NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics,
            NeowStructuredOutputKind.Relic, [target])] };
        bool ContinuationNeutral(ModelKey key) => key.IsValid && VanillaRelicRewardEffects.TryGet(basis.ProfileId, key, out var effect) &&
            effect.NestedOnObtainPreservesRewardContinuation;
        ModelKey[] Nested(IEnumerable<PredictedEffectGroup> groups) => groups.SelectMany(g => g.OrderedItems)
            .Where(e => e.Kind == PredictedEffectKind.AddRelic && e.Relation == PredictedEffectRelation.NestedRelic &&
                e.TargetKey is { } key && ContinuationNeutral(key))
            .Select(e => e.TargetKey!.Value).Distinct().ToArray();
        IReadOnlyList<NeowChoiceResult> SourceChoices(ulong root, ulong ordinal, int slot)
        {
            var owner = party.Players[slot];
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,
                VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
            Check(SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension, players, slot, owner,
                AncientOptionConditionProfile.BroadDefault, SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards,
                10, false, out var source, out var issue), "SourceRequest:" + issue);
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, slot);
            // Other players have no selected openings in these queries, so there
            // is no prior shared continuation to replace or approximate here.
            return PartyNeowProjection.ProjectChoices(source!, root, rng.Niche, rng.CombatPotionGeneration,
                authoredCapsuleEffects: PartyInitialQuery.CapsuleEffectPremise(SearchQuery.Empty));
        }
        var rows = new List<object>();
        var oracleRows = new List<object>();
        var fixtures = new List<object>();
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("MpR3.DeviceUnavailable");
        for (int slot = 0; slot < players; slot++)
        {
            Check(party.Players[slot].PlayerSlotIndex == slot, "CapturedSlotBinding");
            var probe = Child(Compile(slot, Pair()), slot);
            var probeRoute = NeowReplayPlan.Compile(probe, false);
            ulong pairOrdinal = 0, directOrdinal = 0;
            ModelKey firstTarget = default, secondTarget = default, pinnedTarget = default, directTarget = default;
            byte witnessFirst = 255, witnessSecond = 255;
            for (ulong ordinal = 0; ordinal < 65536 && !secondTarget.IsValid; ordinal++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
                var observation = NeowFamilyReplay.Observe(root, probeRoute);
                if (!observation.IdentityPass) continue;
                var choice = SourceChoices(root, ordinal, slot).Single(c => c.RelicKey == bones);
                var routes = choice.BonesOutcome?.OriginalRoutes ?? [];
                ModelKey[] AtFirst(byte first) => routes.Where(r => r.AcquisitionOrder.Count == 2 &&
                    Beta110FastRelicCatalog.TryGetId(r.AcquisitionOrder[0], out byte id) && id == first)
                    .SelectMany(r => Nested(r.RelicScopedResults.Where(s => s.SourceRelicKey == large).SelectMany(s => s.EffectGroups))).Distinct().ToArray();
                var forward = AtFirst(observation.BonesFirst);
                var reverse = AtFirst(observation.BonesSecond);
                var later = reverse.FirstOrDefault(k => !forward.Contains(k));
                Beta110FastRelicCatalog.TryGetId(arcane, out byte arcaneId);
                var pinned = AtFirst(arcaneId);
                if (!later.IsValid || forward.Length == 0 || pinned.Length == 0) continue;
                // A source-only target difference is insufficient: an unmodeled
                // nested obtain may conservatively retain the forward route.
                // Demand a real failing forward predicate before claiming the
                // second-route reconstruction is exercised by this witness.
                var laterChild = Child(Compile(slot, WithTarget(Pair(), later)), slot);
                var laterRoute = NeowReplayPlan.Compile(laterChild, true);
                if (new CapsuleRelicReplay(laterChild, laterRoute with { First = observation.BonesFirst,
                        Second = observation.BonesSecond }).Matches(root) ||
                    !new CapsuleRelicReplay(laterChild, laterRoute with { First = observation.BonesSecond,
                        Second = observation.BonesFirst }).Matches(root)) continue;
                pairOrdinal = ordinal; firstTarget = forward[0]; secondTarget = later; pinnedTarget = pinned[0];
                witnessFirst = observation.BonesFirst; witnessSecond = observation.BonesSecond;
            }
            Check(secondTarget.IsValid, "BoundedActualSecondRouteWitness:" + slot);
            var directQuery = SearchQuery.Empty with { OpeningRoute = new(large) };
            var directProbe = NeowReplayPlan.Compile(Child(Compile(slot, directQuery), slot), false);
            for (ulong ordinal = 0; ordinal < 65536 && !directTarget.IsValid; ordinal++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
                if (!NeowFamilyReplay.Observe(root, directProbe).IdentityPass) continue;
                var choice = SourceChoices(root, ordinal, slot).Single(c => c.RelicKey == large);
                directTarget = Nested(choice.EffectGroups).FirstOrDefault();
                if (directTarget.IsValid) directOrdinal = ordinal;
            }
            Check(directTarget.IsValid, "BoundedTrackedCoreWitness:" + slot);
            foreach (var fixture in new[] {
                (Name: "GenericSecondRoute", Query: WithTarget(Pair(), secondTarget), Start: pairOrdinal, Generic: true),
                (Name: "GenericFirstRoute", Query: WithTarget(Pair(), firstTarget), Start: pairOrdinal, Generic: true),
                (Name: "GenericPinnedOneRoute", Query: WithTarget(Pair(true), pinnedTarget), Start: pairOrdinal, Generic: true),
                (Name: "TrackedCoreDirectControl", Query: WithTarget(directQuery, directTarget), Start: directOrdinal, Generic: false) })
            {
                var parent = Compile(slot, fixture.Query);
                var child = Child(parent, slot);
                var route = NeowReplayPlan.Compile(child, true);
                Check(RelicFullGpuPlan.TryCreate(child, route, out var full, out var planIssue) && full is not null,
                    "GpuPlan:" + fixture.Name + ":" + planIssue);
                Check(full!.GenericReplay == fixture.Generic && full.UseExactGenericBagCapacity == fixture.Generic,
                    "ActualGenericDispatchAndFactoryCapacity:" + fixture.Name);
                Check(full.SequencePlan.Predicates.Length == 0 && full.SequencePlan.ShopPredicates.Length == 0,
                    "ActualEmptySequenceOrTrackedControl:" + fixture.Name);
                if (fixture.Name == "GenericPinnedOneRoute") Check(full.CapsuleMetadata[29] != 0, "ActualPinnedSingleRoute");
                int bagCapacity = full.ConfiguredGenericBagCapacity;
                Check(bagCapacity is >= 1 and <= 512 && (fixture.Generic || bagCapacity == 512), "ConfiguredGenericBagCapacity");
                if (fixture.Generic)
                {
                    CheckPartyRelicCapacityBoundaries(full, Check);
                    long sum = full.SequencePlan.Pool.BucketLengths.Where((_, i) => full.SequencePlan.Pool.BucketScopes[i] != 0 &&
                        full.SequencePlan.Pool.BucketKinds[i] is >= 1 and <= 3).Sum(length => (long)length);
                    Check(bagCapacity == (sum is >= 1 and <= 512 ? sum : 512), "CapacityCountsEveryActualEntry");
                    var pool = full.SequencePlan.Pool;
                    var badOffsets = (int[])pool.BucketOffsets.Clone(); badOffsets[0] = -1;
                    foreach (var invalid in new[] { pool with { BucketLengths = [] }, pool with { BucketOffsets = badOffsets },
                        pool with { BucketScopes = new byte[pool.BucketCount] } })
                        Check((full with { UseExactGenericBagCapacity = true,
                            SequencePlan = full.SequencePlan with { Pool = invalid } }).ConfiguredGenericBagCapacity == 512,
                            "InvalidOrZeroCapacityRetains512");
                }
                string shaderSource = RelicFamilyGpuExecutor.ShaderSource(rfull: true, genericReplay: full.GenericReplay,
                    localStateCapacity: full.SequencePlan.LocalStateCapacity);
                if (full.GenericReplay) shaderSource = shaderSource.Replace("#version 450", "#version 450\n#define RT2_R_GENERIC_BAG_CAPACITY " +
                    bagCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
                string sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shaderSource)));
                Check(shaderSource.Contains("if (plan_meta.values[3u] != 0u || plan_meta.values[4u] != 0u) {", StringComparison.Ordinal) &&
                    shaderSource.Contains("if (logical >= batch_meta.values[2u]) return;", StringComparison.Ordinal) &&
                    shaderSource.Contains("if (plan_meta.values[2u] != 0u) return;", StringComparison.Ordinal) &&
                    shaderSource.Contains("record_processed_workgroup();", StringComparison.Ordinal) &&
                    shaderSource.Contains("if (!rfull_capsule_matches(root)) return;", StringComparison.Ordinal) &&
                    shaderSource.Contains("append_output(logical);", StringComparison.Ordinal), "RetainedSequenceGuardAndEntryContracts");
                if (full.GenericReplay) Check(shaderSource.Contains("#define RT2_R_GENERIC_BAG_CAPACITY " + bagCapacity, StringComparison.Ordinal) &&
                    shaderSource.Contains("if(total+bucket.y>512u)return true;", StringComparison.Ordinal) &&
                    !shaderSource.Contains("uint bag[512]", StringComparison.Ordinal) && !shaderSource.Contains("uint rarities[512]", StringComparison.Ordinal),
                    "ExactCapacitySourceAndOriginalOverflowPath");
                File.WriteAllText(Path.Combine(output, $"{experiment}-slot{slot}-{fixture.Name}-capacity.glsl"), shaderSource);
                File.WriteAllText(Path.Combine(output, $"{experiment}-slot{slot}-{fixture.Name}-source.json"), JsonSerializer.Serialize(new {
                    Players = players, Slot = slot, fixture.Name, ConfiguredBagCapacity = bagCapacity, SourceSha256 = sourceHash,
                    full.BagCapacityRevision, FactoryExactCapacity = full.UseExactGenericBagCapacity,
                    SpirvCompileEvidence = "ExistingRuntimeLog.ShaderModuleReady.SourceSha256.SpirvSha256.CompileSpirvMs;NoDuplicateCompile",
                    PipelineSetupEvidence = "ExistingRuntimeLog.ComputePipelineCreateStarted.Ready;Executor.SetupMs"
                }, ProductionSearchReplay.JsonOptions));
                var replay = new CapsuleRelicReplay(child, route);
                var positive = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, fixture.Start));
                Check(replay.Matches(positive.RootHash) && ProductionExactSearchEvaluator.Evaluate(parent, positive).IsMatch,
                    "WholeTableExactPositive:" + fixture.Name);
                bool? firstPass = null, secondPass = null;
                if (fixture.Name is "GenericFirstRoute" or "GenericSecondRoute")
                {
                    firstPass = new CapsuleRelicReplay(child, route with { First = witnessFirst, Second = witnessSecond }).Matches(positive.RootHash);
                    secondPass = new CapsuleRelicReplay(child, route with { First = witnessSecond, Second = witnessFirst }).Matches(positive.RootHash);
                    Check(fixture.Name == "GenericSecondRoute" ? firstPass == false && secondPass == true : firstPass == true,
                        "ActualForwardReverseWitness:" + fixture.Name);
                }
                using var gpu = RelicFamilyGpuExecutor.Create(rd, child, full);
                var batch = new SearchBatch(fixture.Start, oraclePopulation);
                var expected = Enumerable.Range(0, oraclePopulation).Select(i => replay.Matches(Beta111Profile.Instance.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, fixture.Start + (ulong)i)))).ToArray();
                var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(parent, false);
                var execution = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true, cpuWorkerCount: 16);
                try
                {
                    var cpu = cpuFamilies.Single(f => f.FamilyId == "R.Relic");
                    foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                        Enumerable.Range(0, 129).Select(i => (ulong)i * 2).ToArray()) })
                    {
                        var wanted = input.EnumerateLogicalOrdinals().Where(i => expected[(int)i]).ToArray();
                        // CPU invocation can await worker permits. Start outside
                        // Godot's synchronization context before synchronously
                        // waiting; the RenderingDevice calls stay on this thread.
                        var cpuResult = Task.Run(async () => await cpu.InvokeAsync(execution, new(batch, parent), input, default)
                            .ConfigureAwait(false)).GetAwaiter().GetResult();
                        Check(cpuResult.EnumerateLogicalOrdinals().SequenceEqual(wanted), "RegisteredCpuFullPopulation:" + fixture.Name);
                        {
                            var actual = gpu.Execute(input, default, out var metrics);
                            Check(actual.Batch == input.Batch && actual.EnumerateLogicalOrdinals().SequenceEqual(wanted) &&
                                metrics.ProcessedCandidates == input.Count, "GpuFullPopulation:" + fixture.Name);
                            oracleRows.Add(new { Players = players, Slot = slot, fixture.Name, Dense = input.IsDense,
                                Input = input.Count, ActualStageInput = metrics.ProcessedCandidates, Output = actual.Count, Digest = Digest(actual),
                                FullCpuGpuPopulationParity = true });
                        }
                    }
                }
                finally
                {
                    Task.Run(async () =>
                    {
                        try { foreach (var family in cpuFamilies) await family.DisposeAsync(execution).ConfigureAwait(false); }
                        finally { await execution.DisposeAsync().ConfigureAwait(false); }
                    }).GetAwaiter().GetResult();
                }
                var measuredBatch = new SearchBatch(fixture.Start, 1 << 24);
                foreach (bool compact in new[] { false, true })
                {
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(measuredBatch,
                        Enumerable.Range(0, measuredPopulation).Select(i => (ulong)i * (1u << 24) / (uint)measuredPopulation).ToArray())
                        : FamilyCandidateSet.Dense(new SearchBatch(fixture.Start, measuredPopulation));
                    FamilyCandidateSet? stable = null;
                    for (int round = -1; round < 5; round++)
                    {
                        var actual = gpu.Execute(input, default, out var metrics);
                        Check(metrics.ProcessedCandidates == input.Count && (stable is null ||
                            actual.Batch == stable.Batch && actual.ExportAbi1().Span.SequenceEqual(stable.ExportAbi1().Span)), "MeasuredFullAbi1Stability:" + fixture.Name);
                        stable ??= actual;
                        string digest = Digest(actual);
                        rows.Add(new { Players = players, Slot = slot, fixture.Name, Compact = compact, Round = round,
                            ConfiguredBagCapacity = bagCapacity, Warmup = round < 0, Input = input.Count,
                            ActualStageInput = metrics.ProcessedCandidates, Output = actual.Count,
                            CanonicalMs = metrics.CanonicalAbi1ReadyMs, DispatchMs = metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs,
                            Digest = digest, FullAbi1Stable = true });
                        Console.WriteLine($"{logTag}_ROUND players={players};slot={slot};shape={fixture.Name};compact={compact};round={round};actualStageInput={metrics.ProcessedCandidates};output={actual.Count};canonicalMs={metrics.CanonicalAbi1ReadyMs:G9};digest={digest}");
                    }
                }
                fixtures.Add(new { Players = players, Slot = slot, fixture.Name, PositiveGlobalOrdinal = fixture.Start,
                    PositiveBatchLocalOrdinal = 0, WholeTableExactPositive = true, fixture.Generic,
                    WitnessForwardPass = firstPass, WitnessReversePass = secondPass, ReverseInvokedOnlyAfterForwardFailure = true,
                    WitnessRouteEvidence = "SourceProjectedOutputsAndExistingCpuReplayWithPinnedOrder;NotGpuCounters",
                    OrdinaryEntries = full.SequencePlan.Pool.BucketLengths.Where((_, i) => full.SequencePlan.Pool.BucketScopes[i] == 1 &&
                        full.SequencePlan.Pool.BucketKinds[i] is >= 1 and <= 3).Sum(),
                    Buckets = full.SequencePlan.Pool.BucketLengths, ConfiguredBagCapacity = bagCapacity,
                    DenseRevision = full.Revision(false), CompactRevision = full.Revision(true), SetupMs = gpu.SetupMs,
                    SourceSha256 = sourceHash, TrackedCoreSourceUnchanged = !fixture.Generic });
                File.WriteAllText(Path.Combine(output, $"{experiment}-slot{slot}-{fixture.Name}.json"), JsonSerializer.Serialize(
                    new ProductionSearchReplayWorkload(1, Table(slot, fixture.Query), options with {
                        StartSeed = positive.CanonicalSeed }), ProductionSearchReplay.JsonOptions));
            }
            Console.WriteLine($"{logTag}_SLOT_PASS players={players};slot={slot};nativePool=true;secondRouteWitness=true;trackedCoreControl=true");
        }
        File.WriteAllText(Path.Combine(output, experiment + "-context.json"), JsonSerializer.Serialize(new ProductionSearchReplayContext(1,
            "SavedNativePartyContext;R3CapacityOnly;ActualFactory", context), ProductionSearchReplay.JsonOptions));
        File.WriteAllText(Path.Combine(output, experiment + ".json"), JsonSerializer.Serialize(new {
            Players = players, Checks = checks, OraclePopulation = oraclePopulation, MeasuredPopulation = measuredPopulation,
            FactoryCapacityScope = "Beta111;Players2To4;GenericReplay;EmptyOrdinaryAndShopPredicates",
            Old512GenericPipelineCreated = false, CpuOracleOnlyPopulation = oraclePopulation,
            Boundary = "ProductionExecutorCanonicalAbi1;DenseAndSpreadCompact;OneWarmFiveSustainedInvocations;SetupDigestExcluded;NotE2EOrAComparison",
            Source = "ActualFactoryPools;OriginalGenericAdmission;SourceProjectedWitnesses;NoSameRunFitting",
            Fixtures = fixtures, OracleRows = oracleRows, Rows = rows
        }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"{logTag}_PASS players={players};checks={checks};fixtures={fixtures.Count};factoryCapacityEnabled=true;capacityOnly=true;fullAbi1Parity=true");
    }
}
