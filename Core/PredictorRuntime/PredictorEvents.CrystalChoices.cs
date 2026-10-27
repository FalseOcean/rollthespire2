using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private PredictorInputResult SubmitEventCrystalOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("CRYSTAL_SPHERE", "UNCOVER_FUTURE"):
                SpendEventGold(_eventGoldAmount);
                BeginCrystalSphere(3);
                return PredictorInputResult.Accepted;
            case ("CRYSTAL_SPHERE", "PAYMENT_PLAN"):
                var debt = new ModelKey("CARD", "DEBT");
                PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, debt).Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, debt);
                Accept(new("CrystalSphereDebt", Working.Deck[^1], [], null, null));
                BeginCrystalSphere(6);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> CrystalSphereOptions()
    {
        _eventGoldAmount = 50 + 1 + _eventRng!.NextInt(49);
        return [new("UNCOVER_FUTURE", GoldCost: _eventGoldAmount), new("PAYMENT_PLAN")];
    }

}
