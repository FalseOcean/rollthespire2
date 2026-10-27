using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World.Beta109;

internal static class PredictorDrawingEventChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Drawing event: " + message); }

    internal static void Run(PredictorRun captured)
    {
        foreach (string entry in new[] { "ABYSSAL_BATHS", "TABLET_OF_TRUTH", "AROMA_OF_CHAOS", "SELF_HELP_BOOK" })
        {
            var run = Fixture(captured, entry);
            string before = JsonSerializer.Serialize(run.Working);
            var route = Preview(run, []);
            Check(route.Nodes.Length == 2 && route.Nodes[0].Advanced &&
                route.Nodes[0].Event is { BusinessChoiceRequired: false, AppliedActions: 0 } &&
                route.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                entry + " omits low-value options/loops while preserving independent combat Rewards");
            Check(before == JsonSerializer.Serialize(run.Working), entry + " leaves the captured run untouched");
            Check(!Preview(run, [], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Advanced,
                entry + " retains the explicit concrete route input boundary");
        }
        GoldBranches(captured);
        GoldSourcesDeferToConsumers(captured);
        ConveyorLocalPremises(captured);
        EventObtainHealthProjectionReplay(captured);
        OptionalGridReceipts(captured);
        EqualConsumption(captured);
        DeterministicConfirmation(captured);
        RepyReadsOnlyKeyMembership(captured);
        CombatBranchPreviews(captured);
        CombatRewardInputs(captured);
        CombatRewardFollowingCursor(captured);
        RerollKeepsIndependentPickup(captured);
        MultipleRerollsKeepTheirOwnOffers(captured);
        BridgeWithoutKnownTargets(captured);
        ActiveEventDisplayReceipts(captured);
        Console.WriteLine("RUNTIME_DRAWING_EVENT_PASS zero-consumption loops; deferred native gold sources and real event/shop consumers; candidate Skip/Lucky Fysh receipts; equal real Rewards consumption; deterministic confirmation and immediate combat branch rewards with real missing premises; authored-mode isolation");
    }

    private static PredictorRun Fixture(PredictorRun captured, string entry, bool lucky = false,
        string[]? followingEvents = null, int startFloor = 1,
        Func<PredictorState, PredictorState>? configure = null, MapPointType[]? routeRooms = null, int act = 2)
    {
        var types = routeRooms ?? new[] { MapPointType.Monster }.Concat(Enumerable.Repeat(MapPointType.Unknown,
            1 + (followingEvents?.Length ?? 0))).Concat([MapPointType.Monster, MapPointType.Boss]).ToArray();
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], GoldBudget = 1000,
            Position = new(act, startFloor, "drawing-event:entry"),
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Monster, PredictorRoomKind.Monster, null, startFloor)],
            VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
            Acts = captured.Working.Acts.Select(value => value.Act == act ? value with
            { RawEvents = new[] { entry }.Concat(followingEvents ?? []).Select(value => new ModelKey("EVENT", value)).ToImmutableArray(), EventCursor = 0,
                NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 } : value).ToImmutableArray()
        };
        if (lucky) state = state with { Relics = [new(state.NextInstanceId, new("RELIC", "LUCKY_FYSH"), 1)],
            NextInstanceId = state.NextInstanceId + 1 };
        state = configure?.Invoke(state) ?? state;
        var run = new PredictorRun(captured.Context, state);
        run.SetRoute(act, points.Select(point => point.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static PredictorRouteContinuation Preview(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.LiveMapDefaults)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints),
            draft = JsonSerializer.Serialize(run.CaptureNodeDraft()), authored = JsonSerializer.Serialize(plans);
        var request = run.Request;
        var phase = run.Phase;
        var result = run.PreviewRouteContinuation(plans, 2, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            draft == JsonSerializer.Serialize(run.CaptureNodeDraft()) && authored == JsonSerializer.Serialize(plans) &&
            request == run.Request && phase == run.Phase,
            "drawing preview preserves the source run, checkpoint, accepted prefix, request and authored plan");
        return result;
    }

    // Compare published behavior, excluding each forecast's fresh trace IDs.
    // Before/After facts include the observed RNG states and pending knowledge.
    private static string ResultValues(PredictorRouteContinuation route) => JsonSerializer.Serialize(route.Nodes.Select(node => new
    {
        node.Position, node.Room, node.Model, node.Phase, node.Advanced,
        Event = node.Event is not { } value ? null : new
        { value.Event, value.AppliedActions, value.NeededInput, value.GoldBalance, value.HealthRequired, value.HealthConsequences },
        Cards = node.Cards.Select(card => new { card.Value, card.IsKnown }),
        Rewards = (node.Event?.Rewards ?? node.Rewards).Select(reward => new
        { reward.Kind, reward.Value, reward.IsKnown, reward.Occurrence, reward.DefaultSkipped, reward.AppliedChoice }),
        Pending = PredictorRouteCauseEvidence.OriginalCauses(node.PendingCauses).Select(cause => new
        { cause.Code, cause.NodeId, cause.AffectedDomains, cause.ReasonKind, cause.RequiredPropertyKeys, Occurrence = cause.Choice?.Occurrence }),
        Facts = node.Diagnostics!.Before.Concat(node.Diagnostics.After).Select(node.Diagnostics.Snapshot.Fact).Select(fact => new
        { fact.NodeId, fact.Boundary, fact.PropertyKey, fact.Status, fact.Origin, fact.DisplayValue })
    }));

    private static PredictorPlannedNode Choose(PredictorRouteNodeKnowledge node, string key) =>
        new(node.Position.NodeId) { Event = new(node.Event!.Event!.Value)
        { Actions = [new(PredictorEventActionKind.Option)
            { ExpectedOption = node.Event.EntryOptions.Single(option => option.Key == key).Value! }] } };

    internal static void RepyReadsOnlyKeyMembership(PredictorRun captured)
    {
        var source = new PredictorRun(captured.Context with { Live = null, Operation = null, Modifiers = [],
            NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns) }, captured.Checkpoints[0].State);
        PredictorState Keys(PredictorState state)
        {
            state = state with { PotionSlots = [null, null, null],
                PersonalBag = new(false, [], [new("Common", [new("RELIC", "ANCHOR"), new("RELIC", "BAG_OF_MARBLES")])], []) };
            for (int i = 0; i < 2; i++)
                state = PredictorObtainSources.AddCard(source.Context, state, new ModelKey("CARD", "LANTERN_KEY"));
            return state;
        }
        var run = Fixture(source, "AROMA_OF_CHAOS", act: 3, configure: Keys,
            routeRooms: [MapPointType.Monster, MapPointType.RestSite, MapPointType.Unknown, MapPointType.Monster, MapPointType.Boss]);
        var smith = new PredictorPlannedNode(run.Working.Position.NodeId)
        { RestActions = [new(PredictorRestAction.Smith)], LeaveRest = true };
        var initial = Preview(run, [smith]);
        Check(initial.Nodes.Length == 2 && initial.Nodes[0].Advanced &&
            initial.Nodes[0].PendingCauses.Any(cause => cause.Code == "PlannedRestTargetPending") &&
            initial.Nodes[1].Event?.Event?.Entry == "WAR_HISTORIAN_REPY",
            "an actual omitted Smith target retains known Lantern membership and the act-three Repy occurrence");
        foreach (string branch in new[] { "INITIAL_UNLOCK_CAGE", "INITIAL_UNLOCK_CHEST" })
        {
            var plan = Choose(initial.Nodes[1], branch);
            string before = JsonSerializer.Serialize(run.Working);
            var result = Preview(run, [smith, plan]);
            var repy = result.Nodes[1];
            Check(repy.Advanced && repy.Event is { Advanced: true, AppliedActions: 1 } &&
                repy.Cards.All(card => card.Value.Key.Entry != "LANTERN_KEY") &&
                repy.Event.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Relic && reward.IsKnown) == 2 &&
                repy.Event.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Potion && reward.IsKnown) == 2 &&
                repy.Event.AppliedResult!.ObtainedRelics.Any(relic => relic.Key.Entry == "HISTORY_COURSE"),
                branch + " ignores unrelated Smith precision, removes both real keys and automatically confirms the other reward");
            Check(before == JsonSerializer.Serialize(run.Working) && plan.Event!.Actions.Length == 1,
                branch + " keeps the caller and authored business prefix unchanged");
            Check(!Preview(run, [smith, plan], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Advanced,
                "Repy's precise reader does not turn an authored Smith target into a default");
        }

        // Isolate the option guard using an explicit modeled-event queue in
        // act two: act-three unknown-room admission already blocks uncertain
        // Lantern membership before reaching Repy (covered by occurrence checks).
        var missing = PredictorRouteCauseEvidence.CreateExplicit("RepyKeyMembershipMissing", "capture:repy",
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation,
            "The key population was not observed.", ["deck.All.candidates"]);
        var uncertain = new PredictorRun(source.Context with { Live = new(2, true, [missing]) }, source.Working);
        var guarded = Fixture(uncertain, "WAR_HISTORIAN_REPY", configure: Keys);
        var guardedNode = Preview(guarded, []).Nodes[0];
        foreach (string branch in new[] { "INITIAL_UNLOCK_CAGE", "INITIAL_UNLOCK_CHEST" })
        {
            var result = Preview(guarded, [Choose(guardedNode, branch)]).Nodes[0];
            Check(!result.Advanced && result.Event!.PendingCauses.Any(cause => cause.Code == "PlannedEventActionDependent") &&
                PredictorRouteCauseEvidence.OriginalCauses(result.PendingCauses).Any(cause => cause.Code == missing.Code),
                branch + " retains the real missing Lantern population prerequisite instead of accepting its witness");
        }
    }

    private static void ActiveEventDisplayReceipts(PredictorRun captured)
    {
        foreach (var (entry, option) in new[] { ("MORPHIC_GROVE", "GROUP"), ("PUNCH_OFF", "NAB") })
        {
            var run = Fixture(captured, entry, startFloor: 7);
            var initial = Preview(run, []).Nodes[0];
            var plan = Choose(initial, option);
            if (run.ObserveEventOccurrence() is { } occurrence)
            {
                var candidate = occurrence.Candidates.First();
                Check(run.Submit(run.Request!, new ChooseEventOccurrence(candidate.Event, candidate.QueueEvent,
                    candidate.RawOrdinal, occurrence.Cursor, candidate.Cursor)) == PredictorInputResult.Accepted,
                    entry + " enters its actual active occurrence");
            }
            Check(run.Submit(run.Request!, new ChooseEventOption(option)) == PredictorInputResult.Accepted &&
                run.Phase != PredictorPhase.Complete, entry + " retains a real target/reward child after accepting its option");
            string unchanged = JsonSerializer.Serialize(run.Working);
            var projected = Preview(run, [plan]).Nodes[0].Event!;
            Check(projected.AppliedActions >= 1 && projected.AppliedResult != null,
                entry + " exposes the already accepted active-prefix receipt; sourcePhase=" + run.Phase +
                "; applied=" + projected.AppliedActions + "; result=" + (projected.AppliedResult != null) +
                "; advanced=" + projected.Advanced + "; needed=" + projected.NeededInput +
                "; retained=" + projected.AuthoredActionsNotApplied +
                "; rewards=" + string.Join(',', projected.Rewards.Select(reward => reward.Kind + ":" + reward.Value?.Status)) +
                "; pending=" + string.Join(',', projected.PendingCauses.Select(cause => cause.Code)));
            if (entry == "MORPHIC_GROVE")
                Check(projected.AppliedResult is { AllGoldLost: true, GoldChange: -1000 },
                    "active Morphic replay observes the actual pre-option budget, rather than subtracting zero from zero");
            else
                Check(projected.AppliedResult!.GeneratedCards.Any(card => card.IsKnown && card.Value.Key.Entry == "INJURY"),
                    "active PunchOff replay retains the already issued curse rather than diffing its post-option deck against itself");
            Check(unchanged == JsonSerializer.Serialize(run.Working), entry + " display replay leaves its actual active source unchanged");
        }
    }

    private static void ConveyorLocalPremises(PredictorRun captured)
    {
        var run = Fixture(captured, "ENDLESS_CONVEYOR", configure: state => state with
            { PotionSlots = [null, null] });
        string before = JsonSerializer.Serialize(run.Working);
        var initial = Preview(run, []).Nodes[0];
        var input = new PredictorConveyorInput(2, 20);
        var action = new PredictorPlannedEventAction(PredictorEventActionKind.ConveyorInputs) { Conveyor = input };
        var plan = new PredictorPlannedNode(initial.Position.NodeId)
            { Event = new(new("EVENT", "ENDLESS_CONVEYOR")) { Actions = [action] } };
        var reduced = Preview(run, [plan]).Nodes[0];
        var fullPlan = plan with { Event = plan.Event! with
            { Actions = [new(PredictorEventActionKind.Health) { Health = new(60, 80) }] } };
        var full = Preview(run, [fullPlan]).Nodes[0];
        Check(reduced.Event is { HealthRequired: false, ConveyorLostHp: 20 } && reduced.Event.ConveyorInput == input &&
            reduced.Event.Options.Select(option => option.Value).SequenceEqual(full.Event!.Options.Select(option => option.Value)),
            "native Conveyor's local lost-HP/slot premise reproduces the actual full-health weighted offer");
        var chefOption = reduced.Event!.Options.Single(option => option.Key == "OBSERVE_CHEF").Value!;
        var chosen = Preview(run, [plan with { Event = plan.Event! with
            { Actions = [action, new(PredictorEventActionKind.Option) { ExpectedOption = chefOption }] } }]);
        Check(chosen.Nodes.Length == 2 && chosen.Nodes[0].Advanced && chosen.Nodes[0].Event!.ConveyorInput == input &&
            chosen.Nodes[0].Event!.ConveyorLostHp == 20 &&
            chosen.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
            before == JsonSerializer.Serialize(run.Working),
            "native Chef consumes the actual local plan, retains its adopted receipt and continues independent combat without mutating capture");
        var occurrence = initial.Event!.Occurrence!;
        var selected = initial.Event.SelectedOccurrence!;
        var authored = Preview(run, [plan with { Event = plan.Event! with
        { Occurrence = new(selected.Event, selected.QueueEvent, selected.RawOrdinal, occurrence.Cursor, selected.Cursor) } }],
            PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0];
        Check(!authored.Advanced && authored.Event is { HealthRequired: true, ConveyorInput: null },
            "native full Authored Conveyor still requires actual current/max HP");
    }

    private static void EventObtainHealthProjectionReplay(PredictorRun captured)
    {
        PredictorRun? run = null;
        PredictorPlannedNode? buyPlan = null;
        PredictorEventNodeKnowledge? initialEvent = null;
        // Keep the real nine-item shuffle/six-slot stock. Search a bounded set
        // of complete seed contexts for an actual Waffle offer, never inject it.
        foreach (string seed in new[] { captured.Context.Seed }.Concat(Enumerable.Range(1, 24).Select(value => value.ToString("D12"))))
        {
            var context = captured.Context with { Seed = seed,
                AncientAuthority = captured.Context.AncientAuthority is { } authority
                    ? Beta109WorldSnapshotProjector.ProjectForSeed(authority, seed) : null };
            var probe = Fixture(new PredictorRun(context, captured.Working), "FAKE_MERCHANT");
            var first = Preview(probe, []).Nodes[0];
            var open = Choose(first, "OPEN_INVENTORY");
            var stock = Preview(probe, [open]).Nodes[0];
            var waffle = stock.Event!.Options.FirstOrDefault(option => option.IsKnown &&
                option.Value?.OfferedRelic?.Entry == "FAKE_LEES_WAFFLE");
            if (waffle == null) continue;
            run = probe; initialEvent = first.Event;
            buyPlan = open with { Event = open.Event! with
                { Actions = open.Event.Actions.Add(new(PredictorEventActionKind.Option) { ExpectedOption = waffle.Value }) } };
            break;
        }
        Check(run != null && buyPlan != null, "bounded native Fake Merchant stocks expose an actual Fake Lees Waffle");
        var actualRun = run!; var purchasePlan = buyPlan!;
        string before = JsonSerializer.Serialize(actualRun.Working), checkpoints = JsonSerializer.Serialize(actualRun.Checkpoints);
        var buying = Preview(actualRun, [purchasePlan]);
        var bought = buying.Nodes[0];
        Check(!bought.Advanced && bought.Event is { AppliedActions: 2, NeededInput: PredictorEventNeededInput.Option } &&
            bought.Event.RequiredChoices.IsEmpty && bought.Event.Options.Any(option => option.Key == "CLOSE_INVENTORY") &&
            bought.ObtainHealthEffects.Single() is { Source.Entry: "FAKE_LEES_WAFFLE",
                Kind: PredictorObtainHealthEffectKind.HealMaxHpFraction, Amount: 10 },
            "actual Waffle purchase returns to the inventory after default health projection and remains observable");
        var close = bought.Event!.Options.Single(option => option.Key == "CLOSE_INVENTORY").Value!;
        var closedPlan = purchasePlan with { Event = purchasePlan.Event! with
            { Actions = purchasePlan.Event.Actions.Add(new(PredictorEventActionKind.Option) { ExpectedOption = close }) } };
        var closed = Preview(actualRun, [closedPlan]).Nodes[0];
        Check(closed.Event is { AppliedActions: 3, NeededInput: PredictorEventNeededInput.Option } &&
            closed.Event.Options.Any(option => option.Key == "OPEN_INVENTORY") && closed.ObtainHealthEffects.Length == 1,
            "a later Close input replays the exact earlier implicit health receipt before its actual source position");
        var leave = closed.Event!.Options.Single(option => option.Key == "LEAVE").Value!;
        var completed = Preview(actualRun, [closedPlan with { Event = closedPlan.Event! with
            { Actions = closedPlan.Event.Actions.Add(new(PredictorEventActionKind.Option) { ExpectedOption = leave }) } }]);
        Check(completed.Nodes.Length == 2 && completed.Nodes[0].Advanced && completed.Nodes[0].Event!.AppliedActions == 4 &&
            completed.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "native Waffle buy/close/leave preserves the independent combat suffix");

        var health = new PredictorPlannedEventAction(PredictorEventActionKind.Obtain)
        { Obtain = new(PredictorObtainChildAction.Health, new("RELIC", "FAKE_LEES_WAFFLE")) { Health = new(40, 80) } };
        var explicitHealth = Preview(actualRun, [closedPlan with { Event = closedPlan.Event! with
            { Actions = closedPlan.Event.Actions.Insert(2, health) } }]).Nodes[0];
        Check(explicitHealth.Event is { AppliedActions: 4, NeededInput: PredictorEventNeededInput.Option } &&
            explicitHealth.ObtainHealthEffects.IsEmpty && explicitHealth.Event.HealthConsequences.Contains(new(8, 0)),
            "an explicit health child still uses its actual eight-HP heal and is not replaced by a default projection");
        var occurrence = initialEvent!.Occurrence!; var selected = initialEvent.SelectedOccurrence!;
        var authoredPlan = purchasePlan with { Event = purchasePlan.Event! with
        { Occurrence = new(selected.Event, selected.QueueEvent, selected.RawOrdinal, occurrence.Cursor, selected.Cursor) } };
        var authored = Preview(actualRun, [authoredPlan], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0];
        Check(!authored.Advanced && authored.Event!.RequiredChoices.Single().Phase == PredictorPhase.ObtainHealth,
            "full Authored Fake Merchant keeps its actual complete health input boundary");
        Check(before == JsonSerializer.Serialize(actualRun.Working) && checkpoints == JsonSerializer.Serialize(actualRun.Checkpoints),
            "health projection observation/replay leaves the actual captured state and checkpoints unchanged");
    }

    private static void GoldBranches(PredictorRun captured)
    {
        var run = Fixture(captured, "COLOSSAL_FLOWER");
        var first = Preview(run, []).Nodes[0];
        Check(!first.Advanced && first.Event is { BusinessChoiceRequired: true, GoldBalance: 1000 },
            "Flower still has its independent Pollinous Core relic business branch");
        var take = Preview(run, [Choose(first, "EXTRACT")]);
        Check(take.Nodes.Length == 2 && take.Nodes[0].Event!.GoldBalance == 1035,
            "explicit flower extraction grants the actual 35 gold");
        var deeper = Preview(run, [Choose(first, "REACH_DEEPER")]);
        Check(!deeper.Nodes[0].Advanced && deeper.Nodes[0].Event!.BusinessChoiceRequired &&
            deeper.Nodes[0].Event!.Options.Any(option => option.Value?.GoldGain == 75),
            "a later flower page retains its changed gold decision");
    }

    private static void GoldSourcesDeferToConsumers(PredictorRun captured)
    {
        static string Describe(PredictorRouteContinuation route) => string.Join('|', route.Nodes.Select(node =>
            $"{node.Room}/{node.Phase}/advanced={node.Advanced}/gold={node.Event?.GoldBalance ?? node.Shop?.GoldBalance}/" +
            "pending=" + string.Join(',', node.PendingCauses.Select(cause => cause.Code))));
        var run = Fixture(captured, "JUNGLE_MAZE_ADVENTURE", configure: state => state with { GoldBudget = 0 });
        string before = JsonSerializer.Serialize(run.Working);
        var projected = Preview(run, []);
        var first = projected.Nodes[0];
        Check(projected.Nodes.Length == 2 && first.Advanced && first.Event is
            { Advanced: true, AppliedActions: 0, BusinessChoiceRequired: false, GoldBalance: null } &&
            first.Event.GoldPendingCauses.Any(cause => cause.NodeId == first.Position.NodeId &&
                (cause.AffectedDomains & PredictorCombatDomain.Gold) != 0) &&
            projected.Nodes[1].Advanced && projected.Nodes[1].Rewards.Any(reward =>
                reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "unselected native Jungle gold alternatives retain unknown gold while following independent combat cards continue; " + Describe(projected));
        Check(!Preview(run, [], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Advanced &&
            before == JsonSerializer.Serialize(run.Working),
            "gold deferral neither completes AuthoredChoices nor mutates the actual caller");

        var eventRun = Fixture(captured, "JUNGLE_MAZE_ADVENTURE",
            followingEvents: ["MORPHIC_GROVE", "BUGSLAYER"],
            configure: state => state with { GoldBudget = 0 },
            routeRooms: [MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster,
                MapPointType.Unknown, MapPointType.Monster, MapPointType.Boss]);
        var eventResult = eventRun.PreviewRouteContinuation([], 5,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(eventResult.Nodes.Length == 3 && eventResult.Nodes[0].Advanced && eventResult.Nodes[1].Advanced &&
            !eventResult.Nodes[2].Advanced && eventResult.Nodes[2].Event!.EntryOptions.IsEmpty &&
            eventResult.Nodes[2].PendingCauses.Any(cause => cause.Code == "EventGoldAppearancePending" &&
                PredictorRouteCauseEvidence.OriginalCauses([cause]).Any(source =>
                    source.NodeId == eventResult.Nodes[0].Position.NodeId && source.Code == "EventOptionChoice")),
            "a real Morphic appearance threshold consumes the unresolved gold source after the independent combat, before native event entry; " + Describe(eventResult));

        var shopRun = Fixture(captured, "JUNGLE_MAZE_ADVENTURE", configure: state => state with { GoldBudget = 0 },
            routeRooms: [MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster,
                MapPointType.Shop, MapPointType.Monster, MapPointType.Boss]);
        var shopResult = shopRun.PreviewRouteContinuation([], 4,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        var shopNode = shopResult.Nodes[2];
        Check(shopResult.Nodes.Length == 4 && shopNode is { Advanced: true, Shop.GoldBalance: null } &&
            shopNode.Shop.Offers.Any(offer => offer.IsKnown) && shopNode.Shop.Offers.All(offer => offer.Affordable == null) &&
            shopResult.Nodes[3].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "unknown gold masks actual shop affordability while its independent stock and no-purchase continuation remain truthful; " + Describe(shopResult));
        var offer = shopNode.Shop!.Offers.First(value => value.IsKnown && value.Kind == PredictorShopEntryKind.CharacterCard);
        var buy = new PredictorPlannedNode(shopNode.Position.NodeId) { Shop = new()
            { Actions = [new(PredictorShopActionKind.Buy) { ExpectedOffer = offer.Value }] } };
        var buying = shopRun.PreviewRouteContinuation([buy], 4,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(buying.Nodes.Length == 3 && !buying.Nodes[2].Advanced && buying.Nodes[2].Shop!.AppliedActions == 0 &&
            buying.Nodes[2].PendingCauses.Any(cause => cause.Code == "PlannedShopAffordabilityPending" &&
                PredictorRouteCauseEvidence.OriginalCauses([cause]).Any(source =>
                    source.NodeId == buying.Nodes[0].Position.NodeId && source.Code == "EventOptionChoice")),
            "a real shop purchase stops at its gold consumer with the original Jungle choice cause; " + Describe(buying));
        var join = Choose(shopResult.Nodes[0], "JOIN_FORCES");
        var resolved = shopRun.PreviewRouteContinuation([join], 4,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(resolved.Nodes[2].Shop is { GoldBalance: not null, GoldPendingCauses.IsEmpty: true } known &&
            known.Offers.All(value => value.Affordable.HasValue),
            "explicitly resolving the source restores exact downstream shop affordability");

        var differentStreams = Preview(Fixture(captured, "WHISPERING_HOLLOW"), []).Nodes[0];
        Check(!differentStreams.Advanced && differentStreams.Event!.BusinessChoiceRequired,
            "removing the gold requirement does not join branches with distinct real public Rewards consumption");
        var treasury = Preview(Fixture(captured, "SUNKEN_TREASURY"), []);
        Check(treasury.Nodes.Length == 2 && treasury.Nodes[0].Advanced && treasury.Nodes[0].Event!.GoldBalance == null &&
            treasury.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "Treasury gold and its real optional curse remain correlated without forcing a gold-source selection; " + Describe(treasury));
    }

    private static void OptionalGridReceipts(PredictorRun captured)
    {
        var run = Fixture(captured, "BRAIN_LEECH", lucky: true);
        var first = Preview(run, []).Nodes[0];
        var plan = Choose(first, "SHARE_KNOWLEDGE");
        var skipped = Preview(run, [plan]);
        var grid = skipped.Nodes[0].Event!;
        Check(skipped.Nodes.Length == 2 && skipped.Nodes[0].Advanced && grid.OfferedCards.Length == 5 &&
            grid.GoldBalance == 1000 && skipped.Nodes[0].Cards.Length == run.Working.Deck.Length,
            "unselected generated grid defaults to Skip without a speculative card or Lucky Fysh gold");
        var receipt = plan with { Event = plan.Event! with { Actions = plan.Event.Actions.Add(
            new(PredictorEventActionKind.Cards) { ExpectedCards = grid.OfferedCards, CardIndices = [0] }) } };
        var taken = Preview(run, [receipt]);
        Check(taken.Nodes[0].Event!.GoldBalance == 1015 &&
            taken.Nodes[0].Cards.Length == run.Working.Deck.Length + 1,
            "an explicit card receipt executes Lucky Fysh's real hook");
        var forced = Preview(Fixture(captured, "BUGSLAYER", lucky: true), []);
        Check(forced.Nodes.Length == 2 && forced.Nodes[0].Event!.GoldBalance == 1015,
            "both Bugslayer branches directly add one card; forced Lucky Fysh gold is not an optional receipt");
    }

    private static void EqualConsumption(PredictorRun captured)
    {
        var run = Fixture(captured, "COLORFUL_PHILOSOPHERS");
        var projected = Preview(run, []);
        Check(projected.Nodes.Length == 2 && projected.Nodes[0].Event is
            { Advanced: true, BusinessChoiceRequired: false, AppliedActions: 0 },
            "equal-consumption card pools need no selected world");
        var native = Fixture(captured, "COLORFUL_PHILOSOPHERS");
        if (native.ObserveEventOccurrence() is { } occurrence)
        {
            var candidate = occurrence.Candidates.First();
            Check(native.Submit(native.Request!, new ChooseEventOccurrence(candidate.Event, candidate.QueueEvent,
                candidate.RawOrdinal, occurrence.Cursor, candidate.Cursor)) == PredictorInputResult.Accepted,
                "concrete colorful occurrence");
        }
        Check(native.Submit(native.Request!, new ChooseEventOption(native.EventOptions[0].Key)) == PredictorInputResult.Accepted,
            "concrete colorful option");
        Check(native.Submit(native.Request!, new LeaveRewards()) == PredictorInputResult.Accepted, "concrete colorful Skip");
        native.AdvanceRoute();
        var concrete = native.PreviewCurrentCombat()!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!;
        var drawn = projected.Nodes[1].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Check(drawn.IsKnown && drawn.Value!.Cards.SequenceEqual(concrete.Cards.Select(card => card with { Id = 0 })),
            "equal consumption advances the real Rewards stream to the same following combat cards");
    }

    private static void DeterministicConfirmation(PredictorRun captured)
    {
        var run = Fixture(captured, "PUNCH_OFF", startFloor: 7);
        var first = Preview(run, []).Nodes[0];
        var plan = Choose(first, "I_CAN_TAKE_THEM");
        var drawing = Preview(run, [plan]).Nodes[0].Event!;
        Check(drawing.Advanced && !drawing.BusinessChoiceRequired && drawing.AppliedActions == 1 &&
            drawing.Rewards.Any(reward => reward.DefaultSkipped && reward.Kind == PredictorRewardKind.Card),
            "adopted fight executes its deterministic confirmation and combat defaults without extra authored actions");
        // Live drawing chooses the occurrence automatically. AuthoredChoices
        // deliberately requires that occurrence as a separate explicit input;
        // supply it before comparing the later confirmation boundary.
        var eventNode = first.Event!;
        var selected = eventNode.SelectedOccurrence!;
        var explicitPlan = plan with { Event = plan.Event! with
        { Occurrence = eventNode.Occurrence is { } occurrence ? new(selected.Event, selected.QueueEvent,
            selected.RawOrdinal, occurrence.Cursor, selected.Cursor) : null } };
        var concrete = Preview(run, [explicitPlan], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Event!;
        Check(concrete.NeededInput == PredictorEventNeededInput.Option && concrete.AppliedActions == 1 &&
            concrete.Options.Length == 1 && concrete.Options[0].Key == "FIGHT",
            "authored route retains its explicit confirmation page; actual=" + concrete.NeededInput +
            "; options=" + string.Join(',', concrete.Options.Select(option => option.Key)) +
            "; pending=" + string.Join(',', concrete.PendingCauses.Select(cause => cause.Code)));
        var confirmed = explicitPlan with { Event = explicitPlan.Event! with
        { Actions = explicitPlan.Event.Actions.Add(new(PredictorEventActionKind.Option)
            { ExpectedOption = concrete.Options[0].Value }) } };
        var explicitFight = Preview(run, [confirmed], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Event!;
        Check(explicitFight.NeededInput == PredictorEventNeededInput.Combat && explicitFight.AppliedActions == 2,
            "authored mode still accepts the explicit confirmation and reaches its real combat child");

        var dummy = Fixture(captured, "BATTLEWORN_DUMMY", startFloor: 7);
        var dummyFirst = Preview(dummy, []).Nodes[0];
        var dummyResult = Preview(dummy, [Choose(dummyFirst, "SETTING_1")]).Nodes[0].Event!;
        Check(!dummyResult.Advanced && dummyResult.NeededInput == PredictorEventNeededInput.Combat &&
            PredictorRouteCauseEvidence.OriginalCauses(dummyResult.PendingCauses).Any(cause => cause.Code == "EventTimeout"),
            "automatic combat defaults retain the real Dummy timeout branch declaration");
    }

    private static void CombatBranchPreviews(PredictorRun captured)
    {
        foreach (var (entry, option, encounter) in new[]
        {
            ("PUNCH_OFF", "I_CAN_TAKE_THEM", "PUNCH_OFF_EVENT_ENCOUNTER"),
            ("THE_LANTERN_KEY", "KEEP_THE_KEY", "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER"),
            ("DENSE_VEGETATION", "REST", "DENSE_VEGETATION_EVENT_ENCOUNTER")
        })
        {
            var run = Fixture(captured, entry, startFloor: 7);
            string before = JsonSerializer.Serialize(run.Working);
            var first = Preview(run, []).Nodes[0];
            var branch = first.Event!.EntryOptions.Single(value => value.Key == option).ResultPreview!;
            Check(branch.NeededInput == PredictorEventNeededInput.Combat && branch.Combat != null &&
                branch.CombatEncounter == new ModelKey("ENCOUNTER", encounter) && branch.Options.IsEmpty,
                entry + " initial business branch preview executes its sole confirmation and exposes the real combat child");
            Check(branch.Rewards.SequenceEqual(branch.Combat!.Rewards) &&
                branch.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                entry + " exposes independent combat rewards before another click or combat declaration");
            if (entry == "PUNCH_OFF")
                Check(branch.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Relic && reward.IsKnown) &&
                    branch.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Potion && reward.IsKnown),
                    "Punch Off preview retains its real relic and potion extras");
            if (entry == "THE_LANTERN_KEY")
                Check(branch.Rewards.Any(reward => reward.Kind == PredictorRewardKind.SpecialCard &&
                    reward.IsKnown && reward.Value!.Cards.Any(card => card.Key.Entry == "LANTERN_KEY")),
                    "Lantern preview retains its real special card extra");
            var plan = Choose(first, option);
            var adopted = Preview(run, [plan]).Nodes[0].Event!;
            Check(adopted is { Advanced: true, BusinessChoiceRequired: false, AppliedActions: 1 } &&
                adopted.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.DefaultSkipped &&
                    reward.Value?.Status == PredictorRewardStatus.Skipped) &&
                (entry != "THE_LANTERN_KEY" || adopted.Rewards.Any(reward =>
                    reward.Kind == PredictorRewardKind.SpecialCard && reward.DefaultSkipped &&
                    reward.Value?.Status == PredictorRewardStatus.Skipped)),
                entry + " adopted branch really skips optional card and special-card offers and returns with one authored action");
            Check(plan.Event!.Actions.Length == 1 && before == JsonSerializer.Serialize(run.Working),
                entry + " projections preserve the caller state and never save an invented Fight input");
        }

        var missingHealth = Fixture(captured, "THE_LANTERN_KEY", startFloor: 7, configure: state => state with
        {
            Relics = [new(state.NextInstanceId, new("RELIC", "MEAT_ON_THE_BONE"), 1)],
            NextInstanceId = state.NextInstanceId + 1
        });
        var pending = Preview(missingHealth, []).Nodes[0].Event!.EntryOptions
            .Single(option => option.Key == "KEEP_THE_KEY").ResultPreview!;
        Check(pending.Combat is { MissingFacts.VictoryHealth: true } &&
            pending.Combat.PendingCauses.Any(cause => cause.Code == "VictoryHealth") &&
            pending.LocalPendingCauses.Any(cause => cause.Code == "EventCombatChoice" &&
                !cause.Parents.Any(parent => parent.Code == "VictoryHealth")) &&
            pending.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "a missing real victory-health premise remains in its combat projection without becoming a prerequisite of independent combat rewards");
    }

    private static void CombatRewardInputs(PredictorRun captured)
    {
        foreach (bool partial in new[] { false, true })
        foreach (var (entry, branch) in new[]
        {
            ("PUNCH_OFF", "I_CAN_TAKE_THEM"),
            ("THE_LANTERN_KEY", "KEEP_THE_KEY"),
            ("DENSE_VEGETATION", "REST")
        })
        {
            var source = partial ? new PredictorRun(captured.Context with
            {
                Live = new(2, true,
                [
                    PredictorRouteCauseEvidence.CreateExplicit("DrawingTestPotionInventory", "capture:potions",
                        PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
                        "Potion slots were not observed.", ["potions.orderedSlots"]),
                    PredictorRouteCauseEvidence.CreateExplicit("DrawingTestNiche", "capture:niche",
                        PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.MissingObservation,
                        "The Niche cursor was not observed.", ["rng.Niche.state"]),
                    PredictorRouteCauseEvidence.CreateExplicit("DrawingTestDeck", "capture:deck",
                        PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation,
                        "Earlier card edits remain unresolved.", ["deck.Upgradable.candidates"])
                ])
            }, captured.Working) : captured;
            var run = Fixture(source, entry, startFloor: 7, configure: state => partial ? state with
            {
                Relics = [new(state.NextInstanceId, new("RELIC", "MEAT_ON_THE_BONE"), 1)],
                NextInstanceId = state.NextInstanceId + 1
            } : state);
            string before = JsonSerializer.Serialize(run.Working);
            var initial = Preview(run, []).Nodes[0];
            var plan = Choose(initial, branch);
            var combat = initial.Event!.EntryOptions.Single(option => option.Key == branch).ResultPreview!;
            Check(combat.CombatEncounter != null, entry + " adopted combat carries its actual encounter binding");
            var offer = combat.Combat!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown);
            var choice = new PredictorPlannedRewardChoice(offer.Value!, offer.Occurrence, CardIndex: 0);
            var selected = plan with { Event = plan.Event! with { Actions = plan.Event.Actions
                .Add(new(PredictorEventActionKind.Reward) { RewardChoice = choice }) } };
            var result = Preview(run, [selected]);
            var taken = result.Nodes[0].Event!;
            Check(taken.AppliedActions == 2 && taken.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card &&
                reward.AppliedChoice?.CardIndex == 0 && reward.Value?.Status == PredictorRewardStatus.Taken),
                entry + " binds and takes the actual known reward after its automatic Fight confirmation; partial=" + partial +
                "; applied=" + taken.AppliedActions + "; pending=" + string.Join(',', taken.PendingCauses.Select(cause => cause.Code)));
            Check(taken.Rewards.Where(reward => reward.Kind == PredictorRewardKind.SpecialCard)
                .All(reward => reward.DefaultSkipped && reward.Value?.Status == PredictorRewardStatus.Skipped),
                entry + " optional special-card return does not ask a fabricated mandatory receipt");
            Check(result.Nodes.Length == 2 && taken.Advanced &&
                result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                entry + " returns through the same event parent and continues independent following rewards; partial=" + partial);
            if (partial)
                Check(PredictorRouteCauseEvidence.OriginalCauses(result.PendingCauses).Any(cause => cause.Code == "VictoryHealth") &&
                    result.PendingCauses.Any(cause => cause.Code == "DrawingTestPotionInventory"),
                    entry + " retains the original missing health and inventory causes after taking an independent reward");
            Check(before == JsonSerializer.Serialize(run.Working) && plan.Event!.Actions.Length == 1 &&
                selected.Event!.Actions.All(action => action.Action != PredictorEventActionKind.Combat),
                entry + " leaves caller state, saved business prefix and missing combat facts untouched");
            var wrong = selected with { Event = selected.Event with { Actions = selected.Event.Actions.Insert(1,
                new(PredictorEventActionKind.Combat) { ExpectedEncounter = new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK") }) } };
            Check(Preview(run, [wrong]).Nodes[0].Event!.PendingCauses.Any(cause => cause.Code == "PlannedEventEncounterChanged"),
                entry + " retains the real nested encounter identity invariant");
            // Revisit the unselected event and then the same adopted fight.
            // An option observation must not settle the caller, and an invalid
            // sibling must not contaminate the later accepted replay.
            var sibling = initial.Event!.EntryOptions.First(option => option.Key != branch);
            Preview(run, [Choose(initial, sibling.Key)]);
            var unselectedAgain = Preview(run, []);
            Check(!unselectedAgain.Nodes[0].Event!.EntryOptions.Single(option => option.Key == branch)
                    .ResultPreview!.Advanced &&
                ResultValues(result) == ResultValues(Preview(run, [selected])),
                entry + " event/combat/successor results, knowledge, RNG and pending causes survive sibling/replay order; partial=" + partial);
        }
    }

    private static void CombatRewardFollowingCursor(PredictorRun captured)
    {
        var source = new PredictorRun(captured.Context with
        {
            Live = new(2, true, [PredictorRouteCauseEvidence.CreateExplicit("DrawingTestHuntDeck", "capture:hunt",
                PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation,
                "The persistent Hunt source remains unresolved.", ["deck.state"])])
        }, captured.Working);
        var run = Fixture(source, "PUNCH_OFF", startFloor: 7, configure: state =>
        {
            var hunt = PredictorCardChanges.Definition(source.Context, new("CARD", "THE_HUNT")).Prototype with
            { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
            return state with { Cards = state.Cards.Add(hunt), Deck = state.Deck.Add(hunt.Id), NextInstanceId = hunt.Id + 1 };
        });
        var first = Preview(run, []).Nodes[0];
        var plan = Choose(first, "I_CAN_TAKE_THEM");
        var combat = first.Event!.EntryOptions.Single(option => option.Key == "I_CAN_TAKE_THEM").ResultPreview!;
        var original = combat.Combat!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card &&
            reward.IsKnown && reward.Value!.RerollRecipe?.IsFromCombat == true);
        var selected = plan with { Event = plan.Event! with { Actions = plan.Event.Actions
            .Add(new(PredictorEventActionKind.Reward)
            { RewardChoice = new(original.Value!, original.Occurrence, CardIndex: 0) }) } };
        var result = Preview(run, [selected]);
        Check(result.Nodes[0].Event!.Rewards.Any(reward => reward.IsKnown &&
            reward.Value is { Kind: PredictorRewardKind.Card, Status: PredictorRewardStatus.Taken,
                RerollRecipe.IsFromCombat: true } && reward.AppliedChoice?.CardIndex == 0),
            "an unresolved Hunt extra does not back-propagate its following cursor into the already generated ordinary combat offer");
        Check(result.Nodes.Length == 2 && result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card &&
            !reward.IsKnown && PredictorRouteCauseEvidence.OriginalCauses(reward.PendingCauses)
                .Any(cause => cause.Code == "DrawingTestHuntDeck")),
            "the same unresolved Hunt extra does affect the following generated reward through its true original source");
    }

    private static void RerollKeepsIndependentPickup(PredictorRun captured)
    {
        var run = Fixture(captured, "COLOSSAL_FLOWER", startFloor: 7, configure: state => state with
        {
            Relics = [new(state.NextInstanceId, new("RELIC", "DRIFTWOOD"), 1)],
            NextInstanceId = state.NextInstanceId + 1
        });
        var first = Preview(run, []).Nodes[0];
        var eventPlan = Choose(first, "EXTRACT");
        var baseline = run.PreviewRouteContinuation([eventPlan], 3,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        var combat = baseline.Nodes[1];
        var offer = combat.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        var reroll = new PredictorPlannedRewardChoice(offer.Value!, offer.Occurrence) { Reroll = true };
        var plan = new PredictorPlannedNode(combat.Position.NodeId) { RewardChoices = [reroll] };
        string before = JsonSerializer.Serialize(run.Working);
        var result = run.PreviewRouteContinuation([eventPlan, plan], 3,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        var rerolled = result.Nodes[1];
        var replacement = rerolled.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Check(result.Nodes.Length == 3 && rerolled.Advanced && replacement is
            { DefaultSkipped: true, AppliedChoice.Reroll: true, Value.HasRerolled: true } &&
            rerolled.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Gold).Value is
                { Status: PredictorRewardStatus.Taken } gold &&
            gold.GoldAmount == combat.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Gold).Value!.GoldAmount,
            "reroll-only leaves the real picker so the same independent combat gold is still actually collected before default Skip");
        Check(!result.Nodes.Skip(1).SelectMany(node => node.PendingCauses).Any(cause =>
                cause.NodeId == combat.Position.NodeId && cause.Code is "PendingGoldChoice" or "PendingCardChoice") &&
            result.Nodes[2].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "reroll does not manufacture unresolved gold/card choices or contaminate later independent rewards");
        Check(before == JsonSerializer.Serialize(run.Working) && plan.RewardChoices.Single() == reroll,
            "reroll navigation preserves the caller and its single original semantic reroll intent");
    }

    private static void MultipleRerollsKeepTheirOwnOffers(PredictorRun captured)
    {
        var run = Fixture(captured, "PUNCH_OFF", startFloor: 7, configure: state =>
        {
            var hunt = PredictorCardChanges.Definition(captured.Context, new("CARD", "THE_HUNT")).Prototype with
                { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
            return state with { Cards = state.Cards.Add(hunt), Deck = state.Deck.Add(hunt.Id),
                Relics = [new(hunt.Id + 1, new("RELIC", "DRIFTWOOD"), 1)], NextInstanceId = hunt.Id + 2 };
        });
        string before = JsonSerializer.Serialize(run.Working);
        var first = Preview(run, []).Nodes[0];
        var plan = Choose(first, "I_CAN_TAKE_THEM");
        var baseline = Preview(run, [plan]);
        var offers = baseline.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown).ToArray();
        Check(offers.Length == 2 && offers.All(reward => reward.Value!.CanReroll),
            "native Hunt fixture produces two distinct rerollable card opportunities");
        var rerolls = offers.Select(reward => new PredictorPlannedRewardChoice(reward.Value!, reward.Occurrence)
            { Reroll = true }).ToImmutableArray();
        var rewards = new PredictorPlannedNode(baseline.Nodes[1].Position.NodeId) { RewardChoices = rerolls };
        var twice = Preview(run, [plan, rewards]);
        Check(twice.Nodes.Length == 2 && twice.Nodes[1].Advanced &&
            twice.Nodes[1].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown &&
                reward.DefaultSkipped && reward.AppliedChoice?.Reroll == true && reward.Value?.HasRerolled == true) == 2,
            "two actual Hunt offers reroll sequentially through separate picker navigation and keep both default skips");
        var reversed = Preview(run, [plan, rewards with { RewardChoices = rerolls.Reverse().ToImmutableArray() }]);
        Check(reversed.Nodes[1].Advanced && reversed.Nodes[1].Rewards.Count(reward => reward.AppliedChoice?.Reroll == true) == 2,
            "either authored reroll order binds both real reward occurrences without a stale active picker");
        Check(before == JsonSerializer.Serialize(run.Working) && rewards.RewardChoices.SequenceEqual(rerolls),
            "multiple rerolls do not mutate the caller or rewrite saved offer identities");
    }

    private static void BridgeWithoutKnownTargets(PredictorRun captured)
    {
        var run = Fixture(captured, "AROMA_OF_CHAOS", followingEvents: ["SLIPPERY_BRIDGE"], startFloor: 7,
            configure: state => state with {
                Relics = [new(state.NextInstanceId, new("RELIC", "MAW_BANK"), 1)], NextInstanceId = state.NextInstanceId + 1 });
        var result = run.PreviewRouteContinuation([], 3, scope: PredictorRoutePreviewScope.AllModeledNodes,
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(result.Nodes.Length == 3 && result.Nodes.Take(2).All(node => node.Advanced) &&
            result.Nodes[1].Event?.Event?.Entry == "SLIPPERY_BRIDGE" &&
            result.Nodes[1].Event is { GoldBalance: 1024, AppliedActions: 0 } &&
            result.Nodes[2].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "bridge's unresolved removable population preserves its occurrence, real Maw Bank entry hooks, and later independent Rewards");
        Check(result.Knowledge!.Facts.Any(fact => fact.PropertyKey == "event.default.bridgeEntrySummary"),
            "the bridge test reaches the drawing-only missing-population entry summary");
        var bridge = result.Nodes[1].Diagnostics!;
        var before = bridge.Before.Select(bridge.Snapshot.Fact).Single(fact => fact.PropertyKey == "rng.Rewards.state");
        var after = bridge.After.Select(bridge.Snapshot.Fact).Single(fact => fact.PropertyKey == "rng.Rewards.state");
        Check(before.Status == PredictorKnowledgeStatus.Known && after.Status == PredictorKnowledgeStatus.Known &&
            before.DisplayValue == after.DisplayValue,
            "the zero-public-consumption bridge summary neither fabricates a target nor advances Rewards");
    }
}
