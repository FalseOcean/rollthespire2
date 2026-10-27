using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private EffectCardType? _tinkerType;
    private bool _trialAbandonPending;
    private ImmutableArray<PredictorEventOption> _trialAbandonOptions = [];

    private PredictorInputResult SubmitEventTransformsOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("AROMA_OF_CHAOS", "LET_GO"):
            case ("SYMBIOTE", "KILL_WITH_FIRE"):
                BeginEventCardSelection(PredictorCardChanges.TransformCandidates(Context, Working), 1, token);
                return PredictorInputResult.Accepted;
            case ("AROMA_OF_CHAOS", "MAINTAIN_CONTROL"):
                BeginEventCardSelection(Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToImmutableArray(), 1, token);
                return PredictorInputResult.Accepted;
            case ("MORPHIC_GROVE", "GROUP"):
                _preview.EventTrace?.MorphicGoldBefore(this);
                int before = Working.GoldBudget;
                Working = Working with { GoldBudget = 0 };
                Accept(new("EventGoldStolen", 0, [], before, 0));
                BeginEventCardSelection(PredictorCardChanges.TransformCandidates(Context, Working), 2, token);
                return PredictorInputResult.Accepted;
            case ("MORPHIC_GROVE", "LONER"):
                ApplyEventHealth(5, 5);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("WHISPERING_HOLLOW", "GOLD"):
                int cost = option.GoldCost!.Value;
                int beforeGold = Working.GoldBudget;
                Working = Working with { GoldBudget = Math.Max(0, beforeGold - cost) };
                Accept(new("EventGoldSpent", 0, [], beforeGold, Working.GoldBudget));
                var generated = PredictorRewardGeneration.Generate(Context, Working,
                    [new(PredictorRewardKind.Potion), new(PredictorRewardKind.Potion)], cancellationToken: token);
                _eventRewardContinuation = true;
                var priorInputs = _inputs;
                Phase = PredictorPhase.Complete;
                PrepareRewardNode(Working.Position, generated.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Event }).ToImmutableArray(), generated.State, token);
                _inputs = priorInputs;
                EventOptions = [];
                GenerationStages = generated.Stages;
                return PredictorInputResult.Accepted;
            case ("WHISPERING_HOLLOW", "HUG"):
                BeginEventCardSelection(PredictorCardChanges.TransformCandidates(Context, Working), 1, token);
                return PredictorInputResult.Accepted;
            case ("TRASH_HEAP", "GRAB"):
                Working = PredictorSettlementEffects.TakeGold(Working, 100);
                var cards = Beta111EventResultCatalog.TrashHeapGrabCards;
                var selected = cards[_eventRng!.NextInt(cards.Count)];
                var definition = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key == selected);
                PredictorSettlementEffects.RequireImplementedCard(definition.Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, definition.Prototype);
                Accept(new("TrashHeapGrab", 0, [Working.Deck[^1]], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("TRASH_HEAP", "DIVE_IN"):
                ApplyEventHealth(-8, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                var fixedRelics = Beta111EventResultCatalog.TrashHeapDiveRelics;
                var fixedRelic = fixedRelics[_eventRng!.NextInt(fixedRelics.Count)];
                Accept(new("TrashHeapRelic", 0, [], null, null));
                if (BeginObtainRelic(fixedRelic, null, PredictorObtainReturn.Event, 1, token))
                    ResumeEventAfterRelicObtain(1);
                return PredictorInputResult.Accepted;
            case ("SYMBIOTE", "APPROACH"):
                BeginEventCardSelection(SymbioteCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("TINKER_TIME", "CHOOSE_CARD_TYPE"):
                var types = new List<string> { "ATTACK", "SKILL", "POWER" };
                _eventRng!.UnstableShuffle(types);
                EventOptions = types.Take(2).Select(t => new PredictorEventOption(t)).ToImmutableArray();
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("TRIAL", "REJECT"):
                EventOptions = [new("ACCEPT"), new("DOUBLE_DOWN")];
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("TRIAL", "DOUBLE_DOWN"):
                _trialAbandonPending = true;
                _trialAbandonOptions = EventOptions;
                EventOptions = [];
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("TRIAL", "ACCEPT"):
                EventOptions = _eventRng!.NextInt(3) switch
                {
                    0 => [new("MERCHANT_GUILTY"), new("MERCHANT_INNOCENT")],
                    1 => [new("NOBLE_GUILTY"), new("NOBLE_INNOCENT")],
                    _ => [new("NONDESCRIPT_GUILTY"), new("NONDESCRIPT_INNOCENT")]
                };
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("TRIAL", "MERCHANT_INNOCENT"):
                AddTrialCurse("SHAME");
                var upgradable = Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToImmutableArray();
                if (upgradable.IsEmpty) FinishEvent();
                else BeginEventCardSelection(upgradable, Math.Min(2, upgradable.Length), token);
                return PredictorInputResult.Accepted;
            case ("TRIAL", "MERCHANT_GUILTY"):
                AddTrialCurse("REGRET");
                BeginTrialMerchantRelic(1, token);
                return PredictorInputResult.Accepted;
            case ("TRIAL", "NOBLE_GUILTY"):
                ApplyEventHealth(10, 0);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("TRIAL", "NOBLE_INNOCENT"):
                AddTrialCurse("REGRET");
                Working = PredictorSettlementEffects.TakeGold(Working, 300);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("TRIAL", "NONDESCRIPT_INNOCENT"):
                AddTrialCurse("DOUBT");
                var transforms = PredictorCardChanges.TransformCandidates(Context, Working);
                if (transforms.IsEmpty) FinishEvent();
                else BeginEventCardSelection(transforms, Math.Min(2, transforms.Length), token);
                return PredictorInputResult.Accepted;
            case ("TRIAL", "NONDESCRIPT_GUILTY"):
                AddTrialCurse("DOUBT");
                var trialRewards = PredictorRewardGeneration.Generate(Context, Working,
                    [new(PredictorRewardKind.Card), new(PredictorRewardKind.Card)], cancellationToken: token);
                _eventRewardContinuation = true;
                var trialInputs = _inputs;
                Phase = PredictorPhase.Complete;
                PrepareRewardNode(Working.Position, trialRewards.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Event }).ToImmutableArray(), trialRewards.State, token);
                _inputs = trialInputs;
                EventOptions = [];
                GenerationStages = trialRewards.Stages;
                return PredictorInputResult.Accepted;
            case ("COLORFUL_PHILOSOPHERS", _):
                var chosenPool = Context.Catalog!.CharacterPools.Single(p => p.Character.Entry == choice.OptionKey);
                var colorful = PredictorRewardGeneration.Generate(Context, Working,
                    new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare }
                        .Select(r => new PredictorRewardPlan(PredictorRewardKind.Card, CardCount: 3,
                            Odds: PredictorCardOdds.Uniform, Rarity: r,
                            CardPoolOverride: chosenPool.Cards, NoCardPoolModifications: true)),
                    cancellationToken: token);
                _eventRewardContinuation = true;
                var colorfulInputs = _inputs;
                Phase = PredictorPhase.Complete;
                PrepareRewardNode(Working.Position, colorful.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Event }).ToImmutableArray(), colorful.State, token);
                _inputs = colorfulInputs;
                EventOptions = [];
                GenerationStages = colorful.Stages;
                return PredictorInputResult.Accepted;
            case ("TINKER_TIME", "ATTACK"):
            case ("TINKER_TIME", "SKILL"):
            case ("TINKER_TIME", "POWER"):
                _tinkerType = choice.OptionKey switch { "ATTACK" => EffectCardType.Attack,
                    "SKILL" => EffectCardType.Skill, _ => EffectCardType.Power };
                var riders = (_tinkerType switch
                {
                    EffectCardType.Attack => new[] { "SAPPING", "VIOLENCE", "CHOKING" },
                    EffectCardType.Skill => new[] { "ENERGIZED", "WISDOM", "CHAOS" },
                    _ => new[] { "EXPERTISE", "CURIOUS", "IMPROVEMENT" }
                }).ToList();
                _eventRng!.UnstableShuffle(riders);
                EventOptions = riders.Take(2).Select(r => new PredictorEventOption(r)).ToImmutableArray();
                _requestId++;
                return PredictorInputResult.Accepted;
        }
        if (_eventKey!.Value.Entry == "TINKER_TIME" && _tinkerType != null)
        {
            var rider = Enum.Parse<PredictorMadScienceRider>(choice.OptionKey, true);
            var definition = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key.Entry == "MAD_SCIENCE");
            var card = definition.Prototype with { Type = _tinkerType.Value, TinkerTimeType = _tinkerType.Value,
                TinkerTimeRider = rider };
            PredictorSettlementEffects.RequireImplementedCard(card, context: Context);
            Working = PredictorObtainSources.AddCard(Context, Working, card);
            Accept(new("TinkerTimeCard", Working.Deck[^1], [], null, null));
            FinishEvent();
            return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> ColorfulOptions()
    {
        var catalog = Context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var ordered = new[] { "NECROBINDER", "IRONCLAD", "REGENT", "SILENT", "DEFECT" };
        var options = ordered.Where(entry => entry != Context.Character.Entry &&
            catalog.CharacterPools.Any(p => p.Character.Entry == entry)).ToList();
        while (options.Count > 3) options.RemoveAt(_eventRng!.NextInt(options.Count));
        return options.Select(entry => new PredictorEventOption(entry)).ToImmutableArray();
    }

    private ImmutableArray<long> SymbioteCandidates() => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        return card.Type == EffectCardType.Attack && card.Enchantment == null &&
            !PredictorCardChanges.Definition(Context, card.Key).Unplayable;
    }).ToImmutableArray();

    private void AddTrialCurse(string entry)
    {
        var prototype = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key.Entry == entry).Prototype;
        PredictorSettlementEffects.RequireImplementedCard(prototype, context: Context);
        Working = PredictorObtainSources.AddCard(Context, Working, prototype);
        Accept(new("TrialCurse", Working.Deck[^1], [], null, null));
    }

    private void BeginTrialMerchantRelic(int ordinal, CancellationToken token)
    {
        BeginEventFrontRelic(ordinal + 1, token);
    }

}
