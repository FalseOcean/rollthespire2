using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Presentation.Ui1;

public enum Ui1AnalysisState
{
    Empty,
    Loading,
    Success,
    Partial,
    Unknown,
    Unsupported,
    Error
}

public sealed record PredictedEffectItemViewModel(
    int ItemOrder,
    PredictedEffectKind Kind,
    GameContentDisplayViewModel? TargetContent,
    string DisplayText,
    string CompactDisplayText,
    string Tooltip,
    int Multiplicity,
    int? Amount,
    PredictionPrecision Precision,
    string PrecisionLabel,
    ModelKey? SourceKey,
    string? OfferItemId,
    bool ShowInNormalMode,
    string EffectNodeId,
    string? ParentEffectNodeId,
    PredictedEffectRelation Relation,
    bool IsProductRelevant,
    bool IsPlayerChoiceEffect,
    EffectPresentationDetailLevel NormalViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectCompactSummaryKind CompactSummaryKind = EffectCompactSummaryKind.None)
{
    public ModelKey? TargetKey => TargetContent?.ModelKey;
    public GameContentKind? ContentKind => TargetContent?.ContentKind;
}

public sealed record PredictedEffectGroupViewModel(
    string GroupId,
    int GroupOrder,
    string DisplayLabel,
    EffectSelectionPolicy SelectionPolicy,
    string SelectionPolicyLabel,
    EffectPredictionScope Scope,
    int? RequiredSelectionCount,
    string? SelectionSetId,
    string? BundleId,
    int? BundleOrder,
    IReadOnlyList<PredictedEffectItemViewModel> OrderedItems,
    PredictionPrecision Precision,
    string PrecisionLabel,
    ModelKey? SourceRelicKey,
    PredictedEffectPhase Phase,
    bool ShowInNormalMode,
    bool IsProductRelevant,
    bool IsPlayerChoiceGroup,
    EffectPresentationDetailLevel NormalViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectPresentationDetailLevel DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
    EffectCompactSummaryKind CompactSummaryKind = EffectCompactSummaryKind.None,
    string CompactSummaryText = "");

public sealed record BonesRelicScopedResultViewModel(
    GameContentDisplayViewModel RelicDisplay,
    PredictionPrecision ProductRelevantProjectionPrecision,
    string ProductPrecisionLabel,
    FullEffectSemanticsCompleteness FullEffectSemanticsCompleteness,
    IReadOnlyList<PredictedEffectGroupViewModel> EffectGroups)
{
    public ModelKey SourceRelicKey => RelicDisplay.ModelKey;
    public string DisplayName => RelicDisplay.DisplayName;
}

public sealed record BonesOutcomeGroupViewModel(
    string GroupId,
    string RepresentativeRouteId,
    IReadOnlyList<string> EquivalentRouteIds,
    AcquisitionOrderImpact Impact,
    string ImpactLabel,
    RouteProjectionStatus RouteProjectionStatus,
    string RouteProjectionLabel,
    PredictionPrecision RouteProjectionPrecision,
    PredictionPrecision FinalCursePrecision,
    GameContentDisplayViewModel? FinalCurseDisplay,
    IReadOnlyList<string> RouteWarningLabels,
    string Title,
    IReadOnlyList<string> AcquisitionOrderLabels,
    IReadOnlyList<BonesRelicScopedResultViewModel> RelicResults,
    IReadOnlyList<PredictedEffectGroupViewModel> SharedContinuationGroups,
    string SharedContinuationLabel,
    IReadOnlyList<string> DifferenceEvidenceLabels,
    int OriginalRouteCount);

public sealed record PlayerChoiceFinalCurseViewModel(
    GameContentDisplayViewModel Content,
    PredictionPrecision Precision,
    string PrecisionLabel,
    string HeaderLabel,
    string SummaryLabel);

public sealed record PlayerChoiceOutcomeViewModel(
    string OutcomeId,
    string PolicyLabel,
    GameContentDisplayViewModel ChoiceRelic,
    IReadOnlyList<PredictedEffectGroupViewModel> ChoiceGroups,
    IReadOnlyList<PredictedEffectGroupViewModel> AutomaticEffectGroups,
    PlayerChoiceFinalCurseViewModel? FinalCurse,
    PredictionPrecision FinalCursePrecision,
    string FinalCursePrecisionLabel,
    string EquivalentSelectionLabel,
    SourceAuthority ProductRelevantAuthority,
    SnapshotCompleteness ProductRelevantCompleteness,
    EffectPredictionScope PredictionScope,
    int EquivalentSelectionCount,
    int RawLegalSelectionCount,
    int DistinctDeckStateCount)
{
    public string ChoiceRelicName => ChoiceRelic.DisplayName;
    public string? FinalCurseName => FinalCurse?.Content.DisplayName;
    public string? FinalCurseDisplayName => FinalCurse?.Content.DisplayName;
    public ModelKey? FinalCurseModelKey => FinalCurse?.Content.ModelKey;
    public GameContentIconBindingViewModel? FinalCurseIconBinding => FinalCurse?.Content.IconBinding;
    public string FinalCurseLabel => FinalCurse?.SummaryLabel ?? string.Empty;
}

