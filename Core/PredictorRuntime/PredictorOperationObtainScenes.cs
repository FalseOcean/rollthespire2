using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorOperationObtainChildKind { Deck, NewLeaf, ChooseCard, ChooseManyCards, Bundle, Rewards }

// Parent describes the real suspended caller, including its original offers.
// Nesting these records retains known reward-obtain parents without replaying
// acquisition. A source alone cannot reconstruct a Large Capsule loop cursor.
internal sealed record PredictorOperationObtainScene(PredictorOperationScene Parent,
    PredictorObtainReturn Destination, long SourceId, long RelicId, ModelKey Source,
    PredictorOperationObtainChildKind Kind) : PredictorOperationScene
{
    internal PredictorOperationDeckObtain? DeckChoice { get; init; }
    internal ImmutableArray<PredictorCard> Cards { get; init; } = [];
    internal ImmutableArray<ImmutableArray<PredictorCard>> Bundles { get; init; } = [];
    internal PredictorOperationRewardsScene? RewardScene { get; init; }
    internal ImmutableArray<long> NewLeafCandidates { get; init; } = [];
    internal PredictorRestAction? RestAction { get; init; }
    internal bool Mandatory { get; init; }
    internal override void Restore(PredictorRun run) => run.RestoreOperationObtain(this);
}

internal sealed record PredictorOperationRestRewardsScene(PredictorOperationRestScene Parent,
    PredictorOperationRewardsScene Rewards) : PredictorOperationScene
{
    internal override void Restore(PredictorRun run) => run.RestoreOperationRestRewards(this);
}

internal sealed partial class PredictorRun
{
    private ImmutableArray<long> _operationNewLeafCandidates;
    private PredictorObtainCompletion? _operationBoundDeckObtain;
    private int _operationDeckMinimum, _operationDeckMaximum;
    private bool _operationDeckCancelable;
    private ImmutableArray<long> _operationRestCandidates;
    private PredictorPhase _operationRestChoicePhase;
    private int _operationRestMinimum, _operationRestMaximum;
    private bool _operationRestCancelable;
    private ImmutableArray<long> _operationShopRemovalCandidates;
    private bool _operationShopRemovalCancelable;
    private PredictorOperationRewardRemoval? _operationRewardRemoval;

    private void SetOperationDeckBounds(PredictorOperationDeckObtain choice, PredictorObtainCompletion completion)
    {
        if (choice.MinimumCount < 0 || choice.MaximumCount < choice.MinimumCount || choice.MaximumCount != choice.Count ||
            choice.MaximumCount > choice.CandidateIds.Length)
            throw new InvalidDataException("PredictorOperationDeckBoundsInvalid");
        _operationBoundDeckObtain = completion;
        _operationDeckMinimum = choice.MinimumCount;
        _operationDeckMaximum = choice.MaximumCount;
        _operationDeckCancelable = choice.Cancelable;
    }

    internal bool ValidateOperationChoice(PredictorInput input)
    {
        if (_operationTreasureParty is { AllocationActive: true } && input is LeaveTreasure)
            return false; // Native multiplayer skip is a vote, not room departure.
        if (Phase == PredictorPhase.RewardRemoveCards && _operationRewardRemoval is { } rewardRemoval && _rewardRemovalId == rewardRemoval.RewardId)
            return input switch
            {
                SelectRemovalCards cards => !cards.CardIds.IsDefault && cards.CardIds.Length == 1 && cards.CardIds.All(rewardRemoval.CandidateIds.Contains),
                CancelRewardCardRemoval => rewardRemoval.Cancelable,
                _ => true
            };
        if (Phase == PredictorPhase.Shop && _shopRemovalEntry != null && !_operationShopRemovalCandidates.IsDefault)
            return input switch
            {
                ChooseShopRemovalCard card => _operationShopRemovalCandidates.Contains(card.CardId),
                CancelShopCardRemoval => _operationShopRemovalCancelable,
                _ => true
            };
        if (Phase == PredictorPhase.ObtainDeckCards && _operationBoundDeckObtain != null && _operationBoundDeckObtain == _obtainDeckPending &&
            input is ChooseObtainDeckCards deck)
            return !deck.CardIds.IsDefault && deck.CardIds.Length >= _operationDeckMinimum && deck.CardIds.Length <= _operationDeckMaximum;
        if (Phase == _operationRestChoicePhase && !_operationRestCandidates.IsDefault)
            return input switch
            {
                SelectSmithCard card => _operationRestCandidates.Contains(card.CardId),
                SelectCookCards cards => !cards.CardIds.IsDefault && cards.CardIds.Length >= _operationRestMinimum &&
                    cards.CardIds.Length <= _operationRestMaximum && cards.CardIds.All(_operationRestCandidates.Contains),
                CancelSmithSelection or CancelCookSelection => _operationRestCancelable,
                _ => true
            };
        return true;
    }

