using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private PredictorInputResult SubmitEventCombatChoicesOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("DENSE_VEGETATION", "TRUDGE_ON"):
                ApplyEventHealth(-8, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                Working = PredictorSettlementEffects.TakeGold(Working, _eventGoldAmount);
                Accept(new("DenseVegetationGold", 0, [], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("DENSE_VEGETATION", "REST"):
                if (_eventHealth == null && _preview.Event.ProjectHealthIndependently)
                {
                    // HP only determines the healing amount. The mimicked
                    // rest's reward plans and fight successor are independent.
                    _preview.Event.UnknownHealingAmount = true;
                    int maxHpGain = 5 * Working.Relics.Count(r => !r.Melted && r.Key.Entry == "STONE_HUMIDIFIER");
                    if (maxHpGain != 0) ApplyEventHealth(maxHpGain, maxHpGain);
                    if (BeginEventMimicRestHeal(21)) ResumeEventAfterMimicRestHeal(21);
                    return PredictorInputResult.Accepted;
                }
                var currentHealth = _eventHealth!;
                var restOutcome = RestHealOutcome(true, currentHealth.MaxHp);
                int healedHp = (int)Math.Min(currentHealth.MaxHp,
                    currentHealth.CurrentHp + (restOutcome.OverrideHealAmount ??
                        currentHealth.MaxHp * restOutcome.BaseMaxHpFraction + restOutcome.FlatHealBonus));
                _eventHealth = new(healedHp, currentHealth.MaxHp);
                Accept(new("DenseVegetationMimicRestHeal", 0, [], null, null)
                {
                    RestHealOutcome = restOutcome,
                    EventHealthConsequences = [new(healedHp - currentHealth.CurrentHp, 0)]
                });
                if (restOutcome.MaxHpDelta != 0) ApplyEventHealth(restOutcome.MaxHpDelta, restOutcome.MaxHpDelta);
                if (BeginEventMimicRestHeal(21)) ResumeEventAfterMimicRestHeal(21);
                return PredictorInputResult.Accepted;
            case ("DENSE_VEGETATION", "FIGHT"):
                BeginEventCombat(new("ENCOUNTER", "DENSE_VEGETATION_EVENT_ENCOUNTER"), [], false, token);
                return PredictorInputResult.Accepted;
            case ("PUNCH_OFF", "NAB"):
                AddTrialCurse("INJURY");
                OfferEventRewardPlans([new(PredictorRewardKind.Relic)], token);
                return PredictorInputResult.Accepted;
            case ("PUNCH_OFF", "I_CAN_TAKE_THEM"):
                EventOptions = [new("FIGHT")];
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("PUNCH_OFF", "FIGHT"):
                BeginEventCombat(new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER"),
                    [new(PredictorRewardKind.Relic), new(PredictorRewardKind.Potion)], false, token);
                return PredictorInputResult.Accepted;
            case ("BATTLEWORN_DUMMY", "SETTING_1"):
            case ("BATTLEWORN_DUMMY", "SETTING_2"):
            case ("BATTLEWORN_DUMMY", "SETTING_3"):
                int setting = choice.OptionKey[^1] - '0';
                BeginEventCombat(new("ENCOUNTER", $"BATTLEWORN_DUMMY_EVENT_V{setting}_ENCOUNTER"), [], true, token);
                return PredictorInputResult.Accepted;
            case ("THE_LANTERN_KEY", "RETURN_THE_KEY"):
                Working = PredictorSettlementEffects.TakeGold(Working, 100);
                Accept(new("LanternKeyGold", 0, [], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("THE_LANTERN_KEY", "KEEP_THE_KEY"):
                EventOptions = [new("FIGHT")];
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("THE_LANTERN_KEY", "FIGHT"):
                BeginEventCombat(new("ENCOUNTER", "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER"),
                    [new(PredictorRewardKind.SpecialCard,
                        PrefilledCardKey: new("CARD", "LANTERN_KEY"))], false, token);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> DenseVegetationOptions()
    {
        _eventGoldAmount = 61 + _eventRng!.NextInt(39);
        return [new("TRUDGE_ON", GoldGain: _eventGoldAmount, HealthDamage: 8), new("REST")];
    }

    private ImmutableArray<PredictorEventOption> PunchOffOptions()
    {
        PrepareEventCombatLayout(new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER"));
        _eventGoldAmount = 91 + _eventRng!.NextInt(8);
        return [new("NAB"), new("I_CAN_TAKE_THEM")];
    }

    private ImmutableArray<PredictorEventOption> TheLanternKeyOptions()
    {
        PrepareEventCombatLayout(new("ENCOUNTER", "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER"));
        return [new("RETURN_THE_KEY", GoldGain: 100), new("KEEP_THE_KEY")];
    }

}
