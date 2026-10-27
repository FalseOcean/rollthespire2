using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// Named dependencies of detached observations. A missing value is propagated
// only through the source readers implemented by the bounded route projection.
[Flags]
internal enum PredictorCombatDomain
{
    None = 0,
    Gold = 1 << 0,
    Deck = 1 << 1,
    Health = 1 << 2,
    Relics = 1 << 3,
    InstanceIds = 1 << 4,
    NicheRng = 1 << 5,
    RewardsRng = 1 << 6,
    CardRewards = 1 << 7,
    GoldRewards = 1 << 8,
    PotionRewards = 1 << 9,
    RelicRewards = 1 << 10,
    ReturnedCards = 1 << 11,
    Continuation = 1 << 12,
    Odds = 1 << 13,
    PotionInventory = 1 << 14,
    CombatPotionRng = 1 << 15,
    Map = 1 << 16,
    ExtraCardRewards = 1 << 17,
    ExtraGoldRewards = 1 << 18,
    RemoveCardRewards = 1 << 19,
    ShopsRng = 1 << 20,
    TreasureRng = 1 << 21,
    TransformationsRng = 1 << 22,
    UpFrontRng = 1 << 23,
    UnknownMapPointRng = 1 << 24,
    CardRarityOdds = 1 << 25,
    PotionDropOdds = 1 << 26,
    RewardAlternativeState = 1 << 27,
    CardRewardHookState = 1 << 28
}

internal sealed record PredictorCombatPendingCause(string Code, string Description,
    PredictorCombatDomain AffectedDomains, PredictorEnemyCreationSource? EnemySource = null)
{
    public string? NodeId { get; init; }
    // Bind before comparing/merging. External captures may still use the
    // positional constructor; an empty value is never a merge identity.
    public string Identity { get; init; } = string.Empty;
    public string? ChoiceId { get; init; }
    public PredictorPendingChoice? Choice { get; init; }
    public PredictorPendingReasonKind ReasonKind { get; init; } = PredictorPendingReasonKind.ImplementationGap;
    public ImmutableArray<string> RequiredPropertyKeys { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> Parents { get; init; } = [];
    public string SourceEvidence { get; init; } = "Dependency evidence has not been bound.";
}

// Value is absent, never a fabricated zero, when the disposition depends on a
// pending cause. A known net-restored Gold disposition retains its explicit
// GoldAmountUnspecified metadata; IsKnown does not invent a numeric amount.
// UnknownMultiplicity represents a source whose reward count is
// itself unknown (currently stolen cards), rather than one invented reward.
internal sealed record PredictorCombatRewardKnowledge(PredictorRewardKind Kind,
    PredictorReward? Value, ImmutableArray<PredictorCombatPendingCause> PendingCauses,
    bool UnknownMultiplicity = false)
{
    internal bool IsKnown => Value != null && PendingCauses.IsEmpty && !UnknownMultiplicity;
    // Identities/levels can be proven while only Wing Charm's enchantment
    // target remains pending. Value/IsKnown still describe the whole result.
    public ImmutableArray<PredictorCardRewardOptionKnowledge> CardOptions { get; init; } = [];
    // Route projection ordinal among same Kind/Origin/Key, including entries
    // whose values are masked (for example an unknown-amount Heist).
    public int Occurrence { get; init; } = -1;
    public ImmutableArray<PredictorRouteRewardAlternativeKnowledge> Alternatives { get; init; } = [];
    public PredictorPlannedRewardChoice? AppliedChoice { get; init; }
    // Forecast policy disposition is separate from authored reward actions;
    // a reroll-only plan can retain its Reroll while the new offer is skipped.
    public bool DefaultSkipped { get; init; }
    public PredictorKnowledgeOrigin? DispositionOrigin { get; init; }
    public PredictorCompletedRewardEdit? CompletedEdit { get; init; }
    public PredictorObtainChoiceKnowledge? RequiredChoices { get; init; }
    public int AppliedChildren { get; init; }
    public int? SacrificeCount { get; init; }
    public RolltheSpire2.Core.Identity.ModelKey? SacrificeRelic { get; init; }
    // Forecast-local generation identity, never a game/detached instance ID.
    // A changed or replaced offer loses its connection to that computation.
    internal string? SourceIdentity { get; init; }
}

internal sealed record PredictorCombatStreamKnowledge(PredictorStream Stream,
    PredictorStreamState KnownBase, PredictorStreamState? Value,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    internal bool HasUnknownOffset => Value == null;
}

internal sealed record PredictorCombatGoldKnowledge(int KnownBase, int? Value,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses);

// A display-only projection, deliberately without a PredictorState, accepted
// inputs, actionable reward IDs, or a continuation checkpoint.
internal sealed record PredictorCombatPreview(PredictorPosition Position,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses,
    ImmutableArray<PredictorCombatStreamKnowledge> Streams,
    PredictorCombatGoldKnowledge Gold,
    PredictorCombatRequirements MissingFacts);
