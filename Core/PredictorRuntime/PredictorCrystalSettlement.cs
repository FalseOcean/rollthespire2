using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRewardKind { Gold, Card, Relic, Potion, SpecialCard, RemoveCard }
internal enum PredictorRewardOrigin { Combat, Event, SmallCapsule, BonesInternal }
internal enum PredictorRewardStatus { Open, Taken, Skipped, Aborted }
internal enum PredictorPhase { Rewards, ReplacePotion, Complete, OpeningChoice, TransformCards, RestSite, SmithCards, RestCookCards, EventChoice, EventCards, Shop, ChooseBundle, EventEligibility, CombatFacts, Victory, ObtainHealth, ObtainChooseCard, ObtainChooseManyCards, ObtainDeckCards, TreasureClosed, TreasureRelic, AncientChoice, RestHealFact, OpeningModifier, OpeningModifierCards, RoomEntryHealth, RewardRemoveCards }
internal enum PredictorInputResult { Accepted, Stale, Invalid, TooFull, NotAllowed }

internal sealed record PredictorReward(
    long Id, PredictorRewardKind Kind, PredictorRewardOrigin Origin, ModelKey Key,
    ImmutableArray<PredictorCard> Cards, int GoldMinimum = 0, int GoldMaximum = 0,
    bool PopulateGold = false, int GoldAmount = 0, PredictorRewardStatus Status = PredictorRewardStatus.Open,
    bool CardSelectionEntered = false, bool RelicSubscriptionActive = true, long? ContentInstanceId = null,
    bool ManuallySetCards = false)
{
    internal bool Mandatory => Origin == PredictorRewardOrigin.BonesInternal && Kind == PredictorRewardKind.Relic;
    public long SubscriptionOrder { get; init; }
    public bool IsWaxRelic { get; init; }
    public bool PreserveCardInstance { get; init; }
    public bool WasGoldStolenBack { get; init; }
    public bool CanSkip { get; init; } = true;
    public bool CanReroll { get; init; }
    public bool HasRerolled { get; init; }
    public PredictorRewardPlan? RerollRecipe { get; init; }
    public bool RerollIsCardReward { get; init; } = true;
}

internal abstract record PredictorInput;
internal sealed record TakeReward(long RewardId, long? CardId = null) : PredictorInput;
internal sealed record EnterCardSelection(long RewardId) : PredictorInput;
internal sealed record ExitCardSelection : PredictorInput;
internal sealed record SkipCardReward : PredictorInput;
internal sealed record LeaveRewards : PredictorInput;
internal sealed record SelectRemovalCards(ImmutableArray<long> CardIds) : PredictorInput;
internal sealed record CancelRewardCardRemoval : PredictorInput;
internal sealed record DiscardPotion(int Slot, long PotionId) : PredictorInput;
internal sealed record UseOutOfCombatPotion(int Slot, long PotionId) : PredictorInput;
internal sealed record ReplaceRewardPotion(int Slot, long PotionId) : PredictorInput;
internal sealed record CancelPotionReplacement : PredictorInput;
internal sealed record CorrectGoldBalance(int Amount) : PredictorInput;
internal sealed record ChooseOpeningRelic(ModelKey Relic) : PredictorInput;

internal sealed record PredictorRequest(Guid SessionId, long Revision, long RequestId, string NodeId, PredictorPhase Phase);
internal sealed record PredictorAcceptedInput(string Action, long ObjectId, ImmutableArray<long> Targets, int? BeforeGold, int? AfterGold);
internal sealed record PredictorCardRewardAlternative(long RewardId, long RelicId, string Key);