    internal bool ValidateOperationDeckChoice(PredictorInput input) => ValidateOperationChoice(input);

    internal PredictorSelection OperationSelectionBounds(PredictorSelection selection)
    {
        if (Phase == PredictorPhase.RewardRemoveCards && _operationRewardRemoval is { } removal && _rewardRemovalId == removal.RewardId)
            return selection with { CandidateCardIds = removal.CandidateIds, RequiredCardCount = 1, MinimumCardCount = 1, MaximumCardCount = 1 };
        if (Phase == PredictorPhase.Shop && _shopRemovalEntry != null && !_operationShopRemovalCandidates.IsDefault)
            return selection with { CandidateCardIds = _operationShopRemovalCandidates, RequiredCardCount = 1, MinimumCardCount = 1, MaximumCardCount = 1 };
        if (Phase == PredictorPhase.ObtainDeckCards && _operationBoundDeckObtain != null && _operationBoundDeckObtain == _obtainDeckPending)
            return selection with { RequiredCardCount = _operationDeckMaximum, MinimumCardCount = _operationDeckMinimum, MaximumCardCount = _operationDeckMaximum };
        if (Phase == _operationRestChoicePhase && !_operationRestCandidates.IsDefault)
            return selection with { CandidateCardIds = _operationRestCandidates, RequiredCardCount = _operationRestMaximum,
                MinimumCardCount = _operationRestMinimum, MaximumCardCount = _operationRestMaximum };
        return selection;
    }

    internal bool OperationChoiceCancelable => Phase == PredictorPhase.RewardRemoveCards && _operationRewardRemoval is { } removal && _rewardRemovalId == removal.RewardId
        ? removal.Cancelable : Phase == PredictorPhase.Shop && _shopRemovalEntry != null && !_operationShopRemovalCandidates.IsDefault
        ? _operationShopRemovalCancelable : Phase == PredictorPhase.ObtainDeckCards && _operationBoundDeckObtain != null &&
        _operationBoundDeckObtain == _obtainDeckPending ? _operationDeckCancelable :
        Phase == _operationRestChoicePhase && !_operationRestCandidates.IsDefault && _operationRestCancelable;

    internal void RestoreOperationRestRewards(PredictorOperationRestRewardsScene scene)
    {
        scene.Parent.Restore(this);
        if (Phase != PredictorPhase.RestSite || !RestOptions.Contains(PredictorRestAction.Heal))
            throw new InvalidDataException("PredictorOperationRestRewardsParentInvalid");
        _restHealRewardsPending = true;
        _obtainFrames = _obtainFrames.Add(new(new("RELIC", "REST_HEAL_REWARDS"), PredictorObtainReturn.RestHeal,
            0, Rewards, Phase, 0, false));
        RestoreOperationRewards(scene.Rewards, requireEmpty: false);
    }

