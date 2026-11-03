using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static partial class FamilyNeowChecks
{
    // This entry only prepares fixtures. RfullGpuSanity.gd executes the exported
    // selected bodies; expected values always come from the original CPU replay.
    internal static int RunRelicOpeningPrefix(PreparedSearch source, int requestedCount)
    {
        string? saved = Environment.GetEnvironmentVariable("RT2_R_OPENING_PREFIX");
        var fixtureGroups = new Dictionary<string, List<object>>();
        var shaderShapes = new Dictionary<string, (bool Generic, bool Prefix, int BagCapacity, int StateCapacity)>();
        int count = Math.Clamp(requestedCount, 512, 4096), checks = 0, positiveChecks = 0;
        int newTracked = 0, newGeneric = 0;
        try
        {
            var context = OpeningPrefixContext(WithSmallRelicLanes(source));
            ModelKey small = BaseGameModelKeys.Relics.SmallCapsule, large = BaseGameModelKeys.Relics.LargeCapsule;
            ModelKey Key(int lane, int position = 0)
            {
                var seedRequest = CompileFixtureRequest(NeowSearchFilter.Empty, context, source.RunOptions);
                if (!RelicFamilyPlanCompiler.TryCompilePool(seedRequest.Model, out var pool, out var dense, out _, out string issue))
                    throw new Exception("OpeningPrefixFixturePool:" + issue);
                int bucket = Enumerable.Range(0, pool.BucketCount).Single(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] == lane);
                ushort id = pool.DenseRelicIds[pool.BucketOffsets[bucket] + position];
                return dense.Single(pair => pair.Value == id).Key;
            }
            NeowStructuredEffectSearchCondition Condition(ModelKey capsule, params ModelKey[] targets) => new(capsule,
                capsule == small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, targets);
            PreparedSearch Query(ModelKey[] pair, ModelKey[] order,
                NeowStructuredEffectSearchCondition[] conditions, bool sequence = false, bool shop = false,
                SearchContext? explicitContext = null, bool fixedPair = true)
            {
                var ctx = explicitContext ?? context;
                // Two eligible identities produce both offer layouts without
                // changing acquisition policy. They are real authority inputs,
                // not edits to a compiled route or shader metadata.
                ctx = ctx with { Authority = ctx.Authority.WithEffectAuthority(ctx.Authority.EffectAuthority! with {
                    BonesEligibleRelics = pair }) };
                return CompileFixtureRequest(NeowSearchFilter.Empty with {
                    NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                    RequiredBonesCombination = fixedPair ? pair : [], RequiredBonesAcquisitionOrder = order,
                    StructuredNeowEffects = conditions,
                    RelicSequenceConditions = sequence ? [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2,
                        new([], [Key(1)], []))] : [],
                    RelicShopSequenceConditions = shop ? [new(2, CombatRewardSequenceOrderMode.Unordered, [Key(4), null])] : []
                }, ctx, source.RunOptions);
            }
            RelicFullGpuPlan? Plan(PreparedSearch query, string mode, out string issue)
            {
                Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", mode);
                return RelicFullGpuPlan.TryCreate(query.Model, NeowReplayPlan.Compile(query.Model, true), out var plan, out issue) ? plan : null;
            }
            string Group(RelicFullGpuPlan body)
            {
                int bag = body.GenericReplay ? body.ConfiguredGenericBagCapacity : 0;
                string key = (body.GenericReplay ? "generic" : "tracked") +
                    (body.UsesOpeningPrefix ? "-prefix" : "-legacy") + ".B" + bag + ".S" + body.SequencePlan.LocalStateCapacity;
                shaderShapes[key] = (body.GenericReplay, body.UsesOpeningPrefix, bag, body.SequencePlan.LocalStateCapacity);
                return key;
            }
            void Add(string name, PreparedSearch query, bool expectNew)
            {
                var off = Plan(query, "off", out string offIssue);
                var on = Plan(query, "on", out string onIssue) ?? throw new Exception("OpeningPrefixOnUnavailable:" + name + ":" + onIssue);
                if (expectNew && !on.UsesOpeningPrefix) throw new Exception("OpeningPrefixBodyNotSelected:" + name);
                if (off?.UsesOpeningPrefix == true) throw new Exception("OpeningPrefixOffChanged:" + name);
                if (on.UsesOpeningPrefix) { if (on.GenericReplay) newGeneric++; else newTracked++; }
                var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(query).SingleOrDefault(body => body.FamilyId == "R.Relic");
                if (registered is null || !registered.ConditionPerformance.UsesGpu ||
                    registered.ConditionPerformance.PhysicalImplementationRevision != on.Revision(false) &&
                    registered.ConditionPerformance.PhysicalImplementationRevision != on.Revision(true))
                    throw new Exception("OpeningPrefixProductionRegistration:" + name);
                if (!FamilyExecutionCoordinator.Plan(query).Stages.SelectMany(s => s.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
                    throw new Exception("OpeningPrefixCoverage:" + name);
                var route = NeowReplayPlan.Compile(query.Model, true);
                var original = new CapsuleRelicReplay(query.Model, route);
                int positives = 0;
                foreach (bool compact in new[] { false, true })
                {
                    ulong start = compact ? 39303UL : 0UL;
                    uint[] ordinals = Enumerable.Range(0, count).Where(i => !compact || i % 3 == 0).Select(i => (uint)i).ToArray();
                    uint[] expected = ordinals.Where(i => original.Matches(Beta111Profile.Instance.ComputeRootSeed(
                        Beta110SeedCodec.FormatOrdinal(start + i)))).ToArray();
                    positives += expected.Length; checks += ordinals.Length;
                    foreach (var body in new[] { (Name: "on", Plan: on), (Name: "off", Plan: off) })
                    {
                        if (body.Plan is null) continue;
                        string group = Group(body.Plan);
                        if (!fixtureGroups.TryGetValue(group, out var fixtures)) fixtureGroups[group] = fixtures = [];
                        fixtures.Add(new { name = name + "." + body.Name, compact, start, count, ordinals, expected,
                            buffers = RelicFamilyGpuExecutor.PackRfullFixtureBuffers(body.Plan) });
                    }
                }
                if (positives == 0) throw new Exception("OpeningPrefixFixtureHasNoPositive:" + name);
                positiveChecks += positives;
                Console.WriteLine($"R_OPENING_PREFIX_FIXTURE name={name};on={on.Revision(false)};off={off?.Revision(false) ?? offIssue};positive={positives}");
            }

            // Fixed consumption is proved by each predecessor's own pool rules.
            // Include pinned predecessor first, Capsule first and Any policy;
            // ordinary/shop observe the initial bag, before Capsule consumption.
            foreach (ModelKey predecessor in new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.ArcaneScroll,
                         BaseGameModelKeys.Relics.HeftyTablet, BaseGameModelKeys.Relics.LeadPaperweight, BaseGameModelKeys.Relics.LostCoffer,
                         BaseGameModelKeys.Relics.ScrollBoxes })
            {
                Add(predecessor.Entry + ".BeforeLarge.OrdinaryShop", Query([predecessor, large], [predecessor, large],
                    [Condition(large, Key(1))], sequence: true, shop: true), expectNew: true);
            }
            foreach (ModelKey predecessor in new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.ArcaneScroll })
            {
                Add(predecessor.Entry + ".AnySmall", Query([predecessor, small], [], [Condition(small, Key(1))]), true);
                Add(predecessor.Entry + ".CapsuleFirst", Query([predecessor, large], [large, predecessor], [Condition(large, Key(1))]), false);
            }
            // Source partition and grouped multiplicity remain the established
            // tracked algorithm. An unobserved source still consumes its draws.
            Add("SmallLarge.LargeOnly", Query([small, large], [small, large], [Condition(large, Key(1))]), false);
            Add("SmallLarge.SourcePartitions", Query([small, large], [], [Condition(small, Key(1)), Condition(large, Key(2))]), false);
            Add("SmallLarge.Grouped", Query([small, large], [], [new(BaseGameModelKeys.Relics.NeowsBones,
                NeowStructuredConditionKind.ExactGroupedCapsuleMultiset, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [Key(1), Key(2)], AllowDuplicateOutputs: true)]), false);
            Add("UnfixedPair.ArcaneSmall", Query([BaseGameModelKeys.Relics.ArcaneScroll, small], [],
                [Condition(small, Key(1))], fixedPair: false), true);
            var weakContext = context with { Authority = context.Authority.WithEffectAuthority(context.Authority.EffectAuthority! with {
                ColorlessRewardPool = null, ColorlessRewardPoolExact = false, OtherCharacterPools = null,
                OtherCharacterPoolsExact = false, PotionPool = null, PotionPoolExact = false }) };
            Add("Arcane.UnrelatedPoolsAbsent", Query([BaseGameModelKeys.Relics.ArcaneScroll, large],
                [BaseGameModelKeys.Relics.ArcaneScroll, large], [Condition(large, Key(1))], explicitContext: weakContext), true);
            Add("UnfixedArcane.UnrelatedPoolsAbsent", Query([BaseGameModelKeys.Relics.ArcaneScroll, small], [],
                [Condition(small, Key(1))], explicitContext: weakContext, fixedPair: false), true);

            var defectContext = OpeningPrefixContext(WithSmallRelicLanes(source), BaseGameModelKeys.Characters.Defect);
            var defectQuery = Query([BaseGameModelKeys.Relics.ScrollBoxes, large], [BaseGameModelKeys.Relics.ScrollBoxes, large],
                [Condition(large, Key(1))], explicitContext: defectContext);
            Add("DefectScroll.ResultDependent", defectQuery, true);
            var defectRoute = NeowReplayPlan.Compile(defectQuery.Model, true);
            if (!defectRoute.Authority.UsesDefectScrollBoxesRule) throw new Exception("OpeningPrefixDefectRuleNotActive");
            // Explicitly retain a special-branch input in the native fixtures;
            // merely naming Defect does not prove this rare branch was observed.
            bool branchSeen = false;
            for (ulong ordinal = 0; ordinal < 4096 && !branchSeen; ordinal++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                var observation = NeowFamilyReplay.Observe(root, defectRoute);
                if (!observation.IdentityPass) continue;
                var rewards = observation.RewardsAfterBones;
                ushort[] cards = new ushort[6]; byte[] sizes = new byte[2], claws = new byte[2];
                if (!NeowLocalOperators.GenerateScrollBoxes(defectRoute.Authority.EffectCatalog, true, ref rewards, cards, sizes, claws))
                    throw new Exception("OpeningPrefixDefectOriginalDonorFailed");
                if (!claws.Any(c => c != 0)) continue;
                bool expected = new CapsuleRelicReplay(defectQuery.Model, defectRoute).Matches(root);
                var plan = Plan(defectQuery, "on", out string issue) ?? throw new Exception(issue);
                foreach (bool compact in new[] { false, true }) fixtureGroups[Group(plan)].Add(new {
                    name = "DefectScroll.ObservedSpecialBranch", compact, start = ordinal, count = 1,
                    ordinals = new uint[] { 0 }, expected = expected ? new uint[] { 0 } : [],
                    buffers = RelicFamilyGpuExecutor.PackRfullFixtureBuffers(plan) });
                branchSeen = true; checks++;
                Console.WriteLine($"R_OPENING_PREFIX_SCROLL_SPECIAL seed={Beta110SeedCodec.FormatOrdinal(ordinal)};kept={expected};PASS");
            }
            if (!branchSeen) throw new Exception("OpeningPrefixDefectSpecialBranchMissing");

            // Core Exact independently creates positive observations in both
            // offer layouts, with fixed acquisition order different from offers.
            foreach (ModelKey predecessor in new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.ArcaneScroll })
            {
                var identity = Query([predecessor, large], [predecessor, large], []);
                var n = NeowReplayPlan.Compile(identity.Model, false);
                var offers = new HashSet<byte>();
                int crossWitnesses = 0;
                for (ulong ordinal = 0; ordinal < 4096 && (offers.Count < 2 || crossWitnesses == 0); ordinal++)
                {
                    string seed = Beta110SeedCodec.FormatOrdinal(ordinal);
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                    var observation = NeowFamilyReplay.Observe(root, n);
                    if (!observation.IdentityPass || offers.Contains(observation.BonesFirst) && crossWitnesses > 0) continue;
                    if (!SearchAuthorityProjector.TryProjectForRootHash(identity.Model, root, seed, out var authority, out _, out string issue))
                        throw new Exception("OpeningPrefixCoreProjection:" + issue);
                    var outcome = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.NeowsBones,
                        PredictionPrecision.Exact, authority, root).BonesOutcome!;
                    var picked = outcome.OriginalRoutes.Single(r => r.RelicScopedResults[0].SourceRelicKey == predecessor);
                    ModelKey[] Outputs(IEnumerable<PredictedEffectGroup> groups, PredictedEffectKind kind) => groups
                        .SelectMany(g => g.OrderedItems).Where(i => i.Kind == kind && i.TargetKey.HasValue &&
                            (kind != PredictedEffectKind.AddRelic || i.Relation == PredictedEffectRelation.NestedRelic))
                        .Select(i => i.TargetKey!.Value).ToArray();
                    ModelKey[] relics = picked.RelicScopedResults.Single(r => r.SourceRelicKey == large).EffectGroups
                        .SelectMany(g => g.OrderedItems).Where(i => i.Kind == PredictedEffectKind.AddRelic &&
                            i.Relation == PredictedEffectRelation.NestedRelic && i.TargetKey.HasValue).Select(i => i.TargetKey!.Value).ToArray();
                    var query = Query([predecessor, large], [predecessor, large], [Condition(large, relics)]);
                    if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, root, seed, out var input, out issue)) throw new Exception(issue);
                    if (!ProductionExactSearchEvaluator.Evaluate(query.Model, input).IsMatch ||
                        !new CapsuleRelicReplay(query.Model, NeowReplayPlan.Compile(query.Model, true)).Matches(root))
                        throw new Exception("OpeningPrefixCorePositive:" + predecessor + ":" + seed);
                    var other = outcome.OriginalRoutes.Single(r => r.RelicScopedResults[0].SourceRelicKey == large);
                    ModelKey[] pickedCards = Outputs(picked.RelicScopedResults.Single(r => r.SourceRelicKey == predecessor).EffectGroups,
                        PredictedEffectKind.AddCard);
                    ModelKey[] otherCards = Outputs(other.RelicScopedResults.Single(r => r.SourceRelicKey == predecessor).EffectGroups,
                        PredictedEffectKind.AddCard);
                    ModelKey[] otherRelics = Outputs(other.RelicScopedResults.Single(r => r.SourceRelicKey == large).EffectGroups,
                        PredictedEffectKind.AddRelic);
                    ModelKey[] uniqueCards = pickedCards.Except(otherCards).ToArray();
                    if (crossWitnesses == 0 && uniqueCards.Length > 0 && !relics.ToHashSet().SetEquals(otherRelics))
                    {
                        var cardCondition = new NeowStructuredEffectSearchCondition(predecessor,
                            predecessor == BaseGameModelKeys.Relics.Kaleidoscope ? NeowStructuredConditionKind.IndependentOfferGroupTargets
                                : NeowStructuredConditionKind.ExactSingle,
                            predecessor == BaseGameModelKeys.Relics.Kaleidoscope ? NeowStructuredEffectScope.SelectableOfferGroups
                                : NeowStructuredEffectScope.ProductRelevantEffects,
                            NeowStructuredOutputKind.Card, [uniqueCards[0]]);
                        var cross = Query([predecessor, large], [], [Condition(large, otherRelics), cardCondition]);
                        var cn = NeowReplayPlan.Compile(cross.Model, false);
                        var cr = NeowReplayPlan.Compile(cross.Model, true);
                        Beta110FastRelicCatalog.TryGetId(predecessor, out byte predecessorId);
                        Beta110FastRelicCatalog.TryGetId(large, out byte largeId);
                        bool na = NeowFamilyReplay.Matches(root, cn with { First = predecessorId, Second = largeId });
                        bool nb = NeowFamilyReplay.Matches(root, cn with { First = largeId, Second = predecessorId });
                        bool ra = new CapsuleRelicReplay(cross.Model, cr with { First = predecessorId, Second = largeId }).Matches(root);
                        bool rb = new CapsuleRelicReplay(cross.Model, cr with { First = largeId, Second = predecessorId }).Matches(root);
                        if (!na || nb || ra || !rb || ProductionExactSearchEvaluator.Evaluate(cross.Model, input).IsMatch)
                            throw new Exception("OpeningPrefixCrossRouteCoreWitness:" + predecessor + ":" + seed);
                        foreach (string mode in new[] { "off", "on" })
                        {
                            Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", mode);
                            if (predecessor == BaseGameModelKeys.Relics.Kaleidoscope)
                            {
                                string expectedRevision = BonesCapsuleAllocation.LocalRevision;
                                int hiddenMode = BonesHiddenUpgradeContinuation.CaptureMode();
                                if (hiddenMode != 0)
                                {
                                    if (!RelicFullGpuPlan.TryCreateBonesKBoundary(cross.Model, cr, out var boundary, out string boundaryIssue) || boundary is null)
                                        throw new Exception("OpeningPrefixKBoundaryPlan:" + boundaryIssue);
                                    var hidden = BonesHiddenUpgradeContinuation.Compile(cross.Model, boundary, hiddenMode);
                                    expectedRevision += BonesHiddenUpgradeContinuation.Revision + "." + hidden.ModeName;
                                    if (hidden.KnownFull) expectedRevision += BonesHiddenUpgradeContinuation.KnownFullGpuRevision;
                                }
                                if (cn.CapsuleNicheContinuation is not null) expectedRevision += NeowCapsuleNicheContinuation.Revision;
                                var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(cross);
                                if (!BonesCapsuleAllocation.TryCreateDefaultUnit(cross.Model, FilterExecutionPreferences.From(cross.RunOptions), registered, out var unit) || unit is null ||
                                    unit.ConditionPerformance.PhysicalImplementationRevision != expectedRevision ||
                                    !unit.UsesGpu || unit.InputModes != FilterInputModes.Dense ||
                                    !unit.Coverage.ToHashSet().SetEquals(["N.Neow", "R.Relic"]))
                                    throw new Exception("OpeningPrefixChangedKLocalUnit:" + mode);
                                // The old local body's truth is same-route OR,
                                // stricter than independent N and R unions.
                                if ((na && ra) || (nb && rb)) throw new Exception("OpeningPrefixKCrossWitnessSpliced");
                            }
                            else if (!NeowCapsuleComposite.TryCreate(cross.Model, cn, out var composite) || composite?.UsesBonesArcane != true ||
                                !NeowFamilyReplay.Matches(root, cn) || !composite.Reference.Matches(root))
                                throw new Exception("OpeningPrefixChangedArcaneIndependentUnion:" + mode);
                            checks++;
                        }
                        crossWitnesses++;
                        Console.WriteLine($"R_OPENING_PREFIX_CROSS predecessor={predecessor};seed={seed};independentUnion=true;sameRoute=false;Exact=false;PASS");
                    }
                    var plan = Plan(query, "on", out issue) ?? throw new Exception(issue);
                    string group = Group(plan);
                    if (!fixtureGroups.TryGetValue(group, out var fixtures)) fixtureGroups[group] = fixtures = [];
                    foreach (bool compact in new[] { false, true }) fixtures.Add(new {
                        name = predecessor.Entry + ".CorePositive.Offer" + observation.BonesFirst,
                        compact, start = ordinal, count = 1, ordinals = new uint[] { 0 }, expected = new uint[] { 0 },
                        buffers = RelicFamilyGpuExecutor.PackRfullFixtureBuffers(plan) });
                    offers.Add(observation.BonesFirst); positiveChecks++;
                    Console.WriteLine($"R_OPENING_PREFIX_CORE predecessor={predecessor};seed={seed};offerFirst={observation.BonesFirst};PASS");
                }
                if (offers.Count != 2 || crossWitnesses == 0) throw new Exception("OpeningPrefixCoreWitnessMissing:" + predecessor);
            }

            // A formerly unsupported shape checks the new default and the
            // selection captured during immutable request preparation.
            var guardQuery = Query([BaseGameModelKeys.Relics.ArcaneScroll, large], [], [Condition(large, Key(1))]);
            var guardRoute = NeowReplayPlan.Compile(guardQuery.Model, true);
            Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", null);
            bool capturedDefault = RelicFullGpuPlan.CaptureOpeningPrefixSelection();
            if (!capturedDefault || !RelicFullGpuPlan.TryCreate(guardQuery.Model, guardRoute, out var defaultPlan, out _) ||
                defaultPlan?.UsesOpeningPrefix != true || !CapsuleRelicFamily.TryCreate(guardQuery.Model, FilterExecutionPreferences.From(guardQuery.RunOptions), out var defaultFamily) ||
                defaultFamily is null || !defaultFamily.ConditionPerformance.UsesGpu ||
                defaultFamily.ConditionPerformance.PhysicalImplementationRevision != defaultPlan.Revision(false) &&
                defaultFamily.ConditionPerformance.PhysicalImplementationRevision != defaultPlan.Revision(true))
                throw new Exception("OpeningPrefixDefaultNotSelected");
            string defaultRevision = defaultPlan.Revision(false);
            string defaultFamilyRevision = defaultFamily.ConditionPerformance.PhysicalImplementationRevision;
            uint[][] capturedBuffers = RelicFamilyGpuExecutor.PackRfullFixtureBuffers(defaultPlan).Select(b => b.ToArray()).ToArray();
            Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", "off");
            bool capturedOff = RelicFullGpuPlan.CaptureOpeningPrefixSelection();
            if (capturedOff || !RelicFullGpuPlan.TryCreate(guardQuery.Model, guardRoute, out var capturedOnPlan, out _,
                    openingPrefixSelection: capturedDefault) || capturedOnPlan?.UsesOpeningPrefix != true ||
                capturedOnPlan.Revision(false) != defaultRevision ||
                !RelicFamilyGpuExecutor.PackRfullFixtureBuffers(capturedOnPlan).Zip(capturedBuffers)
                    .All(pair => pair.First.SequenceEqual(pair.Second)))
                throw new Exception("OpeningPrefixCapturedOnOverrideLost");
            // An existing prepared plan and Family retain their selected body
            // when another request changes the process setting.
            if (defaultPlan.Revision(false) != defaultRevision ||
                defaultFamily.ConditionPerformance.PhysicalImplementationRevision != defaultFamilyRevision ||
                !RelicFamilyGpuExecutor.PackRfullFixtureBuffers(defaultPlan).Zip(capturedBuffers)
                    .All(pair => pair.First.SequenceEqual(pair.Second)))
                throw new Exception("OpeningPrefixPreparedBodyChangedWithEnvironment");
            Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", "on");
            if (RelicFullGpuPlan.TryCreate(guardQuery.Model, guardRoute, out _, out string capturedOffIssue,
                    openingPrefixSelection: capturedOff) || capturedOffIssue != "RfullPreCapsuleRewardsContinuationUnsupported")
                throw new Exception("OpeningPrefixCapturedOffOverrideLost:" + capturedOffIssue);
            checks += 4;
            Console.WriteLine("R_OPENING_PREFIX_DEFAULT unset=on;capturedOnSurvivesOff=true;capturedOffSurvivesOn=true;preparedPlanAndFamilyImmutable=true;PASS");
            // Missing identity authority remains unavailable on both settings;
            // the extension must not make the original proof optional.
            var invalid = NeowReplayPlan.Compile(guardQuery.Model, true);
            invalid = invalid with { Authority = invalid.Authority with { IdentityAuthorityExact = false } };
            foreach (string mode in new[] { "off", "on" })
            {
                Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", mode);
                if (RelicFullGpuPlan.TryCreate(guardQuery.Model, invalid, out _, out _)) throw new Exception("OpeningPrefixInvalidAuthorityAdmitted:" + mode);
                checks++;
            }
            foreach (ModelKey predecessor in new[] { BaseGameModelKeys.Relics.ArcaneScroll,
                         BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.ScrollBoxes })
            {
                var originalEffects = context.Authority.EffectAuthority!;
                var brokenEffects = predecessor == BaseGameModelKeys.Relics.Kaleidoscope
                    ? originalEffects with { OtherCharacterPools = null, OtherCharacterPoolsExact = false }
                    : originalEffects with { CharacterRewardPool = null, CharacterRewardPoolExact = false,
                        CharacterRewardIdentityPoolExact = false };
                var brokenContext = context with { Authority = context.Authority.WithEffectAuthority(brokenEffects) };
                var broken = Query([predecessor, large], [predecessor, large], [Condition(large, Key(1))],
                    explicitContext: brokenContext);
                foreach (string mode in new[] { "off", "on" })
                {
                    if (Plan(broken, mode, out _) is not null || CapsuleRelicFamily.TryCreate(broken.Model, FilterExecutionPreferences.From(broken.RunOptions), out _))
                        throw new Exception("OpeningPrefixMissingDependentPoolAdmitted:" + predecessor + ":" + mode);
                    checks++;
                }
            }
            var tinyWorld = guardQuery.Model.Authority.WorldAuthority!;
            var tinyGeneration = tinyWorld.Beta109Generation!;
            var tinyBuckets = tinyGeneration.PlayerRelicBuckets.Select(b => {
                if (!b.BucketId.EndsWith(":Common", StringComparison.OrdinalIgnoreCase) &&
                    !b.BucketId.EndsWith(":Uncommon", StringComparison.OrdinalIgnoreCase) &&
                    !b.BucketId.EndsWith(":Rare", StringComparison.OrdinalIgnoreCase)) return b;
                var entries = b.OrderedEntries.Take(1).ToArray();
                return b with { OrderedEntries = entries, OrderedRelics = entries.Select(e => e.RelicKey).ToArray() };
            }).ToArray();
            var tiny = guardQuery with { Model = guardQuery.Model with { CompiledSearch = guardQuery.Model.CompiledSearch with {
                Context = guardQuery.Model.CompiledSearch.Context with { Authority = guardQuery.Model.Authority.WithWorldAuthority(tinyWorld with {
                    Beta109Generation = tinyGeneration with { PlayerRelicBuckets = tinyBuckets } }) } } } };
            var tinyRoute = NeowReplayPlan.Compile(tiny.Model, true);
            // The normal catalog compiler explicitly inserts Circlet. A small
            // valid pool therefore has supported depletion semantics; absence
            // cannot be inferred from the fixture's authored ordinary entries.
            if (tinyRoute.Authority.EffectCatalog.KeyOf(tinyRoute.Authority.EffectCatalog.CapsuleCircletId) !=
                BaseGameModelKeys.OrdinaryRelics.Circlet) throw new Exception("OpeningPrefixTinyCircletFixtureMissing");
            Add("Tiny.ValidDepletion", tiny, true);
            var circletQuery = CompileFixtureRequest(tiny.Model.Evaluation.ToLegacyFilter() with {
                StructuredNeowEffects = [Condition(large, BaseGameModelKeys.OrdinaryRelics.Circlet)]
            }, tiny.Model.CompiledSearch.Context, tiny.RunOptions);
            Add("Tiny.CircletOutput", circletQuery, true);
            var missingCirclet = tinyRoute with { Authority = tinyRoute.Authority with {
                EffectCatalog = tinyRoute.Authority.EffectCatalog with { CapsuleCircletId = Beta110FastDenseId.Invalid } } };
            foreach (string mode in new[] { "off", "on" })
            {
                Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", mode);
                if (RelicFullGpuPlan.TryCreate(tiny.Model, missingCirclet, out _, out _))
                    throw new Exception("OpeningPrefixTinyInvalidCircletAdmitted:" + mode);
                checks++;
            }
            string? directory = Environment.GetEnvironmentVariable("RT2_R_OPENING_FIXTURE_DIR");
            if (!string.IsNullOrEmpty(directory))
            foreach (var group in fixtureGroups)
            foreach (var variant in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
            {
                string target = Path.Combine(directory, group.Key, variant.ToString());
                Directory.CreateDirectory(target);
                var shape = shaderShapes[group.Key];
                string shader = RelicFamilyGpuExecutor.ShaderSource(true, genericReplay: shape.Generic,
                    localStateCapacity: shape.StateCapacity, numericalVariant: variant, openingPrefix: shape.Prefix);
                if (shape.Generic) shader = shader.Replace("#version 450", "#version 450\n#define RT2_R_GENERIC_BAG_CAPACITY " +
                    shape.BagCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(target, "rfull.glsl"), shader);
                File.WriteAllText(Path.Combine(target, "rfull-fixtures.json"), System.Text.Json.JsonSerializer.Serialize(group.Value));
            }
            if (newTracked == 0 || newGeneric == 0) throw new Exception("OpeningPrefixNewBodiesNotExercised");
            Console.WriteLine($"R_OPENING_PREFIX_PASS checks={checks};positives={positiveChecks};newTracked={newTracked};newGeneric={newGeneric};fixtures={fixtureGroups.Sum(g => g.Value.Count)};independentOriginalCpu=true");
            return 0;
        }
        finally { Environment.SetEnvironmentVariable("RT2_R_OPENING_PREFIX", saved); }
    }

    private static SearchContext OpeningPrefixContext(PreparedSearch request, ModelKey? characterOverride = null)
    {
        // The same coherent Beta111 construction used by the existing Arcane
        // Core Exact check: effect source pools and R's World bag describe one
        // world. This is fixture construction, not a second R implementation.
        var world = request.Model.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        NeowEffectRelicSnapshot[] Relics(IEnumerable<Beta109RelicBucketSnapshot> buckets) => buckets.SelectMany(bucket =>
            bucket.OrderedEntries.Select((entry, index) => new NeowEffectRelicSnapshot(entry.RelicKey, index,
                Enum.Parse<EffectRelicRarity>(bucket.BucketId.Split(':')[^1]), NestedRelicEffectKind.NoTrackedImmediateEffect,
                RarityCode: bucket.BucketId.Split(':')[^1], IsAllowedInShops: entry.IsAllowedInShops, ShopEligibilityExact: true))).ToArray();
        var effects = request.Model.Authority.EffectAuthority! with {
            SharedRelicPoolSource = Relics(generation.SharedRelicBuckets), CharacterRelicPoolSource = Relics(generation.PlayerRelicBuckets),
            CapturedProfileId = RuntimeProfileId.Beta111, CharacterRewardIdentityPoolExact = true };
        var players = generation.PlayerRelicBuckets.Select(bucket => {
            var shared = generation.SharedRelicBuckets.Single(b => b.BucketId.Split(':')[^1] == bucket.BucketId.Split(':')[^1]);
            var entries = shared.OrderedEntries.Concat(bucket.OrderedEntries).DistinctBy(e => e.RelicKey).ToArray();
            return bucket with { OrderedEntries = entries, OrderedRelics = entries.Select(e => e.RelicKey).ToArray() };
        }).ToArray();
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(characterOverride ?? request.Model.Authority.Character.CharacterKey,
            request.Model.Authority.Ascension, 0, 1).WithEffectAuthority(effects).WithWorldAuthority(world with {
                CapturedProfileId = RuntimeProfileId.Beta111,
                Beta109Generation = generation with { Profile = RuntimeProfileId.Beta111, PlayerRelicBuckets = players } });
        return request.Model.CompiledSearch.Context with { Authority = authority, CharacterKey = authority.Character.CharacterKey,
            ProfileId = RuntimeProfileId.Beta111,
            Detection = new GameVersionDetection(RuntimeVersionCompatibility.Beta111Version, RuntimeVersionCompatibility.Beta111Version,
                "beta", "r-opening-prefix-fixture", true, string.Empty) };
    }
}
