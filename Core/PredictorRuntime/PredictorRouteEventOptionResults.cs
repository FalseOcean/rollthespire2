using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Detached route projection only. These never supply numerical HP facts or
    // belong to a saved run; BeginEvent resets this observation residue.
    // Default health completion is not a player input. Retain its exact place
    // and trusted before-state for detached event observation reconstruction.
    private sealed record RouteEventObtainHealthProjection(string Node, int InputIndex, ModelKey Source,
        PredictorObtainCompletion Pending, ImmutableArray<long> Parents, PredictorState Before, bool TransformLeafy);

    // A display boundary is taken from actual applied execution, before the
    // branch join supplies a private continuation witness. Combat has its own
    // settlement/result owner, so callers freeze this boundary before entering
    // its projection rather than diffing the eventual whole-event inventory.
    private sealed record RouteEventDisplayBoundary(PredictorState State,
        ImmutableArray<PredictorRouteCardKnowledge> Cards,
        ImmutableArray<PredictorCombatPendingCause> RelicPendingCauses,
        int? Gold, ImmutableArray<PredictorAcceptedInput> Inputs,
        ImmutableArray<PredictorCombatRewardKnowledge> Rewards);

    private static RouteEventDisplayBoundary CaptureRouteEventDisplayBoundary(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        var deck = RouteDeckKnowledge(run);
        var cards = ObserveRouteCards(run, dependencies, knownInstanceIds).Select(card =>
            // The display removes numerical instance IDs. A new card's known
            // fields remain useful even when an earlier optional receipt made
            // only the private allocation offset uncertain.
            run._preview.Partial != null && deck.ReadTarget(card.Value.Id, PredictorDeckProjectionScope.All) != null &&
                run._preview.Partial.WitnessCardCauses(card.Value).IsEmpty
                ? card with { PendingCauses = [] } : card).ToImmutableArray();
        return new(run.Working, cards,
            CausesFor(dependencies, PredictorCombatDomain.Relics),
            CausesFor(dependencies, PredictorCombatDomain.Gold).IsEmpty ? run.Working.GoldBudget : null, run._inputs,
            ObserveRouteRewards(run, dependencies));
    }

    private static PredictorEventOptionResultKnowledge ObserveAppliedRouteEventResult(
        RouteEventDisplayBoundary before, RouteEventDisplayBoundary after)
    {
        var inputs = after.Inputs.Skip(before.Inputs.Length).ToArray();
        var beforeIds = before.State.Deck.ToHashSet();
        var afterCards = after.Cards.ToDictionary(card => card.Value.Id);
        var changed = ImmutableArray.CreateBuilder<PredictorEventCardChangeKnowledge>();
        foreach (var known in before.Cards.Where(card => card.IsKnown))
        {
            var card = known.Value;
            var transformed = inputs.SelectMany(input => input.Transformations).FirstOrDefault(value => value.OriginalId == card.Id);
            if (transformed != null && afterCards.TryGetValue(transformed.ReplacementId, out var replacement) && replacement.IsKnown)
                changed.Add(new("Transform", DisplayObtainCard(card), DisplayObtainCard(replacement.Value)));
            else if (!after.State.Deck.Contains(card.Id))
                changed.Add(new("Remove", DisplayObtainCard(card), null));
            else if (afterCards.TryGetValue(card.Id, out var updated) && updated.IsKnown && updated.Value != card)
            {
                var value = updated.Value;
                string action = value.UpgradeLevel > card.UpgradeLevel ? "Upgrade" : value.UpgradeLevel < card.UpgradeLevel ? "Downgrade" :
                    value.Enchantment != card.Enchantment || value.EnchantmentAmount != card.EnchantmentAmount ? "Enchant" : "Change";
                changed.Add(new(action, DisplayObtainCard(card), DisplayObtainCard(value)));
            }
        }
        var acquired = after.Cards.Where(card => !beforeIds.Contains(card.Value.Id))
            .Select(card => card with { Value = DisplayObtainCard(card.Value) }).ToImmutableArray();
        var beforeRelics = before.State.Relics.Select(relic => relic.Id).ToHashSet();
        var afterRelics = after.State.Relics.Select(relic => relic.Id).ToHashSet();
        return new(PredictorEventNeededInput.None, [], [], [], after.Rewards, [])
        {
            GeneratedCards = acquired, ChangedCards = changed.ToImmutable(),
            ObtainedRelics = after.RelicPendingCauses.IsEmpty ? after.State.Relics.Where(relic => !beforeRelics.Contains(relic.Id))
                .Select(relic => relic with { Id = 0 }).ToImmutableArray() : [],
            LostRelics = before.RelicPendingCauses.IsEmpty && after.RelicPendingCauses.IsEmpty
                ? before.State.Relics.Where(relic => !afterRelics.Contains(relic.Id))
                .Select(relic => relic.Key).ToImmutableArray() : [],
            RelicPendingCauses = after.RelicPendingCauses,
            GoldBalance = after.Gold,
            GoldChange = before.Gold is { } oldGold && after.Gold is { } newGold ? newGold - oldGold : null,
            AllGoldLost = inputs.Any(input => input.Action == "EventGoldStolen"),
            LocalPendingCauses = acquired.SelectMany(card => card.PendingCauses).Concat(after.RelicPendingCauses).Distinct().ToImmutableArray(),
            Advanced = true
        };
    }

    private static (RouteEventDisplayBoundary Start, RouteEventDisplayBoundary? BeforeCombat)
        CaptureAppliedRouteEventDisplayStart(PredictorRun run, List<RouteDependency> dependencies,
            HashSet<long> knownInstanceIds, CancellationToken token)
    {
        if (!run._nodeReplayInputs.Any(input => input is ChooseEventOption))
            return (CaptureRouteEventDisplayBoundary(run, dependencies, knownInstanceIds), null);
        var replay = run.CreateEntryReplay(PreviewReplayPurpose.EventObservation);
        RouteEventDisplayBoundary? start = null, beforeCombat = null;
        // The same accepted-input replay already used for option observations
        // reconstructs an active draft's real pre-choice boundary. No option
        // preview, final private deck or hypothetical branch is a replay input.
        RestoreRouteEventObservationDraft(replay, run, dependencies, knownInstanceIds, token,
            (boundary, reads, input) =>
            {
                if (start == null && input is ChooseEventOption)
                    start = CaptureRouteEventDisplayBoundary(boundary, reads, knownInstanceIds);
            },
            (boundary, reads) =>
            {
                if (start != null && beforeCombat == null && boundary.Phase == PredictorPhase.CombatFacts)
                    beforeCombat = CaptureRouteEventDisplayBoundary(boundary, reads, knownInstanceIds);
            });
        return (start ?? throw new InvalidOperationException("PredictorEventAppliedDisplayEntryMissing"), beforeCombat);
    }

    private void RecordRouteEventObtainHealthProjection(ModelKey source, PredictorObtainCompletion pending, bool transformLeafy)
    {
        if (_eventKey == null) return;
        _preview.Event.ObtainHealthProjections.Add(new(Working.Position.NodeId, _nodeReplayInputs.Length, source,
            pending, _obtainFrames.Select(frame => frame.SourceId).ToImmutableArray(), Working, transformLeafy));
    }
    private static ImmutableArray<PredictorEventOptionKnowledge> ObserveRouteEventEntryOptions(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, CancellationToken token)
    {
        if (!run._nodeReplayInputs.Any(i => i is ChooseEventOption))
            return ObserveRouteEventOptionResults(run, ObserveRouteEventOptions(run, dependencies, knownInstanceIds),
                dependencies, knownInstanceIds, token);
        var entry = run.CreateEntryReplay(PreviewReplayPurpose.EventObservation);
        entry.RestoreRoutes(run._nodeEntryRoutes, run._preview.Settings.AcceptsPrefixes);
        entry.AdvanceRoute(token);
        foreach (var input in run._nodeReplayInputs.TakeWhile(i => i is not ChooseEventOption))
        {
            if (entry.Submit(entry.Request!, input, token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorEventEntryObservationReplayMismatch");
        }
        AttachEventTrace(entry, run, "entry");
        return ObserveRouteEventOptionResults(entry, ObserveRouteEventOptions(entry, dependencies, knownInstanceIds),
            dependencies, knownInstanceIds, token);
    }

    private static ImmutableArray<PredictorEventOptionKnowledge> ObserveRouteEventOptionResults(PredictorRun run,
        ImmutableArray<PredictorEventOptionKnowledge> options, List<RouteDependency> dependencies,
        HashSet<long> knownInstanceIds, CancellationToken token, int nestedDepth = 0)
    {
        if (run.Phase != PredictorPhase.EventChoice || run.EventNeedsHealthFact) return options;
        var outcomes = ImmutableArray.CreateBuilder<RouteEventOptionOutcome>();
        var observations = options.Select(option =>
        {
            token.ThrowIfCancellationRequested();
            if (option.Value == null || option.Enabled == false) return option;
            // Each branch starts at the same actual entry and accepted prefix.
            // No previous option's draws, inventory, HP or target survives here.
            var branch = run.CreateEntryReplay(PreviewReplayPurpose.EventObservation);
            RestoreRouteEventObservationDraft(branch, run, dependencies, knownInstanceIds, token);
            var branchDependencies = AttachEventOptionContext(branch, run, dependencies, "option." + option.Key);
            var local = new List<PredictorCombatPendingCause>();
            var cardsBefore = branch.Working.Deck.ToHashSet();
            int? goldBefore = CausesFor(branchDependencies, PredictorCombatDomain.Gold).IsEmpty ? branch.Working.GoldBudget : null;
            var cardValuesBefore = branch.Working.Deck.Select(branch.Working.Card).ToImmutableArray();
            var completeCardValuesBefore = cardValuesBefore.Where(card => branch._preview.Partial == null ||
                branch._preview.Partial.Deck.ReadTarget(card.Id, PredictorDeckProjectionScope.All) != null)
                .Select(card => card.Id).ToHashSet();
            var relicsBefore = branch.Working.Relics.Select(r => r.Id).ToHashSet();
            int inputsBefore = branch._inputs.Length;
            int healthCommandsBefore = branch._preview.Event.PendingHealthCommands.Length;
            bool generatesDirectRandomCard = !RouteEventDirectCardCandidates(branch, option.Key).IsEmpty;
            if (run._eventKey is { } rewardEvent)
                ConfigureCompletedRewardEdits(branch, new(run.Working.Position.NodeId)
                { Event = new(rewardEvent)
                    { Potions = run.RouteEventPotionInput(rewardEvent),
                      Occurrence = run._nodeReplayInputs.OfType<ChooseEventOccurrence>().LastOrDefault(),
                      Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = option.Value }] } },
                    PredictorRewardEditOwner.Event, 1);
            ApplyRouteEventAction(branch, new(PredictorEventActionKind.Option) { ExpectedOption = option.Value },
                branchDependencies, knownInstanceIds, local, token);
            ProjectRouteEventHealthIndependentResult(branch, branchDependencies, knownInstanceIds, local, token);
            ContinueDrawingEventConfirmation(branch, branchDependencies, knownInstanceIds, local, token);
            ApplyRouteEventDefaultCardTarget(branch, branchDependencies, knownInstanceIds, local, token);
            PropagateRouteEventHealthPending(branch, branchDependencies);
            ApplyRouteEventDefaultRewardPickup(branch, branchDependencies, local, token);
            ContinueDrawingEventConfirmation(branch, branchDependencies, knownInstanceIds, local, token);
            ApplyRouteEventDefaultRewardPickup(branch, branchDependencies, local, token);
            var combat = branch.Phase == PredictorPhase.CombatFacts
                ? ObserveRouteEventCombat(branch, null, branchDependencies) : null;
            if (branch.Phase != PredictorPhase.Complete && local.Count == 0)
                local.Add(RouteCause("Event" + RouteEventNeededInput(branch) + "Choice", branch.Working.Position.NodeId,
                    PredictorCombatDomain.Continuation, RouteEventCombatContinuationCauses(branch, combat)));
            var child = ObserveRouteObtainChoice(branch, branchDependencies, knownInstanceIds);
            var offeredPending = RouteEventOfferedCardCauses(branch, branchDependencies);
            var candidates = branch.EventOfferedCards.IsEmpty ? branch.EventCardCandidates.Select(branch.Working.Card)
                .Select(c => new PredictorRouteCardKnowledge(c, IsKnownRouteDeckTarget(branch, new(c.Id, c.Key, c.FloorAdded),
                    branchDependencies, knownInstanceIds) ? [] : RouteDeckTargetCauses(branch, [new(c.Id, c.Key, c.FloorAdded)], branchDependencies, knownInstanceIds)))
                .ToImmutableArray() : [];
            // These additions were produced after the option's actual reads
            // passed. Their model/level is known even when a prior unresolved
            // allocation makes the private numerical instance ID uncertain.
            var generated = branch.Working.Deck.Where(id => !cardsBefore.Contains(id))
                .Select(id => new PredictorRouteCardKnowledge(DisplayObtainCard(branch.Working.Card(id)),
                    generatesDirectRandomCard ? CausesFor(branchDependencies, PredictorCombatDomain.RewardsRng) : []))
                .ToImmutableArray();
            var relicPending = CausesFor(branchDependencies, PredictorCombatDomain.Relics);
            // Only finite source progressions. Repeatable Baths/Bridge and
            // inventory/Conveyor loops keep their own local input boundary.
            bool finiteNested = branch._eventKey?.Entry is "TINKER_TIME" or "TRIAL" or "DOLL_ROOM" or
                "ROUND_TEA_PARTY" or "COLOSSAL_FLOWER" or "DENSE_VEGETATION" or "PUNCH_OFF" or "THE_LANTERN_KEY";
            var nestedOptions = finiteNested && nestedDepth < 4 && branch.Phase == PredictorPhase.EventChoice && !branch.EventNeedsHealthFact
                ? ObserveRouteEventOptionResults(branch, ObserveRouteEventOptions(branch, branchDependencies, knownInstanceIds),
                    branchDependencies, knownInstanceIds, token, nestedDepth + 1) : ImmutableArray<PredictorEventOptionKnowledge>.Empty;
            var transformations = branch._inputs.Skip(inputsBefore).SelectMany(i => i.Transformations).ToArray();
            var changed = ImmutableArray.CreateBuilder<PredictorEventCardChangeKnowledge>();
            foreach (var before in cardValuesBefore)
            {
                // A remove/transform may need only known identity and eligibility.
                // Do not turn that proof into a full before-card display carrying
                // a private upgrade/enchantment witness from an earlier choice.
                if (!completeCardValuesBefore.Contains(before.Id)) continue;
                var transformed = transformations.FirstOrDefault(t => t.OriginalId == before.Id);
                if (transformed != null)
                {
                    changed.Add(new("Transform", DisplayObtainCard(before), DisplayObtainCard(branch.Working.Card(transformed.ReplacementId))));
                    continue;
                }
                if (!branch.Working.Deck.Contains(before.Id)) { changed.Add(new("Remove", DisplayObtainCard(before), null)); continue; }
                var after = branch.Working.Card(before.Id);
                if (after == before) continue;
                string action = after.UpgradeLevel > before.UpgradeLevel ? "Upgrade" : after.UpgradeLevel < before.UpgradeLevel ? "Downgrade" :
                    after.Enchantment != before.Enchantment || after.EnchantmentAmount != before.EnchantmentAmount ? "Enchant" : "Change";
                changed.Add(new(action, DisplayObtainCard(before), DisplayObtainCard(after)));
            }
            var result = new PredictorEventOptionResultKnowledge(RouteEventNeededInput(branch),
                offeredPending.IsEmpty ? branch.EventOfferedCards.Select(DisplayObtainCard).ToImmutableArray() : [],
                candidates, child == null ? [] : [child], combat?.Rewards ?? ObserveRouteRewards(branch, branchDependencies),
                branchDependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray())
            {
                Options = nestedOptions, ChangedCards = changed.ToImmutable(),
                LocalPendingCauses = PredictorRouteCauseEvidence.Normalize(local
                    .Concat(offeredPending)
                    .Concat(candidates.SelectMany(c => c.PendingCauses))
                    .Concat(generated.SelectMany(c => c.PendingCauses))
                    .Concat(branchDependencies.Where(d => !dependencies.Contains(d)).Select(d => d.Cause))),
                HealthRequired = branch.EventNeedsHealthFact,
                Combat = combat, CombatEncounter = combat == null ? null : branch._eventCombatEntry?.Encounter.Key,
                CardsRequired = branch.EventCardsRequired, GeneratedCards = generated,
                ObtainedRelics = relicPending.IsEmpty ? branch.Working.Relics.Where(r => !relicsBefore.Contains(r.Id))
                    .Select(r => r with { Id = 0 }).ToImmutableArray() : [],
                RelicPendingCauses = relicPending,
                HealthConsequences = branch._inputs.Skip(inputsBefore).SelectMany(i => i.EventHealthConsequences).ToImmutableArray(),
                PendingHealthCommands = branch._preview.Event.PendingHealthCommands.Skip(healthCommandsBefore).ToImmutableArray(),
                GoldBalance = CausesFor(branchDependencies, PredictorCombatDomain.Gold).IsEmpty ? branch.Working.GoldBudget : null,
                GoldChange = goldBefore is { } knownGold && CausesFor(branchDependencies, PredictorCombatDomain.Gold).IsEmpty
                    ? branch.Working.GoldBudget - knownGold : null,
                AllGoldLost = branch._inputs.Skip(inputsBefore).Any(input => input.Action == "EventGoldStolen"),
                Advanced = branch.Phase == PredictorPhase.Complete && local.Count == 0
            };
            branch._preview.EventTrace?.Stage(branch, branchDependencies);
            // Publish the actual pending picker above; abstract only its
            // audited leaf effects for independent downstream continuation.
            if (branch.Phase != PredictorPhase.Complete)
            {
                if (!ContinueDrawingLowValueEvent(branch, branchDependencies, local) &&
                    (nestedOptions.IsEmpty || !ContinuePendingEventOptions(branch, nestedOptions, branchDependencies, local)))
                    ContinuePendingRouteEvent(branch, branchDependencies, local, token);
            }
            outcomes.Add(new(option.Key, branch.Working, branch.Phase, local.ToImmutableArray(),
                branchDependencies.ToImmutableArray(), branch._preview.Event.PendingHealthCommands,
                branch._preview.Event.UnknownHealingAmount, branch._preview.Event.PendingCardEffect, branch._preview.Event.AssignedGold));
            return option with { ResultPreview = result with { TraceIdentity = branch._preview.EventTrace?.Identity } };
        }).ToImmutableArray();
        run._preview.Event.OptionOutcomes = outcomes.ToImmutable();
        return observations;
    }

    private static ImmutableArray<PredictorCombatPendingCause> RouteEventOfferedCardCauses(PredictorRun run,
        List<RouteDependency> dependencies)
    {
        if (run.EventOfferedCards.IsEmpty) return [];
        // Both grids use noncombat odds. Eggs and late card modifiers still
        // apply to these generated cards; Wing Charm reads actual Niche RNG.
        var domains = PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Relics;
        if (PredictorSettlementEffects.Has(run.Working, "WING_CHARM")) domains |= PredictorCombatDomain.NicheRng;
        return CausesFor(dependencies, domains);
    }

    private static ImmutableArray<PredictorEventHealthDelta> RouteEventHealthConsequences(PredictorRun run)
        => run._inputs.SelectMany(i => i.EventHealthConsequences).ToImmutableArray();

    private static void RestoreRouteEventObservationDraft(PredictorRun branch, PredictorRun source,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, CancellationToken token,
        Action<PredictorRun, List<RouteDependency>, PredictorInput>? beforeAcceptedInput = null,
        Action<PredictorRun, List<RouteDependency>>? observeBoundary = null)
    {
        branch.RestoreRoutes(source._nodeEntryRoutes, source._preview.Settings.AcceptsPrefixes);
        branch.AdvanceRoute(token);
        var local = new List<PredictorCombatPendingCause>();
        var copiedDependencies = new List<RouteDependency>(dependencies);
        void ReplayProjectedSuccessors(int inputIndex)
        {
            var health = source._preview.Event.ObtainHealthProjections.Where(value =>
                value.Node == source.Working.Position.NodeId && value.InputIndex == inputIndex).ToArray();
            int nextHealth = 0;
            void ReplayHealth()
            {
                while (nextHealth < health.Length && branch.Phase == PredictorPhase.ObtainHealth)
                {
                    var projection = health[nextHealth++];
                    var pending = branch._leafyPending ?? branch._wafflePending ?? branch._fakeWafflePending ??
                        branch._fragrantPending ?? branch._serePending ?? branch._precariousPending;
                    if (CurrentRouteObtainSource(branch) != projection.Source || pending != projection.Pending ||
                        !branch._obtainFrames.Select(frame => frame.SourceId).SequenceEqual(projection.Parents) ||
                        JsonSerializer.Serialize(branch.Working) != JsonSerializer.Serialize(projection.Before))
                        throw new InvalidOperationException("PredictorEventObtainHealthProjectionReplayMismatch;event=" + source._eventKey +
                            ";node=" + projection.Node + ";input=" + inputIndex + ";source=" + projection.Source +
                            ";actualSource=" + CurrentRouteObtainSource(branch) + ";phase=" + branch.Phase);
                    // Re-execute the independent successor from this branch's
                    // actual before-state and the original passed decision.
                    // The source's final partial Deck is never a replay input.
                    FinalizeProjectedObtainHealth(branch, projection.Pending, projection.Source, projection.TransformLeafy, token);
                    branch._preview.Event.ObtainHealthProjections.Add(projection);
                }
            }
            ReplayHealth();
            foreach (var projection in source._preview.Event.CombatProjections.Where(value => value.InputIndex == inputIndex))
            {
                ContinueDrawingEventConfirmation(branch, copiedDependencies, knownInstanceIds, local, token);
                observeBoundary?.Invoke(branch, copiedDependencies);
                if (!ApplyRouteEventAction(branch, projection.Action, copiedDependencies, knownInstanceIds, local, token))
                    throw new InvalidOperationException("PredictorEventCombatProjectionReplayMismatch");
                ReplayHealth();
            }
            if (nextHealth != health.Length)
                throw new InvalidOperationException("PredictorEventObtainHealthProjectionReplayPositionMismatch;event=" + source._eventKey +
                    ";input=" + inputIndex + ";phase=" + branch.Phase);
        }
        for (int inputIndex = 0; inputIndex < source._nodeReplayInputs.Length; inputIndex++)
        {
            ReplayProjectedSuccessors(inputIndex);
            var input = source._nodeReplayInputs[inputIndex];
            if (input is ChooseEventOccurrence && source._preview.Event.BridgeEntrySummarized)
                branch._preview.Event.BridgeEntryPending = true;
            if (input is not ProvideEventHealthFact)
                ProjectRouteEventHealthIndependentResult(branch, copiedDependencies, knownInstanceIds, local, token);
            beforeAcceptedInput?.Invoke(branch, copiedDependencies, input);
            if (branch.Submit(branch.Request!, input, token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorEventObservationReplayMismatch");
            observeBoundary?.Invoke(branch, copiedDependencies);
        }
        ReplayProjectedSuccessors(source._nodeReplayInputs.Length);
        // The current stage may have deterministic successors beyond a pure
        // cost. Replay that same route-only projection before inspecting it.
        if (!source.EventNeedsHealthFact)
            ProjectRouteEventHealthIndependentResult(branch, copiedDependencies, knownInstanceIds, local, token);
        ContinueDrawingEventConfirmation(branch, copiedDependencies, knownInstanceIds, local, token);
        observeBoundary?.Invoke(branch, copiedDependencies);
        if (branch.Phase != source.Phase || branch.EventNeedsHealthFact != source.EventNeedsHealthFact)
            throw new InvalidOperationException("PredictorEventObservationReplayBoundaryMismatch;event=" + source._eventKey +
                ";source=" + source.Phase + ";replay=" + branch.Phase +
                ";sourceNeedsHealth=" + source.EventNeedsHealthFact + ";replayNeedsHealth=" + branch.EventNeedsHealthFact +
                ";inputs=" + string.Join(',', source._nodeReplayInputs.Select(input => input.GetType().Name)) +
                ";combatProjectionPositions=" + string.Join(',', source._preview.Event.CombatProjections.Select(value => value.InputIndex)) +
                ";replayPending=" + string.Join(',', local.Select(cause => cause.Code)));
        branch.RestoreRoutes(source.Routes, source._preview.Settings.AcceptsPrefixes);
    }

    private static bool ProjectRouteEventHealthIndependentResult(PredictorRun run, List<RouteDependency> dependencies,
        HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (ProjectDrawingTabletDecipher(run, dependencies)) return true;
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._pendingHealthOption is not { } branch ||
            run._eventKey is not { } key || !RouteEventHealthIndependentBranch(key.Entry, branch)) return false;
        var option = run._pendingHealthOptions.Single(o => o.Key == branch);
        run._preview.Event.ProjectHealthIndependently = true;
        var reads = RouteEventActionReadDomains(run, option, dependencies, knownInstanceIds);
        var readCauses = RouteEventActionReadCauses(run, option, dependencies, knownInstanceIds);
        if (!readCauses.IsEmpty)
        {
            run._preview.Event.ProjectHealthIndependently = false;
            // The remainder belongs to the real non-health reader. Requiring
            // HP would not make its missing deck/inventory facts available.
            run.EventOptions = run._pendingHealthOptions;
            run._pendingHealthOption = null;
            run._pendingHealthOptions = [];
            if (TryContinueRouteEventDeckAction(run, option, readCauses, dependencies, token)) return true;
            local.Add(RouteCause("EventNonHealthActionDependent", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                readCauses, PredictorRouteCauseEvidence.Key("event-branch", key.Serialized, branch)));
            return false;
        }
        run.EventOptions = run._pendingHealthOptions;
        run._pendingHealthOption = null;
        run._pendingHealthOptions = [];
        // Reuse the audited source successor. No fabricated HP fact, target or
        // second replay input is inserted; health commands stay observations.
        var trace = run.EventTrace();
        var input = new ChooseEventOption(branch);
        var traceReads = trace?.Action(run, input, reads, dependencies) ?? [];
        if (run.TrySubmitEvent(input, token) != PredictorInputResult.Accepted)
            throw new InvalidOperationException("PredictorEventHealthIndependentProjectionRejected");
        PropagateRouteEventHealthPending(run, dependencies);
        trace?.Stage(run, dependencies, traceReads);
        return true;
    }

    private static bool RouteEventHealthIndependentBranch(string key, string branch) => (key, branch) is
        ("MORPHIC_GROVE", "LONER") or ("WHISPERING_HOLLOW", "HUG") or ("TRASH_HEAP", "DIVE_IN") or
        ("TRIAL", "NOBLE_GUILTY") or ("THIS_OR_THAT", "PLAIN") or ("UNREST_SITE", _) or ("SAPPHIRE_SEED", "EAT") or
        ("SLIPPERY_BRIDGE", "HOLD_ON") or ("STONE_OF_ALL_TIME", _) or ("SUNKEN_STATUE", "DIVE_INTO_WATER") or
        ("ROUND_TEA_PARTY", "ENJOY_TEA") or ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") or ("ABYSSAL_BATHS", _) or
        ("DROWNING_BEACON", "CLIMB") or ("TABLET_OF_TRUTH", "SMASH") or
        ("COLOSSAL_FLOWER", "REACH_DEEPER") or ("COLOSSAL_FLOWER", "POLLINOUS_CORE") or ("DENSE_VEGETATION", _) or
        ("SPIRALING_WHIRLPOOL", "DRINK") or ("ROOM_FULL_OF_CHEESE", "SEARCH") or
        ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK") or ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT") or
        ("BRAIN_LEECH", "RIP") or ("BYRDONIS_NEST", "EAT") or ("DOLL_ROOM", "TAKE_SOME_TIME") or ("DOLL_ROOM", "EXAMINE") or
        ("SPIRIT_GRAFTER", _) or ("JUNGLE_MAZE_ADVENTURE", "SOLO_QUEST");

    private static void PropagateRouteEventHealthPending(PredictorRun run, List<RouteDependency> dependencies)
    {
        if (run._preview.Event.PendingHealthCommands.IsEmpty && !run._preview.Event.UnknownHealingAmount &&
            !(run._preview.Event.ProjectHealthIndependently && run.Phase == PredictorPhase.EventCards)) return;
        var domains = PredictorCombatDomain.Health;
        bool mayLoseHp = run._preview.Event.PendingHealthCommands.Any(c => c.HpDelta < 0 || c.MaxHpDelta < 0) ||
            run._preview.Event.ProjectHealthIndependently && run.Phase == PredictorPhase.EventCards &&
                run._eventKey?.Entry is ("WHISPERING_HOLLOW" or "SPIRIT_GRAFTER");
        if (mayLoseHp && (run.Working.PotionSlots.Any(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE") ||
            !CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty))
            domains |= PredictorCombatDomain.PotionInventory;
        if (mayLoseHp && (run.Working.Relics.Any(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed) ||
            !CausesFor(dependencies, PredictorCombatDomain.Relics).IsEmpty))
            domains |= PredictorCombatDomain.Relics;
        AddDependency(dependencies, new(RouteCause("EventHealthOutcomePending", run.Working.Position.NodeId, domains,
            mayLoseHp ? CausesFor(dependencies, PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics) : [],
            PredictorRouteCauseEvidence.Key("event-health", run._eventKey?.Serialized ?? "", run._inputs.Length.ToString()))));
    }
}
