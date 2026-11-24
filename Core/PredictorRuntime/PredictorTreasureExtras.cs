namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // TreasureRoom.OpenChest invokes GenerateForRoomEnd after normal Gold and
    // SpoilsMap, before relic assignment. Vanilla 0.111 starts this set empty;
    // its current reward-list hooks cannot add a reward for TreasureRoom.
    internal void SettleTreasureExtraRewards(CancellationToken token = default)
    {
        if (Phase != PredictorPhase.TreasureClosed)
            throw new InvalidOperationException("PredictorTreasureExtraPhaseInvalid");
        var generated = PredictorRewardGeneration.Generate(Context, Working, [], cancellationToken: token);
        if (!generated.Rewards.IsEmpty)
            throw new NotImplementedException("PredictorTreasureExtraRewardContinuationPending");
        Working = generated.State;
        GenerationStages = generated.Stages;
        _preview.Partial?.RecordTreasureExtraEmpty(this, generated.Rewards.Length);
        Accept(new("TreasureExtraRewardsEmpty", 0, [], null, null));
    }
}