    private ImmutableArray<long> OperationTransformCandidates()
    {
        if (Phase == PredictorPhase.TransformCards && !_operationNewLeafCandidates.IsDefault)
            return _operationNewLeafCandidates;
        // A future BeginNewLeaf asks before entering TransformCards. Its
        // candidate set belongs to that new operation, not an imported picker.
        _operationNewLeafCandidates = default;
        return PredictorCardChanges.TransformCandidates(Context, Working);
    }

    internal void RestoreOperationObtain(PredictorOperationObtainScene scene)
    {
        if (scene.Parent == null || scene.Source.Category != "RELIC" || !scene.Source.IsValid ||
            !Working.Relics.Any(r => r.Id == scene.RelicId && r.Key == scene.Source) ||
            !Enum.IsDefined(scene.Kind) || !Enum.IsDefined(scene.Destination) ||
            scene.Cards.IsDefault || scene.Bundles.IsDefault || scene.NewLeafCandidates.IsDefault ||
            scene.Destination == PredictorObtainReturn.Rest && scene.RestAction is not (PredictorRestAction.Dig or PredictorRestAction.Hatch))
            throw new InvalidDataException("PredictorOperationObtainSceneInvalid");
        void RequireCards(ImmutableArray<PredictorCard> cards)
        {
            if (cards.IsDefault || cards.Any(c => c == null || c.Id <= 0 || c.Id >= Working.NextInstanceId ||
                c.Key.Category != "CARD" || !c.Key.IsValid || c.UpgradeLevel < 0 || c.MaxUpgradeLevel < c.UpgradeLevel) ||
                cards.Select(c => c.Id).Distinct().Count() != cards.Length)
                throw new InvalidDataException("PredictorOperationObtainCardsInvalid");
        }
        RequireCards(scene.Cards);
        foreach (var bundle in scene.Bundles) RequireCards(bundle);
        var deck = scene.DeckChoice;
        if (scene.Kind == PredictorOperationObtainChildKind.Deck &&
            (deck == null || deck.RelicId != scene.RelicId || deck.Source != scene.Source ||
             !Enum.IsDefined(deck.Operation) || deck.Count < 0 || deck.CandidateIds.IsDefaultOrEmpty ||
             deck.Count > deck.CandidateIds.Length || deck.CandidateIds.Distinct().Count() != deck.CandidateIds.Length ||
             deck.CandidateIds.Any(id => !Working.Deck.Contains(id))) ||
            scene.Kind == PredictorOperationObtainChildKind.NewLeaf &&
            (scene.Source.Entry != "NEW_LEAF" || scene.NewLeafCandidates.IsEmpty ||
             scene.NewLeafCandidates.Distinct().Count() != scene.NewLeafCandidates.Length ||
             scene.NewLeafCandidates.Any(id => !Working.Deck.Contains(id))) ||
            scene.Kind == PredictorOperationObtainChildKind.ChooseCard && scene.Source.Entry is not ("LEAD_PAPERWEIGHT" or "HEFTY_TABLET" or "MASSIVE_SCROLL") ||
            scene.Kind == PredictorOperationObtainChildKind.ChooseManyCards && scene.Source.Entry != "SEA_GLASS" ||
            scene.Kind == PredictorOperationObtainChildKind.Bundle && (scene.Source.Entry != "SCROLL_BOXES" || scene.Bundles.Length != 2) ||
            scene.Kind == PredictorOperationObtainChildKind.Rewards &&
            (scene.RewardScene == null || scene.Source.Entry == "LARGE_CAPSULE" || scene.Mandatory != (scene.Source.Entry == "NEOWS_BONES")))
            throw new InvalidDataException("PredictorOperationObtainChildInvalid");

        // Native generated offers are owned by their picker, not by the deck.
        // Register their copied instances so the existing AddExistingCard path
        // can receive them, retaining every observed ID and next-ID boundary.
        var offered = scene.Cards.Concat(scene.Bundles.SelectMany(b => b)).ToArray();
        var registered = Working.Cards;
        foreach (var candidate in offered)
        {
            var card = candidate with { Removed = false };
            var existing = registered.FirstOrDefault(c => c.Id == card.Id);
            if (Working.Relics.Any(r => r.Id == card.Id) || Working.PotionSlots.Any(p => p?.Id == card.Id) ||
                existing != null && (Working.Deck.Contains(card.Id) || existing with { Removed = false } != card))
                throw new InvalidDataException("PredictorOperationObtainCardIdentityCollision");
            registered = existing == null ? registered.Add(card) : registered.SetItem(registered.IndexOf(existing), card);
        }
        Working = Working with { Cards = registered };

        if (scene.Parent is PredictorOperationShopScene shop)
            RestoreOperationShop(shop, allowUnboundPurchase: true);
        else scene.Parent.Restore(this);
        if (!HasObservedOperationParent(scene.Destination))
            throw new InvalidDataException("OperationNestedObtainParentMissing");
        if (scene.Destination == PredictorObtainReturn.Shop && _shopPendingRelicEntry != scene.SourceId)
            throw new InvalidDataException("PredictorOperationShopChildContinuationMissing");
        if (scene.Destination == PredictorObtainReturn.Shop && !_shopEntries.Any(e => e.Id == scene.SourceId && e.Key == scene.Source))
            throw new InvalidDataException("PredictorOperationShopChildSourceMismatch");
        if ((scene.Destination is PredictorObtainReturn.Reward or PredictorObtainReturn.RewardAlternative) && OpenReward(scene.SourceId) < 0)
            throw new InvalidDataException("PredictorOperationObtainSourceRewardMissing");
        if (scene.Destination == PredictorObtainReturn.RewardAlternative)
            _sacrificePendingRewards = _sacrificePendingRewards.Add(scene.SourceId);
        if (scene.Destination == PredictorObtainReturn.Rest) _restPendingObtain = scene.RestAction;
        if (scene.Destination == PredictorObtainReturn.Treasure) _treasureObtaining = true;
        _cardSelection = null;
        var completion = new PredictorObtainCompletion(scene.Destination, scene.SourceId);
        switch (scene.Kind)
        {
            case PredictorOperationObtainChildKind.Deck:
                _obtainDeckPending = completion;
                _obtainDeckRelicId = scene.RelicId;
                _obtainDeckSource = scene.Source;
                _obtainDeckOperation = deck!.Operation;
                _obtainDeckCount = deck.Count;
                _obtainDeckCandidateOrder = deck.CandidateIds;
                SetOperationDeckBounds(deck, completion);
                Phase = PredictorPhase.ObtainDeckCards;
                break;
            case PredictorOperationObtainChildKind.NewLeaf:
                _newLeafObtain = completion;
                _parentReward = scene.Destination == PredictorObtainReturn.Reward ? scene.SourceId : null;
                _operationNewLeafCandidates = scene.NewLeafCandidates;
                Phase = PredictorPhase.TransformCards;
                break;
            case PredictorOperationObtainChildKind.ChooseCard:
                _obtainCardPending = completion;
                _obtainCardSource = scene.Source;
                _obtainCardChoices = scene.Cards;
                Phase = PredictorPhase.ObtainChooseCard;
                break;
            case PredictorOperationObtainChildKind.ChooseManyCards:
                _obtainManyPending = completion;
                _obtainManyChoices = scene.Cards;
                Phase = PredictorPhase.ObtainChooseManyCards;
                break;
            case PredictorOperationObtainChildKind.Bundle:
                _newLeafObtain = completion;
                _scrollBoxBundles = scene.Bundles;
                Phase = PredictorPhase.ChooseBundle;
                break;
            case PredictorOperationObtainChildKind.Rewards:
                _obtainFrames = _obtainFrames.Add(new(scene.Source, scene.Destination, scene.SourceId,
                    Rewards, Phase, 0, scene.Mandatory));
                RestoreOperationRewards(scene.RewardScene!, requireEmpty: false);
                break;
        }
        _requestId++;
    }
}
