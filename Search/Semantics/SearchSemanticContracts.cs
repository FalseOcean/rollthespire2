using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

public enum QueryNormalizationStatus
{
    Legal,
    Impossible
}

public enum SemanticRelationKind
{
    SameFact,
    ParentScoped,
    SharedParent,
    SequenceMembership,
    Implies,
    Conflicts,
    SharedState,
    StateInfluence,
    RouteScoped
}

public enum SemanticFactKind
{
    OpeningRoute,
    OpeningRouteRelicRequirement,
    StructuredOpeningOutput,
    CapsuleNestedRelic,
    RelicGrabBag,
    ActVariant,
    Boss,
    EventSequence,
    EventResult,
    CanonicalMerchantColorless,
    AncientIdentity,
    AncientOption,
    SeaGlassTarget,
    CombatReward,
    RewardInfluenceRelic,
    ResolvedRewardRoute,
    LegacyFlatBossConstraint,
    RelicShopSequence
}

public enum ProjectionFidelity
{
    Exact,
    EquivalentUnderCurrentRuntime,
    LossyButConservative,
    Unsupported
}

public enum ResolvedCombatRewardRouteKind
{
    NotApplicable,
    NeutralNonPerturbingContinuation,
    PinnedOpeningRoute
}

public sealed record SemanticFactRef(
    string FactId,
    SemanticFactKind Kind,
    int? Act = null,
    int? Ordinal = null,
    ModelKey? Key = null);

public sealed record SemanticRelation(
    SemanticRelationKind Kind,
    SemanticFactRef Source,
    SemanticFactRef Target,
    string Reason);

/// <summary>
/// SearchQuery route-local relic identity requirement captured directly from the
/// modern opening-route UI before legacy execution projection. The current UI
/// uses this for Neow's Bones offered relic refinement. It is intentionally a
/// Query fact rather than a legacy compatibility sidecar.
/// </summary>
public enum BonesRouteOrderMode
{
    AnyOrder,
    ExactOrder
}

public sealed record OpeningRouteRelicRequirement(
    ModelKey ParentRouteRelicKey,
    IReadOnlyList<ModelKey> RequiredRelicKeys,
    BonesRouteOrderMode OrderMode = BonesRouteOrderMode.AnyOrder)
{
    public bool IsEmpty =>
        !ParentRouteRelicKey.IsValid ||
        RequiredRelicKeys.Count == 0;
}

/// <summary>
/// SearchQuery parent-scoped representation of the Boss/Map UI. The VariantKey is
/// the selected Act model. Empty Boss predicates mean "no child restriction";
/// the parent Variant requirement still remains part of the query.
/// </summary>
public sealed record VariantScopedBossBranch(
    int Act,
    ModelKey VariantKey,
    ModelKeySetFilter FirstBoss,
    ModelKeySetFilter SecondBoss,
    bool IncludesSecondBoss)
{
    public bool IsValid =>
        Act is >= 1 and <= 3 &&
        VariantKey.IsValid;
}

/// <summary>
/// Compatibility-only constraints whose modern player intent was already lost
/// before the Semantic Spine existed. They are preserved verbatim; adapters must
/// not invent parent/branch attribution from them.
/// </summary>
public sealed record LegacyWorldSemanticConstraints(
    IReadOnlyList<ActModelKeySetFilter> BossFilters,
    IReadOnlyList<ActOrdinalModelKeySetFilter> BossOrdinalFilters,
    IReadOnlyList<ActModelKeySetFilter> AncientIdentityFilters,
    IReadOnlyList<ActModelKeySetFilter> AncientOptionFilters,
    IReadOnlyList<ActModelKeySetFilter> AncientSeaGlassTargetFilters)
{
    public static LegacyWorldSemanticConstraints Empty { get; } = new(
        Array.Empty<ActModelKeySetFilter>(),
        Array.Empty<ActOrdinalModelKeySetFilter>(),
        Array.Empty<ActModelKeySetFilter>(),
        Array.Empty<ActModelKeySetFilter>(),
        Array.Empty<ActModelKeySetFilter>());
}

