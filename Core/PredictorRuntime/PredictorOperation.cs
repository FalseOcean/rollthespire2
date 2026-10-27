using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperation
{
    // Every query starts from detached current facts. Explicit further inputs
    // answer child choices; no route default or automatic card pick participates.
    internal static PredictorOperationResult Preview(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> inputs, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Fingerprint) || snapshot.Context.Operation == null ||
            snapshot.Scene == null || inputs.IsDefault || inputs.Any(i => i == null) ||
            snapshot.CurrentHp < 0 || snapshot.MaximumHp < 1 || snapshot.CurrentHp > snapshot.MaximumHp)
            throw new ArgumentException("PredictorOperationSnapshotInvalid");
        token.ThrowIfCancellationRequested();
        var run = new PredictorRun(snapshot.Context, snapshot.State);
        snapshot.Scene.Restore(run);
        run.StartOperationHealth(snapshot.CurrentHp, snapshot.MaximumHp);
        var disposition = PredictorInputResult.Accepted;
        foreach (var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            disposition = run.SubmitOperation(input, token);
            if (disposition != PredictorInputResult.Accepted) break;
        }
        token.ThrowIfCancellationRequested();
        var output = run.OperationResult(snapshot.Fingerprint, disposition);
        return output with { Inputs = inputs,
            Waiting = output.Waiting with { SelectedCardIds = inputs.IsEmpty ? snapshot.SelectedCardIds : [] } };
    }

    internal static PredictorOperationResult Preview(PredictorOperationSnapshot snapshot,
        PredictorInput input, CancellationToken token = default) => Preview(snapshot, [input], token);
}

internal sealed partial class PredictorRun
{
    private int _operationHp;
    private int _operationMaxHp;
    private int _operationHealthEffectsSeen;
    private bool _operationHealthStarted;
    private bool _operationHealthKnown = true;
    private ImmutableArray<PredictorOperationRandomUpgrade> _operationRandomUpgrades = [];

    private void ObserveOperationRandomUpgrade(long sourceId, RolltheSpire2.Core.Identity.ModelKey source,
        PredictorState before)
    {
        if (Context.Operation == null) return;
        var changed = Working.Deck.Where(id => Working.Card(id).UpgradeLevel != before.Card(id).UpgradeLevel).ToArray();
        if (changed.Length == 0) return;
        _operationRandomUpgrades = _operationRandomUpgrades.Add(new(sourceId, source,
            changed.Select(before.Card).ToImmutableArray(), changed.Select(Working.Card).ToImmutableArray()));
    }

    internal void StartOperationHealth(int hp, int maxHp)
    {
        _operationHp = hp;
        _operationMaxHp = maxHp;
        _operationHealthEffectsSeen = Working.CurrentHealthEffects.Length;
        _operationHealthStarted = true;
        SynchronizeOperationEventHealth();
    }

    internal PredictorInputResult SubmitOperation(PredictorInput input, CancellationToken token)
    {
        if (Context.Operation == null) throw new InvalidOperationException("PredictorOperationContextMissing");
        token.ThrowIfCancellationRequested();
        if (Request is not { } request) return PredictorInputResult.Stale;
        if (TrySubmitOperationMend(input) is { } mendResult) return mendResult;
        if (input is TakeReward opaque && Phase == PredictorPhase.Rewards && _cardSelection == null &&
            OpenReward(opaque.RewardId) is var opaqueIndex && opaqueIndex >= 0 &&
            Rewards[opaqueIndex].Kind == PredictorRewardKind.Opaque)
        {
            if (opaque.CardId != null) return PredictorInputResult.Invalid;
            MarkTaken(opaque.RewardId);
            Accept(new("OpaqueRewardAdditionalEffectsOmitted", opaque.RewardId, [], null, null));
            DrainObtainCompletions(token);
            CompleteOperationLinkedRewards(token);
            return PredictorInputResult.Accepted;
        }
        if (!ValidateOperationChoice(input))
            return PredictorInputResult.Invalid;
        // Native potion rewards stay available when full. Route's replacement
        // interaction is a planning convenience, not a native choice to import.
        if (input is TakeReward take && Rewards.FirstOrDefault(r => r.Id == take.RewardId) is
            { Kind: PredictorRewardKind.Potion, Status: PredictorRewardStatus.Open } &&
            Working.PotionSlots.All(p => p != null) && !PredictorSettlementEffects.Has(Working, "SOZU"))
            return PredictorInputResult.TooFull;
        if (input is CorrectGoldBalance or AdjustCurrentCombat or ReplaceRewardPotion)
            return PredictorInputResult.Invalid;
        var result = SubmitCore(request, input, token);
        if (result is PredictorInputResult.Accepted or PredictorInputResult.NotAllowed)
        {
            DrainObtainCompletions(token);
            CompleteOperationLinkedRewards(token);
        }
        if (result != PredictorInputResult.Accepted) return result;
        if (Phase == PredictorPhase.EventChoice && input is DiscardPotion or UseOutOfCombatPotion)
            RefreshEventOptionsAfterPotionMutation();
        ObserveOperationHealthEffects();
        // HP is a captured fact, not another player decision. Feed the existing
        // numerical handlers at their actual fact boundary and let them resume.
        while (Phase is PredictorPhase.ObtainHealth or PredictorPhase.RestHealFact)
        {
            token.ThrowIfCancellationRequested();
            if (!_operationHealthKnown || _operationHp <= 0) break;
            var before = Request!;
            PredictorInput health = Phase == PredictorPhase.ObtainHealth
                ? new ProvideEventHealthFact(new(_operationHp, _operationMaxHp))
                : new ProvideRestMaxHpFact(_operationMaxHp);
            if (SubmitCore(before, health, token) != PredictorInputResult.Accepted || Request == before)
                throw new InvalidOperationException("PredictorOperationHealthContinuationInvalid");
            DrainObtainCompletions(token);
            CompleteOperationLinkedRewards(token);
            ObserveOperationHealthEffects();
        }
        Working.Validate();
        return result;
    }

