using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

public enum PredictedEffectKind
{
    AddCard,
    RemoveCard,
    UpgradeCard,
    TransformCard,
    AddRelic,
    AddPotion,
    AttemptAddPotion,
    ChangeGold,
    SetGold,
    ChangeMaxHp,
    ChangeCurrentHp,
    ChangePotionCapacity,
    FutureRuntimeEffect,
    DescriptionOnly,
    AutomaticEffect
}

public enum EffectSelectionPolicy
{
    ForcedObtain,
    OptionalClaim,
    ChooseExactlyOne,
    ChooseOneOrSkip,
    ChooseExactlyN,
    ChooseAny,
    NoPlayerChoice
}

public enum EffectGameSelectionPolicy
{
    Unspecified,
    NoPlayerChoice,
    ForcedObtain,
    OptionalObtain,
    PlayerChoice
}

public enum EffectRouteEnumerationPolicy
{
    FollowGameSelection,
    AssumeObtainOnly
}

public enum EffectPredictionDomain
{
    Neow
}

public enum EffectPredictionScope
{
    ImmediateOptionEffect,
    FinitePlayerChoiceRoutes,
    NestedObtain,
    FutureRuntimeEffect
}

public enum PredictedEffectPhase
{
    RelicAcquisition,
    RelicImmediateEffect,
    NestedObtain,
    SharedContinuation,
    FutureRuntime
}

public enum EffectPresentationDetailLevel
{
    Hidden,
    CompactSummary,
    Detailed
}

public enum EffectCompactSummaryKind
{
    None,
    Applied,
    FixedStarterPair,
    PlayerChoiceDeckMutation
}

/// <summary>
/// One structured effect item. SourceKey is populated for transforms and other
/// source-to-target mutations. OfferItemId keeps multiple effects belonging to
/// one selectable reward together without flattening their semantics.
/// </summary>
public sealed record PredictedEffect(
    PredictedEffectKind Kind,
    ModelKey? TargetKey,
    int? Amount,
    PredictionPrecision Precision,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    EvidenceCode EvidenceCode,
    int ItemOrder = 0,
    int Multiplicity = 1,
    string SourceStep = "",
    ModelKey? SourceKey = null,
    string? OfferItemId = null,
    ModelKey? SourceRelicKey = null,
    string? SourceEffectGroupId = null,
    PredictedEffectPhase Phase = PredictedEffectPhase.RelicImmediateEffect,
    string EffectNodeId = "",
    string? ParentEffectNodeId = null,
    PredictedEffectRelation Relation = PredictedEffectRelation.Root,
    bool IsProductRelevant = true,
    bool IsPlayerChoiceEffect = false,
    EffectPresentationDetailLevel NormalViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectCompactSummaryKind CompactSummaryKind = EffectCompactSummaryKind.None)
{
    public bool HasGlam { get; init; }
}

/// <summary>
/// Ordered effect group with an explicit player-selection contract.
/// SelectionSetId identifies alternative groups (for example ScrollBoxes
/// bundles or route branches) while BundleId preserves one reward bundle.
/// </summary>
public sealed record PredictedEffectGroup(
    string GroupId,
    int GroupOrder,
    EffectSelectionPolicy SelectionPolicy,
    EffectPredictionDomain Domain,
    EffectPredictionScope Scope,
    string SourceStep,
    IReadOnlyList<PredictedEffect> OrderedItems,
    int? RequiredSelectionCount = null,
    string? SelectionSetId = null,
    string? BundleId = null,
    int? BundleOrder = null,
    ModelKey? SourceRelicKey = null,
    string? SourceEffectGroupId = null,
    PredictedEffectPhase Phase = PredictedEffectPhase.RelicImmediateEffect,
    string? RngEvidenceCode = null,
    EffectGameSelectionPolicy GameSelectionPolicy = EffectGameSelectionPolicy.Unspecified,
    EffectRouteEnumerationPolicy RouteEnumerationPolicy = EffectRouteEnumerationPolicy.FollowGameSelection,
    IReadOnlyList<EvidenceCode>? PolicyEvidenceCodes = null,
    bool IsProductRelevant = true,
    bool IsPlayerChoiceGroup = false,
    EffectPresentationDetailLevel NormalViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectCompactSummaryKind CompactSummaryKind = EffectCompactSummaryKind.None);

public static class AnalysisPrecisionAggregator
{
    public static PredictionPrecision AggregateEffectGroups(IReadOnlyList<PredictedEffectGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            return PredictionPrecision.Unsupported;
        }

        PredictedEffect[] effects = groups
            .OrderBy(group => group.GroupOrder)
            .SelectMany(group => group.OrderedItems.OrderBy(effect => effect.ItemOrder))
            .ToArray();
        return effects.Length == 0
            ? PredictionPrecision.Unsupported
            : AggregateEffects(effects);
    }

    public static PredictionPrecision AggregateEffects(IReadOnlyList<PredictedEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (effects.Count == 0)
        {
            return PredictionPrecision.Unsupported;
        }

        PredictionPrecision first = effects[0].Precision;
        if (effects.All(effect => effect.Precision == first))
        {
            return first;
        }

        return PredictionPrecision.Partial;
    }
}
