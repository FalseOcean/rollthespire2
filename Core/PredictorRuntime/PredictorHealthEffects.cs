namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorHealthEffects
{
    // TungstenRod.ModifyHpLostAfterOsty applies outside combat too, including
    // LoseMaxHp's damage step. Explicit Kill is a different command.
    internal static int ModifyOutOfCombatHpLoss(PredictorState state, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        foreach (var relic in state.Relics.Where(r => !r.Melted && r.Key.Entry == "TUNGSTEN_ROD"))
            amount = Math.Max(0, amount - 1);
        return amount;
    }
}
