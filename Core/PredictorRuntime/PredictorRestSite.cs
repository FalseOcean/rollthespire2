using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRestAction { Heal, Smith, Lift, Dig, Cook, Clone, Hatch, Kindle, Mend }
internal sealed record SelectRestAction(PredictorRestAction Action) : PredictorInput;
internal sealed record SelectSmithCard(long CardId) : PredictorInput;
internal sealed record CancelSmithSelection : PredictorInput;
internal sealed record SelectCookCards(ImmutableArray<long> CardIds) : PredictorInput;
internal sealed record CancelCookSelection : PredictorInput;
internal sealed record ProvideRestMaxHpFact(int MaxHp) : PredictorInput;
internal sealed record LeaveRestSite : PredictorInput;
internal sealed record PredictorRestHealOutcome(decimal BaseMaxHpFraction, int FlatHealBonus,
    int MaxHpDelta, bool IsMimicked, int? OverrideHealAmount = null, int MaxHpLoss = 0);
internal sealed record PredictorRestOffer(PredictorRestAction Action, bool Enabled);

internal sealed partial class PredictorRun
{
    internal ImmutableArray<PredictorRestAction> RestOptions { get; private set; } = [];
    internal ImmutableArray<PredictorRestOffer> RestOffers
    {
        get
        {
            var offers = RestOptions.Select(action => new PredictorRestOffer(action,
                action switch
                {
                    PredictorRestAction.Smith => Working.Deck.Any(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel),
                    PredictorRestAction.Cook => Working.Deck.Count(id => Working.Card(id).Removable) >= 2,
                    _ => true
                })).ToImmutableArray();
            if (_preview.Partial != null)
                foreach (var offer in offers) _preview.Partial.RecordRestQualification(this, offer.Action, offer.Enabled);
            return offers;
        }
    }
    private PredictorRestAction? _restPendingObtain;
    private bool _restHealRewardsPending;
    private int? _restMaxHpFact;
    private ImmutableArray<long> _restHatchSuppliers = [];
    private int _restHatchSelectionCount;
    internal ImmutableArray<long> RestCookCandidates => Phase == PredictorPhase.RestCookCards ?
        Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray() : [];

    internal PredictorRestHealOutcome RestHealOutcome(bool isMimicked, int? maxHp = null)
    {
        _preview.Partial?.RecordRestHealInputs(this, isMimicked, maxHp);
        int humidifier = 5 * Working.Relics.Count(r => !r.Melted && r.Key.Entry == "STONE_HUMIDIFIER");
        if (Context.HasRunModifier("NIGHT_TERRORS"))
        {
            if (maxHp is null or < 1) throw new InvalidOperationException("PredictorRestMaxHpFactRequired");
            int loss = isMimicked ? 0 : Math.Min(5, maxHp.Value + humidifier - 1);
            return new(0, 0, humidifier - loss, isMimicked, maxHp, loss);
        }
        return new(.3m, 15 * Working.Relics.Count(r => !r.Melted && r.Key.Entry == "REGAL_PILLOW"),
            humidifier, isMimicked);
    }

    private List<PredictorRewardPlan> RestHealRewardPlans()
    {
        var plans = new List<PredictorRewardPlan>();
        foreach (var relic in Working.Relics.Where(r => !r.Melted))
            switch (relic.Key.Entry)
            {
                case "DREAM_CATCHER": plans.Add(new(PredictorRewardKind.Card,
                    EncounterSource: true, IsFromCombat: false)); break;
                case "TINY_MAILBOX": plans.Add(new(PredictorRewardKind.Potion)); plans.Add(new(PredictorRewardKind.Potion)); break;
            }
        return plans;
    }

    // Dense Vegetation calls the same heal command, then resumes its event's
    // mandatory Fight option only after any reward choices have finished.
    internal bool BeginEventMimicRestHeal(long sourceId)
    {
        if (Phase != PredictorPhase.EventChoice) throw new InvalidOperationException("PredictorMimicRestHealPhaseInvalid");
        return BeginMimicRestHealRewards(RestHealRewardPlans(), sourceId);
    }

    internal void ResetRest()
    {
        RestOptions = [];
        _restPendingObtain = null;
        _restHealRewardsPending = false;
        _restMaxHpFact = null;
        _restHatchSuppliers = [];
        _restHatchSelectionCount = 0;
    }

