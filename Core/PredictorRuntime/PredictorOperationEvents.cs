using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Captured values belong to the current native transaction, not event entry.
internal sealed record PredictorOperationEventScene(ModelKey Event, PredictorStreamState Rng,
    ImmutableArray<PredictorEventOption> Options, PredictorEventHealthFact Health) : PredictorOperationScene
{
    public string? Branch { get; init; }
    public int GoldAmount { get; init; }
    public long? BridgeCard { get; init; }
    public int BridgeHoldOns { get; init; }
    public ImmutableHashSet<long> BridgeSkipped { get; init; } = [];
    public int TabletCount { get; init; }
    public EffectCardType? TinkerType { get; init; }
    public ImmutableDictionary<long, EffectCardType> FuturePotionTypes { get; init; } = ImmutableDictionary<long, EffectCardType>.Empty;
    public ImmutableDictionary<long, EffectCardRarity> FuturePotionRarities { get; init; } = ImmutableDictionary<long, EffectCardRarity>.Empty;
    public long? RanwidPotion { get; init; }
    public long? RanwidRelic { get; init; }
    public ModelKey? WongoFeatured { get; init; }
    public int WongoPointsEarned { get; init; }
    public bool RepySecondPending { get; init; }
    public string? ConveyorDish { get; init; }
    public string? ConveyorLastDish { get; init; }
    public int ConveyorGrabs { get; init; }
    public bool ConveyorHadGrab { get; init; }
    public ModelKey? LayoutEncounter { get; init; }
    public PredictorOperationRewardsScene? RewardScene { get; init; }
    public ImmutableArray<PredictorCard> OfferedCards { get; init; } = [];
    public ImmutableArray<long> CardCandidates { get; init; } = [];
    public ImmutableArray<long> SelectedCardIds { get; init; } = [];
    public int CardsRequired { get; init; }
    public bool AwaitingCards { get; init; }
    public bool MimicRestRewards { get; init; }
    public bool TrialAbandonPending { get; init; }
    internal override void Restore(PredictorRun run) => run.RestoreOperationEvent(this);
}

internal sealed partial class PredictorRun
{
    private ImmutableDictionary<long, EffectCardRarity> _operationFuturePotionRarities = ImmutableDictionary<long, EffectCardRarity>.Empty;
    internal void RestoreOperationEvent(PredictorOperationEventScene scene)
    {
        RequireEmptyOperationScene();
        if (Context.Operation == null || scene.Event.Category != "EVENT" || !scene.Event.IsValid ||
            scene.Options.IsDefault || scene.Health.CurrentHp < 1 || scene.Health.MaxHp < scene.Health.CurrentHp ||
            scene.TabletCount is < 0 or > 4 || scene.BridgeHoldOns < 0 || scene.ConveyorGrabs < 0 ||
            scene.CardCandidates.IsDefault || scene.OfferedCards.IsDefault || scene.SelectedCardIds.IsDefault ||
            scene.CardCandidates.Distinct().Count() != scene.CardCandidates.Length ||
            scene.SelectedCardIds.Distinct().Count() != scene.SelectedCardIds.Length ||
            scene.SelectedCardIds.Any(id => !scene.CardCandidates.Contains(id)) ||
            scene.AwaitingCards && (scene.Branch == null || scene.CardsRequired < 1 ||
                scene.CardsRequired > scene.CardCandidates.Length || scene.RewardScene != null) ||
            scene.CardCandidates.Any(id => !Working.Deck.Contains(id) && !scene.OfferedCards.Any(c => c.Id == id)) ||
            scene.BridgeCard is { } bridge && !Working.Deck.Contains(bridge))
            throw new InvalidDataException("PredictorOperationEventSceneInvalid");
        // Restore the actual reward screen first, while the transaction is empty.
        scene.RewardScene?.Restore(this);
        _eventKey = scene.Event;
        _eventRng = scene.Rng.Restore();
        ResetEventChoiceState(3, true);
        _eventHealth = scene.Health;
        _eventBranch = scene.Branch;
        _eventGoldAmount = scene.GoldAmount;
        _bridgeCard = scene.BridgeCard;
        _bridgeHoldOns = scene.BridgeHoldOns;
        _bridgeSkipped = scene.BridgeSkipped;
        _tabletCount = scene.TabletCount;
        _tinkerType = scene.TinkerType;
        _futurePotionTypes = scene.FuturePotionTypes;
        _operationFuturePotionRarities = scene.FuturePotionRarities;
        _ranwidPotionId = scene.RanwidPotion;
        _ranwidRelicId = scene.RanwidRelic;
        if (scene.Event.Entry == "RELIC_TRADER")
        {
            var trades = new[] { "TOP", "MIDDLE", "BOTTOM" }
                .Select(key => scene.Options.SingleOrDefault(o => o.Key == key)).Where(o => o != null).ToArray();
            if (trades.Any(o => o!.RelicId == null || o.OfferedRelic == null))
                throw new InvalidDataException("PredictorOperationRelicTraderOffersMissing");
            _traderOwned = trades.Select(o => o!.RelicId!.Value).ToImmutableArray();
            _traderOffers = trades.Select(o => o!.OfferedRelic!.Value).ToImmutableArray();
        }
        _wongoFeatured = scene.WongoFeatured;
        _wongoPointsEarned = scene.WongoPointsEarned;
        _repySecondPending = scene.RepySecondPending;
        _conveyorDish = scene.ConveyorDish;
        _conveyorLastDish = scene.ConveyorLastDish;
        _conveyorGrabs = scene.ConveyorGrabs;
        _conveyorHadGrab = scene.ConveyorHadGrab;
        _eventLayoutEncounter = scene.LayoutEncounter;
        _eventOfferedCards = scene.OfferedCards;
        _eventCardCandidates = scene.CardCandidates;
        _eventCardsRequired = scene.CardsRequired;
        _eventRewardContinuation = scene.RewardScene != null && !scene.MimicRestRewards;
        if (scene.MimicRestRewards)
        {
            if (scene.Event.Entry != "DENSE_VEGETATION" || scene.Branch != "REST" || scene.RewardScene == null)
                throw new InvalidDataException("PredictorOperationMimicRestParentInvalid");
            _obtainFrames = _obtainFrames.Add(new(new("RELIC", "MIMIC_REST_HEAL_REWARDS"),
                PredictorObtainReturn.EventRestHeal, 21, [], PredictorPhase.EventChoice, 0, false));
        }
        EventOptions = scene.Options;
        if (scene.TrialAbandonPending)
        {
            if (scene.Event.Entry != "TRIAL" || scene.RewardScene != null || scene.AwaitingCards)
                throw new InvalidDataException("PredictorOperationTrialAbandonParentInvalid");
            _trialAbandonPending = true;
            _trialAbandonOptions = scene.Options;
            EventOptions = [];
        }
        if (scene.RewardScene == null)
            Phase = scene.AwaitingCards ? PredictorPhase.EventCards : PredictorPhase.EventChoice;
        _requestId++;
    }

}
