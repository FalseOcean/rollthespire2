using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    internal const long MuseumRelicSource = 34;
    internal const long WaterfallRelicSource = 35;
    internal const long ThermaeRelicSource = 36;
    private int _operationEventFixedCurseCount;

    private PredictorInputResult SubmitModernOperationEvent(ChooseEventOption choice, PredictorEventOption option, CancellationToken token)
    {
        if (Context.Operation == null || !Context.UsesNonCombatGeneration)
            return PredictorInputResult.NotAllowed;
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("MUSEUM_OF_TORTURE", "TAKE"):
                ObtainOperationEventRelic(option, MuseumRelicSource, token);
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
