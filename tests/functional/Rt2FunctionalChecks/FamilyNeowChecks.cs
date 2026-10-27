using RolltheSpire2.Core.Authority;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Tests.FastSearch;

internal static partial class FamilyNeowChecks
{
    internal static int Run((string Name, ExactSearchExecutionRequest Request)[] scenarios, int count,
        RuntimeContextAuthoritySnapshot fixedSeedAuthority)
    {
        int checkedSeeds = 0, failures = 0, capsuleShapes = 0;
        var gpuFixtures = new List<object>();
        var rfullFixtures = new List<object>();
        CheckCoreIsolation();
        CheckPhysicalPreparationIsolation();
        if (Environment.GetEnvironmentVariable("RT2_GROUPED_CAPSULE_UI_FIXTURES") == "1")
        {
            CheckGroupedCapsuleUi(scenarios.First(s => s.Name == "LargeCapsuleTwoRelics").Request, rfullFixtures);
            string directory = Environment.GetEnvironmentVariable("RT2_NEOW_GPU_FIXTURE_DIR")
                ?? throw new InvalidOperationException("GroupedFixtureDirectoryRequired");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "rfull.glsl"), RelicFamilyGpuExecutor.ShaderSource(rfull: true));
            File.WriteAllText(Path.Combine(directory, "rfull-fixtures.json"), System.Text.Json.JsonSerializer.Serialize(rfullFixtures));
            Console.WriteLine($"GroupedCapsuleUiAdmission=PASS;fixtures={rfullFixtures.Count};legacyMetadataEqual=true");
            return 0;
        }
        if (Environment.GetEnvironmentVariable("RT2_RFULL_FIXTURES_ONLY") == "1")
        {
            CheckNrFixtures(scenarios.First(s => s.Name == "LargeCapsuleTwoRelics").Request, gpuFixtures, rfullFixtures);
            string directory = Environment.GetEnvironmentVariable("RT2_NEOW_GPU_FIXTURE_DIR")
                ?? throw new InvalidOperationException("RfullFixtureDirectoryRequired");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "rfull.glsl"), RelicFamilyGpuExecutor.ShaderSource(rfull: true));
            File.WriteAllText(Path.Combine(directory, "rfull-fixtures.json"), System.Text.Json.JsonSerializer.Serialize(rfullFixtures));
            Console.WriteLine($"Rfull focused fixtures: {rfullFixtures.Count};PASS");
            return 0;
        }
        // These independent historical seeds belong to the original four-relic
        // source fixture, separate from the broader R numerical-shape fixture.
        var capsuleFixture = Beta110LargeCapsuleSemanticFixture.Validate(fixedSeedAuthority, scenarios[0].Request.Detection);
        if (!capsuleFixture.Passed) throw new Exception("Large Capsule semantic fixture failed:" + capsuleFixture.Issue);
        Console.WriteLine("Large Capsule fixed-seed unordered pair, duplicate multiplicity and Bones Exact fixture: PASS");
        foreach (var scenario in scenarios)
        {
            var request = scenario.Request;
            NeowReplayPlan n = NeowReplayPlan.Compile(request, false);
            // Same captured numerical inputs, mod provenance and unaudited flags:
            // policy must admit existing algorithms without rewriting provenance.
            if (request.Authority.EffectAuthority is { } originalEffects)
            {
                var a = request.Authority;
                var modEffects = originalEffects with { CapturedProfileId = RuntimeProfileId.Beta111, SourceAuthority = SourceAuthority.ModdedRuntimeBestEffort,
                    CharacterRewardPoolExact = false, CharacterRewardHooksNoOpExact = false,
                    ColorlessRewardPoolExact = false, ColorlessRewardHooksNoOpExact = false,
                    OtherCharacterPoolsExact = false, OtherCharacterRewardHooksNoOpExact = false,
                    TransformPoolsExact = false, DeckExact = false };
                var modAuthority = new RuntimeContextAuthoritySnapshot(a.Character, a.Ascension, a.PlayerSlotIndex, a.PlayersCount,
                    a.NoRunModifiers, a.VanillaNeowCatalogExact, a.AllCharacterCardPoolsUnlocked, a.UnlockedCommonCards, a.UnlockedUncommonCards,
                    RuntimeVersionCompatibility.Beta111Version, RuntimeProfileId.Beta111, false, "test-mod", "test-mod",
                    SourceAuthority.ModdedRuntimeBestEffort, SnapshotCompleteness.Partial, IdentityResolutionStatus.Unknown,
                    modEffects, a.WorldAuthority, a.PredictionGameMode, a.PredictionGameModeAuthority);
                var modCompiled = request.CompiledSearch with { Context = request.CompiledSearch.Context with { Authority = modAuthority, ProfileId = RuntimeProfileId.Beta111 } };
                var modRequest = request with { CompiledSearch = modCompiled };
                var modPlan = NeowReplayPlan.Compile(modRequest, false);
                if (!modAuthority.UsesBestEffortModel || modEffects.SourceAuthority != SourceAuthority.ModdedRuntimeBestEffort)
                    throw new Exception("Mod policy/provenance lost");
                for (ulong sample = 0; sample < 64; sample++)
                    if (NeowFamilyReplay.Matches(sample, n) != NeowFamilyReplay.Matches(sample, modPlan))
                        throw new Exception("Mod-origin-only numerical regression:" + scenario.Name);
                if (NeowFamilyGpuPlan.TryCreate(n, out _, out _) && !NeowFamilyGpuPlan.TryCreate(modPlan, out _, out var modIssue))
                    throw new Exception("Mod-origin-only GPU admission lost:" + scenario.Name + ":" + modIssue);
                if ((modEffects with { OrderedDeck = null }).HasExactDeck)
                    throw new Exception("Mod policy fabricated missing deck");
            }
            var physicalFamilies = FamilyExecutionCoordinator.Plan(request).OrderedFamilies;
            if (physicalFamilies.Any(f => f is CapsuleRelicFamily) && physicalFamilies.Any(f => f is NeowFamily && f.ConditionPerformance.UsesGpu) &&
                FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, physicalFamilies) != FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize)
                throw new Exception("CPU R underfilled N GPU root geometry");
            if (request.Evaluation.HasNeowConstraints && !FamilyExecutionCoordinator.Plan(request).Stages.Any(s => s.Coverage.Contains("N.Neow")))
                throw new Exception("Production registration lost N Coverage: " + scenario.Name);
            if (NeowFamilyGpuPlan.TryCreate(n, out var gpu, out _) && gpu is not null)
            {
                if (scenario.Name == "LeafyPoulticeTransforms") CheckLeafyPreGateAdmission(request, n, gpuFixtures);
                if (NeowCapsuleComposite.TryCreate(request, n, out var nr) && nr is not null)
                {
                    AddPositiveNrFixtures(request, n, nr, gpu, "Nr." + scenario.Name, gpuFixtures);
                }
                foreach (bool compactInput in new[] { false, true })
                {
                    uint[] ordinals = Enumerable.Range(0, count).Where(i => !compactInput || i % 7 == 0).Select(i => (uint)i).ToArray();
                    ulong start = compactInput ? 39303UL : 0;
                    uint[] expected = ordinals.Where(i => NeowFamilyReplay.Matches(Beta110Profile.Instance.ComputeRootSeed(
                        Beta110SeedCodec.FormatOrdinal(start + i)), n)).ToArray();
                    gpuFixtures.Add(new { name = scenario.Name, compact = compactInput, start, count, ordinals, expected, gpu });
                }
            }
            if (n.Bones && (n.StructuredConditions.Length > 0 || n.HasFinalCurseFastProjection))
            {
                byte[] pair = n.Authority.BonesEligibleRelicIds.Where(id => (n.BonesAll & Beta110FastRelicCatalog.Bit(id)) != 0).ToArray();
                if (pair.Length == 2)
                foreach (bool reverse in new[] { false, true })
                {
                    var positivePlan = n with { Authority = n.Authority with { BonesEligibleRelicIds = pair },
                        First = pair[reverse ? 1 : 0], Second = pair[reverse ? 0 : 1] };
                    if (!NeowFamilyGpuPlan.TryCreate(positivePlan, out var positiveGpu, out _) || positiveGpu is null) throw new Exception("GPU positive fixture unavailable");
                    // Deliberately small bounded dispatch validates positive
                    // checkpoint transport; Production routes tiny pools to fused.
                    if (positiveGpu.Meta[67] == 1 && positiveGpu.Meta[68] != 0) positiveGpu.Meta[66] = 1;
                    uint[] ordinals = Enumerable.Range(0, 4096).Select(i => (uint)i).ToArray();
                    uint[] expected = ordinals.Where(i => NeowFamilyReplay.Matches(Beta110Profile.Instance.ComputeRootSeed(
                        Beta110SeedCodec.FormatOrdinal(i)), positivePlan)).ToArray();
                    if (expected.Length == 0) throw new Exception("Bones local fixture has no positive matches: " + scenario.Name);
                    gpuFixtures.Add(new { name = scenario.Name + ".TwoEntryPool." + reverse, compact = false, start = 0UL,
                        count = 4096, ordinals, expected, gpu = positiveGpu });
                }
            }
            if (n.Authority.EffectCatalog.OrdinaryRelics.Length != 0 || n.Authority.EffectCatalog.PlayerRelicBuckets.Length != 0)
                throw new Exception("N retains a Relic Bag catalog");
            var models = new NeowFamilyProjections(request, n, false, null);
            var cost = models.Expected(new HashSet<string>());
            if (cost.IsResolved && (cost.ExpectedFilteringWorkUnitsPerInput < 0 || cost.ExpectedFilteringWorkUnitsPerInput > cost.FullFilteringWorkUnitsPerInput + 1e-10))
                throw new Exception("N Cost envelope violated: " + scenario.Name);
            bool capsule = NeowReplayPlan.HasCapsule(request);
            CapsuleRelicReplay? r = null;
            if (capsule)
            {
                try { r = new(request, NeowReplayPlan.Compile(request, true)); }
                catch (InvalidOperationException ex) when (ex.Message == "RFamilyUnboundCapsuleRoute_ExactOnly") { }
            }
            if (capsule) capsuleShapes++;
            // Predictor validates the supported N-local query without R predicates.
            // The retired complete Fast executor is not a test dependency.
            var donorRequest = CompileFixtureRequest(n.Filter, request.CompiledSearch.Context, request.RunOptions);

            int mismatches = 0;
            for (int i = 0; i < count; i++)
            {
                ulong root = XxHash64.HashUtf8(Beta110SeedCodec.FormatOrdinal((ulong)i), 0);
                bool actual = NeowFamilyReplay.Matches(root, n);
                if (actual != OriginalRouteSchedule(root, n)) throw new Exception("N fixed-route schedule parity: " + scenario.Name);
                var partySchedule = n with { Authority = n.Authority with { PlayersCount = 2 } };
                if (NeowPartyReplay.Matches(root, partySchedule) != OriginalRouteSchedule(root, partySchedule))
                    throw new Exception("Party fixed-route schedule parity: " + scenario.Name);
                string seed = Beta110SeedCodec.FormatOrdinal((ulong)i);
                if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta110Profile.Instance, root, seed, out var input, out var issue))
                    throw new Exception(issue);
                bool exactMatch = ProductionExactSearchEvaluator.Evaluate(donorRequest, input).IsMatch;
                if (exactMatch && !actual) mismatches++;
                if (r is not null) _ = r.Matches(root);
                checkedSeeds++;
            }
            Console.WriteLine($"{scenario.Name}: N Exact false rejects={mismatches}; capsule={capsule}");
            failures += mismatches;
        }
        var batch = new SearchBatch(100, 16);
        var compact = FamilyCandidateSet.FromAbi1(batch, new ulong[] { 1, 5, 15 });
        if (!compact.ExportAbi1().Span.SequenceEqual(new ulong[] { 1, 5, 15 })) throw new Exception("ABI1 altered");
        CheckCapsuleOracle(scenarios[0].Request, Math.Max(count, 4096));
        CheckPlanner();
        CheckCpuAbi(scenarios[0].Request);
        CheckCpuChunks(scenarios[0].Request);
        CheckCpuFamily(scenarios.First(s => s.Name == "CursedPearl").Request);
        CheckTerminalCompletionTracking();
        CheckNrFixtures(scenarios.First(s => s.Name == "LargeCapsuleTwoRelics").Request, gpuFixtures, rfullFixtures);
        CheckBonesArcaneComposite(scenarios.First(s => s.Name == "LargeCapsuleTwoRelics").Request, gpuFixtures);
        string? fixtureDir = Environment.GetEnvironmentVariable("RT2_NEOW_GPU_FIXTURE_DIR");
        if (!string.IsNullOrEmpty(fixtureDir))
        {
            Directory.CreateDirectory(fixtureDir);
            File.WriteAllText(Path.Combine(fixtureDir, "shader.glsl"), NeowFamilyGpuExecutor.ShaderSource());
            File.WriteAllText(Path.Combine(fixtureDir, "leafy-shader.glsl"), NeowFamilyGpuExecutor.ShaderSource(leafyPreGate: true));
            File.WriteAllText(Path.Combine(fixtureDir, "nr.glsl"), NeowFamilyGpuExecutor.ShaderSource(capsuleComposite: true));
            File.WriteAllText(Path.Combine(fixtureDir, "bones-nr.glsl"), NeowFamilyGpuExecutor.ShaderSource(bonesCapsuleComposite: true));
            File.WriteAllText(Path.Combine(fixtureDir, "bones-arcane-nr.glsl"), NeowFamilyGpuExecutor.ShaderSource(bonesArcaneComposite: true));
            for (int stage = 0; stage < 3; stage++)
            {
                File.WriteAllText(Path.Combine(fixtureDir, $"stage{stage}.glsl"), NeowFamilyGpuExecutor.ShaderSource(stage));
                File.WriteAllText(Path.Combine(fixtureDir, $"leafy-stage{stage}.glsl"), NeowFamilyGpuExecutor.ShaderSource(stage, leafyPreGate: true));
                File.WriteAllText(Path.Combine(fixtureDir, $"bones-nr-stage{stage}.glsl"), NeowFamilyGpuExecutor.ShaderSource(stage, bonesCapsuleComposite: true));
                File.WriteAllText(Path.Combine(fixtureDir, $"bones-arcane-nr-stage{stage}.glsl"), NeowFamilyGpuExecutor.ShaderSource(stage, bonesArcaneComposite: true));
            }
            File.WriteAllText(Path.Combine(fixtureDir, "fixtures.json"), System.Text.Json.JsonSerializer.Serialize(gpuFixtures));
            File.WriteAllText(Path.Combine(fixtureDir, "rfull.glsl"), RelicFamilyGpuExecutor.ShaderSource(rfull: true));
            File.WriteAllText(Path.Combine(fixtureDir, "rfull-fixtures.json"), System.Text.Json.JsonSerializer.Serialize(rfullFixtures));
            Console.WriteLine($"Exported {gpuFixtures.Count} GPU fixtures to {fixtureDir}");
            Console.WriteLine($"Exported {rfullFixtures.Count} Rfull GPU fixtures to {fixtureDir}");
        }
        Console.WriteLine($"Family Neow sanity: checked={checkedSeeds};capsuleShapes={capsuleShapes};failures={failures}");
        return failures == 0 ? 0 : 1;
    }

    private static void AddPositiveNrFixtures(ExactSearchExecutionRequest request, NeowReplayPlan n,
        NeowCapsuleComposite nr, NeowFamilyGpuPlan gpu, string name, List<object> fixtures)
    {
        const int window = 4096;
        ulong start = 0;
        uint[] denseExpected = [];
        int nKept = 0, rKept = 0;
        for (int attempt = 0; attempt < 64 && denseExpected.Length == 0; attempt++)
        {
            var accepted = new List<uint>();
            for (uint ordinal = 0; ordinal < window; ordinal++)
            {
                ulong root = Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + ordinal));
                bool passN = NeowFamilyReplay.Matches(root, n), passR = nr.Reference.Matches(root);
                if (passN) nKept++;
                if (passR) rKept++;
                if (passN && passR) accepted.Add(ordinal);
            }
            denseExpected = accepted.ToArray();
            if (denseExpected.Length == 0) start += window;
        }
        if (denseExpected.Length == 0)
            throw new Exception($"Nr fixture has no positive window:scenario={name};scanned={start};N={nKept};R={rKept};joint=0");
        string witnessSeed = Beta110SeedCodec.FormatOrdinal(start + denseExpected[0]);
        ulong witnessRoot = Beta110Profile.Instance.ComputeRootSeed(witnessSeed);
        if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta110Profile.Instance, witnessRoot, witnessSeed, out var witnessInput, out string witnessIssue))
            throw new Exception("Nr positive root binding:" + name + ":" + witnessIssue);
        var witness = ProductionExactSearchEvaluator.Evaluate(request, witnessInput);
        if (!witness.IsMatch)
            throw new Exception($"Nr positive reference lacks whole-query Exact witness:scenario={name};seed={witnessSeed};N={nKept};R={rKept};joint={denseExpected.Length};disposition={witness.Evaluation.Disposition};issue={witness.FailureCode}");
        foreach (bool compactInput in new[] { false, true })
        {
            // Sparse input remains ordered ABI1 and includes the real
            // positive witness, even when an arbitrary stride misses it.
            uint[] nrOrdinals = Enumerable.Range(0, window).Where(i => !compactInput || i % 3 == 0)
                .Select(i => (uint)i).Append(denseExpected[0]).Distinct().Order().ToArray();
            uint[] nrExpected = nrOrdinals.Where(i => {
                ulong root = Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i));
                return NeowFamilyReplay.Matches(root, n) && nr.Reference.Matches(root);
            }).ToArray();
            fixtures.Add(new { name = name, composite = nr.Metadata, compact = compactInput, start,
                count = window, ordinals = nrOrdinals, expected = nrExpected, gpu });
            if (nrExpected.Length == 0)
                throw new Exception($"Nr fixture lost positive witness:scenario={name};compact={compactInput};start={start};N={nKept};R={rKept};joint={denseExpected.Length}");
        }
        var accelerator = new NeowFamily(request, n, composite: nr);
        if (!accelerator.ConditionPerformance.UsesGpu || accelerator.PricingComposite != nr ||
            !accelerator.Coverage.ToHashSet().SetEquals(["N.Neow", "R.Relic"]) ||
            !FamilyExecutionCoordinator.Plan(request).OrderedFamilies.SelectMany(f => f.Coverage).ToHashSet().IsSupersetOf(accelerator.Coverage))
            throw new Exception("Nr explicit accelerator or default logical coverage unavailable:" + name);
        Console.WriteLine($"Nr positive Exact witness:scenario={name};seed={witnessSeed};start={start};N={nKept};R={rKept};joint={denseExpected.Length};PASS");
    }

    private static ExactSearchExecutionRequest CompileFixtureRequest(
        NeowSearchFilter filter, SearchContext context, SearchRunOptions options)
    {
        var compiled = SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(filter), context);
        var result = ExactSearchExecutionRequestFactory.Compile(compiled, options);
        return result.Plan ?? throw new InvalidOperationException("NeowFixtureCompileFailed:" + result.Issue);
    }

    private static void CheckLeafyPreGateAdmission(ExactSearchExecutionRequest source, NeowReplayPlan direct, List<object> fixtures)
    {
        byte leafy = Beta110FastRelicCatalog.LeafyPoultice, golden = Beta110FastRelicCatalog.GoldenPearl;
        byte arcane = Beta110FastRelicCatalog.ArcaneScroll, invalid = Beta110FastRelicCatalog.InvalidId;
        var condition = direct.StructuredConditions.Single(c => c.SourceRelicId == leafy);
        ushort[] strikes = [condition.Target0, ..Enumerable.Range(10_000, 63).Select(x => (ushort)x)];
        ushort[] defends = [condition.Target1, ..Enumerable.Range(20_000, 63).Select(x => (ushort)x)];
        var strongCatalog = direct.Authority.EffectCatalog with
        {
            LeafyTransformAuthorityExact = true,
            LeafyStrikeTransformPool = strikes,
            LeafyDefendTransformPool = defends
        };
        direct = direct with { Authority = direct.Authority with { EffectCatalog = strongCatalog } };
        if (!NeowFamilyGpuPlan.TryCreate(direct, out var directGpu, out _) || directGpu is null || !directGpu.UsesLeafyPreGate)
            throw new Exception("Strong direct Leafy pair did not enable pre-gate");
        AddFixture("LeafyPreGate.Direct", direct, directGpu, 262_144);
        NeowReplayPlan Bones(byte first, byte second, byte companion, bool fixedOrder) => direct with
        {
            Bones = true,
            RequireBones = true,
            Selected = Beta110FastRelicCatalog.NeowsBones,
            BonesAll = Beta110FastRelicCatalog.Bit(leafy) | Beta110FastRelicCatalog.Bit(companion),
            First = fixedOrder ? first : invalid,
            Second = fixedOrder ? second : invalid
        };
        bool Gate(NeowReplayPlan plan)
        {
            if (!NeowFamilyGpuPlan.TryCreate(plan, out var gpu, out _) || gpu is null)
                throw new Exception("Leafy admission fixture did not compile");
            return gpu.UsesLeafyPreGate;
        }
        NeowReplayPlan leafyFirst = Bones(leafy, arcane, arcane, true) with
        {
            Authority = direct.Authority with { BonesEligibleRelicIds = [leafy, arcane] }
        };
        if (!Gate(leafyFirst)) throw new Exception("Fixed Leafy-first route lost pre-gate");
        if (!NeowFamilyGpuPlan.TryCreate(leafyFirst, out var leafyFirstGpu, out _) || leafyFirstGpu is null)
            throw new Exception("Fixed Leafy-first GPU fixture unavailable");
        AddFixture("LeafyPreGate.BonesLeafyFirst", leafyFirst, leafyFirstGpu, 262_144);
        NeowReplayPlan arcaneFirst = Bones(arcane, leafy, arcane, true);
        if (Gate(arcaneFirst)) throw new Exception("Unproved predecessor entered Leafy pre-gate");
        if (!Gate(arcaneFirst with { LeafyBonesInitialTransformInvariant = true }))
            throw new Exception("Proved vanilla predecessor lost Leafy pre-gate");
        if (!Gate(Bones(invalid, invalid, golden, false))) throw new Exception("GoldenPearl pair did not enter safe pre-gate");
        NeowReplayPlan anyArcane = Bones(invalid, invalid, arcane, false);
        if (Gate(anyArcane)) throw new Exception("Unproved any-order companion entered Leafy pre-gate");
        if (!Gate(anyArcane with { LeafyBonesInitialTransformInvariant = true }))
            throw new Exception("Proved vanilla any-order companion lost Leafy pre-gate");

        RuntimeContextAuthoritySnapshot beta111Authority = Beta110FastSearchFixtureAuthority.CreateBeta111(
            source.CompiledSearch.Context.CharacterKey, source.CompiledSearch.Context.Ascension,
            source.CompiledSearch.Context.Authority.PlayerSlotIndex,
            source.CompiledSearch.Context.Authority.PlayersCount);
        CheckPrecariousShearsLeafyCoreProjection(beta111Authority);
        var beta111Detection = new GameVersionDetection(RuntimeVersionCompatibility.Beta111Version,
            RuntimeVersionCompatibility.Beta111Version, "beta", "leafy-beta111-fixture", true, string.Empty);
        var beta111Context = source.CompiledSearch.Context with
        {
            ProfileId = RuntimeProfileId.Beta111,
            Authority = beta111Authority,
            Detection = beta111Detection
        };
        var beta111DirectRequest = CompileFixtureRequest(source.Evaluation.ToLegacyFilter(), beta111Context, source.RunOptions);
        NeowReplayPlan beta111Direct = NeowReplayPlan.Compile(beta111DirectRequest, false);
        if (!NeowFamilyGpuPlan.TryCreate(beta111Direct, out var beta111DirectGpu, out _) || beta111DirectGpu is null ||
            !beta111DirectGpu.UsesLeafyPreGate || beta111DirectGpu.Meta[58] != 87 || beta111DirectGpu.Meta[59] != 87)
            throw new Exception("Compiled Beta111 source-sized direct Leafy shape did not enable the 87x87 pre-gate");
        AddPositiveWindowFixtures("LeafyPreGate.Beta111Direct.SourceSized", beta111Direct, beta111DirectGpu);
        NeowSearchFilter beta111BonesFilter = source.Evaluation.ToLegacyFilter() with
        {
            NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.NeowsBones),
            RequireNeowsBones = true,
            BonesRelics = new ModelKeySetFilter([], [BaseGameModelKeys.Relics.LeafyPoultice,
                BaseGameModelKeys.Relics.ArcaneScroll], [])
        };
        var beta111Request = CompileFixtureRequest(beta111BonesFilter, beta111Context, source.RunOptions);
        NeowReplayPlan beta111Bones = NeowReplayPlan.Compile(beta111Request, false);
        if (!beta111Bones.LeafyBonesInitialTransformInvariant)
            throw new Exception("Compiled audited Beta111 vanilla deck did not prove the Bones Leafy source invariant");
        if (!NeowFamilyGpuPlan.TryCreate(beta111Bones, out var beta111Gpu, out _) || beta111Gpu is null ||
            !beta111Gpu.UsesLeafyPreGate || !beta111Gpu.UsesStagedDense)
            throw new Exception("Compiled audited Beta111 vanilla Bones shape did not select staged Leafy pre-gate");
        if (beta111Gpu.Meta[58] != 87 || beta111Gpu.Meta[59] != 87)
            throw new Exception($"Beta111 source-sized transform fixture expected 87x87 candidates, got {beta111Gpu.Meta[58]}x{beta111Gpu.Meta[59]}");
        AddPositiveWindowFixtures("LeafyPreGate.Beta111Staged.AnyOrder", beta111Bones, beta111Gpu);
        NeowSearchFilter precariousFilter = beta111BonesFilter with
        {
            BonesRelics = new ModelKeySetFilter([], [BaseGameModelKeys.Relics.LeafyPoultice,
                BaseGameModelKeys.Relics.PrecariousShears], [])
        };
        var precariousRequest = CompileFixtureRequest(precariousFilter, beta111Context, source.RunOptions);
        NeowReplayPlan beta111FixedPredecessor = NeowReplayPlan.Compile(precariousRequest, false) with
        {
            First = Beta110FastRelicCatalog.PrecariousShears,
            Second = leafy
        };
        if (!beta111FixedPredecessor.LeafyBonesInitialTransformInvariant)
            throw new Exception("Beta111 PrecariousShears predecessor did not retain the audited Leafy source invariant");
        if (!NeowFamilyGpuPlan.TryCreate(beta111FixedPredecessor, out var fixedGpu, out _) || fixedGpu is null || !fixedGpu.UsesLeafyPreGate)
            throw new Exception("Compiled Beta111 PrecariousShears predecessor lost Leafy pre-gate");
        AddPositiveWindowFixtures("LeafyPreGate.Beta111Staged.PrecariousShearsFirst", beta111FixedPredecessor, fixedGpu);
        ushort finalCurse = beta111Bones.Authority.EffectCatalog.GeneratedCurseIds[0];
        NeowReplayPlan combined = beta111Bones with
        {
            EnabledDomains = beta111Bones.EnabledDomains | Beta110FastDomain.FinalCurse,
            RequiredFinalCurseIds = [finalCurse]
        };
        if (!NeowFamilyGpuPlan.TryCreate(combined, out var combinedGpu, out _) || combinedGpu is null || !combinedGpu.UsesLeafyPreGate)
            throw new Exception("Leafy plus Final Curse lost pre-gate");
        AddPositiveWindowFixtures("LeafyPreGate.Beta111Staged.FinalCurse", combined, combinedGpu);
        var single = direct with { StructuredConditions = direct.StructuredConditions.Select(c =>
            c.SourceRelicId == leafy ? c with { TargetCount = 1 } : c).ToArray() };
        if (Gate(single)) throw new Exception("Wide single-output Leafy predicate entered pre-gate");
        Console.WriteLine("Leafy pre-gate admission: direct, Leafy-first, vanilla invariant, GoldenPearl, unproved predecessor and wide bypass: PASS");

        void AddFixture(string name, NeowReplayPlan plan, NeowFamilyGpuPlan gpu, int count)
        {
            uint[] ordinals = Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
            uint[] expected = ordinals.Where(i => NeowFamilyReplay.Matches(
                Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i)), plan)).ToArray();
            if (expected.Length == 0) throw new Exception(name + " needs positive matches");
            fixtures.Add(new { name, compact = false, start = 0UL, count, ordinals, expected, gpu });
        }
        void AddPositiveWindowFixtures(string name, NeowReplayPlan plan, NeowFamilyGpuPlan gpu)
        {
            const int window = 262_144;
            ulong start = 0;
            uint[] expected = [];
            for (int attempt = 0; attempt < 256 && expected.Length == 0; attempt++)
            {
                expected = Enumerable.Range(0, window).Select(i => (uint)i).Where(i => NeowFamilyReplay.Matches(
                    Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i)), plan)).ToArray();
                if (expected.Length == 0) start += window;
            }
            if (expected.Length == 0) throw new Exception(name + " could not find a positive window");
            uint[] dense = Enumerable.Range(0, window).Select(i => (uint)i).ToArray();
            fixtures.Add(new { name, compact = false, start, count = window, ordinals = dense, expected, gpu });
            uint[] compact = Enumerable.Range(0, 4096).Select(i => (uint)i).Concat(expected).Distinct().Order().ToArray();
            uint[] compactExpected = compact.Where(i => NeowFamilyReplay.Matches(
                Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i)), plan)).ToArray();
            fixtures.Add(new { name = name + ".Compact", compact = true, start, count = window,
                ordinals = compact, expected = compactExpected, gpu });
        }
    }

    private static void CheckPrecariousShearsLeafyCoreProjection(RuntimeContextAuthoritySnapshot authority)
    {
        static ModelKey[] TransformTargets(IEnumerable<PredictedEffectGroup> groups) => groups
            .SelectMany(group => group.OrderedItems)
            .Where(item => item.Kind == PredictedEffectKind.TransformCard && item.TargetKey.HasValue)
            .Select(item => item.TargetKey!.Value).ToArray();

        for (ulong ordinal = 0; ordinal < 100_000; ordinal++)
        {
            ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            NeowEffectProjection bones = Beta111NeowEffectProjector.ProjectFromRootHash(
                BaseGameModelKeys.Relics.NeowsBones, PredictionPrecision.Exact, authority, root,
                enableComplexBonesDeckInteractions: true);
            if (bones.BonesOutcome is not { } outcome ||
                !outcome.OfferedRelics.Contains(BaseGameModelKeys.Relics.PrecariousShears) ||
                !outcome.OfferedRelics.Contains(BaseGameModelKeys.Relics.LeafyPoultice))
                continue;

            ModelKey[] direct = TransformTargets(Beta111NeowEffectProjector.ProjectFromRootHash(
                BaseGameModelKeys.Relics.LeafyPoultice, PredictionPrecision.Exact, authority, root).EffectGroups);
            BonesAcquisitionRouteResult[] routes = outcome.OriginalRoutes.Where(route =>
                route.AcquisitionOrder.SequenceEqual(new[] { BaseGameModelKeys.Relics.PrecariousShears,
                    BaseGameModelKeys.Relics.LeafyPoultice })).ToArray();
            if (direct.Length != 2 || routes.Length == 0)
                throw new Exception("Core projection did not expose the expected PrecariousShears -> Leafy route");
            foreach (BonesAcquisitionRouteResult route in routes)
            {
                BonesRelicScopedResult leafy = route.RelicScopedResults.Single(result =>
                    result.SourceRelicKey == BaseGameModelKeys.Relics.LeafyPoultice);
                if (!TransformTargets(leafy.EffectGroups).SequenceEqual(direct))
                    throw new Exception("PrecariousShears changed Leafy's Core Transformations result");
            }
            Console.WriteLine($"Leafy Core projection after PrecariousShears: routes={routes.Length};ordinal={ordinal};targets unchanged: PASS");
            return;
        }
        throw new Exception("Could not find a Beta111 Bones PrecariousShears + Leafy projection fixture");
    }

    private static void CheckTerminalCompletionTracking()
    {
        var fullRootBatch = new SearchBatch(1_000, FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize);
        var empty = FamilyCandidateSet.FromSortedAbi1(fullRootBatch, []);
        ulong fullRootBatchEnd = 1_000UL + (ulong)FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
        if (ProductionSearchSession.DeriveSafeNextOrdinal(empty, []) != fullRootBatchEnd)
            throw new Exception("Zero-survivor full root batch cursor did not reach batch end");

        var batch = new SearchBatch(1_000, 100);
        var sparse = FamilyCandidateSet.FromSortedAbi1(batch, [3, 20, 90]);
        if (ProductionSearchSession.DeriveSafeNextOrdinal(sparse, [true, false, true]) != 1_020)
            throw new Exception("Out-of-order Exact completion crossed the first unfinished survivor");
        if (ProductionSearchSession.DeriveSafeNextOrdinal(sparse, [false, true, true]) != 1_003)
            throw new Exception("Cancelled terminal work crossed its first unfinished survivor");
        if (ProductionSearchSession.DeriveSafeNextOrdinal(sparse, [true, true, true]) != 1_100)
            throw new Exception("Completed sparse terminal batch did not reach batch end");

        var dense = FamilyCandidateSet.Dense(new SearchBatch(1_100, 4));
        if (ProductionSearchSession.DeriveSafeNextOrdinal(dense, [true, true, false, true]) != 1_102)
            throw new Exception("Dense terminal cursor crossed the first unfinished candidate");
        Console.WriteLine("Terminal completion tracking: zero, sparse, out-of-order, cancellation seam and continuity: PASS");
    }

    internal static int CheckCapsuleObtainPolicy()
    {
        var context = RolltheSpire2.Infrastructure.Snapshots.ProductionSearchReplay.Read<
            RolltheSpire2.Infrastructure.Snapshots.ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        ModelKey Key(string value) => ModelKey.TryParseExact(value, out var key) ? key : throw new Exception(value);
        ModelKey[] targets = [Key("RELIC:DEMON_TONGUE"), Key("RELIC:FROZEN_EGG"), Key("RELIC:OLD_COIN")];
        var query = SearchQuery.Empty with {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule], BonesRouteOrderMode.AnyOrder),
            StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.NeowsBones,
                NeowStructuredConditionKind.ExactGroupedCapsuleMultiset, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, targets, AllowDuplicateOutputs: false)] };
        const string seed = "R93FGU9M7R67";
        var originalEffects = context.Authority.EffectAuthority!;
        if (originalEffects.OrderedRelicBag!.Single(r => r.RelicKey == targets[2]).NestedEffectKind != NestedRelicEffectKind.UnknownHook)
            throw new Exception("Old Coin fixture must retain its original unknown pickup classification");
        int checks = 0;
        foreach (bool classificationMissing in new[] { false, true })
        {
            var effects = originalEffects;
            if (classificationMissing)
            {
                NeowEffectRelicSnapshot Change(NeowEffectRelicSnapshot relic) => targets.Contains(relic.RelicKey)
                    ? relic with { NestedEffectKind = NestedRelicEffectKind.UnknownHook, NestedClassificationExact = false } : relic;
                effects = effects with { OrderedRelicBag = effects.OrderedRelicBag!.Select(Change).ToArray(),
                    SharedRelicPoolSource = effects.SharedRelicPoolSource!.Select(Change).ToArray(),
                    CharacterRelicPoolSource = effects.CharacterRelicPoolSource!.Select(Change).ToArray() };
            }
            var inputContext = context with { Authority = context.Authority.WithEffectAuthority(effects) };
            var compiled = SearchCompiler.Compile(query, inputContext);
            var plan = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 1)).Plan!;
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
            var result = ProductionExactSearchEvaluator.Evaluate(plan, input);
            if (!result.IsMatch) throw new Exception("Capsule whitelist Exact: " + result.FailureCode);
            // The information projector uses the same observation boundary without a Query premise.
            var projection = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.NeowsBones,
                PredictionPrecision.Exact, result.Authority, input.RootHash, enableComplexBonesDeckInteractions: true);
            foreach (var route in projection.BonesOutcome!.OriginalRoutes)
            {
                if (route.ProductRelevantProjectionPrecision != PredictionPrecision.Exact)
                    throw new Exception("Untracked obtain downgraded information precision");
                var actual = route.RelicScopedResults.SelectMany(r => r.EffectGroups).SelectMany(g => g.OrderedItems)
                    .Where(e => e.Kind == PredictedEffectKind.AddRelic && e.Relation == PredictedEffectRelation.NestedRelic)
                    .Select(e => e.TargetKey!.Value).ToArray();
                if (actual.Length != 3 || !targets.All(actual.Contains)) throw new Exception("Capsule identity changed");
                checks++;
            }
        }
        Console.WriteLine($"Capsule obtain whitelist: Exact and information; both orders; unknown/missing non-whitelist classification: PASS ({checks})");
        return 0;
    }

    // Original iterator scheduling remains an independent control for N05.
    // The party control deliberately evaluates both orders even after a match.
    private static bool OriginalRouteSchedule(ulong root, NeowReplayPlan plan)
    {
        var observation = NeowFamilyReplay.Observe(root, plan);
        if (!observation.IdentityPass) return false;
        if (plan.StructuredConditions.Length == 0 && !plan.HasFinalCurseFastProjection) return true;
        bool keep = false;
        foreach (var route in NeowFamilyReplay.Routes(plan, observation))
        {
            bool current = NeowFamilyReplay.Route(root, plan, route.First, route.Second, observation.RewardsAfterBones);
            if (plan.Authority.PlayersCount == 1 && current) return true;
            keep |= current;
        }
        return keep;
    }

    private static void CheckPhysicalPreparationIsolation()
    {
        var character = BaseGameModelKeys.Characters.Defect;
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(character, 10, 0, 1);
        var context = new SearchContext(RuntimeProfileId.Beta111, character, 10, authority,
            new("0.111.0", "0.111.0", "beta", "N-preparation-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
        ExactSearchExecutionRequest Request(SearchContext input, SearchQuery query)
        {
            var compiled = SearchCompiler.Compile(query, input);
            return new(compiled, new("000000000000", 4096, 30, 1), "000000000000", 4096,
                CompiledSearchEvaluationProjector.Project(compiled).Evaluation, CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
        }
        var identityQuery = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with {
            NeowRelics = new([], [BaseGameModelKeys.Relics.GoldenPearl], []) } };
        var solo = Request(context, identityQuery);
        string? oldExperiment = Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT");
        try
        {
            Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", "invalid-preparation-fixture");
            var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(solo, false);
            if (registered.Single(f => f.FamilyId == "N.Neow") is not NeowFamily { GpuPlan: null })
                throw new Exception("Explicit CPU registration lost N under an invalid GPU experiment");
            var gpuAttempt = FamilyExecutionCoordinator.CreateRegisteredFamilies(solo, true).OfType<NeowFamily>().Single();
            if (!gpuAttempt.GpuPreparationFailed || gpuAttempt.ConditionPerformance.UsesGpu)
                throw new Exception("GPU construction failure did not retain the N CPU realization");
            var selected = FamilyExecutionCoordinator.Plan(solo, true);
            if (selected.GpuPreparationFallbacks.Count != 1 ||
                !selected.GpuPreparationFallbacks[0].Detail.Contains("invalid-preparation-fixture", StringComparison.Ordinal) ||
                FamilyExecutionCoordinator.Plan(solo, false).GpuPreparationFallbacks.Count != 0 ||
                FamilyExecutionCoordinator.SelectedPreparationFallbacks([gpuAttempt], FamilyPlanner.InOrder([])).Count != 0)
                throw new Exception("N selected fallback evidence lost its cause or reported an unselected/explicit CPU fallback");
        }
        finally { Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", oldExperiment); }
        var world = RelicSequenceFixtureFactory.CreateModern();
        var players = Enumerable.Range(0, 2).Select(slot =>
        {
            var player = Beta110FastSearchFixtureAuthority.CreateBeta111(character, 10, slot, 2);
            var effects = player.EffectAuthority!;
            effects = effects with { TransformPool = effects.TransformPool!.Where(c =>
                c.CardKey.Entry is "BETA111_TRANSFORM_19" or "BETA111_TRANSFORM_20" or "BETA111_TRANSFORM_21").ToArray() };
            return player.WithEffectAuthority(effects).WithWorldAuthority(world with {
                CapturedProfileId = RuntimeProfileId.Beta111,
                Beta109Generation = world.Beta109Generation! with {
                    Profile = RuntimeProfileId.Beta111, GameMode = WorldGameMode.Multiplayer,
                    IsMultiplayer = true, PlayerCount = 2, PersonalPlayerSlot = slot, PartyBossDiscoveryExact = true,
                    LobbyPlayers = Enumerable.Range(0, 2).Select(i => new Beta109LobbyPlayerSnapshot(i, character, false, true)).ToArray(),
                    PartyRelicBuckets = Enumerable.Repeat(world.Beta109Generation.PlayerRelicBuckets, 2).ToArray()
                }
            });
        }).ToArray();
        var party = new OrderedPartyAuthority(players, ["ControlledNFixture", "ControlledNFixture"], players[0].WorldAuthority!);
        var partyContext = context with { Authority = players[0], Party = party };
        SearchQuery Personal(int target) => SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
            StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.LeafyPoultice, NeowStructuredConditionKind.ExactUnorderedPair,
                NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card, [new("CARD", "BETA111_TRANSFORM_" + target)])] };
        var query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = Personal(19) },
            new(1, ModelKeySetFilter.Empty) { Conditions = Personal(20) }] };
        var request = Request(partyContext, query);
        ulong[] Execute(IFamilyInvocation family, ExactSearchExecutionRequest input)
        {
            var execution = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true);
            try
            {
                var batch = new SearchBatch(0, 4096);
                return family.InvokeAsync(execution, new(batch, input), FamilyCandidateSet.Dense(batch), default)
                    .AsTask().GetAwaiter().GetResult().EnumerateLogicalOrdinals().ToArray();
            }
            finally { family.DisposeAsync(execution).AsTask().GetAwaiter().GetResult(); execution.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        var expected = Execute(new NeowFamily.PartyInitial(request, optimize: false), request);
        if (!Execute(new NeowFamily.PartyInitial(request), request).SequenceEqual(expected))
            throw new Exception("Party numerical offers/local observation reuse changed survivors");
        // This is controlled identity correctness evidence, not a real runtime
        // party throughput fixture. Distinct Any gates must remain an AND.
        foreach (int count in new[] { 2, 3, 4 })
        {
            // The controlled players and shared World must describe the same
            // complete ordered roster. A solo World cannot establish a party.
            var identityWorld = players[0].WorldAuthority! with { Beta109Generation = players[0].WorldAuthority!.Beta109Generation! with {
                PlayerCount = count,
                LobbyPlayers = Enumerable.Range(0, count).Select(slot => new Beta109LobbyPlayerSnapshot(slot, character, false, true)).ToArray(),
                PartyRelicBuckets = Enumerable.Repeat(world.Beta109Generation!.PlayerRelicBuckets, count).ToArray()
            } };
            var identities = Enumerable.Range(0, count).Select(slot =>
                Beta110FastSearchFixtureAuthority.CreateBeta111(character, 10, slot, count).WithWorldAuthority(identityWorld with {
                    Beta109Generation = identityWorld.Beta109Generation! with { PersonalPlayerSlot = slot }
                })).ToArray();
            var identityParty = new OrderedPartyAuthority(identities, Enumerable.Repeat("ControlledNIdentity", count).ToArray(), identities[0].WorldAuthority!);
            var identityContext = context with { Authority = identities[0], Party = identityParty };
            foreach (bool combined in new[] { false, true })
            {
                var gates = SearchQuery.Empty with { Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot,
                    new([BaseGameModelKeys.Relics.GoldenPearl, BaseGameModelKeys.Relics.LeafyPoultice], [], []))
                { Conditions = SearchQuery.Empty with {
                    OpeningRoute = combined ? new(BaseGameModelKeys.Relics.LeafyPoultice) : null,
                    LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = new(
                        [BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.LeafyPoultice], [], [BaseGameModelKeys.Relics.DowsingRod]) },
                    LegacyWorld = SearchQuery.Empty.LegacyWorld with { AncientOptionFilters = [new(1, new(
                        [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LeafyPoultice], [], []))] }
                } }).ToArray() };
                var gateRequest = Request(identityContext, gates);
                var family = new NeowFamily.PartyInitial(gateRequest);
                for (ulong root = 0; root < 4096; root++)
                    if (family.MatchesInitialOffers(root) != family.MatchesInitialOffersReference(root))
                        throw new Exception($"Party numerical independent-gate parity {count}/{combined}/{root}");
                if (!Execute(family, gateRequest).SequenceEqual(Execute(new NeowFamily.PartyInitial(gateRequest, optimize: false), gateRequest)))
                    throw new Exception("Party numerical dense/ordinal envelope differs from rich gates");
            }
        }
        var firstOnlyQuery = query with { Players = query.Players.Select(p => p.Slot == 1
            ? p with { Conditions = p.Conditions with { StructuredOpeningEffects = [] } } : p).ToArray() };
        var firstOnly = Request(partyContext, firstOnlyQuery);
        if (expected.Length == 0 || !Execute(new NeowFamily.PartyInitial(firstOnly), firstOnly).Except(expected).Any())
            throw new Exception("Party preparation fixture must expose a later-slot rejection and positive full matches");
        foreach (int failedSlot in new[] { 0, 1 })
        {
            int failures = 0;
            bool Prepare(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue)
            {
                gpu = null; issue = "FixtureNotAdmitted";
                if (plan.Authority.PlayerSlotIndex == failedSlot) { failures++; throw new InvalidOperationException("InjectedNPreparationFailure"); }
                return false;
            }
            var failed = new NeowFamily.PartyInitial(request, preferGpu: true, gpuPlanFactory: Prepare);
            var selectedFallbacks = FamilyExecutionCoordinator.SelectedPreparationFallbacks([failed], FamilyPlanner.InOrder([failed]));
            if (selectedFallbacks.Count != 1 || !selectedFallbacks[0].Detail.Contains("InjectedNPreparationFailure", StringComparison.Ordinal) ||
                !selectedFallbacks[0].Detail.Contains("slot=" + failedSlot, StringComparison.Ordinal))
                throw new Exception("Party GPU preparation failure lost selected fallback slot/exception evidence");
            if (failures != 1 || failed.ConditionPerformance.UsesGpu || !Execute(failed, request).SequenceEqual(expected))
                throw new Exception("GPU preparation failure lost whole-party CPU predicates at slot " + failedSlot);
        }
        Console.WriteLine("N physical preparation: CPU ignores invalid GPU experiment; both party failure positions retain all predicates: PASS");
    }
    private static void CheckCoreIsolation()
    {
        // The compiled input itself must exclude party-only state/operators;
        // driver dead-code elimination is not the isolation contract.
        foreach (int stage in new[] { 0, 1, 2, 3 })
        foreach (bool upgrades in new[] { false, true })
        {
            string solo = NeowFamilyGpuExecutor.ShaderSource(stage, authoredUpgrades: upgrades);
            foreach (string forbidden in new[] { "n_arrival", "party_plan", "execute_massive_scroll_route",
                "players_count", "plan_value(5u)", "plan_value(6u)" })
                if (solo.Contains(forbidden, StringComparison.Ordinal))
                    throw new Exception("Singleplayer shader contains party code: " + forbidden);
            string party = NeowFamilyGpuExecutor.ShaderSource(stage, authoredUpgrades: upgrades, multiplayer: true);
            if (!party.Contains("n_arrival", StringComparison.Ordinal) ||
                !party.Contains("execute_massive_scroll_route", StringComparison.Ordinal) ||
                !party.Contains("plan_value(5u)", StringComparison.Ordinal))
                throw new Exception("Party shader lost its owner/prefix/effect implementation");
        }
        Console.WriteLine("N singleplayer/party compiled-source isolation: PASS");
    }

    private static void CheckGroupedCapsuleUi(ExactSearchExecutionRequest source, List<object> fixtures)
    {
        var original = source;
        if (!RelicFamilyPlanCompiler.TryCompilePool(original, out var pool, out var dense, out _, out _))
            throw new Exception("Grouped UI pool unavailable");
        ModelKey Key(int rarity)
        {
            int bucket = Enumerable.Range(0, pool.BucketCount).Single(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] == rarity);
            return dense.Single(p => p.Value == pool.DenseRelicIds[pool.BucketOffsets[bucket]]).Key;
        }
        ModelKey[] pair = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule];
        foreach (int targets in new[] { 1, 2, 3 })
        foreach (int pickup in new[] { 0, 1, 2 })
        {
            var condition = new NeowStructuredEffectSearchCondition(BaseGameModelKeys.Relics.NeowsBones,
                NeowStructuredConditionKind.ExactGroupedCapsuleMultiset, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, Enumerable.Range(1, targets).Select(Key).ToArray(), AllowDuplicateOutputs: false);
            ExactSearchExecutionRequest Request(bool allowDuplicates)
            {
                var filter = source.Evaluation.ToLegacyFilter() with { NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                    RequiredBonesCombination = pair, RequiredBonesAcquisitionOrder = pickup == 0 ? [] : pickup == 1 ? pair : pair.AsEnumerable().Reverse().ToArray(),
                    StructuredNeowEffects = [condition with { AllowDuplicateOutputs = allowDuplicates }] };
                return WithSmallRelicLanes(CompileFixtureRequest(filter, source.CompiledSearch.Context, source.RunOptions));
            }
            var current = Request(false); var legacy = Request(true);
            var currentRoute = NeowReplayPlan.Compile(current, true); var legacyRoute = NeowReplayPlan.Compile(legacy, true);
            if (!RelicFullGpuPlan.TryCreate(current, currentRoute, out var gpu, out var issue) || gpu is null ||
                !RelicFullGpuPlan.TryCreate(legacy, legacyRoute, out var oldGpu, out _) || oldGpu is null ||
                !gpu.CapsuleMetadata.SequenceEqual(oldGpu.CapsuleMetadata))
                throw new Exception("Grouped UI admission/metadata: " + issue);
            if (pickup == 0)
            {
                if (!NeowCapsuleComposite.TryCreate(current, NeowReplayPlan.Compile(current, false), out var composite) || composite is null ||
                    !FamilyExecutionCoordinator.Plan(current).Stages.SelectMany(s => s.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
                    throw new Exception("Grouped UI lost the joint numerical body or a selected logical obligation");
            }
            AddRfullFixtures(current, $"BonesGroupedUi.Targets{targets}.Pickup{pickup}", fixtures);
        }
    }

    private static void CheckNrFixtures(ExactSearchExecutionRequest source, List<object> fixtures, List<object> rfullFixtures)
    {
        CheckGroupedCapsuleUi(source, rfullFixtures);
        var request = source;
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out _)) throw new Exception("Nr World pool unavailable");
        ModelKey Key(int lane, int position)
        {
            int bucket = Enumerable.Range(0, pool.BucketCount).Single(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] == lane);
            ushort id = pool.DenseRelicIds[pool.BucketOffsets[bucket]+position];
            return dense.Single(p => p.Value == id).Key;
        }
        foreach (int shape in Enumerable.Range(0,6))
        {
            bool small = shape == 0;
            ModelKey capsule = small ? BaseGameModelKeys.Relics.SmallCapsule : BaseGameModelKeys.Relics.LargeCapsule;
            ModelKey[] targets = shape >= 4 ? [Key(shape - 2, 0)]
                : shape < 2 ? [Key(1,0)] : [Key(1,0), Key(shape == 2 ? 1 : 2,1)];
            var condition = source.Evaluation.ToLegacyFilter().StructuredNeowEffects.Single() with { SourceRelicKey = capsule,
                Kind = small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair, OutputKeys = targets };
            var filter = source.Evaluation.ToLegacyFilter() with { NeowRoute = new NeowRouteSearchCondition(capsule), StructuredNeowEffects = [condition] };
            var query = CompileFixtureRequest(filter, source.CompiledSearch.Context, source.RunOptions);
            var n = NeowReplayPlan.Compile(query,false);
            if (!NeowCapsuleComposite.TryCreate(query,n,out var nr) || nr is null || !NeowFamilyGpuPlan.TryCreate(n,out var gpu,out _))
                throw new Exception("Nr positive World-authority fixture unavailable");
            var selected = FamilyExecutionCoordinator.Plan(query);
            if (!CapsuleRelicFamily.TryCreate(query,out var rFamily) || rFamily is null) throw new Exception("R baseline unavailable");
            AddRfullFixtures(query, $"CapsuleOnly.Shape{shape}", rfullFixtures);
            if (!selected.Stages.SelectMany(stage => stage.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
                throw new Exception("Greedy numerical allocation lost an authored capsule obligation");
            var withSequence = query with { Evaluation = query.Evaluation with {
                RelicSequenceConditions = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, new([], [Key(1,0)], []))] } };
            AddRfullFixtures(withSequence, $"CapsuleOrdinary.Shape{shape}", rfullFixtures);
            var withShop = query with { Evaluation = query.Evaluation with {
                RelicShopSequenceConditions = [new(2, CombatRewardSequenceOrderMode.Unordered, [Key(4,0), null])] } };
            AddRfullFixtures(withShop, $"CapsuleShop.Shape{shape}", rfullFixtures);
            if (shape == 1)
            {
                var duplicateCondition = condition with { OutputKeys = [targets[0], targets[0]], AllowDuplicateOutputs = true };
                var duplicateFilter = filter with { StructuredNeowEffects = [duplicateCondition] };
                var duplicateRequest = CompileFixtureRequest(duplicateFilter, source.CompiledSearch.Context, source.RunOptions);
                NeowReplayPlan duplicateRoute = NeowReplayPlan.Compile(duplicateRequest, true);
                if (RelicFullGpuPlan.TryCreate(duplicateRequest, duplicateRoute, out _, out _) ||
                    !CapsuleRelicFamily.TryCreate(duplicateRequest, out IFamilyInvocation? duplicateFamily) ||
                    duplicateFamily is null || duplicateFamily.ConditionPerformance.UsesGpu)
                    throw new Exception("Unsupported duplicate Capsule shape did not select explicit CPU R fallback");

                var unboundFilter = filter with { NeowRoute = null };
                var unboundRequest = CompileFixtureRequest(unboundFilter, source.CompiledSearch.Context, source.RunOptions);
                if (CapsuleRelicFamily.TryCreate(unboundRequest, out _))
                    throw new Exception("Unbound Capsule projection silently entered R filtering");
            }
            if (NeowCapsuleComposite.TryCreate(withSequence,n,out _))
                throw new Exception("Nr claimed incomplete R sequence Coverage");
            if (!FamilyExecutionCoordinator.Plan(withSequence).Stages.SelectMany(s => s.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
                throw new Exception("Sequence query lost an independent logical obligation");
            AddPositiveNrFixtures(query, n, nr, gpu, "Nr.WorldShape" + shape, fixtures);
        }

        ExactSearchExecutionRequest Request(NeowSearchFilter filter) =>
            CompileFixtureRequest(filter, source.CompiledSearch.Context, source.RunOptions);
        NeowSearchFilter BonesFilter(NeowStructuredEffectSearchCondition condition, params ModelKey[] pair) =>
            source.Evaluation.ToLegacyFilter() with
            {
                NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.NeowsBones),
                RequiredBonesCombination = pair,
                RequiredBonesAcquisitionOrder = [],
                StructuredNeowEffects = [condition]
            };

        ModelKey companion = BaseGameModelKeys.Relics.GoldenPearl;
        var bonesSmall = new NeowStructuredEffectSearchCondition(
            BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [Key(1, 0)]);
        AddRfullFixtures(Request(BonesFilter(bonesSmall, BaseGameModelKeys.Relics.SmallCapsule, companion)),
            "BonesSmallCapsule", rfullFixtures);

        var bonesLarge = new NeowStructuredEffectSearchCondition(
            BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [Key(1, 0), Key(2, 0)]);
        AddRfullFixtures(Request(BonesFilter(bonesLarge, BaseGameModelKeys.Relics.LargeCapsule, companion)),
            "BonesLargeCapsule", rfullFixtures);

        // Admission is about Capsule continuation, not all pickup state. Exercise
        // both offer layouts through existing fixtures and all acquisition orders.
        foreach (ModelKey neutral in new[] { BaseGameModelKeys.Relics.CursedPearl,
                     BaseGameModelKeys.Relics.DowsingRod, BaseGameModelKeys.Relics.NeowsSacrifice,
                     BaseGameModelKeys.Relics.PrecariousShears, BaseGameModelKeys.Relics.SilkenTress,
                     BaseGameModelKeys.Relics.SilverCrucible, BaseGameModelKeys.Relics.LeafyPoultice })
        foreach (bool smallCapsule in new[] { true, false })
        foreach (int pickup in new[] { 0, 1, 2 })
        {
            var capsuleCondition = smallCapsule ? bonesSmall : bonesLarge;
            ModelKey capsule = capsuleCondition.SourceRelicKey;
            var filter = BonesFilter(capsuleCondition, neutral, capsule) with {
                RequiredBonesAcquisitionOrder = pickup == 0 ? [] : pickup == 1 ? [capsule, neutral] : [neutral, capsule] };
            AddRfullFixtures(Request(filter), $"BonesNeutral.{neutral}.{smallCapsule}.Pickup{pickup}", rfullFixtures);
        }
        foreach (byte consuming in new[] { Beta110FastRelicCatalog.ArcaneScroll, Beta110FastRelicCatalog.HeftyTablet,
                     Beta110FastRelicCatalog.LeadPaperweight, Beta110FastRelicCatalog.LostCoffer,
                     Beta110FastRelicCatalog.Kaleidoscope, Beta110FastRelicCatalog.ScrollBoxes,
                     Beta110FastRelicCatalog.SmallCapsule, Beta110FastRelicCatalog.LargeCapsule,
                     Beta110FastRelicCatalog.MassiveScroll, Beta110FastRelicCatalog.InvalidId })
            if (RelicFullGpuPlan.CapsuleContinuationNeutralCompanion(consuming))
                throw new Exception("Relevant/unknown companion was admitted as Capsule-neutral");

        // Acquisition order is independent of Bones offer order. The synthetic
        // authority intentionally stores the neutral companion before Capsule;
        // both shuffle layouts must still admit the Small -> companion route.
        NeowSearchFilter exactSmallFirst = BonesFilter(bonesSmall, companion, BaseGameModelKeys.Relics.SmallCapsule) with
        {
            RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.SmallCapsule, companion]
        };
        AddRfullFixtures(Request(exactSmallFirst), "BonesSmallExactPickupReverseOffer", rfullFixtures);

        ExactSearchExecutionRequest Grouped(ModelKey[] targets, bool withSequence)
        {
            var grouped = new NeowStructuredEffectSearchCondition(
                BaseGameModelKeys.Relics.NeowsBones,
                NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic,
                targets,
                AllowDuplicateOutputs: true);
            NeowSearchFilter filter = BonesFilter(grouped,
                BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule);
            ExactSearchExecutionRequest result = Request(filter);
            return withSequence
                ? result with { Evaluation = result.Evaluation with {
                    RelicSequenceConditions = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2,
                        new([], [Key(1, 0)], []))] } }
                : result;
        }
        // Closed-form three-item lane oracle, independent of the scenario-union implementation.
        foreach (var sample in new[] {
            (Targets:new[]{Key(1,0)}, P:.5),
            (Targets:new[]{Key(1,0),Key(1,1)}, P:.25),
            (Targets:new[]{Key(1,0),Key(1,1),Key(1,2)}, P:.125),
            (Targets:new[]{Key(1,0),Key(2,0),Key(3,0)}, P:6*.5*.33*.17/27),
            (Targets:new[]{Key(1,0),Key(1,0)}, P:0d) })
        foreach (int pickup in new[]{0,1,2}) {
            var q=WithSmallRelicLanes(Grouped(sample.Targets,false));
            q=q with {Evaluation=q.Evaluation with {RequiredBonesAcquisitionOrder=pickup==0?[]:pickup==1?
                [BaseGameModelKeys.Relics.SmallCapsule,BaseGameModelKeys.Relics.LargeCapsule]:
                [BaseGameModelKeys.Relics.LargeCapsule,BaseGameModelKeys.Relics.SmallCapsule]}};
            if (!RolltheSpire2.Search.Selectivity.CapsuleRelicProbabilityEstimator.TryEstimateBonesNestedCapsules(
                RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(q),q.Evaluation.StructuredNeowEffects,false,
                [BaseGameModelKeys.Relics.SmallCapsule,BaseGameModelKeys.Relics.LargeCapsule],out double p,out _,out string issue) ||
                Math.Abs(p-sample.P)>1e-10) throw new Exception($"GroupedProbability:{sample.Targets.Length}:{pickup}:{p}!={sample.P}:{issue}");
        }
        var shared=WithSmallRelicLanes(Grouped([Key(1,0)],false));
        shared=shared with {Evaluation=shared.Evaluation with {RelicSequenceConditions=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.ExactSlot,1,new([], [Key(1,0)], []))]}};
        if(!RolltheSpire2.Search.Selectivity.CapsuleRelicProbabilityEstimator.TryEstimateBonesNestedCapsules(
            RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(shared),shared.Evaluation.StructuredNeowEffects,true,
            [BaseGameModelKeys.Relics.SmallCapsule,BaseGameModelKeys.Relics.LargeCapsule],out double sharedP,out _,out string sharedIssue) ||
            Math.Abs(sharedP-(1-Math.Pow(.5,3))/3)>1e-10) throw new Exception($"GroupedSharedBag:{sharedP}:{sharedIssue}");
        Console.WriteLine("GroupedCapsuleProbability=PASS;16 closed-form partial/full/mixed/duplicate/pickup/shared-lane cases");
        AddRfullFixtures(Grouped([Key(1, 0), Key(1, 1), Key(1, 2)], false),
            "BonesGroupedSameRarity", rfullFixtures);
        AddRfullFixtures(Grouped([Key(1, 0), Key(2, 0), Key(3, 0)], false),
            "BonesGroupedMixedRarity", rfullFixtures);
        AddRfullFixtures(Grouped([Key(1, 0), Key(2, 0), Key(3, 0)], true),
            "BonesGroupedMixedRarityOrdinary", rfullFixtures);
        CheckBonesComposite(Grouped([Key(1, 0), Key(2, 0), Key(3, 0)], false), fixtures);
        CheckBonesComposite(Grouped([Key(1, 0), Key(1, 1), Key(1, 2)], false), fixtures);
        foreach (ModelKey[] partial in new[] { new[]{Key(1, 0)}, new[]{Key(1, 0), Key(3, 0)} })
        {
            AddRfullFixtures(Grouped(partial, false), "BonesGroupedPartial" + partial.Length, rfullFixtures);
            CheckBonesComposite(Grouped(partial, false), fixtures);
        }
        AddRfullFixtures(Grouped([Key(1, 0), Key(1, 0)], false), "BonesGroupedDuplicateMultiplicity", rfullFixtures);

        foreach (bool full in new[] { false, true })
        foreach (int pickup in new[] { 0, 1, 2 })
        foreach (bool mixed in new[] { false, true })
        {
            var small = bonesSmall with { OutputKeys = [Key(1, 0)] };
            var large = bonesLarge with { OutputKeys = full
                ? [Key(mixed ? 2 : 1, mixed ? 0 : 1), Key(mixed ? 3 : 1, mixed ? 0 : 2)]
                : [Key(mixed ? 2 : 1, mixed ? 0 : 1)],
                Kind = NeowStructuredConditionKind.ExactUnorderedPair };
            var filter = BonesFilter(small, BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule) with
            {
                StructuredNeowEffects = [small, large],
                RequiredBonesAcquisitionOrder = pickup == 0 ? [] : pickup == 1
                    ? [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule]
                    : [BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.SmallCapsule]
            };
            AddRfullFixtures(Request(filter), $"BonesSourceConstrained.Full{full}.Pickup{pickup}.Mixed{mixed}", rfullFixtures);
            AddRfullFixtures(Request(filter with { StructuredNeowEffects = [large] }),
                $"BonesSourceConstrained.LargeOnly.Full{full}.Pickup{pickup}.Mixed{mixed}", rfullFixtures);
            if (!full) AddRfullFixtures(Request(filter with { StructuredNeowEffects = [small] }),
                $"BonesSourceConstrained.SmallOnly.Pickup{pickup}.Mixed{mixed}", rfullFixtures);
        }

        void AssertCpuFallback(ExactSearchExecutionRequest unsupported, string expectedIssue)
        {
            NeowReplayPlan unsupportedRoute = NeowReplayPlan.Compile(unsupported, true);
            if (RelicFullGpuPlan.TryCreate(unsupported, unsupportedRoute, out _, out string issue) ||
                !issue.StartsWith(expectedIssue, StringComparison.Ordinal) ||
                !CapsuleRelicFamily.TryCreate(unsupported, out IFamilyInvocation? family) || family is null ||
                family.ConditionPerformance.UsesGpu)
                throw new Exception("Unsupported Rfull shape did not retain CPU fallback: " + expectedIssue + ":" + issue);
        }
        NeowSearchFilter unfixed = BonesFilter(bonesSmall, BaseGameModelKeys.Relics.SmallCapsule, companion) with
            { RequiredBonesCombination = [] };
        AssertCpuFallback(Request(unfixed), "RfullFixedBonesPairRequired");
        NeowSearchFilter preConsumingOrder = BonesFilter(bonesSmall,
            BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.SmallCapsule) with
            { RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.SmallCapsule] };
        AssertCpuFallback(Request(preConsumingOrder), "RfullPreCapsuleRewardsContinuationUnsupported");
        NeowSearchFilter anyConsumingOrder = BonesFilter(bonesSmall,
            BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.SmallCapsule);
        AssertCpuFallback(Request(anyConsumingOrder), "RfullPreCapsuleRewardsContinuationUnsupported");
        AddRfullFixtures(Request(anyConsumingOrder with {
            RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.ArcaneScroll] }),
            "BonesCapsuleBeforeConsumingCompanion", rfullFixtures);
        var largeAfterSmall = new NeowStructuredEffectSearchCondition(
            BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [Key(1, 0), Key(1, 1)]);
        NeowSearchFilter precedingCapsule = BonesFilter(largeAfterSmall,
            BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule) with
            { RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule] };
        AddRfullFixtures(Request(precedingCapsule), "BonesSourceConstrained.PrecedingUnobservedSmall", rfullFixtures);
        ExactSearchExecutionRequest groupedOrdered = Grouped([Key(1, 0), Key(2, 0), Key(3, 0)], false) with
        {
            Evaluation = Grouped([Key(1, 0), Key(2, 0), Key(3, 0)], false).Evaluation with
            {
                RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule]
            }
        };
        AddRfullFixtures(groupedOrdered, "BonesGroupedPinnedSmallFirst", rfullFixtures);
        AddRfullFixtures(groupedOrdered with { Evaluation = groupedOrdered.Evaluation with {
            RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.SmallCapsule] } },
            "BonesGroupedPinnedLargeFirst", rfullFixtures);

        Console.WriteLine($"Rfull plan/parity fixtures: {rfullFixtures.Count}; direct, fixed Bones, grouped and sequence shapes: PASS");
    }

    private static void AddRfullFixtures(ExactSearchExecutionRequest request, string name, List<object> fixtures)
    {
        if (name.StartsWith("Bones", StringComparison.Ordinal)) request = WithSmallRelicLanes(request);
        NeowReplayPlan route = NeowReplayPlan.Compile(request, true);
        // This numerical fixture uses tiny synthetic lanes from the older test
        // authority. Real Beta111 admission is checked separately by Host corpus;
        // only the immutable applicability fact is supplied to this shader test.
        bool sourceFixture = name.StartsWith("BonesSourceConstrained", StringComparison.Ordinal);
        if (sourceFixture) route = route with { DirectNestedVanilla111 = true };
        if (route.Bones)
        {
            byte[] fixedPair = request.Evaluation.RequiredBonesCombination
                .Select(key => { Beta110FastRelicCatalog.TryGetId(key, out byte id); return id; }).ToArray();
            route = route with { Authority = route.Authority with { BonesEligibleRelicIds = fixedPair } };
        }
        if (!RelicFullGpuPlan.TryCreate(request, route, out RelicFullGpuPlan? plan, out string issue) || plan is null)
            throw new Exception($"Rfull supported fixture unavailable: {name}:{issue}");
        var selected = FamilyExecutionCoordinator.Plan(request);
        var r = FamilyExecutionCoordinator.CreateRegisteredFamilies(request).OfType<CapsuleRelicFamily>().SingleOrDefault();
        // Default greedy ordering may choose a coupled or private transport unit.
        // Registration still exposes the standalone numerical Rfull body.
        if (!sourceFixture && !name.StartsWith("CapsuleOnly", StringComparison.Ordinal) &&
            (r is null || !r.ConditionPerformance.UsesGpu ||
             r.ConditionPerformance.PhysicalImplementationRevision != plan.Revision(false) &&
             r.ConditionPerformance.PhysicalImplementationRevision != plan.Revision(true)))
            throw new Exception("Planner did not register the Rfull GPU physical: " + name);
        if (!selected.Stages.SelectMany(stage => stage.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
            throw new Exception("Default plan lost a Capsule query obligation:" + name);

        var reference = new CapsuleRelicReplay(request, route);
        uint[][] buffers = RelicFamilyGpuExecutor.PackRfullFixtureBuffers(plan);
        foreach (bool compact in new[] { false, true })
        {
            const int count = 4096;
            ulong start = compact ? 39303UL : 0UL;
            uint[] ordinals = Enumerable.Range(0, count).Where(index => !compact || index % 3 == 0)
                .Select(index => (uint)index).ToArray();
            uint[] expected = ordinals.Where(ordinal =>
            {
                ulong root = Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + ordinal));
                bool cpu = reference.Matches(root);
                return cpu;
            }).ToArray();
            fixtures.Add(new { name, compact, start, count, ordinals, expected, buffers });
        }
    }

    private static void CheckBonesComposite(ExactSearchExecutionRequest request, List<object> fixtures)
    {
        request = WithSmallRelicLanes(request);
        var n = NeowReplayPlan.Compile(request, false);
        if (!NeowCapsuleComposite.TryCreate(request, n, out var composite) || composite?.UsesBonesCheckpoint != true ||
            !NeowFamilyGpuPlan.TryCreate(n, out var fullGpu, out _) || fullGpu is null || !fullGpu.UsesStagedDense)
            throw new Exception("Bounded Bones NR staged admission unavailable");
        if (!FamilyExecutionCoordinator.Plan(request).Stages.SelectMany(stage => stage.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
            throw new Exception("Bounded Bones NR allocation unavailable");
        var withSequence = request with { Evaluation = request.Evaluation with {
            RelicSequenceConditions = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 1,
                new([], [request.Evaluation.StructuredNeowEffects.Single().OutputKeys[0]], []))] } };
        if (NeowCapsuleComposite.TryCreate(withSequence, n, out _)) throw new Exception("Bones NR claimed sequence Coverage");
        var ordered = request with { Evaluation = request.Evaluation with {
            RequiredBonesAcquisitionOrder = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule] } };
        if (NeowCapsuleComposite.TryCreate(ordered, n, out _)) throw new Exception("Bones NR admitted ordered grouped shape");

        // Exercise all target permutations and all source assignments, with both
        // offer layouts and independently forced CPU pickup orders. Small lanes
        // give positive witnesses without weakening the real-size staged pair.
        var condition = request.Evaluation.StructuredNeowEffects.Single();
        if (condition.OutputKeys.Count == 3 && composite.Metadata[6] != composite.Metadata[9])
            CheckBonesCompositeExactWitness(request, fixtures);
        int permutation = 0;
        int[][] orders = condition.OutputKeys.Count switch {
            1 => [new[]{0}], 2 => [new[]{0,1}, new[]{1,0}],
            _ => [new[]{0,1,2}, new[]{0,2,1}, new[]{1,0,2}, new[]{1,2,0}, new[]{2,0,1}, new[]{2,1,0}] };
        foreach (int[] order in orders)
        {
            var variant = request with { Evaluation = request.Evaluation with {
                StructuredNeowEffects = [condition with { OutputKeys = order.Select(i => condition.OutputKeys[i]).ToArray() }] } };
            NeowReplayPlan route = NeowReplayPlan.Compile(variant, true);
            if (!NeowCapsuleComposite.TryCreate(variant, n, out var nr) || nr is null) throw new Exception("Grouped permutation lost NR");
            // Tiny pair exercises fused Dense; real pool exercises resident stage 2.
            foreach (bool tiny in new[] { false, true })
            {
                NeowReplayPlan np = tiny ? n with { Authority = n.Authority with { BonesEligibleRelicIds = [3, 28] } } : n;
                NeowReplayPlan rp = tiny ? route with { Authority = route.Authority with { BonesEligibleRelicIds = [3, 28] } } : route;
                var forward = new CapsuleRelicReplay(variant, rp with { First = 28, Second = 3 });
                var reverse = new CapsuleRelicReplay(variant, rp with { First = 3, Second = 28 });
                if (!NeowFamilyGpuPlan.TryCreate(np, out var gpu, out _) || gpu is null) throw new Exception("Bones NR GPU packing failed");
                const int count = 65536;
                uint[] dense = Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
                ulong start = 0;
                uint[] expected = [];
                for (int attempt = 0; attempt < 128 && expected.Length == 0; attempt++)
                {
                    expected = dense.Where(i => {
                        ulong root = Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i));
                        if (!NeowFamilyReplay.Matches(root, np)) return false;
                        bool a = forward.Matches(root), b = reverse.Matches(root);
                        if (a != b) throw new Exception("Grouped output changed with Capsule pickup order");
                        return a;
                    }).ToArray();
                    if (expected.Length == 0) start += count;
                }
                if (expected.Length == 0) throw new Exception("Grouped NR fixture needs positives");
                if (tiny && permutation == 0 && condition.OutputKeys.Count == 3 && composite.Metadata[6] != composite.Metadata[9])
                {
                    var rarityOrders = new HashSet<string>();
                    var offerOrders = new HashSet<byte>();
                    foreach (uint ordinal in expected)
                    {
                        ulong root = Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + ordinal));
                        var observation = NeowFamilyReplay.Observe(root, np);
                        offerOrders.Add(observation.BonesFirst);
                        var rewards = observation.RewardsAfterBones;
                        int Rarity() { float roll = rewards.NextFloat(); return roll < .5f ? 1 : roll < .83f ? 2 : 3; }
                        rarityOrders.Add($"{Rarity()}{Rarity()}{Rarity()}");
                    }
                    if (rarityOrders.Count != 6 || offerOrders.Count != 2)
                        throw new Exception("Grouped NR witnesses missed a rarity/source assignment or reverse offer");
                }
                uint[] compact = dense.Where(i => i % 7 == 0).Concat(expected).Distinct().Order().ToArray();
                foreach (bool compactInput in new[] { false, true })
                    fixtures.Add(new { name = $"Nr.BonesGrouped.T{condition.OutputKeys.Count}.{condition.OutputKeys[^1].Entry}.P{permutation}.Tiny{tiny}",
                        bonesComposite = true, composite = nr.Metadata, compact = compactInput, start, count,
                        ordinals = compactInput ? compact : dense, expected, gpu });
            }
            permutation++;
        }

        // An already selected composite must fault, not return the original input,
        // when its GPU execution context is unavailable. No real device needed.
        var family = new NeowFamily(request, n, composite: composite);
        var context = new FamilyExecutionContext(requiresGpu: false);
        var batch = new SearchBatch(0, 1);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool faulted = false;
            try { family.InvokeAsync(context, new(batch, request), FamilyCandidateSet.Dense(batch), CancellationToken.None).AsTask().GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { faulted = true; }
            if (!faulted) throw new Exception("Selected Bones NR silently recovered after physical fault");
        }
        Console.WriteLine($"Bones NR: targets={condition.OutputKeys.Count};permutations={orders.Length};both pickup orders, staged/fused/compact, split applicability and fail-fast: PASS");
    }

    private static void CheckBonesCompositeExactWitness(ExactSearchExecutionRequest request, List<object> fixtures)
    {
        // Give Core Exact and R's World pool the same immutable shared/character
        // sources. Ordinary fixture names carry audited no-immediate-effect DTOs.
        var world = request.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        NeowEffectRelicSnapshot[] Relics(IEnumerable<Beta109RelicBucketSnapshot> buckets) => buckets
            .SelectMany(bucket => bucket.OrderedEntries.Select((entry, index) => new NeowEffectRelicSnapshot(
                entry.RelicKey, index, Enum.Parse<EffectRelicRarity>(bucket.BucketId.Split(':')[^1]),
                NestedRelicEffectKind.NoTrackedImmediateEffect, RarityCode: bucket.BucketId.Split(':')[^1],
                IsAllowedInShops: entry.IsAllowedInShops, ShopEligibilityExact: true))).ToArray();
        var effects = request.Authority.EffectAuthority! with {
            SharedRelicPoolSource = Relics(generation.SharedRelicBuckets),
            CharacterRelicPoolSource = Relics(generation.PlayerRelicBuckets),
            CapturedProfileId = RuntimeProfileId.Beta111
        };
        var players = generation.PlayerRelicBuckets.Select(bucket => {
            var shared = generation.SharedRelicBuckets.Single(item => item.BucketId.Split(':')[^1] == bucket.BucketId.Split(':')[^1]);
            var entries = shared.OrderedEntries.Concat(bucket.OrderedEntries).DistinctBy(entry => entry.RelicKey).ToArray();
            return bucket with { OrderedEntries = entries, OrderedRelics = entries.Select(item => item.RelicKey).ToArray() };
        }).ToArray();
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(request.Authority.Character.CharacterKey,
            request.Authority.Ascension, 0, 1).WithEffectAuthority(effects).WithWorldAuthority(
                world with { CapturedProfileId = RuntimeProfileId.Beta111,
                    Beta109Generation = generation with { Profile = RuntimeProfileId.Beta111, PlayerRelicBuckets = players } });
        var context = request.CompiledSearch.Context with { Authority = authority, ProfileId = RuntimeProfileId.Beta111,
            Detection = new GameVersionDetection(RuntimeVersionCompatibility.Beta111Version, RuntimeVersionCompatibility.Beta111Version,
                "beta", "bones-nr-exact-fixture", true, string.Empty) };
        request = request with { CompiledSearch = request.CompiledSearch with { Context = context } };
        var n = NeowReplayPlan.Compile(request, false);
        for (ulong ordinal = 0; ordinal < 100_000; ordinal++)
        {
            string seed = Beta110SeedCodec.FormatOrdinal(ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            if (!NeowFamilyReplay.Matches(root, n)) continue;
            if (!SearchAuthorityProjector.TryProjectForRootHash(request, root, seed, out var projected, out _, out string issue))
                throw new Exception("Bones Exact authority projection failed: " + issue);
            var outcome = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.NeowsBones,
                PredictionPrecision.Exact, projected, root).BonesOutcome!;
            ModelKey[] targets = outcome.OriginalRoutes[0].RelicScopedResults.SelectMany(result => result.EffectGroups)
                .SelectMany(group => group.OrderedItems).Where(item => item.Kind == PredictedEffectKind.AddRelic &&
                    item.Relation == PredictedEffectRelation.NestedRelic && item.TargetKey.HasValue)
                .Select(item => item.TargetKey!.Value).ToArray();
            if (targets.Length != 3) throw new Exception("Core grouped witness did not observe three relics");
            foreach (var other in outcome.OriginalRoutes.Skip(1))
            {
                var combined = other.RelicScopedResults.SelectMany(result => result.EffectGroups)
                    .SelectMany(group => group.OrderedItems).Where(item => item.Kind == PredictedEffectKind.AddRelic &&
                        item.Relation == PredictedEffectRelation.NestedRelic && item.TargetKey.HasValue)
                    .Select(item => item.TargetKey!.Value.Serialized).Order().ToArray();
                if (!combined.SequenceEqual(targets.Select(key => key.Serialized).Order()))
                    throw new Exception("Core grouped outputs changed with pickup order");
            }
            if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, root, seed, out var input, out issue))
                throw new Exception(issue);
            foreach (int targetCount in new[]{1, 2, 3, 0})
            {
                ModelKey[] required = targetCount == 0 ? [targets[0], targets[0]] : targets.Take(targetCount).ToArray();
                bool shouldMatch = targetCount != 0;
                var filter = NeowSearchFilter.Empty with {
                    NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                    RequiredBonesCombination = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule],
                    StructuredNeowEffects = [request.Evaluation.StructuredNeowEffects.Single() with { OutputKeys = required }]
                };
                var query = CompileFixtureRequest(filter, context, request.RunOptions);
                var exact = ProductionExactSearchEvaluator.Evaluate(query, input);
                if (exact.IsMatch != shouldMatch) throw new Exception("Grouped Production Exact witness failed: " + exact.FailureCode);
                var np = NeowReplayPlan.Compile(query, false);
                if (!NeowCapsuleComposite.TryCreate(query, np, out var nr) || nr is null)
                {
                    RelicFullGpuPlan.TryCreate(query, NeowReplayPlan.Compile(query, true), out _, out string admissionIssue);
                    throw new Exception("Production Exact witness NR admission: " + admissionIssue);
                }
                if (nr.Reference.Matches(root) != shouldMatch)
                    throw new Exception("Production Exact witness disagrees with R reference: " + string.Join(',', required));
                if (!NeowFamilyGpuPlan.TryCreate(np, out var gpu, out string gpuIssue) || gpu is null)
                    throw new Exception("Production Exact witness N GPU admission: " + gpuIssue);
                fixtures.Add(new { name = "Nr.BonesGrouped.ProductionExactWitness.Targets" + targetCount, bonesComposite = true, composite = nr.Metadata,
                    compact = false, start = ordinal, count = 1, ordinals = new uint[]{0}, expected = shouldMatch ? new uint[]{0} : [], gpu });
                Console.WriteLine($"Bones NR Production Exact witness: seed={seed};targets={targetCount};match={shouldMatch};PASS");
            }
            return;
        }
        throw new Exception("No bounded Bones Exact witness found");
    }

    private static ExactSearchExecutionRequest WithSmallRelicLanes(ExactSearchExecutionRequest request)
    {
        WorldAuthoritySnapshot world = request.Authority.WorldAuthority ?? throw new Exception("Rfull fixture World authority missing");
        Beta109WorldGenerationSnapshot generation = world.Beta109Generation ?? throw new Exception("Rfull fixture generation missing");
        Beta109RelicBucketSnapshot[] buckets = generation.PlayerRelicBuckets.Select(bucket =>
        {
            if (!bucket.BucketId.EndsWith(":Common", StringComparison.OrdinalIgnoreCase) &&
                !bucket.BucketId.EndsWith(":Uncommon", StringComparison.OrdinalIgnoreCase) &&
                !bucket.BucketId.EndsWith(":Rare", StringComparison.OrdinalIgnoreCase)) return bucket;
            Beta109RelicBucketEntrySnapshot[] entries = bucket.OrderedEntries.Take(3).ToArray();
            return bucket with { OrderedEntries = entries, OrderedRelics = entries.Select(entry => entry.RelicKey).ToArray() };
        }).ToArray();
        WorldAuthoritySnapshot compactWorld = world with { Beta109Generation = generation with {
            PlayerRelicBuckets = buckets, SnapshotFingerprint = generation.SnapshotFingerprint + ":rfull-small-lanes" } };
        var context = request.CompiledSearch.Context with { Authority = request.Authority.WithWorldAuthority(compactWorld) };
        return request with { CompiledSearch = request.CompiledSearch with { Context = context } };
    }

    private static void CheckCpuFamily(ExactSearchExecutionRequest source)
    {
        var request = source;
        var plan = NeowReplayPlan.Compile(request, false);
        var family = new NeowFamily(request, plan, preferGpu: false);
        var context = new FamilyExecutionContext(requiresGpu: false);
        var batch = new SearchBatch(100, 256);
        var output = LinearFamilyPipeline.FilterAsync(context, new(batch, request), [family], CancellationToken.None).AsTask().GetAwaiter().GetResult();
        ulong[] expected = Enumerable.Range(0, 256).Select(i => (ulong)i).Where(i => NeowFamilyReplay.Matches(
            Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(100 + i)), plan)).ToArray();
        if (expected.Length == 0 || !output.ExportAbi1().Span.SequenceEqual(expected)) throw new Exception("CPU Production Family pipeline parity failed");
        family.DisposeAsync(context).AsTask().GetAwaiter().GetResult(); context.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"CPU real N invocation through linear Family pipeline: survivors={expected.Length};PASS");
    }

    private static void CheckCpuAbi(ExactSearchExecutionRequest source)
    {
        var request = source;
        var cpu = new FamilyCpuExecution(request, "N.fixture", "fixture");
        var batch = new SearchBatch(100, 16);
        var window = new FamilyObservationWindow(batch, request);
        var input = FamilyCandidateSet.FromSortedAbi1(batch, [1, 5, 15]);
        ulong acceptedRoot = Beta110Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(105));
        var output = cpu.Execute(window, input, CancellationToken.None, root => root == acceptedRoot);
        if (!output.ExportAbi1().Span.SequenceEqual(new ulong[] { 5 })) throw new Exception("CPU changed batch-local ordinal identity");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { cpu.Execute(window, input, cancellation.Token, _ => throw new Exception("Cancelled work ran")); }
        catch (OperationCanceledException) { Console.WriteLine("CPU ABI1 reconstruction and pre-cancel passed"); return; }
        throw new Exception("CPU ignored cancellation");
    }

    private static void CheckCpuChunks(ExactSearchExecutionRequest source)
    {
        var request = source;
        var batch = new SearchBatch(39303, FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize);
        var window = new FamilyObservationWindow(batch, request);
        ulong[] ordinals = Enumerable.Range(0, 2 * FamilyCpuExecution.Capacity + 3).Select(i => (ulong)(i * 17 + 1)).ToArray();
        var input = FamilyCandidateSet.FromSortedAbi1(batch, ordinals);
        var cpu = new FamilyCpuExecution(request, "R.chunkFixture", "fixture");
        int seen = 0;
        var output = cpu.Execute(window, input, CancellationToken.None, root => (++seen % 3) == 0);
        if (output.Batch != batch || !output.ExportAbi1().Span.SequenceEqual(ordinals.Where((_, i) => (i+1)%3 == 0).ToArray()))
            throw new Exception("CPU chunk concatenation changed ABI1 identity/order");
        using var cancellation = new CancellationTokenSource();
        seen = 0; bool cancelled = false;
        try { cpu.Execute(window, input, cancellation.Token, _ => { if (++seen == FamilyCpuExecution.Capacity + 1) cancellation.Cancel(); return true; }); }
        catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled || seen != FamilyCpuExecution.Capacity + 1) throw new Exception("CPU chunk cancellation published or continued partial work");
        seen = 0;
        bool faulted = false;
        try { cpu.Execute(window, input, CancellationToken.None, _ => ++seen > FamilyCpuExecution.Capacity ? throw new InvalidOperationException("InjectedChunkFault") : true); }
        catch (FamilyCpuMatchException ex) when (ex.InnerException is InvalidOperationException { Message: "InjectedChunkFault" })
        {
            faulted = ex.FamilyId == "R.chunkFixture" && ex.Batch == batch &&
                ex.LogicalOrdinal == ordinals[FamilyCpuExecution.Capacity] &&
                ex.GlobalOrdinal == batch.GlobalCandidate(ex.LogicalOrdinal);
        }
        if (!faulted) throw new Exception("CPU chunk fault returned a population");
        faulted = false;
        try { cpu.Execute(window, input, CancellationToken.None, _ => true); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("FamilyCpuInvocationFaulted:", StringComparison.Ordinal)) { faulted = true; }
        if (!faulted) throw new Exception("Faulted CPU invocation was reused");
        Console.WriteLine("CPU multi-chunk sparse ABI1, mid-invocation cancellation and selected fault rejection: PASS");
    }

    private static void CheckPlanner()
    {
        foreach (bool nFirst in new[] { false, true })
        {
            var n = new ConditionalFixture("N.fixture", "R.fixture", .1, .25, nFirst ? 1 : 5, nFirst ? 100 : 1);
            var r = new ConditionalFixture("R.fixture", "N.fixture", .2, .5, nFirst ? 5 : 2, nFirst ? 1 : 50);
            var plan = FamilyPlanner.Plan([n, r]);
            string expected = nFirst ? n.FamilyId : r.FamilyId;
            if (plan.Stages[0].Family.FamilyId != expected || plan.RankingAuthority != FamilyPlannerRankingAuthority.GreedyUnitEliminationCost)
                throw new Exception("Planner ignored conditional Expected Filtering Cost");
            if (Math.Abs(plan.Stages[1].ExpectedInputFraction * plan.Stages[1].Survival!.Value - .05) > 1e-12)
                throw new Exception("Planner/ETA stage population ignored conditional Survival");
        }
        Console.WriteLine("Conditional Planner: both order choices and joint population passed");
    }

    private sealed class ConditionalFixture(string id, string other, double marginal, double conditional, double ownCost, double conditionalCost) : IFamilyInvocation
    {
        public string FamilyId => id;
        public bool HasConditionalProjections => true;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(id, 8192, [], "fixture");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Resolved(id, marginal, "fixture");
        public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passed) => FamilySurvivalProjection.Resolved(id, passed.Contains(other) ? conditional : marginal, "fixture");
        public FamilyExpectedFilteringCostProjection ExpectedFilteringCost => ResolveExpectedFilteringCost(new HashSet<string>());
        public FamilyExpectedFilteringCostProjection ResolveExpectedFilteringCost(IReadOnlySet<string> passed) =>
            new(id, 200, passed.Contains(other) ? conditionalCost : ownCost, ResolveSurvival(passed).SurvivalProbability, 1, [], "fixture");
        public FamilyConditionPerformanceProjection ConditionPerformance => new(id, "fixture.unmeasured", "fixture", 1, "fixture", usesGpu: false);
        public FamilyPerformanceObservation CapturePerformanceObservation() => throw new NotSupportedException();
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window, FamilyCandidateSet input, CancellationToken token) => throw new NotSupportedException();
    }

    private static void CheckCapsuleOracle(ExactSearchExecutionRequest source, int count)
    {
        var world = source.Authority.WorldAuthority!;
        var preview = RelicSequencePredictor.PredictFromRootHash(Beta110Profile.Instance, 0, "fixture", world, 10);
        ModelKey target = preview.Lanes.First(l => l.Kind == RelicSequenceKind.Common).Entries[0].RelicKey;
        foreach (bool bones in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        {
            ModelKey a = reverse ? BaseGameModelKeys.Relics.LargeCapsule : BaseGameModelKeys.Relics.SmallCapsule;
            ModelKey b = reverse ? BaseGameModelKeys.Relics.SmallCapsule : BaseGameModelKeys.Relics.LargeCapsule;
            var filter = NeowSearchFilter.Empty with {
                NeowRoute = new(bones ? BaseGameModelKeys.Relics.NeowsBones : a),
                RequiredBonesAcquisitionOrder = bones ? new[] { a, b } : [],
                StructuredNeowEffects = [new(a, reverse ? NeowStructuredConditionKind.ExactUnorderedPair : NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [target])],
                RelicSequenceConditions = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, new([], [target], []))]
            };
            var context = source.CompiledSearch.Context;
            if (bones) context = context with { Authority = source.Authority.WithEffectAuthority(source.Authority.EffectAuthority! with {
                BonesEligibleRelics = new[] { a, b } }) };
            var request = CompileFixtureRequest(filter, context, source.RunOptions);
            var plan = NeowReplayPlan.Compile(request, true);
            var replay = new CapsuleRelicReplay(request, plan);
            int positives = 0;
            for (int i = 0; i < count; i++)
            {
                ulong root = XxHash64.HashUtf8(Beta110SeedCodec.FormatOrdinal((ulong)i), 0);
                var observation = NeowFamilyReplay.Observe(root, plan);
                bool expected = false;
                if (observation.IdentityPass)
                {
                    var predicted = RelicSequencePredictor.PredictFromRootHash(Beta110Profile.Instance, root, "fixture", world, 10);
                    var lanes = new[] { RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare }
                        .Select(k => predicted.Lanes.First(l => l.Kind == k).Entries.Select(e => e.RelicKey).ToArray()).ToArray();
                    var rng = bones ? observation.RewardsAfterBones : new Beta110FastRng(unchecked(root + (ulong)plan.Authority.PlayerSlotIndex + NeowFamilyReplay.RewardsHash));
                    // The predicate targets the first acquired Capsule only;
                    // the second still consumes the same R-owned bag afterward.
                    int pulls = reverse ? 2 : 1;
                    int[] consumed = new int[3];
                    var outputs = new List<ModelKey>();
                    for (int pull = 0; pull < pulls; pull++)
                    {
                        float roll = rng.NextFloat();
                        int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
                        while (lane < 3 && consumed[lane] == lanes[lane].Length) lane++;
                        outputs.Add(lane < 3 ? lanes[lane][consumed[lane]++] : plan.Authority.EffectCatalog.KeyOf(plan.Authority.EffectCatalog.CapsuleCircletId));
                    }
                    expected = lanes[0].Take(2).Contains(target) && outputs.Contains(target);
                }
                bool actual = replay.Matches(root);
                if (actual != expected) throw new Exception($"R Capsule/core sequence mismatch: bones={bones};reverse={reverse};seed={i}");
                if (actual) positives++;
            }
            if (positives == 0) throw new Exception("R oracle fixture did not exercise positive rejection boundary");
            var nPlan = NeowReplayPlan.Compile(request, false);
            var nModel = new NeowFamilyProjections(request, nPlan, false, null);
            var rModel = new NeowFamilyProjections(request, plan, true, replay.Pool);
            var empty = new HashSet<string>();
            double? n = nModel.Survival(empty).SurvivalProbability, r = rModel.Survival(empty).SurvivalProbability;
            double? nr = rModel.Survival(new HashSet<string> { "N.Neow" }).SurvivalProbability;
            double? rn = nModel.Survival(new HashSet<string> { "R.Relic" }).SurvivalProbability;
            if (!n.HasValue || !r.HasValue || !nr.HasValue || !rn.HasValue) throw new Exception("Supported conditional fixture became unresolved");
            if (Math.Abs(n.Value * nr.Value - r.Value * rn.Value) > 1e-12)
                throw new Exception("Conditional survival changes with Family order");
            var nCost = nModel.Expected(empty); var rCost = rModel.Expected(new HashSet<string> { "N.Neow" });
            foreach (var cost in new[] { nCost, rCost })
                if (!cost.IsResolved || cost.ExpectedFilteringWorkUnitsPerInput < 0 || cost.ExpectedFilteringWorkUnitsPerInput > cost.FullFilteringWorkUnitsPerInput + 1e-10)
                    throw new Exception("Expected Cost is outside its full path envelope");
            Console.WriteLine($"R core oracle: bones={bones};reverse={reverse};positives={positives};N={n};R={r};R|N={nr};N|R={rn};Ncost={nCost.ExpectedFilteringWorkUnitsPerInput};Rcost|N={rCost.ExpectedFilteringWorkUnitsPerInput}");
        }
    }
}