public sealed record LegacyNeowSemanticConstraints(
    ModelKeySetFilter NeowRelics,
    bool RequireNeowsBones,
    ModelKeySetFilter BonesRelics,
    IReadOnlyList<ModelKey> RequiredBonesCombination,
    bool RequireSmallCapsule,
    bool RequireLargeCapsule,
    ModelKeySetFilter CapsuleContainedRelics,
    bool RequireWhetstone,
    bool RequireWarPaint,
    ModelKey? RequiredFinalCurse,
    IReadOnlyList<ModelKey> BannedFinalCurses,
    NeowSearchPreset Preset,
    IReadOnlyList<ModelKey> RequiredBonesAcquisitionOrder,
    IReadOnlyList<NeowEffectOutputSearchCondition> EffectOutputConditions)
{
    public static LegacyNeowSemanticConstraints Empty { get; } = new(
        ModelKeySetFilter.Empty,
        false,
        ModelKeySetFilter.Empty,
        Array.Empty<ModelKey>(),
        false,
        false,
        ModelKeySetFilter.Empty,
        false,
        false,
        null,
        Array.Empty<ModelKey>(),
        NeowSearchPreset.None,
        Array.Empty<ModelKey>(),
        Array.Empty<NeowEffectOutputSearchCondition>());
}

/// <summary>
/// Typed canonical Search query. Existing domain contracts are adopted where
/// their semantics are already suitable; the major new structure is the
/// VariantScopedBossBranch set. Compatibility sidecars preserve legacy facts
/// without pretending that lost modern intent can be reconstructed.
/// </summary>
public sealed record PlayerOfferQuery(int Slot, ModelKeySetFilter Offers)
{
    public SearchQuery Conditions { get; init; } = SearchQuery.Empty;
    public AncientOptionConditionProfile AncientPremises { get; init; } = AncientOptionConditionProfile.BroadDefault;
    public Core.Effects.PartyNeowPlan? SelectedOption { get; init; }
    public IReadOnlyList<NeowStructuredEffectSearchCondition> Results { get; init; } = [];
}

public sealed record SearchQuery(
    NeowRouteSearchCondition? OpeningRoute,
    OpeningRouteRelicRequirement? OpeningRouteRelicRequirement,
    IReadOnlyList<NeowStructuredEffectSearchCondition> StructuredOpeningEffects,
    IReadOnlyList<VariantScopedBossBranch> VariantBossBranches,
    IReadOnlyList<AncientSearchBranchCondition> AncientBranches,
    IReadOnlyList<RelicSequenceSearchCondition> RelicSequenceConstraints,
    IReadOnlyList<EventSequenceSearchCondition> EventSequenceConstraints,
    IReadOnlyList<NormalCombatRewardSearchCondition> LegacyCombatRewardConstraints,
    LegacyNeowSemanticConstraints LegacyNeow,
    LegacyWorldSemanticConstraints LegacyWorld)
{
    public IReadOnlyList<PlayerOfferQuery> Players { get; init; } = Array.Empty<PlayerOfferQuery>();
    public static SearchQuery Empty { get; } = new(
        null,
        null,
        Array.Empty<NeowStructuredEffectSearchCondition>(),
        Array.Empty<VariantScopedBossBranch>(),
        Array.Empty<AncientSearchBranchCondition>(),
        Array.Empty<RelicSequenceSearchCondition>(),
        Array.Empty<EventSequenceSearchCondition>(),
        Array.Empty<NormalCombatRewardSearchCondition>(),
        LegacyNeowSemanticConstraints.Empty,
        LegacyWorldSemanticConstraints.Empty);

    public IReadOnlyList<StandardMapSearchCondition> StandardMaps { get; init; } = Array.Empty<StandardMapSearchCondition>();

    public CombatCardRewardSequenceSearchCondition? CombatCardRewards { get; init; }
    public TransformationAggregateCondition? TransformationAggregate { get; init; }
    public CombatPotionRewardSequenceSearchCondition? CombatPotionRewards { get; init; }
    public IReadOnlyList<EventResultSearchCondition> EventResultConditions { get; init; } =
        Array.Empty<EventResultSearchCondition>();
    public IReadOnlyList<MerchantColorlessSlotCondition> MerchantColorlessConditions { get; init; } =
        Array.Empty<MerchantColorlessSlotCondition>();
    public IReadOnlyList<MerchantColorlessSequenceSearchCondition> MerchantColorlessSequenceConditions { get; init; } =
        Array.Empty<MerchantColorlessSequenceSearchCondition>();
    public IReadOnlyList<RelicShopSequenceSearchCondition> RelicShopSequenceConditions { get; init; } =
        Array.Empty<RelicShopSequenceSearchCondition>();

    public bool HasCombatRewardConstraints =>
        CombatCardRewards is { IsEmpty: false } ||
        CombatPotionRewards is { IsEmpty: false } ||
        LegacyCombatRewardConstraints.Any(condition => !condition.IsEmpty);
}