// A Crystal-only adapter over the donor's detached reward settlement.
// No route transitions, checkpoint navigation or live mutations are exposed.
internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorAcceptedInput> _inputs = [];
    private long _requestId;
    private long? _cardSelection;
    private readonly Guid _sessionId = Guid.NewGuid();
    private ModelKey? _eventKey;
    private RolltheSpire2.Core.Rng.Xoshiro256StarStar? _eventRng;
    internal PredictorContext Context { get; }
    internal PredictorState Working { get; private set; }
    internal ImmutableArray<PredictorReward> Rewards { get; private set; } = [];
    internal ImmutableArray<PredictorRewardStage> GenerationStages { get; private set; } = [];
    internal PredictorPhase Phase { get; private set; } = PredictorPhase.Complete;
    internal PredictorRequest? Request => Phase == PredictorPhase.Complete ? null : new(_sessionId, 0, _requestId, Working.Position.NodeId, Phase);

    internal PredictorRun(PredictorContext context, PredictorState state)
    {
        PredictorSeedState.ValidateContext(context);
        state.Validate();
        Context = context;
        Working = state;
    }

    internal PredictorInputResult Submit(PredictorRequest request, PredictorInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request == null || Phase == PredictorPhase.Complete || Request != request) return PredictorInputResult.Stale;
        if (TrySubmitCrystal(input, cancellationToken) is { } result) return result;
        if (Phase != PredictorPhase.Rewards) return PredictorInputResult.Invalid;
        if (input is EnterCardSelection enter)
        {
            int i = OpenReward(enter.RewardId);
            if (i < 0 || Rewards[i].Kind != PredictorRewardKind.Card || _cardSelection != null) return PredictorInputResult.Invalid;
            _ = GetCardRewardAlternatives(enter.RewardId);
            _cardSelection = enter.RewardId;
            Rewards = Rewards.SetItem(i, Rewards[i] with { CardSelectionEntered = true });
            Accept(new("EnterCardSelection", enter.RewardId, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is not TakeReward take) return PredictorInputResult.Invalid;
        int index = OpenReward(take.RewardId);
        if (index < 0 || (_cardSelection != null && _cardSelection != take.RewardId)) return PredictorInputResult.Invalid;
        var selected = Rewards[index];
        if (selected.Kind != PredictorRewardKind.Card && take.CardId != null) return PredictorInputResult.Invalid;
        // The solver verifies card taking; relics/potions are reward targets,
        // not a promise to simulate their subsequent obtain/choice effects.
        if (selected.Kind != PredictorRewardKind.Card) return PredictorInputResult.Invalid;
        var card = selected.Cards.FirstOrDefault(c => c.Id == take.CardId);
        if (card == null || !selected.CardSelectionEntered) return PredictorInputResult.Invalid;
        if (!Working.Cards.Any(c => c.Id == card.Id)) Working = Working with { Cards = Working.Cards.Add(card) };
        Working = PredictorObtainSources.AddExistingCard(Context, Working, card);
        MarkTaken(selected.Id);
        _cardSelection = null;
        Working.Validate();
        Accept(new("TakeReward", selected.Id, take.CardId is long cardId ? [cardId] : [], null, null));
        return PredictorInputResult.Accepted;
    }

    private void FinishEvent()
    {
        Working.Validate();
        Phase = PredictorPhase.Complete;
        _requestId++;
        ResetCrystalSphere();
        _eventKey = null;
        _eventRng = null;
    }
    private void PrepareRewardNode(PredictorPosition position, ImmutableArray<PredictorReward> rewards,
        PredictorState preparedState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Phase is not (PredictorPhase.Complete or PredictorPhase.OpeningChoice)) throw new InvalidOperationException("PredictorNodeStillOpen");
        if (string.IsNullOrWhiteSpace(position.NodeId) || position.Act < 0 || position.Floor < 0 || rewards.IsDefault) throw new ArgumentException("PredictorNodeInvalid");
        // These are implementation preconditions, not a production capability gate.
        // Never silently execute unaudited hooks as no-ops while adding content.
        PredictorSettlementEffects.RequireImplementedInventory(preparedState);
        var working = preparedState with { Position = position };
        var built = ImmutableArray.CreateBuilder<PredictorReward>();
        foreach (var prototype in rewards)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prototype == null || !Enum.IsDefined(prototype.Kind) || !Enum.IsDefined(prototype.Origin) || prototype.Cards.IsDefault ||
                (prototype.Kind is not (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) && prototype.Cards.Length != 0) ||
                (prototype.Kind == PredictorRewardKind.SpecialCard && prototype.Cards.Length != 1))
                throw new ArgumentException("PredictorRewardInvalid");
            if (prototype.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion && !prototype.Key.IsValid)
                throw new ArgumentException("PredictorRewardKeyMissing");
            if (prototype.Kind == PredictorRewardKind.Potion && prototype.Key.Category != "POTION")
                throw new ArgumentException("PredictorPotionKeyInvalid");
            long rewardId = working.NextInstanceId;
            working = working with { NextInstanceId = checked(rewardId + 1) };
            long? contentId = null;
            if (prototype.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion)
            {
                contentId = working.NextInstanceId;
                working = working with { NextInstanceId = checked(working.NextInstanceId + 1) };
            }
            var cards = ImmutableArray.CreateBuilder<PredictorCard>();
            foreach (var card in prototype.Cards)
            {
                PredictorSettlementEffects.RequireImplementedCard(card);
                if (card.UpgradeLevel < 0 || card.MaxUpgradeLevel < card.UpgradeLevel || !Enum.IsDefined(card.Type))
                    throw new ArgumentException("PredictorCardInvalid");
                if (prototype.PreserveCardInstance)
                {
                    if (prototype.Kind != PredictorRewardKind.SpecialCard || !working.Cards.Contains(card) || card.Removed || working.Deck.Contains(card.Id))
                        throw new ArgumentException("PredictorReturnedCardInstanceInvalid");
                    cards.Add(card);
                }
                else
                {
                    cards.Add(card with { Id = working.NextInstanceId, Removed = false });
                    working = working with { NextInstanceId = checked(working.NextInstanceId + 1) };
                }
            }
            if (prototype.Kind == PredictorRewardKind.Card && cards.Count == 0) throw new ArgumentException("PredictorCardOptionsMissing");
            int amount = prototype.GoldAmount;
            if (prototype.Kind == PredictorRewardKind.Gold && prototype.PopulateGold)
                (amount, working) = working.Draw(PredictorStream.Rewards, prototype.GoldMinimum, checked(prototype.GoldMaximum + 1));
            if (amount < 0) throw new ArgumentException("PredictorGoldRewardInvalid");
            built.Add(prototype with { Id = rewardId, Cards = cards.ToImmutable(), GoldAmount = amount,
                Status = PredictorRewardStatus.Open, CardSelectionEntered = false,
                RelicSubscriptionActive = prototype.Kind == PredictorRewardKind.Card && !prototype.ManuallySetCards, ContentInstanceId = contentId });
        }
        working.Validate();
        Working = working;
        Rewards = built.ToImmutable();
        _inputs = [];
        _cardSelection = null;
        Phase = PredictorPhase.Rewards;
        _requestId++;
    }
    internal ImmutableArray<PredictorCardRewardAlternative> GetCardRewardAlternatives(long rewardId)
    {
        int index = OpenReward(rewardId);
        if (index < 0) return [];
        var reward = Rewards[index];
        var alternatives = ImmutableArray.CreateBuilder<PredictorCardRewardAlternative>();
        if (reward.CanSkip) alternatives.Add(new(rewardId, 0, "SKIP"));
        if (reward.CanReroll) alternatives.Add(new(rewardId, 0, "REROLL"));
        foreach (var relic in Working.Relics.Where(r => !r.Melted && r.Key.Entry == "PAELS_WING"))
            alternatives.Add(new(rewardId, relic.Id, "SACRIFICE"));
        if (alternatives.Count > 2)
            throw new InvalidOperationException("More than 2 card reward alternatives are not supported.");
        return alternatives.ToImmutable();
    }
    private int OpenReward(long id) => Rewards.FindIndex(r => r.Id == id && r.Status == PredictorRewardStatus.Open);
    private void MarkTaken(long id)
    {
        int i = OpenReward(id);
        Rewards = Rewards.SetItem(i, Rewards[i] with { Status = PredictorRewardStatus.Taken, CardSelectionEntered = false, RelicSubscriptionActive = false });
    }
    private void Accept(PredictorAcceptedInput input) { _inputs = _inputs.Add(input); _requestId++; }
    private void SpendEventGold(int amount)
    {
        int before = Working.GoldBudget;
        Working = Working with { GoldBudget = Math.Max(0, before - amount) };
        Accept(new("EventGoldSpent", 0, [], before, Working.GoldBudget));
    }
}