    internal PredictorOperationResult OperationResult(string fingerprint, PredictorInputResult disposition)
    {
        ObserveOperationHealthEffects();
        var selection = OperationSelectionBounds(Selection);
        if (Phase == PredictorPhase.EventCards)
            selection = new(null, null, _eventCardsRequired, _eventCardCandidates);
        var cards = Phase == PredictorPhase.EventCards ? EventOfferedCards :
            Phase == PredictorPhase.ObtainChooseManyCards ? ObtainManyCardChoices : ObtainCardChoices;
        return new(fingerprint, disposition, Working, _operationHp, _operationMaxHp,
            new(Phase, selection, EventOptions, cards, ScrollBoxBundles)
            { Cancelable = OperationChoiceCancelable, TrialAbandonConfirmationPending = _trialAbandonPending,
                AncientOffers = AncientOffers, RestOffers = RestOffers,
                MendTargetPending = OperationMendTargetPending, MendTargets = OperationMendTargets,
                OpaqueRestOptions = OperationOpaqueRestOptions },
            Rewards, CompletedChildRewards, _inputs, ShopOffers)
        { CrystalBoard = ExportOperationCrystalBoard(), TreasureParty = ExportOperationTreasureParty(),
            LinkedRewardGroups = OperationLinkedGroups, HealthKnown = _operationHealthKnown,
            MendOutcome = OperationMendOutcome, RandomUpgrades = _operationRandomUpgrades };
    }

    private void ObserveOperationHealthEffects()
    {
        if (!_operationHealthStarted) return;
        if (_operationHealthEffectsSeen > Working.CurrentHealthEffects.Length) _operationHealthEffectsSeen = 0;
        foreach (var effect in Working.CurrentHealthEffects.Skip(_operationHealthEffectsSeen))
        {
            _operationMaxHp = Math.Max(1, checked(_operationMaxHp + effect.MaxHpDelta));
            _operationHp = Math.Clamp(checked(_operationHp + effect.HealRequested), 0, _operationMaxHp);
        }
        _operationHealthEffectsSeen = Working.CurrentHealthEffects.Length;
        SynchronizeOperationEventHealth();
    }

    private void ObserveOperationInput(PredictorAcceptedInput input)
    {
        if (!_operationHealthStarted) return;
        ObserveOperationHealthEffects();
        if (input.RestHealOutcome is { IsMimicked: false } rest)
            _operationHp = Math.Min(_operationMaxHp, checked(_operationHp +
                (rest.OverrideHealAmount ?? (int)(_operationMaxHp * rest.BaseMaxHpFraction + rest.FlatHealBonus))));
        foreach (var delta in input.EventHealthConsequences)
        {
            _operationMaxHp = Math.Max(1, checked(_operationMaxHp + delta.MaxHpDelta));
            _operationHp = Math.Clamp(checked(_operationHp + delta.HpDelta), 0, _operationMaxHp);
        }
        if (input.PotionHealthEffect is { } potion)
        {
            _operationMaxHp = Math.Max(1, checked(_operationMaxHp + potion.MaxHpDelta));
            _operationHp = Math.Clamp(checked(_operationHp + potion.MaxHpDelta +
                (int)(_operationMaxHp * potion.HealMaxHpFraction)), 0, _operationMaxHp);
        }
        SynchronizeOperationEventHealth();
    }

    private void SynchronizeOperationEventHealth()
    {
        if (_operationHealthStarted && _operationHealthKnown && _eventHealth != null)
            _eventHealth = new(_operationHp, _operationMaxHp);
    }

    private void ObserveOperationVictoryHealth(PredictorEventHealthFact? health, int precedingEffects)
    {
        if (!_operationHealthStarted) return;
        _operationHealthKnown = health != null;
        if (health is null) return;
        _operationHp = health.CurrentHp;
        _operationMaxHp = health.MaxHp;
        // The supplied fact is after AfterCombatEnd. Do not apply its preceding
        // effects a second time; subsequent Victory effects still participate.
        _operationHealthEffectsSeen = precedingEffects;
    }
}
