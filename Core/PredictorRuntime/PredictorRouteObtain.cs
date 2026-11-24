using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static bool SameRouteObtainInput(PredictorInput expected, PredictorInput actual) => (expected, actual) switch
    {
        (ChooseObtainDeckCards a, ChooseObtainDeckCards b) => a.CardIds.SequenceEqual(b.CardIds),
        (ChooseObtainManyCards a, ChooseObtainManyCards b) => a.CardIds.SequenceEqual(b.CardIds),
        (SelectRemovalCards a, SelectRemovalCards b) => a.CardIds.SequenceEqual(b.CardIds),
        _ => expected == actual
    };

    private static bool IsRouteObtainChildInput(PredictorInput input) => input is
        TakeReward or SkipCardReward or LeaveRewards or ChooseObtainCard or ChooseObtainManyCards or ChooseObtainDeckCards or
        SelectTransformationCard or ChooseScrollBoxBundle or ProvideEventHealthFact or ReplaceRewardPotion or
        CancelPotionReplacement or SelectRemovalCards;

    // Shared by shop and Ancient continuation. Resolve semantic intent before
    // submitting any input; a failed binding never changes the offered choice.
    private static PredictorInput? ResolveRouteObtainChildInput(PredictorRun run, PredictorPlannedObtainChild step,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local,
        RoutePartialState.AncientChildTrace? trace = null)
    {
        PredictorInput? Pending(string code, ImmutableArray<PredictorCombatPendingCause> parents = default)
        {
            local.Add(RouteCause(code, run.Working.Position.NodeId, PredictorCombatDomain.Continuation, parents,
                PredictorRouteCauseEvidence.Key("obtain-action", step.ExpectedSource.Serialized, run._inputs.Length.ToString(), step.Action.ToString())));
            return null;
        }
        if (step == null || step.DeckCards.IsDefault || step.ExpectedCards.IsDefault || step.CardIndices.IsDefault || step.ExpectedBundles.IsDefault)
            throw new ArgumentException("PredictorRouteObtainPlanInvalid");
        if (CurrentRouteObtainSource(run) is not { } source || source != step.ExpectedSource)
            return Pending("PlannedObtainSourceChanged");
        bool CardsMatch(ImmutableArray<PredictorCard> actual, ImmutableArray<PredictorCard> expected)
        {
            trace?.Input("expectedCards", System.Text.Json.JsonSerializer.Serialize(expected.Select(DisplayObtainCard)));
            trace?.Read("actualCardOffer", System.Text.Json.JsonSerializer.Serialize(actual.Select(DisplayObtainCard)),
                CausesFor(dependencies, PredictorCombatDomain.RewardsRng));
            return actual.Select(DisplayObtainCard).SequenceEqual(expected.Select(DisplayObtainCard));
        }
        bool IndicesValid(int count)
        {
            trace?.Input("cardIndices", string.Join(" | ", step.CardIndices)); trace?.Read("cardOfferCount", count);
            return step.CardIndices.All(i => i >= 0 && i < count) && step.CardIndices.Distinct().Count() == step.CardIndices.Length;
        }
        var deckRead = RouteObtainDeckRead(run);
        bool KnownTargets()
        {
            trace?.Input("deckTargets", System.Text.Json.JsonSerializer.Serialize(step.DeckCards));
            bool KnownTarget(PredictorPlannedCard target)
            {
                var causes = RouteDeckTargetCauses(run, [target], dependencies, knownInstanceIds,
                    deckRead.Scope, deckRead.Execution);
                bool present = run.Working.Deck.Contains(target.InstanceId);
                trace?.Read("target." + target.InstanceId + ".membership", present, causes);
                if (!present) return false;
                var card = run.Working.Card(target.InstanceId);
                trace?.Read("target." + target.InstanceId + ".model", card.Key.Serialized, causes);
                if (card.Key != target.Key) return false;
                trace?.Read("target." + target.InstanceId + ".floorAdded", card.FloorAdded, causes);
                if (card.FloorAdded != target.FloorAdded) return false;
                return IsKnownRouteDeckTarget(run, target, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution);
            }
            bool known = step.DeckCards.All(KnownTarget);
            trace?.Read("fixedDeckTargetsKnown", known,
                known ? [] : RouteDeckTargetCauses(run, step.DeckCards, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution));
            return known;
        }
        bool CandidateTargetsMissing(ImmutableArray<long> candidates, string field)
        {
            bool missing = step.DeckCards.Any(c => !candidates.Contains(c.InstanceId));
            trace?.Read(field, !missing, RouteDeckTargetCauses(run, step.DeckCards, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution));
            return missing;
        }
        ImmutableArray<PredictorCombatPendingCause> ReceiptCauses(ModelKey relic)
        {
            var causes = ObtainCauses(run, relic, dependencies, entryOnly: true);
            trace?.Gate("rewardReceiptAllowed", causes); return causes;
        }
        ImmutableArray<PredictorCombatPendingCause> Reads(PredictorCombatDomain domains)
        {
            var causes = CausesFor(dependencies, domains);
            trace?.Gate("dependency." + domains, causes);
            return causes;
        }
        switch (step.Action)
        {
            case PredictorObtainChildAction.Reward:
            {
                if (run.Phase != PredictorPhase.Rewards || step.Reward is not { } choice)
                    return Pending("PlannedObtainRewardChanged");
                trace?.Input("reward", System.Text.Json.JsonSerializer.Serialize(choice));
                var candidates = run.Rewards.Where(r => r.Kind == choice.ExpectedReward.Kind &&
                    r.Origin == choice.ExpectedReward.Origin && r.Key == choice.ExpectedReward.Key).ToArray();
                if (choice.Occurrence < 0 || choice.Occurrence >= candidates.Length ||
                    !SameRouteRewardOffer(run, candidates[choice.Occurrence], choice, dependencies)) return Pending("PlannedObtainRewardChanged");
                var reward = candidates[choice.Occurrence];
                var rewardKnowledge = ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(reward)];
                trace?.Read("rewardOffer", System.Text.Json.JsonSerializer.Serialize(reward with { Id = 0 }), rewardKnowledge.PendingCauses);
                trace?.Read("rewardStatus", reward.Status);
                if (reward.Status != PredictorRewardStatus.Open ||
                    !RouteRewardOperationKnown(run, rewardKnowledge, choice))
                    return Pending("PlannedObtainRewardPending", reward.Status == PredictorRewardStatus.Open ?
                        ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(reward)].PendingCauses : []);
                if (choice.Skip)
                    return reward.Kind == PredictorRewardKind.Card && reward.CanSkip && !reward.Mandatory
                        ? new SkipCardReward() : Pending("PlannedObtainRewardPending");
                if (reward.Kind == PredictorRewardKind.Relic && !ReceiptCauses(reward.Key).IsEmpty ||
                    reward.Kind == PredictorRewardKind.Potion && !Reads(PredictorCombatDomain.PotionInventory).IsEmpty)
                {
                    var causes = reward.Kind == PredictorRewardKind.Relic ?
                        ObtainCauses(run, reward.Key, dependencies, entryOnly: true) : Reads(PredictorCombatDomain.PotionInventory);
                    trace?.Gate("rewardReceiptAllowed", causes);
                    return Pending("PlannedObtainRewardDependent", causes);
                }
                // Concrete completion generates the curse in the same Submit.
                // Live drawing instead owns its unknown-identity tail at that
                // boundary; this independent relic receipt need not read Niche.
                if (source.Entry == "NEOWS_BONES" && run.Rewards.Count(r => r.Status == PredictorRewardStatus.Open) == 1 &&
                    !run.CanProjectLiveBonesCurse() && !Reads(PredictorCombatDomain.NicheRng).IsEmpty)
                    return Pending("PlannedObtainClosureDependent", CausesFor(dependencies, PredictorCombatDomain.NicheRng));
                if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
                {
                    trace?.Input("rewardCardIndex", choice.CardIndex?.ToString() ?? "none");
                    if (choice.CardIndex is not { } index || index < 0 || index >= reward.Cards.Length)
                        return Pending("PlannedObtainRewardChanged");
                    return new TakeReward(reward.Id, reward.Kind == PredictorRewardKind.Card ? reward.Cards[index].Id : null);
                }
                return new TakeReward(reward.Id);
            }
            case PredictorObtainChildAction.LeaveRewards:
                if (run.Phase == PredictorPhase.Rewards)
                {
                    trace?.Read("cardRewardSelectionActive", run.Selection.CardRewardId != null);
                    if (run.Selection.CardRewardId == null)
                        trace?.Read("mandatoryRewardOpen", run.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && (!r.CanSkip || r.Mandatory)));
                }
                if (run.Phase != PredictorPhase.Rewards || run.Selection.CardRewardId != null ||
                    run.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && (!r.CanSkip || r.Mandatory)))
                    return Pending("PlannedObtainClosurePending");
                if (source.Entry == "NEOWS_BONES" && !run.CanProjectLiveBonesCurse() && !Reads(PredictorCombatDomain.NicheRng).IsEmpty)
                    return Pending("PlannedObtainClosureDependent", CausesFor(dependencies, PredictorCombatDomain.NicheRng));
                return new LeaveRewards();
            case PredictorObtainChildAction.ChooseCard:
                if (run.Phase != PredictorPhase.ObtainChooseCard || !CardsMatch(run.ObtainCardChoices, step.ExpectedCards) ||
                    !IndicesValid(run.ObtainCardChoices.Length) || step.CardIndices.Length > 1)
                    return Pending("PlannedObtainCardChanged");
                if (!Reads(PredictorCombatDomain.RewardsRng).IsEmpty) return Pending("PlannedObtainCardPending", CausesFor(dependencies, PredictorCombatDomain.RewardsRng));
                return new ChooseObtainCard(step.CardIndices.IsEmpty ? null : run.ObtainCardChoices[step.CardIndices[0]].Id);
            case PredictorObtainChildAction.ChooseManyCards:
                if (run.Phase != PredictorPhase.ObtainChooseManyCards || !CardsMatch(run.ObtainManyCardChoices, step.ExpectedCards) ||
                    !IndicesValid(run.ObtainManyCardChoices.Length)) return Pending("PlannedObtainCardChanged");
                if (!Reads(PredictorCombatDomain.RewardsRng).IsEmpty) return Pending("PlannedObtainCardPending", CausesFor(dependencies, PredictorCombatDomain.RewardsRng));
                return new ChooseObtainManyCards(step.CardIndices.Select(i => run.ObtainManyCardChoices[i].Id).ToImmutableArray());
            case PredictorObtainChildAction.ChooseDeckCards:
                if (run.Phase != PredictorPhase.ObtainDeckCards || !KnownTargets() ||
                    CandidateTargetsMissing(run.ObtainDeckCandidates, "deckCandidateMembership")) return Pending("PlannedObtainDeckTargetPending",
                        run.Phase == PredictorPhase.ObtainDeckCards ? RouteDeckTargetCauses(run, step.DeckCards, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution) : []);
                if (run._obtainDeckOperation == PredictorObtainDeckOperation.Astrolabe &&
                    !Reads(PredictorCombatDomain.NicheRng).IsEmpty) return Pending("PlannedObtainTransformPending", CausesFor(dependencies, PredictorCombatDomain.NicheRng));
                return new ChooseObtainDeckCards(step.DeckCards.Select(c => c.InstanceId).ToImmutableArray());
            case PredictorObtainChildAction.TransformCard:
                if (run.Phase != PredictorPhase.TransformCards || step.DeckCards.Length != 1 || !KnownTargets() ||
                    CandidateTargetsMissing(run.Selection.CandidateCardIds, "transformCandidateMembership")) return Pending("PlannedObtainDeckTargetPending",
                        run.Phase == PredictorPhase.TransformCards && step.DeckCards.Length == 1 ? RouteDeckTargetCauses(run, step.DeckCards, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution) : []);
                if (!Reads(PredictorCombatDomain.NicheRng).IsEmpty) return Pending("PlannedObtainTransformPending", CausesFor(dependencies, PredictorCombatDomain.NicheRng));
                return new SelectTransformationCard(step.DeckCards[0].InstanceId);
            case PredictorObtainChildAction.ChooseBundle:
                if (run.Phase == PredictorPhase.ChooseBundle)
                {
                    trace?.Input("bundleIndex", step.BundleIndex?.ToString() ?? "none");
                    if (step.BundleIndex is >= 0)
                    {
                        trace?.Read("bundleCount", run.ScrollBoxBundles.Length);
                        if (step.BundleIndex < run.ScrollBoxBundles.Length) trace?.Input("expectedBundleCount", step.ExpectedBundles.Length);
                    }
                }
                if (run.Phase != PredictorPhase.ChooseBundle || step.BundleIndex is not { } bundle || bundle < 0 ||
                    bundle >= run.ScrollBoxBundles.Length || step.ExpectedBundles.Length != run.ScrollBoxBundles.Length ||
                    !run.ScrollBoxBundles.Zip(step.ExpectedBundles).All(p => CardsMatch(p.First, p.Second)))
                    return Pending("PlannedObtainBundleChanged");
                if (!Reads(PredictorCombatDomain.RewardsRng).IsEmpty) return Pending("PlannedObtainBundlePending", CausesFor(dependencies, PredictorCombatDomain.RewardsRng));
                return new ChooseScrollBoxBundle(bundle);
            case PredictorObtainChildAction.Health:
                if (run.Phase == PredictorPhase.ObtainHealth && step.Health is { } observedHealth)
                { trace?.Input("health.current", observedHealth.CurrentHp); trace?.Input("health.maximum", observedHealth.MaxHp); }
                if (run.Phase != PredictorPhase.ObtainHealth || step.Health is not { } health ||
                    health.CurrentHp < 1 || health.MaxHp < health.CurrentHp) return Pending("PlannedObtainHealthPending");
                if (!Reads(HealthObtainReadDomains(run, source)).IsEmpty) return Pending("PlannedObtainHealthDependent", CausesFor(dependencies, HealthObtainReadDomains(run, source)));
                return new ProvideEventHealthFact(health);
            case PredictorObtainChildAction.ReplacePotion:
                if (run.Phase == PredictorPhase.ReplacePotion)
                {
                    trace?.Input("potionSlot", step.PotionSlot?.ToString() ?? "none");
                    trace?.Read("potionSlotCount", run.Working.PotionSlots.Length);
                    if (step.PotionSlot is { } observedSlot && observedSlot >= 0 && observedSlot < run.Working.PotionSlots.Length && step.ExpectedPotion is { } observedPotion)
                    {
                        trace?.Input("expectedPotion", System.Text.Json.JsonSerializer.Serialize(observedPotion));
                        trace?.Read("actualPotion", System.Text.Json.JsonSerializer.Serialize(run.Working.PotionSlots[observedSlot]),
                            CausesFor(dependencies, PredictorCombatDomain.PotionInventory | PredictorCombatDomain.InstanceIds));
                    }
                }
                if (run.Phase != PredictorPhase.ReplacePotion || step.PotionSlot is not { } slot || slot < 0 ||
                    slot >= run.Working.PotionSlots.Length || step.ExpectedPotion is not { } potion ||
                    run.Working.PotionSlots[slot] != potion || !Reads(PredictorCombatDomain.PotionInventory | PredictorCombatDomain.InstanceIds).IsEmpty)
                    return Pending("PlannedObtainPotionTargetPending", run.Phase == PredictorPhase.ReplacePotion &&
                        step.PotionSlot is { } plannedSlot && plannedSlot >= 0 && plannedSlot < run.Working.PotionSlots.Length &&
                        step.ExpectedPotion is { } plannedPotion && run.Working.PotionSlots[plannedSlot] == plannedPotion
                        ? CausesFor(dependencies, PredictorCombatDomain.PotionInventory | PredictorCombatDomain.InstanceIds) : []);
                return new ReplaceRewardPotion(slot, potion.Id);
            case PredictorObtainChildAction.CancelPotionReplacement:
                return run.Phase == PredictorPhase.ReplacePotion ? new CancelPotionReplacement() : Pending("PlannedObtainPotionTargetPending");
            case PredictorObtainChildAction.RemoveRewardCards:
                if (run.Phase != PredictorPhase.RewardRemoveCards || step.DeckCards.Length != 1 || !KnownTargets())
                    return Pending("PlannedObtainDeckTargetPending", run.Phase == PredictorPhase.RewardRemoveCards && step.DeckCards.Length == 1
                        ? RouteDeckTargetCauses(run, step.DeckCards, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution) : []);
                return new SelectRemovalCards(step.DeckCards.Select(c => c.InstanceId).ToImmutableArray());
            default: throw new ArgumentException("PredictorRouteObtainPlanInvalid");
        }
    }

    private static bool ApplyRouteObtainChild(PredictorRun run, PredictorPlannedObtainChild step,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token, bool traceAncient = false)
    {
        // The source receipt already projected this exact target operation as
        // unresolved. Preserve the authored intent without trying to bind it
        // to a different source or blocking independent following operations.
        if (run.IsProjectedLiveObtainTarget(step)) return true;
        if (step.Action != PredictorObtainChildAction.Health) CompleteLiveObtainHealth(run, dependencies, token);
        if (step.Action != PredictorObtainChildAction.ChooseDeckCards || step.ExpectedSource.Entry != "PAELS_TOOTH")
            CompleteLivePaelsTooth(run, dependencies, token);
        CaptureCompletedCardRewardEdits(run, dependencies);
        var trace = traceAncient ? run._preview.Partial?.BeginAncientChild(run, step) : null;
        int causeStart = local.Count;
        PredictorReward? acceptedReward = null;
        PredictorPlannedRewardChoice? acceptedChoice = null;
        PredictorInput? input = null;
        bool inputResolved = false;
        if (step.Action == PredictorObtainChildAction.Reward && step.Reward is { } plannedReward &&
            CurrentRouteObtainSource(run) == step.ExpectedSource && run.Phase == PredictorPhase.Rewards)
        {
            var matching = run.Rewards.Where(r => r.Kind == plannedReward.ExpectedReward.Kind &&
                r.Origin == plannedReward.ExpectedReward.Origin && r.Key == plannedReward.ExpectedReward.Key).ToArray();
            if (plannedReward.Occurrence >= 0 && plannedReward.Occurrence < matching.Length &&
                SameRouteRewardOffer(run, matching[plannedReward.Occurrence], plannedReward, dependencies))
            {
                acceptedReward = matching[plannedReward.Occurrence]; acceptedChoice = plannedReward;
                bool requestedOperationKnown;
                if (!plannedReward.Reroll && plannedReward.SacrificeRelicId == null)
                {
                    // Resolve the actual target once before navigation. A bad
                    // Take/Skip must leave the prior picker untouched.
                    input = ResolveRouteObtainChildInput(run, step, dependencies, knownInstanceIds, local, trace);
                    inputResolved = true;
                    requestedOperationKnown = input != null;
                }
                else
                {
                    // Alternative availability belongs to its producer. This
                    // pure observation admits navigation; actual execution
                    // below retains the original validation and failure cause.
                    requestedOperationKnown = ObserveRouteRewardAlternatives(run, acceptedReward, dependencies,
                        RewardOfferReadCauses(ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(acceptedReward)]))
                        .Any(alternative => alternative.IsKnown && (plannedReward.Reroll ? alternative.Key == "REROLL" :
                            alternative.Key == "SACRIFICE" && alternative.RelicId == plannedReward.SacrificeRelicId));
                }
                // A reroll-only intent leaves its real picker open. Moving to
                // another validated opportunity is navigation, not an authored
                // Skip: leave the old offer open for the existing live-default
                // disposition at the end of this reward transaction.
                if (requestedOperationKnown && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
                    acceptedReward.Status == PredictorRewardStatus.Open &&
                    RouteRewardOperationKnown(run, ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(acceptedReward)], plannedReward) &&
                    run.Selection.CardRewardId is { } selectedId && selectedId != acceptedReward.Id &&
                    run.Rewards.FirstOrDefault(reward => reward.Id == selectedId) is { HasRerolled: true,
                        Status: PredictorRewardStatus.Open } selected && IsLiveDefaultCardSkip(run, selected) &&
                    run._preview.Rewards.Results.TryGetValue(selectedId, out var prior) &&
                    prior.NodeId == run.Working.Position.NodeId && prior.Source.Id == selectedId &&
                    prior.Choice is { Reroll: true, CardIndex: null, Skip: false, SacrificeRelicId: null, Children.IsEmpty: true })
                {
                    if (run.Submit(run.Request!, new ExitCardSelection(), token) != PredictorInputResult.Accepted)
                    {
                        local.Add(RouteCause("PlannedObtainInputPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
                        trace?.Complete(run, null, false, local.Skip(causeStart).ToImmutableArray());
                        return false;
                    }
                }
                var alternative = ApplyRouteRewardAlternative(run, acceptedReward, plannedReward, dependencies,
                    knownInstanceIds, local, token, out _, out var followup);
                if (alternative is { } applied)
                { trace?.Complete(run, null, applied, local.Skip(causeStart).ToImmutableArray()); return applied; }
                if (plannedReward.Reroll) step = step with { Reward = followup };
            }
        }
        if (!inputResolved) input = ResolveRouteObtainChildInput(run, step, dependencies, knownInstanceIds, local, trace);
        bool Complete(bool applied)
        { trace?.Complete(run, input, applied, local.Skip(causeStart).ToImmutableArray()); return applied; }
        if (input == null) return Complete(false);
        if (step.Action == PredictorObtainChildAction.Reward && step.Reward!.ExpectedReward.Kind == PredictorRewardKind.Card &&
            run.Selection.CardRewardId == null)
        {
            var reward = run.Rewards.Where(r => r.Kind == step.Reward.ExpectedReward.Kind && r.Origin == step.Reward.ExpectedReward.Origin &&
                r.Key == step.Reward.ExpectedReward.Key).ElementAt(step.Reward.Occurrence);
            if (run.Submit(run.Request!, new EnterCardSelection(reward.Id), token) != PredictorInputResult.Accepted)
            { local.Add(RouteCause("PlannedObtainRewardPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation)); return Complete(false); }
        }
        bool potionReceipt = input is TakeReward take && run.Rewards.Any(r => r.Id == take.RewardId && r.Kind == PredictorRewardKind.Potion);
        var result = run.Submit(run.Request!, input, token);
        if (result == PredictorInputResult.Accepted || potionReceipt &&
            result is (PredictorInputResult.TooFull or PredictorInputResult.NotAllowed))
        {
            if (result == PredictorInputResult.Accepted && acceptedReward != null && acceptedChoice != null)
                RecordRouteRewardChoice(run, acceptedReward, acceptedChoice, dependencies);
            return Complete(true);
        }
        local.Add(RouteCause("PlannedObtainInputPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        return Complete(false);
    }

    private static bool IsKnownRouteDeckTarget(PredictorRun run, PredictorPlannedCard target,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        PredictorDeckProjectionScope scope = PredictorDeckProjectionScope.All,
        PredictorDeckUncertainFact requiredFacts = PredictorDeckUncertainFact.All)
    {
        if (!run.Working.Deck.Contains(target.InstanceId) || run.Working.Card(target.InstanceId) is not { } card ||
            card.Key != target.Key || card.FloorAdded != target.FloorAdded ||
            !knownInstanceIds.Contains(card.Id) && !CausesFor(dependencies, PredictorCombatDomain.InstanceIds).IsEmpty)
            return false;
        if (run._preview.Partial != null)
            return RouteDeckKnowledge(run).ReadTarget(card.Id, scope, RouteTargetRequiredFacts(card, requiredFacts)) != null;
        return !dependencies.Any(d => d.MayChangeExistingCards && (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
            (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Id == card.Id)));
    }

    private static PredictorDeckUncertainFact RouteTargetRequiredFacts(PredictorCard card, PredictorDeckUncertainFact facts) =>
        // Authored model binding and the native removal hook's persistent map
        // coordinates remain real reads even when upgrades are irrelevant.
        facts | PredictorDeckUncertainFact.Definition |
        (card.Key.Entry == "SPOILS_MAP" ? PredictorDeckUncertainFact.Other : PredictorDeckUncertainFact.None);

    // Match the same card/identity predicates as the reader. A stale authored
    // target is a binding failure, not evidence that every deck cause blocked it.
    private static ImmutableArray<PredictorCombatPendingCause> RouteDeckTargetCauses(PredictorRun run,
        IEnumerable<PredictorPlannedCard> targets, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        PredictorDeckProjectionScope scope = PredictorDeckProjectionScope.All,
        PredictorDeckUncertainFact requiredFacts = PredictorDeckUncertainFact.All)
    {
        var matched = targets.Where(target => run.Working.Deck.Contains(target.InstanceId) &&
                run.Working.Card(target.InstanceId) is { } card && card.Key == target.Key && card.FloorAdded == target.FloorAdded)
            .ToArray();
        var identity = matched.Where(t => !knownInstanceIds.Contains(t.InstanceId))
            .SelectMany(_ => CausesFor(dependencies, PredictorCombatDomain.InstanceIds));
        if (run._preview.Partial != null)
        {
            var deck = RouteDeckKnowledge(run);
            var gaps = matched.SelectMany(t =>
            {
                var card = run.Working.Card(t.InstanceId);
                var targetGaps = deck.TargetGaps(t.InstanceId, scope, RouteTargetRequiredFacts(card, requiredFacts));
                var sources = run._preview.Partial.WitnessCardMembershipCauses(card);
                if (sources.IsEmpty) return targetGaps;
                // The numerical ID belongs to an earlier event alternative,
                // not to a missing base capture or a new implementation gap.
                return targetGaps.Where(g => g.Reason != "DeckTargetMembershipNotKnown").Concat(sources.Select(source =>
                    new PredictorDeckKnowledgeGap(source.NodeId!, source.ChoiceId, "ConditionalCardMembership",
                        PredictorDeckUncertainFact.Membership, [t.InstanceId], []))).ToImmutableArray();
            }).Distinct().ToImmutableArray();
            var projection = new PredictorDeckProjection(scope, null, [], gaps.IsEmpty, [], gaps);
            return PredictorRouteCauseEvidence.Normalize(identity.Concat(RouteDeckProjectionCauses(run, projection, dependencies)));
        }
        return PredictorRouteCauseEvidence.Normalize(identity.Concat(matched.SelectMany(target => dependencies.Where(d =>
            d.MayChangeExistingCards && (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
            (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Id == target.InstanceId)))
            .Select(d => d.Cause))));
    }

    private static ModelKey? CurrentRouteObtainSource(PredictorRun run) => run.Phase switch
    {
        PredictorPhase.TransformCards => new("RELIC", "NEW_LEAF"),
        PredictorPhase.ChooseBundle => new("RELIC", "SCROLL_BOXES"),
        PredictorPhase.ObtainChooseCard => run._obtainCardSource,
        PredictorPhase.ObtainChooseManyCards => new("RELIC", "SEA_GLASS"),
        PredictorPhase.ObtainDeckCards => run._obtainDeckSource,
        PredictorPhase.ObtainHealth => new("RELIC", run._leafyPending != null ? "LEAFY_POULTICE" :
            run._fragrantPending != null ? "FRAGRANT_MUSHROOM" : run._wafflePending != null ? "LEES_WAFFLE" :
            run._fakeWafflePending != null ? "FAKE_LEES_WAFFLE" : run._serePending != null ? "SERE_TALON" : "PRECARIOUS_SHEARS"),
        PredictorPhase.Rewards or PredictorPhase.ReplacePotion or PredictorPhase.RewardRemoveCards when !run._obtainFrames.IsEmpty => run._obtainFrames[^1].Relic,
        PredictorPhase.Rewards or PredictorPhase.ReplacePotion or PredictorPhase.RewardRemoveCards when run._eventKey != null => run._eventKey,
        _ => null
    };

    private static PredictorCombatDomain HealthObtainReadDomains(PredictorRun run, ModelKey source)
    {
        if (source.Entry == "LEAFY_POULTICE")
        {
            var reads = PredictorCombatDomain.Deck | PredictorCombatDomain.PotionInventory |
                PredictorCombatDomain.Relics | PredictorCombatDomain.TransformationsRng;
            run._preview.Partial?.Synchronize(run.Working);
            var deck = run._preview.Partial?.Deck ?? PredictorDeckKnowledge.Create(run.Context, run.Working);
            if (deck.Project(PredictorDeckProjectionScope.BasicStrike).KnownCount == 0 &&
                deck.Project(PredictorDeckProjectionScope.BasicDefend).KnownCount == 0)
                reads &= ~PredictorCombatDomain.TransformationsRng;
            return reads;
        }
        return source.Entry switch
        {
            "FRAGRANT_MUSHROOM" => PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics,
            "SERE_TALON" or "PRECARIOUS_SHEARS" => PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics,
            _ => PredictorCombatDomain.None
        };
    }

    private static PredictorCard DisplayObtainCard(PredictorCard card) => card with { Id = 0 };

    private static PredictorObtainChoiceKnowledge? ObserveRouteObtainChoice(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        if (CurrentRouteObtainSource(run) is not { } source) return null;
        var read = run.Phase switch
        {
            PredictorPhase.ObtainChooseCard or PredictorPhase.ObtainChooseManyCards or PredictorPhase.ChooseBundle => PredictorCombatDomain.RewardsRng,
            PredictorPhase.TransformCards => PredictorCombatDomain.NicheRng,
            PredictorPhase.ObtainDeckCards when run._obtainDeckOperation == PredictorObtainDeckOperation.Astrolabe => PredictorCombatDomain.NicheRng,
            PredictorPhase.ObtainHealth => HealthObtainReadDomains(run, source),
            _ => PredictorCombatDomain.None
        };
        var pending = CausesFor(dependencies, read);
        var cards = run.Selection.CandidateCardIds.Select(run.Working.Card).Select(c => new PredictorRouteCardKnowledge(c,
            IsKnownRouteDeckTarget(run, new(c.Id, c.Key, c.FloorAdded), dependencies, knownInstanceIds) ? [] :
                RouteDeckTargetCauses(run, [new(c.Id, c.Key, c.FloorAdded)], dependencies, knownInstanceIds))).ToImmutableArray();
        return new(run.Phase, source, cards,
            pending.IsEmpty ? (run.Phase == PredictorPhase.ObtainChooseCard ? run.ObtainCardChoices : run.ObtainManyCardChoices)
                .Select(DisplayObtainCard).ToImmutableArray() : [],
            pending.IsEmpty ? run.ScrollBoxBundles.Select(b => b.Select(DisplayObtainCard).ToImmutableArray()).ToImmutableArray() : [],
            run.Phase == PredictorPhase.Rewards ? ObserveRouteRewards(run, dependencies) : [], pending)
        { MinimumCards = run.Selection.MinimumCardCount,
            MaximumCards = run.Phase == PredictorPhase.ObtainChooseManyCards ? run.ObtainManyCardChoices.Length :
                run.Phase == PredictorPhase.ObtainChooseCard ? 1 : run.Selection.MaximumCardCount };
    }

    private static void ValidatePlannedObtainChild(PredictorPlannedObtainChild step)
    {
        RequirePlan(step != null && Enum.IsDefined(step.Action) && step.ExpectedSource.IsValid && step.ExpectedSource.Category is "RELIC" or "EVENT" &&
            !step.DeckCards.IsDefault && !step.ExpectedCards.IsDefault && !step.CardIndices.IsDefault && !step.ExpectedBundles.IsDefault);
        bool deckAction = step.Action is PredictorObtainChildAction.ChooseDeckCards or PredictorObtainChildAction.TransformCard or PredictorObtainChildAction.RemoveRewardCards;
        bool generatedAction = step.Action is PredictorObtainChildAction.ChooseCard or PredictorObtainChildAction.ChooseManyCards;
        RequirePlan(deckAction || step.DeckCards.IsEmpty);
        foreach (var card in step.DeckCards) ValidatePlannedCard(card);
        RequirePlan(step.DeckCards.Select(c => c.InstanceId).Distinct().Count() == step.DeckCards.Length);
        RequirePlan(step.Action is not (PredictorObtainChildAction.TransformCard or PredictorObtainChildAction.RemoveRewardCards) || step.DeckCards.Length == 1);
        RequirePlan(generatedAction || step.ExpectedCards.IsEmpty && step.CardIndices.IsEmpty);
        RequirePlan(!generatedAction || !step.ExpectedCards.IsEmpty && step.CardIndices.All(i => i >= 0 && i < step.ExpectedCards.Length) &&
            step.CardIndices.Distinct().Count() == step.CardIndices.Length &&
            (step.Action != PredictorObtainChildAction.ChooseCard || step.CardIndices.Length <= 1));
        foreach (var card in step.ExpectedCards) ValidateObtainOfferCard(card);
        RequirePlan(step.Action == PredictorObtainChildAction.ChooseBundle
            ? step.ExpectedBundles.Length == 2 && step.BundleIndex is >= 0 and < 2
            : step.ExpectedBundles.IsEmpty && step.BundleIndex == null);
        foreach (var bundle in step.ExpectedBundles)
        {
            RequirePlan(!bundle.IsDefaultOrEmpty);
            foreach (var card in bundle) ValidateObtainOfferCard(card);
        }
        RequirePlan(step.Action == PredictorObtainChildAction.Health
            ? step.Health != null && step.Health.CurrentHp > 0 && step.Health.MaxHp >= step.Health.CurrentHp : step.Health == null);
        RequirePlan(step.Action == PredictorObtainChildAction.ReplacePotion
            ? step.PotionSlot >= 0 && step.ExpectedPotion != null && step.ExpectedPotion.Id > 0 &&
                step.ExpectedPotion.Key.IsValid && step.ExpectedPotion.Key.Category == "POTION"
            : step.PotionSlot == null && step.ExpectedPotion == null);
        RequirePlan(step.Action == PredictorObtainChildAction.Reward ? step.Reward != null : step.Reward == null);
        if (step.Reward is { } reward)
        {
            // Reuse the established reward-snapshot contract, including gold
            // disposition and non-default card payload validation.
            ValidatePlannedNodes([new("obtain-child-validation") { RewardChoices = [reward] }]);
        }
    }

    private static void ValidateObtainOfferCard(PredictorCard card)
        => RequirePlan(card != null && card.Id >= 0 && card.Key.IsValid && card.Key.Category == "CARD" && Enum.IsDefined(card.Type) &&
            card.UpgradeLevel >= 0 && card.MaxUpgradeLevel >= card.UpgradeLevel && card.FloorAdded >= 0);
}
