using RolltheSpire2.Core.Authority;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search.Neow;

int count = ReadInt(args, "--count", 10_000);
if (args.Contains("--shion-neow-optimization", StringComparer.Ordinal)) return ShionNeowOptimizationChecks.Run();
if (args.Contains("--shion-exact-trial", StringComparer.Ordinal)) return ShionExactTrialChecks.Run();
if (args.Contains("--neow-cpu-four-way", StringComparer.Ordinal)) return NeowCpuFourWayChecks.Run();
if (args.Contains("--prepared-combat-pools", StringComparer.Ordinal)) return PreparedCombatRewardPoolChecks.Run(ReadInt(args, "--count", 32));
if (args.Contains("--c-complete-matching", StringComparer.Ordinal)) return CCompleteMatchingChecks.Run(
    ReadInt(args, "--count", 128), args.Contains("--export-c-complete", StringComparer.Ordinal));
if (args.Contains("--simple-probability-principles", StringComparer.Ordinal)) return SimpleProbabilityPrinciplesChecks.Run();
if (args.Contains("--manual-family-order", StringComparer.Ordinal)) return FamilyAllocationPricingChecks.RunManualOrder();
if (args.Contains("--variant-opening-preparation", StringComparer.Ordinal)) return VariantOpeningPreparationChecks.Run();
if (args.Contains("--variant-opening-survival", StringComparer.Ordinal)) return VariantOpeningSurvivalChecks.Run();
if (args.Contains("--variant-opening-execution", StringComparer.Ordinal)) return VariantOpeningExecutionChecks.Run();
if (args.Contains("--variant-opening-integration", StringComparer.Ordinal)) return VariantOpeningIntegrationChecks.Run();
if (args.Contains("--family-work-estimates", StringComparer.Ordinal)) return FamilyWorkEstimateChecks.Run();
if (args.Contains("--family-quote-entry", StringComparer.Ordinal))
    return FamilyQuoteEntryChecks.Run(args.Contains("--quote-baseline", StringComparer.Ordinal)
        ? args[Array.IndexOf(args, "--quote-baseline") + 1] : null);
if (args.Contains("--eta-cost-closure", StringComparer.Ordinal)) return EtaCostClosureChecks.Run();
if (args.Contains("--combat-probability-bench", StringComparer.Ordinal)) return PredictabilityMathChecks.RunCombatProbabilityBench();
if (args.Contains("--small-candidate-exact", StringComparer.Ordinal)) return SmallCandidateExactChecks.Run();
if (args.Contains("--search-failure-continuation", StringComparer.Ordinal)) return SearchFailureContinuationChecks.Run();
if (args.Contains("--search-recovery-cost", StringComparer.Ordinal)) return SearchFailureContinuationChecks.RunRecoveryCost();
if (args.Contains("--family-private-complete-call-cost", StringComparer.Ordinal)) return FamilyPrivateCompleteCallCostChecks.Run();
if (Array.IndexOf(args, "--family-private-complete-call-reference") is int privateReferenceAt && privateReferenceAt >= 0)
    return FamilyPrivateCompleteCallCostChecks.RunReference(args[privateReferenceAt + 1]);
if (Array.IndexOf(args, "--family-complete-call-reference") is int publicReferenceAt && publicReferenceAt >= 0)
    return FamilyCanonicalCostChecks.RunReference(args[publicReferenceAt + 1]);
