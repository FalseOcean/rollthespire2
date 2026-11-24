using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

/// <summary>
/// Product-facing projection state. This is deliberately separate from full relic
/// semantics: a future combat hook can remain incomplete while the opening seed
/// projection, deck mutation, relevant RNG continuity, and final curse are exact.
/// </summary>
public enum ProductRelevantProjectionStatus
{
    Evaluated,
    NotEvaluatedByPolicy,
    Unsupported,
    Unknown
}

public enum FullEffectSemanticsCompleteness
{
    Complete,
    Partial,
    NotEvaluated,
    Unknown
}

public enum PredictedEffectRelation
{
    Root,
    NestedRelic,
    NestedAutomaticEffect,
    AffectedTarget
}

public enum PlayerChoicePolicyKind
{
    RemoveStrike,
    RemoveDefend,
    RemoveStrikeStrike,
    RemoveStrikeDefend,
    RemoveDefendDefend,
    UpgradeStarterCard,
    TransformStrike,
    TransformDefend,
    ChooseOfferedCard,
    SkipOfferedCard,
    ChooseOfferedCardCombination
}

public sealed record PlayerChoicePolicyDescriptor(
    string PolicyId,
    PlayerChoicePolicyKind Kind,
    IReadOnlyList<ModelKey> SourceCardKeys,
    int SelectionCount,
    IReadOnlyList<int>? SourceUpgradeLevels = null)
{
    public string StableKey => string.Join("|",
        PolicyId,
        Kind,
        SelectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string.Join(",", SourceCardKeys.Select(key => key.Serialized)),
        string.Join(",", SourceUpgradeLevels ?? Array.Empty<int>()));
}

/// <summary>
/// First-stage result after raw legal card-instance selections have been collapsed
/// by product policy, resulting shadow deck, and relevant RNG state.
/// </summary>
public sealed record PlayerChoiceSelectionTrace(
    string TraceId,
    ModelKey ChoiceRelicKey,
    PlayerChoicePolicyDescriptor ChoicePolicy,
    IReadOnlyList<PredictedEffect> ChoiceEffects,
    string ChoiceDeckFingerprint,
    string RelevantRngFingerprint,
    int RawLegalSelectionCount,
    int DistinctDeckStateCount,
    int EquivalentSelectionCount,
    int AcquisitionStepIndex);

/// <summary>
/// Second-stage product outcome after downstream automatic effects and the Bones
/// final curse have been evaluated. UI and Search consume these outcomes, never
/// raw card-instance combinations or UI result groups.
/// </summary>
public sealed record DistinctPlayerChoiceOutcome(
    string OutcomeId,
    string SourceRouteId,
    ModelKey ChoiceRelicKey,
    PlayerChoicePolicyDescriptor ChoicePolicy,
    IReadOnlyList<PredictedEffectGroup> ChoiceEffectGroups,
    IReadOnlyList<PredictedEffectGroup> AutomaticEffectGroups,
    string FinalShadowDeckFingerprint,
    ModelKey? FinalCurseKey,
    PredictionPrecision FinalCursePrecision,
    PredictionPrecision ProductRelevantProjectionPrecision,
    ProductRelevantProjectionStatus ProjectionStatus,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    IReadOnlyList<EvidenceCode> EvidenceCodes,
    SourceAuthority ProductRelevantAuthority,
    SnapshotCompleteness ProductRelevantCompleteness,
    EffectPredictionScope PredictionScope,
    int RawLegalSelectionCount,
    int DistinctDeckStateCount,
    int EquivalentSelectionCount)
{
    public ModelKey? FinalCurseModelKey => FinalCurseKey;
}

public sealed record PlayerChoiceRouteSet(
    string RouteSetId,
    IReadOnlyList<ModelKey> AcquisitionOrder,
    ModelKey ChoiceRelicKey,
    string AcquisitionOrderRouteId,
    int RawLegalSelectionCount,
    int DistinctDeckStateCount,
    IReadOnlyList<DistinctPlayerChoiceOutcome> DistinctOutcomes);

public sealed record PlayerChoiceImpactAnalysis(
    IReadOnlyList<PlayerChoiceRouteSet> RouteSets)
{
    public static PlayerChoiceImpactAnalysis Empty { get; } =
        new(Array.Empty<PlayerChoiceRouteSet>());
}
