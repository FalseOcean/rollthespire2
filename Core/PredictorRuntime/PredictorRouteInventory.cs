using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// BeforeChoices is after combat settlement and before reward collection, or
// before a non-combat node's choices. BeforeLeave is after choices/collection,
// either before its closing input or on the completed node's departure map.
internal enum PredictorRoutePotionStage { BeforeChoices, BeforeLeave }
internal enum PredictorRoutePotionActionKind { Discard, Use }
internal sealed record PredictorPlannedPotionAction(PredictorRoutePotionStage Stage,
    PredictorRoutePotionActionKind Action, int Slot, PredictorPotion ExpectedPotion, PredictorPhase ExpectedPhase);

internal sealed record PredictorRoutePotionSlotKnowledge(int Slot, PredictorPotion? Value,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses, bool CanDiscard, bool CanUse)
{
    internal bool IsKnown => PendingCauses.IsEmpty;
}

internal sealed record PredictorRouteInventoryKnowledge(PredictorPhase Phase,
    ImmutableArray<PredictorRouteCardKnowledge> Cards,
    ImmutableArray<PredictorCombatPendingCause> DeckPendingCauses,
    ImmutableArray<PredictorRouteRelicKnowledge> Relics,
    ImmutableArray<PredictorCombatPendingCause> RelicPendingCauses,
    int? PotionCapacity, ImmutableArray<PredictorRoutePotionSlotKnowledge> PotionSlots,
    ImmutableArray<PredictorCombatPendingCause> PotionPendingCauses);

internal sealed record PredictorRoutePotionBoundaryKnowledge(PredictorRoutePotionStage Stage,
    PredictorRouteInventoryKnowledge Before, PredictorRouteInventoryKnowledge After, int AppliedActions);

internal sealed partial class PredictorRun
{
    // Live map forecasts display generated bottles without projecting their
    // receipt, replacement or later slot management. Explicit reward choices
    // and potion actions still use the complete detached simulator below.
    private static bool IsLiveDefaultPotionOmission(PredictorRun run, PredictorReward reward,
        List<RouteDependency> dependencies) =>
        run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
        reward.Kind == PredictorRewardKind.Potion && !reward.Mandatory;

