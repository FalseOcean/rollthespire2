using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRewardKind { Gold, Card, Relic, Potion, SpecialCard, RemoveCard, Opaque }
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
    public bool GoldAmountUnspecified { get; init; }
    public bool GoldNetRestored { get; init; }
    internal bool HasValidGoldDisposition => !GoldAmountUnspecified && !GoldNetRestored ||
        GoldAmountUnspecified && GoldNetRestored && Kind == PredictorRewardKind.Gold && WasGoldStolenBack &&
        Status == PredictorRewardStatus.Taken && !CanSkip && !PopulateGold && GoldAmount == 0;
    public bool CanSkip { get; init; } = true;
    public bool CanReroll { get; init; }
    public bool HasRerolled { get; init; }
    public PredictorRewardPlan? RerollRecipe { get; init; }
    public bool RerollIsCardReward { get; init; } = true;
    public string? OpaqueSourceType { get; init; }
    public string? OpaqueSourceAssembly { get; init; }
    // Source observation for detached knowledge only, not part of saved intent
    // or reward identity. An observed reward loaded from a snapshot is already
    // concrete; newly generated/replayed offers recompute this scope.
    [System.Text.Json.Serialization.JsonIgnore]
    public PredictorCardRewardNicheScope? CardRewardNicheScope { get; init; }
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
internal sealed record PredictorNodeRecord(string NodeId, ImmutableArray<PredictorAcceptedInput> Inputs,
    ImmutableArray<PredictorReward> Rewards)
{
    public ImmutableArray<PredictorHealthEffect> HealthEffects { get; init; } = [];
}
internal sealed record PredictorAcceptedInput(string Action, long ObjectId, ImmutableArray<long> Targets, int? BeforeGold, int? AfterGold)
{
    public ImmutableArray<PredictorPotion> GeneratedPotions { get; init; } = [];
    public ImmutableArray<PredictorCardTrigger> CombatCardTriggers { get; init; } = [];
    public ImmutableArray<PredictorTemporaryCardTrigger> CombatTemporaryCardTriggers { get; init; } = [];
    public ImmutableArray<PredictorCombatCardSourceRef> CombatCardSourceOrder { get; init; } = [];
    public ImmutableArray<PredictorHealthConsequence> HealthConsequences { get; init; } = [];
    public ImmutableArray<PredictorTransformation> Transformations { get; init; } = [];
    public ImmutableArray<PredictorPotionAttempt> PotionAttempts { get; init; } = [];
    public ImmutableArray<PredictorEventHealthDelta> EventHealthConsequences { get; init; } = [];
    public PredictorEventHealthFact? EventHealthFact { get; init; }
    public PredictorRestHealOutcome? RestHealOutcome { get; init; }
    public PredictorRelic? RemovedRelic { get; init; }
    public bool? CombatTookDamage { get; init; }
    public ImmutableArray<PredictorCombatRelicEffect> CombatRelicEffects { get; init; } = [];
    public PredictorPotionHealthEffect? PotionHealthEffect { get; init; }
    public PredictorCombatPotionFacts? CombatPotionFacts { get; init; }
    public ImmutableArray<PredictorEnemyCreationFact> CombatEnemyCreations { get; init; } = [];
    public PredictorStolenCardFacts? CombatStolenCards { get; init; }
    public PredictorGremlinGoldFacts? CombatGremlinGold { get; init; }
    public ImmutableArray<PredictorCombatExtraRewardRef> CombatExtraRewardOrder { get; init; } = [];
    public bool FurCoatMarkedEnemies { get; init; }
    public int? SourceMaximumHp { get; init; }
    public bool? FirstPlayerTurnOccurred { get; init; }
    public PredictorEventHealthFact? VictoryHealthFact { get; init; }
}
internal sealed record PredictorCheckpoint(PredictorState State, PredictorNodeRecord? CompletedNode);
internal sealed record PredictorSelection(long? ParentRewardId, long? CardRewardId, int RequiredCardCount,
    ImmutableArray<long> CandidateCardIds)
{
    public int MinimumCardCount { get; init; } = RequiredCardCount;
    public int MaximumCardCount { get; init; } = RequiredCardCount;
}

