using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static PredictorEventNodeKnowledge ProjectRouteEvent(PredictorRun run, PredictorPlannedEvent? plan,
        ImmutableArray<PredictorEventOccurrenceExclusion> exclusions,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (plan != null) ValidatePlannedEvent(plan);
        var authoredPlan = plan;
        var eventTrace = run.EventTrace();
        var occurrence = ObserveRouteEventOccurrence(run, dependencies, exclusions);
        var eventKey = run._eventKey ?? run.Working.Visits.LastOrDefault()?.Model ?? plan?.ExpectedEvent;
        if (run._eventKey is { } activePotionEvent) run.RequireLocalEventPotionRarities(activePotionEvent, local);
        var occurrencePending = RouteEventOccurrenceCauses(run, dependencies);
        int applied = 0;
        bool selectionBlocked = false;
        bool automaticOccurrenceChanged = false;
        PredictorEventOccurrenceCandidate? selectedOccurrence = null;
        if (occurrence != null)
        {
            var choice = plan?.Occurrence;
            run._preview.OccurrenceOrigin = PredictorKnowledgeOrigin.PlayerInput;
            if (choice == null && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
                occurrencePending.IsEmpty && occurrence.Candidates.FirstOrDefault(c => !c.ExcludedByPlayer) is { } first)
            {
                choice = new(first.Event, first.QueueEvent, first.RawOrdinal, occurrence.Cursor, first.Cursor);
                run._preview.OccurrenceOrigin = PredictorKnowledgeOrigin.ApprovedDefault;
            }
            if (choice == null)
                local.Add(RouteCause("EventOccurrenceChoice", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            else if (!occurrencePending.IsEmpty)
                local.Add(RouteCause("EventOccurrenceIdentityPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation, occurrencePending));
            else if ((plan?.Occurrence != null && choice.Event != plan.ExpectedEvent) || !occurrence.Candidates.Any(c => c.Event == choice.Event &&
                c.QueueEvent == choice.QueueEvent && c.RawOrdinal == choice.RawOrdinal && c.Cursor == choice.SelectedCursor) ||
                choice.ExpectedCursor != occurrence.Cursor)
                local.Add(RouteCause("PlannedEventOccurrenceChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            else if (run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
                occurrence.Candidates.Single(candidate => candidate.Event == choice.Event &&
                    candidate.QueueEvent == choice.QueueEvent && candidate.RawOrdinal == choice.RawOrdinal &&
                    candidate.Cursor == choice.SelectedCursor) is { IsCycleFallback: false } selected &&
                selected.EligibilityPendingCauses.Where(cause => (cause.AffectedDomains & PredictorCombatDomain.Gold) != 0)
                    .ToImmutableArray() is { IsEmpty: false } goldAppearance)
                local.Add(PredictorRouteCauseEvidence.CreateExplicit("EventGoldAppearancePending", run.Working.Position.NodeId,
                    PredictorCombatDomain.Continuation, PredictorPendingReasonKind.DerivedDependency,
                    "This queued event's actual gold admission predicate is unresolved; choosing one prior gold outcome cannot prove its identity.",
                    ["event.appearance." + selected.QueueEvent.Entry + ".allowed", "gold.balance"], goldAppearance,
                    occurrenceKey: PredictorRouteCauseEvidence.Key("event-appearance-gold", selected.QueueEvent.Serialized, selected.Cursor.ToString())));
            else if (!CanEnterPlannedRouteEvent(run, choice.Event, dependencies, local)) { }
            else if (run.Submit(run.Request!, choice, token) != PredictorInputResult.Accepted)
                local.Add(RouteCause("PlannedEventOccurrencePending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            else selectedOccurrence = occurrence.Candidates.Single(c => c.Event == choice.Event && c.QueueEvent == choice.QueueEvent &&
                c.RawOrdinal == choice.RawOrdinal && c.Cursor == choice.SelectedCursor);
            eventKey = run._eventKey ?? eventKey;
            automaticOccurrenceChanged = plan is { Occurrence: null, ExpectedOccurrence: { } receipt } && choice != receipt;
            selectionBlocked = run.Phase == PredictorPhase.EventEligibility;
        }
        // An automatic receipt binds actions without selecting an occurrence.
        // A changed queue position is a different instance even with the same
        // event model. Keep authored intent visible and recompute this event.
        if (occurrence == null && plan is { Occurrence: null, ExpectedOccurrence: { } acceptedReceipt })
            automaticOccurrenceChanged = run._nodeReplayInputs.OfType<ChooseEventOccurrence>().LastOrDefault() != acceptedReceipt;
        bool authoredEventChanged = plan != null && (eventKey != plan.ExpectedEvent || automaticOccurrenceChanged);
        bool automaticEventChanged = authoredEventChanged && plan!.Occurrence == null &&
            run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults;
        // Automatic occurrence is a recomputed prediction, whereas all event
        // actions and potion hypotheses remain owned by their authored identity.
        // Observe the new event normally without applying the old event's plan.
        if (authoredEventChanged)
        {
            if (!automaticEventChanged)
                local.Add(RouteCause("PlannedEventChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            plan = null;
        }

        var entryOptions = run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults && !selectionBlocked
            ? ObserveRouteEventEntryOptions(run, dependencies, knownInstanceIds, token) : [];
        var (appliedDisplayStart, acceptedCombatBoundary) = CaptureAppliedRouteEventDisplayStart(run, dependencies, knownInstanceIds, token);
        RouteEventDisplayBoundary? appliedDisplayEnd = acceptedCombatBoundary;
        void CaptureBeforeCombat()
        {
            if (run.Phase == PredictorPhase.CombatFacts)
                appliedDisplayEnd ??= CaptureRouteEventDisplayBoundary(run, dependencies, knownInstanceIds);
        }
        CaptureBeforeCombat();
        if (local.Count == 0 && plan != null && !selectionBlocked)
        {
            applied = ActiveRouteEventPlanPrefix(run, plan, dependencies, knownInstanceIds, local, token);
            for (; applied < plan.Actions.Length && local.Count == 0; applied++)
            {
                ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Event = plan },
                    PredictorRewardEditOwner.Event, applied);
                if (run.Phase == PredictorPhase.Complete) break;
                // Preserve an authored actual HP input. Otherwise pure costs
                // and healing amounts do not block their known successors.
                if (plan.Actions[applied].Action != PredictorEventActionKind.Health)
                    ProjectRouteEventHealthIndependentResult(run, dependencies, knownInstanceIds, local, token);
                if (plan.Actions[applied].Action != PredictorEventActionKind.Option)
                    ContinueDrawingEventConfirmation(run, dependencies, knownInstanceIds, local, token);
                CaptureBeforeCombat();
                if (plan.Actions[applied].Action is PredictorEventActionKind.Reward or PredictorEventActionKind.Obtain or PredictorEventActionKind.LeaveRewards)
                    ContinueDrawingEventCombat(run, dependencies, local, token);
                if (local.Count != 0 || run.Phase == PredictorPhase.Complete) break;
                if (!ApplyRouteEventAction(run, plan.Actions[applied], dependencies, knownInstanceIds, local, token)) break;
                PropagateRouteEventHealthPending(run, dependencies);
            }
        }
        ProjectRouteEventHealthIndependentResult(run, dependencies, knownInstanceIds, local, token);
        // An authored next action always wins over the drawing's confirmation
        // default, including a saved input that is currently unbound.
        if (plan == null || applied >= plan.Actions.Length)
        {
            ContinueDrawingEventConfirmation(run, dependencies, knownInstanceIds, local, token);
            CaptureBeforeCombat();
            ContinueDrawingEventCombat(run, dependencies, local, token);
        }
        ApplyRouteEventDefaultCardTarget(run, dependencies, knownInstanceIds, local, token);
        if (eventKey is { } rewardEvent)
            ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId)
                { Event = plan ?? new(rewardEvent)
                    { Occurrence = run._nodeReplayInputs.OfType<ChooseEventOccurrence>().LastOrDefault() } },
                PredictorRewardEditOwner.Event, applied);
        ApplyRouteEventDefaultRewardPickup(run, dependencies, local, token);
        if (plan == null || applied >= plan.Actions.Length)
        {
            ContinueDrawingEventConfirmation(run, dependencies, knownInstanceIds, local, token);
            CaptureBeforeCombat();
            ApplyRouteEventDefaultRewardPickup(run, dependencies, local, token);
        }
        bool advanced = run.Phase == PredictorPhase.Complete && local.Count == 0 && !selectionBlocked;
        var needed = RouteEventNeededInput(run);
        PredictorCombatPreview? combat = null;
        if (needed == PredictorEventNeededInput.Combat)
        {
            var action = plan?.Actions.ElementAtOrDefault(applied);
            combat = ObserveRouteEventCombat(run, action?.Action == PredictorEventActionKind.Combat ? action : null, dependencies);
        }
        if (!advanced && local.Count == 0)
            local.Add(RouteCause("Event" + needed + "Choice", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                needed == PredictorEventNeededInput.Combat ? RouteEventCombatContinuationCauses(run, combat) : [],
                PredictorRouteCauseEvidence.Key("event-stage", eventKey?.Serialized ?? "", run._eventBranch ?? "", applied.ToString(), needed.ToString())));
        var child = advanced ? null : ObserveRouteObtainChoice(run, dependencies, knownInstanceIds);
        var offeredPending = RouteEventOfferedCardCauses(run, dependencies);
        var offeredKnowledge = ObserveRouteEventOfferedCards(run, dependencies);
        var candidates = run.EventOfferedCards.IsEmpty
            ? ObserveRouteEventTargetCards(run, dependencies, knownInstanceIds) : [];
        var options = ObserveRouteEventOptions(run, dependencies, knownInstanceIds);
        if (run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
            options = ObserveRouteEventOptionResults(run, options, dependencies, knownInstanceIds, token);
        var result = new PredictorEventNodeKnowledge(eventKey, options, candidates,
            offeredKnowledge?.OfferBinding?.Value.Cards ?? [],
            child == null ? [] : [child], ObserveRouteRewards(run, dependencies),
            dependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray())
        {
            OfferedCardOptions = offeredKnowledge?.CardOptions ?? [],
            OfferedCardBinding = offeredKnowledge?.OfferBinding,
            PotionInputRequirement = EventPotionRequirement(eventKey ?? occurrence?.Candidates.FirstOrDefault()?.Event),
            PotionInput = eventKey is { } potionEvent ? run.RouteEventPotionInput(potionEvent) : null,
            ConveyorInput = run._conveyorInput, ConveyorLostHp = run._conveyorLostHp,
            EntryOptions = entryOptions,
            // CompleteNode retires the editable replay draft. The accepted
            // node receipts still prove which option actually ran, including
            // a direct result completed by the default reward pickup above.
            AppliedResult = run._inputs.Any(input => input.Action.StartsWith("EventOption:", StringComparison.Ordinal))
                ? ObserveAppliedRouteEventResult(appliedDisplayStart,
                    appliedDisplayEnd ?? CaptureRouteEventDisplayBoundary(run, dependencies, knownInstanceIds)) : null,
            Occurrence = occurrence, SelectedOccurrence = selectedOccurrence,
            AuthoredEvent = authoredPlan?.ExpectedEvent,
            AuthoredActionsNotApplied = authoredEventChanged &&
                (authoredPlan!.Actions.Length > 0 || authoredPlan.Potions != null),
            OccurrencePendingCauses = occurrencePending, OfferedCardPendingCauses = offeredPending,
            NeededInput = advanced ? PredictorEventNeededInput.None : needed, HealthRequired = run.EventNeedsHealthFact,
            BusinessChoiceRequired = !advanced && needed == PredictorEventNeededInput.Option &&
                DrawingEventBusinessChoiceRequired(run, options),
            CardsRequired = run.EventCardsRequired, Combat = combat,
            CombatEncounter = combat == null ? null : run._eventCombatEntry?.Encounter.Key, Crystal = run.CrystalView,
            GoldBalance = CausesFor(dependencies, PredictorCombatDomain.Gold).IsEmpty ? run.Working.GoldBudget : null,
            GoldPendingCauses = CausesFor(dependencies, PredictorCombatDomain.Gold),
            HealthConsequences = RouteEventHealthConsequences(run),
            PendingHealthCommands = run._preview.Event.PendingHealthCommands,
            Advanced = advanced, AppliedActions = applied
        };
        // Freeze the authored boundary before private witness completion of an
        // unresolved picker/reward leaf. That completion is not a chosen input.
        eventTrace?.Stage(run, dependencies);
        string? traceIdentity = eventTrace?.Identity;
        if (!advanced && ContinueDefaultFakeMerchant(run, plan, local))
        {
            eventTrace?.Stage(run, dependencies);
            result = result with { Advanced = true, NeededInput = PredictorEventNeededInput.None };
        }
        else if (!advanced && (run._preview.Event.BridgeEntrySummarized && ContinueDrawingLowValueEvent(run, dependencies, local) ||
            ContinuePendingEventOptions(run, options, dependencies, local) ||
            ContinueDrawingLowValueEvent(run, dependencies, local) ||
            ContinuePendingRouteEvent(run, dependencies, local, token)))
            result = result with { Advanced = true,
                BusinessChoiceRequired = false,
                GoldBalance = run._preview.Partial?.EventGoldBalance(run.Working)?.KnownValue is { } gold
                    ? checked((int)gold) : CausesFor(dependencies, PredictorCombatDomain.Gold).IsEmpty ? run.Working.GoldBudget : null,
                GoldPendingCauses = CausesFor(dependencies, PredictorCombatDomain.Gold),
                PendingHealthCommands = run._preview.Event.PendingHealthCommands,
                PendingCauses = dependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray() };
        // A retained plan mismatch is visible, but does not block the current
        // event's ordinary partial continuation or unrelated later facts.
        if (automaticEventChanged)
            local.Add(RouteCause(automaticOccurrenceChanged ? "PlannedEventOccurrenceChanged" : "PlannedEventChanged",
                run.Working.Position.NodeId, PredictorCombatDomain.None));
        return result with { TraceIdentity = traceIdentity,
            PendingCauses = dependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray() };
    }

    private static PredictorEventNeededInput RouteEventNeededInput(PredictorRun run) => run.Phase switch
    {
        PredictorPhase.Complete => PredictorEventNeededInput.None,
        PredictorPhase.EventEligibility => PredictorEventNeededInput.Occurrence,
        PredictorPhase.EventChoice when run.EventNeedsHealthFact => PredictorEventNeededInput.Health,
        PredictorPhase.EventChoice when run.TrialAbandonConfirmationPending => PredictorEventNeededInput.TrialAbandon,
        PredictorPhase.EventChoice when run.CrystalView != null => PredictorEventNeededInput.Crystal,
        PredictorPhase.EventChoice => PredictorEventNeededInput.Option,
        PredictorPhase.EventCards => PredictorEventNeededInput.Cards,
        PredictorPhase.CombatFacts => PredictorEventNeededInput.Combat,
        PredictorPhase.Rewards when !run.HasObtainChild => PredictorEventNeededInput.Rewards,
        _ => PredictorEventNeededInput.Obtain
    };

    private static ImmutableArray<PredictorCombatPendingCause> RouteEventOccurrenceCauses(PredictorRun run,
        List<RouteDependency> dependencies) => run.Working.Position.Act == 3 ? dependencies.Where(d =>
            RouteCardMayVary(run, [d], "LANTERN_KEY"))
            .Select(d => d.Cause).Distinct().ToImmutableArray() : [];

    private static bool ApplyRouteEventAction(PredictorRun run, PredictorPlannedEventAction step,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (step.Obtain?.Action != PredictorObtainChildAction.Health) CompleteLiveObtainHealth(run, dependencies, token);
        if (step.Obtain == null) CompleteLivePaelsTooth(run, dependencies, token);
        bool Pending(string code, ImmutableArray<PredictorCombatPendingCause> parents = default)
        { local.Add(RouteCause(code, run.Working.Position.NodeId, PredictorCombatDomain.Continuation, parents,
            PredictorRouteCauseEvidence.Key("event-action", run._eventKey?.Serialized ?? "", run._inputs.Length.ToString(), step.Action.ToString()))); return false; }
        PredictorInput? input = null;
        var traceDomains = PredictorCombatDomain.None;
        switch (step.Action)
        {
            case PredictorEventActionKind.Option:
            {
                if (run.Phase != PredictorPhase.EventChoice || run.EventNeedsHealthFact || run.CrystalView != null ||
                    step.ExpectedOption is not { } expected) return Pending("PlannedEventOptionPending");
                var actual = run.EventOptions.FirstOrDefault(o => o.Key == expected.Key);
                var observation = ObserveRouteEventOptions(run, dependencies, knownInstanceIds).FirstOrDefault(o => o.Key == expected.Key);
                if (actual == null || !SameRouteEventOption(actual, expected)) return Pending("PlannedEventOptionChanged");
                if (observation?.IsKnown != true || !actual.Enabled) return Pending("PlannedEventOptionPending", observation?.PendingCauses ?? []);
                var reads = RouteEventActionReadDomains(run, actual, dependencies, knownInstanceIds);
                var readCauses = RouteEventActionReadCauses(run, actual, dependencies, knownInstanceIds);
                if (!readCauses.IsEmpty)
                {
                    if (TryContinueRouteEventDeckAction(run, actual, readCauses, dependencies, token)) return true;
                    return Pending("PlannedEventActionDependent", readCauses);
                }
                traceDomains = reads;
                input = new ChooseEventOption(expected.Key);
                break;
            }
            case PredictorEventActionKind.Cards:
                if (run.Phase != PredictorPhase.EventCards) return Pending("PlannedEventCardPending");
                if (!run.EventOfferedCards.IsEmpty)
                {
                    var knowledge = ObserveRouteEventOfferedCards(run, dependencies)!;
                    if (knowledge.OfferBinding == null || !knowledge.IsKnown &&
                        run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults)
                        return Pending("PlannedEventOfferedCardsPending", knowledge.PendingCauses);
                    var expectedOffer = run.EventCardOffer() with { Cards = step.ExpectedCards };
                    if (!SameBoundRewardOffer(run.EventCardOffer(), new(expectedOffer, 0)
                        { UnresolvedEnchantmentIndices = step.UnresolvedEnchantmentIndices }, knowledge.OfferBinding) ||
                        step.CardIndices.Length != run.EventCardsRequired || step.CardIndices.Any(i => i < 0 || i >= run.EventOfferedCards.Length))
                        return Pending("PlannedEventOfferedCardsChanged");
                    input = new SelectEventCards(step.CardIndices.Select(i => run.EventOfferedCards[i].Id).ToImmutableArray());
                }
                else
                {
                    var deckRead = RouteEventDeckRead(run._eventKey?.Entry, run._eventBranch);
                    if (step.DeckCards.Length != run.EventCardsRequired || step.DeckCards.Any(c =>
                        !IsKnownRouteDeckTarget(run, c, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution) || !run.EventCardCandidates.Contains(c.InstanceId)))
                        return Pending("PlannedEventDeckTargetPending", step.DeckCards.Length == run.EventCardsRequired ?
                            RouteDeckTargetCauses(run, step.DeckCards, dependencies, knownInstanceIds, deckRead.Scope, deckRead.Execution) : []);
                    // Every event transformation substitutes and restores Niche
                    // around its own RNG, including Conveyor's Jelly Liver.
                    if (!CausesFor(dependencies, PredictorCombatDomain.Relics).IsEmpty) return Pending("PlannedEventCardHooksPending", CausesFor(dependencies, PredictorCombatDomain.Relics));
                    input = new SelectEventCards(step.DeckCards.Select(c => c.InstanceId).ToImmutableArray());
                }
                break;
            case PredictorEventActionKind.Health:
                if (!run.EventNeedsHealthFact || step.Health is not { } health) return Pending("PlannedEventHealthPending");
                var healthReads = run._conveyorEntryHealthPending ? PredictorCombatDomain.PotionInventory :
                    RouteEventActionReadDomains(run, run._pendingHealthOptions.First(o => o.Key == run._pendingHealthOption), dependencies, knownInstanceIds, resolvingHealth: true);
                if (!run._conveyorEntryHealthPending) healthReads |= PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics;
                healthReads &= ~PredictorCombatDomain.Health;
                healthReads = RefineEventPotionReads(run, healthReads, emptySlotOnly: run._conveyorEntryHealthPending);
                var healthCauses = run._conveyorEntryHealthPending ? CausesFor(dependencies, healthReads) :
                    RouteEventActionReadCauses(run, run._pendingHealthOptions.First(o => o.Key == run._pendingHealthOption),
                        dependencies, knownInstanceIds, resolvingHealth: true)
                    .Where(c => (c.AffectedDomains & healthReads) != 0).ToImmutableArray();
                if (!healthCauses.IsEmpty) return Pending("PlannedEventHealthDependent", healthCauses);
                traceDomains = healthReads;
                input = new ProvideEventHealthFact(health);
                break;
            case PredictorEventActionKind.ConveyorInputs:
                if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
                    run._eventKey?.Entry != "ENDLESS_CONVEYOR" || !run._conveyorEntryHealthPending || step.Conveyor == null)
                    return Pending("PlannedEventHealthPending");
                input = new ProvideConveyorInputs(step.Conveyor);
                break;
            case PredictorEventActionKind.Obtain:
                return ApplyRouteObtainChild(run, step.Obtain!, dependencies, knownInstanceIds, local, token);
            case PredictorEventActionKind.Reward:
            case PredictorEventActionKind.LeaveRewards:
                if (run._eventKey is not { } source) return Pending("PlannedEventRewardPending");
                return ApplyRouteObtainChild(run, new(step.Action == PredictorEventActionKind.Reward ?
                    PredictorObtainChildAction.Reward : PredictorObtainChildAction.LeaveRewards, source)
                    { Reward = step.RewardChoice }, dependencies, knownInstanceIds, local, token);
            case PredictorEventActionKind.Combat:
            {
                var entry = run._eventCombatEntry;
                if (run.Phase != PredictorPhase.CombatFacts || entry == null || entry.Encounter.Key != step.ExpectedEncounter)
                    return Pending("PlannedEventEncounterChanged");
                if (run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
                    return ProjectDrawingEventCombat(run, step, dependencies, local, token);
                if (!CausesFor(dependencies, PredictorCombatDomain.Deck | PredictorCombatDomain.Relics | PredictorCombatDomain.PotionInventory).IsEmpty)
                    return Pending("PlannedEventCombatDependent", CausesFor(dependencies, PredictorCombatDomain.Deck | PredictorCombatDomain.Relics | PredictorCombatDomain.PotionInventory));
                PredictorCombatDefaultResolution resolution;
                try { resolution = run.ResolveCombatDefaults(step.Combat)!; }
                catch (ArgumentException ex) when (IsRouteEventCombatBindingError(ex)) { return Pending("PlannedEventCombatFactsPending"); }
                var missing = resolution.MissingFacts with { EventTimeout = resolution.MissingFacts.EventTimeout && step.EventRanOutOfTime == null };
                if (missing.NeedsDeclaration || resolution.NeedsExtraRewardOrder)
                    return Pending("EventCombatDeclarationRequired", RouteCombatDeclarationCauses(run, missing, resolution));
                if (entry.Encounter.Key.Entry.StartsWith("BATTLEWORN_DUMMY_", StringComparison.Ordinal) != step.EventRanOutOfTime.HasValue)
                    return Pending("PlannedEventTimeoutChanged");
                var upgradable = run.Working.Deck.Select(run.Working.Card).Where(c => c.UpgradeLevel < c.MaxUpgradeLevel).ToImmutableArray();
                bool randomVictoryUpgrade = !upgradable.IsEmpty && entry.Encounter.IsMonsterEncounter && run.Working.Relics.Any(r => !r.Melted &&
                    r.Key.Entry == "FISHING_ROD" && (r.FishingRodCombatsSeen + 1) % 3 == 0);
                if (randomVictoryUpgrade)
                    foreach (var dependency in dependencies.Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.NicheRng) != 0).ToArray())
                        AddDependency(dependencies, dependency with
                        {
                            Cause = dependency.Cause with { AffectedDomains = PredictorCombatDomain.Deck },
                            PossibleCards = upgradable,
                            MayChangeExistingCards = true
                        });
                // Battleworn setting 2 upgrades random deck cards on return.
                // The same actual parent transaction remains alive throughout.
                input = new ResolveCombat(resolution.Facts, step.EventRanOutOfTime);
                break;
            }
            case PredictorEventActionKind.ConfirmAbandon:
                if (!run.TrialAbandonConfirmationPending) return Pending("PlannedEventAbandonPending");
                input = new ConfirmTrialAbandon(step.ConfirmAbandon!.Value);
                break;
            case PredictorEventActionKind.CrystalTool:
            case PredictorEventActionKind.CrystalCell:
                if (!SameRouteCrystal(run.CrystalView, step.ExpectedCrystal)) return Pending("PlannedEventCrystalChanged");
                if (step.Action == PredictorEventActionKind.CrystalCell &&
                    !CausesFor(dependencies, PredictorCombatDomain.Relics | PredictorCombatDomain.Deck).IsEmpty)
                    return Pending("PlannedEventCrystalDependent", CausesFor(dependencies, PredictorCombatDomain.Relics | PredictorCombatDomain.Deck));
                input = step.Action == PredictorEventActionKind.CrystalTool ? new SelectCrystalTool(step.Tool!.Value) :
                    new RevealCrystalCell(step.X!.Value, step.Y!.Value);
                break;
            default: throw new ArgumentException("PredictorRouteEventPlanInvalid");
        }
        var selectedEvent = run._eventKey;
        bool zeroGold = input is ChooseEventOption { OptionKey: "GROUP" } && selectedEvent?.Entry == "MORPHIC_GROVE";
        var generatedCards = input is ChooseEventOption selected ? RouteEventDirectCardCandidates(run, selected.OptionKey) : [];
        var actionTrace = run.EventTrace();
        var actionReads = actionTrace?.Action(run, input, traceDomains, dependencies) ?? [];
        var result = run.Submit(run.Request!, input, token);
        if (result != PredictorInputResult.Accepted) return Pending("PlannedEventInputPending");
        if (!generatedCards.IsEmpty) PropagateRouteEventDirectCardGeneration(run, dependencies, generatedCards);
        if (zeroGold)
        {
            run._preview.Event.AssignedGold = 0;
            run._preview.Partial?.AssignEventGold();
            ClearRouteEventDependencyDomain(dependencies, PredictorCombatDomain.Gold);
        }
        if (input is ProvideEventHealthFact) ClearRouteEventDependencyDomain(dependencies, PredictorCombatDomain.Health);
        actionTrace?.Stage(run, dependencies, actionReads, zeroGold);
        return true;
    }

    // The event's declaration gate already produced these missing facts. Keep
    // the producer roots rather than treating the event wrapper as a new input.
    private static ImmutableArray<PredictorCombatPendingCause> RouteCombatDeclarationCauses(PredictorRun run,
        PredictorCombatRequirements missing, PredictorCombatDefaultResolution resolution)
    {
        var result = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        void Add(string code) => result.Add(RouteCause(code, run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        foreach (var source in missing.EnemyCreations)
            result.Add(PredictorRouteCauseEvidence.Bind(new("EnemyCreationCount", "Enemy creation count is unknown",
                PredictorCombatDomain.Continuation, source), run.Working.Position.NodeId));
        if (missing.StolenCards) Add("StolenCardIdentity");
        if (missing.GremlinGold)
            Add(resolution.Facts.GremlinGold is { GoldWasStolen: true, StolenGoldLoss: null } gold &&
                !PredictorCombatRewardFacts.HasHeistReward(gold) ? "StolenGoldLoss" : "RecoveredGoldAmount");
        if (missing.VictoryHealth) Add("VictoryHealth");
        if (missing.FirstPlayerTurn) Add("FirstPlayerTurn");
        if (missing.EventTimeout) Add("EventTimeout");
        if (resolution.NeedsExtraRewardOrder) Add("ExtraRewardOrder");
        return result.ToImmutable();
    }

    private static bool IsRouteEventCombatBindingError(ArgumentException ex) =>
        PredictorCombatPlanBindingException.IsPendingPlanBinding(ex);

    private static ImmutableArray<PredictorCard> RouteEventDirectCardCandidates(PredictorRun run, string branch)
    {
        PredictorRewardPlan recipe;
        if (run._eventKey?.Entry == "INFESTED_AUTOMATON")
        {
            bool power = branch == "STUDY";
            var pool = run.Context.Catalog!.CharacterCards.Where(c =>
            {
                var definition = PredictorCardChanges.Definition(run.Context, c.CardKey);
                return power ? c.CardType == EffectCardType.Power : definition.CanonicalCost == 0 && !definition.CostsX;
            }).Select(c => c.CardKey).ToImmutableArray();
            if (pool.IsEmpty) return [];
            recipe = new(PredictorRewardKind.Card, CardCount: 1, Odds: PredictorCardOdds.Regular,
                NoUpgradeRoll: true, CardPoolOverride: pool, NoCardPoolModifications: !power);
        }
        else if (run._eventKey?.Entry == "ENDLESS_CONVEYOR" && branch == "GRAB_FRIED_EEL")
            recipe = new(PredictorRewardKind.Card, CardCount: 1, Colorless: true, NoUpgradeRoll: true);
        else return [];
        return PredictorRewardGeneration.ResolveCardPool(run.Context, run.Working, ref recipe, isCardReward: false)
            .Where(c => c.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .Select(c => PredictorCardChanges.Definition(run.Context, c.CardKey).Prototype).ToImmutableArray();
    }

    private static void PropagateRouteEventDirectCardGeneration(PredictorRun run, List<RouteDependency> dependencies,
        ImmutableArray<PredictorCard> possibleCards)
    {
        var domains = PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds;
        if (PredictorSettlementEffects.Has(run.Working, "LUCKY_FYSH")) domains |= PredictorCombatDomain.Gold;
        if (PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS")) domains |= PredictorCombatDomain.Health;
        // Both donor recipes are non-encounter generation; neither reads the
        // persistent card-rarity offset, and only its actual pool is possible.
        foreach (var dependency in dependencies.Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.RewardsRng) != 0).ToArray())
            AddDependency(dependencies, dependency with { Cause = dependency.Cause with { AffectedDomains = domains },
                PossibleCards = possibleCards });
    }

    private static PredictorCombatDomain RouteEventRewardDomains(PredictorRun run, PredictorReward reward,
        PredictorCombatDomain domains)
    {
        if (run._obtainFrames.IsEmpty && (run._eventKey?.Entry == "CRYSTAL_SPHERE" ||
            (run._eventKey?.Entry, run._eventBranch) is ("POTION_COURIER", "GRAB_POTIONS") or ("DROWNING_BEACON", "BOTTLE")))
            domains &= ~PredictorCombatDomain.RewardsRng;
        if (reward.RerollRecipe is { Odds: PredictorCardOdds.Uniform, Rarity: not null })
            domains &= ~PredictorCombatDomain.Odds;
        if (reward.RerollRecipe is { EncounterSource: false, ForceRarityOddsChange: false })
            domains &= ~PredictorCombatDomain.Odds;
        return domains;
    }

    private static PredictorCombatPreview? ObserveRouteEventCombat(PredictorRun run, PredictorPlannedEventAction? step,
        List<RouteDependency> dependencies)
    {
        dependencies = [.. dependencies];
        PropagateDeckCombatReaders(run, step?.Combat, dependencies, []);
        PredictorCombatProjection? projection;
        try { projection = run.ProjectCurrentCombat(step?.Combat); }
        catch (ArgumentException ex) when (IsRouteEventCombatBindingError(ex)) { return null; }
        if (projection == null) return null;
        var preview = projection.Knowledge;
        if (step?.EventRanOutOfTime != null)
            preview = preview with { MissingFacts = preview.MissingFacts with { EventTimeout = false },
                PendingCauses = preview.PendingCauses.Where(c => c.Code != "EventTimeout").ToImmutableArray() };
        var inherited = dependencies.Select(d => d.Cause).ToImmutableArray();
        var rewards = ObserveRouteRewards(projection.Witness, dependencies, preview.Rewards);
        return preview with { Rewards = rewards, PendingCauses = preview.PendingCauses.AddRange(inherited).Distinct().ToImmutableArray() };
    }

    private static bool SameRouteEventOption(PredictorEventOption actual, PredictorEventOption expected)
        => actual with { Enabled = true } == expected with { Enabled = true };

    private static bool SameRouteCrystal(PredictorCrystalView? actual, PredictorCrystalView? expected)
        => actual != null && expected != null && actual.Remaining == expected.Remaining && actual.Tool == expected.Tool &&
            actual.PlacedAllItems == expected.PlacedAllItems && actual.Cells.SequenceEqual(expected.Cells);

    private static void ClearRouteEventDependencyDomain(List<RouteDependency> dependencies, PredictorCombatDomain domain)
    {
        for (int i = dependencies.Count - 1; i >= 0; i--)
        {
            var retained = dependencies[i].Cause.AffectedDomains & ~domain;
            if (retained == PredictorCombatDomain.None) dependencies.RemoveAt(i);
            else dependencies[i] = dependencies[i] with { Cause = dependencies[i].Cause with { AffectedDomains = retained } };
        }
    }

    private static int ActiveRouteEventPlanPrefix(PredictorRun run, PredictorPlannedEvent plan,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (run._nodeReplayInputs.IsEmpty) return 0;
        var prefix = run.CreateEntryReplay(PreviewReplayPurpose.EventPlanBinding);
        prefix.RestoreRoutes(run._nodeEntryRoutes, run._preview.Settings.AcceptsPrefixes);
        prefix.AdvanceRoute(token);
        int cursor = 0, applied = 0;
        var actual = run._nodeReplayInputs;
        while (cursor < actual.Length && actual[cursor] is ChooseEventOccurrence or DeclareEventEligibility or CorrectGoldBalance)
        {
            if (actual[cursor] is ChooseEventOccurrence occurrence && plan.Occurrence != null && occurrence != plan.Occurrence)
            { local.Add(RouteCause("PlannedEventOccurrenceChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation)); return 0; }
            if (prefix.Submit(prefix.Request!, actual[cursor++], token) != PredictorInputResult.Accepted)
                throw new InvalidOperationException("PredictorEventAcceptedOccurrenceReplayMismatch");
        }
        while (cursor < actual.Length)
        {
            // Accepted inventory/budget corrections are the current boundary's
            // facts. Replaying them does not add an event choice to the plan.
            if (actual[cursor] is CorrectGoldBalance or DiscardPotion or UseOutOfCombatPotion)
            {
                if (prefix.Submit(prefix.Request!, actual[cursor++], token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                    throw new InvalidOperationException("PredictorEventAcceptedCorrectionReplayMismatch");
                continue;
            }
            if (applied >= plan.Actions.Length)
            { local.Add(RouteCause("PlannedEventAcceptedPrefixMissing", run.Working.Position.NodeId, PredictorCombatDomain.Continuation)); return applied; }
            int before = prefix._nodeReplayInputs.Length;
            var prefixLocal = new List<PredictorCombatPendingCause>();
            if (!ApplyRouteEventAction(prefix, plan.Actions[applied], [.. dependencies], knownInstanceIds, prefixLocal, token))
            { local.AddRange(prefixLocal); return applied; }
            var generated = prefix._nodeReplayInputs.Skip(before).ToArray();
            int count = Math.Min(generated.Length, actual.Length - cursor);
            if (generated.Length == 0 || !generated.Take(count).Zip(actual.Skip(cursor).Take(count)).All(pair =>
                JsonSerializer.Serialize(pair.First, pair.First.GetType()) == JsonSerializer.Serialize(pair.Second, pair.Second.GetType())))
            { local.Add(RouteCause("PlannedEventAcceptedPrefixChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation)); return applied; }
            cursor += count;
            if (count < generated.Length) return applied;
            applied++;
        }
        return applied;
    }

    private static bool RouteEventOptionNeedsHealth(string key, string branch) => (key, branch) is
        ("MORPHIC_GROVE", "LONER") or ("WHISPERING_HOLLOW", "HUG") or ("TRASH_HEAP", "DIVE_IN") or
        ("TRIAL", "NOBLE_GUILTY") or ("THIS_OR_THAT", "PLAIN") or ("UNREST_SITE", _) or ("SAPPHIRE_SEED", "EAT") or
        ("SLIPPERY_BRIDGE", "HOLD_ON") or ("STONE_OF_ALL_TIME", _) or ("SUNKEN_STATUE", "DIVE_INTO_WATER") or
        ("ROUND_TEA_PARTY", "ENJOY_TEA") or ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") or
        ("ABYSSAL_BATHS", "IMMERSE") or ("ABYSSAL_BATHS", "LINGER") or ("ABYSSAL_BATHS", "ABSTAIN") or
        ("DROWNING_BEACON", "CLIMB") or ("TABLET_OF_TRUTH", "DECIPHER") or ("TABLET_OF_TRUTH", "SMASH") or
        ("COLOSSAL_FLOWER", "REACH_DEEPER") or ("COLOSSAL_FLOWER", "POLLINOUS_CORE") or
        ("DENSE_VEGETATION", "TRUDGE_ON") or ("DENSE_VEGETATION", "REST") or ("SPIRALING_WHIRLPOOL", "DRINK") or
        ("ROOM_FULL_OF_CHEESE", "SEARCH") or ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK") or
        ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT") or ("BRAIN_LEECH", "RIP") or ("BYRDONIS_NEST", "EAT") or
        ("DOLL_ROOM", "TAKE_SOME_TIME") or ("DOLL_ROOM", "EXAMINE") or ("SPIRIT_GRAFTER", _) or
        ("JUNGLE_MAZE_ADVENTURE", "SOLO_QUEST");

    private static (ImmutableArray<long> Candidates, int Requested)? RouteEventPicker(PredictorRun run, string key, string branch)
    {
        var deck = run.Working.Deck;
        var transforms = PredictorCardChanges.TransformCandidates(run.Context, run.Working);
        var upgrades = deck.Where(id => run.Working.Card(id).UpgradeLevel < run.Working.Card(id).MaxUpgradeLevel).ToImmutableArray();
        var removable = deck.Where(id => run.Working.Card(id).Removable).ToImmutableArray();
        return (key, branch) switch
        {
            ("AROMA_OF_CHAOS", "LET_GO") or ("SYMBIOTE", "KILL_WITH_FIRE") or ("WHISPERING_HOLLOW", "HUG") => (transforms, 1),
            ("MORPHIC_GROVE", "GROUP") => (transforms, 2),
            ("TRIAL", "NONDESCRIPT_INNOCENT") => (transforms, 2),
            ("AROMA_OF_CHAOS", "MAINTAIN_CONTROL") or ("SAPPHIRE_SEED", "EAT") or ("SPIRIT_GRAFTER", "REJECTION") => (upgrades, 1),
            ("TRIAL", "MERCHANT_INNOCENT") => (upgrades, 2),
            ("SYMBIOTE", "APPROACH") or ("STONE_OF_ALL_TIME", "PUSH") => (run.SymbioteCandidates(), 1),
            ("SAPPHIRE_SEED", "PLANT") or ("FIELD_OF_MAN_SIZED_HOLES", "ENTER_YOUR_HOLE") => (run.SownCandidates(), 1),
            ("ZEN_WEAVER", "EMOTIONAL_AWARENESS") or ("WELLSPRING", "BATHE") or ("DOORS_OF_LIGHT_AND_DARK", "DARK") => (removable, 1),
            ("ZEN_WEAVER", "ARACHNID_ACUPUNCTURE") or ("FIELD_OF_MAN_SIZED_HOLES", "RESIST") or ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") => (removable, 2),
            ("WOOD_CARVINGS", "BIRD") or ("WOOD_CARVINGS", "TORUS") => (run.WoodBasicCandidates(), 1),
            ("WOOD_CARVINGS", "SNAKE") => (run.WoodSnakeCandidates(), 1),
            ("SELF_HELP_BOOK", _) when branch != "NO_OPTIONS" => (run.SelfHelpBookCandidates(branch), 1),
            ("SPIRALING_WHIRLPOOL", "OBSERVE") => (run.SpiralingWhirlpoolCandidates(), 1),
            ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT") => (run.GraveSoulCandidates(), 1),
            ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL") => (deck.Where(id => PredictorObtainSources.CanEnchant(run.Context, run.Working.Card(id))).ToImmutableArray(), 1),
            ("WATERLOGGED_SCRIPTORIUM", "PRICKLY_SPONGE") => (deck.Where(id => PredictorObtainSources.CanEnchant(run.Context, run.Working.Card(id))).ToImmutableArray(), 2),
            ("ENDLESS_CONVEYOR", "GRAB_JELLY_LIVER") => (transforms, 1),
            ("AMALGAMATOR", _) => (deck.Where(id => run.Working.Card(id).Removable &&
                PredictorCardChanges.Definition(run.Context, run.Working.Card(id).Key) is { Rarity: "Basic" } definition &&
                (branch == "COMBINE_STRIKES" ? definition.IsStrike : definition.IsDefend)).ToImmutableArray(), 2),
            _ => null
        };
    }

    private static ModelKey? RouteEventObtainRelic(PredictorRun run, string key, string branch)
    {
        string? relic = (key, branch) switch
        {
            ("TEA_MASTER", _) => branch,
            ("SUNKEN_STATUE", "GRAB_SWORD") => "SWORD_OF_STONE",
            ("ROUND_TEA_PARTY", "ENJOY_TEA") => "ROYAL_POISON",
            ("DROWNING_BEACON", "CLIMB") => "FRESNEL_LENS",
            ("LOST_WISP", "CLAIM") => "LOST_WISP",
            ("COLOSSAL_FLOWER", "POLLINOUS_CORE") => "POLLINOUS_CORE",
            ("GRAVE_OF_THE_FORGOTTEN", "ACCEPT") => "FORGOTTEN_SOUL",
            ("ROOM_FULL_OF_CHEESE", "SEARCH") => "CHOSEN_CHEESE",
            ("HUNGRY_FOR_MUSHROOMS", _) => branch,
            ("WELCOME_TO_WONGOS", "MYSTERY_BOX") => "WONGOS_MYSTERY_TICKET",
            ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CAGE") or ("WAR_HISTORIAN_REPY", "SECOND_UNLOCK_CAGE") => "HISTORY_COURSE",
            ("DOLL_ROOM", _) when branch.StartsWith("DOLL_", StringComparison.Ordinal) => branch[5..],
            _ => null
        };
        if (relic != null) return new("RELIC", relic);
        if (key == "WELCOME_TO_WONGOS" && branch == "FEATURED_ITEM") return run._wongoFeatured;
        if (key == "WELCOME_TO_WONGOS" && branch == "BARGAIN_BIN")
        {
            var snapshot = run.Working;
            var catalog = run.Context.Catalog!;
            return snapshot.PersonalBag.Pull("Common", false,
                candidate => PredictorRewardGeneration.RelicAllowed(run.Context, snapshot, candidate),
                candidate => catalog.ShopRelics.Any(r => r.Key == candidate && r.IsAllowedInShops)).Key ?? new ModelKey("RELIC", "CIRCLET");
        }
        if ((key, branch) is ("TRASH_HEAP", "DIVE_IN") or ("DOLL_ROOM", "RANDOM"))
        {
            var local = PredictorStreamState.Capture(PredictorStream.Niche, run._eventRng!).Restore();
            if (key == "TRASH_HEAP")
            {
                var relics = RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapDiveRelics;
                return relics[local.NextInt(relics.Count)];
            }
            var entries = new[] { "DAUGHTER_OF_THE_WIND", "MR_STRUGGLES", "BING_BONG" };
            return new("RELIC", entries[local.NextInt(entries.Length)]);
        }
        if (key == "FAKE_MERCHANT" && branch.StartsWith("BUY_", StringComparison.Ordinal))
            return run._fakeStock.Single(s => s.Slot == int.Parse(branch.AsSpan(4))).Key;
        if (key == "RELIC_TRADER" && branch is "TOP" or "MIDDLE" or "BOTTOM")
            return run._traderOffers[branch == "TOP" ? 0 : branch == "MIDDLE" ? 1 : 2];
        return null;
    }

    private static void ValidatePlannedEvent(PredictorPlannedEvent plan)
    {
        RequirePlan(plan.ExpectedEvent.IsValid && plan.ExpectedEvent.Category == "EVENT" && !plan.Actions.IsDefault);
        ValidateEventPotionInput(plan);
        if (plan.Occurrence is { } occurrence)
            RequirePlan(occurrence.Event == plan.ExpectedEvent && occurrence.QueueEvent.IsValid && occurrence.QueueEvent.Category == "EVENT" &&
                occurrence.RawOrdinal > 0 && occurrence.ExpectedCursor >= 0 && occurrence.SelectedCursor >= occurrence.ExpectedCursor);
        if (plan.ExpectedOccurrence is { } receipt)
            RequirePlan(receipt.Event == plan.ExpectedEvent && receipt.QueueEvent.IsValid && receipt.QueueEvent.Category == "EVENT" &&
                receipt.RawOrdinal > 0 && receipt.ExpectedCursor >= 0 && receipt.SelectedCursor >= receipt.ExpectedCursor);
        foreach (var step in plan.Actions)
        {
            RequirePlan(step != null && Enum.IsDefined(step.Action) && !step.DeckCards.IsDefault && !step.ExpectedCards.IsDefault && !step.CardIndices.IsDefault);
            RequirePlan(!step.UnresolvedEnchantmentIndices.IsDefault &&
                (step.Action == PredictorEventActionKind.Cards || step.UnresolvedEnchantmentIndices.IsEmpty));
            if (!step.UnresolvedEnchantmentIndices.IsEmpty)
                ValidateRewardEnchantmentMask(new(0, PredictorRewardKind.Card, PredictorRewardOrigin.Event,
                    plan.ExpectedEvent, step.ExpectedCards), step.UnresolvedEnchantmentIndices);
            RequirePlan((step.Action == PredictorEventActionKind.Option) == (step.ExpectedOption != null));
            RequirePlan((step.Action == PredictorEventActionKind.Health) == (step.Health != null));
            RequirePlan((step.Action == PredictorEventActionKind.ConveyorInputs) == (step.Conveyor != null));
            if (step.Conveyor is { } conveyor)
                RequirePlan(plan.ExpectedEvent.Entry == "ENDLESS_CONVEYOR" && conveyor.EmptyPotionSlots >= 0 && conveyor.LostHp >= 0);
            RequirePlan((step.Action == PredictorEventActionKind.Obtain) == (step.Obtain != null));
            RequirePlan((step.Action == PredictorEventActionKind.Reward) == (step.RewardChoice != null));
            RequirePlan((step.Action == PredictorEventActionKind.Combat) == (step.ExpectedEncounter != null));
            RequirePlan(step.Action == PredictorEventActionKind.Combat || step.Combat == null && step.EventRanOutOfTime == null);
            RequirePlan((step.Action == PredictorEventActionKind.ConfirmAbandon) == (step.ConfirmAbandon != null));
            RequirePlan((step.Action is PredictorEventActionKind.CrystalTool or PredictorEventActionKind.CrystalCell) == (step.ExpectedCrystal != null));
            RequirePlan((step.Action == PredictorEventActionKind.CrystalTool) == (step.Tool != null));
            RequirePlan((step.Action == PredictorEventActionKind.CrystalCell) == (step.X != null && step.Y != null));
            if (step.ExpectedOption is { } option) RequirePlan(!string.IsNullOrWhiteSpace(option.Key) && option.CardTargetId is not < 1 && option.PotionId is not < 1 && option.RelicId is not < 1 &&
                option.LocalPotionOrdinal is not < 0 && (option.LocalPotionOrdinal.HasValue == option.LocalPotionKey.HasValue) &&
                (!option.LocalPotionOrdinal.HasValue || plan.Potions is { OrderedSlots.IsDefault: false } input &&
                    option.PotionId == null && option.LocalPotionOrdinal < input.OrderedSlots.Length &&
                    input.OrderedSlots[option.LocalPotionOrdinal.Value] == option.LocalPotionKey));
            if (step.Health is { } health) RequirePlan(health.CurrentHp > 0 && health.MaxHp >= health.CurrentHp);
            if (step.Obtain is { } obtain) ValidatePlannedObtainChild(obtain);
            if (step.RewardChoice is { } reward) ValidatePlannedNodes([new("event-reward-shape") { RewardChoices = [reward] }]);
            if (step.Combat is { } combat) ValidatePlannedCombat(combat);
            if (step.ExpectedEncounter is { } encounter) RequirePlan(encounter.IsValid && encounter.Category == "ENCOUNTER");
            if (step.Tool is { } tool) RequirePlan(Enum.IsDefined(tool));
            if (step.ExpectedCrystal is { } crystal) RequirePlan(!crystal.Cells.IsDefault && crystal.Remaining > 0 && Enum.IsDefined(crystal.Tool));
            if (step.X != null || step.Y != null) RequirePlan(step.X is >= 0 and < 11 && step.Y is >= 0 and < 11);
            RequirePlan(step.Action == PredictorEventActionKind.Cards || step.DeckCards.IsEmpty && step.ExpectedCards.IsEmpty && step.CardIndices.IsEmpty);
            if (step.Action == PredictorEventActionKind.Cards)
            {
                RequirePlan(step.DeckCards.IsEmpty != step.ExpectedCards.IsEmpty &&
                    step.DeckCards.Select(c => c.InstanceId).Distinct().Count() == step.DeckCards.Length &&
                    step.CardIndices.Distinct().Count() == step.CardIndices.Length &&
                    step.CardIndices.All(i => i >= 0 && i < step.ExpectedCards.Length));
                foreach (var card in step.DeckCards) ValidatePlannedCard(card);
                RequirePlan(!step.ExpectedCards.IsEmpty || step.CardIndices.IsEmpty);
                RequirePlan(step.ExpectedCards.All(c => c != null && c.Key.IsValid && c.Key.Category == "CARD" && c.Id >= 0));
            }
        }
    }
}
