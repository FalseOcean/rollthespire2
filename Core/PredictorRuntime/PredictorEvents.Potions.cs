using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private ImmutableDictionary<long, EffectCardType> _futurePotionTypes = ImmutableDictionary<long, EffectCardType>.Empty;
    private long? _stonePotionId;
    private long? _ranwidPotionId;
    private long? _ranwidRelicId;

    private PredictorInputResult SubmitEventPotionsOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("POTION_COURIER", "GRAB_POTIONS"):
                OfferEventRewardPlans(Enumerable.Repeat(new PredictorRewardPlan(PredictorRewardKind.Potion,
                    PrefilledPotionKey: new ModelKey("POTION", "FOUL_POTION")), 3), token);
                return PredictorInputResult.Accepted;
            case ("POTION_COURIER", "RANSACK"):
                var uncommon = Context.PotionPool.Where(p => p.Rarity == PredictorPotionRarity.Uncommon).ToArray();
                if (uncommon.Length == 0) { FinishEvent(); return PredictorInputResult.Accepted; }
                var chosen = Working.Draw(PredictorStream.Rewards, 0, uncommon.Length);
                Working = chosen.State;
                OfferEventRewardPlans([new(PredictorRewardKind.Potion, PrefilledPotionKey: uncommon[chosen.Value].Key)], token);
                return PredictorInputResult.Accepted;
            case ("THE_FUTURE_OF_POTIONS", _):
                long potionId = EventPotionToken(option);
                int potionSlot = EventPotionSlots.FindIndex(p => p?.Id == potionId);
                if (potionSlot < 0) return PredictorInputResult.Invalid;
                var potion = EventPotionSlots[potionSlot]!;
                var rarity = Context.Operation != null && _operationFuturePotionRarities.TryGetValue(potionId, out var actualRarity)
                    ? actualRarity : Context.PotionPool.Single(p => p.Key == potion.Key).Rarity switch
                {
                    PredictorPotionRarity.Common => EffectCardRarity.Common,
                    PredictorPotionRarity.Uncommon => EffectCardRarity.Uncommon,
                    _ => EffectCardRarity.Rare
                };
                var cardType = _futurePotionTypes[potionId];
                SetEventPotionSlot(potionSlot, null);
                Accept(new("FutureDiscardPotion", potionId, [], null, null));
                var cardPool = Context.Catalog!.CharacterCards.Where(c => c.Rarity == rarity && c.CardType == cardType)
                    .Select(c => c.CardKey).ToImmutableArray();
                var futureRewards = PredictorRewardGeneration.Generate(Context, Working,
                    [new(PredictorRewardKind.Card, CardCount: 3, Odds: PredictorCardOdds.Uniform,
                        Rarity: rarity, CardPoolOverride: cardPool, NoCardPoolModifications: true)
                        { UpgradeAfterGeneration = true }], cancellationToken: token);
                _eventRewardContinuation = true;
                var futureInputs = _inputs;
                Phase = PredictorPhase.Complete;
                PrepareRewardNode(Working.Position, futureRewards.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Event }).ToImmutableArray(), futureRewards.State, token);
                _inputs = futureInputs;
                EventOptions = [];
                GenerationStages = futureRewards.Stages;
                return PredictorInputResult.Accepted;
            case ("STONE_OF_ALL_TIME", "LIFT"):
                int stoneSlot = EventPotionSlots.FindIndex(p => p?.Id == _stonePotionId);
                if (stoneSlot < 0) return PredictorInputResult.Invalid;
                SetEventPotionSlot(stoneSlot, null);
                Accept(new("StoneDiscardPotion", _stonePotionId!.Value, [], null, null));
                ApplyEventHealth(10, 10);
                _eventRng!.NextInt(100);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("STONE_OF_ALL_TIME", "PUSH"):
                ApplyEventHealth(-6, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                var vigorous = SymbioteCandidates();
                if (vigorous.IsEmpty)
                {
                    _eventRng!.NextInt(100);
                    FinishEvent();
                }
                else BeginEventCardSelection(vigorous, 1, token);
                return PredictorInputResult.Accepted;
            case ("RANWID_THE_ELDER", "POTION"):
                int ranwidSlot = EventPotionSlots.FindIndex(p => p?.Id == _ranwidPotionId);
                if (ranwidSlot < 0) return PredictorInputResult.Invalid;
                SetEventPotionSlot(ranwidSlot, null);
                Accept(new("RanwidDiscardPotion", _ranwidPotionId!.Value, [], null, null));
                BeginEventFrontRelic(16, token);
                return PredictorInputResult.Accepted;
            case ("RANWID_THE_ELDER", "GOLD"):
                SpendEventGold(100);
                BeginEventFrontRelic(17, token);
                return PredictorInputResult.Accepted;
            case ("RANWID_THE_ELDER", "RELIC"):
                if (Context.Operation is not null) RemoveRelicInstance(_ranwidRelicId!.Value);
                else RemoveRelic(_ranwidRelicId!.Value);
                BeginEventFrontRelic(18, token);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> FuturePotionOptions()
    {
        var occupied = EventPotionSlots.OfType<PredictorPotion>().ToArray();
        if (occupied.Length < 2) throw new InvalidOperationException("PredictorFuturePotionEntryRequiresTwo");
        var keys = Context.PotionPool.ToDictionary(p => p.Key);
        foreach (var potion in occupied)
        {
            var types = keys[potion.Key].Rarity == PredictorPotionRarity.Common
                ? new[] { EffectCardType.Attack, EffectCardType.Skill }
                : new[] { EffectCardType.Attack, EffectCardType.Skill, EffectCardType.Power };
            _futurePotionTypes = _futurePotionTypes.Add(potion.Id, types[_eventRng!.NextInt(types.Length)]);
        }
        return occupied.Take(3).Select(p => EventPotionOption(p.Id > 0 ? "POTION:" + p.Id : "POTION:LOCAL:" + (-p.Id - 1),
            p, _futurePotionTypes[p.Id])).ToImmutableArray();
    }

    private ImmutableArray<PredictorEventOption> StoneOfAllTimeOptions()
    {
        var potions = EventPotionSlots.OfType<PredictorPotion>().ToArray();
        if (potions.Length == 0) throw new InvalidOperationException("PredictorStonePotionRequired");
        _stonePotionId = potions[_eventRng!.NextInt(potions.Length)].Id;
        return [EventPotionOption("LIFT", potions.Single(potion => potion.Id == _stonePotionId)),
            new("PUSH", Enabled: SymbioteCandidates().Length > 0, HealthDamage: 6)];
    }

    private ImmutableArray<PredictorEventOption> RanwidOptions()
    {
        var potions = EventPotionSlots.OfType<PredictorPotion>().ToArray();
        if (potions.Length > 0) _ranwidPotionId = potions[_eventRng!.NextInt(potions.Length)].Id;
        var relics = Working.Relics.Where(IsRelicTradable).ToArray();
        if (relics.Length > 0) _ranwidRelicId = relics[_eventRng!.NextInt(relics.Length)].Id;
        return [_ranwidPotionId is { } selectedPotion ? EventPotionOption("POTION", potions.Single(potion => potion.Id == selectedPotion)) : new("POTION", Enabled: false),
            new("GOLD", GoldCost: 100),
            new("RELIC", Enabled: _ranwidRelicId != null, RelicId: _ranwidRelicId)];
    }

}
