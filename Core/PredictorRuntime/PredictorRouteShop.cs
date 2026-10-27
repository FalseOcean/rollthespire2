using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Installed only on a detached projection. A null callback is the normal
    // concrete runtime and cannot turn Parasol into an optional preference.

    private static void ConfigureRouteShopProjection(PredictorRun run, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> entryCauses)
    {
        run._preview.ShopEntryCauses = entryCauses;
        run._preview.ShopAutomaticPurchaseAllowed = slot =>
        {
            var entry = run._shopEntries[slot];
            if (!entry.Stocked) return true;
            var domains = entry.Kind == PredictorShopEntryKind.CardRemoval ? PredictorCombatDomain.None : RouteShopOfferDomains(entry.Kind);
            ImmutableArray<PredictorCombatPendingCause> obtainCauses = [];
            if (entry.Kind == PredictorShopEntryKind.Relic && entry.Key is { } key)
                obtainCauses = ObtainCauses(run, key, dependencies, entryOnly: true);
            if (entry.Kind == PredictorShopEntryKind.Potion)
                domains |= PredictorCombatDomain.PotionInventory;
            if (entry.Kind == PredictorShopEntryKind.CardRemoval)
            {
                // Optional additions cannot turn an existing removable card
                // into an empty picker. Pause only if existence itself is not
                // established; the actual target is still a player choice.
                bool knownCandidate = run.Working.Deck.Any(id => run.Working.Card(id).Removable &&
                    !dependencies.Any(d => d.MayChangeExistingCards && (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                        (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Id == id))));
                if (!knownCandidate) domains |= PredictorCombatDomain.Deck;
            }
            var causes = PredictorRouteCauseEvidence.Normalize(CausesFor(dependencies, domains).AddRange(obtainCauses));
            if (causes.IsEmpty) return true;
            foreach (var cause in causes)
                if (!entryCauses.Contains(cause)) entryCauses.Add(cause);
            var pending = RouteCause("ShopAutomaticPurchasePending", run.Working.Position.NodeId,
                PredictorCombatDomain.Continuation, causes, "shop-slot:" + slot);
            if (!entryCauses.Contains(pending)) entryCauses.Add(pending);
            return false;
        };
    }

    private static PredictorShopNodeKnowledge ProjectRouteShop(PredictorRun run, PredictorPlannedShop? plan,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (plan != null) ValidatePlannedShop(plan);
        if (run._preview.ShopEntryCauses is { } entry)
            foreach (var cause in entry)
                if (!local.Contains(cause)) local.Add(cause);
        var actions = plan?.Actions ?? [];
        run._preview.Partial?.RecordShopStockObservation(run, ObserveRouteShop(run, dependencies));
        var resolvedActions = new List<PredictorPlannedShopAction>();
        int applied = ActiveRouteShopPlanPrefix(run, actions, resolvedActions, token);
        bool advanced = false;
        var finalOffers = ImmutableArray<PredictorShopOfferKnowledge>.Empty;
        if (!local.Any(c => c.Code == "ShopAutomaticPurchasePending"))
        {
            for (; applied < actions.Length; applied++)
            {
                ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Shop = plan }, PredictorRewardEditOwner.Shop, applied);
                if (actions[applied].Action == PredictorShopActionKind.Leave && run.HasObtainChild)
                {
                    CaptureCompletedCardRewardEdits(run, dependencies);
                    ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
                }
                token.ThrowIfCancellationRequested();
                if (actions[applied].Action == PredictorShopActionKind.Leave) finalOffers = ObserveRouteShop(run, dependencies);
                var resolvedAction = ResolvedRouteShopAction(run, actions[applied]);
                if (!ApplyRouteShopAction(run, actions[applied], dependencies, knownInstanceIds, local, token)) break;
                resolvedActions.Add(resolvedAction);
                if (run.Phase == PredictorPhase.Complete) { applied++; advanced = true; break; }
                if (run._preview.ShopEntryCauses?.Any(c => c.Code == "ShopAutomaticPurchasePending") == true)
                {
                    foreach (var cause in run._preview.ShopEntryCauses)
                        if (!local.Contains(cause)) local.Add(cause);
                    applied++;
                    break;
                }
            }
        }
        if (local.Count == 0)
        {
            var editPlan = plan;
            if (editPlan == null && run.HasObtainChild)
            {
                var frame = run._obtainFrames.FirstOrDefault(frame => frame.Destination == PredictorObtainReturn.Shop);
                int slot = frame == null ? -1 : run._shopEntries.FindIndex(entry => entry.Id == frame.SourceId);
                if (slot >= 0)
                {
                    var prefix = run._shopPendingRelicIgnoreCost ? ImmutableArray<PredictorPlannedShopAction>.Empty :
                        ImmutableArray.Create(new PredictorPlannedShopAction(PredictorShopActionKind.Buy)
                            { ExpectedOffer = RouteShopSnapshot(run, slot) });
                    // A normal default visit leaves after the child. Parasol's
                    // source continuation still owns its later purchases/targets.
                    if (!run._parasolActive && run._shopRemovalEntry == null)
                        prefix = prefix.Add(new(PredictorShopActionKind.Leave));
                    editPlan = new() { Actions = prefix };
                }
            }
            ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Shop = editPlan }, PredictorRewardEditOwner.Shop,
                plan == null ? editPlan?.Actions.Count(action => action.Action != PredictorShopActionKind.Leave) ?? 0 : applied);
            CaptureCompletedCardRewardEdits(run, dependencies);
            ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
        }
        if (!advanced && local.Count == 0)
        {
            // Exhausting the authored shopping sequence means no additional
            // purchases. Keep the final stock/quotes visible so this completed
            // projection can still be edited and extended on the next replay.
            if ((plan != null && applied == actions.Length ||
                    plan == null && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults) &&
                run.Phase == PredictorPhase.Shop && !run._parasolActive && run._shopRemovalEntry == null)
            {
                finalOffers = ObserveRouteShop(run, dependencies);
                advanced = ApplyRouteShopAction(run, new(PredictorShopActionKind.Leave), dependencies, knownInstanceIds, local, token, authored: false);
            }
        }
        if (!advanced && local.Count == 0)
            local.Add(RouteCause(run._shopRemovalEntry != null ? "ShopRemovalChoice" :
                run.Phase != PredictorPhase.Shop ? "ShopObtainChoice" : "ShopChoice",
                run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        var child = advanced ? null : ObserveRouteObtainChoice(run, dependencies, knownInstanceIds);
        var goldPending = CausesFor(dependencies, PredictorCombatDomain.Gold);
        return new(advanced ? finalOffers : ObserveRouteShop(run, dependencies), local.Distinct().ToImmutableArray())
        {
            Advanced = advanced, AppliedActions = applied, AutomaticPurchasesActive = run._parasolActive,
            ResolvedActions = resolvedActions.ToImmutableArray(),
            RequiredChoices = child == null ? [] : [child],
            GoldBalance = goldPending.IsEmpty ? run.Working.GoldBudget : null, GoldPendingCauses = goldPending
        };
    }

    private static PredictorCombatDomain RouteShopOfferDomains(PredictorShopEntryKind kind)
        => (kind == PredictorShopEntryKind.CardRemoval ? PredictorCombatDomain.None : PredictorCombatDomain.ShopsRng) |
            PredictorCombatDomain.Relics |
            (kind == PredictorShopEntryKind.Relic ? PredictorCombatDomain.RewardsRng :
                kind == PredictorShopEntryKind.CharacterCard ? PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Odds :
                PredictorCombatDomain.None);

    private static PredictorShopOfferSnapshot RouteShopSnapshot(PredictorRun run, int slot)
    {
        var entry = run._shopEntries[slot];
        return new(slot, entry.Revision, entry.Kind, entry.Key,
            entry.Card is { } card ? card with { Id = 0 } : null,
            entry.BaseCost, run.ShopCost(entry), entry.IsOnSale, entry.Stocked)
        { StockRevisionIsOccurrence = true };
    }

    private static bool RouteShopOfferMatches(PredictorRun run, PredictorShopOfferSnapshot actual,
        PredictorShopOfferSnapshot expected)
    {
        if (!expected.StockRevisionIsOccurrence)
        {
            // Explicit legacy encoding: old RefreshShopCards assigned the
            // current global revision to every stocked card after a purchase.
            // Reconstruct exactly that old observation, then compare every
            // field. This is not permission to ignore a replacement revision,
            // changed model/card, price, sale state or stock availability.
            var entry = run._shopEntries[actual.Slot];
            actual = actual with { StockRevision = entry.Stocked && entry.Card != null ? run._shopRevision : entry.Revision,
                StockRevisionIsOccurrence = false };
        }
        return actual == expected;
    }

    private static PredictorPlannedShopAction ResolvedRouteShopAction(PredictorRun run, PredictorPlannedShopAction action)
        => action.Action is PredictorShopActionKind.Buy or PredictorShopActionKind.BeginRemoval &&
            action.ExpectedOffer is { } expected && expected.Slot >= 0 && expected.Slot < run._shopEntries.Length
            ? action with { ExpectedOffer = RouteShopSnapshot(run, expected.Slot) } : action;

    private static ImmutableArray<PredictorShopOfferKnowledge> ObserveRouteShop(PredictorRun run,
        List<RouteDependency> dependencies)
    {
        var offers = ImmutableArray.CreateBuilder<PredictorShopOfferKnowledge>();
        for (int slot = 0; slot < run._shopEntries.Length; slot++)
        {
            var entry = run._shopEntries[slot];
            var causes = CausesFor(dependencies, RouteShopOfferDomains(entry.Kind));
            var affordability = causes.AddRange(CausesFor(dependencies, PredictorCombatDomain.Gold)).Distinct().ToImmutableArray();
            offers.Add(new(slot, entry.Kind, causes.IsEmpty ? RouteShopSnapshot(run, slot) : null, causes)
            {
                Affordable = affordability.IsEmpty ? run.Working.GoldBudget >= run.ShopCost(entry) : null,
                AffordabilityPendingCauses = affordability
            });
        }
        return offers.ToImmutable();
    }

    private static bool ApplyRouteShopAction(PredictorRun run, PredictorPlannedShopAction action,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token, bool authored = true)
    {
        var trace = run._preview.Partial?.BeginShopAction(run, action, authored);
        int before = local.Count;
        bool applied = ApplyRouteShopActionCore(run, action, dependencies, knownInstanceIds, local, token, trace);
        trace?.Complete(run, applied, local.Skip(before).ToImmutableArray(), ObserveRouteShop(run, dependencies));
        return applied;
    }

    private static bool ApplyRouteShopActionCore(PredictorRun run, PredictorPlannedShopAction action,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token, RoutePartialState.ShopActionTrace? trace)
    {
        if (action.Obtain?.Action != PredictorObtainChildAction.Health) CompleteLiveObtainHealth(run, dependencies, token);
        if (action.Obtain == null) CompleteLivePaelsTooth(run, dependencies, token);
        bool Pending(string code, ImmutableArray<PredictorCombatPendingCause> parents = default)
        {
            local.Add(RouteCause(code, run.Working.Position.NodeId, PredictorCombatDomain.Continuation, parents,
                PredictorRouteCauseEvidence.Key("shop-action", run._inputs.Length.ToString(), action.Action.ToString())));
            return false;
        }
        if (action.Action == PredictorShopActionKind.Obtain)
        {
            // Child-specific reads belong to ApplyRouteObtainChild's owner.
            trace?.ReadFlow(run);
            int before = local.Count;
            return action.Obtain != null && ApplyRouteObtainChild(run, action.Obtain, dependencies, knownInstanceIds, local, token) ||
                Pending("PlannedShopObtainPending", local.Skip(before).ToImmutableArray());
        }
        trace?.ReadFlow(run);
        if (run.Phase != PredictorPhase.Shop) return Pending("ShopObtainChoice");
        if (action.Action == PredictorShopActionKind.Leave)
        {
            trace?.ReadFlow(run, removal: true);
            if (run._parasolActive || run._shopRemovalEntry != null) return Pending("ShopRemovalChoice");
            trace?.ReadInventoryOpen(run);
            if (run._shopInventoryOpen && run.Submit(run.Request!, new CloseShopInventory(), token) != PredictorInputResult.Accepted)
                return Pending("PlannedShopLeaveUnavailable");
            return run.Submit(run.Request!, new LeaveShop(), token) == PredictorInputResult.Accepted || Pending("PlannedShopLeaveUnavailable");
        }
        if (action.Action == PredictorShopActionKind.CancelRemoval)
        {
            trace?.ReadFlow(run, removal: true);
            return run.Submit(run.Request!, new CancelShopCardRemoval(), token) == PredictorInputResult.Accepted || Pending("PlannedShopRemovalUnavailable");
        }
        if (action.Action == PredictorShopActionKind.ChooseRemoval)
        {
            bool knownTarget = RouteShopKnownCard(run, action.Card, dependencies, knownInstanceIds);
            trace?.ReadRemovalTarget(run, action.Card, knownTarget,
                knownTarget ? [] : RouteDeckTargetCauses(run, action.Card == null ? [] : [action.Card], dependencies, knownInstanceIds));
            if (!knownTarget) return Pending("PlannedShopTargetPending",
                RouteDeckTargetCauses(run, action.Card == null ? [] : [action.Card], dependencies, knownInstanceIds));
            trace?.ReadFlow(run, removal: true);
            if (!run._parasolActive && !CausesFor(dependencies, PredictorCombatDomain.Gold).IsEmpty)
            {
                trace?.ReadGold(run);
                return Pending("PlannedShopAffordabilityPending", CausesFor(dependencies, PredictorCombatDomain.Gold));
            }
            if (run._shopRemovalEntry.HasValue) trace?.ReadRemovalQuote(run);
            return run.Submit(run.Request!, new ChooseShopRemovalCard(action.Card!.InstanceId), token) == PredictorInputResult.Accepted ||
                Pending("PlannedShopRemovalUnavailable");
        }
        trace?.ReadFlow(run, removal: true);
        if (run._parasolActive || run._shopRemovalEntry != null) return Pending("ShopRemovalChoice");
        if (action.ExpectedOffer is not { } expected || expected.Slot < 0 || expected.Slot >= run._shopEntries.Length)
            return Pending("PlannedShopOfferChanged");
        var observed = ObserveRouteShop(run, dependencies)[expected.Slot];
        trace?.ReadOffer(run, observed);
        if (!observed.IsKnown) return Pending("PlannedShopOfferPending", observed.PendingCauses);
        if (!RouteShopOfferMatches(run, observed.Value!, expected) || !expected.Stocked) return Pending("PlannedShopOfferChanged");
        trace?.ReadAffordability(run, observed);
        if (observed.Affordable == null) return Pending("PlannedShopAffordabilityPending", observed.AffordabilityPendingCauses);
        if (observed.Affordable == false) return Pending("PlannedShopInsufficientGold");
        var entry = run._shopEntries[expected.Slot];
        if (action.Action == PredictorShopActionKind.Buy)
        {
            if (entry.Kind == PredictorShopEntryKind.CardRemoval) return Pending("PlannedShopOfferChanged");
            if (entry.Kind == PredictorShopEntryKind.Relic)
            {
                var obtain = ObtainCauses(run, entry.Key!.Value, dependencies, entryOnly: true);
                trace?.ReadGate("relic.obtainEntry", obtain);
                if (!obtain.IsEmpty) return Pending("PlannedShopObtainDependent", ObtainCauses(run, entry.Key!.Value, dependencies, entryOnly: true));
            }
            if (entry.Kind == PredictorShopEntryKind.Potion && !CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty)
            {
                trace?.ReadGate("potions.receipt", CausesFor(dependencies, PredictorCombatDomain.PotionInventory));
                return Pending("PlannedShopPotionReceiptPending", CausesFor(dependencies, PredictorCombatDomain.PotionInventory));
            }
        }
        else if (entry.Kind != PredictorShopEntryKind.CardRemoval) return Pending("PlannedShopOfferChanged");
        else
        {
            trace?.ReadRemovalCandidates(run);
            if (run.Working.Deck.All(id => !run.Working.Card(id).Removable) &&
                !CausesFor(dependencies, PredictorCombatDomain.Deck).IsEmpty)
                return Pending("PlannedShopTargetPending", CausesFor(dependencies, PredictorCombatDomain.Deck));
        }
        trace?.ReadInventoryOpen(run);
        if (!run._shopInventoryOpen && run.Submit(run.Request!, new OpenShopInventory(), token) != PredictorInputResult.Accepted)
            return Pending("PlannedShopInventoryUnavailable");
        var input = action.Action == PredictorShopActionKind.BeginRemoval ? (PredictorInput)new BeginShopCardRemoval(entry.Id, run._shopRevision) :
            new BuyShopEntry(entry.Id, run._shopRevision);
        var result = run.Submit(run.Request!, input, token);
        return result == PredictorInputResult.Accepted || Pending(result == PredictorInputResult.TooFull ?
            "PlannedShopPotionInventoryFull" : "PlannedShopPurchaseUnavailable");
    }

    private static bool RouteShopKnownCard(PredictorRun run, PredictorPlannedCard? target,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
        => target != null && run.Working.Deck.Contains(target.InstanceId) && run.Working.Card(target.InstanceId) is { } card &&
            card.Key == target.Key && card.FloorAdded == target.FloorAdded &&
            (knownInstanceIds.Contains(card.Id) || CausesFor(dependencies, PredictorCombatDomain.InstanceIds).IsEmpty) &&
            !dependencies.Any(d => d.MayChangeExistingCards && (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Id == card.Id)));

    // Match an already accepted active prefix against the authored semantic
    // actions at their original boundaries. Counting purchase names would bind
    // a changed plan to an unrelated purchase or replay the same purchase twice.
    private static int ActiveRouteShopPlanPrefix(PredictorRun run,
        ImmutableArray<PredictorPlannedShopAction> actions, List<PredictorPlannedShopAction> resolvedActions, CancellationToken token)
    {
        if (actions.IsEmpty || run._nodeReplayInputs.IsEmpty) return 0;
        var replay = run.CreateEntryReplay(PreviewReplayPurpose.RoomObservation);
        replay.RestoreRoutes(run._nodeEntryRoutes, run._preview.Settings.AcceptsPrefixes);
        replay.AdvanceRoute(token);
        int count = 0;
        foreach (var input in run._nodeReplayInputs)
        {
            token.ThrowIfCancellationRequested();
            bool matches = count < actions.Length && RouteShopAcceptedActionMatches(replay, actions[count], input);
            var resolvedAction = matches ? ResolvedRouteShopAction(replay, actions[count]) : null;
            if (replay.Request is not { } request)
                throw new InvalidOperationException("PredictorShopActivePrefixReplayMismatch");
            var result = replay.Submit(request, input, token);
            if (result is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorShopActivePrefixReplayMismatch");
            if (matches && (result == PredictorInputResult.Accepted || actions[count].Action == PredictorShopActionKind.Obtain &&
                    result is PredictorInputResult.TooFull or PredictorInputResult.NotAllowed))
            {
                resolvedActions.Add(resolvedAction!);
                count++;
            }
        }
        return count;
    }

    private static bool RouteShopAcceptedActionMatches(PredictorRun run, PredictorPlannedShopAction action, PredictorInput input)
    {
        if (action.Action is PredictorShopActionKind.Buy or PredictorShopActionKind.BeginRemoval && action.ExpectedOffer is { } expected &&
            expected.Slot >= 0 && expected.Slot < run._shopEntries.Length && RouteShopOfferMatches(run, RouteShopSnapshot(run, expected.Slot), expected))
        {
            long id = run._shopEntries[expected.Slot].Id;
            return action.Action == PredictorShopActionKind.Buy && input is BuyShopEntry buy && buy.EntryId == id ||
                action.Action == PredictorShopActionKind.BeginRemoval && input is BeginShopCardRemoval remove && remove.EntryId == id;
        }
        if (action.Action == PredictorShopActionKind.ChooseRemoval && input is ChooseShopRemovalCard choose)
            return action.Card is { } card && card.InstanceId == choose.CardId && run.Working.Deck.Contains(card.InstanceId) &&
                run.Working.Card(card.InstanceId).Key == card.Key && run.Working.Card(card.InstanceId).FloorAdded == card.FloorAdded;
        if (action.Action == PredictorShopActionKind.CancelRemoval) return input is CancelShopCardRemoval;
        if (action.Action == PredictorShopActionKind.Leave) return input is LeaveShop;
        if (action.Action != PredictorShopActionKind.Obtain || action.Obtain == null) return false;
        var resolved = ResolveRouteObtainChildInput(run, action.Obtain, [], run.Working.Cards.Select(c => c.Id).ToHashSet(), []);
        return resolved != null && SameRouteObtainInput(resolved, input);
    }

    internal static void ValidatePlannedShop(PredictorPlannedShop plan)
    {
        RequirePlan(plan != null && !plan.Actions.IsDefault);
        foreach (var action in plan.Actions)
        {
            RequirePlan(action != null && Enum.IsDefined(action.Action));
            RequirePlan(action.ExpectedOffer == null || action.Action is PredictorShopActionKind.Buy or PredictorShopActionKind.BeginRemoval);
            RequirePlan(action.Card == null || action.Action == PredictorShopActionKind.ChooseRemoval);
            RequirePlan(action.Obtain == null || action.Action == PredictorShopActionKind.Obtain);
            if (action.Card is { } card) ValidatePlannedCard(card);
            if (action.ExpectedOffer is { } offer)
            {
                RequirePlan(offer.Slot >= 0 && offer.StockRevision >= 0 && Enum.IsDefined(offer.Kind) && offer.BaseCost >= 0 && offer.Cost >= 0);
                RequirePlan(offer.Key == null || offer.Key.Value.IsValid);
                RequirePlan(offer.Card == null || offer.Card.Key.IsValid && offer.Card.Key.Category == "CARD" &&
                    offer.Card.UpgradeLevel >= 0 && offer.Card.MaxUpgradeLevel >= offer.Card.UpgradeLevel);
            }
            if (action.Obtain is { } obtain) ValidatePlannedObtainChild(obtain);
        }
    }
}
