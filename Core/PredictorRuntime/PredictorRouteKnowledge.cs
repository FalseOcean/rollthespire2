using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRoutePreviewScope { CombatAndRest, AllModeledNodes }
// Defaults are projection intent only. They never edit the authored plans or
// become accepted inputs/checkpoints on the caller's concrete run.
internal enum PredictorRoutePreviewPolicy { AuthoredChoices, LiveMapDefaults }

// Authored intent contains identities, never a generated continuation state.
// An offer snapshot is checked again after every redraw. A changed or ambiguous
// offer leaves the choice pending instead of selecting a replacement silently.
internal sealed record PredictorPlannedCard(long InstanceId, ModelKey Key, int FloorAdded);
// Occurrence is the zero-based ordinal among rewards of the same Kind,
// Origin and Key, including already closed rewards in this node's list.
internal sealed record PredictorPlannedRewardChoice(PredictorReward ExpectedReward, int Occurrence,
    int? CardIndex = null, bool Skip = false)
{
    public ImmutableArray<int> UnresolvedEnchantmentIndices { get; init; } = [];
    public bool Reroll { get; init; }
    public PredictorReward? ExpectedRerolledReward { get; init; }
    public long? SacrificeRelicId { get; init; }
    public ImmutableArray<PredictorPlannedObtainChild> Children { get; init; } = [];
}
internal sealed record PredictorPlannedRestAction(PredictorRestAction Action)
{
    public PredictorPlannedCard? SmithCard { get; init; }
    public ImmutableArray<PredictorPlannedCard> CookCards { get; init; } = [];
    public int? MaximumHp { get; init; }
}
// The receipt address is forecast-local semantic context, not authority to
// receive a relic. A changed parent/source occurrence leaves the intent unbound.
internal sealed record PredictorOptionalObtainAddress(PredictorObtainReturn Destination, long SourceId,
    int Occurrence, ImmutableArray<long> ParentSourceIds);
internal sealed record PredictorPlannedOptionalObtain(PredictorOptionalObtainAddress Address,
    PredictorPlannedObtainChild Input);
internal sealed record PredictorOptionalObtainInput(PredictorOptionalObtainAddress Address,
    PredictorObtainChoiceKnowledge Choice);
internal enum PredictorObtainHealthEffectKind { LoseMaxHp, Damage, GainMaxHpAndFullHeal, HealMaxHpFraction }
// A nominal command from a real receipt, never a computed remaining-HP value.
internal sealed record PredictorObtainHealthEffect(ModelKey Source, PredictorOptionalObtainAddress Address,
    PredictorObtainHealthEffectKind Kind, int Amount, ImmutableArray<PredictorCombatPendingCause> PendingCauses);
internal sealed record PredictorPaelsToothStorageKnowledge(long SourceId, PredictorOptionalObtainAddress Address,
    string SourceNodeId, int? InitialCount, int? RemainingCount, ImmutableArray<PredictorCombatPendingCause> PendingCauses);
internal sealed record PredictorPlannedNode(string NodeId)
{
    // Optional drawing-only card-target intent. It is used only when this
    // exact source is actually acquired in this node, never as a receipt.
    public ImmutableArray<PredictorPlannedOptionalObtain> ObtainInputs { get; init; } = [];
    public PredictorCombatNodeOverrides? Combat { get; init; }
    public ImmutableArray<PredictorPlannedRestAction> RestActions { get; init; } = [];
    public bool LeaveRest { get; init; }
    public ImmutableArray<PredictorPlannedRewardChoice> RewardChoices { get; init; } = [];
    public bool LeaveRewards { get; init; }
    public ImmutableArray<PredictorPlannedPotionAction> PotionActions { get; init; } = [];
    public PredictorPlannedShop? Shop { get; init; }
    public PredictorPlannedAncient? Ancient { get; init; }
    public PredictorPlannedEvent? Event { get; init; }
    public ImmutableArray<PredictorEventOccurrenceExclusion> EventOccurrenceExclusions { get; init; } = [];
    public PredictorPlannedTreasure? Treasure { get; init; }
}

internal sealed record PredictorRouteCardKnowledge(PredictorCard Value,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    internal bool IsKnown => PendingCauses.IsEmpty;
    // A target reader can establish identity/eligibility without knowing every
    // persistent card field. Do not render those unrelated witness fields.
    public bool FullCardKnown { get; init; } = true;
}