public sealed record SearchEvaluationAssumptions(
    AncientOptionConditionProfile AncientEligibilityAssumptions)
{
    public static SearchEvaluationAssumptions BroadDefault { get; } = new(
        AncientOptionConditionProfile.BroadDefault);
}

/// <summary>
/// Semantic interpretation context. Ancient eligibility assumptions are player
/// supplied context, not SearchQuery predicates. Runtime fingerprints
/// identify the immutable eligibility world without pulling execution knobs into
/// semantic identity.
/// </summary>
public sealed record SearchContext(
    RuntimeProfileId ProfileId,
    ModelKey CharacterKey,
    int Ascension,
    RuntimeContextAuthoritySnapshot Authority,
    GameVersionDetection Detection,
    SearchEvaluationAssumptions EvaluationAssumptions)
{
    public OrderedPartyAuthority? Party { get; init; }
    public string UnlockSnapshotFingerprint => Authority.UnlockSnapshotFingerprint;
    public string CatalogFingerprint => Authority.CatalogFingerprint;
    public string EffectSnapshotFingerprint => Authority.EffectSnapshotFingerprint;
    public string WorldSnapshotFingerprint => Authority.WorldSnapshotFingerprint;
}

public sealed record QueryNormalizationResult(
    SearchQuery NormalizedQuery,
    QueryNormalizationStatus Status,
    IReadOnlyList<string> RemovedRedundancies,
    IReadOnlyList<string> ImpliedConstraints,
    IReadOnlyList<SemanticRelation> Relations,
    IReadOnlyList<string> Diagnostics);

public sealed record ResolvedCombatRewardRouteSemantics(
    ResolvedCombatRewardRouteKind Kind,
    ModelKey? PinnedOpeningRouteKey,
    bool OpeningRouteIsQueryFact,
    string ProductPolicyId);

public sealed record ResolvedRouteSemantics(
    ResolvedCombatRewardRouteSemantics CombatReward);

public sealed record LegacySearchCriteriaProjection(
    NeowSearchFilter Filter,
    ProjectionFidelity Fidelity,
    IReadOnlyList<string> Diagnostics);

public sealed record ExactSearchEvaluationProjectionResult(
    ExactSearchEvaluationProjection Evaluation,
    ProjectionFidelity Fidelity,
    IReadOnlyList<string> Diagnostics);

public sealed record CompiledSearch(
    SearchQuery Query,
    SearchContext Context,
    QueryNormalizationResult Normalization,
    ResolvedRouteSemantics ResolvedRouteSemantics,
    bool RequiresComplexBonesDeckInteractionEvaluation,
    string SemanticFingerprint)
{
    internal IReadOnlyList<CompiledSearch> PlayerSearches { get; init; } = [];
    internal bool UsesPartyFamilyProjection { get; init; }
    public SearchQuery NormalizedQuery => Normalization.NormalizedQuery;
    public IReadOnlyList<SemanticRelation> Relations => Normalization.Relations;
    public QueryNormalizationStatus Status => Normalization.Status;
}

/// <summary>
/// Product-owned semantic policy. Consumer-specific Fast/Exact/Probability route
/// contracts are downstream projections of this semantic decision, never parallel
/// authorities. SearchCompiler resolves it before the Planner boundary.
/// </summary>
public sealed record ProductSemanticPolicy(
    string PolicyId,
    ResolvedCombatRewardRouteKind UnpinnedCombatRewardRoute)
{
    public static ProductSemanticPolicy Current { get; } = new(
        "SearchSemantics.2026-08-11",
        ResolvedCombatRewardRouteKind.NeutralNonPerturbingContinuation);
}
