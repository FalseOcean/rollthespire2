using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.Neow;

public sealed record NeowChoiceResult(
    int SlotIndex,
    ModelKey RelicKey,
    PredictionPrecision IdentityPrecision,
    NeowEffectImplementationStatus EffectCapability,
    PredictionPrecision EffectPrecision,
    IReadOnlyList<PredictedEffectGroup> EffectGroups,
    IReadOnlyList<PredictionWarning> Warnings,
    EvidenceCode EvidenceCode,
    EvidenceCode EffectEvidenceCode,
    ModelSourceMetadata SourceMetadata,
    BonesOutcomeAnalysis? BonesOutcome = null,
    PredictionPrecision ProductRelevantProjectionPrecision = PredictionPrecision.Unknown,
    FullEffectSemanticsCompleteness FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Unknown,
    ProductRelevantProjectionStatus ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Unknown,
    OpeningRewardContinuationAnalysis? OpeningRewardContinuations = null);
