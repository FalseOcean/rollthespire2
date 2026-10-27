using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;

internal static partial class PartyPhase1Checks
{
    private static async Task RunNeowGpu(GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        var kernelArgs = OS.GetCmdlineUserArgs();
        if (kernelArgs.Contains("--party-neow-ordered-k-kernels"))
        {
            string Argument(string key)
            {
                int index = Array.IndexOf(kernelArgs, key);
                return index >= 0 && index + 1 < kernelArgs.Length ? kernelArgs[index + 1]
                    : throw new ArgumentException("Missing kernel-check argument: " + key);
            }
            string contextsRoot = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(Argument("--context")))!)!.FullName;
            CheckNeowOrderedKKernelOptimizations(contextsRoot, Path.GetFullPath(Argument("--output")));
            return;
        }
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (var (count, lockedCharacter) in new[] { (2, false), (3, false), (4, false), (3, true) })
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, 128);
            var unlocks = Enumerable.Range(0, count).Select(_ => UnlockState.all.ToSerializable()).ToArray();
            if (lockedCharacter) { unlocks[1] = UnlockState.none.ToSerializable(); unlocks[1].NumberOfRuns = 1; }
            var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed,
                BaseGameModelKeys.Characters.All.Take(count).ToArray(), unlocks, 10, "0.111.0");
            var players = party.Players.Select(owner =>
            {
                var offers = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(seed, owner, Beta111Profile.Instance, true).RelicKeys;
                return new PlayerOfferQuery(owner.PlayerSlotIndex, new([offers[0], offers[1]], [], []))
                { Conditions = SearchQuery.Empty with { OpeningRoute = new(offers[0]),
                    LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = new([], [offers[0]], []) } } };
            }).ToArray();
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
                10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var compiled = SearchCompiler.Compile(SearchQuery.Empty with { Players = players }, context);
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 1)).Plan!;
            if (lockedCharacter)
            {
                // Slot 1 is Silent under UnlockState.none, not an unlocked tutorial
                // character. Runtime capture keeps its incomplete authority; both
                // numerical backends must leave this query to whole-table Exact.
                Check(!party.Players[1].IsProductionBetaProjectionAuthorityExact, "locked character retains incomplete numerical authority");
                foreach (bool preferGpu in new[] { false, true })
                {
                    Check(!NeowFamily.TryCreateParty(request, out var unavailable, preferGpu) && unavailable is null,
                        "locked character is not admitted by either numerical N backend");
                    var remaining = FamilyExecutionCoordinator.Plan(request, preferGpu);
                    Check(remaining.OrderedFamilies.All(f => !f.Coverage.Contains("N.Neow")),
                        "unavailable N remains outside filtering coverage");
                }
                GD.Print($"PARTY_N_GPU_AUTHORITY_REMAINDER players={count};lockedSlot=1;cpuGpuNotAdmitted=true;owner=WholeTableExact;matchClaimed=false");
                continue;
            }
            var gpu = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: true);
            var compatible = CheckNeowCompatibilityShape(request, $"identity-{count}p");
            var cpu = new NeowFamily.PartyInitial(request);
            Check(gpu.ConditionPerformance.UsesGpu, "complete runtime authority admits GPU N");
            Check(FamilyExecutionCoordinator.Plan(request, false).OrderedFamilies.All(f => !f.ConditionPerformance.UsesGpu), "explicit CPU selection");
            Check(FamilyExecutionCoordinator.Plan(request, true).OrderedFamilies.Any(f => f.ConditionPerformance.UsesGpu), "production GPU selection");
            try
            {
                var batch = new SearchBatch(0, 16384);
                foreach (bool compact in new[] { false, true })
                {
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, 8192).Select(i => (ulong)(i * 2)).ToArray()) : FamilyCandidateSet.Dense(batch);
                    var window = new FamilyObservationWindow(batch, request);
                    var expected = await cpu.InvokeAsync(execution, window, input, default);
                    var actual = await gpu.InvokeAsync(execution, window, input, default);
                    var compatibleActual = await compatible.InvokeAsync(execution, window, input, default);
                    Check(expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()), "GPU/CPU complete survivor parity");
                    Check(expected.EnumerateLogicalOrdinals().SequenceEqual(compatibleActual.EnumerateLogicalOrdinals()), "IntegerUnitFloat/native/CPU complete ordered ABI1 parity");
                    Check(actual.EnumerateLogicalOrdinals().Contains(128UL), "known joint positive retained");
                    GD.Print($"PARTY_N_GPU players={count};lockedCharacter={lockedCharacter};compact={compact};roots={input.Count};survivors={actual.Count};integerUnitFloatParity=true;compatibleTransport=Public");
                }
            }
            finally { await gpu.DisposeAsync(execution); await compatible.DisposeAsync(execution); await cpu.DisposeAsync(execution); }
            if (count == 2)
            {
                await CheckNeowDenseBatchPairs(request, execution, Check);
                await using var session = FamilyExecutionCoordinator.Start(request, failOnPhysicalRecovery: true, gpuAvailable: true);
                await session.Completion;
                Check(session.TryReadCandidate(out var candidate) && candidate is not null, "GPU to whole-table Exact Production Search");
                if (kernelArgs.Contains("--party-neow-dense-pair-checks"))
                {
                    GD.Print($"PARTY_N_DENSE_PAIR_SLICE_PASS checks={checks};players=2;fullUnchangedEffectSuite=false");
                    return;
                }
            }

        }
        int unresolvedRouteOnly = await RunNeowEffectsGpu(detection, execution);
        GD.Print($"PARTY_N_GPU_PASS checks={checks};originalRouteOnlyWholeExactUnresolved={unresolvedRouteOnly};originalRouteOnlyMatchClaimed=false;strengthenedPhialIsSeparateQuery=true");
    }

    private static async Task<int> RunNeowEffectsGpu(GameVersionDetection detection, FamilyExecutionContext execution)
    {
        var args = OS.GetCmdlineUserArgs(); int outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("Missing --output");
        string evidenceDirectory = Path.Combine(Path.GetFullPath(args[outputIndex + 1]), "party-n-effects");
        Directory.CreateDirectory(evidenceDirectory);
        int unresolvedRouteOnly = 0;
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, "000000000040",
            BaseGameModelKeys.Characters.All.Take(2).ToArray(), [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
        var owner = party.Players[1];
        foreach (var (source, warPaint, mode) in new[] { (BaseGameModelKeys.Relics.MassiveScroll, false, "authored"), (BaseGameModelKeys.Relics.NewLeaf, false, "authored"),
            (BaseGameModelKeys.Relics.NewLeaf, true, "authored"), (BaseGameModelKeys.Relics.PhialHolster, false, "authored"), (BaseGameModelKeys.Relics.ArcaneScroll, false, "authored"), (BaseGameModelKeys.Relics.SmallCapsule, false, "authored"),
            (BaseGameModelKeys.Relics.NewLeaf, false, "unselected"), (BaseGameModelKeys.Relics.NewLeaf, false, "unspecified-capsule") })
        {
            var prefix = source == BaseGameModelKeys.Relics.NewLeaf ? BaseGameModelKeys.Relics.SmallCapsule : BaseGameModelKeys.Relics.NewLeaf;
            int niche = mode != "authored" ? 0 : prefix == BaseGameModelKeys.Relics.SmallCapsule
                ? Math.Max(0, party.Players[0].EffectAuthority!.OrderedDeck!.Count(c => c.CanUpgrade && c.CardType == (warPaint ? RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill : RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack)) - 1) : 1;
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey, 10,
                party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var first = SearchQuery.Empty with { OpeningRoute = mode == "unselected" ? null : new(prefix), StructuredOpeningEffects = mode == "authored" && prefix == BaseGameModelKeys.Relics.SmallCapsule
                ? [new(prefix, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [warPaint ? BaseGameModelKeys.OrdinaryRelics.WarPaint : BaseGameModelKeys.OrdinaryRelics.Whetstone])] : [] };
            CapsuleRelicReplay? capsuleReference = null;
            if (mode == "authored" && prefix == BaseGameModelKeys.Relics.SmallCapsule)
            {
                var pre = SearchCompiler.Compile(SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty)] }, context);
                var parent = ExactSearchExecutionRequestFactory.Compile(pre, new("000000000040", 1, 1, 1)).Plan!;
                var childPrefix = ExactSearchExecutionRequestFactory.ForPlayer(parent, pre.PlayerSearches[0]);
                capsuleReference = new(childPrefix, NeowReplayPlan.Compile(childPrefix, true));
            }
            ulong ordinal = Enumerable.Range(0, 131072).Select(i => (ulong)i).First(i =>
            {
                string text = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i);
                return (capsuleReference is null || capsuleReference.Matches(Beta111Profile.Instance.ComputeRootSeed(text))) &&
                    RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(text, owner, Beta111Profile.Instance, true).RelicKeys.Contains(source) &&
                    (mode == "unselected" || RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(text, party.Players[0], Beta111Profile.Instance, true).RelicKeys.Contains(prefix));
            });
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            if (capsuleReference is not null)
            {
                var ignored = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 0);
                var ignoredEffect = new NeowEffectProjectionEngine(Beta111Profile.Instance, "premise-check", root, party.Players[0])
                { AuthoredCapsuleEffects = PartyInitialQuery.CapsuleEffectPremise(SearchQuery.Empty) }.Project(prefix, ignored);
                if (ignored.Niche.CallCount != 0 || ignored.Rewards.CallCount != 1 ||
                    ignoredEffect.EffectGroups.SelectMany(g => g.OrderedItems).Any(e => e.Kind == PredictedEffectKind.UpgradeCard))
                    throw new Exception("Unspecified Capsule preserves its draw and skips obtain side effects");
            }
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 1);
            for (int i=0;i<niche;i++) rng.Niche.NextInt(2);
            var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-gpu-effect-check", root, owner).Project(source, rng);
            var kind = source == BaseGameModelKeys.Relics.SmallCapsule ? NeowStructuredOutputKind.Relic : source == BaseGameModelKeys.Relics.PhialHolster ? NeowStructuredOutputKind.Potion : NeowStructuredOutputKind.Card;
            var scope = source == BaseGameModelKeys.Relics.SmallCapsule ? NeowStructuredEffectScope.NestedRelics : source == BaseGameModelKeys.Relics.NewLeaf ? NeowStructuredEffectScope.TransformResults :
                source == BaseGameModelKeys.Relics.PhialHolster ? NeowStructuredEffectScope.GeneratedPotions :
                source == BaseGameModelKeys.Relics.ArcaneScroll ? NeowStructuredEffectScope.ProductRelevantEffects : NeowStructuredEffectScope.SelectableOfferGroups;
            var target = projection.EffectGroups.SelectMany(g => g.OrderedItems).First(e => e.TargetKey is { } key && key.Category == (kind == NeowStructuredOutputKind.Relic ? "RELIC" : kind == NeowStructuredOutputKind.Potion ? "POTION" : "CARD")).TargetKey!.Value;
            var condition = new NeowStructuredEffectSearchCondition(source, source == BaseGameModelKeys.Relics.PhialHolster ? NeowStructuredConditionKind.ExactUnorderedPair : NeowStructuredConditionKind.ExactSingle, scope, kind, [target]);
            var local = SearchQuery.Empty with { OpeningRoute = new(source), StructuredOpeningEffects = [condition] };
            var query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty) { Conditions = local }] };
            bool routeOnlySharedConsumer = source == BaseGameModelKeys.Relics.PhialHolster || source == BaseGameModelKeys.Relics.SmallCapsule;
            bool originalUnresolvedPrefix = routeOnlySharedConsumer || mode == "unspecified-capsule";
            foreach (bool strengthened in routeOnlySharedConsumer ? new[] { false, true } : new[] { false })
            {
            string shape = $"effect-{source.Entry}-{mode}-warPaint{warPaint}" +
                (originalUnresolvedPrefix ? strengthened ? "-strengthened-prefix-result" : "-original-route-only" : "");
            var caseQuery = query;
            if (strengthened)
            {
                // This is an additional authored condition, not an equivalent
                // rewrite or a repair of the original route-only query.
                var prefixRng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 0);
                var prefixProjection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-gpu-shared-prefix", root, party.Players[0])
                    .Project(prefix, prefixRng);
                var transform = prefixProjection.EffectGroups.SelectMany(g => g.OrderedItems)
                    .Single(e => e.Kind == PredictedEffectKind.TransformCard);
                if (prefix != BaseGameModelKeys.Relics.NewLeaf || prefixRng.Niche.CallCount != 1 ||
                    prefixRng.CombatPotionGeneration.CallCount != 0 || transform.TargetKey is not { } prefixTarget ||
                    transform.Precision != PredictionPrecision.Exact)
                    throw new InvalidDataException("PartyN.StrengthenedPrefixAuthority");
                var prefixCondition = new NeowStructuredEffectSearchCondition(prefix, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card, [prefixTarget]);
                caseQuery = query with { Players = query.Players.Select(p => p.Slot == 0 ? p with
                    { Conditions = first with { StructuredOpeningEffects = [prefixCondition] } } : p).ToArray() };
            }
            var compiled = SearchCompiler.Compile(caseQuery, context);
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 1)).Plan!;
            var prefixReplay = strengthened ? NeowReplayPlan.Compile(
                ExactSearchExecutionRequestFactory.ForPlayer(request, compiled.PlayerSearches[0]), false) : null;
            var child = ExactSearchExecutionRequestFactory.ForPlayer(request, compiled.PlayerSearches[1]);
            var replay = NeowReplayPlan.Compile(child, false) with { SharedNicheDraws = niche };
            var personalCapsule = source == BaseGameModelKeys.Relics.SmallCapsule ? new CapsuleRelicReplay(child, NeowReplayPlan.Compile(child, true)) : null;
            var gpu = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: true);
            var baseline = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: false);
            var compatible = CheckNeowCompatibilityShape(request, shape);
            var cpu = new NeowFamily.PartyInitial(request);
            try
            {
                var batch = new SearchBatch(ordinal / 16384 * 16384, 16384);
                var input = FamilyCandidateSet.Dense(batch);
                var window = new FamilyObservationWindow(batch, request);
                var initial = input.EnumerateLogicalOrdinals().Where(i => cpu.MatchesInitialOffers(Beta111Profile.Instance.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, batch.GlobalCandidate(i))))).ToArray();
                var numerical = await cpu.InvokeAsync(execution, window, input, default);
                bool MatchesNumerical(ulong i)
                {
                    ulong candidateRoot = Beta111Profile.Instance.ComputeRootSeed(
                        VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, batch.GlobalCandidate(i)));
                    return NeowFamilyReplay.Matches(candidateRoot, replay) &&
                        (prefixReplay is null || NeowFamilyReplay.Matches(candidateRoot, prefixReplay));
                }
                var expected = initial.Where(MatchesNumerical).ToArray();
                if (!expected.SequenceEqual(numerical.EnumerateLogicalOrdinals()))
                    throw new Exception("Party N CPU result predicate did not match its numerical reference: " + source);
                var actual = await gpu.InvokeAsync(execution, window, input, default);
                var compatibleActual = await compatible.InvokeAsync(execution, window, input, default);
                if (!expected.SequenceEqual(actual.EnumerateLogicalOrdinals()) || !actual.EnumerateLogicalOrdinals().Contains(ordinal - batch.BatchBase))
                    throw new Exception("Party N effect parity: " + source);
                if (!expected.SequenceEqual(compatibleActual.EnumerateLogicalOrdinals()))
                    throw new Exception("Party N IntegerUnitFloat/native/CPU dense ordered ABI1 effect parity: " + source);
                var old = await baseline.InvokeAsync(execution, window, input, default);
                if (!old.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()))
                    throw new Exception("Private/public complete population mismatch");
                var sparseOrdinals = Enumerable.Range(0, batch.BatchCandidateCount / 2).Select(i => (ulong)(i*2))
                    .Concat(strengthened ? new[] { ordinal - batch.BatchBase } : Array.Empty<ulong>()).Distinct().Order().ToArray();
                var sparse = FamilyCandidateSet.FromSortedAbi1(batch, sparseOrdinals);
                var sparseOld = await baseline.InvokeAsync(execution, window, sparse, default);
                var sparseNew = await gpu.InvokeAsync(execution, window, sparse, default);
                var sparseCompatible = await compatible.InvokeAsync(execution, window, sparse, default);
                var sparseCpu = await cpu.InvokeAsync(execution, window, sparse, default);
                var sparseExpected = expected.Where(i => Array.BinarySearch(sparseOrdinals, i) >= 0).ToArray();
                if (!sparseExpected.SequenceEqual(sparseCpu.EnumerateLogicalOrdinals()))
                    throw new Exception("Party N compact CPU complete prefix/result numerical reference: " + shape);
                if (!sparseOld.EnumerateLogicalOrdinals().SequenceEqual(sparseNew.EnumerateLogicalOrdinals()))
                    throw new Exception("Private/public compact population mismatch");
                if (!sparseCpu.EnumerateLogicalOrdinals().SequenceEqual(sparseCompatible.EnumerateLogicalOrdinals()) ||
                    !sparseNew.EnumerateLogicalOrdinals().SequenceEqual(sparseCompatible.EnumerateLogicalOrdinals()))
                    throw new Exception("Party N IntegerUnitFloat/native/CPU compact ordered ABI1 effect parity: " + source);
                if (strengthened && !sparseCompatible.EnumerateLogicalOrdinals().Contains(ordinal - batch.BatchBase))
                    throw new Exception("Party N strengthened shared-prefix compact positive missing");
                var empty = await gpu.InvokeAsync(execution, window, FamilyCandidateSet.FromSortedAbi1(batch, []), default);
                if (empty.Count != 0) throw new Exception("Private empty input");
                if ((await compatible.InvokeAsync(execution, window, FamilyCandidateSet.FromSortedAbi1(batch, []), default)).Count != 0)
                    throw new Exception("Party N IntegerUnitFloat public empty input");
                if (personalCapsule is null && actual.Count >= initial.Length) throw new Exception("Post-pickup CPU/GPU did not filter: " + source);
                var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
                bool originalRouteOnly = originalUnresolvedPrefix && !strengthened;
                if (originalRouteOnly)
                {
                    string detail = MpNExactFailureDetails(shape, request, exact, evidenceDirectory, seed);
                    if (exact.Evaluation.Disposition != SearchDisposition.Unknown ||
                        exact.Document?.Sections.SelectMany(s => s.NeowChoices).Any(c =>
                            c.EffectEvidenceCode.Value == "party.earlier-player-shared-opening-unresolved") != true)
                        throw new InvalidDataException("PartyN.OriginalSharedPrefixUnknownPremise:" + detail);
                    unresolvedRouteOnly++;
                    GD.Print($"PARTY_N_EFFECT_ORIGINAL_UNRESOLVED name={shape};seed={seed};gpuCpuAbi1Parity=true;wholeExact=Unknown;matchClaimed=false;queryUnchanged=true");
                }
                else if (!exact.IsMatch)
                    throw new Exception("Whole-table Exact rejects GPU positive: " + MpNExactFailureDetails(
                        shape, request, exact, evidenceDirectory, seed));
                if (originalUnresolvedPrefix)
                {
                    ProductionSearchReplay.Write(Path.Combine(evidenceDirectory, shape + "-workload.json"), new ProductionSearchReplayWorkload(
                        ProductionSearchReplay.SchemaVersion, request.CompiledSearch.Query, request.RunOptions));
                    ProductionSearchReplay.Write(Path.Combine(evidenceDirectory, shape + "-context.json"), new ProductionSearchReplayContext(
                        ProductionSearchReplay.SchemaVersion, strengthened ? "Explicit additional captured NewLeaf transform condition; separate stronger query" : "Original route-only query; Exact Unknown retained", request.CompiledSearch.Context));
                    CompatibilityChecks.WriteEvidence(Path.Combine(evidenceDirectory, shape + "-parity.json"), new {
                        Shape = shape, Seed = seed, StrengthenedQuery = strengthened, OriginalQueryMatchClaimed = false,
                        WholeExactDisposition = exact.Evaluation.Disposition.ToString(), exact.FailureCode,
                        DenseInputCount = input.Count, CompactInputCount = sparse.Count,
                        DenseExpected = expected, DenseCpu = numerical.EnumerateLogicalOrdinals().ToArray(),
                        DenseNative = actual.EnumerateLogicalOrdinals().ToArray(), DenseInteger = compatibleActual.EnumerateLogicalOrdinals().ToArray(),
                        CompactExpected = sparseExpected, CompactCpu = sparseCpu.EnumerateLogicalOrdinals().ToArray(),
                        CompactNative = sparseNew.EnumerateLogicalOrdinals().ToArray(), CompactInteger = sparseCompatible.EnumerateLogicalOrdinals().ToArray(),
                        PrefixNumericalPredicateIncluded = prefixReplay is not null, QueryFingerprint = compiled.SemanticFingerprint,
                        ActualDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(), TargetMetalExecutionClaimed = false
                    });
                }
                if (source == BaseGameModelKeys.Relics.MassiveScroll && OS.GetCmdlineUserArgs().Contains("--party-n-bench"))
                {
                    double oldMs = 0, newMs = 0; long survivors = 0;
                    for (int trial = 0; trial < 4; trial++)
                    {
                        var measured = new SearchBatch((ulong)trial * (1UL << 20), 1 << 20);
                        var measuredInput = FamilyCandidateSet.Dense(measured);
                        var measuredWindow = new FamilyObservationWindow(measured, request);
                        FamilyCandidateSet? oldResult = null, newResult = null;
                        foreach (bool optimized in trial % 2 == 0 ? new[] { false, true } : new[] { true, false })
                        {
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            var result = await (optimized ? gpu : baseline).InvokeAsync(execution, measuredWindow, measuredInput, default);
                            if (optimized) { newMs += watch.Elapsed.TotalMilliseconds; newResult = result; }
                            else { oldMs += watch.Elapsed.TotalMilliseconds; oldResult = result; }
                        }
                        if (!oldResult!.EnumerateLogicalOrdinals().SequenceEqual(newResult!.EnumerateLogicalOrdinals()))
                            throw new Exception("Private/public measured workload parity");
                        survivors += newResult.Count;
                    }
                    GD.Print($"PARTY_N_FUSED_BENCH roots={4L << 20};survivors={survivors};privateChainMs={oldMs:F3};optimizedMs={newMs:F3};boundary=FamilyFilterWarm;excludes=ExactAndStartup");
                }
                GD.Print($"PARTY_N_EFFECT_GPU name={shape};mode={mode};source={source};warPaint={warPaint};nichePrefix={niche};initial={initial.Length};survivors={actual.Count};positive={ordinal};integerUnitFloatParity=true;denseCompact=true;compatibleTransport=Public;wholeExact={exact.Evaluation.Disposition};originalQueryMatchClaimed=false");
            }
            finally { await gpu.DisposeAsync(execution); await baseline.DisposeAsync(execution); await compatible.DisposeAsync(execution); await cpu.DisposeAsync(execution); }
            }
        }
        // Fixed user workload, fixed root interval; separate from constructed positives.
        if (OS.GetCmdlineUserArgs().Contains("--party-n-bench"))
        {
            const string startSeed = "000000000000";
            var measuredParty = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, startSeed,
                [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Ironclad],
                [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, measuredParty.Players[0].Character.CharacterKey,
                10, measuredParty.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = measuredParty };
            ModelKey.TryParseExact("CARD:OUTRAGE", out var outrage);
            var local = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
                StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.LeafyPoultice,
                    NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.TransformResults,
                    NeowStructuredOutputKind.Card, [outrage, outrage], AllowDuplicateOutputs: true)] };
            var query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = local },
                new(1, ModelKeySetFilter.Empty) { Conditions = local }] };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(startSeed, 1, 1, 1)).Plan!;
            var fused = new NeowFamily.PartyInitial(request, true);
            var chain = new NeowFamily.PartyInitial(request, true, fuse: false);
            try
            {
                var warm = new SearchBatch(0, 1 << 20);
                await fused.InvokeAsync(execution, new(warm, request), FamilyCandidateSet.Dense(warm), default);
                await chain.InvokeAsync(execution, new(warm, request), FamilyCandidateSet.Dense(warm), default);
                double fusedMs = 0, chainMs = 0; long survivors = 0;
                for (int i = 0; i < 16; i++)
                {
                    var batch = new SearchBatch((ulong)i << 20, 1 << 20);
                    FamilyCandidateSet? a = null, b = null;
                    foreach (bool useFused in i % 2 == 0 ? new[] { false, true } : new[] { true, false })
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        var result = await (useFused ? fused : chain).InvokeAsync(execution, new(batch, request), FamilyCandidateSet.Dense(batch), default);
                        if (useFused) { a = result; fusedMs += timer.Elapsed.TotalMilliseconds; }
                        else { b = result; chainMs += timer.Elapsed.TotalMilliseconds; }
                    }
                    if (!a!.EnumerateLogicalOrdinals().SequenceEqual(b!.EnumerateLogicalOrdinals())) throw new Exception("Double Leafy fused parity");
                    survivors += a.Count;
                }
                GD.Print($"PARTY_N_FUSED_LEAFY_BENCH roots={16L << 20};survivors={survivors};privateChainMs={chainMs:F3};fusedMs={fusedMs:F3};boundary=FamilyFilterWarm;excludes=ExactAndStartup");
            }
            finally { await fused.DisposeAsync(execution); await chain.DisposeAsync(execution); }
        }
        await CheckNeowConjunctions(detection, execution, party);
        GD.Print($"PARTY_N_EFFECT_GPU_PASS originalRouteOnlyWholeExactUnresolved={unresolvedRouteOnly};originalRouteOnlyMatchClaimed=false;strengthenedSharedConsumersAreSeparateQueries=true");
        return unresolvedRouteOnly;
    }

    private static async Task CheckNeowConjunctions(GameVersionDetection detection, FamilyExecutionContext execution,
        RolltheSpire2.Core.Authority.OrderedPartyAuthority party)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var owner = party.Players[1];
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        foreach (var source in new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.ScrollBoxes,
                     BaseGameModelKeys.Relics.MassiveScroll, BaseGameModelKeys.Relics.NeowsBones })
        {
            ulong ordinal = Enumerable.Range(0, 4096).Select(i => (ulong)i).First(i =>
                RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i), owner, Beta111Profile.Instance, true).RelicKeys.Contains(source));
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 1);
            var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-n-conjunction", root, owner,
                enableComplexBonesDeckInteractions: true) { AuthoredCapsuleEffects = PartyInitialQuery.CapsuleEffectPremise(SearchQuery.Empty) }.Project(source, rng);
            var groups = projection.EffectGroups.Where(g => g.OrderedItems.Any(e => e.Kind == PredictedEffectKind.AddCard)).OrderBy(g => g.GroupOrder).ToArray();
            ModelKey Card(int group, int item) => groups[group].OrderedItems.Where(e => e.Kind == PredictedEffectKind.AddCard && e.TargetKey.HasValue).ElementAt(item).TargetKey!.Value;
            var local = SearchQuery.Empty with { OpeningRoute = new(source) };
            if (source == BaseGameModelKeys.Relics.NeowsBones)
            {
                var bones = projection.BonesOutcome ?? throw new Exception("Bones conjunction authority");
                var curse = bones.OriginalRoutes.First(r => r.SharedContinuation.FinalCurseKey.HasValue).SharedContinuation.FinalCurseKey;
                local = local with { LegacyNeow = local.LegacyNeow with { RequiredBonesCombination = bones.OfferedRelics,
                    RequiredFinalCurse = curse } };
            }
            else if (source == BaseGameModelKeys.Relics.Kaleidoscope)
            {
                var ordered = new NeowStructuredEffectSearchCondition(source, NeowStructuredConditionKind.IndependentOfferGroupTargets,
                    NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [Card(0, 0), Card(1, 0)], AllowDuplicateOutputs: true)
                { KaleidoscopeGroupOrder = KaleidoscopeGroupOrderMode.ExactOrder, KaleidoscopePositionalSlots = [Card(0, 0), Card(1, 0)] };
                local = local with { StructuredOpeningEffects = [ordered, ordered with { OutputKeys = ordered.OutputKeys.ToArray(),
                    KaleidoscopePositionalSlots = ordered.KaleidoscopePositionalSlots.ToArray() }, ordered with
                    { OutputKeys = [Card(0, 0)], KaleidoscopeGroupOrder = KaleidoscopeGroupOrderMode.AnyOrder, KaleidoscopePositionalSlots = [] }] };
                Check(NeowChoiceCommitment.Cards(local.StructuredOpeningEffects, source).Length == 2, "Repeated K commitment retains two picks");
                Check(!NeowChoiceCommitment.HasCompatibleRequirements([ordered, ordered with { KaleidoscopePositionalSlots = [null, Card(1, 0)] }], source),
                    "Conflicting K take/skip rejected");
            }
            else if (source == BaseGameModelKeys.Relics.ScrollBoxes)
            {
                local = local with { StructuredOpeningEffects = new[] { Card(0, 0), Card(0, 1) }.Select(k =>
                    new NeowStructuredEffectSearchCondition(source, NeowStructuredConditionKind.StructuredCardComposition,
                        NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [k])).ToArray() };
                Check(!NeowChoiceCommitment.HasCompatibleRequirements(local.StructuredOpeningEffects.Append(new(source,
                    NeowStructuredConditionKind.SpecialOffer, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [],
                    SpecialOffer: NeowSpecialOfferKind.ScrollBoxesTripleClaw)), source), "Scroll special and normal require different choices");
            }
            else
            {
                var row = new NeowStructuredEffectSearchCondition(source, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [Card(0, 0)]);
                local = local with { StructuredOpeningEffects = [row, row with { OutputKeys = row.OutputKeys.ToArray() }] };
                Check(NeowChoiceCommitment.Cards(local.StructuredOpeningEffects, source).Length == 1, "Repeated Massive is one committed pick");
                Check(!NeowChoiceCommitment.HasCompatibleRequirements([row, row with { OutputKeys = [Card(0, 1)] }], source), "Massive cannot commit two picks");
            }
            var query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)
                { Conditions = local }] };
            var compiled = SearchCompiler.Compile(query, context);
            if (source == BaseGameModelKeys.Relics.MassiveScroll)
                Check(compiled.PlayerSearches[1].NormalizedQuery.StructuredOpeningEffects.Count == 1, "Value-equivalent rows normalize once");
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 2)).Plan ?? throw new Exception("N conjunction compile");
            var cpu = new NeowFamily.PartyInitial(request);
            var gpu = new NeowFamily.PartyInitial(request, true);
            var compatible = CheckNeowCompatibilityShape(request, "conjunction-" + source.Entry);
            try
            {
                Check(gpu.ConditionPerformance.UsesGpu, "Conjunction GPU registered: " + source);
                var batch = new SearchBatch(ordinal, 2051);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 1026).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var a = await cpu.InvokeAsync(execution, new(batch, request), input, default);
                    var b = await gpu.InvokeAsync(execution, new(batch, request), input, default);
                    var c = await compatible.InvokeAsync(execution, new(batch, request), input, default);
                    Check(a.EnumerateLogicalOrdinals().SequenceEqual(b.EnumerateLogicalOrdinals()), "N conjunction CPU/GPU: " + source);
                    Check(a.EnumerateLogicalOrdinals().SequenceEqual(c.EnumerateLogicalOrdinals()), "N conjunction IntegerUnitFloat/native/CPU ordered ABI1: " + source);
                    Check(a.EnumerateLogicalOrdinals().Contains(0UL), "N conjunction positive: " + source);
                    int initial = input.EnumerateLogicalOrdinals().Count(i => cpu.MatchesInitialOffers(Beta111Profile.Instance.ComputeRootSeed(
                        VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, batch.GlobalCandidate(i)))));
                    Check(a.Count < initial, "N CPU results reject offer-only survivors: " + source);
                }
                Check(ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch,
                    "N conjunction whole-table Exact: " + source);
            }
            finally { await cpu.DisposeAsync(execution); await gpu.DisposeAsync(execution); await compatible.DisposeAsync(execution); }
        }
        GD.Print($"PARTY_N_CONJUNCTION_PASS checks={checks};sameChoice=true;cpuResults=true;denseCompact=true");
    }

    private static NeowFamily.PartyInitial CheckNeowCompatibilityShape(ExactSearchExecutionRequest request, string name)
    {
        var compatible = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: true,
            gpuPlanFactory: NeowFamilyGpuPlan.TryCreateIntegerUnitFloat);
        IFamilyInvocation invocation = compatible;
        if (!invocation.ConditionPerformance.UsesGpu || invocation.GpuNumericRequirements != GpuNumericRequirements.Int64 ||
            !invocation.ConditionPerformance.PhysicalImplementationRevision.Contains(".Public.", StringComparison.Ordinal) ||
            !invocation.ConditionPerformance.PhysicalImplementationRevision.Contains("IntegerUnitFloat", StringComparison.Ordinal) ||
            invocation.CanBindPrivateSerial || invocation.QuotePhysicalWork(new(false, false, 16384)) is not null ||
            invocation.QuotePhysicalWork(new(true, false, 8192)) is not null)
            throw new InvalidDataException("PartyN.CompatibilityShapeOrPrice:" + name);
        var stages = (List<(ExactSearchExecutionRequest Request, IFamilyInvocation Family)>)typeof(NeowFamily.PartyInitial)
            .GetField("_gpuStages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(compatible)!;
        int exported = 0;
        var sourceFiles = new List<object>();
        var args = OS.GetCmdlineUserArgs(); int outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("Missing --output");
        string output = Path.Combine(Path.GetFullPath(args[outputIndex + 1]), "party-n-integer-unit-float");
        Directory.CreateDirectory(output);
        foreach (var child in stages.Select(s => s.Family).Where(f => f.ConditionPerformance.UsesGpu))
        {
            if (child is not NeowFamily { GpuPlan: { NumericalVariant: NeowGpuNumericalVariant.IntegerUnitFloat } packed } ||
                child.CanBindPrivateSerial || child.QuotePhysicalWork(new(false, false, 16384)) is not null)
                throw new InvalidDataException("PartyN.CompatibilityChildOrPrice:" + name);
            for (int stage = 0; stage < 4; stage++)
            {
                string source = MpNIntegerUnitFloatSource(packed, stage);
                string filename = $"{name}-child{exported}-stage{stage}.glsl";
                File.WriteAllText(Path.Combine(output, filename), source);
                sourceFiles.Add(new { filename, stage, child = exported, packed.UsesLeafyPreGate,
                    packed.HasAuthoredUpgrades, packed.HasLocalResults,
                    sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))) });
            }
            bool rejected = false;
            try { NeowPartyGpuPacking.Pack([packed, packed]); }
            catch (InvalidOperationException ex) when (ex.Message == "PartyN.FusedNumericalVariantNotAdmitted") { rejected = true; }
            if (!rejected) throw new InvalidDataException("PartyN.CompatibilityFusedPackingAdmitted:" + name);
            exported++;
        }
        if (exported == 0) throw new InvalidDataException("PartyN.CompatibilityNoGpuChildren:" + name);
        CompatibilityChecks.WriteEvidence(Path.Combine(output, name + "-shape.json"), new { name,
            variant = "IntegerUnitFloat", children = exported, forcedFuse = true,
            selected = invocation.ConditionPerformance, requirements = invocation.GpuNumericRequirements.ToString(),
            quoted = false, privateBinding = false, fusedPacking = false, sourceStagesPerChild = 4,
            sourceFiles,
            sourceEvidence = "ActualChildPlans;Stage0To3;NoFloat64;SourceExportIsNotPipelineExecution" });
        GD.Print($"PARTY_N_COMPAT_SHAPE_PASS name={name};gpuChildren={exported};forcedFuse=true;selected=Public;quoted=false;privateBinding=false;sourceStages=4");
        return compatible;
    }

    private static async Task RunNeowBatchComparison(GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        const string seed = "000000000000";
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed,
            [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Ironclad],
            [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        ModelKey.TryParseExact("CARD:OUTRAGE", out var outrage);
        ModelKey.TryParseExact("CARD:ABRASIVE", out var abrasive);
        ModelKey.TryParseExact("CARD:ADAPTIVE_STRIKE", out var adaptive);
        var leafy = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
            StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.LeafyPoultice,
                NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.TransformResults,
                NeowStructuredOutputKind.Card, [outrage, outrage], AllowDuplicateOutputs: true)] };
        var bones = leafy with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            StructuredOpeningEffects = [..leafy.StructuredOpeningEffects,
                new(BaseGameModelKeys.Relics.Kaleidoscope, NeowStructuredConditionKind.IndependentOfferGroupTargets,
                    NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
                    [abrasive, adaptive], AllowDuplicateOutputs: true)] };
        // Fixed queries and [0, 2^26) for timing. Identity also exercises nonempty output.
        foreach (string shape in new[] { "DoubleLeafy", "BonesLeafyK", "Identity" })
        {
            var query = SearchQuery.Empty with { Players = [
                new(0, ModelKeySetFilter.Empty) { Conditions = shape == "BonesLeafyK" ? bones : shape == "Identity" ?
                    SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) } : leafy },
                new(1, ModelKeySetFilter.Empty) { Conditions = shape == "DoubleLeafy" ? leafy : SearchQuery.Empty }] };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, 1, 1, 1)).Plan!;
            var gpu = new NeowFamily.PartyInitial(request, true);
            if (FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, [gpu]) != 1 << 24)
                throw new Exception("Party GPU batch selection");
            if (FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, [new NeowFamily.PartyInitial(request)]) != FamilyCpuExecution.Capacity)
                throw new Exception("Party CPU batch changed");
            try
            {
                var warm = new SearchBatch(0, 1 << 24);
                await gpu.InvokeAsync(execution, new(warm, request), FamilyCandidateSet.Dense(warm), default);
                double smallMs = 0, largeMs = 0; long survivors = 0;
                for (int trial = 0; trial < 4; trial++)
                {
                    ulong start = (ulong)trial << 24;
                    List<ulong>? small = null, large = null;
                    foreach (bool big in trial % 2 == 0 ? new[] { false, true } : new[] { true, false })
                    {
                        int size = big ? 1 << 24 : 1 << 20;
                        var merged = new List<ulong>(); double ms = 0;
                        for (int offset = 0; offset < 1 << 24; offset += size)
                        {
                            var batch = new SearchBatch(start + (ulong)offset, size);
                            double before = gpu.CapturePerformanceObservation().PerformanceBoundaryMs;
                            var result = await gpu.InvokeAsync(execution, new(batch, request), FamilyCandidateSet.Dense(batch), default);
                            ms += gpu.CapturePerformanceObservation().PerformanceBoundaryMs - before;
                            merged.AddRange(result.EnumerateLogicalOrdinals().Select(o => o + (ulong)offset));
                        }
                        if (big) { large = merged; largeMs += ms; } else { small = merged; smallMs += ms; }
                    }
                    if (!small!.SequenceEqual(large!)) throw new Exception("Batch-size complete survivor parity: " + shape);
                    survivors += large!.Count;
                }
                GD.Print($"PARTY_N_BATCH_BENCH shape={shape};roots={1L << 26};survivors={survivors};smallBatches=64;largeBatches=4;smallMs={smallMs:F3};largeMs={largeMs:F3};boundary=FamilyFilterWarm;excludes=ExactAndStartup");
            }
            finally { await gpu.DisposeAsync(execution); }
        }
        GD.Print("PARTY_N_BATCH_PASS");
    }

    private static async Task RunFamilies(Node host, string output, GameVersionDetection detection)
    {
        const string seed = "000000000040";
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed,
            BaseGameModelKeys.Characters.All.Take(4).ToArray(), Enumerable.Range(0, 4).Select(_ => UnlockState.all.ToSerializable()).ToArray(), 10, "0.111.0");
        var root = Beta111Profile.Instance.ComputeRootSeed(seed);
        var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 0);
        var nativeRng = new MegaCrit.Sts2.Core.Random.Rng(root, "up_front");
        var sharedBag = new MegaCrit.Sts2.Core.Runs.RelicGrabBag();
        sharedBag.Populate(MegaCrit.Sts2.Core.Models.ModelDb.RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool>()
            .GetUnlockedRelics(UnlockState.all), nativeRng);
        var queries = new List<PlayerOfferQuery>();
        var nativePlayers = party.Players.Select(p => MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(
            MegaCrit.Sts2.Core.Models.ModelDb.AllCharacters.Single(c => c.Id.Entry == p.Character.CharacterKey.Entry),
            UnlockState.all, (ulong)(900 + p.PlayerSlotIndex))).ToArray();
        var nativeActs = MegaCrit.Sts2.Core.Models.ActModel.GetRandomList(new MegaCrit.Sts2.Core.Random.Rng(root, "act_selection"), UnlockState.all, true)
            .Select(a => a.ToMutable()).ToArray();
        var nativeRun = MegaCrit.Sts2.Core.Runs.RunState.CreateForNewRun(nativePlayers, nativeActs, [], MegaCrit.Sts2.Core.Runs.GameMode.Standard, 10, seed);
        var scroll = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-test", root, party.Players[3]).Project(BaseGameModelKeys.Relics.MassiveScroll);
        var nativeCards = MegaCrit.Sts2.Core.Factories.CardFactory.CreateForReward(nativePlayers[3], 3,
            new MegaCrit.Sts2.Core.Runs.CardCreationOptions(
                new MegaCrit.Sts2.Core.Models.CardPoolModel[] { nativePlayers[3].Character.CardPool,
                    MegaCrit.Sts2.Core.Models.ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>() },
                MegaCrit.Sts2.Core.Runs.CardCreationSource.Other, MegaCrit.Sts2.Core.Runs.CardRarityOddsType.RegularEncounter,
                c => c.MultiplayerConstraint == MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.MultiplayerOnly));
        Check(scroll.EffectGroups.SelectMany(g => g.OrderedItems).Where(e => e.Kind == PredictedEffectKind.AddCard)
            .Select(e => e.TargetKey!.Value.Entry).SequenceEqual(nativeCards.Select(c => c.Card.Id.Entry)), "Massive Scroll native multiplayer reward offer");
        foreach (var owner in party.Players)
        {
            var relics = RelicSequencePredictor.Predict(Beta111Profile.Instance, seed, owner.WorldAuthority, 10);
            Check(relics.Precision == PredictionPrecision.Exact, $"P{owner.PlayerSlotIndex + 1} R: {relics.IssueCode}");
            var nativePlayer = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(
                MegaCrit.Sts2.Core.Models.ModelDb.AllCharacters.Single(c => c.Id.Entry == owner.Character.CharacterKey.Entry),
                UnlockState.all, (ulong)(900 + owner.PlayerSlotIndex));
            var nativeBag = new MegaCrit.Sts2.Core.Runs.RelicGrabBag();
            nativeBag.Populate(nativePlayer, nativeRng);
            foreach (var lane in relics.Lanes.Where(l => l.Kind != RelicSequenceKind.Shop))
                Check(lane.FullEntries.Select(e => e.RelicKey.Entry).SequenceEqual(nativeBag.ToSerializable().RelicIdLists[
                    Enum.Parse<MegaCrit.Sts2.Core.Entities.Relics.RelicRarity>(lane.RarityCode)].Select(k => k.Entry)), "native personal bag " + lane.Kind);
            var bag = NeowRewardGenerator.BuildOrderedRelicBag(Beta111Profile.Instance, root, owner);
            Check(bag.Where(r => r.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectRelicRarity.Common).Select(r => r.RelicKey)
                .SequenceEqual(relics.Lanes.Single(l => l.Kind == RelicSequenceKind.Common).FullEntries.Select(e => e.RelicKey)), "N/R use the same personal initial bag");
            var shop = Beta111NormalMerchantColorlessSequenceProjector.Project(root, Beta111MerchantColorlessAuthority.From(owner));
            Check(shop.Precision == PredictionPrecision.Exact, "S: " + shop.EvidenceCode);
            Check(SeedPredictionRequest.TryCreate(seed, owner.Character, 10, 4, owner.PlayerSlotIndex, owner,
                AncientOptionConditionProfile.BroadDefault, SeedPredictionDomainSelection.All, 10, true, out var request, out var error), error.ToString());
            var choices = PartyNeowProjection.ProjectChoices(request!, root, rng.Niche, rng.CombatPotionGeneration);
            request!.PartyOpeningChoices = choices;
            var document = RuntimeProfileRegistry.Predict(detection, request);
            ProductionExactSearchEvaluator.ThrowIfPredictionFailed(document);
            GD.Print($"PARTY_FAMILY_SLOT P{owner.PlayerSlotIndex + 1} " + string.Join(";", document.Sections.Select(s => $"{s.Kind}:{s.DomainStatus}:{s.IssueCode}")));
            Check(document.Sections.All(s => s.DomainStatus == SeedDomainEvaluationStatus.Evaluated), "all personal information sections evaluated");
            var chosen = choices.First();
            var concrete = PartyNeowProjection.ConcreteRoutes(chosen).First();
            var next = concrete.OpeningRewardContinuations!.Routes.First();
            Check(next.NicheState is not null && next.CombatPotionGenerationState is not null, "shared opening states captured");
            rng = rng.WithShared(next.NicheState!.Restore(), next.CombatPotionGenerationState!.Restore());
            var first = relics.Lanes.Single(l => l.Kind == RelicSequenceKind.Common).Entries[0].RelicKey;
            var ancient = document.Sections.SelectMany(s => s.Ancients).First(a => a.Act == 2);
            Check(ancient.OptionPrecision == PredictionPrecision.Exact, "A: " + ancient.OptionIssueCode);
            var events = RolltheSpire2.Core.Events.Beta111EventResultProjector.Project(root, owner.PlayerSlotIndex,
                RolltheSpire2.Core.Events.Beta111EventResultAuthority.From(owner));
            var reward = document.Sections.Select(s => s.NormalCombatRewardSequencePrediction).First(r => r is not null)!
                .Routes.First(r => r.EquivalentRoutes.Any(e => e.RouteId == $"choice.{chosen.SlotIndex}.{next.Route.RouteId}"));
            Check(reward.Battles.Count > 0, "C: " + reward.IssueCode);
            queries.Add(new(owner.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = SearchQuery.Empty with
            {
                OpeningRoute = new(chosen.RelicKey),
                RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([first], [], []))],
                AncientBranches = [new(2, ancient.AncientKey, [ancient.Options.First(o => o.IsSelectable).OptionKey], [])],
                MerchantColorlessSequenceConditions = [new(1, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon, [shop.Merchants[0].UncommonColorless])],
                CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [reward.Battles[0].Cards[0].CardKey]),
                EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, events.TrashHeapGrabCard)]
            } });
        }
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey, 10,
            party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        var compiled = SearchCompiler.Compile(SearchQuery.Empty with { Players = queries,
            StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 0)] }, context);
        var plan = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 1));
        Check(plan.Success, plan.Issue);
        var match = ProductionExactSearchEvaluator.Evaluate(plan.Plan!, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
        Check(match.IsMatch, "joint N+R+S+A+E+C+M: " + match.Evaluation.Disposition + " / " + match.FailureCode);
        if (OS.GetCmdlineUserArgs().Contains("--party-fast-checks") || OS.GetCmdlineUserArgs().Contains("--party-cost-measure"))
        { await CheckFastFamilies(plan.Plan!); return; }
        var negative = queries.ToArray();
        var wrong = RelicSequencePredictor.Predict(Beta111Profile.Instance, seed, party.Players[3].WorldAuthority).Lanes[0].Entries[1].RelicKey;
        negative[3] = negative[3] with { Conditions = negative[3].Conditions with
        { RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([wrong], [], []))] } };
        var negativePlan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(compiled.Query with { Players = negative }, context), new(seed, 1, 1, 1));
        Check(!ProductionExactSearchEvaluator.Evaluate(negativePlan.Plan!, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch,
            "a rejected P4 condition rejects the whole table");
        await using (var session = RolltheSpire2.Search.FamilyExecution.FamilyExecutionCoordinator.Start(plan.Plan!, gpuAvailable: false))
        {
            await session.Completion;
            Check(session.TryReadCandidate(out var candidate) && candidate is not null,
                "whole-table Production Search returns one accepted root: " + session.GetProgress().FailureCode);
        }
        var runtime = RolltheSpire2.Bootstrap.ModRuntimeSnapshot.NotInitialized with
        { Profile = Beta111Profile.Instance, Detection = detection, Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        var draft = new RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft(party.Players[0].Character.CharacterKey, 10, compiled.Query, AncientOptionConditionProfile.BroadDefault)
        {
            Version = 4, GameVersion = "0.111.0", Profile = RuntimeProfileId.Beta111,
            Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer,
            Players = party.Players.Select(p => new RolltheSpire2.Ui.Workspace.WorkbenchPlayerDraft(p.PlayerSlotIndex,
                p.Character.CharacterKey, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray()
        };
        T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(obj)!;
        var canvas = new Control { Size = new(1600, 900) }; host.AddChild(canvas);
        var shell = new RolltheSpire2.Ui.Shell.WorkspaceShell(); canvas.AddChild(shell);
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "family-ui"));
        var frame = Field<RolltheSpire2.Ui.Pages.Search.QueryWorkbenchFrame>(shell, "_references");
        frame.RestoreDraft(draft);
        var cardEditor = Field<RolltheSpire2.Ui.Pages.Search.Editors.CombatRewardSearchEditor>(frame, "_combatEditor");
        var picker = Field<ObjectPickerPanel>(frame, "_objectPicker");
        // Empty results exercise modal lifetime without requiring native game card scenes in this headless host.
        var cardCatalog = (RolltheSpire2.Ui.Pages.Search.CombatReward.CombatRewardSearchUiCatalog)cardEditor.GetType()
            .GetProperty("_catalog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(cardEditor)!;
        picker.Open(new([], _ => { }, Field<IGameContentNameResolver>(cardEditor, "_names"),
            Field<IUiTextProvider>(cardEditor, "_text"), Field<IGameIconResolver>(cardEditor, "_icons"),
            key => cardCatalog.CardPickerMetadata.TryGetValue(key, out var metadata) ? metadata.Rarity.ToString() : string.Empty,
            cardCatalog.CreateCardPickerContext("autosave-regression"), cardCatalog.MultiplayerOnlyCards, 4));
        var pickerOverlay = picker;
        Check(pickerOverlay.Visible, "card picker opens before autosave");
        var activeCardDraft = Field<object>(cardEditor, "_draft");
        frame.GetType().GetMethod("PollProduction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(frame, [1.0]);
        var captured = frame.CaptureDraft();
        Check(pickerOverlay.Visible && ReferenceEquals(activeCardDraft, Field<object>(cardEditor, "_draft")),
            "autosave and party capture preserve the open picker and its target player");
        picker.ClosePicker();

        ProductionSearchReplay.Write(Path.Combine(output, "party-draft-before.json"), draft);
        ProductionSearchReplay.Write(Path.Combine(output, "party-draft-after.json"), captured);
        Check(captured.Query.Players.All(p => p.Conditions.RelicSequenceConstraints.Count == 1 && p.Conditions.HasCombatRewardConstraints &&
            p.Conditions.AncientBranches.Count == 1 && p.Conditions.EventResultConditions.Count == 1 &&
            p.Conditions.MerchantColorlessSequenceConditions.Count == 1), "all per-slot authoring survives capture");
        shell.GetType().GetMethod("CreatePredictor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, [Type.Missing]);
        var controller = Field<RolltheSpire2.Ui.Controllers.AnalysisPageController>(shell, "_predictorController");
        controller.SetPartyDraft(draft, seed, match.Document!.Party);
        var page = Field<RolltheSpire2.Ui.Pages.Analysis.AnalysisPage>(shell, "_predictor");
        Check(page.LastRequest?.PlayersCount == 4 && page.LastDocument is not null, "existing information page receives party document");
        var bar = Field<RolltheSpire2.Ui.Components.AnalysisRequestBar>(page, "_requestBar");
        Field<OptionButton>(bar, "_partyPlayer").EmitSignal(OptionButton.SignalName.ItemSelected, 3L);
        Check(page.LastRequest?.PlayerSlotIndex == 3 && page.LastRequest.Character.CharacterKey == party.Players[3].Character.CharacterKey,
            "player switch binds P4 information and character");
        Field<Button>(bar, "_railSolo").EmitSignal(Button.SignalName.Pressed);
        Check(page.LastRequest?.PlayersCount == 1 && !Field<OptionButton>(bar, "_partyPlayer").Visible,
            "prediction switches from party to solo");
        Field<Button>(bar, "_railMulti").EmitSignal(Button.SignalName.Pressed);
        Check(page.LastRequest?.PlayersCount == 4 && Field<OptionButton>(bar, "_partyPlayer").Visible,
            "prediction restores its party after solo");
        canvas.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"PARTY_FAMILIES_PASS checks={checks}");
    }
}
