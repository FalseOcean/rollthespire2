using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorTreasureActionKind { Open, TakeRelic, Obtain, Leave }

// Opening and leaving are separate authored choices. Gold and Spoils quest
// settlement belong to opening; only the offered relic can be skipped later.
internal sealed record PredictorPlannedTreasureAction(PredictorTreasureActionKind Action)
{
    public ModelKey? ExpectedRelic { get; init; }
    public PredictorPlannedObtainChild? Obtain { get; init; }
}

internal sealed record PredictorPlannedTreasure
{
    public ImmutableArray<PredictorPlannedTreasureAction> Actions { get; init; } = [];
}

internal sealed record PredictorTreasureNodeKnowledge(ModelKey? OfferedRelic,
    ImmutableArray<PredictorCombatPendingCause> RelicPendingCauses,
    ImmutableArray<PredictorObtainChoiceKnowledge> RequiredChoices,
    ImmutableArray<PredictorCombatRewardKnowledge> Rewards,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public bool? GenerationAllowed { get; init; }
    public bool Opened { get; init; }
    public bool RelicTaken { get; init; }
    // Source amounts, before Bowler Hat/Ectoplasm; zero means a known suppressed
    // reward or absent quest. Null means the actual source amount is pending.
    public int? NormalGold { get; init; }
    public int? SpoilsGold { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> NormalGoldPendingCauses { get; init; } = [];
    public ImmutableArray<PredictorCombatPendingCause> SpoilsPendingCauses { get; init; } = [];
    public int? GoldBalance { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> GoldPendingCauses { get; init; } = [];
    public bool Advanced { get; init; }
    public int AppliedActions { get; init; }
}