if (args.Contains("--family-canonical-cost-only", StringComparer.Ordinal)) return FamilyCanonicalCostChecks.Run();
if (args.Contains("--initial-room-numerical", StringComparer.Ordinal)) return InitialRoomNumericalChecks.Run();
if (args.Contains("--predictor-partial-knowledge", StringComparer.Ordinal))
{
    PredictorChoiceIntChecks.Run();
    PredictorKnowledgeTraceChecks.Run();
    PredictorDeckKnowledgeChecks.Run();
    PredictorCauseEvidenceChecks.Run();
    PredictorRouteFactRecorderChecks.Run();
    InRunMapDiagnosticsFormatterChecks.Run();
    PredictorRoutePartialKnowledgeChecks.Run();
    PredictorCombatTraceChecks.Run();
    PredictorEventTraceChecks.Run();
    PredictorStreamKnowledgeChecks.Run();
    PredictorRoomResultRecordingChecks.Run();
    return 0;
}
if (args.Contains("--predictor-map-draft", StringComparer.Ordinal))
{
    PredictorMapStrokeRecognitionChecks.Run();
    return PredictorMapRouteDraftChecks.Run();
}
if (args.Contains("--predictor-runtime", StringComparer.Ordinal)) return PredictorRuntimeChecks.Run();
if (args.Contains("--predictor-runtime-sources", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run();
if (args.Contains("--predictor-runtime-route", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(routesOnly: true);
if (args.Contains("--predictor-runtime-events", StringComparer.Ordinal)) return PredictorEventChecks.Run();
if (args.Contains("--predictor-runtime-initialization", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "initialization");
if (args.Contains("--predictor-runtime-live-capture", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "live-capture");
if (args.Contains("--predictor-feature-preferences", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "feature-preferences");
if (args.Contains("--predictor-runtime-live-policy", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "live-policy");
if (args.Contains("--predictor-runtime-drawing", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "drawing");
if (args.Contains("--predictor-runtime-drawing-boundaries", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "drawing-boundaries");
if (args.Contains("--predictor-runtime-drawing-overblocks", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "drawing-overblocks");
if (args.Contains("--predictor-runtime-card-operations", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "card-operations");
if (args.Contains("--predictor-runtime-dependency-boundaries", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "dependency-boundaries");
if (args.Contains("--predictor-runtime-repy", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "repy");
if (args.Contains("--predictor-runtime-tablet", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "tablet");
if (args.Contains("--predictor-runtime-combat-facts", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "combat-facts");
if (args.Contains("--predictor-runtime-shop", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "shop");
if (args.Contains("--predictor-runtime-route-choices", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "route-choices");
if (args.Contains("--predictor-runtime-route-events", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "route-events");
if (args.Contains("--predictor-runtime-route-journey", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "route-journey");
if (args.Contains("--predictor-runtime-obtain", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "obtain");
if (args.Contains("--predictor-runtime-map", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "map");
if (args.Contains("--predictor-operation", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "operation");
if (args.Contains("--neow-operation", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "neow-operation");
if (args.Contains("--predictor-runtime-native-events", StringComparer.Ordinal)) return PredictorRuntimeSourceChecks.Run(domain: "events");
if (args.Contains("--transformation-relic")) return TransformationRelicContinuationChecks.Run();
if (args.Contains("--runtime-version-profile", StringComparer.Ordinal)) return RuntimeVersionProfileChecks.Run();
if (args.Contains("--event-prediction", StringComparer.Ordinal)) return PredictorClosureChecks.CheckEventResults();
if (args.Contains("--silken-tress-order")) return FamilyCombatRewardChecks.CheckSilkenTressOpeningOrder();
if (args.Contains("--capsule-obtain-policy")) return FamilyNeowChecks.CheckCapsuleObtainPolicy();
if (args.Contains("--combination-regressions", StringComparer.Ordinal)) return CombinationRegressionChecks.Run();
if (args.Contains("--persistence", StringComparer.Ordinal)) return PersistenceChecks.Run();
if (args.Contains("--in-run-feature-defaults", StringComparer.Ordinal)) return PersistenceChecks.Run(featurePreferencesOnly: true);
if (args.Contains("--developer-notes-document", StringComparer.Ordinal))
{
    int archive = DeveloperNotesArchiveChecks.Run();
    return archive == 0 ? PersistenceChecks.Run(notesOnly: true) : archive;
}
if (args.Contains("--feedback-export", StringComparer.Ordinal)) return FeedbackBundleChecks.Run();
if (args.Contains("--preset-workbench", StringComparer.Ordinal)) return PresetWorkbenchChecks.Run();
if (args.Contains("--seed-library-workflow", StringComparer.Ordinal)) return SeedLibraryWorkflowChecks.Run();
if (args.Contains("--seed-library-persistence", StringComparer.Ordinal)) return SeedLibraryPersistenceChecks.Run();
if (args.Contains("--card-collection-store", StringComparer.Ordinal)) return CardCollectionStoreChecks.Run();
if (args.Contains("--combat-card-exclusion", StringComparer.Ordinal)) return CombatCardExclusionChecks.Run();
if (args.Contains("--card-collection-workbench", StringComparer.Ordinal)) return CardCollectionWorkbenchChecks.Run();
if (args.Contains("--predictability-math", StringComparer.Ordinal)) return PredictabilityMathChecks.Run();
if (args.Contains("--workbench-contract")) return WorkbenchContractChecks.Run();
if (args.Contains("--result-navigation")) return WorkbenchContractChecks.RunResultNavigation();
if (args.Contains("--combat-rarity-audit")) return CombatRarityAuditChecks.Run();
if (args.Contains("--object-picker-context")) return ObjectPickerContextChecks.Run();
if (args.Contains("--unknown-room", StringComparer.Ordinal)) return UnknownRoomChecks.Run();
if (args.Contains("--treasure-room", StringComparer.Ordinal)) { Console.WriteLine("TREASURE_ROOM_PASS " + RelicCpuChecks.CheckTreasureProjection()); return 0; }
if (args.Contains("--initial-room-exact", StringComparer.Ordinal)) return InitialRoomExactChecks.Run();
if (args.Contains("--map-route", StringComparer.Ordinal)) return MapRouteChecks.Run();
if (args.Contains("--map-drawing-integration", StringComparer.Ordinal)) return MapDrawingIntegrationChecks.Run();
if (args.Contains("--map-lossy-integration", StringComparer.Ordinal)) return MapLossyIntegrationChecks.Run();
if (args.Contains("--map-route-sequence", StringComparer.Ordinal)) return MapRouteSequenceChecks.Run();
if (args.Contains("--map-cost-measure", StringComparer.Ordinal)) return MapRouteChecks.MeasureCost();
if (args.Contains("--map-preview", StringComparer.Ordinal)) return MapPreviewChecks.Run();
if (args.Contains("--map-full-integration", StringComparer.Ordinal))
    return MapFullIntegrationChecks.Run(args.Contains("--map-full-baseline", StringComparer.Ordinal)
        ? args[Array.IndexOf(args, "--map-full-baseline") + 1] : null);
if (args.Contains("--transformation-aggregate"))
{
    int openingAt=Array.IndexOf(args,"--opening-filter");
    if(openingAt>=0 && openingAt+1>=args.Length)throw new ArgumentException("--opening-filter requires a value");
    return TransformationAggregateChecks.Run(openingAt>=0 && openingAt+1<args.Length ? args[openingAt+1] : null);
}
if (args.Contains("--morphic-filter")) return MorphicFilterChecks.Run();
if (args.Contains("--authored-morphic")) return RolltheSpire2.Tests.FastSearch.AuthoredMorphicChecks.Run();
if (args.Contains("--authored-morphic-captured")) return RolltheSpire2.Tests.FastSearch.AuthoredMorphicChecks.RunCaptured();
if (args.Contains("--release-compatibility")) return ReleaseCompatibilityChecks.Run();
if (args.Contains("--historical-data")) return HistoricalDataChecks.Run();
if (args.Contains("--archive-current-runtime")) return HistoricalDataChecks.ArchiveCurrent();
if (args.Contains("--cpu-cost-closure")) return CpuCostClosureChecks.Run();
if (args.Contains("--cpu-cost-measure")) return CpuCostClosureChecks.Measure(args);
if (args.Contains("--family-cpu-integration")) return FamilyCpuIntegrationChecks.Run();
if (args.Contains("--family-cpu-only")) return FamilyCpuOnlyChecks.Run();
if (args.Contains("--cpu-worker-budget", StringComparer.Ordinal)) return CpuWorkerBudgetChecks.Run();
if (args.Contains("--cpu-relic")) return RelicCpuChecks.Run(count);
if (args.Contains("--cpu-neow-identity")) return NeowIdentityCpuChecks.Run();
if (args.Contains("--cpu-relic-shop")) return RelicShopCpuChecks.Run();
if (args.Contains("--terminal-queue-accounting")) return TerminalQueueAccountingChecks.Run();
if (args.Contains("--compatibility-reference", StringComparer.Ordinal)) return CompatibilityReferenceChecks.Run();
if (args.Contains("--shop-ordered", StringComparer.Ordinal)) return ShopOrderedChecks.Run();
if (args.Contains("--wwp-commitment", StringComparer.Ordinal)) return WwpCommitmentChecks.Run();
if (args.Contains("--exact-boundary", StringComparer.Ordinal)) return ExactBoundaryChecks.Run();
if (args.Contains("--exact-cancellation", StringComparer.Ordinal)) return ExactBoundaryChecks.RunCancellation();
if (args.Contains("--party-exact-independent", StringComparer.Ordinal)) return ExactBoundaryChecks.RunPartyIndependentMismatch();
if (args.Contains("--predictor-closure", StringComparer.Ordinal)) return PredictorClosureChecks.Run();
if (args.Contains("--production-replay", StringComparer.Ordinal)) return ProductionReplayChecks.Run();
if (args.Contains("--gpu-cost-calibration", StringComparer.Ordinal)) return GpuCostCalibrationChecks.Run();
if (args.Contains("--family-ae", StringComparer.Ordinal)) return FamilyAEChecks.Run(count);
if (args.Contains("--family-allocation-pricing", StringComparer.Ordinal)) return FamilyAllocationPricingChecks.Run();
if (args.Contains("--gpu-owner-lifecycle", StringComparer.Ordinal)) return GpuOwnerLifecycleChecks.Run();
if (args.Contains("--family-world", StringComparer.Ordinal)) return FamilyWorldChecks.Run(count);
if (args.Contains("--family-combat", StringComparer.Ordinal)) return FamilyCombatRewardChecks.Run(count, args.Contains("--export-combat-gpu", StringComparer.Ordinal));
if (args.Contains("--c-explicit-opening", StringComparer.Ordinal)) return CExplicitOpeningChecks.Run(count);
if (!args.Contains("--family-neow", StringComparer.Ordinal)) throw new ArgumentException("Select a current --family-* / --predictor-closure / --exact-boundary / --wwp-commitment check; old whole-Fast pipeline checks have retired.");
ModelKey characterKey = BaseGameModelKeys.Characters.Silent;
RuntimeContextAuthoritySnapshot authority = Beta110FastSearchFixtureAuthority.Create(characterKey, 10, 0, 1);
var detection = new GameVersionDetection("0.110.1", "0.110.1", "beta", "parity-fixture", true, string.Empty);
var scenarios = new (string Name, NeowSearchFilter Filter)[]
{
    ("FastInfrastructureDisabled", NeowSearchFilter.Empty),
    ("CursedPearl", NeowSearchFilter.Empty with
    {
        NeowRelics = new ModelKeySetFilter(Array.Empty<ModelKey>(), new[] { BaseGameModelKeys.Relics.CursedPearl }, Array.Empty<ModelKey>())
    }),
    ("RequiredGoldenPearlRoute", NeowSearchFilter.Empty with
    {
        NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.GoldenPearl)
    }),
    ("ExcludedCursedPearl", NeowSearchFilter.Empty with
    {
        NeowRelics = new ModelKeySetFilter(Array.Empty<ModelKey>(), Array.Empty<ModelKey>(), new[] { BaseGameModelKeys.Relics.CursedPearl })
    }),
    ("RequireBones", NeowSearchFilter.Empty with { RequireNeowsBones = true }),
    ("BonesUnorderedPair", NeowSearchFilter.Empty with
    {
        RequireNeowsBones = true,
        RequiredBonesCombination = new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.DowsingRod }
    }),
    ("BonesOrderForward", NeowSearchFilter.Empty with
    {
        RequireNeowsBones = true,
        RequiredBonesCombination = new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.DowsingRod },
        RequiredBonesAcquisitionOrder = new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.DowsingRod }
    }),
    ("BonesOrderReverse", NeowSearchFilter.Empty with
    {
        RequireNeowsBones = true,
        RequiredBonesCombination = new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.DowsingRod },
        RequiredBonesAcquisitionOrder = new[] { BaseGameModelKeys.Relics.DowsingRod, BaseGameModelKeys.Relics.CursedPearl }
    }),
    ("FastPlusExactOnly", NeowSearchFilter.Empty with
    {
        NeowRelics = new ModelKeySetFilter(Array.Empty<ModelKey>(), new[] { BaseGameModelKeys.Relics.CursedPearl }, Array.Empty<ModelKey>()),
        RequireSmallCapsule = true
    }),
    ("ExactOnly", NeowSearchFilter.Empty with { RequireSmallCapsule = true }),
    ("CrossRouteNoSplice", NeowSearchFilter.Empty with
    {
        NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.GoldenPearl),
        RequiredBonesCombination = new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.DowsingRod }
    })
};

ModelKey benchCommon0 = new(BaseGameModelKeys.Categories.Card, "BENCH_CHARACTER_0");
ModelKey benchCommon1 = new(BaseGameModelKeys.Categories.Card, "BENCH_CHARACTER_1");
ModelKey benchUncommon10 = new(BaseGameModelKeys.Categories.Card, "BENCH_CHARACTER_10");
ModelKey benchRare16 = new(BaseGameModelKeys.Categories.Card, "BENCH_CHARACTER_16");
ModelKey benchColorless0 = new(BaseGameModelKeys.Categories.Card, "BENCH_COLORLESS_0");
ModelKey benchOther00 = new(BaseGameModelKeys.Categories.Card, "BENCH_OTHER_0_0");
ModelKey benchOther10 = new(BaseGameModelKeys.Categories.Card, "BENCH_OTHER_1_0");
ModelKey benchCommonPotion = new(BaseGameModelKeys.Categories.Potion, "BENCH_COMMON_POTION");
ModelKey benchUncommonPotion = new(BaseGameModelKeys.Categories.Potion, "BENCH_UNCOMMON_POTION");
var approximateScenarios = new (string Name, NeowSearchFilter Filter, bool ExpectRisk)[]
{
    ("LargeCapsuleOneRelic", StructuredRoute(
        BaseGameModelKeys.Relics.LargeCapsule,
        Structured(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.Whetstone)), false),
    ("LargeCapsuleTwoRelics", StructuredRoute(
        BaseGameModelKeys.Relics.LargeCapsule,
        Structured(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.Whetstone, BaseGameModelKeys.OrdinaryRelics.WarPaint)), false),
    ("SmallCapsuleRelic", StructuredRoute(
        BaseGameModelKeys.Relics.SmallCapsule,
        Structured(BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.WarPaint)), false),
    ("KaleidoscopeOneGroupTarget", StructuredRoute(
        BaseGameModelKeys.Relics.Kaleidoscope,
        Structured(BaseGameModelKeys.Relics.Kaleidoscope, NeowStructuredConditionKind.IndependentOfferGroupTargets,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchOther00)), false),
    ("KaleidoscopeTwoGroupTargets", StructuredRoute(
        BaseGameModelKeys.Relics.Kaleidoscope,
        Structured(BaseGameModelKeys.Relics.Kaleidoscope, NeowStructuredConditionKind.IndependentOfferGroupTargets,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchOther00, benchOther10)), false),
    ("ScrollBoxesCommon", StructuredRoute(
        BaseGameModelKeys.Relics.ScrollBoxes,
        Structured(BaseGameModelKeys.Relics.ScrollBoxes, NeowStructuredConditionKind.StructuredCardComposition,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchCommon0)), false),
    ("ScrollBoxesCommonUncommon", StructuredRoute(
        BaseGameModelKeys.Relics.ScrollBoxes,
        Structured(BaseGameModelKeys.Relics.ScrollBoxes, NeowStructuredConditionKind.StructuredCardComposition,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchCommon0, benchUncommon10)), false),
    ("BonesKaleidoscopeAndRelic", BonesStructured(
        new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.CursedPearl },
        Structured(BaseGameModelKeys.Relics.Kaleidoscope, NeowStructuredConditionKind.IndependentOfferGroupTargets,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchOther00)), false),
    ("BonesLargeCapsuleAndRelic", BonesStructured(
        new[] { BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.CursedPearl },
        Structured(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.Whetstone, BaseGameModelKeys.OrdinaryRelics.WarPaint)), false),
    ("CapsuleWhetstone", StructuredRoute(
        BaseGameModelKeys.Relics.LargeCapsule,
        Structured(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.Whetstone)), false),
    ("CapsuleWarPaint", StructuredRoute(
        BaseGameModelKeys.Relics.SmallCapsule,
        Structured(BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.WarPaint)), false),
    ("WhetstoneDownstreamKaleidoscopeIdentityOnly", BonesStructured(
        new[] { BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.Kaleidoscope },
        Structured(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.Whetstone),
        Structured(BaseGameModelKeys.Relics.Kaleidoscope, NeowStructuredConditionKind.IndependentOfferGroupTargets,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchOther00)), false),
    ("ArcaneScrollRareCard", StructuredRoute(
        BaseGameModelKeys.Relics.ArcaneScroll,
        Structured(BaseGameModelKeys.Relics.ArcaneScroll, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.ProductRelevantEffects, NeowStructuredOutputKind.Card,
            benchRare16)), false),
    ("HeftyTabletRareOffer", StructuredRoute(
        BaseGameModelKeys.Relics.HeftyTablet,
        Structured(BaseGameModelKeys.Relics.HeftyTablet, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchRare16)), false),
    ("LeadPaperweightColorless", StructuredRoute(
        BaseGameModelKeys.Relics.LeadPaperweight,
        Structured(BaseGameModelKeys.Relics.LeadPaperweight, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchColorless0)), false),
    ("LostCofferCardPotion", StructuredRoute(
        BaseGameModelKeys.Relics.LostCoffer,
        Structured(BaseGameModelKeys.Relics.LostCoffer, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchCommon0),
        Structured(BaseGameModelKeys.Relics.LostCoffer, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Potion,
            benchCommonPotion)), false),
    ("PhialHolsterPotions", StructuredRoute(
        BaseGameModelKeys.Relics.PhialHolster,
        Structured(BaseGameModelKeys.Relics.PhialHolster, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.GeneratedPotions, NeowStructuredOutputKind.Potion,
            benchCommonPotion, benchUncommonPotion)), false),
    ("LeafyPoulticeTransforms", StructuredRoute(
        BaseGameModelKeys.Relics.LeafyPoultice,
        Structured(BaseGameModelKeys.Relics.LeafyPoultice, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card,
            benchCommon0, benchCommon1)), false),
    ("NewLeafNormalizedTransform", StructuredRoute(
        BaseGameModelKeys.Relics.NewLeaf,
        Structured(BaseGameModelKeys.Relics.NewLeaf, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card,
            benchCommon0)), false),
    ("BonesNewLeafTransformFinalCurse", BonesStructured(
        new[] { BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.CursedPearl },
        Structured(BaseGameModelKeys.Relics.NewLeaf, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card,
            benchCommon0)) with
    {
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false)
};

var c0LossyScenarios = new (string Name, NeowSearchFilter Filter, bool ExpectRisk)[]
{
    ("FinalCurseOnly", NeowSearchFilter.Empty with
    {
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false),
    ("BonesIdentityFinalCurse", NeowSearchFilter.Empty with
    {
        NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.NeowsBones),
        RequireNeowsBones = true,
        RequiredBonesCombination = new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.DowsingRod },
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false),
    ("BonesKaleidoscopeFinalCurse", BonesStructured(
        new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.CursedPearl },
        Structured(BaseGameModelKeys.Relics.Kaleidoscope, NeowStructuredConditionKind.IndependentOfferGroupTargets,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
            benchOther00)) with
    {
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false),
    ("BonesLargeCapsuleFinalCurse", BonesStructured(
        new[] { BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.CursedPearl }) with
    {
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false),
    ("BonesLargeCapsuleWhetstoneFinalCurseIdentityOnly", BonesStructured(
        new[] { BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.CursedPearl },
        Structured(BaseGameModelKeys.Relics.LargeCapsule, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.Whetstone)) with
    {
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false),
    ("BonesSmallCapsuleWarPaintFinalCurseIdentityOnly", BonesStructured(
        new[] { BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.CursedPearl },
        Structured(BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
            BaseGameModelKeys.OrdinaryRelics.WarPaint)) with
    {
        RequiredFinalCurse = BaseGameModelKeys.Cards.Greed
    }, false)
};

if (args.Contains("--family-neow", StringComparer.Ordinal))
{
    var familyAuthority = CreateFamilyAuthority(authority);
    return FamilyNeowChecks.Run(scenarios.Concat(approximateScenarios.Select(s => (s.Name, s.Filter)))
        .Select(s =>
        {
            return (s.Name, CompilePlan(s.Name, s.Filter, familyAuthority, detection, count));
        }).ToArray(), count, authority);
}

return 0;
static NeowStructuredEffectSearchCondition Structured(
    ModelKey source,
    NeowStructuredConditionKind kind,
    NeowStructuredEffectScope scope,
    NeowStructuredOutputKind outputKind,
    params ModelKey[] outputs) => new(source, kind, scope, outputKind, outputs);

static NeowSearchFilter StructuredRoute(
    ModelKey route,
    params NeowStructuredEffectSearchCondition[] conditions) => NeowSearchFilter.Empty with
{
    NeowRoute = new NeowRouteSearchCondition(route),
    StructuredNeowEffects = conditions
};

static NeowSearchFilter BonesStructured(
    IReadOnlyList<ModelKey> requiredCombination,
    params NeowStructuredEffectSearchCondition[] conditions) => NeowSearchFilter.Empty with
{
    NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.NeowsBones),
    RequireNeowsBones = true,
    RequiredBonesCombination = requiredCombination,
    StructuredNeowEffects = conditions
};

static ExactSearchExecutionRequest CompilePlan(
    string name,
    NeowSearchFilter filter,
    RuntimeContextAuthoritySnapshot authority,
    GameVersionDetection detection,
    long scanCount,
    int workerCount = 1)
{
    var context = SearchContextFactory.From(
        RuntimeProfileId.Beta110,
        authority.Character.CharacterKey,
        authority.Ascension,
        authority,
        detection,
        filter.AncientOptionConditions);
    var compiled = SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(filter), context);
    var result = ExactSearchExecutionRequestFactory.Compile(compiled,
        new SearchRunOptions("000000000000", scanCount, 30, workerCount, IncludeDiagnostics: false));
    return result.Plan ?? throw new InvalidOperationException("Compile failed:" + name + ":" + result.Disposition + ":" + result.Issue);
}

static RuntimeContextAuthoritySnapshot CreateFamilyAuthority(RuntimeContextAuthoritySnapshot source)
{
    // N's declared Capsule outputs and R's initial bag describe the same world.
    // Retain the broad synthetic R lanes, including the original four audited
    // effect relics and their Whetstone/War Paint side-effect metadata.
    var world = CreateBeta110RelicWorld(source.Character.CharacterKey, modded: false);
    var generation = world.Beta109Generation!;
    var originalEffects = source.EffectAuthority!;
    var known = originalEffects.SharedRelicPoolSource!.Concat(originalEffects.CharacterRelicPoolSource!)
        .ToDictionary(relic => relic.RelicKey);
    var shared = generation.SharedRelicBuckets;
    var character = generation.PlayerRelicBuckets.Select(bucket =>
    {
        string rarity = bucket.BucketId.Split(':')[^1];
        var entries = bucket.OrderedEntries.Concat(known.Values
            .Where(relic => relic.Rarity.ToString() == rarity)
            .Select(relic => new Beta109RelicBucketEntrySnapshot(relic.RelicKey, true, true)))
            .DistinctBy(entry => entry.RelicKey).ToArray();
        return bucket with { OrderedEntries = entries, OrderedRelics = entries.Select(entry => entry.RelicKey).ToArray() };
    }).ToArray();
    var player = character.Select(bucket =>
    {
        var sharedBucket = shared.Single(item => item.BucketId.Split(':')[^1] == bucket.BucketId.Split(':')[^1]);
        var entries = sharedBucket.OrderedEntries.Concat(bucket.OrderedEntries).DistinctBy(entry => entry.RelicKey).ToArray();
        return bucket with { OrderedEntries = entries, OrderedRelics = entries.Select(entry => entry.RelicKey).ToArray() };
    }).ToArray();
    NeowEffectRelicSnapshot[] Relics(IEnumerable<Beta109RelicBucketSnapshot> buckets) => buckets
        .SelectMany(bucket => bucket.OrderedEntries.Select(entry =>
        {
            string rarity = bucket.BucketId.Split(':')[^1];
            var relic = known.TryGetValue(entry.RelicKey, out var declared) ? declared : new NeowEffectRelicSnapshot(
                entry.RelicKey, 0, Enum.Parse<EffectRelicRarity>(rarity), NestedRelicEffectKind.NoTrackedImmediateEffect);
            return relic with { RarityCode = rarity, IsAllowedInShops = entry.IsAllowedInShops,
                ShopEligibilityExact = entry.ShopEligibilityExact };
        })).Select((relic, index) => relic with { BagOrder = index }).ToArray();
    var sharedRelics = Relics(shared);
    var characterRelics = Relics(character);
    var aligned = source.WithEffectAuthority(originalEffects with {
        SharedRelicPoolSource = sharedRelics, CharacterRelicPoolSource = characterRelics,
        RelicBagFingerprint = "family-neow-coherent-relic-bag",
        SnapshotFingerprint = originalEffects.SnapshotFingerprint + ":family-relics"
    }).WithWorldAuthority(world with {
        SharedRelicPoolSource = sharedRelics, CharacterRelicPoolSource = characterRelics,
        Beta109Generation = generation with { SharedRelicBuckets = shared, PlayerRelicBuckets = player,
            SnapshotFingerprint = generation.SnapshotFingerprint + ":family-relics" }
    });
    return aligned.WithEffectAuthority(aligned.EffectAuthority! with {
        OrderedRelicBag = NeowRewardGenerator.BuildOrderedRelicBag(Beta110Profile.Instance,
            generation.RunSeedRoot, aligned)
    });
}

static WorldAuthoritySnapshot CreateBeta110RelicWorld(
    ModelKey characterKey,
    bool modded,
    ModelKey? opaqueModRelic = null,
    int sharedCommonCount = 3,
    bool removeOneUnlockedCommon = false)
{
    WorldAuthoritySnapshot source = RelicSequenceFixtureFactory.CreateModern(
        exactShopEligibility: true,
        sharedCommonCount: sharedCommonCount,
        noUnknownHooksOrModifiers: true);
    Beta109WorldGenerationSnapshot modern = source.Beta109Generation
        ?? throw new InvalidOperationException("Missing modern relic fixture.");
    Beta109RelicBucketSnapshot[] playerBuckets = modern.PlayerRelicBuckets.ToArray();
    int commonIndex = Array.FindIndex(playerBuckets, bucket =>
        bucket.BucketId.EndsWith(":Common", StringComparison.OrdinalIgnoreCase));
    Beta109RelicBucketSnapshot common = playerBuckets[commonIndex];
    var commonEntries = common.OrderedEntries.ToList();
    commonEntries.Add(new Beta109RelicBucketEntrySnapshot(
        new ModelKey(BaseGameModelKeys.Categories.Relic, "CHARACTER_POOL_" + characterKey.Entry),
        true,
        true));
    if (removeOneUnlockedCommon)
    {
        commonEntries.RemoveAll(entry => entry.RelicKey.Entry == "M_PLAYER_C3");
    }
    if (opaqueModRelic.HasValue)
    {
        commonEntries.Add(new Beta109RelicBucketEntrySnapshot(opaqueModRelic.Value, true, true));
    }
    Beta109RelicBucketEntrySnapshot[] entries = commonEntries.ToArray();
    playerBuckets[commonIndex] = common with
    {
        OrderedRelics = entries.Select(entry => entry.RelicKey).ToArray(),
        OrderedEntries = entries
    };

    string seed = "000000000000";
    ulong root = Beta110Profile.Instance.ComputeRootSeed(seed);
    modern = modern with
    {
        Profile = RuntimeProfileId.Beta110,
        OriginalSeed = seed,
        CanonicalSeed = seed,
        ActSelectionRoot = root,
        RunSeedRoot = root,
        CharacterKey = characterKey,
        LobbyPlayers = new[]
        {
            new Beta109LobbyPlayerSnapshot(0, characterKey, false, true)
        },
        PlayerRelicBuckets = playerBuckets,
        NoUnknownHooksOrModifiers = true,
        IsVanilla = !modded,
        FixtureValidationStatus = Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown,
        CatalogFingerprint = $"beta110-relic-c2-catalog:{characterKey.Serialized}:{modded}:{sharedCommonCount}",
        UnlockFingerprint = $"beta110-relic-c2-unlocks:{removeOneUnlockedCommon}",
        RelicAuthorityEvidenceCode = "Beta110C2SyntheticRelicBagParity",
        SnapshotFingerprint = $"beta110-relic-c2-snapshot:{characterKey.Serialized}:{modded}:{sharedCommonCount}:{removeOneUnlockedCommon}"
    };
    return source with
    {
        CapturedProfileId = RuntimeProfileId.Beta110,
        CatalogFingerprint = modern.CatalogFingerprint,
        SnapshotFingerprint = modern.SnapshotFingerprint,
        Beta109Generation = modern
    };
}

static int ReadInt(string[] values, string name, int fallback)
{
    int index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length && int.TryParse(values[index + 1], out int parsed) ? parsed : fallback;
}