// Explicit settlement for source-generated and controlled populated rewards.
// Opening/combat use the private prepared-state boundary. Route UI enters via
// AdvanceRoute and Submit; source handlers retain their actual vanilla stages.
internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorCheckpoint> _checkpoints;
    private ImmutableArray<PredictorAcceptedInput> _inputs = [];
    private long _requestId;
    private long? _parentReward;
    private long? _cardSelection;
    private long? _rewardRemovalId;
    private readonly Guid _sessionId = Guid.NewGuid();
    // Execution intent is separate from Crystal's captured best-effort policy.
    // A solver fork of a strict route snapshot is still only a local simulation.
    private bool _localCrystalSimulation;

    internal PredictorContext Context { get; }
    internal PredictorState Working { get; private set; }
    internal ImmutableArray<PredictorReward> Rewards { get; private set; } = [];
    internal ImmutableArray<PredictorCheckpoint> Checkpoints
    {
        get
        {
            // Local solver state is not a committed route boundary, including
            // forks whose strict source Context carries no Crystal metadata.
            RequireRouteSimulation();
            return _checkpoints;
        }
    }
    internal PredictorPhase Phase { get; private set; } = PredictorPhase.Complete;
    internal long Revision { get; private set; }
    internal PredictorRequest? Request => Phase == PredictorPhase.Complete ? null : new(_sessionId, Revision, _requestId, Working.Position.NodeId, Phase);
    internal PredictorSelection Selection => new(Phase == PredictorPhase.ObtainDeckCards && _obtainDeckPending?.Destination == PredictorObtainReturn.Reward
        ? _obtainDeckPending.SourceId : Phase == PredictorPhase.RewardRemoveCards ? _rewardRemovalId : _parentReward, _cardSelection,
        Phase == PredictorPhase.ObtainDeckCards ? _obtainDeckCount : Phase == PredictorPhase.RestCookCards ? 2 : Phase is PredictorPhase.TransformCards or PredictorPhase.SmithCards or PredictorPhase.RewardRemoveCards ? 1 : 0,
        Phase == PredictorPhase.ObtainDeckCards ? ObtainDeckCandidates :
        Phase == PredictorPhase.TransformCards ? OperationTransformCandidates() :
        Phase == PredictorPhase.SmithCards ? Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToImmutableArray() :
        Phase is PredictorPhase.RestCookCards or PredictorPhase.RewardRemoveCards ? Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray() : [])
        { MinimumCardCount = Phase == PredictorPhase.ObtainDeckCards && (_obtainDeckOperation == PredictorObtainDeckOperation.Claws ||
                _obtainDeckSource.Entry is "GNARLED_HAMMER" or "KIFUDA") ? 0 :
            Phase == PredictorPhase.ObtainDeckCards ? _obtainDeckCount : Phase == PredictorPhase.RestCookCards ? 2 : Phase is PredictorPhase.TransformCards or PredictorPhase.SmithCards or PredictorPhase.RewardRemoveCards ? 1 : 0 };

    internal PredictorRun(PredictorContext context, PredictorState initializedBoundary)
        : this(context, [new(initializedBoundary, null)], 0) { }

    internal PredictorRun(PredictorContext context, ImmutableArray<PredictorCheckpoint> checkpoints, long revision)
    {
        PredictorSeedState.ValidateContext(context);
        if (checkpoints.IsDefaultOrEmpty || revision < 0) throw new InvalidDataException("PredictorCheckpointMissing");
        if (context.Crystal is not null && (checkpoints.Length != 1 || revision != 0))
            throw new InvalidDataException("PredictorLocalCrystalCheckpointInvalid");
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint?.State == null) throw new InvalidDataException("PredictorCheckpointInvalid");
            checkpoint.State.Validate();
            if (checkpoint.CompletedNode is { } node && (node.NodeId != checkpoint.State.Position.NodeId ||
                node.Inputs.IsDefault || node.Rewards.IsDefault ||
                node.Inputs.Any(i => i == null || string.IsNullOrEmpty(i.Action) || i.Targets.IsDefault || i.GeneratedPotions.IsDefault ||
                    i.CombatCardTriggers.IsDefault || i.HealthConsequences.IsDefault || i.Transformations.IsDefault || i.PotionAttempts.IsDefault || i.EventHealthConsequences.IsDefault ||
                    i.CombatCardTriggers.Any(t => t == null || t.InstanceId <= 0 || t.EffectiveTriggers < 0) ||
                    i.CombatTemporaryCardTriggers.IsDefault || i.CombatCardSourceOrder.IsDefault ||
                    i.CombatTemporaryCardTriggers.Any(t => t == null || t.EffectiveTriggers < 0 || t.UpgradeLevel < 0 || t.Card.Category != "CARD") ||
                    i.HealthConsequences.Any(h => h == null || (h.TemporarySourceIndex is { } ti
                        ? h.SourceCardId != 0 || ti < 0 || ti >= i.CombatTemporaryCardTriggers.Length
                        : h.SourceCardId <= 0))) ||
                node.Rewards.Any(r => r == null || r.Status == PredictorRewardStatus.Open || r.Cards.IsDefault || !r.HasValidGoldDisposition)))
                throw new InvalidDataException("PredictorNodeRecordInvalid");
        }
        if (checkpoints[0].CompletedNode != null || checkpoints.Skip(1).Any(c => c.CompletedNode == null))
            throw new InvalidDataException("PredictorCheckpointSequenceInvalid");
        if (checkpoints.Skip(1).Select(c => c.CompletedNode!.NodeId).Distinct(StringComparer.Ordinal).Count() != checkpoints.Length - 1)
            throw new InvalidDataException("PredictorDuplicateNode");
        Context = context;
        _localCrystalSimulation = context.Crystal is not null;
        _checkpoints = checkpoints;
        Working = checkpoints[^1].State;
        Revision = revision;
    }

    internal void EnterRewardNode(PredictorPosition position, ImmutableArray<PredictorReward> rewards,
        CancellationToken cancellationToken = default)
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete) throw new InvalidOperationException("PredictorNodeStillOpen");
        PrepareRewardNode(position, rewards, Working, cancellationToken);
    }

    private void PrepareRewardNode(PredictorPosition position, ImmutableArray<PredictorReward> rewards,
        PredictorState preparedState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Phase is not (PredictorPhase.Complete or PredictorPhase.OpeningChoice)) throw new InvalidOperationException("PredictorNodeStillOpen");
        if (string.IsNullOrWhiteSpace(position.NodeId) || position.Act < 0 || position.Floor < 0 || rewards.IsDefault ||
            _checkpoints.Any(c => c.CompletedNode?.NodeId == position.NodeId)) throw new ArgumentException("PredictorNodeInvalid");
        // These are implementation preconditions, not a production capability gate.
        // Never silently execute unaudited hooks as no-ops while adding content.
        PredictorSettlementEffects.RequireImplementedInventory(preparedState, Context.Crystal is not null, Context);
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
            if (!prototype.HasValidGoldDisposition)
                throw new ArgumentException("PredictorRecoveredGoldNetRestoreInvalid");
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
                PredictorSettlementEffects.RequireImplementedCard(card, Context.Crystal is not null, Context);
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
                Status = prototype.GoldNetRestored ? PredictorRewardStatus.Taken : PredictorRewardStatus.Open, CardSelectionEntered = false,
                RelicSubscriptionActive = prototype.Kind == PredictorRewardKind.Card && !prototype.ManuallySetCards, ContentInstanceId = contentId });
        }
        working.Validate();
        Working = working;
        Rewards = built.ToImmutable();
        _inputs = [];
        _parentReward = _cardSelection = _rewardRemovalId = null;
        Phase = PredictorPhase.Rewards;
        _requestId++;
    }

    internal PredictorInputResult Submit(PredictorRequest request, PredictorInput input, CancellationToken cancellationToken = default)
    {
        if (_localCrystalSimulation) return SubmitLocalCrystal(request, input, cancellationToken);
        var beforeRequest = Request;
        int beforeGold = Working.GoldBudget;
        var prefix = _nodeReplayInputs;
        var result = SubmitCore(request, input, cancellationToken);
        if (result is PredictorInputResult.Accepted or PredictorInputResult.NotAllowed) DrainObtainCompletions(cancellationToken);
        if (result == PredictorInputResult.Accepted && Phase == PredictorPhase.EventChoice &&
            input is DiscardPotion or UseOutOfCombatPotion or ReplaceRewardPotion)
            RefreshEventOptionsAfterPotionMutation();
        if (_nodeReplayAvailable && input is not AdjustCurrentCombat &&
            result is not (PredictorInputResult.Invalid or PredictorInputResult.Stale) && Phase != PredictorPhase.Complete)
        {
            _nodeReplayInputs = _nodeReplayInputs.Add(input);
            if (input is ResolveCombat combat && Phase == PredictorPhase.Rewards)
            { _combatReplayPrefix = prefix; _combatInputForEditing = combat; }
        }
        if (result is not (PredictorInputResult.Invalid or PredictorInputResult.Stale) && Request != beforeRequest &&
            !(input is CorrectGoldBalance correction && correction.Amount == beforeGold))
            CompleteRouteRedrawUndo();
        return result;
    }

    private PredictorInputResult SubmitCore(PredictorRequest request, PredictorInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request == null || Phase == PredictorPhase.Complete || Request != request) return PredictorInputResult.Stale;
        if (input is AdjustCurrentCombat) return AdjustCombat(cancellationToken);
        if (input is CorrectGoldBalance correction)
        {
            if (correction.Amount < 0) return PredictorInputResult.Invalid;
            int before = Working.GoldBudget;
            Working = Working with { GoldBudget = correction.Amount };
            Accept(new("CorrectGoldBalance", 0, [], before, correction.Amount));
            if (Phase == PredictorPhase.EventChoice) RefreshEventOptionsAfterBudgetCorrection();
            return PredictorInputResult.Accepted;
        }
        if (Phase == PredictorPhase.Victory)
        {
            if (input is not ContinueVictory) return PredictorInputResult.Invalid;
            Working = Working with { Outcome = PredictorRunOutcome.Victory };
            Accept(new("TheArchitectVictory", 0, [], null, null));
            CompleteNode();
            return PredictorInputResult.Accepted;
        }
        if (Phase == PredictorPhase.RoomEntryHealth)
            return input is ProvideRoomMaximumHp health ? SubmitRoomMaximumHp(health) : PredictorInputResult.Invalid;
        if (Phase is PredictorPhase.OpeningModifier or PredictorPhase.OpeningModifierCards) return SubmitModifierOpening(input);
        if (Phase == PredictorPhase.OpeningChoice)
            return input is ChooseOpeningRelic opening ? ChooseOpening(opening.Relic, cancellationToken) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.EventEligibility)
            return input switch
            {
                DeclareEventEligibility eligibility => SubmitEventEligibility(eligibility),
                ChooseEventOccurrence occurrence => SubmitEventOccurrence(occurrence),
                _ => PredictorInputResult.Invalid
            };
        if (Phase == PredictorPhase.CombatFacts)
            return input is ResolveCombat combat ? _eventCombatEntry != null ? SubmitEventCombat(combat, cancellationToken)
                : SubmitRouteCombat(combat, cancellationToken) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.AncientChoice)
            return input is ChooseAncientOffer ancient ? SubmitAncient(ancient, cancellationToken) : PredictorInputResult.Invalid;
        if (input is DiscardPotion or UseOutOfCombatPotion)
            return ApplyPotionInventoryInput(input, cancellationToken);
        if (Phase == PredictorPhase.ReplacePotion)
        {
            if (input is CancelPotionReplacement)
            {
                _parentReward = null;
                Phase = PredictorPhase.Rewards;
                Accept(new("CancelPotionReplacement", 0, [], null, null));
                return PredictorInputResult.Accepted;
            }
            if (input is not ReplaceRewardPotion replacement || !MatchesPotion(replacement.Slot, replacement.PotionId))
                return PredictorInputResult.Invalid;
            long reward = _parentReward!.Value;
            Working = Working with { PotionSlots = Working.PotionSlots.SetItem(replacement.Slot, null) };
            Procure(reward);
            Phase = PredictorPhase.Rewards;
            _parentReward = null;
            Accept(new("ReplaceRewardPotion", reward, [replacement.PotionId], null, null));
            return PredictorInputResult.Accepted;
        }
        if (Phase == PredictorPhase.TransformCards)
            return input is SelectTransformationCard transform ? ApplyNewLeaf(transform.CardId, cancellationToken) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.ChooseBundle)
            return input is ChooseScrollBoxBundle bundle ? SubmitScrollBoxBundle(bundle) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.ObtainHealth)
            return input is ProvideEventHealthFact health ? SubmitLeafyHealth(health, cancellationToken) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.ObtainChooseCard)
            return input is ChooseObtainCard choice ? SubmitObtainCard(choice) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.ObtainDeckCards)
            return input switch { ChooseObtainDeckCards deckChoice => SubmitObtainDeckCards(deckChoice),
                SelectRemovalCards removal => SubmitObtainDeckCards(new(removal.CardIds)), _ => PredictorInputResult.Invalid };
        if (Phase == PredictorPhase.ObtainChooseManyCards)
            return input is ChooseObtainManyCards many ? SubmitObtainManyCards(many) : PredictorInputResult.Invalid;
        if (Phase == PredictorPhase.RewardRemoveCards)
            return SubmitRewardCardRemoval(input);
        if (Phase is PredictorPhase.TreasureClosed or PredictorPhase.TreasureRelic) return SubmitTreasure(input, cancellationToken);
        if (Phase is PredictorPhase.RestSite or PredictorPhase.SmithCards or PredictorPhase.RestCookCards or PredictorPhase.RestHealFact) return SubmitRest(input);
        var eventResult = TrySubmitEvent(input, cancellationToken);
        if (eventResult is { } result) return result;
        if (Phase == PredictorPhase.Shop) return TrySubmitShop(input, cancellationToken);
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
        if (input is ExitCardSelection or SkipCardReward)
        {
            if (_cardSelection is not long id) return PredictorInputResult.Invalid;
            int i = OpenReward(id);
            if (!Rewards[i].CanSkip) return PredictorInputResult.NotAllowed;
            Rewards = Rewards.SetItem(i, Rewards[i] with { CardSelectionEntered = false, RelicSubscriptionActive = false });
            _cardSelection = null;
            // Vanilla's default Skip is EndSelectionAndDoNotCompleteReward.
            // It differs from final OnSkipped when leaving the reward set.
            Accept(new(input is SkipCardReward ? "SkipCardReward" : "ExitCardSelection", id, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is LeaveRewards)
        {
            var obtainResult = TryLeaveObtainChild(cancellationToken);
            if (obtainResult is { } childResult) return childResult;
            if (_cardSelection != null || Rewards.Any(r => r.Status == PredictorRewardStatus.Open && r.Mandatory))
                return PredictorInputResult.Invalid;
            Rewards = Rewards.Select(r => r.Status == PredictorRewardStatus.Open ? r with
            { Status = PredictorRewardStatus.Skipped, RelicSubscriptionActive = false } : r).ToImmutableArray();
            Accept(new("LeaveRewards", 0, [], null, null));
            if (IsOpeningNode) Working = Working with { OpeningCompleted = true };
            if (TryFinishEventCombatRewards()) return PredictorInputResult.Accepted;
            if (TryFinishEventRewards()) return PredictorInputResult.Accepted;
            CompleteNode();
            return PredictorInputResult.Accepted;
        }
        if (input is SacrificeCardReward sacrifice) return SubmitSacrificeCardReward(sacrifice, cancellationToken);
        if (input is RerollCardReward reroll) return SubmitRerollCardReward(reroll, cancellationToken);
        if (input is not TakeReward take) return PredictorInputResult.Invalid;
        int index = OpenReward(take.RewardId);
        if (index < 0 || (_cardSelection != null && _cardSelection != take.RewardId)) return PredictorInputResult.Invalid;
        var selected = Rewards[index];
        int goldBefore = Working.GoldBudget;
        if (selected.Kind != PredictorRewardKind.Card && take.CardId != null) return PredictorInputResult.Invalid;
        switch (selected.Kind)
        {
            case PredictorRewardKind.Gold:
                if (selected.GoldAmountUnspecified || selected.GoldNetRestored) return PredictorInputResult.Invalid;
                Working = PredictorSettlementEffects.TakeGold(Working, selected.GoldAmount);
                MarkTaken(selected.Id);
                break;
            case PredictorRewardKind.Card:
                var card = selected.Cards.FirstOrDefault(c => c.Id == take.CardId);
                if (card == null || !selected.CardSelectionEntered) return PredictorInputResult.Invalid;
                if (!Working.Cards.Any(c => c.Id == card.Id)) Working = Working with { Cards = Working.Cards.Add(card) };
                Working = PredictorObtainSources.AddExistingCard(Context, Working, card);
                MarkTaken(selected.Id);
                _cardSelection = null;
                break;
            case PredictorRewardKind.SpecialCard:
                if (selected.Cards.Length != 1) throw new InvalidOperationException("PredictorSpecialCardPayloadInvalid");
                if (!Working.Cards.Any(c => c.Id == selected.Cards[0].Id)) Working = Working with { Cards = Working.Cards.Add(selected.Cards[0]) };
                Working = PredictorObtainSources.AddExistingCard(Context, Working, selected.Cards[0]);
                MarkTaken(selected.Id);
                break;
            case PredictorRewardKind.RemoveCard:
                if (!Working.Deck.Any(id => Working.Card(id).Removable)) return PredictorInputResult.NotAllowed;
                _rewardRemovalId = selected.Id;
                Phase = PredictorPhase.RewardRemoveCards;
                Accept(new("BeginRewardCardRemoval", selected.Id, [], null, null));
                return PredictorInputResult.Accepted;
            case PredictorRewardKind.Potion:
                if (PredictorSettlementEffects.Has(Working, "SOZU"))
                {
                    // Vanilla NotAllowed settles the reward without procuring a bottle.
                    MarkTaken(selected.Id);
                    Accept(new("PotionNotAllowed", selected.Id, [], null, null));
                    return PredictorInputResult.NotAllowed;
                }
                if (Working.PotionSlots.All(p => p != null))
                {
                    _parentReward = selected.Id;
                    Phase = PredictorPhase.ReplacePotion;
                    Accept(new("PotionTooFull", selected.Id, [], null, null));
                    return PredictorInputResult.TooFull;
                }
                Procure(selected.Id);
                break;
            case PredictorRewardKind.Relic:
                if (!BeginObtainRelic(selected.Key, selected.ContentInstanceId, PredictorObtainReturn.Reward, selected.Id, cancellationToken))
                {
                    Accept(new("TakeReward", selected.Id, [], null, null));
                    return PredictorInputResult.Accepted;
                }
                MarkTaken(selected.Id);
                break;
        }
        Working.Validate();
        Accept(new("TakeReward", selected.Id, take.CardId is long cardId ? [cardId] : [],
            selected.Kind == PredictorRewardKind.Gold ? goldBefore : null,
            selected.Kind == PredictorRewardKind.Gold ? Working.GoldBudget : null));
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitRewardCardRemoval(PredictorInput input)
    {
        if (_rewardRemovalId is not long rewardId || OpenReward(rewardId) < 0)
            throw new InvalidOperationException("PredictorRewardRemovalFrameInvalid");
        if (input is CancelRewardCardRemoval)
        {
            _rewardRemovalId = null;
            Phase = PredictorPhase.Rewards;
            Accept(new("CancelRewardCardRemoval", rewardId, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is not SelectRemovalCards selected || selected.CardIds.IsDefault || selected.CardIds.Length != 1 ||
            !Working.Deck.Contains(selected.CardIds[0]) || !Working.Card(selected.CardIds[0]).Removable)
            return PredictorInputResult.Invalid;
        long cardId = selected.CardIds[0];
        RemoveCards([cardId]);
        MarkTaken(rewardId);
        _rewardRemovalId = null;
        Phase = PredictorPhase.Rewards;
        Working.Validate();
        Accept(new("TakeReward", rewardId, [cardId], null, null));
        return PredictorInputResult.Accepted;
    }

    internal void Rewind(int completedNodeIndex)
    {
        RequireRouteSimulation();
        if (completedNodeIndex < 0 || completedNodeIndex >= _checkpoints.Length - 1)
            throw new ArgumentOutOfRangeException(nameof(completedNodeIndex));
        _checkpoints = _checkpoints.Take(completedNodeIndex + 1).ToImmutableArray();
        ResetToBoundary();
        CompleteRouteRedrawUndo();
    }

    // Explicit restart; hiding the UI does not call this or mutate the session.
    internal void RestartCurrentNode()
    {
        RequireRouteSimulation();
        bool active = Phase != PredictorPhase.Complete;
        ResetToBoundary();
        if (active) CompleteRouteRedrawUndo();
    }

    private void RequireRouteSimulation()
    {
        if (_localCrystalSimulation) throw new InvalidOperationException("PredictorLocalCrystalOnly");
        if (Context.Operation != null) throw new InvalidOperationException("PredictorLocalOperationOnly");
    }
    private void ResetToBoundary()
    {
        ResetCombatWorkflow();
        Working = _checkpoints[^1].State;
        Rewards = [];
        _inputs = [];
        _parentReward = _cardSelection = _rewardRemovalId = null;
        GenerationStages = [];
        RestOptions = [];
        ResetEvent();
        ResetEventCombat();
        ResetObtain();
        ResetShop();
        ResetRest();
        ResetAncient();
        ResetRouteTransaction();
        ResetRewardAlternatives();
        ResetPreviewToBoundary();
        Phase = PredictorPhase.Complete;
        Revision = checked(Revision + 1);
        _requestId++;
    }

    // Shared detached-state effects; the request protocol is owned by Submit.
    // Route departure previews reuse these effects without inventing a request.
    private PredictorInputResult ApplyPotionInventoryInput(PredictorInput input, CancellationToken cancellationToken)
    {
        if (input is DiscardPotion discard)
        {
            if (EventPotionsLocked) return PredictorInputResult.NotAllowed;
            if (!MatchesPotion(discard.Slot, discard.PotionId)) return PredictorInputResult.Invalid;
            Working = Working with { PotionSlots = Working.PotionSlots.SetItem(discard.Slot, null) };
            Accept(new("DiscardPotion", discard.PotionId, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is UseOutOfCombatPotion use)
        {
            if (EventPotionsLocked) return PredictorInputResult.NotAllowed;
            if (!MatchesPotion(use.Slot, use.PotionId)) return PredictorInputResult.Invalid;
            var potion = Working.PotionSlots[use.Slot]!;
            if (potion.Key.Entry == "FOUL_POTION")
            {
                if (CanUseFoulPotionInEvent) return ApplyFoulPotionInEvent(use.Slot, use.PotionId, cancellationToken);
                if (!CanUseFoulPotionInShop) return PredictorInputResult.NotAllowed;
                Working = Working with { PotionSlots = Working.PotionSlots.SetItem(use.Slot, null) };
                ApplyFoulPotionInShop(use.PotionId);
                return PredictorInputResult.Accepted;
            }
            if (potion.Key.Entry is "AMBERGRIS" or "BLOOD_POTION" or "FRUIT_JUICE")
            {
                Working = Working with { PotionSlots = Working.PotionSlots.SetItem(use.Slot, null) };
                Accept(new("UsePotion:" + potion.Key.Entry, potion.Id, [], null, null)
                { PotionHealthEffect = new(potion.Id, potion.Key.Entry == "AMBERGRIS" ? .5m : potion.Key.Entry == "BLOOD_POTION" ? .2m : 0,
                    potion.Key.Entry == "FRUIT_JUICE" ? 5 : 0) });
                return PredictorInputResult.Accepted;
            }
            if (potion.Key.Entry != "ENTROPIC_BREW") return PredictorInputResult.NotAllowed;
            // Separate from Discard: remove-before-use, then actual source generation.
            // Procure/Use hooks with future effects will be added with their relic handlers.
            var used = PredictorSettlementEffects.UseEntropicBrew(Working, Context.PotionPool, use.Slot);
            Working = used.State;
            Accept(new("UseEntropicBrew", use.PotionId, used.Generated.Select(p => p.Id).ToImmutableArray(), null, null)
            { GeneratedPotions = used.Generated });
            return PredictorInputResult.Accepted;
        }
        return PredictorInputResult.Invalid;
    }

    private bool MatchesPotion(int slot, long id) => slot >= 0 && slot < Working.PotionSlots.Length && Working.PotionSlots[slot]?.Id == id;
    private int OpenReward(long id) => Rewards.FindIndex(r => r.Id == id && r.Status == PredictorRewardStatus.Open);
    private void MarkTaken(long id)
    {
        int i = OpenReward(id);
        Rewards = Rewards.SetItem(i, Rewards[i] with { Status = PredictorRewardStatus.Taken, CardSelectionEntered = false, RelicSubscriptionActive = false });
    }
    private void Procure(long rewardId)
    {
        var reward = Rewards[OpenReward(rewardId)];
        int slot = Working.PotionSlots.FindIndex(p => p == null);
        Working = Working with { PotionSlots = Working.PotionSlots.SetItem(slot, new(reward.ContentInstanceId!.Value, reward.Key)) };
        MarkTaken(rewardId);
    }
    private void Accept(PredictorAcceptedInput input)
    {
        ObserveOperationInput(input);
        _inputs = _inputs.Add(input);
        _requestId++;
    }

    private void RemoveCards(ImmutableArray<long> ids)
    {
        foreach (long id in ids)
        {
            ApplySpoilsMapBeforeRemoved(id);
            Working = Working.WithCard(Working.Card(id) with { Removed = true });
        }
        Working = Working with { Deck = Working.Deck.Where(id => !ids.Contains(id)).ToImmutableArray() };
    }

    internal void RemoveRelic(long instanceId)
    {
        RequireRouteSimulation();
        RemoveRelicInstance(instanceId);
        CompleteRouteRedrawUndo();
    }

    // Native event settlement removes from this detached state too. Route
    // editing alone owns the route guard and redraw/undo bookkeeping.
    private void RemoveRelicInstance(long instanceId)
    {
        int index = Working.Relics.FindIndex(r => r.Id == instanceId);
        if (index < 0) throw new ArgumentException("PredictorRelicInstanceMissing");
        var relic = Working.Relics[index];
        // Vanilla 0.111 content has no AfterRemoved overrides. RelicRemoved is
        // a player/UI notification; removing never repopulates either bag.
        Working = Working with { Relics = Working.Relics.RemoveAt(index) };
        Accept(new("RemoveRelic", instanceId, [], null, null) { RemovedRelic = relic });
    }

    internal bool IsRelicTradable(PredictorRelic relic) => !relic.Melted &&
        Context.Catalog!.TradableRelicTypes.Contains(relic.Key) && !(relic.Key.Entry switch
        {
            "LIZARD_TAIL" => relic.LizardTailUsed,
            "WINGED_BOOTS" => relic.TimesUsed >= 3,
            "WONGOS_MYSTERY_TICKET" => relic.GaveRelic,
            _ => false
        });

    private void CompleteNode()
    {
        if (Context.Operation != null && CurrentAncient?.Entry == "NEOW")
            Working = Working with { OpeningCompleted = true };
        ResetCombatWorkflow();
        Working.Validate();
        var healthEffects = Working.CurrentHealthEffects;
        Working = Working with { CurrentHealthEffects = [] };
        _checkpoints = _checkpoints.Add(new(Working, new(Working.Position.NodeId, _inputs, Rewards.AddRange(CompletedChildRewards))
        { HealthEffects = healthEffects }));
        ClearCompletedChildRewards();
        Phase = PredictorPhase.Complete;
        _requestId++;
    }

    private bool IsOpeningNode => Working.Position.NodeId == "opening" || Working.Visits.LastOrDefault()?.Room == PredictorRoomKind.Opening;

    private bool _drainingObtain;
    private void DrainObtainCompletions(CancellationToken token)
    {
        if (_drainingObtain) return;
        _drainingObtain = true;
        try
        {
            if (Phase == PredictorPhase.Rewards && HasObtainChild) ContinueObtainChildIfComplete(token);
            while (ConsumeCompletedObtain() is { } completed)
            {
                switch (completed.Destination)
                {
                    case PredictorObtainReturn.Reward:
                        if (OpenReward(completed.SourceId) >= 0) MarkTaken(completed.SourceId);
                        break;
                    case PredictorObtainReturn.Opening:
                        Working = Working with { OpeningCompleted = true };
                        CompleteNode();
                        break;
                    case PredictorObtainReturn.Shop: ResumeShopAfterRelicObtain(completed.SourceId); break;
                    case PredictorObtainReturn.Event: ResumeEventAfterRelicObtain(completed.SourceId); break;
                    default: ResumeRoomAfterObtain(completed.Destination, completed.SourceId); break;
                }
                if (Phase == PredictorPhase.Rewards && HasObtainChild) ContinueObtainChildIfComplete(token);
            }
        }
        finally { _drainingObtain = false; }
    }

    internal void CompleteRunDeath(string source)
    {
        Working = Working with { Outcome = PredictorRunOutcome.Death };
        Rewards = Rewards.Select(r => r.Status == PredictorRewardStatus.Open ? r with
            { Status = PredictorRewardStatus.Aborted, RelicSubscriptionActive = false } : r).ToImmutableArray();
        Accept(new("RunDeath:" + source, 0, [], null, null));
        AbortObtainFrames();
        CompleteNode();
        ResetObtain();
        ResetEvent();
    }
}