public sealed record PlayerChoiceRouteSetViewModel(
    string RouteSetId,
    string AcquisitionOrderLabel,
    GameContentDisplayViewModel ChoiceRelic,
    int RawLegalSelectionCount,
    int DistinctDeckStateCount,
    string CountsLabel,
    IReadOnlyList<PlayerChoiceOutcomeViewModel> DistinctOutcomes)
{
    public string ChoiceRelicName => ChoiceRelic.DisplayName;
}

public sealed record PlayerChoiceImpactViewModel(
    string Title,
    IReadOnlyList<PlayerChoiceRouteSetViewModel> RouteSets);

public sealed record BonesOutcomeViewModel(
    AcquisitionOrderImpact OverallImpact,
    string OverallImpactLabel,
    OrderComparisonStatus OrderComparisonStatus,
    string OrderComparisonLabel,
    IReadOnlyList<string> OrderComparisonEvidenceLabels,
    string ResultsCountLabel,
    IReadOnlyList<BonesOutcomeGroupViewModel> OutcomeGroups,
    PlayerChoiceImpactViewModel? PlayerChoiceImpact);


public sealed record OpeningRoutePresentationViewModel(
    string RouteId,
    int RouteOrder,
    OpeningRewardRouteKind RouteKind,
    ModelKey RootRelicKey,
    IReadOnlyList<ModelKey> AcquisitionOrder,
    string Label);

public enum NeowPredictionPresentationState : byte
{
    NoAdditionalPredictionNeeded = 0,
    Predicted = 1,
    PredictionUnavailable = 2
}

public sealed record NeowChoiceViewModel(
    int SlotIndex,
    GameContentDisplayViewModel RelicDisplay,
    PredictionPrecision IdentityPrecision,
    string IdentityLabel,
    NeowEffectImplementationStatus EffectCapability,
    PredictionPrecision EffectPrecision,
    string EffectLabel,
    PredictionPrecision ProductRelevantProjectionPrecision,
    string ProductProjectionLabel,
    ProductRelevantProjectionStatus ProductRelevantProjectionStatus,
    FullEffectSemanticsCompleteness FullEffectSemanticsCompleteness,
    IReadOnlyList<string> UserWarnings,
    string EvidenceCode,
    string EffectEvidenceCode,
    IReadOnlyList<PredictedEffectGroupViewModel> EffectGroups,
    BonesOutcomeViewModel? BonesOutcome,
    IReadOnlyList<OpeningRoutePresentationViewModel> OpeningRoutes,
    NeowPredictionPresentationState PredictionState,
    bool HasNormalModePredictionContent)
{
    public ModelKey RelicKey => RelicDisplay.ModelKey;
    public string DisplayName => RelicDisplay.DisplayName;
    public string Tooltip => RelicDisplay.Tooltip;
}


public sealed record BossPredictionViewModel(
    int Act,
    int Ordinal,
    GameContentDisplayViewModel BossDisplay,
    PredictionPrecision IdentityPrecision,
    string IdentityLabel,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCount,
    string EvidenceCode);

public sealed record AncientOptionPredictionViewModel(
    int Ordinal,
    GameContentDisplayViewModel OptionDisplay,
    PredictionPrecision OptionPrecision,
    string PrecisionLabel,
    string EvidenceCode,
    string? VariantId,
    GameContentDisplayViewModel? CharacterTargetDisplay = null,
    PredictionPrecision? CharacterTargetPrecision = null,
    string CharacterTargetLabel = "",
    string CharacterTargetIssueCode = "");

public sealed record AncientPredictionViewModel(
    int Act,
    GameContentDisplayViewModel AncientDisplay,
    PredictionPrecision IdentityPrecision,
    string IdentityLabel,
    PredictionPrecision OptionPrecision,
    string OptionPrecisionLabel,
    AncientOptionsEvaluationStatus OptionsEvaluationStatus,
    string OptionIssueCode,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string IdentityRngStream,
    int IdentityRngCallCount,
    string OptionRngStream,
    int OptionRngCallCount,
    IReadOnlyList<AncientOptionPredictionViewModel> Options,
    string IdentityEvidenceCode,
    string OptionEvidenceCode);

