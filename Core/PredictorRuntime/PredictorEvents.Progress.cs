using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private long? _bridgeCard;
    private int _bridgeHoldOns;
    private ImmutableHashSet<long> _bridgeSkipped = [];
    private int _bathsDamage;
    private int _tabletCount;
    private int _flowerDigs;
    private int _smallTreasuryGold;
    private int _largeTreasuryGold;
    private decimal _jungleSoloGold;
    private decimal _jungleJoinGold;

    private PredictorInputResult SubmitEventProgressOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("SLIPPERY_BRIDGE", "OVERCOME"):
                long bridgeTarget = _bridgeCard ?? throw new InvalidOperationException("PredictorBridgeTargetMissing");
                RemoveEventCards([bridgeTarget]);
                Accept(new("SlipperyBridgeRemove", bridgeTarget, [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("SLIPPERY_BRIDGE", "HOLD_ON"):
                ApplyEventHealth(-(3 + _bridgeHoldOns), 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                _bridgeSkipped = _bridgeSkipped.Add(_bridgeCard!.Value);
                _bridgeHoldOns++;
                EventOptions = SlipperyBridgeOptions();
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("ABYSSAL_BATHS", "ABSTAIN"):
                ApplyEventHealth(10, 0);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("ABYSSAL_BATHS", "IMMERSE"):
            case ("ABYSSAL_BATHS", "LINGER"):
                ApplyEventHealth(2, 2);
                ApplyEventHealth(-_bathsDamage, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                _bathsDamage++;
                EventOptions = [new("LINGER", HealthDamage: _bathsDamage - 2), new("EXIT_BATHS")];
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("ABYSSAL_BATHS", "EXIT_BATHS"):
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("TABLET_OF_TRUTH", "SMASH"):
                ApplyEventHealth(20, 0);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("TABLET_OF_TRUTH", "GIVE_UP"):
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("TABLET_OF_TRUTH", "DECIPHER"):
                int tabletCost = _tabletCount switch { 0 => 3, 1 => 6, 2 => 12, 3 => 24,
                    4 => _eventHealth!.MaxHp - 1, _ => throw new InvalidOperationException("PredictorTabletCountInvalid") };
                bool tabletForcedKill = tabletCost >= _eventHealth!.MaxHp;
                if (tabletForcedKill)
                {
                    ApplyEventHealth(0, -(_eventHealth.MaxHp - 1));
                    if (Phase != PredictorPhase.Complete) ApplyEventKill();
                }
                else ApplyEventHealth(0, -tabletCost);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                if (!tabletForcedKill)
                {
                    var tabletUpgrades = Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToList();
                    if (_tabletCount == 4)
                    {
                        foreach (long cardId in tabletUpgrades)
                        {
                            var card = Working.Card(cardId);
                            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                                card with { UpgradeLevel = card.UpgradeLevel + 1 }));
                            Accept(new("TabletUpgrade", cardId, [], null, null));
                        }
                    }
                    else if (tabletUpgrades.Count > 0)
                    {
                        long cardId = tabletUpgrades[_eventRng!.NextInt(tabletUpgrades.Count)];
                        var card = Working.Card(cardId);
                        Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                            card with { UpgradeLevel = card.UpgradeLevel + 1 }));
                        Accept(new("TabletUpgrade", cardId, [], null, null));
                    }
                }
                _tabletCount++;
                if (_tabletCount == 5) FinishEvent();
                else
                {
                    int nextCost = _tabletCount switch { 1 => 6, 2 => 12, 3 => 24, 4 => _eventHealth.MaxHp - 1, _ => 3 };
                    EventOptions = [new("DECIPHER", MaxHpCost: nextCost, WillKill: _eventHealth.MaxHp <= nextCost), new("GIVE_UP")];
                    _requestId++;
                }
                return PredictorInputResult.Accepted;
            case ("COLOSSAL_FLOWER", "REACH_DEEPER"):
                ApplyEventHealth(-(5 + _flowerDigs), 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                _flowerDigs++;
                EventOptions = ColossalFlowerOptions();
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("COLOSSAL_FLOWER", "EXTRACT"):
            case ("COLOSSAL_FLOWER", "EXTRACT_INSTEAD"):
                int flowerGold = _flowerDigs switch { 0 => 35, 1 => 75, _ => 135 };
                Working = PredictorSettlementEffects.TakeGold(Working, flowerGold);
                Accept(new("FlowerGold", 0, [], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("COLOSSAL_FLOWER", "POLLINOUS_CORE"):
                ApplyEventHealth(-7, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                if (BeginObtainRelic(new("RELIC", "POLLINOUS_CORE"), null, PredictorObtainReturn.Event, 15, token))
                    ResumeEventAfterRelicObtain(15);
                return PredictorInputResult.Accepted;
            case ("SUNKEN_TREASURY", "FIRST_CHEST"):
            case ("SUNKEN_TREASURY", "SECOND_CHEST"):
                bool largeChest = choice.OptionKey == "SECOND_CHEST";
                Working = PredictorSettlementEffects.TakeGold(Working, largeChest ? _largeTreasuryGold : _smallTreasuryGold);
                Accept(new("SunkenTreasuryGold", 0, [], null, Working.GoldBudget));
                if (largeChest) AddTrialCurse("GREED");
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("JUNGLE_MAZE_ADVENTURE", "SOLO_QUEST"):
                var visualOrder = new List<int> { 0, 1, 2 };
                _eventRng!.UnstableShuffle(visualOrder);
                ApplyEventHealth(-18, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                GainJungleGold(_jungleSoloGold);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("JUNGLE_MAZE_ADVENTURE", "JOIN_FORCES"):
                GainJungleGold(_jungleJoinGold);
                FinishEvent();
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> SlipperyBridgeOptions()
    {
        var removable = Working.Deck.Where(id => Working.Card(id).Removable).ToList();
        if (removable.Count == 0) throw new InvalidOperationException("PredictorSlipperyBridgeNoRemovableCard");
        List<long> candidates;
        if (_bridgeCard == null)
            candidates = removable.Where(id => PredictorCardChanges.Definition(Context, Working.Card(id).Key).Rarity != "Basic").ToList();
        else
            candidates = removable.Where(id => Working.Card(id).Key != Working.Card(_bridgeCard.Value).Key && !_bridgeSkipped.Contains(id)).ToList();
        if (candidates.Count == 0) candidates = removable;
        _bridgeCard = candidates[_eventRng!.NextInt(candidates.Count)];
        return [new("OVERCOME", CardTargetId: _bridgeCard), new("HOLD_ON", HealthDamage: 3 + _bridgeHoldOns)];
    }

    private ImmutableArray<PredictorEventOption> ColossalFlowerOptions() => _flowerDigs switch
    {
        0 => [new("EXTRACT", GoldGain: 35), new("REACH_DEEPER", HealthDamage: 5)],
        1 => [new("EXTRACT", GoldGain: 75), new("REACH_DEEPER", HealthDamage: 6)],
        2 => [new("EXTRACT_INSTEAD", GoldGain: 135), new("POLLINOUS_CORE", HealthDamage: 7)],
        _ => throw new InvalidOperationException("PredictorFlowerDepthInvalid")
    };

    private ImmutableArray<PredictorEventOption> SunkenTreasuryOptions()
    {
        _smallTreasuryGold = 52 + _eventRng!.NextInt(16);
        _largeTreasuryGold = 303 + _eventRng.NextInt(61);
        return [new("FIRST_CHEST", GoldGain: _smallTreasuryGold),
            new("SECOND_CHEST", GoldGain: _largeTreasuryGold)];
    }

    private ImmutableArray<PredictorEventOption> JungleMazeOptions()
    {
        // Rng.NextFloat(min,max) preserves the double arithmetic until its
        // float cast; DynamicVar then stores that float as a decimal.
        _jungleSoloGold = 150m + (decimal)(float)(_eventRng!.NextDouble() * 30d - 15d);
        _jungleJoinGold = 50m + (decimal)(float)(_eventRng.NextDouble() * 30d - 15d);
        return [new("SOLO_QUEST", GoldGain: (int)_jungleSoloGold, HealthDamage: 18),
            new("JOIN_FORCES", GoldGain: (int)_jungleJoinGold)];
    }

    private void GainJungleGold(decimal amount)
    {
        foreach (var relic in Working.Relics.Where(r => !r.Melted))
            amount = relic.Key.Entry switch { "BOWLER_HAT" => amount * 1.25m, "ECTOPLASM" => 0m, _ => amount };
        int before = Working.GoldBudget;
        Working = Working with { GoldBudget = checked(before + (int)amount) };
        Accept(new("JungleMazeGold", 0, [], before, Working.GoldBudget));
    }

}
