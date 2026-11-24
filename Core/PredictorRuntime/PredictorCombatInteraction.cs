namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Explicitly restart this battle's reward choices. Replaying the same entry
    // and facts regenerates the same offers; it never advances a live game RNG.
    internal void ReorderCurrentCombatRewards(CancellationToken token = default)
    {
        var combat = _combatInputForEditing;
        if (combat == null || !CanAdjustCurrentCombat)
            throw new InvalidOperationException("PredictorCombatRewardsNotEditable");
        if (AdjustCombat(token) != PredictorInputResult.Accepted ||
            Submit(Request!, combat, token) != PredictorInputResult.Accepted)
            throw new InvalidOperationException("PredictorCombatRewardReplayMismatch");
        AutomaticRewardPickup = false;
    }
}