public sealed record EventPoolSequenceEntryViewModel(
    int Ordinal,
    int RawOrdinal,
    int SourceOrdinal,
    GameContentDisplayViewModel EventDisplay,
    EventPoolSourceKind Source,
    string SourceLabel,
    IReadOnlyList<string> EpochIds,
    PredictionPrecision Precision,
    string PrecisionLabel,
    string EvidenceCode,
    EventCandidateEligibilityKind EligibilityKind,
    string EligibilityLabel);

public sealed record EventPoolRawSequenceEntryViewModel(
    int RawOrdinal,
    int SourceOrdinal,
    GameContentDisplayViewModel EventDisplay,
    EventPoolSourceKind Source,
    string SourceLabel,
    IReadOnlyList<string> EpochIds,
    PredictionPrecision Precision,
    string PrecisionLabel,
    string EvidenceCode);

public sealed record EventPoolActSequenceViewModel(
    int Act,
    ModelKey ActKey,
    string ActLabel,
    IReadOnlyList<EventPoolSequenceEntryViewModel> Entries,
    IReadOnlyList<EventPoolRawSequenceEntryViewModel> RawEntries,
    int RawActLocalCount,
    int RawSharedCount,
    int EligibleCount,
    int EffectiveCount,
    int FilteredOutCount,
    int OpeningAncientCursorOffset,
    int OpeningAncientSkippedCount,
    int StaticFilteredOutCount,
    int DuplicateFilteredOutCount,
    IReadOnlyList<EventEpochFilterEvidence> EpochFilters,
    IReadOnlyList<EventStaticExclusionEvidence> StaticExclusions,
    PredictionPrecision Precision,
    string PrecisionLabel,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCountBefore,
    int RngCallCountAfter,
    string EvidenceCode);

public sealed record RelicSequenceEntryViewModel(
    int Position,
    GameContentDisplayViewModel RelicDisplay,
    PredictionPrecision Precision,
    string PrecisionLabel,
    string EvidenceCode);

public sealed record RelicSequenceLaneViewModel(
    RelicSequenceKind Kind,
    string RarityCode,
    string Title,
    RelicSequencePullDirection PullDirection,
    string PullDirectionLabel,
    IReadOnlyList<RelicSequenceEntryViewModel> Entries,
    IReadOnlyList<RelicSequenceEntryViewModel> TailEntries,
    int TotalCount,
    PredictionPrecision Precision,
    string PrecisionLabel,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCount,
    string EvidenceCode)
{
    public IReadOnlyList<RelicSequenceEntryViewModel> FullEntries { get; init; } = [];
}

public sealed record NormalCombatRewardCardViewModel(
    int Ordinal,
    GameContentDisplayViewModel CardDisplay,
    EffectCardRarity Rarity,
    NormalCombatCardUpgradeState UpgradeState,
    IReadOnlyList<NormalCombatCardEnchantment> Enchantments,
    string DisplaySuffix,
    PredictionPrecision Precision,
    string PrecisionLabel,
    string EvidenceCode);

public sealed record NormalCombatRewardGoldRewardViewModel(
    int RewardOrdinal,
    int Amount,
    bool IsBaseReward,
    bool RngCallConsumed,
    RewardImpactSourceKey? Source,
    string Label,
    string EvidenceCode);

public sealed record NormalCombatRewardPotionViewModel(
    bool Generated,
    GameContentDisplayViewModel? PotionDisplay,
    EffectPotionRarity? Rarity,
    PredictionPrecision Precision,
    string PrecisionLabel,
    string EvidenceCode);

public sealed record NormalCombatRewardCardRewardViewModel(
    int RewardOrdinal,
    bool IsFromCombat,
    IReadOnlyList<NormalCombatRewardCardViewModel> Cards,
    string EvidenceCode);

public sealed record NormalCombatRewardBattleViewModel(
    int BattleOrdinal,
    int Act,
    ModelKey ActKey,
    string Title,
    string ScopeLabel,
    int? Gold,
    NormalCombatGoldProjectionStatus GoldStatus,
    bool GoldRngCallConsumed,
    string GoldLabel,
    NormalCombatRewardPotionViewModel Potion,
    IReadOnlyList<NormalCombatRewardCardViewModel> Cards,
    float PotionOddsBefore,
    float PotionOddsAfter,
    float CardRarityOffsetBefore,
    float CardRarityOffsetAfter,
    PredictionPrecision Precision,
    string PrecisionLabel,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    string RngStream,
    int RngCallCountBefore,
    int RngCallCountAfter,
    string ReasonCode,
    IReadOnlyList<NormalCombatRewardConditionalAssumption> ConditionalAssumptions,
    string EvidenceCode)
{
    public IReadOnlyList<NormalCombatRewardCardRewardViewModel> CardRewards { get; init; } =
        Array.Empty<NormalCombatRewardCardRewardViewModel>();

    public IReadOnlyList<NormalCombatRewardGoldRewardViewModel> GoldRewards { get; init; } =
        Array.Empty<NormalCombatRewardGoldRewardViewModel>();

    public IReadOnlyList<RewardImpactSourceKey> AppliedRewardImpactSources { get; init; } =
        Array.Empty<RewardImpactSourceKey>();

    public string RewardImpactFingerprint { get; init; } = string.Empty;
}


