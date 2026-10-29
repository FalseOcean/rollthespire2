using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorRouteKnowledgeChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static void Run(PredictorRun captured)
    {
        StaleCombatPlan(captured);
        HealthAndUnpickedCards(captured);
        NicheAndUnknownTheft(captured);
        WingCharmRouteScopes(captured);
        TentOrderAndActiveRewards(captured);
        ChangedRewardPlan(captured);
        NextActAncientEntry(captured);
        UnknownDeckRandomUpgrade(captured);
        IndependentDigAndPotionReceipt(captured);
        ConcreteAlternativeWitnesses(captured);
        LiveMapUnselectedRest(captured);
        LiveDefaultRestPolicy(captured);
        ExplicitReadyPrefix(captured);
        PotionNodeEditing(captured);
        Console.WriteLine("RUNTIME_ROUTE_KNOWLEDGE_PASS combat-rest-combat; HP/card/Niche local dependencies; smith binding; Tent order; active rest rewards; changed-offer pending; independent Dig; uncertain potion receipt; concrete alternative witnesses; pure replay");
    }

    private static PredictorRun Route(PredictorRun captured, string firstEncounter = "FUZZY_WURM_CRAWLER_WEAK",
        params string[] relics)
        => Route(captured, firstEncounter, relics, null);

    private static PredictorRun Route(PredictorRun captured, string firstEncounter, string[] relics,
        Func<PredictorState, PredictorState>? configure)
    {
        var context = captured.Context with { NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns) };
        // Keep actual source card/relic catalogs and encounter settlement. Only
        // the generated map is replaced by a deterministic route geometry.
        var original = captured.RouteMap(1);
        var types = new[] { MapPointType.Ancient, MapPointType.Monster, MapPointType.RestSite, MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
        var map = original with { Points = points };
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], Position = new(1, 1, "route-knowledge:entry"),
            Maps = [new(1, map, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = captured.Working.Acts.SetItem(0, captured.Working.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", firstEncounter), new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 })
        };
        foreach (string relic in relics) state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", relic));
        state = state with { Relics = state.Relics.Select(r => r.Key.Entry == "FISHING_ROD" ?
            r with { FishingRodCombatsSeen = 1 } : r).ToImmutableArray() };
        state = configure?.Invoke(state) ?? state;
        var run = new PredictorRun(context, state);
        run.SetRoute(1, points.Select(p => p.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static string RestNode => "map:1:2:3:3";

    internal static void RunHatchOverblockBoundaries(PredictorRun captured)
    {
        const string first = "map:1:1:3:2", second = "map:1:2:3:3";
        PredictorRun Fixture(int eggCount = 1, bool removableEgg = false, bool futureEgg = false,
            ImmutableArray<PredictorCombatPendingCause> incoming = default)
        {
            var types = new[] { MapPointType.Ancient, futureEgg ? MapPointType.Unknown : MapPointType.RestSite, MapPointType.RestSite, MapPointType.Boss };
            var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
                row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
            var state = captured.Working with
            {
                OpeningCompleted = true, Relics = [], Position = new(1, 1, "hatch-boundaries:entry"),
                Maps = [new(1, captured.RouteMap(1) with { Points = points }, [points[0].Position])],
                Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
                VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
                Acts = captured.Working.Acts.Select(act => act.Act == 1 ? act with
                    { RawEvents = [new("EVENT", "BYRDONIS_NEST")], EventCursor = 0 } : act).ToImmutableArray()
            };
            foreach (string relic in new[] { "MINIATURE_TENT", "MEAT_CLEAVER", "PAELS_GROWTH" })
                state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", relic));
            for (int i = 0; i < eggCount; i++)
                state = PredictorObtainSources.AddCard(captured.Context, state, new ModelKey("CARD", "BYRDONIS_EGG"));
            // A removable Egg is an explicit altered-state negative case, not
            // an assertion that the vanilla Quest card can normally be cooked.
            if (removableEgg) state = state with { Cards = state.Cards.Select(card => card.Key.Entry == "BYRDONIS_EGG"
                ? card with { Removable = true } : card).ToImmutableArray() };
            var context = captured.Context with { Live = incoming.IsDefault ? null : new(1, true, incoming), Operation = null };
            var run = new PredictorRun(context, state);
            run.SetRoute(1, points.Select(point => point.Position).ToImmutableArray());
            run.AdvanceRoute();
            return run;
        }
        PredictorRouteContinuation Preview(PredictorRun run, PredictorRoutePreviewPolicy policy,
            params PredictorPlannedNode[] plans)
        {
            string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
            var result = run.PreviewRouteContinuation(plans.ToImmutableArray(), 2,
                scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
            Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints),
                "Hatch qualification/replay preserves actual state and accepted checkpoints");
            return result;
        }
        const PredictorRoutePreviewPolicy live = PredictorRoutePreviewPolicy.LiveMapDefaults;
        var run = Fixture();
        var smith = new PredictorPlannedNode(first) { RestActions = [new(PredictorRestAction.Smith)], LeaveRest = true };
        var options = Preview(run, live, smith).Nodes[0];
        Check(options.AppliedRestActions == 1 && options.RestOfferPendingCauses.IsEmpty &&
            options.RestOffers.Any(offer => offer.Action == PredictorRestAction.Hatch && offer.Enabled),
            "Tent keeps Hatch selectable after unspecified Smith without hiding its real Egg supplier");
        var together = Preview(run, live, smith with { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Hatch)] });
        Check(together.Nodes.Length == 2 && together.Nodes[0].Advanced && together.Nodes[0].AppliedRestActions == 2 &&
            together.Nodes[0].RestActionKnowledge[1].ObtainedRelics is [{ Entry: "BYRDPIP" }] &&
            !together.Nodes[0].Cards.Any(card => card.Value.Key.Entry == "BYRDONIS_EGG") &&
            !together.PendingCauses.Any(cause => cause.Code == "RestDeckDependentAction"),
            "Tent Smith then Hatch obtains the actual pet and preserves following independent nodes");
        var hatch = new PredictorPlannedNode(second) { RestActions = [new(PredictorRestAction.Hatch)], LeaveRest = true };
        var downstream = Preview(run, live, hatch);
        Check(downstream.Nodes.Length == 2 && downstream.Nodes[0].RestDefaultApplied && downstream.Nodes[1].Advanced &&
            downstream.Nodes[1].RestActionKnowledge.Single().ObtainedRelics is [{ Entry: "BYRDPIP" }],
            "A retained later Hatch plan still executes when an upstream explicit target is cleared back to default Smith");
        var exact = Preview(run, PredictorRoutePreviewPolicy.AuthoredChoices,
            smith with { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Hatch)] });
        Check(!exact.Nodes[0].Advanced && exact.Nodes[0].AppliedRestActions == 0,
            "Authored choices still require the actual Smith target before executing Hatch");
        var clone = Preview(run, live, smith with { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Clone)] });
        Check(!clone.Nodes[0].Advanced && clone.Nodes[0].PendingCauses.Any(cause => cause.Code == "RestDeckDependentAction"),
            "Clone's full persistent-card copy boundary is not loosened by Hatch's fixed-model qualification");

        var unresolvedCook = Preview(run, live, new PredictorPlannedNode(first)
            { RestActions = [new(PredictorRestAction.Cook), new(PredictorRestAction.Hatch)], LeaveRest = true });
        Check(!unresolvedCook.Nodes[0].Advanced && !unresolvedCook.Nodes[0].RestOfferPendingCauses.IsEmpty &&
            unresolvedCook.Nodes[0].PendingCauses.Any(cause => cause.Code == "RestDeckDependentAction"),
            "Unresolved membership-changing Cook retains the actual Egg membership blocker");
        var unknownEgg = PredictorRouteCauseEvidence.CreateExplicit("HatchTestEggMembership", "capture:egg",
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation,
            "The captured Egg membership is not established.", ["deck.BYRDONIS_EGG.membership"]);
        var unknown = Preview(Fixture(incoming: [unknownEgg]), live, new PredictorPlannedNode(first)
            { RestActions = [new(PredictorRestAction.Hatch)], LeaveRest = true });
        Check(!unknown.Nodes[0].Advanced && unknown.Nodes[0].RestOfferPendingCauses.Any(cause => cause.Code == unknownEgg.Code) &&
            unknown.Nodes[0].RestOffers.All(offer => offer.Action != PredictorRestAction.Hatch),
            "Unknown Egg membership preserves its original cause and does not publish a certain Hatch option");
        var unknownId = PredictorRouteCauseEvidence.CreateExplicit("HatchTestNextId", "capture:next-id",
            PredictorCombatDomain.InstanceIds, PredictorPendingReasonKind.MissingObservation,
            "The allocation identity of a future Egg is not established.", ["instances.nextId"]);
        var future = Fixture(eggCount: 0, futureEgg: true, incoming: [unknownId]);
        var eventNode = Preview(future, live).Nodes[0];
        var take = eventNode.Event!.EntryOptions.Single(option => option.Key == "TAKE").Value!;
        var sourcePending = Preview(future, live, new PredictorPlannedNode(first)
            { Event = new(eventNode.Event.Event!.Value) { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = take }] } }, hatch);
        Check(sourcePending.Nodes.Length == 2 && !sourcePending.Nodes[1].Advanced &&
            sourcePending.Nodes[1].RestOfferPendingCauses.Any(cause => cause.Code == unknownId.Code) &&
            sourcePending.Nodes[1].PendingCauses.Any(cause => cause.Code == "RestDeckDependentAction"),
            "A newly predicted Egg with unknown source allocation keeps the original InstanceIds blocker");
        var removable = Fixture(removableEgg: true);
        var egg = removable.Working.Deck.Select(removable.Working.Card).Single(card => card.Key.Entry == "BYRDONIS_EGG");
        var other = removable.Working.Deck.Select(removable.Working.Card).First(card => card.Id != egg.Id && card.Removable);
        var cooked = Preview(removable, live, new PredictorPlannedNode(first) { RestActions = [new(PredictorRestAction.Cook)
            { CookCards = [new(egg.Id, egg.Key, egg.FloorAdded), new(other.Id, other.Key, other.FloorAdded)] }], LeaveRest = true }, hatch);
        Check(cooked.Nodes.Length == 2 && cooked.Nodes[0].Advanced && !cooked.Nodes[1].Advanced &&
            cooked.Nodes[1].RestOffers.All(offer => offer.Action != PredictorRestAction.Hatch) &&
            cooked.Nodes[1].PendingCauses.Any(cause => cause.Code == "PlannedRestActionUnavailable"),
            "A real earlier Egg removal cannot invent a supplier for an existing later Hatch plan");
        var multiple = Preview(Fixture(eggCount: 2), live, smith with
            { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Hatch), new(PredictorRestAction.Hatch)] });
        Check(multiple.Nodes[0].Advanced && multiple.Nodes[0].AppliedRestActions == 3 &&
            multiple.Nodes[0].RestActionKnowledge.Count(action => action.ObtainedRelics.Any(relic => relic.Entry == "BYRDPIP")) == 2,
            "Two entry-time Egg suppliers remain two native Hatch instances after the first transforms all Eggs");
        Console.WriteLine("RUNTIME_HATCH_OVERBLOCK_BOUNDARIES_PASS Tent Smith/Hatch; retained later plan; membership/remove negatives; repeated suppliers; exact and Clone boundaries; pure replay");
    }

    internal static void RunTargetBoundaries(PredictorRun captured)
    {
        const string fire = "map:1:1:3:2", merchant = "map:1:2:3:3";
        PredictorRun Fixture(params string[] relics)
        {
            var original = captured.RouteMap(1);
            var types = new[] { MapPointType.Ancient, MapPointType.RestSite, MapPointType.Shop, MapPointType.Boss };
            var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
                row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
            var state = captured.Working with
            {
                OpeningCompleted = true, Relics = [], GoldBudget = 5000, Position = new(1, 1, "target-boundaries:entry"),
                PersonalBag = PredictorRelicBag.Empty(false),
                Maps = [new(1, original with { Points = points }, [points[0].Position])],
                Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)]
            };
            foreach (string relic in relics) state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", relic));
            var run = new PredictorRun(captured.Context with { NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns) }, state);
            run.SetRoute(1, points.Select(point => point.Position).ToImmutableArray());
            run.AdvanceRoute();
            return run;
        }
        PredictorRouteContinuation Preview(PredictorRun run, params PredictorPlannedNode[] plans)
        {
            string before = JsonSerializer.Serialize(run.Working);
            var result = run.PreviewRouteContinuation(plans.ToImmutableArray(), 2,
                scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(before == JsonSerializer.Serialize(run.Working), "Target boundary observations preserve the source run");
            return result;
        }
        static PredictorPlannedCard Target(PredictorCard card) => new(card.Id, card.Key, card.FloorAdded);

        var ordinary = Fixture();
        var initial = Preview(ordinary);
        var shop = initial.Nodes[1].Shop!;
        Check(initial.Nodes[0].DefaultRestAction?.Action == PredictorRestAction.Smith &&
            shop.RemovalCards.Any(card => card.IsKnown && !card.FullCardKnown),
            "Default Smith uncertainty preserves removable targets without claiming their upgrade details");
        var target = shop.RemovalCards.First(card => card.IsKnown).Value;
        var remove = shop.Offers.Single(offer => offer.Kind == PredictorShopEntryKind.CardRemoval).Value!;
        var removed = Preview(ordinary, new PredictorPlannedNode(merchant) { Shop = new() { Actions =
            [new(PredictorShopActionKind.BeginRemoval) { ExpectedOffer = remove },
             new(PredictorShopActionKind.ChooseRemoval) { Card = Target(target) }] } });
        Check(removed.Nodes[1].Advanced && removed.Nodes[1].Shop!.AppliedActions == 2 &&
            removed.Nodes[1].Inventory!.Cards.All(card => card.Value.Id != target.Id) &&
            !removed.Nodes[1].PendingCauses.Any(cause => cause.Code == "PlannedShopTargetPending"),
            "Merchant executes the same field-sensitive removal target offered after a default Smith");

        var tent = Fixture("MINIATURE_TENT", "MEAT_CLEAVER");
        var cards = tent.Working.Deck.Select(tent.Working.Card).Where(card => card.Removable && card.UpgradeLevel < card.MaxUpgradeLevel).Take(3).ToArray();
        Check(cards.Length == 3, "Target boundary fixture has three removable upgrade targets");
        var cook = new PredictorPlannedRestAction(PredictorRestAction.Cook) { CookCards = cards.Take(2).Select(Target).ToImmutableArray() };
        var smith = new PredictorPlannedRestAction(PredictorRestAction.Smith) { SmithCard = Target(cards[2]) };
        var ordered = Preview(tent, new PredictorPlannedNode(fire) { RestActions = [cook, smith], LeaveRest = true });
        var steps = ordered.Nodes[0].RestActionKnowledge;
        Check(ordered.Nodes[0].AppliedRestActions == 2 && steps[0].Candidates.Any(card => card.Value.Id == cards[0].Id) &&
            steps[1].Candidates.All(card => card.Value.Id != cards[0].Id && card.Value.Id != cards[1].Id) &&
            steps[1].Candidates.Any(card => card.IsKnown && card.Value.Id == cards[2].Id),
            "Tent retains Cook's own entry targets and offers Smith only its post-Cook members");
        var missing = Preview(tent, new PredictorPlannedNode(fire) { RestActions = [cook, new(PredictorRestAction.Smith)], LeaveRest = true });
        Check(missing.Nodes[0].RestActionKnowledge.Single(action => action.ActionIndex == 1).Candidates
            .All(card => card.Value.Id != cards[0].Id && card.Value.Id != cards[1].Id),
            "An unspecified later action still exposes its actual boundary candidates");
        var projectedCook = Preview(tent, new PredictorPlannedNode(fire) { RestActions = [new(PredictorRestAction.Smith), cook], LeaveRest = true });
        Check(projectedCook.Nodes[0].AppliedRestActions == 2 && projectedCook.Nodes[0].RestActionKnowledge[1].Candidates.Any(card => card.IsKnown),
            "Cook execution and its selectable targets do not demand unrelated known upgrade fields");

        var dig = Fixture("SHOVEL");
        var dug = Preview(dig, new PredictorPlannedNode(fire) { RestActions = [new(PredictorRestAction.Dig)], LeaveRest = true });
        Check(dug.Nodes[0].RestActionKnowledge.Single().ObtainedRelics is [{ Entry: "CIRCLET" }] &&
            dug.Nodes[0].Inventory!.Relics.Any(relic => relic.Value.Key.Entry == "CIRCLET"),
            "Dig's actual obtain receipt preserves its result independently of reward rows");
        Check(Preview(dig, new PredictorPlannedNode(fire) { LeaveRest = true }).Nodes[0].RestActionKnowledge.IsEmpty,
            "Removing Dig cannot retain its old obtain receipt");

        // Accepted active prefixes are reconstructed at their own boundaries.
        Check(tent.Submit(tent.Request!, new SelectRestAction(PredictorRestAction.Cook)) == PredictorInputResult.Accepted &&
            tent.Submit(tent.Request!, new SelectCookCards(cards.Take(2).Select(card => card.Id).ToImmutableArray())) == PredictorInputResult.Accepted,
            "Active target fixture performs its real detached Cook prefix");
        var active = Preview(tent, new PredictorPlannedNode(fire) { RestActions = [cook, smith], LeaveRest = true });
        Check(active.Nodes[0].AppliedRestActions == 2 && active.Nodes[0].RestActionKnowledge[0].Candidates.Any(card => card.Value.Id == cards[0].Id) &&
            active.Nodes[0].RestActionKnowledge[1].Candidates.All(card => card.Value.Id != cards[0].Id && card.Value.Id != cards[1].Id),
            "Active Cook replay preserves both original and following action target boundaries");
        Console.WriteLine("RUNTIME_DRAWING_TARGET_BOUNDARIES_PASS scoped removal; per-action rest targets; actual Dig receipt; active accepted prefix; pure replay");
    }

    private static void StaleCombatPlan(PredictorRun captured)
    {
        var run = Route(captured);
        var plan = new PredictorPlannedNode(run.Working.Position.NodeId)
        { Combat = new() { CardTriggers = [new(long.MaxValue, 1)] }, LeaveRewards = true };
        run.SetPlannedNode(plan);
        var pending = Pure(run, run.PlannedNodes, 1).Nodes.Single();
        Check(!pending.Advanced && pending.PendingCauses.Any(c => c.Code == "PlannedCombatFactsPending") &&
            run.PlannedNodes.Single() == plan, "Ordinary combat retains a stale instance plan and its established pending code");
        try
        {
            Pure(run, [plan with { Combat = new() { CombatCardSourceOrder = [new(DeckCardId: run.Working.Deck[0])] } }], 1);
            throw new InvalidOperationException("Ordinary route swallowed an unclassified combat execution error");
        }
        catch (ArgumentException ex) when (ex.Message == "PredictorCombatCardSourceOrderWithoutTemporary" &&
            ex is not PredictorCombatPlanBindingException) { }
    }

    private static void PotionNodeEditing(PredictorRun captured)
    {
        PredictorRun FullRoute() => Route(captured, "FUZZY_WURM_CRAWLER_WEAK", ["WHITE_BEAST_STATUE"], state =>
            state with { PotionSlots = [new(state.NextInstanceId, new("POTION", "ENERGY_POTION")),
                new(state.NextInstanceId + 1, new("POTION", "FRUIT_JUICE"))], NextInstanceId = state.NextInstanceId + 2 });
        var run = FullRoute();
        var unedited = Pure(run, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(unedited.Nodes.Length == 3 && unedited.Nodes[0].Rewards.Any(r => r.IsKnown && r.Kind == PredictorRewardKind.Potion &&
            r.Value!.Status == PredictorRewardStatus.Open) && !unedited.PendingCauses.Any(c => c.Code == "PendingPotionChoice"),
            "A known full inventory leaves the generated drop observable and permits independent route continuation");
        var bottle = run.Working.PotionSlots[0]!;
        var offered = unedited.Nodes[0].Rewards.Single(r => r.IsKnown && r.Kind == PredictorRewardKind.Potion);
        var node = new PredictorPlannedNode(run.Working.Position.NodeId)
        { RewardChoices = [new(offered.Value!, offered.Occurrence)],
            PotionActions = [new(PredictorRoutePotionStage.BeforeChoices, PredictorRoutePotionActionKind.Discard, 0, bottle,
            PredictorPhase.Rewards)] };
        var edited = Pure(run, [node], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var boundary = edited.Nodes[0].PotionBoundaries.Single(b => b.Stage == PredictorRoutePotionStage.BeforeChoices);
        Check(edited.Nodes[0].Rewards.Single(r => r.IsKnown && r.Kind == PredictorRewardKind.Potion).Value!.Key ==
            unedited.Nodes[0].Rewards.Single(r => r.IsKnown && r.Kind == PredictorRewardKind.Potion).Value!.Key &&
            edited.Nodes[0].Rewards.Single(r => r.IsKnown && r.Kind == PredictorRewardKind.Card).Value!.Cards.SequenceEqual(
                unedited.Nodes[0].Rewards.Single(r => r.IsKnown && r.Kind == PredictorRewardKind.Card).Value!.Cards),
            "Potion inventory edits occur after generation and do not consume extra reward RNG or change independent offers");
        Check(boundary.AppliedActions == 1 && boundary.Before.PotionSlots[0].Value == bottle &&
            boundary.After.PotionSlots[0] is { IsKnown: true, Value: null } &&
            edited.Nodes[0].Rewards.Any(r => r.IsKnown && r.Kind == PredictorRewardKind.Potion && r.Value!.Status == PredictorRewardStatus.Taken),
            "An authored discard frees the exact observed slot before the explicit receipt");
        Check(edited.Nodes[0].Inventory!.PotionSlots[0] is { IsKnown: true, Value: not null } received &&
            received.Value.Id != bottle.Id && run.Working.PotionSlots[0] == bottle &&
            edited.Nodes[1].Inventory!.PotionSlots.Any(p => p.IsKnown && p.Value?.Id == received.Value.Id),
            "Forecast inventories and later route nodes reflect the new bottle while the actual run remains unchanged");
        var generated = edited.Nodes[0].PotionBoundaries.Single(b => b.Stage == PredictorRoutePotionStage.BeforeLeave).Before.PotionSlots[0].Value!;
        var discardNew = node with { PotionActions = node.PotionActions.Add(new(PredictorRoutePotionStage.BeforeLeave,
            PredictorRoutePotionActionKind.Discard, 0, generated, PredictorPhase.Rewards)) };
        var afterPickup = Pure(run, [discardNew], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(afterPickup.Nodes[0].PotionBoundaries.Single(b => b.Stage == PredictorRoutePotionStage.BeforeLeave).AppliedActions == 1 &&
            afterPickup.Nodes[0].Inventory!.PotionSlots[0] is { IsKnown: true, Value: null } &&
            afterPickup.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown && r.Value!.Status == PredictorRewardStatus.Open),
            "Discarding an explicitly acquired bottle preserves later drop display without default slot management");
        var stale = Pure(run, [node with { PotionActions = [node.PotionActions[0] with
            { ExpectedPotion = bottle with { Id = bottle.Id + 1000 } }] }], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(stale.Nodes[0].PotionBoundaries[0].AppliedActions == 0 && stale.Nodes[0].PendingCauses.Any(c => c.Code == "PlannedPotionBindingPending") &&
            stale.Nodes[0].Inventory!.PotionSlots[0].Value == bottle,
            "A stale potion identity stays pending instead of rebinding to the present slot occupant");
        var fruit = run.Working.PotionSlots[1]!;
        var used = Pure(run, [new(run.Working.Position.NodeId) { PotionActions = [new(PredictorRoutePotionStage.BeforeChoices,
            PredictorRoutePotionActionKind.Use, 1, fruit, PredictorPhase.Rewards)] }], 1, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(used.Nodes[0].PotionBoundaries[0].AppliedActions == 1 && used.Nodes[0].PotionBoundaries[0].After.PotionSlots[1].Value == null,
            "Explicit supported out-of-combat use reuses the detached runtime action and frees its slot");
        var sozu = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "WHITE_BEAST_STATUE", "SOZU");
        var denied = Pure(sozu, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(denied.Nodes.Length == 3 && !denied.PendingCauses.Any(c => c.Code == "PendingPotionChoice") &&
            denied.Nodes[0].Inventory!.PotionSlots.All(p => p.Value == null),
            "Sozu blocks default procurement without inventing occupied slots or stopping independent route projection");
        var sourceUnknown = PredictorRouteCauseEvidence.CreateExplicit("PotionInventoryUnknown", "capture:potions",
            PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
            "Captured capacity is known, but its occupied slots have not been observed.", ["potions.orderedSlots"]);
        var unknownCaptured = new PredictorRun(captured.Context with { Live = new(1, true, [sourceUnknown]) }, captured.Working);
        var unknownRun = Route(unknownCaptured);
        var unknown = Pure(unknownRun, [], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Inventory!;
        Check(unknown.PotionCapacity == unknownRun.Working.PotionSlots.Length && unknown.PotionSlots.IsEmpty &&
            unknown.PotionPendingCauses.Any(cause => cause.Identity == sourceUnknown.Identity),
            "Unknown occupancy preserves independently captured capacity and its original cause without fabricating empty bottles");
    }
    private static PredictorPlannedRestAction Smith(PredictorRun run)
    {
        var card = run.Working.Deck.Select(run.Working.Card).First(c => c.UpgradeLevel < c.MaxUpgradeLevel);
        return new(PredictorRestAction.Smith) { SmithCard = new(card.Id, card.Key, card.FloorAdded) };
    }

    private static PredictorRouteContinuation Pure(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans, int limit = 3,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices)
    {
        var state = JsonSerializer.Serialize(run.Working);
        var checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request;
        var phase = run.Phase;
        var result = run.PreviewRouteContinuation(plans, limit, policy: policy);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            request == run.Request && phase == run.Phase, "Route projection must preserve actual detached state/checkpoints/request");
        Check(result.Nodes.SelectMany(n => n.Rewards).Where(r => r.Value != null).All(r => r.Value!.Id == 0 &&
            r.Value.ContentInstanceId == null && r.Value.Cards.All(c => c.Id == 0)), "Route observations cannot expose actionable witness IDs");
        return result;
    }

    private static void LiveMapUnselectedRest(PredictorRun captured)
    {
        var run = Route(captured);
        var result = Pure(run, [], 4, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var firstCard = result.Nodes[0].Rewards.First(r => r.IsKnown && r.Kind == PredictorRewardKind.Card);
        var skipPlan = new PredictorPlannedNode(run.Working.Position.NodeId)
            { RewardChoices = [new(firstCard.Value!, firstCard.Occurrence, Skip: true)] };
        var explicitSkip = Pure(run, [skipPlan], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(result.Nodes[0].Rewards.Any(r => r.IsKnown && r.Kind == PredictorRewardKind.Card &&
                r.Value!.Status == PredictorRewardStatus.Skipped && r.DefaultSkipped &&
                r.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault && r.AppliedChoice == null) &&
            explicitSkip.Nodes[0].Rewards.Any(r => r.IsKnown && r.Kind == PredictorRewardKind.Card &&
                r.Value!.Status == PredictorRewardStatus.Skipped) &&
            !explicitSkip.Nodes[2].DeckPendingCauses.Any(c => c.Code == "PendingCardChoice" && c.NodeId == run.Working.Position.NodeId),
            "Live default and explicit Skip publish final disposition; default Skip retains approved policy provenance");
        Check(result.Nodes.Length == 4 && result.Nodes[1].Advanced && result.Nodes[1].AppliedRestActions == 0 &&
            result.Nodes[1].RestOffers.Any(o => o.Action == PredictorRestAction.Smith) &&
            result.Nodes[1].RestDefaultApplied && result.Nodes[1].DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null } &&
            !result.Nodes[1].PendingCauses.Any(c => c.Code == "RestChoice"),
            "Unselected rest retains Smith without a chosen target or authored action");
        Check(result.Nodes[^1].Room == PredictorRoomKind.Boss && result.Nodes[^1].Rewards.Any(r =>
            r.Kind == PredictorRewardKind.Card && r.IsKnown), "Basic rest's missing smith target cannot hide independent Boss cards");
        Check(!result.Nodes[2].DeckPendingCauses.Any(c => c.Code == "PendingCardChoice"),
            "Live ordinary card defaults no longer propagate an unselected combat reward choice");
        var missingSmith = new PredictorPlannedNode(RestNode) { RestActions = [new(PredictorRestAction.Smith)], LeaveRest = true };
        var selectedAction = Pure(run, [missingSmith], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(selectedAction.Nodes.Length == 3 && selectedAction.Nodes[1].AppliedRestActions == 1 &&
            selectedAction.Nodes[1].DeckPendingCauses.Any(c => c.Code == "PlannedRestTargetPending") &&
            selectedAction.Nodes[1].RestCards.Any(c => c.IsKnown && c.Value.UpgradeLevel < c.Value.MaxUpgradeLevel) &&
            selectedAction.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.IsKnown),
            "Chosen Smith with no target leaves the target pending while later independent card offers continue");
        var dream = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "DREAM_CATCHER");
        var dependent = Pure(dream, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[^1];
        Check(dependent.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown) &&
            !dependent.PendingCauses.Any(c => c.Code == "RestChoice"),
            "Approved Smith does not spread Dream Catcher's unchosen heal reward uncertainty into later cards");
        var actual = Route(captured);
        ExecuteToNextRewards(actual, actual.DefaultCombatInput()!, false);
        SameKnownRewards(result.Nodes[2], actual);
        var liveCause = new PredictorCombatPendingCause("LiveNichePending", "Captured Niche cursor unavailable", PredictorCombatDomain.NicheRng)
            { NodeId = "live-origin" };
        var live = new PredictorRun(captured.Context with { Live = new(1, true, [liveCause]) }, captured.Working);
        var seeded = Pure(Route(live, "FUZZY_WURM_CRAWLER_WEAK", "WING_CHARM"), [], 1,
            PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(seeded.Rewards.Any(r => r.Kind == PredictorRewardKind.Card && !r.IsKnown &&
                r.PendingCauses.Any(c => c.Code == "LiveNichePending")) &&
            seeded.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown),
            "Live source dependencies seed projection and affect only their actual later readers");
    }

    internal static void RunLiveRestDefaults(PredictorRun captured)
    {
        LiveDefaultRestPolicy(captured);
        LiveMapUnselectedRest(captured);
        Console.WriteLine("RUNTIME_LIVE_REST_DEFAULT_PASS no representative target; ordinary rewards; explicit input precedence; no legal target; provenance; caller unchanged");
    }

    private static void LiveDefaultRestPolicy(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog!;
        var strike = captured.Working.Deck.Select(captured.Working.Card).First(c => c.Key == catalog.CharacterStrikeKey);
        var defend = captured.Working.Deck.Select(captured.Working.Card).First(c => c.Key == catalog.CharacterDefendKey);
        PredictorRun Fixture(ImmutableArray<PredictorCard> cards, bool twoRests = false,
            string[]? relics = null, PredictorRun? source = null) =>
            Route(source ?? captured, "FUZZY_WURM_CRAWLER_WEAK", relics ?? [], state => state with
            {
                Cards = cards, Deck = cards.Select(c => c.Id).ToImmutableArray(),
                Maps = twoRests ? state.Maps.Select(m => m with { Map = m.Map with
                { Points = m.Map.Points.Select(p => p.Position.Row == 3 ? p with { Type = MapPointType.RestSite } : p).ToArray() } }).ToImmutableArray() : state.Maps
            });
        PredictorRouteContinuation Preview(PredictorRun run, params PredictorPlannedNode[] plans) =>
            Pure(run, [.. plans], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);

        var source = Fixture([defend, strike]);
        var basic = Preview(source);
        var rest = basic.Nodes[1];
        Check(rest.RestDefaultApplied && rest.DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null } &&
            rest.AppliedRestActions == 0 && rest.Cards.All(c => !c.IsKnown && c.Value.UpgradeLevel == 0) &&
            rest.RestCards.All(c => c.IsKnown) && basic.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.IsKnown) &&
            source.Working.Card(strike.Id).UpgradeLevel == strike.UpgradeLevel,
            "Default Smith leaves its target unresolved, upgrades no representative card, and preserves independent combat rewards");
        var defaultInputs = rest.Diagnostics!.Results.Select(rest.Diagnostics.Snapshot.Fact)
            .Where(f => f.PropertyKey.StartsWith("rest.compute.", StringComparison.Ordinal) &&
                (f.PropertyKey.EndsWith(".input.action", StringComparison.Ordinal) ||
                 f.PropertyKey.EndsWith(".input.command", StringComparison.Ordinal))).ToArray();
        Check(defaultInputs.Any(f => f.DisplayValue == "Smith") &&
            defaultInputs.All(f => f.Origin == PredictorKnowledgeOrigin.ApprovedDefault),
            "Default Smith and continuation carry ApprovedDefault provenance without an authored card target");
        var copies = Preview(Fixture([defend, strike], twoRests: true));
        Check(copies.Nodes.Skip(1).All(n => n.DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null }) &&
            copies.Nodes[2].Cards.All(c => c.Value.UpgradeLevel == 0),
            "Consecutive defaults do not spend or cycle through concrete card copies");
        var other = catalog.CardDefinitions.Single(d => d.Prototype.Key == new ModelKey("CARD", "POMMEL_STRIKE"))
            .Prototype with { Id = strike.Id, FloorAdded = strike.FloorAdded };
        Check(Preview(Fixture([other])).Nodes[1].DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null },
            "The default does not require any character starter identities");
        var maxed = PredictorCardLevelEffects.Reconcile(captured.Context, other with { UpgradeLevel = other.MaxUpgradeLevel });
        var noTarget = Preview(Fixture([maxed], relics: ["DREAM_CATCHER", "TINY_MAILBOX", "SHOVEL"]));
        Check(noTarget.Nodes.Length == 3 && noTarget.Nodes[1].RestDefaultApplied && noTarget.Nodes[1].DefaultRestAction == null &&
            noTarget.Nodes[1].RestHeal == null && noTarget.Nodes[1].Rewards.IsEmpty &&
            noTarget.Nodes[1].Cards.All(c => c.IsKnown) && !noTarget.PendingCauses.Any(c => c.Code == "RestChoice"),
            "A proven empty upgrade population continues without inventing an upgrade, Heal or Dig");
        var midas = new PredictorRun(captured.Context with
        { Modifiers = captured.Context.Modifiers.Add(new(new("MODIFIER", "MIDAS"))) }, captured.Working);
        var unavailable = Preview(Fixture([strike, defend], source: midas)).Nodes[1];
        Check(unavailable.RestDefaultApplied && unavailable.DefaultRestAction == null && unavailable.Cards.All(c => c.IsKnown),
            "Unavailable Smith does not acquire a fabricated target or substitute another action");
        var explicitDefend = new PredictorPlannedNode(RestNode)
        { RestActions = [new(PredictorRestAction.Smith) { SmithCard = new(defend.Id, defend.Key, defend.FloorAdded) }], LeaveRest = true };
        var selected = Preview(source, explicitDefend).Nodes[1];
        Check(!selected.RestDefaultApplied && selected.AppliedRestActions == 1 &&
            selected.Cards.Single(c => c.Value.Id == defend.Id) is { IsKnown: true, Value.UpgradeLevel: 1 } &&
            selected.Cards.Single(c => c.Value.Id == strike.Id).Value.UpgradeLevel == strike.UpgradeLevel,
            "An explicit target replaces the default uncertainty with its actual upgrade");
        Check(Preview(source, new PredictorPlannedNode(RestNode) { LeaveRest = true }).Nodes[1] is
            { RestDefaultApplied: false, DefaultRestAction: null } left && left.Cards.All(c => c.IsKnown),
            "Explicit Leave has no default upgrade");
        Check(Preview(source, new PredictorPlannedNode(RestNode)
            { RestActions = [new(PredictorRestAction.Heal)], LeaveRest = true }).Nodes[1] is
            { RestDefaultApplied: false, RestHeal: not null, AppliedRestActions: 1 } healed && healed.Cards.All(c => c.IsKnown),
            "Explicit Heal receives no additional default Smith");
    }

    private static void ExplicitReadyPrefix(PredictorRun captured)
    {
        var run = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "LAVA_LAMP");
        run.SetRoutePreviewPrefix(1, run.Routes.Single(r => r.Act == 1).Points.Take(3).ToImmutableArray());
        var before = JsonSerializer.Serialize(run.Working);
        var preview = run.PreviewRouteContinuation([], 8, scope: PredictorRoutePreviewScope.AllModeledNodes,
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults, allowSelectedPrefixes: true);
        Check(preview.Nodes.Length == 2 && preview.Nodes[^1].Room == PredictorRoomKind.RestSite &&
            !preview.PendingCauses.Any(c => c.Code == "RoutePlanNeedsUpdate") && before == JsonSerializer.Serialize(run.Working),
            "Explicit connected prefix stops at its selected end without filling a Boss suffix or changing the caller");
        var combat = preview.Nodes[0];
        Check(combat.CombatRequirements != null && combat.CombatCards.Select(c => c.Value.Id).SequenceEqual(run.Working.Deck) &&
            combat.CombatRelics is [{ IsKnown: true, Value.Key.Entry: "LAVA_LAMP" }],
            "Combat controls observe the known entry inventory before settlement and reward pickup");
        var isolated = Route(captured);
        isolated.SetRoutePreviewPrefix(1, isolated.Routes.Single(r => r.Act == 1).Points.Take(2).ToImmutableArray());
        var one = isolated.PreviewRouteContinuation([], 8, scope: PredictorRoutePreviewScope.AllModeledNodes,
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults, allowSelectedPrefixes: true);
        Check(one.Nodes.Length == 1 && one.Nodes[0].Room == PredictorRoomKind.Monster,
            "A one-node future prefix does not invent the following rest site");
    }

    private static void HealthAndUnpickedCards(PredictorRun captured)
    {
        var run = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "WHITE_BEAST_STATUE", "MEAT_ON_THE_BONE");
        var noRestChoice = Pure(run, []);
        Check(noRestChoice.Nodes.Length == 2 && noRestChoice.Nodes[^1].Room == PredictorRoomKind.RestSite &&
            noRestChoice.Nodes[^1].PendingCauses.Any(c => c.Code == "RestChoice") && !noRestChoice.Nodes[^1].Advanced,
            "A rest site without an authored action must stop locally, with no invented heal/smith default");
        var smith = Smith(run);
        var plans = ImmutableArray.Create(new PredictorPlannedNode(RestNode) { RestActions = [smith], LeaveRest = true });
        var partial = Pure(run, plans);
        Check(partial.Nodes.Length == 3 && partial.Nodes[0].Advanced && partial.Nodes[1].Advanced &&
            partial.Nodes[2].Room == PredictorRoomKind.Monster, "Unknown current combat HP must continue through authored smith to the next actual combat node");
        Check(partial.Nodes[1].DeckPendingCauses.Any(c => c.Code == "PendingCardChoice") &&
            partial.Nodes[1].Cards.Single(c => c.Value.Id == smith.SmithCard!.InstanceId) is { IsKnown: true } known && known.Value.UpgradeLevel > 0,
            "Unpicked prior cards must keep the deck incomplete without making the known existing smith target unknown");
        Check(partial.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown) &&
            partial.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown) &&
            partial.Nodes[2].PendingCauses.Any(c => c.Code == "VictoryHealth" && c.NodeId == run.Working.Position.NodeId),
            "Later independent gold/potion observations retain the original missing-health provenance");
        // Correcting an earlier hypothetical fact redraws the whole suffix.
        var corrected = Pure(run, plans.Add(new(run.Working.Position.NodeId)
        { Combat = new() { VictoryHealth = new(30, 80) }, LeaveRewards = true }));
        Check(corrected.Nodes.Length == 3 && !corrected.Nodes[^1].PendingCauses.Any(c =>
            c.Code == "VictoryHealth" && c.NodeId == run.Working.Position.NodeId), "Earlier fact correction must remove that source's cause from recomputed suffix");
        Check(plans[0].RestActions[0] == smith, "Recomputation must not change future authored choices");
    }

    private static void NicheAndUnknownTheft(PredictorRun captured)
    {
        var run = Route(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "WING_CHARM");
        var partial = Pure(run, [new(RestNode) { RestActions = [Smith(run)], LeaveRest = true }]);
        Check(partial.Nodes.Length == 3 && partial.Nodes[2].Rewards.Where(r => r.Kind == PredictorRewardKind.Card).Any(r =>
            !r.IsKnown && !r.CardOptions.IsEmpty && r.CardOptions.All(option => !option.IsKnown && option.Value.Enchantment == null) &&
            r.PendingCauses.Any(c => c.Code == "EnemyCreationCount" && c.NodeId == run.Working.Position.NodeId)) &&
            partial.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown),
            "Unknown prior Niche offset reaches Wing Charm, while later ordinary gold remains known");
        var theft = Route(captured, "THIEVING_HOPPER_WEAK", "WHITE_BEAST_STATUE");
        var blocked = Pure(theft, [new(RestNode) { RestActions = [Smith(theft)], LeaveRest = true }]);
        Check(blocked.Nodes.Length == 2 && blocked.Nodes[^1].PendingCauses.Any(c => c.Code == "PlannedRestTargetPending") &&
            blocked.Nodes[^1].Cards.All(c => !c.IsKnown), "Unknown theft cannot invent presence of a specific smith target");
    }

    private static void WingCharmRouteScopes(PredictorRun captured)
    {
        var run = Route(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "SILKEN_TRESS", "WING_CHARM");
        string first = run.Working.Position.NodeId;
        var missing = Pure(run, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(missing.Nodes.Length == 3 && missing.Nodes[0].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                .All(reward => reward.IsKnown && reward.CardOptions.All(option => option.WingCharmEligible == false)) &&
            missing.Nodes[2].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && !reward.IsKnown &&
                reward.CardOptions.All(option => option.WingCharmEligible == true && !option.IsKnown &&
                    option.EnchantmentPendingCauses.Any(cause => cause.Code == "EnemyCreationCount" && cause.NodeId == first))),
            "Actual Fabricator→rest→combat retains source cause; consumed Tress means zero first-node targets but multiple later targets");
        Check(missing.Nodes.Where(node => node.Room == PredictorRoomKind.Monster).All(node =>
            node.Rewards.Where(reward => reward.Kind is PredictorRewardKind.Gold or PredictorRewardKind.Potion).All(reward => reward.IsKnown)),
            "Unknown generated enemy count must not hide independent Rewards output at either actual combat node");
        foreach (int count in new[] { 0, 1, 4 })
        {
            var supplied = Pure(run, [new(first)
            { Combat = new() { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, count)] } }],
                3, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(supplied.Nodes.Length == 3 && supplied.Nodes[2].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                .All(reward => reward.IsKnown) && !supplied.PendingCauses.Any(cause => cause.Code == "EnemyCreationCount" && cause.NodeId == first),
                "Supplying the actual source count resolves its original cause throughout the recomputed route");
            if (count == 1)
            {
                var reward = supplied.Nodes[0].Rewards.Single(observation => observation.Kind == PredictorRewardKind.Card);
                Check(reward.Value!.CardRewardNicheScope == null,
                    "Display offers and authored expected identity must not retain ephemeral hook scope");
                var authored = ImmutableArray.Create(new PredictorPlannedNode(first)
                {
                    Combat = new() { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, count)] },
                    RewardChoices = [new(reward.Value!, reward.Occurrence, Skip: true)]
                });
                var persisted = Route(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "SILKEN_TRESS", "WING_CHARM");
                persisted.RestorePlannedNodes(authored);
                var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(persisted),
                    persisted.Context.CatalogFingerprint, persisted.Context.UnlockFingerprint);
                var rebound = Pure(restored, restored.PlannedNodes, 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
                Check(rebound.Nodes.Length == 3 && rebound.Nodes[0].Rewards.Any(observation =>
                        observation.Kind == PredictorRewardKind.Card && observation.AppliedChoice?.Skip == true) &&
                    !rebound.Nodes[0].PendingCauses.Any(cause => cause.Code == "PlannedRewardChanged"),
                    "Saved expected reward rebinds to regenerated scope without changing semantic offer identity");
            }
            for (int node = 0; node < 3; node += 2)
            {
                var incomplete = missing.Nodes[node].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
                var complete = supplied.Nodes[node].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
                Check(incomplete.CardOptions.Select(option => option.Value.Key).SequenceEqual(complete.Value!.Cards.Select(card => card.Key)) &&
                    incomplete.CardOptions.Select(option => option.Value.UpgradeLevel).SequenceEqual(complete.Value.Cards.Select(card => card.UpgradeLevel)),
                    "Filling the count changes only its true Niche dependents, preserving published card identities and upgrades");
                foreach (var reward in missing.Nodes[node].Rewards.Where(reward => reward.Kind is PredictorRewardKind.Gold or PredictorRewardKind.Potion))
                    Check(supplied.Nodes[node].Rewards.Any(actual => actual.Kind == reward.Kind && actual.Value?.Key == reward.Value!.Key &&
                        actual.Value?.GoldAmount == reward.Value.GoldAmount), "Independent Rewards results remain equal across concrete counts");
            }
        }
        var independent = Route(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE");
        var unhooked = Pure(independent, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(unhooked.Nodes.Length == 3 && unhooked.Nodes.SelectMany(node => node.Rewards)
                .All(reward => reward.IsKnown) && unhooked.Nodes[0].PendingCauses.Any(cause => cause.Code == "EnemyCreationCount"),
            "Without a real Niche-dependent reward hook, missing Fabricator count stays a source input remainder while every reward remains known");
    }

    private static void TentOrderAndActiveRewards(PredictorRun captured)
    {
        var run = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "MINIATURE_TENT", "DREAM_CATCHER", "TINY_MAILBOX");
        var plan = new PredictorPlannedNode(RestNode)
        { RestActions = [new(PredictorRestAction.Heal), Smith(run)], LeaveRewards = true, LeaveRest = true };
        var projected = Pure(run, [new(run.Working.Position.NodeId) { LeaveRewards = true }, plan]);
        Check(projected.Nodes.Length == 3 && projected.Nodes[1].AppliedRestActions == 2 && projected.Nodes[1].Advanced,
            "Tent applies ordered heal/reward-closure/smith actions on actual rest continuation");
        var combat = run.DefaultCombatInput()!;
        Check(run.Submit(run.Request!, combat) == PredictorInputResult.Accepted &&
            run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted, "Concrete combat fixture closure");
        run.AdvanceRoute();
        Check(run.Submit(run.Request!, new SelectRestAction(PredictorRestAction.Heal)) == PredictorInputResult.Accepted &&
            run.Phase == PredictorPhase.Rewards, "Fixture pauses at active manual fire rewards");
        var active = Pure(run, [plan], 2);
        Check(active.Nodes.Length == 2 && active.Nodes[0].AppliedRestActions == 2 && active.Nodes[0].Advanced,
            "An active rest reward phase resumes manual closure and the remaining Tent step without replaying heal twice");
    }

    private static void NextActAncientEntry(PredictorRun captured)
    {
        var firstMap = captured.RouteMap(1);
        var end = firstMap.Points.First(p => p.Type == MapPointType.Boss && p.Children.Count == 0);
        var state = captured.Working with
        {
            OpeningCompleted = true, Position = new(1, 18, "route-act-entry"),
            Maps = [new(1, firstMap, [end.Position])],
            Visits = [new(1, end.Position, MapPointType.Boss, PredictorRoomKind.Boss, null, 18)]
        };
        var run = new PredictorRun(captured.Context, state);
        var missing = run.PreviewRouteContinuation([], maximumNodes: 1, scope: PredictorRoutePreviewScope.AllModeledNodes);
        Check(missing.Nodes.IsEmpty && missing.PendingCauses.Any(c => c.Code == "NextActRouteRequired"),
            "An absent next-act route is a local authoring remainder, not a failed execution or invented route");
        var nextMap = run.RouteMap(2);
        var point = nextMap.Points.Single(p => p.Position.Row == 0);
        var path = ImmutableArray.CreateBuilder<MapPosition>();
        while (true)
        {
            path.Add(point.Position);
            if (point.Children.Count == 0) break;
            point = nextMap.Points.Single(p => p.Position == point.Children[0]);
        }
        run.SetRoute(2, path.ToImmutable());
        var before = JsonSerializer.Serialize(run.Working);
        var preview = run.PreviewRouteContinuation([], maximumNodes: 1, scope: PredictorRoutePreviewScope.AllModeledNodes);
        Check(preview.Nodes is [{ Room: PredictorRoomKind.Ancient, Ancient: not null }] &&
            preview.Nodes[0].Position.Act == 2 && preview.Nodes[0].Ancient!.Offers.Length > 0 &&
            before == JsonSerializer.Serialize(run.Working) && run.Checkpoints.Length == 1,
            "A known next-act route generates and observes its actual Ancient without committing the witness map");
        Check(run.PreviewRouteContinuation([], maximumNodes: 1).Nodes.IsEmpty,
            "The existing combat/rest UI consumer does not opt into new Ancient interactions");
    }

    private static void ChangedRewardPlan(PredictorRun captured)
    {
        var run = Route(captured);
        var first = Pure(run, [], 1).Nodes[0].Rewards.First(r => r.Kind == PredictorRewardKind.Card && r.IsKnown).Value!;
        var changed = first with { Cards = first.Cards.SetItem(0, first.Cards[0] with { UpgradeLevel = first.Cards[0].UpgradeLevel + 1 }) };
        var result = Pure(run, [new(run.Working.Position.NodeId)
        { RewardChoices = [new(changed, 0, CardIndex: 0)] }]);
        Check(result.Nodes.Length == 1 && result.Nodes[0].PendingCauses.Any(c => c.Code == "PlannedRewardChanged"),
            "Changed offers leave a saved semantic card choice pending instead of silently selecting a replacement");
    }

    private static void UnknownDeckRandomUpgrade(PredictorRun captured)
    {
        var run = Route(RewardPool(captured, "THE_HUNT"), "FUZZY_WURM_CRAWLER_WEAK", "WHITE_BEAST_STATUE", "FISHING_ROD");
        var result = Pure(run, [new(RestNode) { RestActions = [Smith(run)], LeaveRest = true }]);
        Check(result.Nodes.Length == 3 && result.Nodes[2].Cards.All(c => !c.IsKnown) &&
            result.Nodes[2].Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown),
            "Unknown prior card choice changes third Fishing Rod upgrade population, without hiding independent gold");
        Check(result.Nodes[2].PendingCauses.Any(c => c.Code == "PendingCardChoice" &&
            c.AffectedDomains.HasFlag(PredictorCombatDomain.ExtraCardRewards)),
            "Merging existing Fishing upgrade candidates must retain possible added Hunt source effects");
    }

    private static PredictorRun RewardPool(PredictorRun captured, string required)
    {
        var catalog = captured.Context.Catalog!;
        var all = catalog.AllCharacterCards.Concat(catalog.CharacterCards).Concat(catalog.ColorlessCards).DistinctBy(c => c.CardKey).ToArray();
        var key = new ModelKey("CARD", required);
        var ordinary = all.Where(c => c.CardKey != key && c.Rarity == EffectCardRarity.Common &&
            !PredictorCombatConsequences.IsTriggeredSource(catalog.CardDefinitions.Single(d => d.Prototype.Key == c.CardKey).Prototype) &&
            !catalog.CardDefinitions.Single(d => d.Prototype.Key == c.CardKey).IsQuest).Take(2);
        var pool = ordinary.Prepend(all.Single(c => c.CardKey == key)).Select(c => c with { Rarity = EffectCardRarity.Common }).ToImmutableArray();
        Check(pool.Length == 3, "Controlled reward fixture needs three actual source card definitions");
        return new PredictorRun(captured.Context with { Catalog = catalog with { CharacterCards = pool } }, captured.Working);
    }

    private static void IndependentDigAndPotionReceipt(PredictorRun captured)
    {
        var dig = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", ["SHOVEL"], state =>
            state with { PersonalBag = PredictorRelicBag.Empty(false) });
        var digPlan = new PredictorPlannedNode(RestNode) { RestActions = [new(PredictorRestAction.Dig)], LeaveRest = true };
        var ordinary = Pure(dig, [digPlan]);
        Check(ordinary.Nodes.Length == 3 && ordinary.Nodes[1].Advanced &&
            ordinary.Nodes[1].DeckPendingCauses.Any(c => c.Code == "PendingCardChoice"),
            "Known independent Circlet Dig obtain must continue despite unresolved prior cards, regardless of reward origin/pickup preference");

        var key = new ModelKey("RELIC", "WHETSTONE");
        var dependent = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", ["SHOVEL"], state => state with
        { PersonalBag = new(false, [], [new("Common", [key]), new("Uncommon", [key]), new("Rare", [key])], []) });
        var upgraded = Pure(dependent, [digPlan]);
        Check(upgraded.Nodes.Length == 3 && upgraded.Nodes[1].Advanced &&
            !upgraded.Nodes[1].PendingCauses.Any(c => c.Code == "RestObtainDependent") &&
            upgraded.Nodes[1].DeckPendingCauses.Any(c => c.Code == "PendingCardChoice") &&
            upgraded.Nodes[1].Inventory!.Relics.Any(r => r.Value.Key == key) &&
            upgraded.Nodes[1].Diagnostics!.After.Select(upgraded.Nodes[1].Diagnostics!.Snapshot.Fact).Any(f =>
                f.PropertyKey == "relics.orderedInventory" && f.Status == PredictorKnowledgeStatus.Known && f.DisplayValue!.Contains("WHETSTONE")),
            "Whetstone uses the correlated eligible population and keeps the original card choice unresolved while continuing independent results: " +
            $"nodes={upgraded.Nodes.Length}, advanced={upgraded.Nodes[1].Advanced}, causes={string.Join(',', upgraded.Nodes[1].PendingCauses.Select(c => c.Code))}, relics={string.Join(',', upgraded.Nodes[1].Inventory!.Relics.Select(r => r.Value.Key.Entry + ':' + r.IsKnown))}");

        var capsule = new ModelKey("RELIC", "SMALL_CAPSULE");
        var circlet = new ModelKey("RELIC", "CIRCLET");
        var manualFire = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", ["SHOVEL"], state => state with
        { PersonalBag = new(false, [], [new("Common", [capsule, circlet]), new("Uncommon", [capsule, circlet]), new("Rare", [capsule, circlet])], []) });
        var fireOffers = Pure(manualFire, [digPlan]);
        var child = fireOffers.Nodes[1].Rewards.Single(r => r.IsKnown && r.Kind == PredictorRewardKind.Relic);
        Check(child.Value!.Origin == PredictorRewardOrigin.SmallCapsule && child.Value.Key == circlet,
            "Dig capsule must expose its actual manually collected relic child");
        var collectedFire = Pure(manualFire, [digPlan with { RewardChoices = [new(child.Value, child.Occurrence)] }]);
        Check(collectedFire.Nodes.Length == 3 && collectedFire.Nodes[1].Advanced,
            "Independent manual fire relic obtain must continue on an incomplete deck without combat-origin automatic policy authority");

        var alchemize = Route(RewardPool(captured, "ALCHEMIZE"), "FUZZY_WURM_CRAWLER_WEAK", ["WHITE_BEAST_STATUE"],
            state => state with { PotionSlots = [null, null] });
        var rest = new PredictorPlannedNode(RestNode) { RestActions = [Smith(alchemize)], LeaveRest = true };
        var forecast = Pure(alchemize, [rest]);
        var next = forecast.Nodes[2];
        var potion = next.Rewards.Single(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown);
        Check(potion.Value!.Status == PredictorRewardStatus.Open && next.PendingCauses.Any(c => c.Code == "PotionReceiptPending") &&
            next.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown && r.Value!.Status == PredictorRewardStatus.Taken),
            "Unknown optional Alchemize can fill the last slot: keep the known potion offer open while independent automatic gold still settles");
        var explicitTake = Pure(alchemize, [rest, new(next.Position.NodeId)
        { RewardChoices = [new(potion.Value!, potion.Occurrence)] }]);
        Check(explicitTake.Nodes[^1].PendingCauses.Any(c => c.Code == "PlannedPotionReceiptPending") &&
            explicitTake.Nodes[^1].Rewards.Single(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown).Value!.Status == PredictorRewardStatus.Open,
            "An explicit potion take must also wait for actual slot availability instead of accepting scratch capacity");
    }

    private static void ConcreteAlternativeWitnesses(PredictorRun captured)
    {
        var partial = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "WHITE_BEAST_STATUE", "MEAT_ON_THE_BONE");
        var projected = Pure(partial, [new(RestNode) { RestActions = [Smith(partial)], LeaveRest = true }]).Nodes[2];
        foreach (var (hp, takeCard) in new[] { (1, false), (80, true) })
        {
            var actual = Route(captured, "FUZZY_WURM_CRAWLER_WEAK", "WHITE_BEAST_STATUE", "MEAT_ON_THE_BONE");
            var input = actual.ResolveCombatDefaults(new() { VictoryHealth = new(hp, 80) })!.Input!;
            ExecuteToNextRewards(actual, input, takeCard);
            SameKnownRewards(projected, actual);
        }
        var niche = Route(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "WING_CHARM");
        var nicheProjection = Pure(niche, [new(RestNode) { RestActions = [Smith(niche)], LeaveRest = true }]).Nodes[2];
        foreach (int count in new[] { 0, 2 })
        {
            var actual = Route(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "WING_CHARM");
            var input = actual.ResolveCombatDefaults(new()
            { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, count)] })!.Input!;
            ExecuteToNextRewards(actual, input, false);
            SameKnownRewards(nicheProjection, actual);
        }
    }

    private static void ExecuteToNextRewards(PredictorRun run, ResolveCombat first, bool takeNormalCard)
    {
        var smith = Smith(run).SmithCard!;
        Check(run.Submit(run.Request!, first) == PredictorInputResult.Accepted, "Concrete first combat witness");
        // Match the projection's approved automatic combat pickup preference.
        while (PredictorRewardPickupPolicy.Plan(run).NextInput is { } pickup)
            Check(run.Submit(run.Request!, pickup) == PredictorInputResult.Accepted, "Concrete automatic pickup witness");
        if (takeNormalCard)
        {
            var reward = run.Rewards.First(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open &&
                r.Cards.Any(c => !PredictorCombatConsequences.IsTriggeredSource(c) && !PredictorCardChanges.Definition(run.Context, c.Key).IsQuest));
            var card = reward.Cards.First(c => !PredictorCombatConsequences.IsTriggeredSource(c) && !PredictorCardChanges.Definition(run.Context, c.Key).IsQuest);
            Check(run.Submit(run.Request!, new EnterCardSelection(reward.Id)) == PredictorInputResult.Accepted &&
                run.Submit(run.Request!, new TakeReward(reward.Id, card.Id)) == PredictorInputResult.Accepted, "Concrete ordinary prior-card take witness");
        }
        Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted, "Concrete reward closure witness");
        run.AdvanceRoute();
        Check(run.Submit(run.Request!, new SelectRestAction(PredictorRestAction.Smith)) == PredictorInputResult.Accepted &&
            run.Submit(run.Request!, new SelectSmithCard(smith.InstanceId)) == PredictorInputResult.Accepted &&
            run.Submit(run.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted, "Concrete smith witness");
        run.AdvanceRoute();
        var next = run.ResolveCombatDefaults(new() { VictoryHealth = new(80, 80) })!.Input!;
        Check(run.Submit(run.Request!, next) == PredictorInputResult.Accepted, "Concrete next combat witness");
    }

    private static void SameKnownRewards(PredictorRouteNodeKnowledge projection, PredictorRun actual)
    {
        foreach (var knowledge in projection.Rewards.Where(r => r.IsKnown))
        {
            var expected = knowledge.Value!;
            Check(actual.Rewards.Any(r => r.Kind == expected.Kind && r.Key == expected.Key && r.GoldAmount == expected.GoldAmount &&
                r.WasGoldStolenBack == expected.WasGoldStolenBack &&
                r.Cards.Select(c => c with { Id = 0 }).SequenceEqual(expected.Cards)),
                "Partial known reward must equal the actual concrete alternative witness: " + expected.Kind);
        }
    }
}