internal sealed record PredictorRestActionKnowledge(int ActionIndex, PredictorRestAction Action,
    ImmutableArray<PredictorRouteCardKnowledge> Candidates, ImmutableArray<ModelKey> ObtainedRelics);

internal sealed record PredictorRouteRelicKnowledge(PredictorRelic Value,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    internal bool IsKnown => PendingCauses.IsEmpty;
    // Ownership can be known while the forecast deliberately carries only
    // Pael's count. Value.StoredCards is not a proven empty payload then.
    public PredictorPaelsToothStorageKnowledge? PaelsToothStorage { get; init; }
    internal bool StoredCardsKnown => PaelsToothStorage == null;
}

// The exact known inventory subset is useful for authoring a smith target.
// It is not a complete deck when DeckPendingCauses is nonempty. Reward values
// retain the display-only, non-actionable contract of combat previews.
internal sealed record PredictorRouteNodeKnowledge(PredictorPosition Position,
    PredictorRoomKind Room, ModelKey? Model, PredictorPhase Phase,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorRestOffer> RestOffers,
    ImmutableArray<PredictorRouteCardKnowledge> Cards,
    ImmutableArray<PredictorCombatPendingCause> DeckPendingCauses,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    // These improve already projected card effects; they never block the node.
    public ImmutableArray<PredictorOptionalObtainInput> OptionalObtainInputs { get; init; } = [];
    public ImmutableArray<PredictorObtainHealthEffect> ObtainHealthEffects { get; init; } = [];
    public ImmutableArray<PredictorPaelsToothStorageKnowledge> PaelsToothStorage { get; init; } = [];
    public PredictorRestHealOutcome? RestHeal { get; init; }
    public bool Advanced { get; init; }
    public int AppliedRestActions { get; init; }
    public PredictorPlannedRestAction? DefaultRestAction { get; init; }
    public bool RestDefaultApplied { get; init; }
    // Hatch multiplicity can depend on unresolved Byrdonis Egg membership.
    // In that case RestOffers contains the guaranteed option subset only.
    public ImmutableArray<PredictorCombatPendingCause> RestOfferPendingCauses { get; init; } = [];
    public PredictorShopNodeKnowledge? Shop { get; init; }
    public PredictorAncientNodeKnowledge? Ancient { get; init; }
    public PredictorEventNodeKnowledge? Event { get; init; }
    public PredictorTreasureNodeKnowledge? Treasure { get; init; }
    public PredictorCombatRequirements? CombatRequirements { get; init; }
    // Entry inventory for node-local combat declarations, before theft,
    // upgrades or any post-combat reward pickup can change its membership.
    public ImmutableArray<PredictorRouteCardKnowledge> CombatCards { get; init; } = [];
    public ImmutableArray<PredictorRouteRelicKnowledge> CombatRelics { get; init; } = [];
    public ImmutableArray<PredictorRouteCardKnowledge> RestCards { get; init; } = [];
    public ImmutableArray<PredictorRestActionKnowledge> RestActionKnowledge { get; init; } = [];
    // Boundaries retain the inventory actually observed where operations bind.
    // Final inventory must never be reused as a pre-pickup operation target.
    public ImmutableArray<PredictorRoutePotionBoundaryKnowledge> PotionBoundaries { get; init; } = [];
    public PredictorRouteInventoryKnowledge? Inventory { get; init; }
    public PredictorNodeDiagnostics? Diagnostics { get; init; }
    public PredictorObtainChoiceKnowledge? RewardObtainChoice { get; init; }
}

// A refused entry owns only a departure/entry-before observation. It is not an
// entered room, completed node, generated offer, or post-entry state witness.
internal sealed record PredictorRouteTerminalBoundary(PredictorPosition TargetPosition,
    PredictorPosition DeparturePosition, ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public PredictorNodeDiagnostics? Diagnostics { get; init; }
}

internal sealed record PredictorRouteContinuation(ImmutableArray<PredictorRouteNodeKnowledge> Nodes,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses, bool ReachedLimit)
{
    public PredictorKnowledgeSnapshot? Knowledge { get; init; }
    public PredictorRouteTerminalBoundary? TerminalBoundary { get; init; }
    public PredictorRoutePredictionFault? Fault { get; init; }
}

// A real failed calculation has no settled node or invented after-state. The
// original exception belongs to diagnostics, never to ordinary display text.
internal sealed record PredictorRoutePredictionFault(PredictorPosition Position, string Stage, Exception Error);