    private static void RegisterLiveDefaultPotionOmission(PredictorRun run, PredictorReward reward,
        List<RouteDependency> dependencies)
    {
        if (!IsLiveDefaultPotionOmission(run, reward, dependencies)) return;
        // Keep child offers after private LeaveObtainChild/CompleteNode has
        // closed their transaction. Its Skip is not a potion receipt decision.
        if (!run._preview.Rewards.PotionDisplayKnowledge.ContainsKey(reward.Id))
            run._preview.Rewards.PotionDisplayKnowledge[reward.Id] = (run.Working.Position.NodeId,
                ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(reward)]);
        if (!CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty ||
            RouteRelicMembershipCauses(run, dependencies, "SOZU").IsEmpty &&
                PredictorSettlementEffects.Has(run.Working, "SOZU")) return;
        // A private uncollected witness is not evidence that later real slots
        // remain empty/full. Only actual slot-dependent consumers read this
        // local-input boundary; it owns no drop RNG or route continuation gate.
        AddDependency(dependencies, new(PredictorRouteCauseEvidence.CreateExplicit(
            "MapPotionInventoryNotProjected", run.Working.Position.NodeId, PredictorCombatDomain.PotionInventory,
            PredictorPendingReasonKind.MissingObservation,
            "Live map forecasts display potion drops without carrying their receipt or slot management across the route; a potion-dependent event needs local inventory input.",
            ["potions.orderedSlots"], occurrenceKey: "live-map-potion-scope")));
    }

    private static void ValidatePlannedPotionActions(ImmutableArray<PredictorPlannedPotionAction> actions)
    {
        RequirePlan(!actions.IsDefault);
        PredictorRoutePotionStage previous = PredictorRoutePotionStage.BeforeChoices;
        foreach (var action in actions)
        {
            RequirePlan(action != null && Enum.IsDefined(action.Stage) && Enum.IsDefined(action.Action) &&
                Enum.IsDefined(action.ExpectedPhase) &&
                (action.ExpectedPhase != PredictorPhase.Complete || action.Stage == PredictorRoutePotionStage.BeforeLeave) &&
                (int)action.Stage >= (int)previous && action.Slot >= 0 && action.ExpectedPotion != null &&
                action.ExpectedPotion.Id > 0 && action.ExpectedPotion.Key.IsValid && action.ExpectedPotion.Key.Category == "POTION");
            previous = action.Stage;
        }
    }

    private static PredictorRouteInventoryKnowledge ObserveRouteInventory(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, bool completedDeparture = false)
    {
        if (CausesFor(dependencies, PredictorCombatDomain.InstanceIds).IsEmpty)
            knownInstanceIds.UnionWith(run.Working.Cards.Select(c => c.Id).Concat(run.Working.Relics.Select(r => r.Id))
                .Concat(run.Working.PotionSlots.OfType<PredictorPotion>().Select(p => p.Id)));
        var relicCauses = CausesFor(dependencies, PredictorCombatDomain.Relics);
        var relics = run.Working.Relics.Where(r => !r.Melted).Select(relic => new PredictorRouteRelicKnowledge(relic,
            relicCauses.AddRange(relic.Key.Entry == "PAELS_WING" ? CausesFor(dependencies, PredictorCombatDomain.RewardAlternativeState) : [])
                .AddRange(relic.Key.Entry is "SILVER_CRUCIBLE" or "SILKEN_TRESS" ? CausesFor(dependencies, PredictorCombatDomain.CardRewardHookState) : [])
                .AddRange(knownInstanceIds.Contains(relic.Id) ? [] : CausesFor(dependencies, PredictorCombatDomain.InstanceIds))
                .Distinct().ToImmutableArray())
                { PaelsToothStorage = run._preview.PaelsToothStorage.GetValueOrDefault(relic.Id)?.Knowledge }).ToImmutableArray();
        // An unresolved inventory can include unknown occupied slots or capacity.
        // The witness's slot count and nulls cannot establish either fact.
        var potionCauses = CausesFor(dependencies, PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics);
        var slots = ImmutableArray.CreateBuilder<PredictorRoutePotionSlotKnowledge>();
        if (potionCauses.IsEmpty)
            for (int slot = 0; slot < run.Working.PotionSlots.Length; slot++)
            {
                var potion = run.Working.PotionSlots[slot];
                var identityCauses = potion != null && !knownInstanceIds.Contains(potion.Id)
                    ? CausesFor(dependencies, PredictorCombatDomain.InstanceIds) : [];
                bool available = identityCauses.IsEmpty && potion != null && RoutePotionActionsAvailable(run, completedDeparture);
                slots.Add(new(slot, identityCauses.IsEmpty ? potion : null, identityCauses, available,
                    available && RoutePotionUseAvailable(run, potion!, dependencies)));
            }
        return new(run.Phase, ObserveRouteCards(run, dependencies, knownInstanceIds),
            CausesFor(dependencies, PredictorCombatDomain.Deck), relics, relicCauses,
            relicCauses.IsEmpty ? run.Working.PotionSlots.Length : null, slots.ToImmutable(), potionCauses);
    }

    private static bool RoutePotionActionsAvailable(PredictorRun run, bool completedDeparture) =>
        completedDeparture && run.Phase == PredictorPhase.Complete || run.Request != null &&
        run.Phase is not (PredictorPhase.Complete or PredictorPhase.CombatFacts or PredictorPhase.OpeningChoice or
            PredictorPhase.EventEligibility or PredictorPhase.AncientChoice or PredictorPhase.RoomEntryHealth or
            PredictorPhase.OpeningModifier or PredictorPhase.OpeningModifierCards or PredictorPhase.Victory) && !run.EventPotionsLocked;

    private static bool RoutePotionUseAvailable(PredictorRun run, PredictorPotion potion, List<RouteDependency> dependencies)
    {
        if (potion.Key.Entry == "ENTROPIC_BREW")
            return CausesFor(dependencies, PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.Relics |
                PredictorCombatDomain.InstanceIds).IsEmpty;
        return potion.Key.Entry is "AMBERGRIS" or "BLOOD_POTION" or "FRUIT_JUICE" ||
            potion.Key.Entry == "FOUL_POTION" && (run.CanUseFoulPotionInShop || run.CanUseFoulPotionInEvent);
    }

    private static PredictorRoutePotionBoundaryKnowledge ApplyRouteInventoryActions(PredictorRun run,
        PredictorPlannedNode plan, PredictorRoutePotionStage stage, List<RouteDependency> dependencies,
        HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        bool completedDeparture = stage == PredictorRoutePotionStage.BeforeLeave && run.Phase == PredictorPhase.Complete;
        var before = ObserveRouteInventory(run, dependencies, knownInstanceIds, completedDeparture);
        int applied = 0;
        foreach (var action in plan.PotionActions.Where(a => a.Stage == stage))
        {
            token.ThrowIfCancellationRequested();
            PredictorInput input = action.Action == PredictorRoutePotionActionKind.Discard
                ? new DiscardPotion(action.Slot, action.ExpectedPotion.Id)
                : new UseOutOfCombatPotion(action.Slot, action.ExpectedPotion.Id);
            // A saved active-node draft may already contain the same authored
            // operation. A consumed stable identity is satisfied only by that
            // exact accepted input, never by another bottle now in its slot.
            if (!run.Working.PotionSlots.Any(p => p?.Id == action.ExpectedPotion.Id) &&
                run._nodeReplayInputs.Contains(input) && AcceptedRoutePotionIdentity(run, action.ExpectedPotion))
            { applied++; continue; }
            var inventory = ObserveRouteInventory(run, dependencies, knownInstanceIds, completedDeparture);
            var slot = inventory.PotionSlots.SingleOrDefault(p => p.Slot == action.Slot);
            if (run.Phase != action.ExpectedPhase || slot is not { IsKnown: true } || slot.Value != action.ExpectedPotion)
            {
                local.Add(RouteCause("PlannedPotionBindingPending", plan.NodeId, PredictorCombatDomain.Continuation,
                    run.Phase == action.ExpectedPhase ? slot?.PendingCauses ?? inventory.PotionPendingCauses : [],
                    PredictorRouteCauseEvidence.Key("potion-action", stage.ToString(), applied.ToString(), action.Slot.ToString())));
                break;
            }
            if (!(action.Action == PredictorRoutePotionActionKind.Discard ? slot.CanDiscard : slot.CanUse))
            {
                var parents = action.Action == PredictorRoutePotionActionKind.Use &&
                    action.ExpectedPotion.Key.Entry == "ENTROPIC_BREW" && RoutePotionActionsAvailable(run, completedDeparture)
                    ? CausesFor(dependencies, PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.Relics | PredictorCombatDomain.InstanceIds) : [];
                var cause = RouteCause("PlannedPotionActionUnavailable", plan.NodeId, PredictorCombatDomain.Continuation, parents,
                    PredictorRouteCauseEvidence.Key("potion-action", stage.ToString(), applied.ToString(), action.Slot.ToString()));
                if (!parents.IsEmpty) cause = cause with
                {
                    ReasonKind = PredictorPendingReasonKind.FlowGate,
                    RequiredPropertyKeys = ["rng.combatPotion.state", "flow.guard.entropicBrewRelics", "potions.instanceIds"],
                    SourceEvidence = "PredictorRouteInventory.RoutePotionUseAvailable: Entropic Brew is guarded by CombatPotionRng, Relics and InstanceIds."
                };
                local.Add(cause);
                break;
            }
            var result = completedDeparture ? run.ApplyCompletedRoutePotionInput(input, token) : run.Submit(run.Request!, input, token);
            if (result != PredictorInputResult.Accepted)
            {
                local.Add(RouteCause("PlannedPotionActionPending", plan.NodeId, PredictorCombatDomain.Continuation,
                    occurrenceKey: PredictorRouteCauseEvidence.Key("potion-action", stage.ToString(), applied.ToString(), action.Slot.ToString())));
                break;
            }
            applied++;
            if (CausesFor(dependencies, PredictorCombatDomain.InstanceIds).IsEmpty)
                knownInstanceIds.UnionWith(run.Working.PotionSlots.OfType<PredictorPotion>().Select(p => p.Id));
        }
        return new(stage, before, ObserveRouteInventory(run, dependencies, knownInstanceIds, completedDeparture), applied);
    }

    private static bool AcceptedRoutePotionIdentity(PredictorRun run, PredictorPotion expected) =>
        run._checkpoints.SelectMany(c => c.State.PotionSlots).Any(p => p == expected) ||
        run._inputs.SelectMany(i => i.GeneratedPotions).Any(p => p == expected) ||
        run.Rewards.AddRange(run.CompletedChildRewards).Any(r => r.Kind == PredictorRewardKind.Potion &&
            r.ContentInstanceId == expected.Id && r.Key == expected.Key);

    // Only the detached route projection invokes this at its actual departure
    // boundary. Commit its edited immutable state back to the witness's final
    // checkpoint, which owns the next AdvanceRoute's input state.
    private PredictorInputResult ApplyCompletedRoutePotionInput(PredictorInput input, CancellationToken token)
    {
        if (Phase != PredictorPhase.Complete || _checkpoints[^1].CompletedNode?.NodeId != Working.Position.NodeId)
            throw new InvalidOperationException("PredictorRoutePotionDepartureBoundaryInvalid");
        token.ThrowIfCancellationRequested();
        var result = ApplyPotionInventoryInput(input, token);
        if (result != PredictorInputResult.Accepted) return result;
        Working.Validate();
        var checkpoint = _checkpoints[^1];
        _checkpoints = _checkpoints.SetItem(_checkpoints.Length - 1, checkpoint with
        { State = Working, CompletedNode = checkpoint.CompletedNode! with { Inputs = _inputs } });
        return result;
    }
}
