using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.Effects;

public enum AcquisitionOrderImpact
{
    ProvenIndependent,
    ProvenSensitive,
    Indeterminate
}

/// <summary>
/// Product-facing status for one concrete Bones acquisition order. This is
/// intentionally independent from whether the opposite order was evaluated.
/// </summary>
public enum RouteProjectionStatus
{
    Exact,
    Partial,
    NotEvaluatedByPolicy,
    Unknown,
    Unsupported
}

/// <summary>
/// Status of comparing the two Bones acquisition orders. An indeterminate
/// comparison never downgrades either route's own projection status.
/// </summary>
public enum OrderComparisonStatus
{
    ProvenIndependent,
    ProvenSensitive,
    IndeterminateBecausePolicyDisabled,
    IndeterminateBecauseAuthorityMissing,
    IndeterminateBecauseBranchBudget
}

public enum BonesOrderDifferenceCode
{
    RelicScopedResultsDiffer,
    SharedContinuationDiffers,
    FinalCurseDiffers,
    ObservableStateDiffers,
    RelevantContinuationDiffers,
    PrecisionDiffers,
    WarningScopeDiffers,
    AuthorityOrCompletenessDiffers,
    RouteSetDiffers,
    IncompleteProjection,
    UnknownNestedHook,
    BranchBudgetExceeded
}

/// <summary>
/// Effects attributable to one acquired Bones relic. SourceRelicKey is the
/// alignment identity and is never derived from display text or execution order.
/// </summary>
public sealed record BonesRelicScopedResult(
    ModelKey SourceRelicKey,
    IReadOnlyList<PredictedEffectGroup> EffectGroups,
    PredictionPrecision Precision,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    EvidenceCode EvidenceCode,
    string ObservableFingerprint,
    PredictionPrecision ProductRelevantProjectionPrecision = PredictionPrecision.Unknown,
    FullEffectSemanticsCompleteness FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Unknown,
    ProductRelevantProjectionStatus ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Unknown,
    string ProductRelevantObservableFingerprint = "");

/// <summary>
/// Shared work that happens only after both relics have been obtained, including
/// the route-specific final curse. RelevantContinuationFingerprint represents
/// only state that can still affect the current Bones document after relic scope.
/// </summary>
public sealed record BonesSharedContinuationResult(
    IReadOnlyList<PredictedEffectGroup> EffectGroups,
    ModelKey? FinalCurseKey,
    PredictionPrecision Precision,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    SourceAuthority SourceAuthority,
    SnapshotCompleteness Completeness,
    EffectPredictionScope PredictionScope,
    string ObservableStateFingerprint,
    string RelevantContinuationFingerprint,
    EvidenceCode EvidenceCode,
    PredictionPrecision FinalCursePrecision = PredictionPrecision.Unknown,
    BonesDependencyContinuity? DependencyContinuity = null,
    ProductRelevantProjectionStatus ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Unknown);

public sealed record BonesAcquisitionRouteResult(
    string RouteId,
    IReadOnlyList<ModelKey> AcquisitionOrder,
    IReadOnlyList<BonesRelicScopedResult> RelicScopedResults,
    BonesSharedContinuationResult SharedContinuation,
    PredictionPrecision Precision,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    string ObservableStateFingerprint,
    string RelevantContinuationFingerprint,
    BonesDependencyContinuity? DependencyContinuity = null,
    PredictionPrecision ProductRelevantProjectionPrecision = PredictionPrecision.Unknown,
    ProductRelevantProjectionStatus ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Unknown,
    IReadOnlyList<PlayerChoiceSelectionTrace>? PlayerChoiceSelections = null,
    string ProductRelevantObservableFingerprint = "",
    string FinalShadowDeckFingerprint = "",
    RouteProjectionStatus RouteProjectionStatus = RouteProjectionStatus.Unknown,
    EvidenceCode RouteEvidenceCode = default,
    OpeningRewardContinuation? OpeningRewardContinuation = null);

/// <summary>
/// UI-facing grouping evidence. OriginalRoutes remain present even when an exact
/// representative is sufficient for ordinary presentation.
/// </summary>
public sealed record BonesOutcomeGroup(
    string GroupId,
    AcquisitionOrderImpact Impact,
    BonesAcquisitionRouteResult RepresentativeRoute,
    IReadOnlyList<IReadOnlyList<ModelKey>> EquivalentAcquisitionOrders,
    IReadOnlyList<BonesAcquisitionRouteResult> OriginalRoutes,
    IReadOnlyList<BonesOrderDifferenceCode> DifferenceEvidence);

public sealed record BonesOutcomeAnalysis(
    IReadOnlyList<ModelKey> OfferedRelics,
    IReadOnlyList<BonesAcquisitionRouteResult> OriginalRoutes,
    IReadOnlyList<BonesOutcomeGroup> OutcomeGroups,
    AcquisitionOrderImpact OverallImpact,
    PlayerChoiceImpactAnalysis? PlayerChoiceImpact = null,
    OrderComparisonStatus OrderComparisonStatus = OrderComparisonStatus.IndeterminateBecauseAuthorityMissing,
    IReadOnlyList<BonesOrderDifferenceCode>? OrderComparisonEvidence = null)
{
    public IReadOnlyList<BonesOrderDifferenceCode> EffectiveOrderComparisonEvidence =>
        OrderComparisonEvidence ?? Array.Empty<BonesOrderDifferenceCode>();
}
