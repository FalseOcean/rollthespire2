using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Core.Neow;

// Builds domain facts without constructing a display document or section.
internal static class NeowChoiceFacts
{
    // Omitted observations retain no effect or continuation proof. Offer identity
    // remains available without invoking an unrelated effect implementation.
    internal static NeowEffectProjection UnrequestedNeowEffects => new(
        NeowEffectImplementationStatus.Implemented,
        PredictionPrecision.DescriptionOnly,
        Array.Empty<PredictedEffectGroup>(),
        Array.Empty<PredictionWarning>(),
        "search.neow-effects-not-requested",
        FullEffectSemanticsCompleteness: FullEffectSemanticsCompleteness.NotEvaluated,
        ProductRelevantProjectionStatus: ProductRelevantProjectionStatus.NotEvaluatedByPolicy);

    public static NeowChoiceResult Choice(
        int slotIndex,
        ModelKey relicKey,
        SeedPredictionRequest request,
        PredictionPrecision identityPrecision,
        EvidenceCode identityEvidenceCode,
        NeowEffectProjection effectProjection)
    {
        ArgumentNullException.ThrowIfNull(effectProjection);

        var warnings = new List<PredictionWarning>(effectProjection.Warnings);
        if (identityPrecision != PredictionPrecision.Exact)
        {
            warnings.Add(new PredictionWarning(
                PredictionWarningCode.SnapshotAuthorityIncomplete,
                relicKey,
                "identity-authority"));
        }

        return new NeowChoiceResult(
            slotIndex,
            relicKey,
            identityPrecision,
            effectProjection.Capability,
            effectProjection.Precision,
            effectProjection.EffectGroups,
            warnings,
            identityEvidenceCode,
            effectProjection.EvidenceCode,
            new ModelSourceMetadata(
                IsVanilla: request.Authority.IsVanilla,
                SourceModId: request.Authority.SourceModId,
                SourceAssembly: request.Authority.SourceAssembly,
                SourceAuthority: request.Authority.SourceAuthority,
                ResolutionStatus: request.Authority.ResolutionStatus),
            effectProjection.BonesOutcome,
            effectProjection.ProductRelevantProjectionPrecision,
            effectProjection.FullEffectSemanticsCompleteness,
            effectProjection.ProductRelevantProjectionStatus,
            effectProjection.OpeningRewardContinuations);
    }
}
