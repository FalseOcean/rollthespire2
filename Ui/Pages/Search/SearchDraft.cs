using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.CombatReward;

namespace RolltheSpire2.Ui.Pages.Search;

/// <summary>
/// Player business-query draft only. Run/session controls (start seed, scan range,
/// target count, worker count) deliberately live in SearchRunDraft and never enter
/// SearchQuery/SearchContext/CompiledSearch.
/// </summary>
internal sealed record SearchDraft(
    string NeowAny,
    string NeowAll,
    string NeowBan,
    bool RequireBones,
    string BonesAny,
    string BonesAll,
    string BonesBan,
    bool RequireSmallCapsule,
    bool RequireLargeCapsule,
    string CapsuleRelicAny,
    bool RequireWhetstone,
    bool RequireWarPaint,
    string RequiredFinalCurse,
    string BannedFinalCurses,
    bool ValidationPreset)
{
    public NeowRouteFilterDraft NeowRouteDraft { get; init; } = NeowRouteFilterDraft.Empty;
    public AncientSearchMatrixDraft AncientMatrixDraft { get; init; } = AncientSearchMatrixDraft.Empty;
    public BossMapSearchDraft BossMapDraft { get; init; } = BossMapSearchDraft.Empty;
    public CombatRewardSearchDraft CombatRewardDraft { get; init; } = CombatRewardSearchDraft.Empty;

    public string CapsuleRelicAll { get; init; } = string.Empty;
    public string CapsuleRelicBan { get; init; } = string.Empty;

    public string BonesAcquisitionOrder { get; init; } = string.Empty;
    public string EffectOutputSource { get; init; } = string.Empty;
    public string EffectOutputAny { get; init; } = string.Empty;
    public string EffectOutputAll { get; init; } = string.Empty;
    public string EffectOutputBan { get; init; } = string.Empty;

    public int BossAct { get; init; } = 1;
    public string BossAny { get; init; } = string.Empty;
    public string BossAll { get; init; } = string.Empty;
    public string BossBan { get; init; } = string.Empty;
    public int BossOrdinal { get; init; }
    public string BossOrdinalAny { get; init; } = string.Empty;
    public string BossOrdinalAll { get; init; } = string.Empty;
    public string BossOrdinalBan { get; init; } = string.Empty;

    public int AncientAct { get; init; } = 1;
    public string AncientAny { get; init; } = string.Empty;
    public string AncientAll { get; init; } = string.Empty;
    public string AncientBan { get; init; } = string.Empty;
    public string AncientOptionAny { get; init; } = string.Empty;
    public string AncientOptionAll { get; init; } = string.Empty;
    public string AncientOptionBan { get; init; } = string.Empty;
    public string SeaGlassTargetAny { get; init; } = string.Empty;
    public string SeaGlassTargetAll { get; init; } = string.Empty;
    public string SeaGlassTargetBan { get; init; } = string.Empty;

    // Typed sources. Serialized strings remain compatibility-only fallback for
    // historical/internal draft construction; the modern Search UI populates these
    // lists directly before any text serialization/parsing boundary.
    public IReadOnlyList<RelicSequenceSearchCondition> RelicSequenceDraft { get; init; } =
        Array.Empty<RelicSequenceSearchCondition>();
    public IReadOnlyList<EventSequenceSearchCondition> EventSequenceDraft { get; init; } =
        Array.Empty<EventSequenceSearchCondition>();
    public IReadOnlyList<EventResultSearchCondition> EventResultDraft { get; init; } =
        Array.Empty<EventResultSearchCondition>();
    // Player intent only; current entry-pool authority is bound when compiling.
    public ModelKey? MorphicGroveContainsCard { get; init; }
    public ModelKey? MorphicGroveSecondCard { get; init; }
    // Authoring intent only. Capture current immutable entry pools at compilation.
    public TransformationAggregateCondition? TransformationAggregate { get; init; }
    public IReadOnlyList<MerchantColorlessSlotCondition> MerchantColorlessDraft { get; init; } =
        Array.Empty<MerchantColorlessSlotCondition>();
    public IReadOnlyList<MerchantColorlessSequenceSearchCondition> MerchantColorlessSequenceDraft { get; init; } =
        Array.Empty<MerchantColorlessSequenceSearchCondition>();
    public IReadOnlyList<RelicShopSequenceSearchCondition> RelicShopSequenceDraft { get; init; } =
        Array.Empty<RelicShopSequenceSearchCondition>();
    public string RelicSequenceConditions { get; init; } = string.Empty;
    public string EventSequenceConditions { get; init; } = string.Empty;

    public bool TezcataraHasBasicStrike { get; init; } = true;
    public bool NonupeipeSwiftEnchantableAtLeast4 { get; init; } = true;
    public bool TanxInstinctEnchantableAtLeast3 { get; init; } = true;
    public bool PaelGoopyDefendCardsAtLeast3 { get; init; } = true;
    public bool PaelAllowLegionNoEventPet { get; init; } = true;
    public bool PaelRemovableCardsAtLeast5 { get; init; } = true;
    public bool OrobasArchaicToothConditionMet { get; init; } = true;
    public bool OrobasTouchOfOrobasConditionMet { get; init; } = true;
    public bool DarvAllowPandorasBoxRelicSet { get; init; } = true;
}


/// <summary>
/// UI-local run/session controls. This is not semantic authority and is converted
/// to SearchRunOptions only after CompiledSearch exists.
/// </summary>
internal sealed record SearchRunDraft(
    string StartSeed,
    long ScanCount,
    int TargetMatchCount,
    int WorkerCount);


internal sealed record NeowRouteFilterDraft(
    ModelKey? RouteRelicKey,
    IReadOnlyList<ModelKey> RequiredBonesRelics,
    BonesRouteOrderMode BonesOrderMode,
    IReadOnlyList<NeowStructuredEffectSearchCondition> EffectConditions)
{
    public static NeowRouteFilterDraft Empty { get; } = new(
        null,
        Array.Empty<ModelKey>(),
        BonesRouteOrderMode.AnyOrder,
        Array.Empty<NeowStructuredEffectSearchCondition>());

    internal static NeowRouteFilterDraft FromTransformation(TransformationOpening opening, TransformationPickupOrder order) => new(
        opening switch { TransformationOpening.LeafyPoultice => BaseGameModelKeys.Relics.LeafyPoultice,
            TransformationOpening.NewLeaf => BaseGameModelKeys.Relics.NewLeaf, _ => BaseGameModelKeys.Relics.NeowsBones },
        opening == TransformationOpening.BonesLeafyNewLeaf ? (order == TransformationPickupOrder.NewLeafThenLeafy
            ? [BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.LeafyPoultice]
            : [BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf]) : [],
        order == TransformationPickupOrder.Any ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder, []);
}

internal sealed record AncientSearchRowDraft(
    int Act,
    ModelKey AncientKey,
    bool IsActive,
    IReadOnlyList<ModelKey> SelectedOptionKeys,
    IReadOnlyList<ModelKey> SeaGlassTargetKeys);

internal sealed record AncientSearchMatrixDraft(
    IReadOnlyList<AncientSearchRowDraft> Rows)
{
    public static AncientSearchMatrixDraft Empty { get; } =
        new(Array.Empty<AncientSearchRowDraft>());
}