    internal void EnterRestSite(int act, int floor)
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete || !Working.OpeningCompleted || floor < 1 || !Working.Acts.Any(a => a.Act == act))
            throw new InvalidOperationException("PredictorRestEntryInvalid");
        PredictorSettlementEffects.RequireImplementedInventory(Working, context: Context);
        var position = new PredictorPosition(act, floor, $"rest:{act}:{floor}");
        if (_checkpoints.Any(c => c.CompletedNode?.NodeId == position.NodeId)) throw new InvalidOperationException("PredictorDuplicateNode");
        // Options are transaction instances: generate once; cancellation does not
        // regenerate them or restore an already-consumed action.
        RestOptions = [PredictorRestAction.Heal, PredictorRestAction.Smith];
        // RunState.IterateHookListeners visits Deck cards before relics.
        _restHatchSuppliers = Working.Deck.Where(id => Working.Card(id).Key.Entry == "BYRDONIS_EGG").ToImmutableArray();
        foreach (long cardId in _restHatchSuppliers) RestOptions = RestOptions.Add(PredictorRestAction.Hatch);
        _restHatchSelectionCount = 0;
        foreach (var relic in Working.Relics.Where(r => !r.Melted))
            RestOptions = relic.Key.Entry switch
            {
                "GIRYA" when relic.TimesLifted < 3 => RestOptions.Add(PredictorRestAction.Lift),
                "SHOVEL" => RestOptions.Add(PredictorRestAction.Dig),
                "MEAT_CLEAVER" => RestOptions.Add(PredictorRestAction.Cook),
                "PAELS_GROWTH" => RestOptions.Add(PredictorRestAction.Clone),
                "PUMPKIN_CANDLE" => RestOptions.Add(PredictorRestAction.Kindle),
                _ => RestOptions
            };
        if (Context.HasRunModifier("MIDAS"))
            RestOptions = RestOptions.RemoveAll(a => a == PredictorRestAction.Smith);
        Working = Working with { Position = position };
        Working = Working with { Relics = Working.Relics.Select(r => !r.Melted &&
            r.Key.Entry is "VENERABLE_TEA_SET" or "FAKE_VENERABLE_TEA_SET"
            ? r with { GainEnergyInNextCombat = true } : r).ToImmutableArray() };
        _inputs = []; Rewards = []; GenerationStages = [];
        _parentReward = _cardSelection = null;
        _restPendingObtain = null;
        _restHealRewardsPending = false;
        _restMaxHpFact = null;
        Phase = PredictorPhase.RestSite;
        Accept(new("EnterRestSite", 0, [], null, null));
        _preview.Partial?.RecordRestOptions(this);
    }

    private PredictorInputResult SubmitRest(PredictorInput input)
    {
        var previous = _restModelTrace;
        var trace = _preview.Partial?.BeginRestCommand(this, input);
        _restModelTrace = trace;
        try
        {
            var result = SubmitRestCore(input);
            trace?.Return(this, result);
            return result;
        }
        finally { _restModelTrace = previous; }
    }

    private PredictorInputResult SubmitRestCore(PredictorInput input)
    {
        if (Phase == PredictorPhase.RestHealFact)
        {
            if (input is not ProvideRestMaxHpFact { MaxHp: > 0 } fact) return PredictorInputResult.Invalid;
            _restMaxHpFact = fact.MaxHp;
            Phase = PredictorPhase.RestSite;
            Accept(new("RestMaxHpFact", 0, [], null, null));
            return SubmitRest(new SelectRestAction(PredictorRestAction.Heal));
        }
        if (Phase == PredictorPhase.RestCookCards)
        {
            if (input is CancelCookSelection)
            {
                Phase = PredictorPhase.RestSite;
                Accept(new("CancelRestCook", 0, [], null, null));
                return PredictorInputResult.Accepted;
            }
            if (input is not SelectCookCards cooked || cooked.CardIds.IsDefault || cooked.CardIds.Length != 2 ||
                cooked.CardIds.Distinct().Count() != 2 || cooked.CardIds.Any(id => !RestCookCandidates.Contains(id)))
                return PredictorInputResult.Invalid;
            foreach (long id in cooked.CardIds) ApplySpoilsMapBeforeRemoved(id);
            foreach (long id in cooked.CardIds) _restModelTrace?.ReadRestCard(this, id, smith: false);
            RemoveCards(cooked.CardIds);
            _restModelTrace?.Write("cook.removedIds", string.Join(" | ", cooked.CardIds), alias: "action.Cook");
            _restModelTrace?.Write("cook.nominalMaxHpDelta", 5, alias: "action.Cook");
            _restModelTrace?.RestDeckAfter(this, PredictorRestAction.Cook);
            Accept(new("RestCookRemoveTwoGainMaxHp5", 0, cooked.CardIds, null, null)
            { EventHealthConsequences = [new(5, 5)] });
            CompleteRestAction(PredictorRestAction.Cook);
            return PredictorInputResult.Accepted;
        }
        if (Phase == PredictorPhase.SmithCards)
        {
            if (input is CancelSmithSelection)
            {
                Phase = PredictorPhase.RestSite;
                Accept(new("CancelSmithSelection", 0, [], null, null));
                return PredictorInputResult.Accepted;
            }
            if (input is not SelectSmithCard selected || !Selection.CandidateCardIds.Contains(selected.CardId)) return PredictorInputResult.Invalid;
            var card = Working.Card(selected.CardId);
            _restModelTrace?.ReadRestCard(this, card.Id, smith: true);
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, card with { UpgradeLevel = card.UpgradeLevel + 1 }));
            _restModelTrace?.Write("smith.target.upgradeLevel", Working.Card(card.Id).UpgradeLevel, alias: "action.Smith");
            _restModelTrace?.RestDeckAfter(this, PredictorRestAction.Smith);
            Accept(new("RestSmith", card.Id, [card.Id], null, null));
            CompleteRestAction(PredictorRestAction.Smith);
            return PredictorInputResult.Accepted;
        }
        if (input is LeaveRestSite)
        {
            Accept(new("LeaveRestSite", 0, [], null, null));
            RestOptions = [];
            CompleteNode();
            return PredictorInputResult.Accepted;
        }
        if (input is not SelectRestAction select || !RestOptions.Contains(select.Action)) return PredictorInputResult.Invalid;
        switch (select.Action)
        {
            case PredictorRestAction.Smith:
                _restModelTrace?.RestQualification(this, select.Action);
                if (!Working.Deck.Any(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel)) return PredictorInputResult.NotAllowed;
                Phase = PredictorPhase.SmithCards;
                Accept(new("BeginRestSmith", 0, [], null, null));
                return PredictorInputResult.Accepted;
            case PredictorRestAction.Lift:
                int index = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "GIRYA");
                if (index < 0) return PredictorInputResult.NotAllowed;
                _restModelTrace?.Relic(this, "GIRYA", "timesLifted", Working.Relics[index].TimesLifted);
                Working = Working with { Relics = Working.Relics.SetItem(index, Working.Relics[index] with { TimesLifted = Working.Relics[index].TimesLifted + 1 }) };
                _restModelTrace?.RestCounterAfter("girya.timesLifted", Working.Relics[index].TimesLifted, PredictorRestAction.Lift, "relics.girya.timesLifted");
                Accept(new("RestLift", Working.Relics[index].Id, [], null, null));
                break;
            case PredictorRestAction.Dig:
                _restModelTrace?.DigBefore(this);
                var digging = Working;
                ModelKey pulled = PredictorRewardGeneration.PullRelic(Context, ref digging);
                PredictorSettlementEffects.RequireImplementedRelic(pulled, context: Context);
                Working = digging;
                _restModelTrace?.DigAfter(this, pulled);
                _restPendingObtain = PredictorRestAction.Dig;
                long digSource = Working.Relics.First(r => !r.Melted && r.Key.Entry == "SHOVEL").Id;
                bool digComplete = BeginObtainRelic(pulled, null, PredictorObtainReturn.Rest, digSource);
                Accept(new("RestDig", digSource, [], null, null));
                if (!digComplete) return PredictorInputResult.Accepted;
                _restPendingObtain = null;
                break;
            case PredictorRestAction.Cook:
                _restModelTrace?.RestQualification(this, select.Action);
                if (Working.Deck.Count(id => Working.Card(id).Removable) < 2) return PredictorInputResult.NotAllowed;
                Phase = PredictorPhase.RestCookCards;
                Accept(new("BeginRestCook", 0, [], null, null));
                return PredictorInputResult.Accepted;
            case PredictorRestAction.Clone:
                var originals = Working.Deck.Select(Working.Card)
                    .Where(c => c.Enchantment?.Entry == "CLONE").ToImmutableArray();
                _restModelTrace?.Read("clone.enchantedOriginals", string.Join(" | ", originals.Select(c => c.Id + ":" + c.Key.Serialized)));
                foreach (var original in originals)
                    Working = PredictorObtainSources.CloneCardToDeck(Context, Working, original);
                _restModelTrace?.Write("clone.originalsProcessed", originals.Length, alias: "action.Clone");
                _restModelTrace?.RestDeckAfter(this, PredictorRestAction.Clone);
                Accept(new("RestClone", 0, originals.Select(c => c.Id).ToImmutableArray(), null, null));
                break;
            case PredictorRestAction.Hatch:
                PredictorSettlementEffects.RequireImplementedRelic(new("RELIC", "BYRDPIP"), context: Context);
                _restPendingObtain = PredictorRestAction.Hatch;
                _restModelTrace?.Read("hatch.selectionIndex", _restHatchSelectionCount);
                long hatchSource = _restHatchSuppliers[_restHatchSelectionCount++];
                _restModelTrace?.Read("hatch.supplierId", hatchSource);
                bool hatchComplete = BeginObtainRelic(new("RELIC", "BYRDPIP"), null, PredictorObtainReturn.Rest, hatchSource);
                Accept(new("RestHatch", hatchSource, [], null, null));
                if (!hatchComplete) return PredictorInputResult.Accepted;
                _restPendingObtain = null;
                break;
            case PredictorRestAction.Kindle:
                int candle = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "PUMPKIN_CANDLE");
                if (candle < 0) return PredictorInputResult.NotAllowed;
                _restModelTrace?.Relic(this, "PUMPKIN_CANDLE", "kindleCount", Working.Relics[candle].KindleCount);
                Working = Working with { Relics = Working.Relics.SetItem(candle,
                    Working.Relics[candle] with { KindleCount = checked(Working.Relics[candle].KindleCount + 5) }) };
                _restModelTrace?.RestCounterAfter("candle.kindleCount", Working.Relics[candle].KindleCount, PredictorRestAction.Kindle, "relics.pumpkinCandle.kindleCount");
                Accept(new("RestKindle", Working.Relics[candle].Id, [], null, null));
                break;
            case PredictorRestAction.Heal:
                _restModelTrace?.Read("heal.modifier.NIGHT_TERRORS", Context.HasRunModifier("NIGHT_TERRORS"));
                if (Context.HasRunModifier("NIGHT_TERRORS")) _restModelTrace?.Read("heal.maximumHpProvided", _restMaxHpFact.HasValue);
                if (Context.HasRunModifier("NIGHT_TERRORS") && _restMaxHpFact == null)
                {
                    Phase = PredictorPhase.RestHealFact;
                    Accept(new("BeginRestHealFact", 0, [], null, null));
                    return PredictorInputResult.Accepted;
                }
                // HP remains a typed consequence because the independent run
                // state has no combat HP fact. After-heal hooks precede rewards.
                var outcome = RestHealOutcome(false, _restMaxHpFact);
                _preview.Partial?.RecordRestHealOutput(this, outcome);
                _restMaxHpFact = null;
                Accept(new("RestHeal", 0, [], null, null)
                {
                    RestHealOutcome = outcome,
                    EventHealthConsequences = outcome.MaxHpDelta == 0 ? [] :
                        [new(outcome.OverrideHealAmount == null ? outcome.MaxHpDelta : 0, outcome.MaxHpDelta)]
                });
                _restModelTrace?.ReadRestHealHooks(this);
                var plans = RestHealRewardPlans();
                _restModelTrace?.Write("heal.rewardPlans.count", plans.Count, alias: "heal");
                if (plans.Count > 0)
                {
                    if (!BeginRestHealRewards(plans, 0, default))
                    {
                        _restHealRewardsPending = true;
                        return PredictorInputResult.Accepted;
                    }
                }
                break;
            default: return PredictorInputResult.Invalid;
        }
        CompleteRestAction(select.Action);
        return PredictorInputResult.Accepted;
    }

    private void CompleteRestAction(PredictorRestAction action)
    {
        RestOptions = PredictorSettlementEffects.Has(Working, "MINIATURE_TENT") ? RestOptions.Remove(action) : [];
        _restModelTrace?.RestComplete(this, action);
        Phase = PredictorPhase.RestSite;
        _requestId++;
        // Explicit Leave, even when no actions remain; no implicit map travel.
    }

    internal void ResumeRestAfterObtain(long sourceId)
    {
        if (_restPendingObtain is not { } action || action is not (PredictorRestAction.Dig or PredictorRestAction.Hatch))
            throw new InvalidOperationException("PredictorRestObtainContinuationInvalid");
        _restPendingObtain = null;
        Phase = PredictorPhase.RestSite;
        Accept(new("RestObtainComplete", sourceId, [], null, null));
        CompleteRestAction(action);
    }

    internal void ResumeRestAfterHealRewards(long sourceId)
    {
        if (!_restHealRewardsPending) throw new InvalidOperationException("PredictorRestHealContinuationInvalid");
        _restHealRewardsPending = false;
        Phase = PredictorPhase.RestSite;
        Accept(new("RestHealRewardsComplete", sourceId, [], null, null));
        CompleteRestAction(PredictorRestAction.Heal);
    }
}
