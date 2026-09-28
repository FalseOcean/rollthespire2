using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.Effects;

/// <summary>
/// Pure, transactional result returned by a profile-specific Neow effect projector.
/// Precision is the retained legacy/full-projection aggregate. ProductRelevantProjectionPrecision
/// and FullEffectSemanticsCompleteness express the independent product-facing dimensions.
/// </summary>
public sealed record NeowEffectProjection(
    NeowEffectImplementationStatus Capability,
    PredictionPrecision Precision,
    IReadOnlyList<PredictedEffectGroup> EffectGroups,
    IReadOnlyList<PredictionWarning> Warnings,
    EvidenceCode EvidenceCode,
    BonesOutcomeAnalysis? BonesOutcome = null,
    EffectDependencyImpact? DependencyImpact = null,
    PredictionPrecision ProductRelevantProjectionPrecision = PredictionPrecision.Unknown,
    FullEffectSemanticsCompleteness FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Unknown,
    ProductRelevantProjectionStatus ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Unknown,
    OpeningRewardContinuationAnalysis? OpeningRewardContinuations = null)
{
    public static NeowEffectProjection Exact(
        ModelKey relicKey,
        IReadOnlyList<PredictedEffectGroup> groups,
        EvidenceCode evidenceCode)
    {
        ArgumentNullException.ThrowIfNull(groups);
        PredictionPrecision precision = AnalysisPrecisionAggregator.AggregateEffectGroups(groups);
        if (precision != PredictionPrecision.Exact)
        {
            throw new InvalidOperationException(
                $"Exact projection for {relicKey.Serialized} produced aggregate precision {precision}.");
        }

        return new NeowEffectProjection(
            NeowEffectImplementationStatus.Implemented,
            PredictionPrecision.Exact,
            groups,
            Array.Empty<PredictionWarning>(),
            evidenceCode,
            ProductRelevantProjectionPrecision: PredictionPrecision.Exact,
            FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.Complete,
            ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Evaluated);
    }

    public static NeowEffectProjection ProductExactNoVisibleEffects(
        ModelKey relicKey,
        EvidenceCode evidenceCode,
        FullEffectSemanticsCompleteness fullSemantics = FullEffectSemanticsCompleteness.Partial) => new(
        NeowEffectImplementationStatus.Implemented,
        fullSemantics == FullEffectSemanticsCompleteness.Complete
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial,
        Array.Empty<PredictedEffectGroup>(),
        Array.Empty<PredictionWarning>(),
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Exact,
        FullEffectSemanticsCompleteness: fullSemantics,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Evaluated);

    public static NeowEffectProjection Partial(
        ModelKey relicKey,
        IReadOnlyList<PredictedEffectGroup> groups,
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.Implemented,
        PredictionPrecision.Partial,
        groups,
        new[] { new PredictionWarning(warningCode, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Partial,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.Partial,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Evaluated);

    public static NeowEffectProjection DescriptionOnly(
        ModelKey relicKey,
        IReadOnlyList<PredictedEffectGroup> groups,
        EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.Implemented,
        PredictionPrecision.DescriptionOnly,
        groups,
        Array.Empty<PredictionWarning>(),
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Exact,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.Partial,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Evaluated);

    public static NeowEffectProjection NotEvaluatedByPolicy(
        ModelKey relicKey,
        EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.Implemented,
        PredictionPrecision.DescriptionOnly,
        Array.Empty<PredictedEffectGroup>(),
        new[] { new PredictionWarning(PredictionWarningCode.ComplexResultNotEvaluatedByPolicy, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.DescriptionOnly,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.NotEvaluated,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.NotEvaluatedByPolicy);

    public static NeowEffectProjection NotImplemented(ModelKey relicKey, EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.NotImplemented,
        PredictionPrecision.Unsupported,
        Array.Empty<PredictedEffectGroup>(),
        new[] { new PredictionWarning(PredictionWarningCode.EffectNotImplemented, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Unsupported,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.NotEvaluated,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Unsupported);

    public static NeowEffectProjection Unsupported(ModelKey relicKey, EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.UnsupportedEffectType,
        PredictionPrecision.Unsupported,
        Array.Empty<PredictedEffectGroup>(),
        new[] { new PredictionWarning(PredictionWarningCode.EffectTypeUnsupported, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Unsupported,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.Unknown,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Unsupported);

    public static NeowEffectProjection NotApplicable(ModelKey relicKey, EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.NotApplicable,
        PredictionPrecision.Unsupported,
        Array.Empty<PredictedEffectGroup>(),
        new[] { new PredictionWarning(PredictionWarningCode.EffectNotApplicable, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Unsupported,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.NotEvaluated,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Unsupported);

    public static NeowEffectProjection Unknown(
        ModelKey relicKey,
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.Implemented,
        PredictionPrecision.Unknown,
        Array.Empty<PredictedEffectGroup>(),
        new[] { new PredictionWarning(warningCode, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Unknown,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.Unknown,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Unknown);

    public static NeowEffectProjection UnknownAuthority(ModelKey relicKey, EvidenceCode evidenceCode) =>
        Unknown(relicKey, PredictionWarningCode.EffectAuthorityIncomplete, evidenceCode);

    public static NeowEffectProjection Failed(ModelKey relicKey, EvidenceCode evidenceCode) => new(
        NeowEffectImplementationStatus.Implemented,
        PredictionPrecision.Unknown,
        Array.Empty<PredictedEffectGroup>(),
        new[] { new PredictionWarning(PredictionWarningCode.EffectProjectionFailed, relicKey, evidenceCode) },
        evidenceCode,
        ProductRelevantProjectionPrecision: PredictionPrecision.Unknown,
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.Unknown,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.Unknown);
}
