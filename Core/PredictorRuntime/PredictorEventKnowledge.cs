using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// A route event is an authored sequence, including its nested transactions.
// Repeated options remain separate actions; there is no implicit event choice.
internal enum PredictorEventActionKind
{
    Option, Cards, Health, Obtain, Reward, LeaveRewards, Combat,
    ConfirmAbandon, CrystalTool, CrystalCell, ConveyorInputs
}

internal sealed record PredictorPlannedEvent(ModelKey ExpectedEvent)
{
    public ChooseEventOccurrence? Occurrence { get; init; }
    // Receipt for an automatic prediction, never an occurrence selection.
    public ChooseEventOccurrence? ExpectedOccurrence { get; init; }
    public PredictorEventPotionInput? Potions { get; init; }
    public ImmutableArray<PredictorPlannedEventAction> Actions { get; init; } = [];
}

internal sealed record PredictorPlannedEventAction(PredictorEventActionKind Action)
{
    // Enabled is re-evaluated at this boundary. Every other option field is
    // semantic intent, including a randomly selected persistent target.
    public PredictorEventOption? ExpectedOption { get; init; }
    public ImmutableArray<PredictorPlannedCard> DeckCards { get; init; } = [];
    public ImmutableArray<PredictorCard> ExpectedCards { get; init; } = [];
    public ImmutableArray<int> CardIndices { get; init; } = [];
    public PredictorEventHealthFact? Health { get; init; }
    public PredictorConveyorInput? Conveyor { get; init; }
    public PredictorPlannedObtainChild? Obtain { get; init; }
    public PredictorPlannedRewardChoice? RewardChoice { get; init; }
    public ModelKey? ExpectedEncounter { get; init; }
    public PredictorCombatNodeOverrides? Combat { get; init; }
    public bool? EventRanOutOfTime { get; init; }
    public bool? ConfirmAbandon { get; init; }
    public PredictorCrystalView? ExpectedCrystal { get; init; }
    public PredictorCrystalTool? Tool { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
}

// Value has Enabled normalized to true: availability has its own nullable
// observation and causes, so an unknown budget never becomes a false fact.
internal sealed record PredictorEventOptionKnowledge(string Key, PredictorEventOption? Value,
    bool? Enabled, ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public ImmutableArray<PredictorCombatPendingCause> IdentityPendingCauses { get; init; } = [];
    internal string? TraceIdentity { get; init; }
    internal ImmutableArray<PredictorKnowledgeFactId> IdentityReadFactIds { get; init; } = [];
    internal ImmutableArray<PredictorKnowledgeFactId> AvailabilityReadFactIds { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> AvailabilityPendingCauses { get; init; } = [];
    public PredictorEventOptionResultKnowledge? ResultPreview { get; init; }
    // Display observation bound to the guarded option identity, not another
    // semantic field that would invalidate previously authored choices.
    public ModelKey? LostRelic { get; init; }
    internal bool IsKnown => Value != null && Enabled.HasValue && PendingCauses.IsEmpty;
}

// A detached, independent execution of just this option. Generated identities
// are display values; missing targets/health are never filled by eligibility
// assumptions or another option's witness.
internal sealed record PredictorEventOptionResultKnowledge(
    PredictorEventNeededInput NeededInput,
    ImmutableArray<PredictorCard> OfferedCards,
    ImmutableArray<PredictorRouteCardKnowledge> DeckCardCandidates,
    ImmutableArray<PredictorObtainChoiceKnowledge> RequiredChoices,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public ImmutableArray<PredictorEventOptionKnowledge> Options { get; init; } = [];
    public ImmutableArray<PredictorEventCardChangeKnowledge> ChangedCards { get; init; } = [];
    // Only causes read by this option or produced by its unresolved child.
    // PendingCauses retains the complete route context for diagnostics.
    public ImmutableArray<PredictorCombatPendingCause> LocalPendingCauses { get; init; } = [];
    public bool HealthRequired { get; init; }
    // The option can lead through a deterministic confirmation directly to a
    // combat child. This is the actual handler's child, with the same missing
    // premises and reward masks as an adopted event branch.
    public PredictorCombatPreview? Combat { get; init; }
    public ModelKey? CombatEncounter { get; init; }
    internal string? TraceIdentity { get; init; }
    public int CardsRequired { get; init; }
    public ImmutableArray<PredictorRouteCardKnowledge> GeneratedCards { get; init; } = [];
    public ImmutableArray<PredictorRelic> ObtainedRelics { get; init; } = [];
    public ImmutableArray<ModelKey> LostRelics { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> RelicPendingCauses { get; init; } = [];
    public ImmutableArray<PredictorEventHealthDelta> HealthConsequences { get; init; } = [];
    public ImmutableArray<PredictorEventHealthDelta> PendingHealthCommands { get; init; } = [];
    public int? GoldBalance { get; init; }
    public int? GoldChange { get; init; }
    public bool AllGoldLost { get; init; }
    public bool Advanced { get; init; }
}

internal sealed record PredictorEventCardChangeKnowledge(string Action, PredictorCard Before, PredictorCard? After);

internal enum PredictorEventNeededInput
{
    None, Occurrence, Option, Cards, Health, Combat, Rewards, Obtain, TrialAbandon, Crystal
}

internal sealed record PredictorEventNodeKnowledge(ModelKey? Event,
    ImmutableArray<PredictorEventOptionKnowledge> Options,
    ImmutableArray<PredictorRouteCardKnowledge> DeckCardCandidates,
    ImmutableArray<PredictorCard> OfferedCards,
    ImmutableArray<PredictorObtainChoiceKnowledge> RequiredChoices,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public PredictorRouteEventInputPolicy InputPolicy => PredictorRouteEventPolicy.For(Event);
    // The current stage can differ from the event's entry policy (for example
    // an adopted fight has only a deterministic confirmation page left).
    public bool BusinessChoiceRequired { get; init; }
    public PredictorEventPotionRequirement PotionInputRequirement { get; init; }
    public PredictorEventPotionInput? PotionInput { get; init; }
    public PredictorConveyorInput? ConveyorInput { get; init; }
    public int? ConveyorLostHp { get; init; }
    public ImmutableArray<PredictorEventOptionKnowledge> EntryOptions { get; init; } = [];
    // Only effects of the actual applied prefix, captured before any private
    // completion of unselected alternatives. Reward receipts still own rewards.
    public PredictorEventOptionResultKnowledge? AppliedResult { get; init; }
    internal string? TraceIdentity { get; init; }
    public PredictorEventOccurrenceObservation? Occurrence { get; init; }
    public PredictorEventOccurrenceCandidate? SelectedOccurrence { get; init; }
    public ModelKey? AuthoredEvent { get; init; }
    public bool AuthoredActionsNotApplied { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> OccurrencePendingCauses { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> OfferedCardPendingCauses { get; init; } = [];
    public PredictorEventNeededInput NeededInput { get; init; }
    public bool HealthRequired { get; init; }
    public int CardsRequired { get; init; }
    public PredictorCombatPreview? Combat { get; init; }
    public ModelKey? CombatEncounter { get; init; }
    public PredictorCrystalView? Crystal { get; init; }
    public int? GoldBalance { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> GoldPendingCauses { get; init; } = [];
    public ImmutableArray<PredictorEventHealthDelta> HealthConsequences { get; init; } = [];
    public ImmutableArray<PredictorEventHealthDelta> PendingHealthCommands { get; init; } = [];
    public bool Advanced { get; init; }
    public int AppliedActions { get; init; }
}
