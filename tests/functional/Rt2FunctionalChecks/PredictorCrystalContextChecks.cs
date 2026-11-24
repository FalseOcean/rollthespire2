using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCrystalContextChecks
{
    internal static void Run()
    {
        var captured = new PredictorContext("000000000000", "0.113.0", BaseGameModelKeys.Characters.Ironclad, 10,
            "crystal-catalog", "crystal-unlocks", [], [], [], 0)
        {
            Crystal = new(1, 0, 1, System.Collections.Immutable.ImmutableHashSet<ModelKey>.Empty)
        };
        PredictorSeedState.ValidateContext(captured);
        CheckInvalid(captured with { Crystal = null });
        CheckInvalid(captured with { Crystal = captured.Crystal with { PlayerSlot = 1 } });

        static void CheckInvalid(PredictorContext context)
        {
            try { PredictorSeedState.ValidateContext(context); }
            catch (InvalidDataException ex) when (ex.Message == "PredictorContextInvalid") { return; }
            throw new InvalidOperationException("Crystal context accepted without a valid captured player.");
        }
    }
}
