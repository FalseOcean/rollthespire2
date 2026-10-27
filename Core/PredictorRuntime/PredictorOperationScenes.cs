using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorOperationRewardsScene(ImmutableArray<PredictorReward> Rewards,
    long? SelectedRewardId = null) : PredictorOperationScene
{
    internal PredictorOperationEventRng? EventRng { get; init; }
    internal PredictorOperationRewardRemoval? PendingRemoval { get; init; }
    internal ImmutableArray<PredictorOperationLinkedRewardGroup> LinkedGroups { get; init; } = [];
    internal override void Restore(PredictorRun run) => run.RestoreOperationRewards(this);
}

internal sealed record PredictorOperationEventRng(ulong S0, ulong S1, ulong S2, ulong S3, int Calls);
internal sealed record PredictorOperationRewardRemoval(long RewardId, ImmutableArray<long> CandidateIds,
    int MinimumCount, int MaximumCount, bool Cancelable);

// The relic and payment already belong to State. Only this suspended child and
// the merchant's post-obtain work remain; beginning the obtain again is invalid.
internal sealed record PredictorOperationDeckObtain(long RelicId, ModelKey Source,
    PredictorObtainDeckOperation Operation, int Count, ImmutableArray<long> CandidateIds)
{
    internal int MinimumCount { get; init; } = Count;
    internal int MaximumCount { get; init; } = Count;
    internal bool Cancelable { get; init; }
}
internal sealed record PredictorOperationPaidPurchase(long EntryId, int BeforeGold, bool IgnoreCost);
internal sealed record PredictorOperationShopRemoval(long EntryId, int QuotedCost,
    ImmutableArray<long> CandidateIds, bool Cancelable);
internal sealed record PredictorOperationPaidRelic(long EntryId, int BeforeGold, bool IgnoreCost,
    PredictorOperationDeckObtain Choice);
internal sealed record PredictorOperationShopScene(ImmutableArray<PredictorShopEntry> Entries,
    bool InventoryOpen, int Revision = 0, PredictorOperationPaidRelic? PendingRelic = null) : PredictorOperationScene
{
    internal PredictorOperationPaidPurchase? PendingPurchase { get; init; }
    internal PredictorOperationShopRemoval? PendingRemoval { get; init; }
    internal bool IsMerchantRoom { get; init; } = true;
    internal override void Restore(PredictorRun run) => run.RestoreOperationShop(this);
}

internal sealed record PredictorOperationRestScene(ImmutableArray<PredictorRestAction> Options,
    PredictorPhase Phase = PredictorPhase.RestSite, ImmutableArray<long> HatchSuppliers = default) : PredictorOperationScene
{
    internal ImmutableArray<long> CandidateIds { get; init; }
    internal int? MinimumCount { get; init; }
    internal int? MaximumCount { get; init; }
    internal bool Cancelable { get; init; } = true;
    internal ImmutableArray<PredictorOperationMendTarget> MendTargets { get; init; } = [];
    internal bool MendTargetPending { get; init; }
    internal ImmutableArray<PredictorOperationOpaqueAction> OpaqueOptions { get; init; } = [];
    internal override void Restore(PredictorRun run) => run.RestoreOperationRest(this);
}

internal sealed record PredictorOperationTreasureScene(PredictorRelic? Relic, bool Allowed,
    bool Opened) : PredictorOperationScene
{
    internal override void Restore(PredictorRun run) => run.RestoreOperationTreasure(this);
}

internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorOperationOpaqueAction> _operationOpaqueRestOptions = [];
    internal ImmutableArray<PredictorOperationOpaqueAction> OperationOpaqueRestOptions => _operationOpaqueRestOptions;
    private bool _operationShopIsMerchantRoom = true;
    private bool OperationShopPriceHooksApply => Context.Operation == null || _operationShopIsMerchantRoom;
    private void RequireEmptyOperationScene()
    {
        if (Phase != PredictorPhase.Complete || !Rewards.IsEmpty || !_inputs.IsEmpty)
            throw new InvalidOperationException("PredictorOperationSceneAlreadyStarted");
    }

    internal void RestoreOperationRewards(PredictorOperationRewardsScene scene, bool requireEmpty = true)
    {
        if (requireEmpty) RequireEmptyOperationScene();
        if (scene.Rewards.IsDefault || scene.Rewards.Any(r => r == null ||
            r.Id <= 0 || r.Id >= Working.NextInstanceId || !Enum.IsDefined(r.Kind) ||
            !Enum.IsDefined(r.Origin) || !Enum.IsDefined(r.Status) || r.Cards.IsDefault ||
            !r.HasValidGoldDisposition || r.GoldAmount < 0 ||
            r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open && r.Cards.IsEmpty ||
            r.Cards.Any(c => c == null || c.Id <= 0 || c.Id >= Working.NextInstanceId ||
                c.Key.Category != "CARD" || !c.Key.IsValid || c.UpgradeLevel < 0 || c.MaxUpgradeLevel < c.UpgradeLevel) ||
            r.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion &&
                (!r.Key.IsValid || r.Key.Category != (r.Kind == PredictorRewardKind.Relic ? "RELIC" : "POTION") ||
                 r.ContentInstanceId is not > 0 || r.ContentInstanceId >= Working.NextInstanceId)) ||
            scene.Rewards.Select(r => r.Id).Distinct().Count() != scene.Rewards.Length ||
            scene.SelectedRewardId is { } selected && !scene.Rewards.Any(r => r.Id == selected &&
                r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open && r.CardSelectionEntered) ||
            scene.Rewards.Any(r => r.CardSelectionEntered && r.Id != scene.SelectedRewardId) ||
            scene.EventRng is { } rng && (rng.Calls < 0 || (rng.S0 | rng.S1 | rng.S2 | rng.S3) == 0) ||
            scene.Rewards.Any(r => r.CanReroll && r.RerollRecipe?.UseEventRng == true) && scene.EventRng == null)
            throw new InvalidDataException("PredictorOperationRewardsInvalid");
        Rewards = scene.Rewards;
        _cardSelection = scene.SelectedRewardId;
        if (scene.EventRng is { } eventRng)
            _eventRng = Xoshiro256StarStar.FromState(eventRng.S0, eventRng.S1, eventRng.S2, eventRng.S3, eventRng.Calls);
        Phase = PredictorPhase.Rewards;
        if (scene.PendingRemoval is { } removal)
        {
            if (scene.SelectedRewardId != null || !Rewards.Any(r => r.Id == removal.RewardId && r.Kind == PredictorRewardKind.RemoveCard && r.Status == PredictorRewardStatus.Open) ||
                removal.CandidateIds.IsDefaultOrEmpty || removal.CandidateIds.Distinct().Count() != removal.CandidateIds.Length ||
                removal.CandidateIds.Any(id => !Working.Deck.Contains(id)) || removal.MinimumCount != 1 || removal.MaximumCount != 1)
                throw new InvalidDataException("PredictorOperationRewardRemovalInvalid");
            _rewardRemovalId = removal.RewardId;
            _operationRewardRemoval = removal;
            Phase = PredictorPhase.RewardRemoveCards;
        }
        RestoreOperationLinkedGroups(scene.LinkedGroups);
        _requestId++;
    }

    internal void RestoreOperationShop(PredictorOperationShopScene scene, bool allowUnboundPurchase = false)
    {
        RequireEmptyOperationScene();
        if (scene.Entries.IsDefault || scene.Revision < 0 || scene.Entries.Any(e => e == null ||
            e.Id <= 0 || e.Id >= Working.NextInstanceId || e.BaseCost < 0 || e.Revision < 0 ||
            e.Revision > scene.Revision || !Enum.IsDefined(e.Kind) ||
            e.Stocked && e.Kind != PredictorShopEntryKind.CardRemoval && (e.Key == null || !e.Key.Value.IsValid) ||
            e.Stocked && (e.Kind is PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard) &&
                (e.Card == null || e.Card.Key != e.Key)) ||
            scene.Entries.Select(e => e.Id).Distinct().Count() != scene.Entries.Length)
            throw new InvalidDataException("PredictorOperationShopInvalid");
        if (scene.PendingPurchase is { } purchase &&
            (!allowUnboundPurchase || scene.PendingRelic != null || purchase.BeforeGold < 0 ||
             !scene.Entries.Any(e => e.Id == purchase.EntryId && e.Kind == PredictorShopEntryKind.Relic && e.Stocked)))
            throw new InvalidDataException("PredictorOperationShopChildContinuationMissing");
        if (scene.PendingRemoval is { } removal &&
            (scene.PendingPurchase != null || scene.PendingRelic != null || removal.QuotedCost < 0 || removal.CandidateIds.IsDefaultOrEmpty ||
             removal.CandidateIds.Distinct().Count() != removal.CandidateIds.Length || removal.CandidateIds.Any(id => !Working.Deck.Contains(id)) ||
             !scene.Entries.Any(e => e.Id == removal.EntryId && e.Kind == PredictorShopEntryKind.CardRemoval && e.Stocked)))
            throw new InvalidDataException("PredictorOperationShopRemovalInvalid");
        if (scene.PendingRelic is { } pending)
        {
            var entry = scene.Entries.SingleOrDefault(e => e.Id == pending.EntryId);
            var choice = pending.Choice;
            if (entry is not { Kind: PredictorShopEntryKind.Relic, Stocked: true } ||
                choice == null || entry.Key != choice.Source || pending.BeforeGold < 0 ||
                choice.Count < 0 || choice.CandidateIds.IsDefault || choice.Count > choice.CandidateIds.Length ||
                choice.CandidateIds.Distinct().Count() != choice.CandidateIds.Length ||
                choice.CandidateIds.Any(id => !Working.Deck.Contains(id)) ||
                !Enum.IsDefined(choice.Operation) || !Working.Relics.Any(r => r.Id == choice.RelicId && r.Key == choice.Source))
                throw new InvalidDataException("PredictorOperationPaidRelicInvalid");
        }
        _shopEntries = scene.Entries;
        _operationShopIsMerchantRoom = scene.IsMerchantRoom;
        _shopRevision = scene.Revision;
        _shopInventoryOpen = scene.InventoryOpen;
        Phase = PredictorPhase.Shop;
        if (scene.PendingRemoval is { } selectedRemoval)
        {
            _shopRemovalEntry = selectedRemoval.EntryId;
            _shopRemovalQuotedCost = selectedRemoval.QuotedCost;
            _operationShopRemovalCandidates = selectedRemoval.CandidateIds;
            _operationShopRemovalCancelable = selectedRemoval.Cancelable;
        }
        if (scene.PendingPurchase is { } receipt)
        {
            _shopPendingRelicEntry = receipt.EntryId;
            _shopPendingGoldBefore = receipt.BeforeGold;
            _shopPendingRelicIgnoreCost = receipt.IgnoreCost;
        }
        if (scene.PendingRelic is { } paid)
        {
            _shopPendingRelicEntry = paid.EntryId;
            _shopPendingGoldBefore = paid.BeforeGold;
            _shopPendingRelicIgnoreCost = paid.IgnoreCost;
            _obtainDeckPending = new(PredictorObtainReturn.Shop, paid.EntryId);
            _obtainDeckRelicId = paid.Choice.RelicId;
            _obtainDeckSource = paid.Choice.Source;
            _obtainDeckOperation = paid.Choice.Operation;
            _obtainDeckCount = paid.Choice.Count;
            _obtainDeckCandidateOrder = paid.Choice.CandidateIds;
            Phase = PredictorPhase.ObtainDeckCards;
            SetOperationDeckBounds(paid.Choice, _obtainDeckPending);
        }
        _requestId++;
    }

    internal void RestoreOperationRest(PredictorOperationRestScene scene)
    {
        RequireEmptyOperationScene();
        // Native Hatch options carry only their owner. After one hatch, all
        // eggs transform while another pre-generated option can remain. Zero
        // preserves the absence of a card source instead of inventing one.
        var hatch = scene.HatchSuppliers.IsDefault
            ? Enumerable.Repeat(0L, scene.Options.IsDefault ? 0 : scene.Options.Count(a => a == PredictorRestAction.Hatch)).ToImmutableArray()
            : scene.HatchSuppliers;
        if (scene.Options.IsDefault || scene.Options.Any(a => !Enum.IsDefined(a)) ||
            scene.Phase is not (PredictorPhase.RestSite or PredictorPhase.SmithCards or PredictorPhase.RestCookCards) ||
            scene.Phase == PredictorPhase.SmithCards && !scene.Options.Contains(PredictorRestAction.Smith) ||
            scene.Phase == PredictorPhase.RestCookCards && !scene.Options.Contains(PredictorRestAction.Cook) ||
            hatch.Length != scene.Options.Count(a => a == PredictorRestAction.Hatch) ||
            hatch.Any(id => id < 0 || id > 0 && !Working.Cards.Any(c => c.Id == id)))
            throw new InvalidDataException("PredictorOperationRestInvalid");
        if (scene.MendTargetPending && (scene.Phase != PredictorPhase.RestSite || !scene.Options.Contains(PredictorRestAction.Mend)))
            throw new InvalidDataException("PredictorOperationMendParentInvalid");
        RestoreOperationMendTargets(scene.MendTargets, scene.MendTargetPending);
        _operationOpaqueRestOptions = scene.OpaqueOptions;
        RestOptions = scene.Options;
        _restHatchSuppliers = hatch;
        _restHatchSelectionCount = 0;
        Phase = scene.Phase;
        if (!scene.CandidateIds.IsDefault)
        {
            int count = scene.Phase == PredictorPhase.RestCookCards ? 2 : 1;
            if (scene.Phase is not (PredictorPhase.SmithCards or PredictorPhase.RestCookCards) ||
                scene.CandidateIds.Distinct().Count() != scene.CandidateIds.Length || scene.CandidateIds.Any(id => !Working.Deck.Contains(id)) ||
                (scene.MinimumCount ?? count) < 0 || (scene.MaximumCount ?? count) < (scene.MinimumCount ?? count) ||
                (scene.MaximumCount ?? count) > scene.CandidateIds.Length)
                throw new InvalidDataException("PredictorOperationRestCardSelectionInvalid");
            _operationRestCandidates = scene.CandidateIds;
            _operationRestChoicePhase = scene.Phase;
            _operationRestMinimum = scene.MinimumCount ?? count;
            _operationRestMaximum = scene.MaximumCount ?? count;
            _operationRestCancelable = scene.Cancelable;
        }
        _requestId++;
    }

    internal void RestoreOperationTreasure(PredictorOperationTreasureScene scene)
    {
        RequireEmptyOperationScene();
        if (!scene.Allowed && scene.Relic != null || scene.Relic is { } relic &&
            (relic.Id <= 0 || relic.Id >= Working.NextInstanceId || relic.Key.Category != "RELIC" ||
             !relic.Key.IsValid || Working.Relics.Any(r => r.Id == relic.Id)))
            throw new InvalidDataException("PredictorOperationTreasureInvalid");
        TreasureRelic = scene.Relic;
        _treasureAllowed = scene.Allowed;
        _treasureObtaining = false;
        Phase = scene.Opened ? PredictorPhase.TreasureRelic : PredictorPhase.TreasureClosed;
        _requestId++;
    }
}
