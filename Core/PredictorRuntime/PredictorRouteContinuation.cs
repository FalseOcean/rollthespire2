using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // A detached preview leaves even exhausted-queue fallback at the event
    // boundary, where appearance and event entry prerequisites are observable.
    private sealed record RouteDependency(PredictorCombatPendingCause Cause,
        ImmutableArray<PredictorCard> PossibleCards = default, bool MayChangeExistingCards = false,
        bool AnyExistingCard = false)
    {
        // Default is an unmodeled scope; an initialized empty array proves no change.
        public ImmutableArray<string> ChangedRelicEntries { get; init; }
        public ImmutableArray<string> ChangedRelicStateEntries { get; init; }
        public ImmutableArray<string> ChangedCardMembershipEntries { get; init; } =
            Cause.Code == "PartialUpgradeTargets" ? [] : default;
        public ImmutableArray<string> ChangedCardStateEntries { get; init; }
        // Preserve the producer's attribute/instance scope across synchronization.
        // All is reserved for a genuinely unmodeled deck effect.
        public PredictorDeckUncertainFact DeckFacts { get; init; } = PredictorDeckUncertainFact.All;
        public ImmutableArray<long> AffectedCardIds { get; init; }
        public ImmutableArray<RolltheSpire2.Core.Effects.Snapshots.EffectCardType> AffectedCardTypes { get; init; }
        public bool? ChangesPersonalRelicBag { get; init; }
        public bool? ChangesSharedRelicBag { get; init; }
    }

    // Recompute from the actual boundary and accepted active-node input prefix.
    // Witness omissions below are never accepted inputs of the caller, and no
    // witness checkpoint or state is included in the result.
    internal PredictorRouteContinuation PreviewRouteContinuation(ImmutableArray<PredictorPlannedNode> plans,
        int maximumNodes = 8, CancellationToken token = default,
        PredictorRoutePreviewScope scope = PredictorRoutePreviewScope.CombatAndRest,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices,
        bool allowSelectedPrefixes = false, bool isolateNodeFailures = false)
    {
        RequireRouteSimulation();
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        bool includeAllNodes = scope == PredictorRoutePreviewScope.AllModeledNodes;
        ValidatePlannedNodes(plans);
        if (plans.IsDefault || plans.Any(p => p == null || string.IsNullOrWhiteSpace(p.NodeId) ||
                p.RestActions.IsDefault || p.RewardChoices.IsDefault) ||
            plans.Select(p => p.NodeId).Distinct(StringComparer.Ordinal).Count() != plans.Length ||
            maximumNodes is < 1 or > 64)
            throw new ArgumentException("PredictorRoutePlansInvalid");
        var scratch = CreateRoutePreview(plans, policy, allowSelectedPrefixes);
        scratch.RestoreRoutes(Routes, allowSelectedPrefixes);
        if (Phase != PredictorPhase.Complete)
        {
            var draft = CaptureNodeDraft() ?? throw new InvalidOperationException("PredictorRouteDraftMissing");
            scratch.RestoreNodeDraft(draft, token);
        }
        var dependencies = (Context.Live?.PendingCauses ?? []).Select(source => PredictorRouteCauseEvidence.Bind(source))
            .Select(cause => new RouteDependency(cause,
            MayChangeExistingCards: (cause.AffectedDomains & PredictorCombatDomain.Deck) != 0,
            AnyExistingCard: (cause.AffectedDomains & PredictorCombatDomain.Deck) != 0)).ToList();
        var knownInstanceIds = scratch.Working.Cards.Select(c => c.Id).Concat(scratch.Working.Relics.Select(r => r.Id))
            .Concat(scratch.Working.PotionSlots.OfType<PredictorPotion>().Select(p => p.Id)).ToHashSet();
        var nodes = ImmutableArray.CreateBuilder<PredictorRouteNodeKnowledge>();
        PredictorRouteTerminalBoundary? terminal = null;
        PredictorRoutePredictionFault? fault = null;
        var partial = scratch.StartRouteKnowledge(dependencies);
        while (nodes.Count < maximumNodes)
        {
            token.ThrowIfCancellationRequested();
            int completedNodes = nodes.Count;
            var faultPosition = scratch.Working.Position;
            string faultStage = "entry";
            try
            {
                if (scratch.Phase == PredictorPhase.Complete) faultPosition = RouteTerminalTarget(scratch);
                var entering = partial.Capture(scratch.Working);
                var enteringStreams = partial.StreamProgress();
                var entryCauses = new List<PredictorCombatPendingCause>();
                if (scratch.Phase == PredictorPhase.Complete)
                {
                    if (scratch.Working.Outcome != null) break;
                    if (!CanProjectNextRouteNode(scratch, dependencies, includeAllNodes))
                    {
                        var blocked = PredictorRouteCauseEvidence.Normalize(CausesFor(dependencies, PredictorCombatDomain.Continuation));
                        // Exhausting a selected prefix or ending Act 3 is an
                        // ordinary stop. Only actual entry blockers create this
                        // terminal record, and AdvanceRoute is never called.
                        if (!blocked.IsEmpty)
                        {
                            var target = RouteTerminalTarget(scratch);
                            terminal = new(target, scratch.Working.Position, blocked);
                            partial.RecordBoundary(target.NodeId, PredictorRouteFactPhase.Before, entering,
                                nodes.Count == 0 && Context.Live != null ? PredictorKnowledgeOrigin.Captured : PredictorKnowledgeOrigin.Derived);
                            partial.Facts.RecordResult("route.entry.entered", target.NodeId, false,
                                operation: "route entry blocked before AdvanceRoute",
                                derivation: "CanProjectNextRouteNode refused entry; no room entry or after frame exists.");
                            partial.Facts.RecordUnknown("route.entry.allowed", target.NodeId, PredictorRouteFactPhase.Results,
                                blocked, operation: "route entry prerequisites");
                        }
                        break;
                    }
                    scratch._preview.RequiresEventChoice = includeAllNodes;
                    if (includeAllNodes) ConfigureRouteShopProjection(scratch, dependencies, entryCauses, knownInstanceIds);
                    scratch.AdvanceRoute(token);
                }
                var position = scratch.Working.Position;
                faultPosition = position;
                faultStage = "node";
                partial.RecordBoundary(position.NodeId, PredictorRouteFactPhase.Before, entering,
                    nodes.Count == 0 && Context.Live != null ? PredictorKnowledgeOrigin.Captured : PredictorKnowledgeOrigin.Derived);
                var visit = scratch.Working.Visits.LastOrDefault();
                var room = visit?.Room ?? PredictorRoomKind.Event;
                var plan = plans.SingleOrDefault(p => p.NodeId == position.NodeId) ?? new(position.NodeId);
                var local = entryCauses;
                var potionBoundaries = ImmutableArray.CreateBuilder<PredictorRoutePotionBoundaryKnowledge>();
                var observed = ImmutableArray<PredictorCombatRewardKnowledge>.Empty;
                PredictorRestHealOutcome? heal = null;
                ImmutableArray<PredictorRestOffer> restOffers = [];
                ImmutableArray<PredictorCombatPendingCause>? liveHatchOfferPending = null;
                ImmutableArray<PredictorRouteCardKnowledge> restCards = [];
                var restActionKnowledge = new List<PredictorRestActionKnowledge>();
                int appliedRestActions = 0;
                bool restDefaultApplied = false;
                PredictorPlannedRestAction? defaultRestAction = null;
                bool advanced = false;
                PredictorShopNodeKnowledge? shop = null;
                PredictorAncientNodeKnowledge? ancient = null;
                PredictorEventNodeKnowledge? eventKnowledge = null;
                PredictorTreasureNodeKnowledge? treasure = null;
                PredictorCombatRequirements? combatRequirements = null;
                ImmutableArray<PredictorRouteCardKnowledge> combatCards = [];
                ImmutableArray<PredictorRouteRelicKnowledge> combatRelics = [];
                bool combatNode = room is PredictorRoomKind.Monster or PredictorRoomKind.Elite or PredictorRoomKind.Boss;
                if (!combatNode)
                    potionBoundaries.Add(ApplyRouteInventoryActions(scratch, plan, PredictorRoutePotionStage.BeforeChoices,
                        dependencies, knownInstanceIds, local, token));
                if (scratch.Phase is PredictorPhase.CombatFacts or PredictorPhase.Rewards && combatNode)
                {
                    var afterGeneration = new List<RouteDependency>();
                    if (scratch.Phase == PredictorPhase.CombatFacts)
                    {
                        PropagateDeckCombatReaders(scratch, plan.Combat, dependencies, afterGeneration);
                        combatRequirements = scratch.CombatRequirements;
                        combatCards = ObserveRouteCards(scratch, dependencies, knownInstanceIds);
                        combatRelics = scratch.Working.Relics.Where(r => !r.Melted && PredictorCombatDefaults.IsConfigurableRelic(r.Key))
                            .Select(relic => new PredictorRouteRelicKnowledge(relic, CausesFor(dependencies, PredictorCombatDomain.Relics)
                                .AddRange(knownInstanceIds.Contains(relic.Id) ? [] : CausesFor(dependencies, PredictorCombatDomain.InstanceIds))
                                .Distinct().ToImmutableArray())).ToImmutableArray();
                        PredictorCombatProjection? projection = null;
                        try { projection = scratch.ProjectCurrentCombat(plan.Combat, token); }
                        catch (ArgumentException ex) when (plan.Combat != null && PredictorCombatPlanBindingException.IsPendingPlanBinding(ex))
                        {
                            // Saved identities can become unbound after an earlier
                            // edit. Preserve that plan and expose its local binding
                            // remainder; genuine execution faults still propagate.
                            local.Add(RouteCause("PlannedCombatFactsPending", position.NodeId, PredictorCombatDomain.Continuation));
                        }
                        if (projection != null)
                        {
                            foreach (var cause in projection.Knowledge.PendingCauses)
                                AddDependency(dependencies, new(cause,
                                    cause.Code == "StolenCardIdentity" ? scratch.Working.Deck.Select(scratch.Working.Card).ToImmutableArray() : default,
                                    (cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 && cause.Code != "PaelsToothReturnedCardPending"));
                            scratch = scratch.AcceptSettledRouteCombat(projection.Witness);
                            observed = ObserveRouteRewards(scratch, dependencies, projection.Knowledge.Rewards);
                        }
                    }
                    else observed = ObserveRouteRewards(scratch, dependencies);
                    var used = new HashSet<int>();
                    var skipped = new HashSet<long>();
                    potionBoundaries.Add(ApplyRouteInventoryActions(scratch, plan, PredictorRoutePotionStage.BeforeChoices,
                        dependencies, knownInstanceIds, local, token));
                    bool choicesComplete = local.All(c => (c.AffectedDomains & PredictorCombatDomain.Continuation) == 0) &&
                        ApplyRouteRewardChoices(scratch, plan, dependencies, used, skipped, local, token, knownInstanceIds);
                    observed = ObserveRouteRewards(scratch, dependencies, observed);
                    if (choicesComplete && scratch.Phase == PredictorPhase.Rewards)
                    {
                        potionBoundaries.Add(ApplyRouteInventoryActions(scratch, plan, PredictorRoutePotionStage.BeforeLeave,
                            dependencies, knownInstanceIds, local, token));
                        // Authored closure and approved live card defaults resolve
                        // their choices. Other witness omissions retain dependencies.
                        foreach (var reward in scratch.Rewards.Where(r => r.Status == PredictorRewardStatus.Open && !skipped.Contains(r.Id)))
                        {
                            if (!plan.LeaveRewards) AddPendingReward(scratch, reward, dependencies);
                        }
                        if (dependencies.All(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Continuation) == 0) &&
                            local.All(c => (c.AffectedDomains & PredictorCombatDomain.Continuation) == 0))
                        {
                            if (scratch.Selection.CardRewardId != null) scratch.Submit(scratch.Request!, new ExitCardSelection(), token);
                            advanced = scratch.Submit(scratch.Request!, new LeaveRewards(), token) == PredictorInputResult.Accepted;
                            if (advanced)
                            {
                                // SkipCardReward only exits the picker. The final
                                // source disposition is assigned by LeaveRewards.
                                // Publish it for authored and approved default Skip;
                                // other witness omissions retain their dependencies.
                                observed = observed.Select((knowledge, index) => index < scratch.Rewards.Length &&
                                    skipped.Contains(scratch.Rewards[index].Id)
                                    ? WithRouteRewardResult(scratch, scratch.Rewards[index], knowledge with
                                        { Value = knowledge.Value != null ? DisplayReward(scratch.Rewards[index]) : null }, dependencies)
                                    : knowledge).ToImmutableArray();
                            }
                        }
                    }
                    foreach (var dependency in afterGeneration) AddDependency(dependencies, dependency);
                }
                else if (room == PredictorRoomKind.RestSite && scratch.Phase is PredictorPhase.RestSite or PredictorPhase.SmithCards or PredictorPhase.RestCookCards or PredictorPhase.RestHealFact or PredictorPhase.Rewards)
                {
                    restOffers = scratch.RestOffers;
                    restCards = ObserveRouteCards(scratch, dependencies, knownInstanceIds);
                    restActionKnowledge = ObserveAcceptedRestActions(scratch, token);
                    var used = new HashSet<int>();
                    var skipped = new HashSet<long>();
                    appliedRestActions = Math.Min(plan.RestActions.Length, scratch._inputs.Count(i => i.Action is
                        "RestSmith" or "RestCookRemoveTwoGainMaxHp5" or "RestLift" or "RestDig" or "RestClone" or "RestHatch" or "RestKindle" or "RestHeal"));
                    if (scratch.Phase == PredictorPhase.Rewards)
                    {
                        var rewardBatch = scratch.Rewards;
                        observed = ObserveRouteRewards(scratch, dependencies);
                        ContinueRouteRestRewards(scratch, plan, dependencies, used, skipped, local, token, knownInstanceIds);
                        observed = ObserveCompletedRouteRestRewards(scratch, rewardBatch, observed, dependencies);
                    }
                    foreach (var step in plan.RestActions.Skip(appliedRestActions))
                    {
                        if (local.Count > 0) break;
                        var actionKnowledge = ObserveRouteRestAction(scratch, appliedRestActions, step.Action, dependencies, knownInstanceIds);
                        int actionStart = scratch._inputs.Length;
                        bool actionApplied = ApplyRouteRestAction(scratch, step, dependencies, knownInstanceIds, local, token);
                        restActionKnowledge.Add(RestActionReceipts(scratch, actionKnowledge, actionStart));
                        if (!actionApplied) break;
                        appliedRestActions++;
                        heal = scratch._inputs.LastOrDefault(i => i.RestHealOutcome != null)?.RestHealOutcome;
                        if (scratch.Phase == PredictorPhase.Rewards)
                        {
                            var rewardBatch = scratch.Rewards;
                            observed = ObserveRouteRewards(scratch, dependencies);
                            if (!ApplyRouteRewardChoices(scratch, plan, dependencies, used, skipped, local, token, knownInstanceIds)) break;
                            observed = ObserveCompletedRouteRestRewards(scratch, rewardBatch, observed, dependencies);
                            if (scratch.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && !skipped.Contains(r.Id)) && !plan.LeaveRewards &&
                                !ContinuePendingRestRewardWitness(scratch, dependencies, skipped))
                            {
                                local.Add(RouteCause("RestRewardChoice", position.NodeId, PredictorCombatDomain.Continuation));
                                break;
                            }
                            if (scratch.Phase == PredictorPhase.Rewards)
                            {
                                if (scratch.Selection.CardRewardId != null)
                                    scratch.Submit(scratch.Request!, new ExitCardSelection(), token);
                                scratch.Submit(scratch.Request!, new LeaveRewards(), token);
                                observed = ObserveCompletedRouteRestRewards(scratch, rewardBatch, observed, dependencies);
                            }
                        }
                        if (scratch.Phase != PredictorPhase.RestSite)
                        {
                            local.Add(RouteCause("RestChildChoice", position.NodeId, PredictorCombatDomain.Continuation));
                            break;
                        }
                    }
                    var defaultCandidates = ObserveRouteRestAction(scratch, 0, PredictorRestAction.Smith, dependencies, knownInstanceIds);
                    restDefaultApplied = ApplyRouteRestDefault(scratch, plan, dependencies, knownInstanceIds,
                        local, token, out defaultRestAction);
                    if (restDefaultApplied && defaultRestAction != null) restActionKnowledge.Add(defaultCandidates);
                    advanced |= restDefaultApplied;
                    if (!restDefaultApplied && local.Count == 0 && appliedRestActions == plan.RestActions.Length && scratch.Phase == PredictorPhase.RestSite)
                    {
                        if (plan.LeaveRest) advanced = scratch.Submit(scratch.Request!, new LeaveRestSite(), token) == PredictorInputResult.Accepted;
                        else local.Add(RouteCause("RestChoice", position.NodeId, PredictorCombatDomain.Continuation));
                    }
                    if (policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
                    {
                        // Remaining unselected actions can affect the suffix,
                        // but cannot retroactively hide their own current offer.
                        liveHatchOfferPending = RouteHatchPendingCauses(scratch, dependencies, knownInstanceIds);
                        if (local.Count == 0 && scratch.Phase is PredictorPhase.SmithCards or PredictorPhase.RestCookCards)
                            local.Add(RouteCause("RestTargetChoice", position.NodeId, PredictorCombatDomain.Continuation));
                        advanced |= ContinueUnselectedRouteRest(scratch, plan, appliedRestActions, dependencies, local, token);
                    }
                }
                else if (includeAllNodes && room == PredictorRoomKind.Shop)
                {
                    shop = ProjectRouteShop(scratch, plan.Shop, dependencies, knownInstanceIds, local, token);
                    advanced = shop.Advanced;
                    observed = ObserveRouteRewards(scratch, dependencies);
                }
                else if (includeAllNodes && room is PredictorRoomKind.Opening or PredictorRoomKind.Ancient)
                {
                    ancient = ProjectRouteAncient(scratch, plan.Ancient, dependencies, knownInstanceIds, local, token);
                    advanced = ancient.Advanced;
                    observed = ancient.Rewards;
                }
                else if (includeAllNodes && room == PredictorRoomKind.Event)
                {
                    eventKnowledge = ProjectRouteEvent(scratch, plan.Event, plan.EventOccurrenceExclusions, dependencies, knownInstanceIds, local, token);
                    advanced = eventKnowledge.Advanced;
                    observed = eventKnowledge.Rewards;
                }
                else if (includeAllNodes && room == PredictorRoomKind.Treasure)
                {
                    treasure = ProjectRouteTreasure(scratch, plan.Treasure, dependencies, knownInstanceIds, local, token);
                    advanced = treasure.Advanced;
                    observed = treasure.Rewards;
                }
                else local.Add(RouteCause("RouteNodeOutsideContinuationSlice", position.NodeId, PredictorCombatDomain.Continuation));

                if (CausesFor(dependencies, PredictorCombatDomain.InstanceIds).IsEmpty)
                {
                    knownInstanceIds.UnionWith(scratch.Working.Cards.Select(c => c.Id));
                    knownInstanceIds.UnionWith(scratch.Working.Relics.Select(r => r.Id));
                    knownInstanceIds.UnionWith(scratch.Working.PotionSlots.OfType<PredictorPotion>().Select(p => p.Id));
                }
                if (potionBoundaries.All(b => b.Stage != PredictorRoutePotionStage.BeforeLeave) &&
                    (scratch.Phase == PredictorPhase.Complete || RoutePotionActionsAvailable(scratch, false)))
                    potionBoundaries.Add(ApplyRouteInventoryActions(scratch, plan, PredictorRoutePotionStage.BeforeLeave,
                        dependencies, knownInstanceIds, local, token));
                if (plan.PotionActions.Any(action => !potionBoundaries.Any(boundary => boundary.Stage == action.Stage)))
                    local.Add(RouteCause("PlannedPotionStagePending", position.NodeId, PredictorCombatDomain.Continuation));
                if (policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
                    foreach (var optional in plan.ObtainInputs.Where(optional => !scratch._preview.Obtains.OptionalInputs.Any(observed =>
                        observed.Node == position.NodeId && observed.Input.Choice.Source == optional.Input.ExpectedSource &&
                        SameOptionalObtainAddress(observed.Input.Address, optional.Address))))
                        local.Add(PredictorRouteCauseEvidence.CreateExplicit("PlannedOptionalObtainSourcePending", position.NodeId,
                            PredictorCombatDomain.None, PredictorPendingReasonKind.PlanBinding,
                            "The optional target's original receipt source or parent context no longer binds; its intent is retained without selecting another source.",
                            ["choice.obtain." + optional.Input.ExpectedSource.Entry + ".source"], occurrenceKey: optional.Address.Occurrence.ToString()));
                if (local.Any(c => (c.AffectedDomains & PredictorCombatDomain.Continuation) != 0)) advanced = false;
                var inventory = ObserveRouteInventory(scratch, dependencies, knownInstanceIds, scratch.Phase == PredictorPhase.Complete);
                var cards = ObserveRouteCards(scratch, dependencies, knownInstanceIds);
                var restOfferPending = room != PredictorRoomKind.RestSite ? [] :
                    policy == PredictorRoutePreviewPolicy.LiveMapDefaults
                        ? liveHatchOfferPending ?? RouteHatchPendingCauses(scratch, dependencies, knownInstanceIds)
                        : dependencies.Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                            (d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Key.Entry == "BYRDONIS_EGG")))
                            .Select(d => d.Cause).Distinct().ToImmutableArray();
                observed = RetainRouteAlternativeSources(scratch, observed, dependencies, knownInstanceIds);
                // Ledger publication can fail after recording only part of an
                // after-frame. Such a failure cannot return a trusted prefix
                // sharing that ledger; let the session discard this attempt.
                faultStage = "publication";
                nodes.Add(new(position, room, eventKnowledge?.Event ?? visit?.Model, scratch.Phase, observed,
                    room == PredictorRoomKind.RestSite ? (policy == PredictorRoutePreviewPolicy.LiveMapDefaults ? restOffers : scratch.RestOffers)
                        .Where(o => o.Action != PredictorRestAction.Hatch || restOfferPending.IsEmpty).ToImmutableArray() : [], cards,
                    CausesFor(dependencies, PredictorCombatDomain.Deck),
                    dependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray())
                { Advanced = advanced, AppliedRestActions = appliedRestActions, RestHeal = heal, RestOfferPendingCauses = restOfferPending,
                    OptionalObtainInputs = scratch._preview.Obtains.OptionalInputs.Where(input => input.Node == position.NodeId).Select(input => input.Input).ToImmutableArray(),
                    ObtainHealthEffects = scratch._preview.Obtains.HealthEffects.Where(value => value.Node == position.NodeId).Select(value => value.Effect).ToImmutableArray(),
                    PaelsToothStorage = scratch._preview.PaelsToothStorage.Values.Where(value => value.Knowledge.SourceNodeId == position.NodeId)
                        .Select(value => value.Knowledge).ToImmutableArray(),
                    RestDefaultApplied = restDefaultApplied, DefaultRestAction = defaultRestAction,
                    Shop = shop, Ancient = ancient, Event = eventKnowledge, Treasure = treasure,
                    CombatRequirements = combatRequirements, CombatCards = combatCards, CombatRelics = combatRelics,
                    RestCards = restCards, Inventory = inventory, PotionBoundaries = potionBoundaries.ToImmutable(),
                    RestActionKnowledge = restActionKnowledge.ToImmutableArray(),
                    RewardObtainChoice = scratch._sacrificePendingRewardId != null ?
                        ObserveRouteObtainChoice(scratch, dependencies, knownInstanceIds) : null });
                foreach (var cause in local) AddDependency(dependencies, new(cause));
                partial.RecordBoundary(position.NodeId, PredictorRouteFactPhase.After, partial.Capture(scratch.Working),
                    PredictorKnowledgeOrigin.Derived);
                partial.RecordResults(nodes[^1]);
                partial.RecordConsumption(position.NodeId, enteringStreams);
                if (!advanced) break;
            }
            catch (Exception error) when (isolateNodeFailures && faultStage != "publication" &&
                policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
                error is not OperationCanceledException and not OutOfMemoryException)
            {
                while (nodes.Count > completedNodes) nodes.RemoveAt(nodes.Count - 1);
                fault = new(faultPosition, faultStage, error);
                break;
            }
        }
        var frames = partial.Facts.FreezeFrames();
        var snapshot = frames.Values.FirstOrDefault()?.Snapshot ?? partial.Facts.Snapshot();
        return new(nodes.Select(n => n with { Diagnostics = frames.GetValueOrDefault(n.Position.NodeId)?.Diagnostics }).ToImmutableArray(),
            dependencies.Select(d => d.Cause).Distinct().ToImmutableArray(), nodes.Count == maximumNodes)
        { Knowledge = snapshot, Fault = fault, TerminalBoundary = terminal == null ? null : terminal with
            { Diagnostics = frames.GetValueOrDefault(terminal.TargetPosition.NodeId)?.Diagnostics } };
    }

    // Resolve authored destination identity without generating a room/map or
    // consulting a representative post-entry state. Unknown coordinates remain
    // an act-entry/route boundary, never an invented map point.
    private static PredictorPosition RouteTerminalTarget(PredictorRun run)
    {
        var map = run.Working.Maps.LastOrDefault();
        int act = map?.Act ?? run.Working.Position.Act;
        bool nextAct = map != null && !map.Visited.IsEmpty &&
            map.Map.Points.Single(p => p.Position == map.Visited[^1]).Children.Count == 0;
        if (nextAct) act++;
        if (act > 3) return new(3, run.Working.Position.Floor, "victory");
        var route = run.Routes.SingleOrDefault(r => r.Act == act);
        int next = nextAct || map == null || map.Visited.IsEmpty ? 0 : route?.Points.IndexOf(map.Visited[^1]) + 1 ?? -1;
        int floor = run.Working.Position.Floor + 1;
        if (route != null && next >= 0 && next < route.Points.Length &&
            (nextAct || map == null || map.Visited.IsEmpty || next > 0))
        {
            var point = route.Points[next];
            return new(act, floor, $"map:{act}:{point.Row}:{point.Column}:{floor}");
        }
        return new(act, floor, nextAct ? $"map:{act}:entry" : $"route-boundary:{act}:{floor}");
    }

    private static PredictorCombatPendingCause RouteCause(string code, string nodeId, PredictorCombatDomain domains,
        ImmutableArray<PredictorCombatPendingCause> parents = default, string? occurrenceKey = null,
        PredictorPendingChoice? choice = null)
        => PredictorRouteCauseEvidence.Create(code, nodeId, domains, parents, occurrenceKey, choice);

    private static ImmutableArray<PredictorRouteCardKnowledge> ObserveRouteCards(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
        => run.Working.Deck.Select(run.Working.Card).Select(card => new PredictorRouteCardKnowledge(card,
            dependencies.Where(d => d.MayChangeExistingCards && (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Id == card.Id)) ||
                !knownInstanceIds.Contains(card.Id) && (d.Cause.AffectedDomains & PredictorCombatDomain.InstanceIds) != 0)
                .Select(d => d.Cause).Concat(run._preview.Partial?.WitnessCardCauses(card) ?? [])
                .Distinct().ToImmutableArray())).ToImmutableArray();

    private static void AddDependency(List<RouteDependency> values, RouteDependency dependency)
    {
        dependency = dependency with { Cause = PredictorRouteCauseEvidence.Bind(dependency.Cause) };
        int index = values.FindIndex(d => PredictorRouteCauseEvidence.Bind(d.Cause).Identity == dependency.Cause.Identity);
        if (index < 0) values.Add(dependency);
        else values[index] = values[index] with { Cause = PredictorRouteCauseEvidence.Merge(values[index].Cause, dependency.Cause),
            PossibleCards = (values[index].PossibleCards.IsDefault ? [] : values[index].PossibleCards)
                .AddRange(dependency.PossibleCards.IsDefault ? [] : dependency.PossibleCards).Distinct().ToImmutableArray(),
            MayChangeExistingCards = values[index].MayChangeExistingCards || dependency.MayChangeExistingCards,
            ChangedRelicEntries = MergeChangedEntries(values[index].ChangedRelicEntries, dependency.ChangedRelicEntries),
            ChangedRelicStateEntries = MergeChangedEntries(values[index].ChangedRelicStateEntries, dependency.ChangedRelicStateEntries),
            ChangedCardMembershipEntries = MergeChangedEntries(values[index].ChangedCardMembershipEntries, dependency.ChangedCardMembershipEntries),
            ChangedCardStateEntries = MergeChangedEntries(values[index].ChangedCardStateEntries, dependency.ChangedCardStateEntries),
            ChangesPersonalRelicBag = MergeOptionalChange(values[index].ChangesPersonalRelicBag, dependency.ChangesPersonalRelicBag),
            ChangesSharedRelicBag = MergeOptionalChange(values[index].ChangesSharedRelicBag, dependency.ChangesSharedRelicBag),
            AnyExistingCard = values[index].AnyExistingCard || dependency.AnyExistingCard ||
                values[index].MayChangeExistingCards && values[index].PossibleCards.IsDefaultOrEmpty ||
                dependency.MayChangeExistingCards && dependency.PossibleCards.IsDefaultOrEmpty };
    }

    private static ImmutableArray<PredictorCombatPendingCause> CausesFor(List<RouteDependency> values, PredictorCombatDomain domain)
        => values.Where(d => (d.Cause.AffectedDomains & domain) != 0).Select(d => d.Cause).Distinct().ToImmutableArray();

    private static bool CanProjectNextRouteNode(PredictorRun run, List<RouteDependency> dependencies, bool includeShopsAndAncients)
    {
        if (dependencies.Any(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Continuation) != 0)) return false;
        // Stay in the already generated act. Fresh-map hooks read cards such as
        // Spoils Map; this first slice does not infer a replacement map from an
        // incomplete deck or invent a route through a different map.
        var map = run.Working.Maps.LastOrDefault();
        if (map == null || map.Visited.IsEmpty) return true;
        var point = map.Map.Points.Single(p => p.Position == map.Visited[^1]);
        if (point.Children.Count == 0 && !includeShopsAndAncients)
        {
            AddDependency(dependencies, new(RouteCause("ActBoundaryOutsideContinuationSlice", run.Working.Position.NodeId,
                PredictorCombatDomain.Continuation)));
            return false;
        }
        if (point.Children.Count == 0)
            return CanProjectNextAct(run, dependencies);
        var route = run.Routes.SingleOrDefault(r => r.Act == map.Act);
        int next = route?.Points.IndexOf(map.Visited[^1]) + 1 ?? -1;
        if (route != null && next >= route.Points.Length && run._preview.Settings.AcceptsPrefixes) return false;
        if (route == null || next <= 0 || next >= route.Points.Length || !run.IsLegalRoute(map.Map, route.Points, map.Visited,
            requireBoss: !run._preview.Settings.AcceptsPrefixes))
        {
            if (!includeShopsAndAncients) return true;
            AddDependency(dependencies, new(RouteCause("RoutePlanNeedsUpdate", run.Working.Position.NodeId,
                PredictorCombatDomain.Continuation)));
            return false;
        }
        var target = map.Map.Points.Single(p => p.Position == route.Points[next]);
        if (includeShopsAndAncients && target.Type == RolltheSpire2.Core.Prediction.Maps.MapPointType.Unknown)
        {
            // These are the actual deck readers before a ? room opens:
            // Lantern Key can change its type/identity; Dowsing advances a
            // persistent counter and may transform on this entry.
            string nextNode = $"map:{map.Act}:{target.Position.Row}:{target.Position.Column}:{run.Working.Position.Floor + 1}";
            bool lantern = map.Act == 3 && RouteCardMayVary(run, dependencies, "LANTERN_KEY");
            bool dowsing = RouteCardMayVary(run, dependencies, "DOWSING") ||
                !RouteCardStateCauses(run, dependencies, "DOWSING").IsEmpty;
            if (lantern || dowsing)
            {
                AddDependency(dependencies, new(RouteCause(lantern ? "UnknownRoomLanternKeyPending" : "UnknownRoomDowsingPending",
                    nextNode, PredictorCombatDomain.Continuation, dependencies.Where(d =>
                        RouteCardMayVary(run, [d], lantern ? "LANTERN_KEY" : "DOWSING") ||
                        !lantern && !RouteCardStateCauses(run, [d], "DOWSING").IsEmpty).Select(d => d.Cause).ToImmutableArray())));
                return false;
            }
            // Tutorial room outputs precede every random/odds/relic read.
            bool tutorialRoom = run.Context.NumberOfRuns == 0 &&
                run.RoomHistory.Count(v => v.PointType == RolltheSpire2.Core.Prediction.Maps.MapPointType.Unknown) <= 2;
            var juzuReads = RouteRelicMembershipCauses(run, dependencies, "JUZU_BRACELET");
            var compassReads = RouteRelicMembershipCauses(run, dependencies, "GOLDEN_COMPASS")
                .AddRange(RouteRelicStateCauses(run, dependencies, "GOLDEN_COMPASS"));
            var relicReads = PredictorRouteCauseEvidence.Normalize(juzuReads.AddRange(compassReads));
            bool knownLantern = map.Act == 3 && run.Working.Deck.Any(id => run.Working.Card(id).Key.Entry == "LANTERN_KEY");
            bool knownCompass = compassReads.IsEmpty && run.Working.Relics.Any(r => !r.Melted &&
                r.Key.Entry == "GOLDEN_COMPASS" && r.GoldenPathAct == map.Act - 1);
            if (!tutorialRoom && !knownLantern && !knownCompass)
            {
                var randomReads = CausesFor(dependencies, PredictorCombatDomain.UnknownMapPointRng | PredictorCombatDomain.Map);
                if (!relicReads.IsEmpty || !randomReads.IsEmpty)
                {
                    var parents = PredictorRouteCauseEvidence.Normalize(randomReads.AddRange(relicReads));
                    AddDependency(dependencies, new(PredictorRouteCauseEvidence.CreateExplicit(
                        relicReads.IsEmpty ? "UnknownRoomRandomReadsPending" : "UnknownRoomRelicReadsPending", nextNode,
                        PredictorCombatDomain.Continuation,
                        PredictorPendingReasonKind.DerivedDependency,
                        relicReads.IsEmpty
                            ? "The random room output reads the UnknownMapPoint cursor and captured unknown-room odds."
                            : "The room output depends on Juzu Bracelet membership or Golden Compass membership and active act.",
                        ["rng.UnknownMapPoint.state", "odds.unknownRoom", "relics.JUZU_BRACELET.membership",
                            "relics.GOLDEN_COMPASS.membership", "relics.GOLDEN_COMPASS.goldenPathAct"], parents)));
                    return false;
                }
            }
        }
        if (target.Type is not (RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster or
            RolltheSpire2.Core.Prediction.Maps.MapPointType.Elite or RolltheSpire2.Core.Prediction.Maps.MapPointType.Boss or
            RolltheSpire2.Core.Prediction.Maps.MapPointType.RestSite) &&
            !(includeShopsAndAncients && target.Type is RolltheSpire2.Core.Prediction.Maps.MapPointType.Shop or
                RolltheSpire2.Core.Prediction.Maps.MapPointType.Ancient or RolltheSpire2.Core.Prediction.Maps.MapPointType.Unknown or
                RolltheSpire2.Core.Prediction.Maps.MapPointType.Treasure))
        {
            string nextNodeId = $"map:{map.Act}:{target.Position.Row}:{target.Position.Column}:{run.Working.Position.Floor + 1}";
            AddDependency(dependencies, new(RouteCause("RouteNodeOutsideContinuationSlice", nextNodeId,
                PredictorCombatDomain.Continuation)));
            return false;
        }
        return true;
    }

    private static bool RouteCardMayVary(PredictorRun run, List<RouteDependency> dependencies, string entry)
    {
        bool present = run.Working.Deck.Any(id => run.Working.Card(id).Key.Entry == entry);
        return dependencies.Any(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
            (!d.ChangedCardMembershipEntries.IsDefault ? d.ChangedCardMembershipEntries.Contains(entry) :
            d.Cause.Code != "PartialUpgradeTargets" && (d.PossibleCards.IsDefaultOrEmpty && (!d.MayChangeExistingCards || present) ||
             !d.PossibleCards.IsDefaultOrEmpty && d.PossibleCards.Any(c => c.Key.Entry == entry) ||
             d.AnyExistingCard && present)));
    }

    private static bool CanProjectNextAct(PredictorRun run, List<RouteDependency> dependencies)
    {
        int act = run.Working.Maps[^1].Act + 1;
        if (act > 3) return false;
        string node = $"map:{act}:entry";
        bool possibleSpoils = dependencies.Any(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
            (d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Key.Entry == "SPOILS_MAP")));
        if (possibleSpoils || !CausesFor(dependencies, PredictorCombatDomain.Map | PredictorCombatDomain.Relics).IsEmpty)
        {
            AddDependency(dependencies, new(RouteCause("ActMapPending", node, PredictorCombatDomain.Map | PredictorCombatDomain.Continuation,
                CausesFor(dependencies, PredictorCombatDomain.Map | PredictorCombatDomain.Relics).AddRange(dependencies.Where(d =>
                    (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                    (d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Key.Entry == "SPOILS_MAP"))).Select(d => d.Cause)))));
            return false;
        }
        if (run.Context.HasRunModifier("CURSED_RUN") && !CausesFor(dependencies, PredictorCombatDomain.NicheRng).IsEmpty)
        {
            AddDependency(dependencies, new(RouteCause("ActEntryCardPending", node, PredictorCombatDomain.Continuation,
                CausesFor(dependencies, PredictorCombatDomain.NicheRng))));
            return false;
        }
        var route = run.Routes.FirstOrDefault(r => r.Act == act);
        if (route == null || !run.IsLegalRoute(run.RouteMap(act), route.Points, [], requireBoss: !run._preview.Settings.AcceptsPrefixes))
        {
            AddDependency(dependencies, new(RouteCause("NextActRouteRequired", node, PredictorCombatDomain.Continuation)));
            return false;
        }
        return true;
    }

    private static ImmutableArray<PredictorCombatRewardKnowledge> ObserveRouteRewards(PredictorRun run,
        List<RouteDependency> dependencies, ImmutableArray<PredictorCombatRewardKnowledge> source = default)
    {
        if (source.IsDefault && run._eventCombatAwaitRewards)
            source = run._preview.Event.CombatRewardKnowledge;
        var result = ImmutableArray.CreateBuilder<PredictorCombatRewardKnowledge>();
        for (int index = 0; index < run.Rewards.Length; index++)
        {
            var reward = run.Rewards[index];
            run._preview.Partial?.ObserveRewardSourceIds(reward);
            var domains = reward.Kind switch
            {
                PredictorRewardKind.Card => PredictorCombatDomain.RewardsRng | PredictorCombatDomain.CardRewards,
                PredictorRewardKind.Gold => PredictorCombatDomain.RewardsRng | (reward.WasGoldStolenBack ? PredictorCombatDomain.GoldRewards : PredictorCombatDomain.None),
                PredictorRewardKind.Potion => PredictorCombatDomain.RewardsRng | PredictorCombatDomain.PotionRewards,
                PredictorRewardKind.Relic => PredictorCombatDomain.RewardsRng | PredictorCombatDomain.RelicRewards,
                PredictorRewardKind.SpecialCard => PredictorCombatDomain.ReturnedCards,
                _ => PredictorCombatDomain.None
            };
            if (reward.Kind == PredictorRewardKind.Card && reward.RerollRecipe?.IsFromCombat != true)
                domains |= PredictorCombatDomain.ExtraCardRewards;
            if (reward.Kind == PredictorRewardKind.Gold) domains |= PredictorCombatDomain.ExtraGoldRewards;
            if (reward.Kind == PredictorRewardKind.RemoveCard) domains |= PredictorCombatDomain.RemoveCardRewards;
            if (reward.Kind == PredictorRewardKind.Card) domains |= PredictorCombatDomain.CardRewardHookState;
            domains = RouteEventRewardDomains(run, reward, domains);
            var rewardDependencies = EventCombatRewardDependencies(run, reward, dependencies);
            var inherited = CausesFor(rewardDependencies, domains);
            if (reward.Kind == PredictorRewardKind.Card && CombatCardReadsRarityOffset(run.Context, reward))
                inherited = inherited.AddRange(rewardDependencies.Select(d => d.Cause).Where(cause => CombatOddsCauseApplies(cause, "odds.cardRarity")));
            if (reward.Kind == PredictorRewardKind.Gold)
                inherited = inherited.Where(cause => cause.Code != "CombatPotionDropConsumptionPending" ||
                    cause.NodeId != run.Working.Position.NodeId).ToImmutableArray();
            if (reward.Kind == PredictorRewardKind.Card && reward.RerollRecipe?.IsFromCombat == true)
                inherited = inherited.Where(c => c.Code != "ExtraRewardOrder").ToImmutableArray();
            string? sourceIdentity = run._preview.Partial?.CombatRewardSourceIdentity(run.Working.Position.NodeId, reward);
            var prior = !source.IsDefault ? sourceIdentity != null
                ? source.FirstOrDefault(candidate => candidate.SourceIdentity == sourceIdentity)
                : index < source.Length && source[index].SourceIdentity == null ? source[index] : null : null;
            // Partial card options prove identity; their pending causes belong
            // to enchantments, not the underlying Rewards-generated offer.
            var causes = inherited.AddRange(prior is { CardOptions.IsEmpty: true } ? prior.PendingCauses : [])
                .Distinct().ToImmutableArray();
            bool dropUncertain = dependencies.Any(dependency => dependency.Cause.Code == "CombatPotionDropConsumptionPending" &&
                dependency.Cause.NodeId == run.Working.Position.NodeId);
            var observation = reward.Kind == PredictorRewardKind.Card
                ? CardRewardKnowledge(reward, causes, CausesFor(dependencies, PredictorCombatDomain.NicheRng)
                    .AddRange(prior?.CardOptions.SelectMany(option => option.EnchantmentPendingCauses) ?? []))
                : new PredictorCombatRewardKnowledge(reward.Kind, causes.IsEmpty ? DisplayReward(reward) : null, causes,
                    UnknownMultiplicity: reward.Kind == PredictorRewardKind.Potion && dropUncertain);
            result.Add(WithRouteRewardResult(run, reward, observation with
            { SourceIdentity = sourceIdentity, Occurrence = run.Rewards.Take(index).Count(priorReward => priorReward.Kind == reward.Kind &&
                priorReward.Origin == reward.Origin && priorReward.Key == reward.Key) }, dependencies));
        }
        if (!source.IsDefault)
            result.AddRange(source.Where(r => r.UnknownMultiplicity &&
                (r.SourceIdentity != null ? !result.Any(current => current.SourceIdentity == r.SourceIdentity) :
                    source.IndexOf(r) >= run.Rewards.Length) &&
                !r.PendingCauses.Any(c => (c.AffectedDomains & (PredictorCombatDomain.ExtraCardRewards |
                    PredictorCombatDomain.ExtraGoldRewards | PredictorCombatDomain.RemoveCardRewards)) != 0)));
        var drop = dependencies.Where(dependency => dependency.Cause.Code == "CombatPotionDropConsumptionPending" &&
            dependency.Cause.NodeId == run.Working.Position.NodeId).SelectMany(dependency => dependency.Cause.Parents).ToImmutableArray();
        if (!drop.IsEmpty && !result.Any(reward => reward.Kind == PredictorRewardKind.Potion && reward.UnknownMultiplicity))
            result.Add(new(PredictorRewardKind.Potion, null, PredictorRouteCauseEvidence.Normalize(drop), UnknownMultiplicity: true)
            { SourceIdentity = run._preview.Partial?.CombatPlaceholderIdentity(run.Working.Position.NodeId, "potion-drop-existence") });
        foreach (var domain in new[] { PredictorCombatDomain.ExtraCardRewards, PredictorCombatDomain.ExtraGoldRewards, PredictorCombatDomain.RemoveCardRewards })
        {
            var causes = CausesFor(dependencies, domain);
            if (!causes.IsEmpty) result.Add(new(domain == PredictorCombatDomain.ExtraCardRewards ? PredictorRewardKind.Card :
                domain == PredictorCombatDomain.ExtraGoldRewards ? PredictorRewardKind.Gold : PredictorRewardKind.RemoveCard,
                null, causes, UnknownMultiplicity: true));
        }
        result.AddRange(CompletedLiveDefaultCardRewards(run, dependencies));
        return result.ToImmutable();
    }

    private static PredictorReward DisplayReward(PredictorReward reward) => reward with
    {
        Id = 0, ContentInstanceId = null, SubscriptionOrder = 0,
        CardRewardNicheScope = null,
        Cards = reward.Cards.Select(c => c with { Id = 0 }).ToImmutableArray()
    };

    private static bool SameOffer(PredictorReward actual, PredictorReward expected)
    {
        var a = DisplayReward(actual) with { Status = PredictorRewardStatus.Open, CardSelectionEntered = false };
        var b = DisplayReward(expected) with { Status = PredictorRewardStatus.Open, CardSelectionEntered = false };
        return a.Kind == b.Kind && a.Origin == b.Origin && a.Key == b.Key && a.GoldAmount == b.GoldAmount &&
            a.IsWaxRelic == b.IsWaxRelic && a.PreserveCardInstance == b.PreserveCardInstance &&
            a.WasGoldStolenBack == b.WasGoldStolenBack && a.GoldAmountUnspecified == b.GoldAmountUnspecified &&
            a.GoldNetRestored == b.GoldNetRestored && a.CanSkip == b.CanSkip && a.CanReroll == b.CanReroll &&
            a.HasRerolled == b.HasRerolled && a.ManuallySetCards == b.ManuallySetCards && a.Cards.SequenceEqual(b.Cards);
    }

    private static bool ApplyRouteRewardChoices(PredictorRun run, PredictorPlannedNode plan,
        List<RouteDependency> dependencies, HashSet<int> used, HashSet<long> skipped,
        List<PredictorCombatPendingCause> local, CancellationToken token, HashSet<long>? knownInstanceIds = null)
    {
        if (run._preview.Rewards.CompletedEdit?.Prefix.NodeId != plan.NodeId || !plan.RewardChoices.IsEmpty)
            ConfigureCompletedRewardEdits(run, plan, PredictorRewardEditOwner.Reward, 0);
        CaptureCompletedCardRewardEdits(run, dependencies);
        knownInstanceIds ??= run._checkpoints[^1].State.Cards.Select(c => c.Id)
            .Concat(run._checkpoints[^1].State.Relics.Select(r => r.Id)).ToHashSet();
        if (CausesFor(dependencies, PredictorCombatDomain.InstanceIds).IsEmpty)
            knownInstanceIds.UnionWith(run.Working.Cards.Select(c => c.Id).Concat(run.Working.Relics.Select(r => r.Id)));
        for (int index = 0; index < plan.RewardChoices.Length; index++)
        {
            if (used.Contains(index)) continue;
            var choice = plan.RewardChoices[index];
            if (choice == null || choice.ExpectedReward == null || choice.Occurrence < 0)
                throw new ArgumentException("PredictorRouteRewardChoiceInvalid");
            var identity = run.Rewards.Where(r => r.Kind == choice.ExpectedReward.Kind && r.Origin == choice.ExpectedReward.Origin &&
                r.Key == choice.ExpectedReward.Key).ToArray();
            if (!choice.Children.IsEmpty)
            {
                // The captured caller may already have accepted this parent
                // receipt/sacrifice and be inside its actual child. Bind to that
                // suspended source; never replay its obtain or sacrifice again.
                var suspended = run._obtainFrames.FirstOrDefault(frame =>
                    frame.Destination is PredictorObtainReturn.Reward or PredictorObtainReturn.RewardAlternative &&
                    (frame.Destination == PredictorObtainReturn.Reward ?
                        choice.ExpectedReward.Kind == PredictorRewardKind.Relic && !choice.Skip :
                        choice.SacrificeRelicId is { } wing && run._inputs.Any(input => input.Action == "PaelsWingSacrifice" &&
                            input.ObjectId == wing && input.Targets.Contains(frame.SourceId))) &&
                    frame.ParentRewards.Where(reward => reward.Kind == choice.ExpectedReward.Kind &&
                        reward.Origin == choice.ExpectedReward.Origin && reward.Key == choice.ExpectedReward.Key)
                        .ElementAtOrDefault(choice.Occurrence) is { } parent && parent.Id == frame.SourceId &&
                        SameRouteRewardOffer(run, parent, choice, dependencies));
                if (suspended != null)
                {
                    int parentDepth = run._obtainFrames.IndexOf(suspended);
                    var parent = suspended.ParentRewards.Single(reward => reward.Id == suspended.SourceId);
                    RecordRouteRewardChoice(run, parent, choice, dependencies);
                    if (!ApplyPlannedRewardChildren(run, parent, choice, dependencies, knownInstanceIds, local, token)) return false;
                    ContinueLiveDefaultCardRewardChildren(run, dependencies, token, parentDepth);
                    used.Add(index);
                    continue;
                }
            }
            if (identity.Length == 0 && run.Working.Visits.LastOrDefault()?.Room == PredictorRoomKind.RestSite) continue;
            if (choice.Occurrence >= identity.Length || !SameRouteRewardOffer(run, identity[choice.Occurrence], choice, dependencies))
            {
                local.Add(RouteCause("PlannedRewardChanged", plan.NodeId, PredictorCombatDomain.Continuation,
                    occurrenceKey: PredictorRouteCauseEvidence.Key("planned-reward", index.ToString(), choice.Occurrence.ToString())));
                return false;
            }
            var reward = identity[choice.Occurrence];
            if (reward.Status != PredictorRewardStatus.Open) { used.Add(index); continue; }
            // Taking a relic can replace Rewards with its child transaction.
            // Capture its origin while the source occurrence is still present.
            var childCause = RewardChoiceCause(run, reward, "RewardChildChoice", PredictorCombatDomain.Continuation, dependencies);
            int rewardIndex = run.Rewards.IndexOf(reward);
            if (!RouteRewardOperationKnown(run, ObserveRouteRewards(run, dependencies)[rewardIndex], choice))
            {
                local.Add(RewardChoiceCause(run, reward, "PlannedRewardPending", PredictorCombatDomain.Continuation, dependencies));
                return false;
            }
            var originalReward = reward;
            var originalChoice = choice;
            // A preceding reroll-only choice leaves an open picker. Its real
            // navigation must finish before a second validated opportunity can
            // enter its own picker; no Skip or receipt is implied here.
            if (run.Selection.CardRewardId is { } selectedId && selectedId != reward.Id)
                ExitLiveDefaultRerollSelection(run, token);
            var alternative = ApplyRouteRewardAlternative(run, reward, choice, dependencies,
                knownInstanceIds, local, token, out reward, out choice);
            if (alternative is { } appliedAlternative)
            {
                if (!appliedAlternative) return false;
                used.Add(index);
                if (run.Phase == PredictorPhase.RestSite) return true;
                continue;
            }
            if (choice.Skip)
            {
                if (!reward.CanSkip || reward.Mandatory)
                { local.Add(RewardChoiceCause(run, reward, "PlannedRewardPending", PredictorCombatDomain.Continuation, dependencies)); return false; }
                if (reward.Kind == PredictorRewardKind.Card)
                {
                    if (!reward.CardSelectionEntered) run.Submit(run.Request!, new EnterCardSelection(reward.Id), token);
                    if (run.Submit(run.Request!, new SkipCardReward(), token) != PredictorInputResult.Accepted)
                    { local.Add(RewardChoiceCause(run, reward, "PlannedRewardPending", PredictorCombatDomain.Continuation, dependencies)); return false; }
                }
                // Non-card skips are settled by the ordinary LeaveRewards or
                // LeaveObtainChild action below, preserving mandatory checks.
                skipped.Add(reward.Id);
            }
            else
            {
                if (reward.Kind == PredictorRewardKind.Potion && !CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty)
                {
                    local.Add(RewardChoiceCause(run, reward, "PlannedPotionReceiptPending", PredictorCombatDomain.Continuation, dependencies,
                        CausesFor(dependencies, PredictorCombatDomain.PotionInventory)));
                    return false;
                }
                if (reward.Kind == PredictorRewardKind.Relic &&
                    !ObtainCauses(run, reward.Key, dependencies).IsEmpty)
                {
                    // A manual random/choice obtain can enumerate or mutate the
                    // unresolved deck. Keep its known offered identity visible,
                    // but do not publish the witness's selected card effects.
                    local.Add(RewardChoiceCause(run, reward, "PlannedRelicObtainDependent", PredictorCombatDomain.Continuation, dependencies,
                        ObtainCauses(run, reward.Key, dependencies)));
                    return false;
                }
                long? cardId = null;
                if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
                {
                    if (choice.CardIndex is not { } selectedIndex || selectedIndex < 0 || selectedIndex >= reward.Cards.Length)
                    {
                        local.Add(RewardChoiceCause(run, reward, "PlannedRewardChanged", PredictorCombatDomain.Continuation, dependencies));
                        return false;
                    }
                    cardId = reward.Cards[selectedIndex].Id;
                    if (reward.Kind == PredictorRewardKind.Card && !reward.CardSelectionEntered)
                        run.Submit(run.Request!, new EnterCardSelection(reward.Id), token);
                }
                int childDepth = run._obtainFrames.Length;
                if (run.Submit(run.Request!, new TakeReward(reward.Id, reward.Kind == PredictorRewardKind.Card ? cardId : null), token) != PredictorInputResult.Accepted)
                {
                    local.Add(RewardChoiceCause(run, reward, "PlannedRewardPending", PredictorCombatDomain.Continuation, dependencies));
                    return false;
                }
                RecordRouteRewardChoice(run, originalReward, originalChoice, dependencies);
                if (run._obtainFrames.Length > childDepth &&
                    !ApplyPlannedRewardChildren(run, originalReward, originalChoice, dependencies, knownInstanceIds, local, token)) return false;
                ContinueLiveDefaultCardRewardChildren(run, dependencies, token, childDepth);
                if (run._obtainFrames.Length > childDepth)
                { local.Add(childCause); return false; }
            }
            RecordRouteRewardChoice(run, originalReward, originalChoice, dependencies);
            used.Add(index);
            if (run.Phase == PredictorPhase.RestSite) return true;
            if (run.Phase != PredictorPhase.Rewards)
            {
                local.Add(childCause);
                return false;
            }
        }
        if (run.AutomaticRewardPickup)
        {
            ExitLiveDefaultRerollSelection(run, token);
            var pendingReceipts = new HashSet<long>();
            while (PredictorRewardPickupPolicy.Plan(run.Working, run.Rewards.Where(r => !skipped.Contains(r.Id) && !pendingReceipts.Contains(r.Id) &&
                !IsLiveDefaultPotionOmission(run, r, dependencies)).ToImmutableArray(),
                run.Phase, run.Selection.CardRewardId != null,
                includeOrdinaryNonCombatRelics: run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults,
                includeNonCombatPotions: run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults,
                liveMapDefaults: run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults,
                context: run.Context).NextInput is TakeReward take)
            {
                int index = run.Rewards.FindIndex(r => r.Id == take.RewardId);
                var reward = run.Rewards[index];
                var childCause = RewardChoiceCause(run, reward, "RewardChildChoice", PredictorCombatDomain.Continuation, dependencies);
                if (!ObserveRouteRewards(run, dependencies)[index].IsKnown)
                { pendingReceipts.Add(reward.Id); continue; }
                if (reward.Kind == PredictorRewardKind.Potion && !CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty)
                {
                    pendingReceipts.Add(reward.Id);
                    local.Add(RewardChoiceCause(run, reward, "PotionReceiptPending", PredictorCombatDomain.PotionInventory, dependencies,
                        CausesFor(dependencies, PredictorCombatDomain.PotionInventory)));
                    continue;
                }
                if (reward.Kind == PredictorRewardKind.Relic && !ObtainCauses(run, reward.Key, dependencies).IsEmpty)
                { pendingReceipts.Add(reward.Id); continue; }
                int childDepth = run._obtainFrames.Length;
                if (run.Submit(run.Request!, take, token) != PredictorInputResult.Accepted) break;
                ContinueLiveDefaultCardRewardChildren(run, dependencies, token, childDepth);
                if (run.Phase == PredictorPhase.RestSite) return true;
                if (run.Phase != PredictorPhase.Rewards || run._obtainFrames.Length > childDepth)
                {
                    local.Add(childCause);
                    return false;
                }
            }
        }
        ApplyLiveDefaultCardSkips(run, dependencies, skipped, token);
        return true;
    }

    private static bool ContinueRouteRestRewards(PredictorRun run, PredictorPlannedNode plan,
        List<RouteDependency> dependencies, HashSet<int> used, HashSet<long> skipped,
        List<PredictorCombatPendingCause> local, CancellationToken token, HashSet<long> knownInstanceIds)
    {
        if (!ApplyRouteRewardChoices(run, plan, dependencies, used, skipped, local, token, knownInstanceIds)) return false;
        if (run.Phase == PredictorPhase.RestSite) return true;
        if (run.Phase != PredictorPhase.Rewards ||
            run.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && !skipped.Contains(r.Id)) && !plan.LeaveRewards &&
            !ContinuePendingRestRewardWitness(run, dependencies, skipped))
        { local.Add(RouteCause("RestRewardChoice", plan.NodeId, PredictorCombatDomain.Continuation)); return false; }
        if (run.Selection.CardRewardId != null) run.Submit(run.Request!, new ExitCardSelection(), token);
        if (run.Submit(run.Request!, new LeaveRewards(), token) != PredictorInputResult.Accepted || run.Phase != PredictorPhase.RestSite)
        { local.Add(RouteCause("RestChildChoice", plan.NodeId, PredictorCombatDomain.Continuation)); return false; }
        return true;
    }

    private static void AddPendingReward(PredictorRun run, PredictorReward reward, List<RouteDependency> dependencies)
    {
        if (IsLiveDefaultPotionOmission(run, reward, dependencies))
        {
            RegisterLiveDefaultPotionOmission(run, reward, dependencies);
            return;
        }
        var domains = reward.Kind switch
        {
            PredictorRewardKind.Card or PredictorRewardKind.SpecialCard or PredictorRewardKind.RemoveCard => PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds,
            PredictorRewardKind.Gold => PredictorCombatDomain.Gold,
            PredictorRewardKind.Potion => PredictorCombatDomain.PotionInventory | PredictorCombatDomain.InstanceIds,
            _ => PredictorCombatDomain.Continuation | PredictorCombatDomain.Relics
        };
        var cards = reward.Cards;
        // A shifted Rewards cursor/rarity state changes card identity. The
        // witness's three options cannot stand in for that unknown pool.
        if (reward.Kind == PredictorRewardKind.Card && !CausesFor(dependencies,
                PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Odds).IsEmpty)
        {
            if (reward.RerollRecipe is { } recipe)
            {
                var keys = PredictorRewardGeneration.ResolveCardPool(run.Context, run.Working, ref recipe, isCardReward: true)
                    .Select(c => c.CardKey).ToHashSet();
                cards = run.Context.Catalog!.CardDefinitions.Where(d => keys.Contains(d.Prototype.Key)).Select(d => d.Prototype).ToImmutableArray();
            }
            else cards = run.Context.Catalog!.CardDefinitions.Select(d => d.Prototype).ToImmutableArray();
        }
        if (reward.Kind == PredictorRewardKind.RemoveCard)
            cards = run.Working.Deck.Select(run.Working.Card).Where(c => c.Removable).ToImmutableArray();
        if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
        {
            // AddCard hooks: Lucky Fysh changes gold, Darkstone/Book health,
            // Bing Bong/Hoarder copies and Book's counter stay deck-local.
            if (PredictorSettlementEffects.Has(run.Working, "LUCKY_FYSH")) domains |= PredictorCombatDomain.Gold;
            if (PredictorSettlementEffects.Has(run.Working, "DARKSTONE_PERIAPT") || PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS"))
                domains |= PredictorCombatDomain.Health;
        }
        if (reward.Kind == PredictorRewardKind.Gold && PredictorSettlementEffects.Has(run.Working, "DRAGON_FRUIT")) domains |= PredictorCombatDomain.Health;
        var alternatives = PendingRewardAlternatives(run, reward, dependencies);
        domains |= alternatives.Domains;
        cards = cards.AddRange(alternatives.PossibleCards).Distinct().ToImmutableArray();
        var cause = RewardChoiceCause(run, reward, "Pending" + reward.Kind + "Choice", domains, dependencies, alternatives.ReadCauses);
        if (!alternatives.Options.IsEmpty)
            cause = cause with { RequiredPropertyKeys = cause.RequiredPropertyKeys
                .AddRange(alternatives.Options.Any(option => option.OptionId.StartsWith("reroll:", StringComparison.Ordinal))
                    ? ImmutableArray.Create("choice.reward.reroll") : ImmutableArray<string>.Empty)
                .AddRange(alternatives.Options.Any(option => option.OptionId.StartsWith("sacrifice:", StringComparison.Ordinal))
                    ? ImmutableArray.Create("choice.reward.sacrifice") : ImmutableArray<string>.Empty) };
        bool offerKnown = ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(reward)].IsKnown;
        AddDependency(dependencies, new(cause, cards, reward.Kind == PredictorRewardKind.RemoveCard || alternatives.ChangesExistingDeck));
        run._preview.Partial?.PendingReward(run, reward, cause, offerKnown, alternatives);
    }

    private static PredictorCombatPendingCause RewardChoiceCause(PredictorRun run, PredictorReward reward,
        string code, PredictorCombatDomain domains, List<RouteDependency> dependencies,
        ImmutableArray<PredictorCombatPendingCause> additionalParents = default)
    {
        int index = run.Rewards.IndexOf(reward);
        if (index < 0) throw new InvalidOperationException("PredictorCauseRewardOccurrenceMissing");
        int occurrence = run.Rewards.Take(index).Count(prior => prior.Kind == reward.Kind &&
            prior.Origin == reward.Origin && prior.Key == reward.Key);
        var choice = PredictorRouteCauseEvidence.RewardChoice(run.Working.Position.NodeId, reward, occurrence);
        var parents = ObserveRouteRewards(run, dependencies)[index].PendingCauses
            .AddRange(additionalParents.IsDefault ? [] : additionalParents);
        return RouteCause(code, run.Working.Position.NodeId, domains, parents, choice.Identity, choice);
    }

    private static void PropagateDeckCombatReaders(PredictorRun run, PredictorCombatNodeOverrides? combat,
        List<RouteDependency> dependencies, List<RouteDependency> afterGeneration)
    {
        foreach (var dependency in dependencies.ToArray())
        {
            if ((dependency.Cause.AffectedDomains & PredictorCombatDomain.Deck) == 0) continue;
            var possible = dependency.PossibleCards.IsDefaultOrEmpty || dependency.AnyExistingCard
                ? run.Working.Deck.Select(run.Working.Card).Concat(dependency.PossibleCards.IsDefault ? [] : dependency.PossibleCards).Distinct().ToArray()
                : dependency.PossibleCards.ToArray();
            bool ReadsCard(PredictorCard card, PredictorDeckUncertainFact fields) =>
                RouteCardMayVary(run, [dependency], card.Key.Entry) ||
                dependency.MayChangeExistingCards && (dependency.DeckFacts & fields) != 0 &&
                (dependency.AffectedCardIds.IsDefaultOrEmpty || dependency.AffectedCardIds.Contains(card.Id)) &&
                (dependency.AffectedCardTypes.IsDefaultOrEmpty || dependency.AffectedCardTypes.Contains(card.Type));
            bool Active(string key, PredictorDeckUncertainFact fields = PredictorDeckUncertainFact.Definition) =>
                possible.Any(card => card.Key.Entry == key &&
                    (combat?.CardTriggers.FirstOrDefault(trigger => trigger.InstanceId == card.Id)?.EffectiveTriggers ??
                        run.CombatDefaults.CardTriggers(card.Key)) > 0 && ReadsCard(card, fields));
            var domains = PredictorCombatDomain.None;
            if (Active("HAND_OF_GREED", PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Upgrade)) domains |= PredictorCombatDomain.Gold;
            if (Active("FEED", PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Upgrade) || Active("BRIGHTEST_FLAME")) domains |= PredictorCombatDomain.Health;
            if (Active("ALCHEMIZE")) domains |= PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.PotionInventory;
            if (Active("THE_HUNT")) domains |= PredictorCombatDomain.ExtraCardRewards;
            if (Active("ROYALTIES", PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Upgrade)) domains |= PredictorCombatDomain.ExtraGoldRewards;
            if (Active("FORBIDDEN_GRIMOIRE")) domains |= PredictorCombatDomain.RemoveCardRewards;
            var entry = run._eventCombatEntry ?? run._combatEntry;
            bool randomUpgrade = possible.Any(c => c.UpgradeLevel < c.MaxUpgradeLevel &&
                ReadsCard(c, PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Upgrade)) &&
                (entry?.Kind == PredictorCombatKind.Elite && PredictorSettlementEffects.Has(run.Working, "WAR_HAMMER") ||
                entry?.Encounter.IsMonsterEncounter == true && run.Working.Relics.Any(r => !r.Melted && r.Key.Entry == "FISHING_ROD" && (r.FishingRodCombatsSeen + 1) % 3 == 0));
            if (randomUpgrade) domains |= PredictorCombatDomain.NicheRng | PredictorCombatDomain.Deck;
            if (domains != PredictorCombatDomain.None)
                AddDependency(dependencies, dependency with { Cause = dependency.Cause with { AffectedDomains = domains },
                    MayChangeExistingCards = dependency.MayChangeExistingCards || randomUpgrade,
                    PossibleCards = randomUpgrade ? run.Working.Deck.Select(run.Working.Card).ToImmutableArray() : dependency.PossibleCards });
            // Hunt/Royalties are appended after ordinary original rewards.
            // They leave this node's base offers independent, then change the
            // subsequent Rewards cursor/rarity offset. Do not hide base gold.
            if (Active("THE_HUNT") || Active("ROYALTIES"))
                afterGeneration.Add(dependency with { Cause = dependency.Cause with { AffectedDomains = PredictorCombatDomain.RewardsRng |
                    (Active("THE_HUNT") ? PredictorCombatDomain.Odds : PredictorCombatDomain.None) } });
        }
    }

    private static bool ApplyRouteRestAction(PredictorRun run, PredictorPlannedRestAction step,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local,
        CancellationToken token, bool authored = true)
    {
        if (step == null) throw new ArgumentException("PredictorRouteRestPlanInvalid");
        var previous = run._restPlanTrace;
        var trace = run._preview.Partial?.BeginRestPlan(run, step, authored);
        run._restPlanTrace = trace;
        int before = local.Count;
        try
        {
            bool applied = ApplyRouteRestActionCore(run, step, dependencies, knownInstanceIds, local, token);
            trace?.PlanReturn(applied, local.Skip(before).ToImmutableArray());
            return applied;
        }
        finally { run._restPlanTrace = previous; }
    }

    private static bool ApplyRouteRestActionCore(PredictorRun run, PredictorPlannedRestAction step,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (step == null || step.CookCards.IsDefault) throw new ArgumentException("PredictorRouteRestPlanInvalid");
        // An exhausted action cannot become executable by supplying a card.
        // Report the native option's absence before asking for target facts.
        if (run.Phase == PredictorPhase.RestSite && !run.RestOptions.Contains(step.Action))
        {
            local.Add(RouteCause("PlannedRestActionUnavailable", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            return false;
        }
        bool KnownTarget(PredictorPlannedCard? target)
        {
            bool known = target != null && IsKnownRouteDeckTarget(run, target, dependencies, knownInstanceIds,
                step.Action == PredictorRestAction.Smith ? PredictorDeckProjectionScope.Upgradable : PredictorDeckProjectionScope.Removable,
                step.Action == PredictorRestAction.Smith ? RouteLevelEditFacts : PredictorDeckUncertainFact.Definition);
            run._restPlanTrace?.BindRestTarget(run, target, known, known ? [] :
                RouteDeckTargetCauses(run, target == null ? [] : [target], dependencies, knownInstanceIds,
                    step.Action == PredictorRestAction.Smith ? PredictorDeckProjectionScope.Upgradable : PredictorDeckProjectionScope.Removable,
                    step.Action == PredictorRestAction.Smith ? RouteLevelEditFacts : PredictorDeckUncertainFact.Definition));
            return known;
        }
        if (step.Action == PredictorRestAction.Smith && !KnownTarget(step.SmithCard) ||
            step.Action == PredictorRestAction.Cook && (step.CookCards.Length != 2 || step.CookCards.Any(c => !KnownTarget(c))))
        {
            if (TryProjectLiveRestTarget(run, step, dependencies, token)) return true;
            local.Add(RouteCause("PlannedRestTargetPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                RouteDeckTargetCauses(run, step.Action == PredictorRestAction.Smith ?
                    (step.SmithCard == null ? [] : [step.SmithCard]) : step.CookCards, dependencies, knownInstanceIds,
                    step.Action == PredictorRestAction.Smith ? PredictorDeckProjectionScope.Upgradable : PredictorDeckProjectionScope.Removable,
                    step.Action == PredictorRestAction.Smith ? RouteLevelEditFacts : PredictorDeckUncertainFact.Definition)));
            return false;
        }
        var restDeckCauses = run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults
            ? step.Action switch
            {
                PredictorRestAction.Hatch => RouteHatchPendingCauses(run, dependencies, knownInstanceIds),
                PredictorRestAction.Clone => RouteClonePendingCauses(run, dependencies, knownInstanceIds),
                _ => CausesFor(dependencies, PredictorCombatDomain.Deck)
            } : CausesFor(dependencies, PredictorCombatDomain.Deck);
        if (step.Action is PredictorRestAction.Clone or PredictorRestAction.Hatch && !restDeckCauses.IsEmpty)
        {
            local.Add(RouteCause("RestDeckDependentAction", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                restDeckCauses));
            return false;
        }
        if (step.Action == PredictorRestAction.Dig)
        {
            if (!CausesFor(dependencies, PredictorCombatDomain.RewardsRng).IsEmpty)
            {
                local.Add(RouteCause("RestRelicRngPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                    CausesFor(dependencies, PredictorCombatDomain.RewardsRng)));
                return false;
            }
            if (!CausesFor(dependencies, PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng).IsEmpty)
            {
                var witness = run.Working;
                var key = PredictorRewardGeneration.PullRelic(run.Context, ref witness);
                if (!ObtainCauses(run, key, dependencies).IsEmpty)
                {
                    local.Add(RouteCause("RestObtainDependent", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                        ObtainCauses(run, key, dependencies)));
                    return false;
                }
            }
        }
        if (run.Phase == PredictorPhase.RestSite && run.Submit(run.Request!, new SelectRestAction(step.Action), token) != PredictorInputResult.Accepted)
        {
            local.Add(RouteCause("PlannedRestActionUnavailable", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            return false;
        }
        if (run.Phase == PredictorPhase.SmithCards && step.Action == PredictorRestAction.Smith)
        {
            if (run.Submit(run.Request!, new SelectSmithCard(step.SmithCard!.InstanceId), token) != PredictorInputResult.Accepted)
            { local.Add(RouteCause("PlannedRestTargetPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation)); return false; }
        }
        else if (run.Phase == PredictorPhase.RestCookCards && step.Action == PredictorRestAction.Cook)
        {
            if (run.Submit(run.Request!, new SelectCookCards(step.CookCards.Select(c => c.InstanceId).ToImmutableArray()), token) != PredictorInputResult.Accepted)
            { local.Add(RouteCause("PlannedRestTargetPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation)); return false; }
        }
        else if (run.Phase == PredictorPhase.RestHealFact && step.Action == PredictorRestAction.Heal)
        {
            if (step.MaximumHp == null && TryProjectLiveRestHeal(run, dependencies, token)) return true;
            if (step.MaximumHp is not > 0 || run.Submit(run.Request!, new ProvideRestMaxHpFact(step.MaximumHp.Value), token) != PredictorInputResult.Accepted)
            { local.Add(RouteCause("RestMaximumHp", run.Working.Position.NodeId, PredictorCombatDomain.Continuation | PredictorCombatDomain.Health)); return false; }
        }
        return true;
    }

    // Dependency reads of the implemented obtain handlers in PredictorObtain,
    // SettlementEffects.AfterRelicAdded and NotifyOpenCardRewards. Reward origin
    // and automatic-pickup preferences have no authority over these reads.
    private static PredictorCombatDomain ObtainReadDomains(ModelKey key) => key.Entry switch
    {
        "WHETSTONE" or "WAR_PAINT" or "SAND_CASTLE" or "BEAUTIFUL_BRACELET" or
        "PANDORAS_BOX" or "FRAGRANT_MUSHROOM" or "NEW_LEAF" =>
            PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng,
        "BYRDPIP" or "PAELS_CLAW" or "NUTRITIOUS_SOUP" or "TRI_BOOMERANG" or "ELECTRIC_SHRYMP" or
        "PAELS_GROWTH" or "GNARLED_HAMMER" or "KIFUDA" or "PUNCH_DAGGER" or
        "DOLLYS_MIRROR" or "PAELS_TOOTH" or "CLAWS" or "PRECISE_SCISSORS" or
        "BIIIG_HUG" or "PRECARIOUS_SHEARS" or "EMPTY_CAGE" or "PRESERVED_FOG" or "POMANDER" or
        "YUMMY_COOKIE" or "GOLDEN_COMPASS" or "NEOWS_TALISMAN" or "ARCHAIC_TOOTH" or "LEAFY_POULTICE" => PredictorCombatDomain.Deck,
        "ROYAL_STAMP" or "ASTROLABE" => PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng,
        "DISTINGUISHED_CAPE" or "WING_CHARM" => PredictorCombatDomain.NicheRng,
        "KALEIDOSCOPE" => PredictorCombatDomain.NicheRng | PredictorCombatDomain.RewardsRng,
        "ARCANE_SCROLL" or "SEA_GLASS" or "LOST_COFFER" or "CAULDRON" or "SMALL_CAPSULE" or "NEOWS_BONES" or
        "CALLING_BELL" or "TOY_BOX" or "ORRERY" or "GLASS_EYE" or
        "LEAD_PAPERWEIGHT" or "HEFTY_TABLET" or "SCROLL_BOXES" => PredictorCombatDomain.RewardsRng,
        "ALCHEMICAL_COFFER" => PredictorCombatDomain.CombatPotionRng,
        "PHIAL_HOLSTER" => PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.PotionInventory,
        "NEOWS_SACRIFICE" => PredictorCombatDomain.PotionInventory,
        // Starter replacement invokes another obtain. This bounded projection
        // does not infer the selected starter's nested effects from a witness.
        "TOUCH_OF_OROBAS" => PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng |
            PredictorCombatDomain.RewardsRng | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.CombatPotionRng,
        "LARGE_CAPSULE" => PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng |
            PredictorCombatDomain.RewardsRng | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.CombatPotionRng,
        _ => PredictorCombatDomain.None
    };

    private static PredictorCombatDomain ObtainEntryReadDomains(PredictorRun run, ModelKey key, List<RouteDependency> dependencies)
    {
        run._preview.Partial?.Synchronize(run.Working);
        if (run._preview.Partial?.CanProjectUpgrade(key) == true) return PredictorCombatDomain.None;
        var reads = RefineOpenRewardObtainReads(run, key, ObtainReadDomains(key));
        if (run._preview.Partial != null) reads = run._preview.Partial.RefineObtainReads(key, reads, entryOnly: true);
        // Mirror's entry opens a picker when at least two non-Quest cards are
        // guaranteed. Full payload belongs to the subsequently chosen source,
        // not every other card in the deck.
        if (key.Entry == "DOLLYS_MIRROR" && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
            RouteDeckKnowledge(run).Project(PredictorDeckProjectionScope.Cloneable).Count?.Bounds.Lower > 1)
            reads &= ~PredictorCombatDomain.Deck;
        int requested = key.Entry switch { "EMPTY_CAGE" => 2, "ASTROLABE" => 3, _ => 0 };
        if (requested == 0) return reads;
        // BeginObtainDeckChoice automatically acts only at <= requested eligible
        // cards. A larger guaranteed subset proves that entry only opens the
        // picker; the chosen target and transformation RNG are read later.
        int guaranteed = run.Working.Deck.Select(run.Working.Card).Count(card => card.Removable &&
            (key.Entry != "ASTROLABE" || !PredictorCardChanges.Definition(run.Context, card.Key).IsQuest) &&
            !dependencies.Any(d => d.MayChangeExistingCards && (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Id == card.Id))));
        return guaranteed > requested ? reads & ~(PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng) : reads;
    }

    private static PredictorCombatDomain RefineOpenRewardObtainReads(PredictorRun run, ModelKey key,
        PredictorCombatDomain reads)
    {
        // Wing has no AfterObtained effect. Only still-subscribed card offers
        // receive its immediate late modification; future offers retain their
        // own Niche dependency at generation.
        if (key.Entry == "WING_CHARM" && !run.Working.DetachedCardRewards
            .Concat(run._obtainFrames.SelectMany(frame => frame.ParentRewards)).Concat(run.Rewards)
            .Any(reward => reward.Kind == PredictorRewardKind.Card && reward.Status == PredictorRewardStatus.Open &&
                reward.RelicSubscriptionActive))
            reads &= ~PredictorCombatDomain.NicheRng;
        return reads;
    }
}
