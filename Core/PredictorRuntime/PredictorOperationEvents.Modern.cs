using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    internal const long MuseumRelicSource = 34;
    internal const long WaterfallRelicSource = 35;
    internal const long ThermaeRelicSource = 36;
    private const long WuppyRelicSource = 37;
    private const long ShrineRelicSource = 38;
    private int _operationEventFixedCurseCount;

    private ImmutableArray<PredictorEventOption> ModernEventOptions(string entry)
    {
        // Museum's two CONTINUE pages and Waterfall's ENTER page only change presentation.
        _operationEventFixedCurseCount = entry switch { "WATERFALL_OF_OBVIOUS_SECRETS" => 2, "THERMAE_NONUPEIPE" => 3, _ => 0 };
        return entry switch
        {
            "MUSEUM_OF_TORTURE" => [new("TAKE", OfferedRelic: Beta111EventRules.MuseumOfferedRelic(_eventRng!)), FixedEventHealthOption("BREAK_OUT")],
            "SHRINE_OF_THE_WARLORD" => [new("TAKE_WEAPON"), new("TAKE_SHIELD"), new("TAKE_WINGS", OfferedRelic: new("RELIC", "MIGHTY_WINGS"))],
            "WATERFALL_OF_OBVIOUS_SECRETS" => [new("BE_NICE"), new("BEAT_UP"), ModernPurchaseOption("NEGOTIATE")],
            "WUPPY_IN_TROUBLE" => [new("HELP_IMMEDIATELY", OfferedRelic: new("RELIC", "WUPPY")),
                new("BE_CAUTIOUS", HealthDamage: _eventHealth is { } hp ? hp.CurrentHp / 20 : null, OfferedRelic: new("RELIC", "WUPPY")), new("IGNORE")],
            "THERMAE_NONUPEIPE" => [ModernPurchaseOption("THERMAL_BATHS") with { OfferedRelic = new("RELIC", "BATH_TOWEL") },
                new("PRIVATE_OASIS", OfferedRelic: new("RELIC", "BATH_ROBES")),
                FixedEventHealthOption("DRINK_WATERS") with { OfferedRelic = Beta111EventRules.ThermaeDrinkRelic(_eventRng!) }],
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }

    private PredictorEventOption ModernPurchaseOption(string key)
    {
        if (!Beta111EventOptionRules.TryGoldCost(_eventKey!.Value.Entry, key, out var cost))
            throw new InvalidOperationException("PredictorEventGoldCostMissing:" + key);
        return new(key, cost.CanChoose(Working.GoldBudget), GoldCost: cost.Amount);
    }

    private PredictorInputResult SubmitModernEvent(ChooseEventOption choice, PredictorEventOption option, CancellationToken token)
    {
        if (!Context.UsesNonCombatGeneration)
            return PredictorInputResult.NotAllowed;
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("MUSEUM_OF_TORTURE", "TAKE"):
                ObtainOperationEventRelic(option, MuseumRelicSource, token);
                break;
            case ("MUSEUM_OF_TORTURE", "BREAK_OUT"):
                ApplyFixedEventHealth(choice.OptionKey);
                if (Phase != PredictorPhase.Complete) FinishEvent();
                break;
            case ("SHRINE_OF_THE_WARLORD", "TAKE_WEAPON"):
            case ("SHRINE_OF_THE_WARLORD", "TAKE_SHIELD"):
            case ("WATERFALL_OF_OBVIOUS_SECRETS", "BE_NICE"):
                var card = choice.OptionKey switch { "TAKE_WEAPON" => "MIGHTY_WEAPON", "TAKE_SHIELD" => "MIGHTY_SHIELD", _ => "DINGY_DAGGER" };
                Working = PredictorObtainSources.AddCard(Context, Working, PredictorCardChanges.Definition(Context, new("CARD", card)).Prototype);
                Accept(new("EventCardAdded", Working.Deck[^1], [], null, null));
                FinishEvent();
                break;
            case ("SHRINE_OF_THE_WARLORD", "TAKE_WINGS"):
                AddTrialCurse("CLUMSY");
                ObtainOperationEventRelic(option, ShrineRelicSource, token);
                break;
            case ("WATERFALL_OF_OBVIOUS_SECRETS", "BEAT_UP"):
                BeginEventFrontRelic(WaterfallRelicSource, token);
                break;
            case ("WATERFALL_OF_OBVIOUS_SECRETS", "NEGOTIATE"):
                SpendEventGold(option.GoldCost ?? throw new InvalidDataException("OperationWaterfallNegotiationCostMissing"));
                var state = Working;
                var cards = PredictorRewardGeneration.GenerateCards(Context, ref state,
                    new(PredictorRewardKind.Card, CardCount: 1, Odds: PredictorCardOdds.Uniform,
                        Rarity: EffectCardRarity.Rare, NoUpgradeRoll: true), isCardReward: false);
                Working = PredictorObtainSources.AddCard(Context, state, cards[0]);
                Accept(new("WaterfallNegotiatedCard", Working.Deck[^1], [], null, null));
                FinishEvent();
                break;
            case ("WUPPY_IN_TROUBLE", "HELP_IMMEDIATELY"):
                AddTrialCurse("INJURY");
                ObtainOperationEventRelic(option, WuppyRelicSource, token);
                break;
            case ("WUPPY_IN_TROUBLE", "BE_CAUTIOUS"):
                if (option.HealthDamage is int damage) ApplyEventHealth(-damage, 0);
                else if (_eventHealth != null) ApplyEventHealth(-(_eventHealth.CurrentHp / 20), 0);
                else if (_preview.Event.ProjectHealthIndependently) _preview.Event.UnknownNonlethalHealthAmount = true;
                else throw new InvalidOperationException("PredictorEventHealthFactRequired");
                if (Phase != PredictorPhase.Complete) ObtainOperationEventRelic(option, WuppyRelicSource, token);
                break;
            case ("WUPPY_IN_TROUBLE", "IGNORE"):
                var pool = Context.PotionPool;
                if (pool.IsEmpty) FinishEvent();
                else
                {
                    var draw = Working.Draw(PredictorStream.Rewards, 0, pool.Length);
                    Working = draw.State;
                    OfferEventRewardPlans([new(PredictorRewardKind.Potion, PrefilledPotionKey: pool[draw.Value].Key)], token);
                }
                break;
            case ("THERMAE_NONUPEIPE", "THERMAL_BATHS"):
            case ("THERMAE_NONUPEIPE", "PRIVATE_OASIS"):
                SpendEventGold(choice.OptionKey == "PRIVATE_OASIS" ? Working.GoldBudget :
                    option.GoldCost ?? throw new InvalidDataException("PredictorThermaeCostMissing"));
                ObtainOperationEventRelic(option, ThermaeRelicSource, token);
                break;
            case ("THERMAE_NONUPEIPE", "DRINK_WATERS"):
                ApplyEventHealth(-(option.HealthDamage ?? throw new InvalidDataException("OperationThermaeDamageMissing")), 0);
                if (Phase != PredictorPhase.Complete)
                {
                    AddOperationEventFixedCurses();
                    ObtainOperationEventRelic(option, ThermaeRelicSource, token);
                }
                break;
            default:
                return PredictorInputResult.NotAllowed;
        }
        return PredictorInputResult.Accepted;
    }

    private void ObtainOperationEventRelic(PredictorEventOption option, long source, CancellationToken token)
    {
        var relic = option.OfferedRelic ?? throw new InvalidDataException("OperationEventOfferedRelicMissing:" + _eventKey);
        if (BeginObtainRelic(relic, null, PredictorObtainReturn.Event, source, token))
            ResumeEventAfterRelicObtain(source);
    }

    private bool ResumeModernOperationEventObtain(long source)
    {
        switch (_eventKey?.Entry, source)
        {
            case ("MUSEUM_OF_TORTURE", MuseumRelicSource):
                var curses = Context.Catalog!.UnlockedCurseCards;
                if (curses.IsEmpty) throw new InvalidDataException("OperationMuseumCursePoolMissing");
                var draw = Working.Draw(PredictorStream.NonCombatGeneration, 0, curses.Length);
                Working = PredictorObtainSources.AddCard(Context, draw.State, curses[draw.Value]);
                Accept(new("MuseumRandomCurse", Working.Deck[^1], [], null, null));
                break;
            case ("WATERFALL_OF_OBVIOUS_SECRETS", WaterfallRelicSource):
                AddOperationEventFixedCurses();
                break;
            case ("THERMAE_NONUPEIPE", ThermaeRelicSource):
            case ("WUPPY_IN_TROUBLE", WuppyRelicSource):
            case ("SHRINE_OF_THE_WARLORD", ShrineRelicSource):
                break;
            default:
                return false;
        }
        FinishEvent();
        return true;
    }

    private void AddOperationEventFixedCurses()
    {
        for (int i = 0; i < _operationEventFixedCurseCount; i++)
            AddTrialCurse("GUILTY");
    }
}
