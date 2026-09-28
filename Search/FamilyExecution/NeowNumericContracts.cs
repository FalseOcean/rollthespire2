using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

[Flags]
internal enum Beta110FastDomain
{
    None = 0,
    NeowTopIdentity = 1 << 0,
    SelectedRouteIdentity = 1 << 1,
    RequireBones = 1 << 2,
    BonesUnorderedIdentity = 1 << 3,
    BonesAcquisitionRouteMask = 1 << 4,
    CapsuleNestedRelics = 1 << 5,
    KaleidoscopeOffers = 1 << 6,
    ScrollBoxesOffers = 1 << 7,
    FinalCurse = 1 << 8,
    ArcaneScrollOffer = 1 << 9,
    HeftyTabletOffer = 1 << 10,
    LeadPaperweightOffer = 1 << 11,
    LostCofferOffer = 1 << 12,
    PhialHolsterPotions = 1 << 13,
    LeafyPoulticeTransforms = 1 << 14,
    NewLeafTransform = 1 << 15,
    RelicSequence = 1 << 16,
    SharedUpFrontPrefix = 1 << 17,
    EventStaticCandidateSequence = 1 << 18,
    BossIdentity = 1 << 19,
    AncientIdentity = 1 << 20,
    AncientOptions = 1 << 21,
    SeaGlassTarget = 1 << 22,
    CombatReward = 1 << 23,
    EventResult = 1 << 24,
    MerchantColorless = 1 << 25,
    ShopRelicSequence = 1 << 26,
    MassiveScrollOffer = 1 << 27
}

[Flags]
internal enum Beta110PossibleRouteMask : uint
{
    None = 0,
    ChoiceSlot1 = 1 << 0,
    ChoiceSlot2 = 1 << 1,
    ChoiceSlot3 = 1 << 2,
    // Historical names: these bits select the pickup/evaluation route relative
    // to the two generated offer positions. They do NOT require a particular
    // generated Bones offer order. A pinned A->B pickup maps to one of these bits
    // after the actual offer permutation is known.
    BonesOfferedOrder = 1 << 3,
    BonesReverseOrder = 1 << 4,
    AllChoiceSlots = ChoiceSlot1 | ChoiceSlot2 | ChoiceSlot3,
    BothBonesOrders = BonesOfferedOrder | BonesReverseOrder
}

internal enum Beta110FastStructuredConditionKind : byte
{
    LargeCapsuleNestedRelics,
    SmallCapsuleNestedRelic,
    KaleidoscopeIndependentOfferTargets,
    ScrollBoxesCardComposition,
    ScrollBoxesTripleClaw,
    ArcaneScrollGeneratedCard,
    HeftyTabletRareOffer,
    LeadPaperweightColorlessOffer,
    LostCofferCardOffer,
    LostCofferPotion,
    PhialHolsterPotions,
    LeafyPoulticeTransforms,
    NewLeafTransform,
    MassiveScrollOffer
}

internal readonly record struct Beta110FastStructuredCondition(
    byte SourceRelicId,
    Beta110FastStructuredConditionKind Kind,
    ushort Target0,
    ushort Target1,
    ushort Target2,
    byte TargetCount,
    bool AllowDuplicateOutputs)
{
    internal bool OrderedKaleidoscope { get; init; }
    internal ushort KaleidoscopeFirstTarget { get; init; } = Beta110FastDenseId.Invalid;
    internal ushort KaleidoscopeSecondTarget { get; init; } = Beta110FastDenseId.Invalid;
    public ushort TargetAt(int index) => index switch
    {
        0 => Target0,
        1 => Target1,
        2 => Target2,
        _ => Beta110FastDenseId.Invalid
    };
}

internal enum Beta110CurseFirstDrawGateDispositionP9 : byte
{
    Disabled = 0,
    PartialDecision = 1,
    TerminalDecision = 2,
    BonesContinuation = 3
}

internal readonly record struct Beta110CurseFirstDrawGateP9(
    byte CurseCandidateCount,
    uint AcceptedCurseOrdinalMask,
    uint RejectedCurseOrdinalMask,
    Beta110CurseFirstDrawGateDispositionP9 Disposition,
    string Reason)
{
    public bool Enabled => Disposition != Beta110CurseFirstDrawGateDispositionP9.Disabled && CurseCandidateCount > 0;
    public bool CanShortCircuitPositiveContinuation =>
        Disposition is Beta110CurseFirstDrawGateDispositionP9.TerminalDecision or
                       Beta110CurseFirstDrawGateDispositionP9.BonesContinuation;
}

internal sealed record Beta110FastNeowAuthority(
    int PlayerSlotIndex,
    int PlayersCount,
    int Ascension,
    bool AllCharacterCardPoolsUnlocked,
    bool ScrollBoxesAllowed,
    bool UsesDefectScrollBoxesRule,
    byte[] BonesEligibleRelicIds,
    byte[] EligibleCurseRelicIds,
    bool CurseIdentityAuthorityExact,
    bool IdentityAuthorityExact,
    bool BonesAuthorityExact,
    Beta110FastEffectCatalog EffectCatalog,
    string AuthorityFingerprint);

internal enum Beta110FastDecision { CandidateReject, CandidateKeep }
