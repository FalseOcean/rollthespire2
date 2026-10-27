using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Each step names its current obtain source and carries semantic observations.
// Generated instance IDs are deliberately excluded from binding; deck targets
// retain their actual instance, model and floor identity.
internal enum PredictorObtainChildAction
{
    Reward, LeaveRewards, ChooseCard, ChooseManyCards, ChooseDeckCards,
    TransformCard, ChooseBundle, Health, ReplacePotion, CancelPotionReplacement, RemoveRewardCards
}

internal sealed record PredictorPlannedObtainChild(PredictorObtainChildAction Action, ModelKey ExpectedSource)
{
    public PredictorPlannedRewardChoice? Reward { get; init; }
    public ImmutableArray<PredictorPlannedCard> DeckCards { get; init; } = [];
    public ImmutableArray<PredictorCard> ExpectedCards { get; init; } = [];
    public ImmutableArray<int> CardIndices { get; init; } = [];
    public ImmutableArray<ImmutableArray<PredictorCard>> ExpectedBundles { get; init; } = [];
    public int? BundleIndex { get; init; }
    public PredictorEventHealthFact? Health { get; init; }
    public int? PotionSlot { get; init; }
    public PredictorPotion? ExpectedPotion { get; init; }
}

internal sealed record PredictorPlannedAncient(ModelKey ExpectedAncient, PredictorAncientOffer ExpectedOffer)
{
    public ImmutableArray<PredictorPlannedObtainChild> Children { get; init; } = [];
}

// A null offered value means this slot's identity depends on a missing fact.
// Independent identities remain visible even when mutable setup is pending.
internal sealed record PredictorAncientOfferKnowledge(int Index, PredictorAncientOffer? Value,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public ImmutableArray<PredictorCombatPendingCause> IdentityPendingCauses { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> PreparedCardPendingCauses { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> CharacterTargetPendingCauses { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> TargetPendingCauses { get; init; } = [];
    public PredictorCard? StarterCard { get; init; }
    public PredictorCard? ReplacementCard { get; init; }
    public ModelKey? StarterRelic { get; init; }
    public ModelKey? ReplacementRelic { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> ReplacementPendingCauses { get; init; } = [];
    internal bool IsKnown => Value != null && PendingCauses.IsEmpty;
}

internal sealed record PredictorObtainChoiceKnowledge(PredictorPhase Phase, ModelKey Source,
    ImmutableArray<PredictorRouteCardKnowledge> DeckCards,
    ImmutableArray<PredictorCard> Cards,
    ImmutableArray<ImmutableArray<PredictorCard>> Bundles,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public int MinimumCards { get; init; }
    public int MaximumCards { get; init; }
}

internal sealed record PredictorAncientNodeKnowledge(ModelKey Ancient,
    ImmutableArray<PredictorAncientOfferKnowledge> Offers,
    ImmutableArray<PredictorObtainChoiceKnowledge> RequiredChoices,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public bool Advanced { get; init; }
    public int AppliedChildren { get; init; }
    public PredictorAncientOptionalCards? OptionalCards { get; init; }
    public PredictorAncientBundleChoice? BundleChoice { get; init; }
    public PredictorAncientOutcomeKnowledge? Outcome { get; init; }
}

// Actual node-local acquisition results. Generated candidates and ordinary
// reward receipts have separate owners; neither is an acquired result here.
internal sealed record PredictorAncientOutcomeKnowledge(ModelKey Source,
    ImmutableArray<PredictorCard> AddedCards,
    ImmutableArray<PredictorEventCardChangeKnowledge> ChangedCards,
    ImmutableArray<ModelKey> ObtainedRelics, ImmutableArray<ModelKey> RemovedRelics,
    ImmutableArray<PredictorAncientPotionOutcome> Potions);
internal sealed record PredictorAncientPotionOutcome(ModelKey Key, PredictorInputResult? Result);
internal sealed record PredictorAncientBundleChoice(PredictorObtainChoiceKnowledge Choice, int ChildIndex,
    int? SelectedBundle, PredictorAncientOffer AcceptedOffer);

// A real zero-minimum picker completed by the drawing default remains editable
// at its original child position. The full authored simulator has no default.
internal sealed record PredictorAncientOptionalCards(PredictorObtainChoiceKnowledge Choice, int ChildIndex,
    ImmutableArray<int> SelectedIndices, bool DefaultSkipped, PredictorAncientOffer AcceptedOffer);