public sealed record NormalCombatRewardRouteViewModel(
    string RouteGroupId,
    string RouteLabel,
    IReadOnlyList<string> EquivalentRouteLabels,
    SeedDomainEvaluationStatus Status,
    string IssueCode,
    int RewardsDrawCount,
    string RewardContextFingerprint,
    string ContinuationFingerprint,
    OpeningRewardSupportFlags Capabilities,
    IReadOnlyList<string> UnknownReasonCodes,
    PredictionPrecision Precision,
    string PrecisionLabel,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    IReadOnlyList<NormalCombatRewardBattleViewModel> Battles,
    string EvidenceCode)
{
    public IReadOnlyList<OpeningRoutePresentationViewModel> EquivalentRouteBindings { get; init; } =
        Array.Empty<OpeningRoutePresentationViewModel>();

    public IReadOnlyList<RewardImpactSourceKey> ActiveRewardImpactSources { get; init; } =
        Array.Empty<RewardImpactSourceKey>();

    public string RewardImpactFingerprint { get; init; } = string.Empty;
}

public sealed record SeedDomainViewModel<T>(
    SeedDomainEvaluationStatus Status,
    string IssueCode,
    IReadOnlyList<T> Items);

public sealed record EncounterSequenceEntryViewModel(int Ordinal, GameContentDisplayViewModel EncounterDisplay);
public sealed record ActEncounterSequenceViewModel(int Act, ModelKey ActKey,
    IReadOnlyList<EncounterSequenceEntryViewModel> Normal, IReadOnlyList<EncounterSequenceEntryViewModel> Elite,
    PredictionPrecision Precision, string IssueCode);

public sealed record SeedAnalysisViewModel(
    Ui1AnalysisState State,
    string CanonicalSeed,
    string CharacterName,
    ModelKey CharacterKey,
    int Ascension,
    int PlayersCount,
    int PlayerSlotIndex,
    RuntimeProfileId ProfileId,
    string GameVersion,
    string RequestId,
    string UnlockSummary,
    string ContextSummary,
    string StatusLabel,
    IReadOnlyList<NeowChoiceViewModel> NeowChoices,
    IReadOnlyList<string> UserWarnings)
{
    public IReadOnlyList<ActEncounterSequenceViewModel> EncounterSequences { get; init; } = [];
    public IReadOnlyList<string> OpeningWarnings { get; init; } = Array.Empty<string>();
    public SeedDomainViewModel<BossPredictionViewModel> BossDomain { get; init; } =
        new(SeedDomainEvaluationStatus.Unknown, "BossSectionMissing", Array.Empty<BossPredictionViewModel>());
    public SeedDomainViewModel<AncientPredictionViewModel> AncientDomain { get; init; } =
        new(SeedDomainEvaluationStatus.Unknown, "AncientSectionMissing", Array.Empty<AncientPredictionViewModel>());
    public SeedDomainViewModel<EventPoolActSequenceViewModel> EventPoolSequenceDomain { get; init; } =
        new(SeedDomainEvaluationStatus.Unknown, "EventPoolSequenceSectionMissing", Array.Empty<EventPoolActSequenceViewModel>());
    public SeedDomainViewModel<RelicSequenceLaneViewModel> RelicSequenceDomain { get; init; } =
        new(SeedDomainEvaluationStatus.Unknown, "RelicSequenceSectionMissing", Array.Empty<RelicSequenceLaneViewModel>());
    public SeedDomainViewModel<RelicSequenceLaneViewModel> TreasureRoomRelicSequenceDomain { get; init; } =
        new(SeedDomainEvaluationStatus.Unknown, "TreasureRoomRelicSequenceSectionMissing", Array.Empty<RelicSequenceLaneViewModel>());
    public SeedDomainViewModel<NormalCombatRewardRouteViewModel> NormalCombatRewardDomain { get; init; } =
        new(SeedDomainEvaluationStatus.Unknown, "NormalCombatRewardSectionMissing", Array.Empty<NormalCombatRewardRouteViewModel>());
}
