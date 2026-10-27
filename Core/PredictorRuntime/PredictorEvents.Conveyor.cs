using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private bool _conveyorEntryHealthPending;
    private PredictorConveyorInput? _conveyorInput;
    private int? _conveyorLostHp;
    private int? _conveyorRewardEmptySlotsBefore;
    private string? _conveyorDish;
    private string? _conveyorLastDish;
    private int _conveyorGrabs;
    private bool _conveyorHadGrab;

    private PredictorInputResult SubmitEventConveyorOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("ENDLESS_CONVEYOR", "OBSERVE_CHEF"):
                var chefCards = Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel)
                    .ToArray();
                if (chefCards.Length > 0)
                {
                    long id = chefCards[_eventRng!.NextInt(chefCards.Length)];
                    var card = Working.Card(id);
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        card with { UpgradeLevel = card.UpgradeLevel + 1 }));
                    Accept(new("ConveyorChefUpgrade", id, [], null, null));
                }
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("ENDLESS_CONVEYOR", "LEAVE"):
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("ENDLESS_CONVEYOR", _) when choice.OptionKey == "GRAB_" + _conveyorDish:
                if (_conveyorDish != "GOLDEN_FYSH") SpendEventGold(40);
                _conveyorHadGrab = true;
                ExecuteConveyorDish(token);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> BeginConveyorOptions()
    {
        if (_eventHealth == null)
        {
            _conveyorEntryHealthPending = true;
            return [];
        }
        RollConveyorDish();
        return ConveyorOptions();
    }

    private void RollConveyorDish()
    {
        _conveyorGrabs++;
        if (_conveyorGrabs % 5 == 0)
        {
            _conveyorDish = _conveyorLastDish = "SEAPUNK_SALAD";
            _preview.EventTrace?.ConveyorDish(this, [], fixedFifth: true);
            return;
        }
        var dishes = new List<(string Name, float Weight)>
        {
            ("CAVIAR", 6f), ("SPICY_SNAPPY", 3f), ("JELLY_LIVER", 3f), ("FRIED_EEL", 3f)
        };
        if (EventHasEmptyPotionSlot) dishes.Add(("SUSPICIOUS_CONDIMENT", 3f));
        if ((_conveyorLostHp ?? _eventHealth!.MaxHp - _eventHealth.CurrentHp) > 0) dishes.Add(("CLAM_ROLL", 6f));
        if (_conveyorGrabs > 1) dishes.Add(("GOLDEN_FYSH", 1f));
        dishes.RemoveAll(d => d.Name == _conveyorLastDish);
        var dishReads = _preview.EventTrace?.ConveyorPool(this, dishes) ?? [];
        float roll = _eventRng!.NextFloat() * dishes.Sum(d => d.Weight);
        float cumulative = 0f;
        foreach (var dish in dishes)
        {
            cumulative += dish.Weight;
            if (roll >= cumulative) continue;
            _conveyorDish = _conveyorLastDish = dish.Name;
            _preview.EventTrace?.ConveyorDish(this, dishReads);
            return;
        }
        throw new InvalidOperationException("PredictorConveyorDishRollInvalid");
    }

    private ImmutableArray<PredictorEventOption> ConveyorOptions()
    {
        string dish = _conveyorDish ?? throw new InvalidOperationException("PredictorConveyorDishMissing");
        var grab = new PredictorEventOption("GRAB_" + dish, Enabled: Working.GoldBudget >= 40,
            GoldCost: dish == "GOLDEN_FYSH" ? null : 40,
            GoldGain: dish == "GOLDEN_FYSH" ? 75 : null,
            HealthHeal: dish == "CLAM_ROLL" ? 10 : dish == "CAVIAR" ? 4 : null);
        return _conveyorHadGrab ? [grab, new("LEAVE")] : [grab, new("OBSERVE_CHEF")];
    }

    private void ExecuteConveyorDish(CancellationToken token)
    {
        switch (_conveyorDish)
        {
            case "CAVIAR": ApplyConveyorHealth(4, 4); break;
            case "CLAM_ROLL": ApplyConveyorHealth(10, 0); break;
            case "GOLDEN_FYSH":
                Working = PredictorSettlementEffects.TakeGold(Working, 75);
                Accept(new("ConveyorGoldenFysh", 0, [], null, Working.GoldBudget));
                break;
            case "SPICY_SNAPPY":
                var upgradable = Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel)
                    .ToArray();
                if (upgradable.Length > 0)
                {
                    long id = upgradable[_eventRng!.NextInt(upgradable.Length)];
                    var spicyCard = Working.Card(id);
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        spicyCard with { UpgradeLevel = spicyCard.UpgradeLevel + 1 }));
                    Accept(new("ConveyorSpicyUpgrade", id, [], null, null));
                }
                break;
            case "JELLY_LIVER":
                var targets = PredictorCardChanges.TransformCandidates(Context, Working);
                if (!targets.IsEmpty)
                {
                    BeginEventCardSelection(targets, 1, token);
                    return;
                }
                break;
            case "SEAPUNK_SALAD":
                var feedingFrenzy = new ModelKey("CARD", "FEEDING_FRENZY");
                PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, feedingFrenzy).Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, feedingFrenzy);
                Accept(new("ConveyorFeedingFrenzy", Working.Deck[^1], [], null, null));
                break;
            case "FRIED_EEL":
                var state = Working;
                var card = PredictorRewardGeneration.GenerateCards(Context, ref state,
                    new(PredictorRewardKind.Card, CardCount: 1, Colorless: true,
                        NoUpgradeRoll: true), isCardReward: false)[0];
                PredictorSettlementEffects.RequireImplementedCard(card, context: Context);
                Working = PredictorObtainSources.AddCard(Context, state, card);
                Accept(new("ConveyorFriedEel", Working.Deck[^1], [], null, null));
                break;
            case "SUSPICIOUS_CONDIMENT":
                if (Context.PotionPool.IsEmpty) break;
                var drawn = Working.Draw(PredictorStream.Rewards, 0, Context.PotionPool.Length);
                Working = drawn.State;
                _conveyorRewardEmptySlotsBefore = Working.PotionSlots.Count(potion => potion == null);
                OfferEventRewardPlans([new(PredictorRewardKind.Potion,
                    PrefilledPotionKey: Context.PotionPool[drawn.Value].Key)], token);
                return;
            default: throw new InvalidOperationException("PredictorConveyorDishInvalid");
        }
        AdvanceConveyor();
    }

    private void AdvanceConveyor()
    {
        RollConveyorDish();
        EventOptions = ConveyorOptions();
        Phase = PredictorPhase.EventChoice;
        _requestId++;
    }

    private void ApplyConveyorHealth(int healing, int maxHpGain)
    {
        if (_conveyorLostHp is not { } lost) { ApplyEventHealth(healing, maxHpGain); return; }
        // Beta111 GainMaxHp raises the maximum then heals the same amount;
        // Heal clamps to the missing amount. Those exact deltas need no
        // invented current/max HP and preserve the next pool's missing-HP test.
        int actualHeal = (int)Math.Min((long)healing, (long)lost + maxHpGain);
        _conveyorLostHp = checked((int)((long)lost + maxHpGain - actualHeal));
        Accept(new("EventHealth", 0, [], null, null)
            { EventHealthConsequences = [new(actualHeal, maxHpGain)] });
    }

}
