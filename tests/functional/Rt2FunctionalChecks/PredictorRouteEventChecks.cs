using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorRouteEventChecks
{
    private static ModelKey E(string entry) => new("EVENT", entry);
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Route event: " + message); }
    private static void Select(PredictorRun run, PredictorInput input)
        => Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "fixture input rejected: " + input);
    private static PredictorCard Display(PredictorCard card) => card with { Id = 0 };
    private static PredictorPlannedCard Target(PredictorCard card) => new(card.Id, card.Key, card.FloorAdded);

    internal static void Run(PredictorRun captured)
    {
        PartialDeckOperationChains(captured);
        PartialDeckLeafComposition(captured);
        PartialDeckOperationConsequences(captured);
        MorphicAndPrefix(captured);
        ConveyorAndHealth(captured);
        GeneratedOffersAndObtains(captured);
        GoldMerchantAndCrystal(captured);
        NestedCombat(captured);
        InheritedDependencies(captured);
        ExhaustedEntryRequirements(captured);
        LocalPotionHypotheses(captured);
        FakeMerchantLiveDefault(captured);
        FakeMerchantLocalFoulHypothesis(captured);
        LiveDefaultBranchesAndContinuation(captured);
        LiveDefaultUiBranchReplacement(captured);
        LiveDefaultRewardBeforeHealth(captured);
        LiveRewardAlternatives(captured);
        LiveDefaultHealthScope(captured);
        LiveDefaultEligibilityGates(captured);
        LiveOrderedOccurrenceExclusions(captured);
        OwnerEventInputPolicy(captured);
        AutomaticOccurrenceReceipts(captured);
        Console.WriteLine("RUNTIME_ROUTE_EVENT_PASS explicit occurrence/choices; Morphic event-local RNG; HP boundaries; Conveyor loops; semantic generated offers; shared obtain children; gold/merchant/Crystal; nested combat/rewards/timeout; accepted prefix; inherited Niche and optional-deck locality; live default eligibility, independent branch previews and pending-target continuation; detached replay");
    }

    // Native catalogs, pools and settlement are retained. Geometry and the raw
    // event queue alone fix the node whose authored choice is under test.
    private static PredictorRun Fixture(PredictorRun captured, string entry, bool previousCombat = false,
        string firstEncounter = "FABRICATOR_NORMAL", bool visited = false, PredictorContext? context = null,
        Func<PredictorState, PredictorState>? configure = null, string[]? followingEvents = null)
    {
        const int act = 2;
        var types = followingEvents != null ? new[] { MapPointType.Monster }.Concat(
            Enumerable.Repeat(MapPointType.Unknown, followingEvents.Length + 1)).Concat([MapPointType.Monster, MapPointType.Boss]).ToArray() :
            previousCombat ? new[] { MapPointType.Monster, MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.Boss } :
            new[] { MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], GoldBudget = 1000,
            Position = new(act, 1, "route-event:entry"),
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Monster, PredictorRoomKind.Monster, null, 1)],
            VisitedEvents = visited ? [E(entry)] : [], UnknownOdds = new(0, -1, 0, 0),
            Acts = captured.Working.Acts.Select(a => a.Act == act ? a with
            { RawEvents = new[] { entry }.Concat(followingEvents ?? []).Select(E).ToImmutableArray(), EventCursor = 0, NormalEncounters = [new("ENCOUNTER", firstEncounter), new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 } : a).ToImmutableArray()
        };
        state = configure?.Invoke(state) ?? state;
        var run = new PredictorRun(context ?? captured.Context, state);
        run.SetRoute(act, points.Select(p => p.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static PredictorRouteContinuation Pure(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans, int count = 1,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints),
            draft = JsonSerializer.Serialize(run.CaptureNodeDraft());
        var request = run.Request;
        var result = run.PreviewRouteContinuation(plans, count, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            draft == JsonSerializer.Serialize(run.CaptureNodeDraft()) && request == run.Request,
            "projection preserves actual state, checkpoints, replay prefix and request");
        Check(result.Nodes.Where(n => n.Event != null).All(n => n.Event!.OfferedCards.All(c => c.Id == 0) &&
            n.Event.RequiredChoices.All(c => c.Cards.All(card => card.Id == 0) && c.Bundles.All(b => b.All(card => card.Id == 0))) &&
            n.Event.Rewards.Where(r => r.Value != null).All(r => r.Value!.Id == 0 && r.Value.Cards.All(c => c.Id == 0))),
            "generated event observations never export actionable witness IDs");
        Check(result.Nodes.Where(n => n.Event != null).SelectMany(n => n.Event!.EntryOptions.Concat(n.Event.Options))
            .Where(o => o.ResultPreview != null).Select(o => o.ResultPreview!).All(preview =>
                preview.OfferedCards.All(c => c.Id == 0) && preview.GeneratedCards.All(c => c.Value.Id == 0) &&
                preview.ObtainedRelics.All(r => r.Id == 0) && preview.Rewards.Where(r => r.Value != null)
                    .All(r => r.Value!.Id == 0 && r.Value.Cards.All(c => c.Id == 0))),
            "independent option previews never export generated witness IDs");
        return result;
    }

    private static PredictorPlannedNode Plan(PredictorRun run, string entry)
    {
        var observation = run.ObserveEventOccurrence();
        var candidate = observation?.Candidates.First(c => c.Event == E(entry));
        return new(run.Working.Position.NodeId)
        {
            Event = new(E(entry)) { Occurrence = candidate == null ? null : new(candidate.Event, candidate.QueueEvent,
                candidate.RawOrdinal, observation!.Cursor, candidate.Cursor) }
        };
    }

    private static PredictorEventNodeKnowledge View(PredictorRun run, PredictorPlannedNode plan)
        => Pure(run, [plan]).Nodes.Single().Event!;
    private static PredictorPlannedNode Add(PredictorPlannedNode plan, PredictorPlannedEventAction action)
        => plan with { Event = plan.Event! with { Actions = plan.Event!.Actions.Add(action) } };
    private static PredictorPlannedNode Option(PredictorRun run, PredictorPlannedNode plan, string key)
        => Add(plan, new(PredictorEventActionKind.Option) { ExpectedOption = View(run, plan).Options.Single(o => o.Key == key).Value! });
    private static void Enter(PredictorRun run, PredictorPlannedNode plan)
    { if (plan.Event!.Occurrence != null) Select(run, plan.Event.Occurrence); }
    private static void DeckEqual(PredictorRouteNodeKnowledge projected, PredictorRun concrete, string message)
        => Check(projected.Cards.Select(c => Display(c.Value)).SequenceEqual(concrete.Working.Deck.Select(concrete.Working.Card).Select(Display)), message);

    private static PredictorPlannedNode LiveOption(PredictorRouteNodeKnowledge node, string key)
        => new(node.Position.NodeId) { Event = new(node.Event!.Event!.Value) {
            Actions = [new(PredictorEventActionKind.Option) {
                ExpectedOption = node.Event.EntryOptions.Single(option => option.Key == key).Value! }] } };

    private static string ChainEvidence(PredictorRouteContinuation route) => string.Join("; ", route.Nodes.Select(node =>
        $"{node.Model?.Entry}/{node.Event?.Event?.Entry}:advanced={node.Advanced}:" +
        string.Join(',', node.PendingCauses.Concat(node.Event?.PendingCauses ?? []).Select(cause => cause.Code).Distinct())));

    private static PredictorKnowledgeFact StateFact(PredictorRouteNodeKnowledge node, string property, bool before = false)
    {
        var diagnostics = node.Diagnostics!;
        return (before ? diagnostics.Before : diagnostics.After).Select(diagnostics.Snapshot.Fact)
            .Single(fact => fact.PropertyKey == property);
    }

    private static PredictorRouteNodeKnowledge IndependentCombat(PredictorRouteContinuation route, int count, string message)
    {
        Check(route.Nodes.Length == count && route.Nodes.Take(count - 1).All(node => node.Advanced),
            message + "; " + ChainEvidence(route));
        var combat = route.Nodes[^1];
        Check(combat.Room == PredictorRoomKind.Monster && combat.Rewards.Any(reward =>
            reward.Kind == PredictorRewardKind.Card && reward.IsKnown && reward.Value!.Cards.Length > 0),
            message + ": independent combat card result is populated and known");
        Check(StateFact(combat, "rng.Rewards.state").Status == PredictorKnowledgeStatus.Known &&
            route.Knowledge!.Facts.Where(fact => fact.Status is PredictorKnowledgeStatus.Unknown or PredictorKnowledgeStatus.NotObserved)
                .All(fact => fact.DisplayValue == null), message + ": uncertain facts carry no witness payload");
        return combat;
    }

    private static void PartialDeckOperationChains(PredictorRun captured)
    {
        var failures = new List<string>();
        {
            var run = Fixture(captured, "BUGSLAYER", followingEvents: ["SAPPHIRE_SEED"]);
            var route = Pure(run, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
            if (route.Nodes.Length != 3 || !route.Nodes.Take(2).All(node => node.Advanced) ||
                !route.Nodes[2].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown))
                failures.Add("Bugslayer→unselected Sapphire→combat: " + ChainEvidence(route));
        }
        foreach (string branch in new[] { "EAT", "PLANT" })
        {
            var run = Fixture(captured, "BUGSLAYER", followingEvents: ["SAPPHIRE_SEED"]);
            var initial = Pure(run, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(initial.Nodes.Length == 2 && initial.Nodes[1].Event?.Event == E("SAPPHIRE_SEED"),
                "conditional Bugslayer gain reaches Sapphire entry: " + ChainEvidence(initial));
            var plan = LiveOption(initial.Nodes[1], branch);
            var route = Pure(run, [plan], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
            bool continued = route.Nodes.Length == 3 && route.Nodes.Take(2).All(node => node.Advanced) &&
                route.Nodes[2].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown);
            if (!continued) failures.Add("Bugslayer→Sapphire " + branch + "→combat: " + ChainEvidence(route));
            if (continued)
            {
                var combat = IndependentCombat(route, 3, "conditional addition followed by " + branch);
                var original = route.Nodes[0].Event!.PendingCauses.Single(cause => cause.Code == "EventOptionChoice").ChoiceId;
                Check(original != null && combat.DeckPendingCauses.Any(cause => cause.ChoiceId == original) &&
                    combat.Cards.Where(card => card.Value.Key.Entry is "EXTERMINATE" or "SQUASH").All(card => !card.IsKnown),
                    "later target effects preserve the original Bugslayer ChoiceId and keep its witness card conditional");
                var conditionalTargets = route.Nodes[1].Event!.DeckCardCandidates
                    .Where(card => card.Value.Key.Entry is "EXTERMINATE" or "SQUASH").ToArray();
                Check(conditionalTargets.Length > 0 && conditionalTargets.All(card => !card.IsKnown &&
                    PredictorRouteCauseEvidence.OriginalCauses(card.PendingCauses).Any(cause => cause.ChoiceId == original)),
                    "a conditional target explains the original event choice instead of claiming a missing base capture");
                var native = Fixture(captured, "BUGSLAYER", followingEvents: ["SAPPHIRE_SEED"]);
                Enter(native, Plan(native, "BUGSLAYER")); Select(native, new ChooseEventOption("SQUASH")); native.AdvanceRoute();
                Enter(native, Plan(native, "SAPPHIRE_SEED")); Select(native, new ChooseEventOption(branch));
                if (branch == "EAT") Select(native, new ProvideEventHealthFact(new(40, 80)));
                Select(native, new SelectEventCards([native.EventCardCandidates[0]])); native.AdvanceRoute();
                Check(combat.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards.SequenceEqual(
                    native.PreviewCurrentCombat()!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards),
                    "partial " + branch + " chain agrees with a complete native card/target choice on independent Rewards");
            }
        }
        {
            var run = Fixture(captured, "SAPPHIRE_SEED", followingEvents: ["DOORS_OF_LIGHT_AND_DARK"]);
            var initial = Pure(run, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
            var upgrade = LiveOption(initial.Nodes[0], "EAT");
            var pendingUpgrade = Pure(run, [upgrade], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(pendingUpgrade.Nodes.Length == 2 && pendingUpgrade.Nodes[1].Event?.Event == E("DOORS_OF_LIGHT_AND_DARK"),
                "unknown Sapphire upgrade reaches Doors entry: " + ChainEvidence(pendingUpgrade));
            var removal = LiveOption(pendingUpgrade.Nodes[1], "DARK");
            var route = Pure(run, [upgrade, removal], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
            bool continued = route.Nodes.Length == 3 && route.Nodes.Take(2).All(node => node.Advanced) &&
                route.Nodes[2].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown);
            if (!continued) failures.Add("Sapphire unknown-upgrade→Doors DARK→combat: " + ChainEvidence(route));
            if (continued)
            {
                IndependentCombat(route, 3, "upgrade uncertainty followed by unselected removal");
                Check(StateFact(route.Nodes[1], "deck.Removable.candidates", before: true).Status == PredictorKnowledgeStatus.Known,
                    "ordinary upgrade uncertainty preserves the exact removable membership projection");
                var target = run.Working.Deck.Select(run.Working.Card).First(card => card.Removable);
                var explicitRemoval = Add(removal, new(PredictorEventActionKind.Cards) { DeckCards = [Target(target)] });
                var bound = Pure(run, [upgrade, explicitRemoval], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
                IndependentCombat(bound, 3, "known instance removal through unrelated upgrade uncertainty");
                Check(bound.Nodes[1].Event!.AppliedActions == 2 && !bound.Nodes[1].Cards.Any(card => card.Value.Id == target.Id),
                    "the original removal target binds by known identity and removable membership despite unknown upgrade level");
            }
        }
        foreach (string failure in failures) Console.WriteLine("RUNTIME_ROUTE_STATE_REPRO " + failure);
        Check(failures.Count == 0, "partial deck operations must compose; " + string.Join(" | ", failures));
    }

    private static void PartialDeckLeafComposition(PredictorRun captured)
    {
        foreach (var (entry, branch, omitSapphire, singleton) in new[] {
            ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL", true, false),
            ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL", false, false),
            ("MORPHIC_GROVE", "GROUP", false, false),
            ("WELLSPRING", "BATHE", true, false),
            // One real card makes Quill's eligibility zero or exactly one:
            // Sapphire's PLANT enchants it, while EAT leaves it enchantable.
            ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL", true, true) })
        {
            var run = Fixture(captured, "SAPPHIRE_SEED", followingEvents: [entry],
                configure: state => singleton ? state with { Deck = [state.Deck[0]] } : state);
            var initial = Pure(run, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
            var upgrade = LiveOption(initial.Nodes[0], "EAT");
            ImmutableArray<PredictorPlannedNode> prefix = omitSapphire ? [] : [upgrade];
            var pending = Pure(run, prefix, 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(pending.Nodes.Length == 2 && pending.Nodes[1].Event?.Event == E(entry),
                "partial Sapphire reaches the next leaf entry: " + ChainEvidence(pending));
            var leaf = LiveOption(pending.Nodes[1], branch);
            var result = Pure(run, prefix.Add(leaf), 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var combat = IndependentCombat(result, 3, "Sapphire partial state then " + branch + (singleton ? " zero/exact qualifier" : ""));
            var leafNode = result.Nodes[1];
            Check(!leafNode.DeckPendingCauses.IsEmpty && leafNode.Event!.AppliedActions == 1 &&
                !leafNode.PendingCauses.Any(cause => (cause.AffectedDomains & PredictorCombatDomain.Continuation) != 0),
                "a composed leaf retains scoped deck consequences without inventing a target action or continuation gate");
            var originalChoice = result.Nodes[0].Event!.PendingCauses.Single(cause => cause.Code ==
                (omitSapphire ? "EventOptionChoice" : "EventCardsChoice")).ChoiceId;
            Check(originalChoice != null && combat.DeckPendingCauses.Any(cause => cause.ChoiceId == originalChoice),
                "a second unresolved leaf retains the first operation's original ChoiceId");
            Check(StateFact(leafNode, "rng.Niche.state", before: true).Status == PredictorKnowledgeStatus.Known &&
                StateFact(leafNode, "rng.Niche.state").Status == PredictorKnowledgeStatus.Known &&
                StateFact(leafNode, "rng.Niche.state", before: true).DisplayValue == StateFact(leafNode, "rng.Niche.state").DisplayValue,
                "event-local target/transform work leaves public Niche known and unchanged");
            Check(leafNode.Event!.GoldBalance == (entry switch { "MORPHIC_GROVE" => 0, "WELLSPRING" => 1000, _ => 945 }),
                "composed leaf preserves the actual source gold assignment or fee");
            if (entry == "WELLSPRING")
                Check(leafNode.Cards.Single(card => card.Value.Key.Entry == "GUILTY").IsKnown &&
                    combat.Cards.Single(card => card.Value.Key.Entry == "GUILTY").IsKnown &&
                    leafNode.Cards.Any(card => run.Working.Deck.Contains(card.Value.Id) && !card.IsKnown),
                    "the fixed Guilty receipt follows removal and stays known while earlier card membership remains uncertain");
            if (singleton)
                Check(leafNode.Cards.Length == 1 && leafNode.Cards.All(card => !card.IsKnown),
                    "a witness's zero/exact qualifier cannot publish its representative card attributes as known");

            // Complete-input contrast uses the retained source picker, including
            // automatic singleton selection, rather than guessed RNG counts.
            var native = Fixture(captured, "SAPPHIRE_SEED", followingEvents: [entry],
                configure: state => singleton ? state with { Deck = [state.Deck[0]] } : state);
            Enter(native, Plan(native, "SAPPHIRE_SEED")); Select(native, new ChooseEventOption("EAT"));
            Select(native, new ProvideEventHealthFact(new(40, 80)));
            if (native.Phase == PredictorPhase.EventCards) Select(native, new SelectEventCards([native.EventCardCandidates[0]]));
            native.AdvanceRoute(); Enter(native, Plan(native, entry)); Select(native, new ChooseEventOption(branch));
            if (native.Phase == PredictorPhase.EventCards)
                Select(native, new SelectEventCards(native.EventCardCandidates.Take(entry == "MORPHIC_GROVE" ? 2 : 1).ToImmutableArray()));
            native.AdvanceRoute();
            Check(combat.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards.SequenceEqual(
                native.PreviewCurrentCombat()!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards),
                "composed " + branch + " partial result agrees with complete native inputs on independent Rewards");
        }
    }

    private static void PartialDeckOperationConsequences(PredictorRun captured)
    {
        // C: a missing removal target does not erase a later fixed receipt.
        var removalRun = Fixture(captured, "DOORS_OF_LIGHT_AND_DARK", followingEvents: ["BUGSLAYER"]);
        var removalEntry = Pure(removalRun, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        var removal = LiveOption(removalEntry.Nodes[0], "DARK");
        var removed = Pure(removalRun, [removal], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var gain = LiveOption(removed.Nodes[1], "SQUASH");
        var gained = Pure(removalRun, [removal, gain], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var gainedCombat = IndependentCombat(gained, 3, "unselected removal then fixed card receipt");
        Check(gainedCombat.Cards.Any(card => card.Value.Key.Entry == "SQUASH" && card.IsKnown) &&
            gainedCombat.DeckPendingCauses.Any(cause => cause.ChoiceId ==
                gained.Nodes[0].Event!.PendingCauses.Single(cause => cause.Code == "EventCardsChoice").ChoiceId),
            "fixed gain survives alongside the original pending removal identity");

        // D: the Owner-approved cutoff remains local to Niche; random event edits
        // still execute on the node's own RNG and leave Rewards independent.
        var cutRun = Fixture(captured, "HUNGRY_FOR_MUSHROOMS", followingEvents: ["REFLECTIONS"]);
        var cutEntry = Pure(cutRun, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        var fragrant = LiveOption(cutEntry.Nodes[0], "FRAGRANT_MUSHROOM");
        var cut = Pure(cutRun, [fragrant], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var mirror = LiveOption(cut.Nodes[1], "TOUCH_A_MIRROR");
        var reflected = Pure(cutRun, [fragrant, mirror], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var reflectedCombat = IndependentCombat(reflected, 3, "Niche cutoff then event-local random edits");
        Check(StateFact(reflectedCombat, "rng.Niche.state").Status == PredictorKnowledgeStatus.Unknown &&
            reflected.Nodes[1].Event!.AppliedActions == 1 &&
            reflected.PendingCauses.Any(cause => cause.Code == "RouteRelicDeckRandomnessCutoff"),
            "Reflections neither heals the approved Niche cutoff nor waits for that unrelated stream");

        // E: deterministic prices and card receipts compose with missing targets.
        foreach (bool lucky in new[] { false, true })
        {
            var fees = Fixture(captured, "WATERLOGGED_SCRIPTORIUM", followingEvents: ["ZEN_WEAVER"],
                configure: state => lucky ? state with { Relics = [new(state.NextInstanceId, new("RELIC", "LUCKY_FYSH"), 1)],
                    NextInstanceId = state.NextInstanceId + 1 } : state);
            var feeEntry = Pure(fees, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
            var quill = LiveOption(feeEntry.Nodes[0], "TENTACLE_QUILL");
            var paid = Pure(fees, [quill], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var breathe = LiveOption(paid.Nodes[1], "BREATHING_TECHNIQUES");
            var chain = Pure(fees, [quill, breathe], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var combat = IndependentCombat(chain, 3, "fixed fees and two explicit Zen receipts");
            Check(chain.Nodes[0].Event!.GoldBalance == 945 && chain.Nodes[1].Event!.GoldBalance == (lucky ? 925 : 895) &&
                combat.Cards.Count(card => card.Value.Key.Entry == "ENLIGHTENMENT" && card.IsKnown) == 2,
                "55+50 gold costs survive and explicit two-card receipt invokes Lucky Fysh exactly twice");
        }

        // F: replacing the two omissions recomputes the suffix from actual input,
        // leaving the prior immutable result and its original cause intact.
        var edited = Fixture(captured, "BUGSLAYER", followingEvents: ["SAPPHIRE_SEED"]);
        var omitted = Pure(edited, [], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        IndependentCombat(omitted, 3, "omitted two-event baseline");
        var oldChoice = omitted.Nodes[0].Event!.PendingCauses.Single(cause => cause.Code == "EventOptionChoice").ChoiceId;
        var concreteBug = LiveOption(omitted.Nodes[0], "SQUASH");
        var concreteSapphire = Add(LiveOption(omitted.Nodes[1], "PLANT"), new(PredictorEventActionKind.Cards) {
            DeckCards = [Target(edited.Working.Deck.Select(edited.Working.Card).First(card => card.Type ==
                RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack))] });
        var known = Pure(edited, [concreteBug, concreteSapphire], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var knownCombat = IndependentCombat(known, 3, "concrete replacement of earlier omissions");
        Check(knownCombat.DeckPendingCauses.IsEmpty && !known.PendingCauses.Any(cause => cause.ChoiceId == oldChoice) &&
            omitted.Nodes[^1].DeckPendingCauses.Any(cause => cause.ChoiceId == oldChoice),
            "concrete replacement clears only the recomputed uncertainty and preserves the old forecast");
        var concrete = Fixture(captured, "BUGSLAYER", followingEvents: ["SAPPHIRE_SEED"]);
        Enter(concrete, Plan(concrete, "BUGSLAYER")); Select(concrete, new ChooseEventOption("SQUASH")); concrete.AdvanceRoute();
        Enter(concrete, Plan(concrete, "SAPPHIRE_SEED")); Select(concrete, new ChooseEventOption("PLANT"));
        Select(concrete, new SelectEventCards([concreteSapphire.Event!.Actions[^1].DeckCards[0].InstanceId]));
        DeckEqual(known.Nodes[1], concrete, "fully authored chain retains exact native deck and target identity");
        Console.WriteLine("RUNTIME_ROUTE_STATE_CHAIN_PASS A conditional add/edit; B upgrade/remove; C remove/fixed-add; D Niche/local-RNG/Rewards; E costs/receipts/hooks; F recomputation/native contrast; G structural entry retained by partial-knowledge suite");
    }

    private static void LiveDefaultBranchesAndContinuation(PredictorRun captured)
    {
        var brain = Fixture(captured, "BRAIN_LEECH");
        var initial = Pure(brain, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(initial.SelectedOccurrence?.IsUnconditional == true && initial.NeededInput == PredictorEventNeededInput.Option &&
            initial.EntryOptions.Length == 2, "live default enters an unconditional singleton and preserves switchable entry options");
        var share = initial.EntryOptions.Single(o => o.Key == "SHARE_KNOWLEDGE").ResultPreview!;
        var rip = initial.EntryOptions.Single(o => o.Key == "RIP").ResultPreview!;
        Check(share.OfferedCards.Length == 5 && share.CardsRequired == 1 && rip.Rewards.Single().Value!.Cards.Length == 3 &&
            !rip.HealthRequired && rip.PendingHealthCommands.SequenceEqual([new PredictorEventHealthDelta(-5, 0)]) &&
            rip.HealthConsequences.IsEmpty, "both Brain Leech rewards preview before branch selection without fictitious HP");

        var sharePlan = Option(brain, Plan(brain, "BRAIN_LEECH"), "SHARE_KNOWLEDGE");
        var shared = Pure(brain, [sharePlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(shared.Nodes.Length == 2 && shared.Nodes[0].Advanced && shared.Nodes[0].Event!.NeededInput == PredictorEventNeededInput.Cards &&
            shared.Nodes[0].Event!.OfferedCards.SequenceEqual(share.OfferedCards) &&
            shared.Nodes[1].Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "unpicked Brain Leech grid keeps its target pending while independent later Rewards advance");
        Check(shared.Nodes[0].Event!.EntryOptions.Length == 2, "selected grid branch retains entry branch controls");

        var ripPlan = Option(brain, Plan(brain, "BRAIN_LEECH"), "RIP");
        var ripped = Pure(brain, [ripPlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var ripEvent = ripped.Nodes[0].Event!;
        var ripReward = ripEvent.Rewards.Single();
        Check(ripped.Nodes.Length == 2 && ripEvent.Advanced && !ripEvent.HealthRequired &&
            ripReward.IsKnown && ripReward.Value!.Status == PredictorRewardStatus.Skipped &&
            ripReward.DefaultSkipped && ripReward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault &&
            ripReward.AppliedChoice == null && ripped.Nodes[0].Cards.Length == brain.Working.Deck.Length &&
            ripEvent.PendingCauses.Any(c => c.Code == "EventHealthOutcomePending") &&
            !ripEvent.PendingCauses.Any(c => c.Code == "PendingCardChoice") &&
            ripped.Nodes[1].Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "RIP keeps only its HP outcome pending; the generated ordinary card defaults to approved Skip without a receipt or later card-choice gap");
        var supplied = Add(ripPlan, new(PredictorEventActionKind.Health) { Health = new(40, 80) });
        var actualHp = Pure(brain, [supplied], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(actualHp.HealthConsequences.SequenceEqual([new PredictorEventHealthDelta(-5, 0)]) &&
            actualHp.PendingHealthCommands.IsEmpty && !actualHp.HealthRequired,
            "actual RIP health fact executes the source five HP deduction");
        var nativeRip = Fixture(captured, "BRAIN_LEECH");
        Enter(nativeRip, ripPlan); Select(nativeRip, new ChooseEventOption("RIP"));
        Select(nativeRip, new ProvideEventHealthFact(new(40, 80)));
        var nativeRipReward = nativeRip.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Check(ripReward.Value!.Cards.SequenceEqual(nativeRipReward.Cards.Select(Display)),
            "approved RIP default preserves the cards generated by the concrete source after its real HP cost");
        Select(nativeRip, new EnterCardSelection(nativeRipReward.Id)); Select(nativeRip, new SkipCardReward());
        Select(nativeRip, new LeaveRewards()); nativeRip.AdvanceRoute();
        Check(ripped.Nodes[1].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards
            .SequenceEqual(nativeRip.PreviewCurrentCombat()!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards),
            "default RIP Skip consumes the same source reward RNG as a concrete Skip and preserves later combat card identities");

        var scriptorium = Fixture(captured, "WATERLOGGED_SCRIPTORIUM");
        var enchantPlan = Option(scriptorium, Plan(scriptorium, "WATERLOGGED_SCRIPTORIUM"), "TENTACLE_QUILL");
        var enchant = Pure(scriptorium, [enchantPlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(enchant.Nodes.Length == 2 && enchant.Nodes[0].Event!.Advanced &&
            enchant.Nodes[0].Event!.NeededInput == PredictorEventNeededInput.Cards && enchant.Nodes[0].Event!.GoldBalance == 945 &&
            enchant.Nodes[0].Cards.Any(c => !c.IsKnown) && enchant.Nodes[1].Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "unpicked fixed enchant target changes deck knowledge but preserves later independent Rewards");
        var target = enchant.Nodes[0].Event!.DeckCardCandidates.First(c => c.IsKnown).Value;
        var concrete = Fixture(captured, "WATERLOGGED_SCRIPTORIUM");
        Enter(concrete, enchantPlan); Select(concrete, new ChooseEventOption("TENTACLE_QUILL"));
        Select(concrete, new SelectEventCards([target.Id])); concrete.AdvanceRoute();
        var concreteLater = concrete.PreviewCurrentCombat()!;
        Check(enchant.Nodes[1].Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Value!.Cards
            .SequenceEqual(concreteLater.Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Value!.Cards),
            "omitted enchant target equals actual later reward identities");
    }

    private static void OwnerEventInputPolicy(PredictorRun captured)
    {
        string[] optional = ["ABYSSAL_BATHS", "AMALGAMATOR", "AROMA_OF_CHAOS", "BUGSLAYER", "BYRDONIS_NEST",
            "COLORFUL_PHILOSOPHERS", "DOORS_OF_LIGHT_AND_DARK", "FAKE_MERCHANT", "FIELD_OF_MAN_SIZED_HOLES",
            "GRAVE_OF_THE_FORGOTTEN", "HUNGRY_FOR_MUSHROOMS", "INFESTED_AUTOMATON", "JUNGLE_MAZE_ADVENTURE", "MORPHIC_GROVE", "REFLECTIONS", "SAPPHIRE_SEED",
            "SELF_HELP_BOOK", "SLIPPERY_BRIDGE", "SPIRALING_WHIRLPOOL", "SPIRIT_GRAFTER", "STONE_OF_ALL_TIME",
            "SUNKEN_TREASURY", "SYMBIOTE", "TABLET_OF_TRUTH", "THE_FUTURE_OF_POTIONS", "TINKER_TIME", "WATERLOGGED_SCRIPTORIUM", "WHISPERING_HOLLOW", "WOOD_CARVINGS", "ZEN_WEAVER"];
        Check(PredictorRouteEventPolicy.All.Count == 57 && PredictorRouteEventPolicy.All
            .Where(p => !p.Value.BranchRequired).Select(p => p.Key).Order().SequenceEqual(optional.Order()),
            "all 57 policies preserve the 30 optional branch families after H07 gold-source deferral; actual optional=" +
            string.Join(',', PredictorRouteEventPolicy.All.Where(p => !p.Value.BranchRequired).Select(p => p.Key).Order()));
        Check(PredictorRouteEventPolicy.For(E("BRAIN_LEECH")).BranchRequired &&
            !PredictorRouteEventPolicy.For(E("LUMINOUS_CHOIR")).SpecificCardTargetRequired &&
            !PredictorRouteEventPolicy.For(E("ZEN_WEAVER")).SpecificCardTargetRequired &&
            PredictorRouteEventPolicy.For(E("CRYSTAL_SPHERE")).BranchRequired,
            "business branches stay separate from optional card targets while Crystal retains its real local game/payout choice");
        // H07 gold-source deferral removes the static Gold-only gate, not a
        // handler's genuinely different public-stream consumption. Whisper's
        // potion branch consumes Rewards while its transform branch does not.
        foreach (string entry in optional.Where(k => k is not ("STONE_OF_ALL_TIME" or "THE_FUTURE_OF_POTIONS" or "WHISPERING_HOLLOW")))
        {
            var run = Fixture(captured, entry);
            var preview = Pure(run, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var node = preview.Nodes[0].Event!;
            Check(node.Advanced && preview.Nodes.Length == 2,
                "optional branch retains entry consumption and independent next node without a chosen branch: " + entry +
                "; " + string.Join(",", node.PendingCauses.Select(c => c.Code)));
            Check(node.AppliedActions == 0 && node.EntryOptions.All(o => o.Enabled == false || o.ResultPreview != null),
                "optional event publishes conditional previews without manufacturing player actions: " + entry);
        }
        var whisperRun = Fixture(captured, "WHISPERING_HOLLOW");
        var whisperRoute = Pure(whisperRun, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var whisper = whisperRoute.Nodes[0].Event!;
        Check(whisperRoute.Nodes.Length == 1 && !whisper.Advanced && whisper.BusinessChoiceRequired &&
            whisper.AppliedActions == 0 && whisper.NeededInput == PredictorEventNeededInput.Option &&
            whisper.EntryOptions.Single(option => option.Key == "GOLD").ResultPreview!.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Potion) == 2 &&
            whisper.EntryOptions.Single(option => option.Key == "HUG").ResultPreview!.Rewards.IsEmpty,
            "Whisper still requires its actual unequal Rewards-consumption choice, rather than treating a deferred gold source as equal consumption; " + ChainEvidence(whisperRoute));
        var zen = Fixture(captured, "ZEN_WEAVER");
        var zenPlan = Option(zen, Plan(zen, "ZEN_WEAVER"), "EMOTIONAL_AWARENESS");
        var removed = Pure(zen, [zenPlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(removed.Nodes[0].Event is { Advanced: true, NeededInput: PredictorEventNeededInput.Cards, GoldBalance: 875 } &&
            removed.Nodes.Length == 2 && removed.Nodes[1].Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "chosen Zen branch applies its cost and continues independent Rewards without inventing a removal target");
        var mushrooms = Pure(Fixture(captured, "HUNGRY_FOR_MUSHROOMS"), [], 2,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        var mushroom = mushrooms.Nodes[0].Event!;
        Check(mushroom.EntryOptions.Single(o => o.Key == "FRAGRANT_MUSHROOM").ResultPreview is { Advanced: true } fragrant &&
            fragrant.ObtainedRelics.Any(r => r.Key.Entry == "FRAGRANT_MUSHROOM") &&
            fragrant.LocalPendingCauses.Any(c => c.Code == "RouteRelicDeckRandomnessCutoff") &&
            fragrant.LocalPendingCauses.Any(c => c.Code == "ObtainHealthOutcomePending") &&
            mushroom.EntryOptions.Single(o => o.Key == "BIG_MUSHROOM").ResultPreview!.LocalPendingCauses
                .All(c => c.Code != "RouteRelicDeckRandomnessCutoff") &&
            mushrooms.Nodes[1].Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "Fragrant projects its real missing 15-HP outcome and retains its existing local random-upgrade cutoff; independent Rewards continue; " + ChainEvidence(mushrooms));
        var tinker = Pure(Fixture(captured, "TINKER_TIME"), [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(tinker.EntryOptions.Single().ResultPreview!.Options.Length == 2 &&
            tinker.EntryOptions.Single().ResultPreview!.Options.All(o => o.ResultPreview!.Options.Length == 2),
            "Tinker previews both source type choices and their source rider results before selection");
        var trial = Pure(Fixture(captured, "TRIAL"), [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(trial.EntryOptions.Single(o => o.Key == "ACCEPT").ResultPreview!.Options.Length == 2,
            "Trial exposes the actual randomly drawn judgment and both outcomes before the player accepts");
        var lucky = Fixture(captured, "BUGSLAYER", configure: state => state with
            { Relics = [new(state.NextInstanceId, new("RELIC", "LUCKY_FYSH"), 1)], NextInstanceId = state.NextInstanceId + 1 });
        var omitted = Pure(lucky, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(omitted.Advanced && omitted.GoldBalance == 1015 && omitted.AppliedActions == 0,
            "both Bugslayer branches directly add one card, so forced Lucky Fysh income is real even without an authored branch; gold=" + omitted.GoldBalance +
            "; pending=" + string.Join(',', omitted.PendingCauses.Select(cause => cause.Code)));
        var explicitLucky = Pure(lucky, [Option(lucky, Plan(lucky, "BUGSLAYER"), "SQUASH")],
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(explicitLucky.GoldBalance == 1015, "an explicit card branch retains actual Lucky Fysh income");
    }

    private static void AutomaticOccurrenceReceipts(PredictorRun captured)
    {
        var run = Fixture(captured, "BRAIN_LEECH");
        var initial = Pure(run, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        var selected = initial.SelectedOccurrence!;
        var receipt = new ChooseEventOccurrence(selected.Event, selected.QueueEvent, selected.RawOrdinal,
            initial.Occurrence!.Cursor, selected.Cursor);
        var plan = new PredictorPlannedNode(run.Working.Position.NodeId)
        { Event = new(E("BRAIN_LEECH")) { ExpectedOccurrence = receipt,
            Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = initial.EntryOptions.Single(o => o.Key == "RIP").Value }] } };
        var matched = Pure(run, [plan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(matched.AppliedActions == 1 && !matched.AuthoredActionsNotApplied,
            "automatic occurrence receipt allows the same event instance's stored action");
        var stale = plan with { Event = plan.Event! with { ExpectedOccurrence = receipt with
            { ExpectedCursor = receipt.ExpectedCursor + 1, SelectedCursor = receipt.SelectedCursor + 1 } } };
        var changed = Pure(run, [stale], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Event!;
        Check(changed.AppliedActions == 0 && changed.AuthoredActionsNotApplied && changed.EntryOptions.Length == 2 &&
            changed.PendingCauses.Any(c => c.Code == "PlannedEventOccurrenceChanged" && c.AffectedDomains == PredictorCombatDomain.None),
            "same model at a changed occurrence preserves old intent without replaying it or pinning the old instance");
    }

    private static void LiveDefaultUiBranchReplacement(PredictorRun captured)
    {
        // Mirror EventOptionSection(initial: true): replace the whole branch
        // action sequence, preserving an authored appearance or null default.
        static PredictorPlannedNode ReplaceInitial(PredictorPlannedNode plan, PredictorEventNodeKnowledge node, string key)
            => plan with { Event = new(node.Event!.Value)
            {
                Occurrence = plan.Event?.Occurrence,
                Actions = [new(PredictorEventActionKind.Option)
                    { ExpectedOption = node.EntryOptions.Single(o => o.Key == key).Value }]
            } };

        foreach (bool explicitOccurrence in new[] { false, true })
        {
            var run = Fixture(captured, "BRAIN_LEECH");
            var initial = Pure(run, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
            var appearance = explicitOccurrence ? Plan(run, "BRAIN_LEECH").Event!.Occurrence : null;
            var empty = new PredictorPlannedNode(run.Working.Position.NodeId) { Event = new(E("BRAIN_LEECH")) { Occurrence = appearance } };
            var share = ReplaceInitial(empty, initial, "SHARE_KNOWLEDGE");
            var grid = Pure(run, [share], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
            var picked = Add(share, new(PredictorEventActionKind.Cards) { ExpectedCards = grid.OfferedCards, CardIndices = [0] });
            var completed = Pure(run, [picked], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            Check(completed.Advanced && completed.Event!.NeededInput == PredictorEventNeededInput.None &&
                completed.Cards.Length == run.Working.Deck.Length + 1, "UI replacement fixture first settles its selected grid card");

            var rip = ReplaceInitial(picked, completed.Event!, "RIP");
            var ripped = Pure(run, [rip], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            var ripReward = ripped.Event!.Rewards.Single();
            Check(rip.Event!.Occurrence == appearance && rip.Event.Actions.Length == 1 &&
                ripped.Advanced && ripped.Event.AppliedActions == 1 && ripped.Event.NeededInput == PredictorEventNeededInput.None &&
                ripReward.Value is { Status: PredictorRewardStatus.Skipped } && ripReward.Value.Cards.Length == 3 &&
                ripReward.DefaultSkipped && ripReward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault &&
                ripReward.AppliedChoice == null && ripped.Cards.Select(card => Display(card.Value))
                    .SequenceEqual(run.Working.Deck.Select(run.Working.Card).Select(Display)) &&
                ripReward.Value.Cards.SequenceEqual(initial.EntryOptions.Single(option => option.Key == "RIP").ResultPreview!.Rewards.Single().Value!.Cards),
                "changing to RIP clears the prior grid receipt, recomputes its source cards, and defaults to Skip while preserving the appearance binding");
            Check(!ripped.PendingCauses.Any(c => c.Code.StartsWith("PlannedEvent", StringComparison.Ordinal)),
                "replacement branch has no stale option, generated-grid or accepted-prefix binding");

            var back = ReplaceInitial(rip, ripped.Event, "SHARE_KNOWLEDGE");
            var restored = Pure(run, [back], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            Check(back.Event!.Occurrence == appearance && back.Event.Actions.Length == 1 && restored.Event!.AppliedActions == 1 &&
                restored.Event.NeededInput == PredictorEventNeededInput.Cards && restored.Event.CardsRequired == 1 &&
                restored.Event.OfferedCards.SequenceEqual(grid.OfferedCards) && restored.Cards.Length == run.Working.Deck.Length &&
                restored.Event.Rewards.IsEmpty && !restored.Event.HealthRequired,
                "changing back recomputes the original five-card grid with no prior receipt, HP or reward residue");
        }
    }

    private static void LiveDefaultRewardBeforeHealth(PredictorRun captured)
    {
        var run = Fixture(captured, "BRAIN_LEECH");
        var initial = Pure(run, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        var branch = new PredictorPlannedNode(run.Working.Position.NodeId) { Event = new(E("BRAIN_LEECH"))
        {
            Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = initial.EntryOptions.Single(o => o.Key == "RIP").Value }]
        } };
        var preview = Pure(run, [branch], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        var reward = preview.Rewards.Single(r => r.Kind == PredictorRewardKind.Card);
        var receipt = new PredictorPlannedEventAction(PredictorEventActionKind.Reward)
        { RewardChoice = new(reward.Value!, reward.Occurrence, CardIndex: 1) };
        var chosen = Add(branch, receipt);
        var withoutHp = Pure(run, [chosen], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(withoutHp.Nodes.Length == 2 && withoutHp.Nodes[0].Event!.AppliedActions == 2 &&
            !withoutHp.Nodes[0].Event!.HealthRequired && withoutHp.Nodes[0].Event!.PendingHealthCommands.SequenceEqual([new PredictorEventHealthDelta(-5, 0)]) &&
            withoutHp.Nodes[0].Event!.Rewards.Single().Value!.Status == PredictorRewardStatus.Taken &&
            withoutHp.Nodes[0].Cards.Length == run.Working.Deck.Length + 1 &&
            withoutHp.Nodes[0].Cards.Last().Value.Key == reward.Value!.Cards[1].Key &&
            !withoutHp.Nodes[0].PendingCauses.Any(c => c.Code.StartsWith("Planned", StringComparison.Ordinal)),
            "unknown RIP HP does not block a confirmed card receipt or independent continuation");
        Check(withoutHp.Nodes[0].Event!.HealthConsequences.IsEmpty &&
            withoutHp.Nodes[0].PendingCauses.Any(c => c.Code == "EventHealthOutcomePending") &&
            !withoutHp.Nodes[0].PendingCauses.Any(c => c.Code == "PendingCardChoice"),
            "confirmed card remains exact while only its missing actual HP outcome stays pending");

        var initialReward = initial.EntryOptions.Single(option => option.Key == "RIP").ResultPreview!.Rewards.Single();
        Check(initialReward.DefaultSkipped && initialReward.CompletedEdit == null && initialReward.Kind == PredictorRewardKind.Card,
            "an unselected RIP branch retains an ordinary-card default Skip in its direct event reward owner");
        var directPreviewChoice = Add(branch, new(PredictorEventActionKind.Reward)
            { RewardChoice = new(initialReward.Value!, initialReward.Occurrence, CardIndex: 1) });
        var directResult = Pure(run, [directPreviewChoice], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(directPreviewChoice.Event!.Actions.Length == 2 && directPreviewChoice.Event.Actions[0].ExpectedOption == branch.Event!.Actions[0].ExpectedOption &&
            directPreviewChoice.Event.Actions[1].RewardChoice?.CardIndex == 1 && directResult.Nodes.Length == 2 &&
            directResult.Nodes[0].Event is { AppliedActions: 2, NeededInput: PredictorEventNeededInput.None, Advanced: true } &&
            directResult.Nodes[0].Event!.Rewards.Single().AppliedChoice?.CardIndex == 1 &&
            directResult.Nodes[0].Cards.Select(card => Display(card.Value)).SequenceEqual(withoutHp.Nodes[0].Cards.Select(card => Display(card.Value))),
            "a direct preview choice authors RIP plus its exact reward and advances the independent route without an extra authored LeaveRewards step");
        var concrete = Fixture(captured, "BRAIN_LEECH");
        Enter(concrete, Plan(concrete, "BRAIN_LEECH")); Select(concrete, new ChooseEventOption("RIP"));
        Select(concrete, new ProvideEventHealthFact(new(40, 80)));
        var concreteOffer = concrete.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Select(concrete, new EnterCardSelection(concreteOffer.Id)); Select(concrete, new TakeReward(concreteOffer.Id, concreteOffer.Cards[1].Id));
        Select(concrete, new LeaveRewards()); concrete.AdvanceRoute();
        Check(directResult.Nodes[0].Cards.Select(card => Display(card.Value)).SequenceEqual(concrete.Working.Deck.Select(id => Display(concrete.Working.Card(id)))) &&
            directResult.Nodes[1].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards.SequenceEqual(
                concrete.PreviewCurrentCombat()!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!.Cards),
            "direct RIP preview selection matches concrete event generation, selected deck card and later reward RNG");

        var close = Add(chosen, new(PredictorEventActionKind.LeaveRewards));
        var closed = Pure(run, [close], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(closed.Nodes[0].Event!.AppliedActions == 3 && !closed.Nodes[0].Event!.HealthRequired &&
            closed.Nodes[0].Cards.Select(c => Display(c.Value)).SequenceEqual(withoutHp.Nodes[0].Cards.Select(c => Display(c.Value))) &&
            closed.Nodes[1].Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Value!.Cards
                .SequenceEqual(withoutHp.Nodes[1].Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Value!.Cards),
            "unknown HP reward closure preserves receipt and consumes no duplicate generation RNG");

        // Existing explicit health plans retain their ordered source cost fact.
        // Live defaults require no optional HP editor for this pure cost.
        const int healthActionIndex = 1;
        var health = new PredictorPlannedEventAction(PredictorEventActionKind.Health) { Health = new(40, 80) };
        var supplied = close with { Event = close.Event! with { Actions = close.Event!.Actions.Insert(healthActionIndex, health) } };
        var actual = Pure(run, [supplied], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(supplied.Event!.Actions[healthActionIndex + 1] == receipt && actual.Nodes[0].Event!.AppliedActions == 4 &&
            actual.Nodes[0].Event!.HealthConsequences.SequenceEqual([new PredictorEventHealthDelta(-5, 0)]) &&
            !actual.Nodes[0].Event!.HealthRequired && actual.Nodes[0].Event!.PendingHealthCommands.IsEmpty &&
            !actual.Nodes[0].PendingCauses.Any(c => c.Code == "EventHealthOutcomePending") &&
            actual.Nodes[0].Cards.Select(c => Display(c.Value)).SequenceEqual(withoutHp.Nodes[0].Cards.Select(c => Display(c.Value))) &&
            actual.Nodes[1].Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Value!.Cards
                .SequenceEqual(withoutHp.Nodes[1].Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Value!.Cards),
            "later actual HP executes one source cost and preserves selected card, closure and future RNG identities");

        var lethal = supplied with { Event = supplied.Event! with { Actions = supplied.Event!.Actions.SetItem(healthActionIndex,
            health with { Health = new(3, 80) }) } };
        var dead = Pure(run, [lethal], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(lethal.Event!.Actions[healthActionIndex + 1] == receipt && dead.Nodes.Length == 1 &&
            dead.Nodes[0].Cards.Length == run.Working.Deck.Length && dead.Nodes[0].Event!.Rewards.IsEmpty &&
            dead.Nodes[0].Event!.HealthConsequences.SequenceEqual([new PredictorEventHealthDelta(-3, 0)]),
            "real lethal HP keeps receipt intent in the plan but source death prevents receipt and later nodes");
    }

    private static void LiveRewardAlternatives(PredictorRun captured)
    {
        foreach (string relic in new[] { "DRIFTWOOD", "PAELS_WING" })
        {
            var run = Fixture(captured, "BRAIN_LEECH", configure: state => state with
            {
                Relics = [new(state.NextInstanceId, new("RELIC", relic), 1) { RewardsSacrificed = 1 }],
                NextInstanceId = state.NextInstanceId + 1,
                PersonalBag = PredictorRelicBag.Empty(false)
            });
            var initial = Pure(run, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Event!;
            var branch = new PredictorPlannedNode(run.Working.Position.NodeId) { Event = new(E("BRAIN_LEECH"))
            {
                Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = initial.EntryOptions.Single(o => o.Key == "RIP").Value }]
            } };
            var offer = Pure(run, [branch], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Event!.Rewards
                .Single(reward => reward.Kind == PredictorRewardKind.Card);
            var choice = relic == "DRIFTWOOD"
                ? new PredictorPlannedRewardChoice(offer.Value!, offer.Occurrence) { Reroll = true }
                : new PredictorPlannedRewardChoice(offer.Value!, offer.Occurrence) { SacrificeRelicId = run.Working.Relics.Single().Id };
            var action = new PredictorPlannedEventAction(PredictorEventActionKind.Reward) { RewardChoice = choice };
            var planned = Add(branch, action);
            var result = Pure(run, [planned], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var reward = result.Nodes[0].Event!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
            Check(result.Nodes[0].Event!.AppliedActions == 2 && result.Nodes.Length == 2,
                "event card alternative must use the same plan operation and continue independent route information");
            if (relic == "DRIFTWOOD")
            {
                Check(reward.Value is { HasRerolled: true, CanReroll: false, Status: PredictorRewardStatus.Skipped } &&
                    reward.AppliedChoice?.Reroll == true && reward.DefaultSkipped &&
                    reward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault &&
                    result.Nodes[0].Cards.Length == run.Working.Deck.Length,
                    "event reroll-only intent must publish its one source replacement, then default Skip without a card receipt");
                var take = planned with { Event = planned.Event! with
                {
                    Actions = planned.Event.Actions.SetItem(1, action with
                    { RewardChoice = choice with { ExpectedRerolledReward = reward.Value, CardIndex = 0 } })
                } };
                var selected = Pure(run, [take], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
                Check(selected.Nodes[0].Event!.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value?.Status == PredictorRewardStatus.Taken &&
                    selected.Nodes[0].Cards.Any(card => card.Value.Key == reward.Value!.Cards[0].Key),
                    "selecting after an event reroll must retain the original reroll and obtain its new card");
            }
            else Check(reward.SacrificeCount == 2 && reward.SacrificeRelic?.Entry == "CIRCLET" &&
                result.Nodes[0].Cards.Length == run.Working.Deck.Length,
                "event sacrifice must share the counter, obtain a relic, and never take a displayed card");
        }
    }

    private static void LiveDefaultHealthScope(PredictorRun captured)
    {
        foreach (var (entry, option, command) in new[]
        {
            ("MORPHIC_GROVE", "LONER", new PredictorEventHealthDelta(5, 5)),
            ("THIS_OR_THAT", "PLAIN", new PredictorEventHealthDelta(-6, 0)),
            ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK", new PredictorEventHealthDelta(6, 6)),
            ("TABLET_OF_TRUTH", "SMASH", new PredictorEventHealthDelta(20, 0))
        })
        {
            var run = Fixture(captured, entry);
            var plan = Option(run, Plan(run, entry), option);
            var node = Pure(run, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
            Check(node.Advanced && !node.Event!.HealthRequired && node.Event.PendingHealthCommands.SequenceEqual([command]) &&
                node.Event.HealthConsequences.IsEmpty && node.PendingCauses.Any(c => c.Code == "EventHealthOutcomePending"),
                "fixed cost/heal has no HP input requirement and preserves its source command: " + entry);
        }
        foreach (var (entry, option) in new[] { ("UNREST_SITE", "REST"), ("SPIRALING_WHIRLPOOL", "DRINK") })
        {
            var run = Fixture(captured, entry);
            var node = Pure(run, [Option(run, Plan(run, entry), option)], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            Check(node.Advanced && !node.Event!.HealthRequired && node.Event.HealthConsequences.IsEmpty &&
                node.Event.PendingHealthCommands.IsEmpty && node.PendingCauses.Any(c => c.Code == "EventHealthOutcomePending"),
                "unknown capped healing amount remains an internal dependency without fictitious numbers: " + entry);
        }
        var dense = Fixture(captured, "DENSE_VEGETATION");
        var rested = Pure(dense, [Option(dense, Plan(dense, "DENSE_VEGETATION"), "REST")],
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(!rested.HealthRequired && rested.Advanced && rested.NeededInput == PredictorEventNeededInput.None &&
            rested.AppliedActions == 1 && rested.HealthConsequences.IsEmpty &&
            rested.Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card),
            "drawing Dense Vegetation continues its deterministic fight successor and independent rewards with healing amount unknown");
        var authoredRest = Pure(dense, [Option(dense, Plan(dense, "DENSE_VEGETATION"), "REST")],
            policy: PredictorRoutePreviewPolicy.AuthoredChoices).Nodes.Single().Event!;
        Check(authoredRest.HealthRequired && !authoredRest.Advanced,
            "Authored Dense Vegetation still requires actual health before settling Rest and its fight successor");
        var doll = Fixture(captured, "DOLL_ROOM");
        var examined = Pure(doll, [Option(doll, Plan(doll, "DOLL_ROOM"), "EXAMINE")],
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(!examined.HealthRequired && examined.Options.Length == 3 &&
            examined.PendingHealthCommands.SequenceEqual([new PredictorEventHealthDelta(-15, 0)]) &&
            examined.Options.All(o => o.ResultPreview != null),
            "cost-only Doll Room choices and their independent previews replay without HP");
        var sapphire = Fixture(captured, "SAPPHIRE_SEED");
        var eat = Pure(sapphire, [Option(sapphire, Plan(sapphire, "SAPPHIRE_SEED"), "EAT")],
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(eat.Advanced && !eat.HealthRequired && eat.NeededInput == PredictorEventNeededInput.Cards &&
            eat.PendingHealthCommands.SequenceEqual([new PredictorEventHealthDelta(9, 0)]),
            "fixed healing does not block the independent pending upgrade target");

        var conveyor = Pure(Fixture(captured, "ENDLESS_CONVEYOR"), [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults)
            .Nodes.Single().Event!;
        Check(conveyor.HealthRequired && conveyor.NeededInput == PredictorEventNeededInput.Health,
            "Conveyor still requires actual HP for its weighted dish pool");
        TabletHealthScope(captured);
    }

    internal static void TabletHealthScope(PredictorRun captured)
    {
        const string entry = "TABLET_OF_TRUTH";
        var tablet = Fixture(captured, entry);
        var initial = Plan(tablet, entry);
        var initialView = Pure(tablet, [initial], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        var decipherOption = initialView.Options.Single(option => option.Key == "DECIPHER");
        Check(decipherOption.Value!.MaxHpCost == 3 && decipherOption.ResultPreview is { HealthRequired: false } preview &&
            preview.ChangedCards.IsEmpty && preview.LocalPendingCauses.Any(cause => cause.Code == "EventTabletDecipherOutcomePending"),
            "Tablet entry preserves the stated max-HP price and reports an uncertain upgrade without asking for HP");

        var plan = Add(initial, new(PredictorEventActionKind.Option) { ExpectedOption = decipherOption.Value });
        var continued = Pure(tablet, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var decipher = continued.Nodes[0].Event!;
        Check(continued.Nodes.Length == 2 && continued.Nodes[0].Advanced && !decipher.HealthRequired &&
            decipher.NeededInput == PredictorEventNeededInput.Option && decipher.AppliedActions == 1 &&
            decipher.Options.Single(option => option.Key == "DECIPHER").Value!.MaxHpCost == 6 &&
            decipher.AppliedResult is { ChangedCards.IsEmpty: true } && decipher.HealthConsequences.IsEmpty &&
            decipher.PendingHealthCommands.IsEmpty &&
            decipher.PendingCauses.Any(cause => cause.Code == "EventTabletDecipherOutcomePending" &&
                cause.ReasonKind == PredictorPendingReasonKind.MissingObservation &&
                (cause.AffectedDomains & (PredictorCombatDomain.Health | PredictorCombatDomain.Deck)) ==
                (PredictorCombatDomain.Health | PredictorCombatDomain.Deck)) &&
            continued.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
            "Tablet selected Decipher retains HP/upgrade uncertainty, repeat choices and independent next-combat rewards");
        var omitted = Pure(tablet, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(continued.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card).Select(reward => reward.Value)
            .Select(value => JsonSerializer.Serialize(value)).SequenceEqual(omitted.Nodes[1].Rewards
                .Where(reward => reward.Kind == PredictorRewardKind.Card).Select(reward => JsonSerializer.Serialize(reward.Value))),
            "Tablet's unresolved private draw cannot alter later public reward offers");

        var authored = Pure(tablet, [plan]).Nodes.Single().Event!;
        Check(authored.HealthRequired && !authored.Advanced && authored.NeededInput == PredictorEventNeededInput.Health,
            "Authored Tablet still requires actual HP before computing its lethal branch and upgrade target");
        var declared = Add(plan, new(PredictorEventActionKind.Health) { Health = new(50, 80) });
        var known = Pure(tablet, [declared], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(!known.HealthRequired && known.AppliedResult!.ChangedCards.Length == 1 &&
            known.HealthConsequences.Sum(consequence => consequence.MaxHpDelta) == -3 &&
            known.PendingCauses.All(cause => cause.Code != "EventTabletDecipherOutcomePending"),
            "An explicitly authored Tablet health fact retains native exact loss and random-upgrade execution");

        var givingUp = Add(plan, new(PredictorEventActionKind.Option)
        { ExpectedOption = decipher.Options.Single(option => option.Key == "GIVE_UP").Value });
        var exited = Pure(tablet, [givingUp], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(!exited.HealthRequired && exited.Advanced && exited.AppliedActions == 2,
            "Tablet Give Up remains usable after a Decipher with unresolved health");
        foreach (int? nextCost in new int?[] { 12, 24, null })
        {
            plan = Add(plan, new(PredictorEventActionKind.Option)
            { ExpectedOption = decipher.Options.Single(option => option.Key == "DECIPHER").Value });
            decipher = Pure(tablet, [plan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
            Check(!decipher.HealthRequired && decipher.Options.Single(option => option.Key == "DECIPHER").Value!.MaxHpCost == nextCost,
                "Tablet repeated Decipher preserves fixed costs and leaves the final max-HP-derived cost unknown");
        }
        plan = Add(plan, new(PredictorEventActionKind.Option)
        { ExpectedOption = decipher.Options.Single(option => option.Key == "DECIPHER").Value });
        var fifth = Pure(tablet, [plan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(!fifth.HealthRequired && fifth.Advanced && fifth.AppliedActions == 5 && fifth.AppliedResult!.ChangedCards.IsEmpty,
            "Tablet's fifth Decipher finishes without inventing survivor health or confirmed upgraded targets");
        Console.WriteLine("RUNTIME_TABLET_HEALTH_PASS independent public rewards; unresolved HP and upgrade targets; repeat prices and Give Up; explicit HP and Authored completeness");
    }

    private static void LiveDefaultEligibilityGates(PredictorRun captured)
    {
        var pendingContext = captured.Context with { Live = new(2, true,
            [new("EligibilityDeckHpUnknown", "fixture missing deck/HP", PredictorCombatDomain.Deck | PredictorCombatDomain.Health)]) };
        var mixed = Fixture(captured, "MORPHIC_GROVE", context: pendingContext, configure: state => state with
        {
            GoldBudget = 40,
            Acts = state.Acts.Select(a => a.Act == 2 ? a with
            { RawEvents = [E("MORPHIC_GROVE"), E("SLIPPERY_BRIDGE"), E("BRAIN_LEECH")] } : a).ToImmutableArray()
        });
        var eventView = Pure(mixed, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(eventView.Event == E("BRAIN_LEECH") && eventView.Occurrence!.Candidates.Single().QueueEvent == E("BRAIN_LEECH"),
            "unknown deck/HP cannot bypass known failed mixed gold or floor gates");

        var unknownGoldContext = captured.Context with { Live = new(2, true,
            [new("EligibilityGoldUnknown", "fixture unknown actual gold", PredictorCombatDomain.Gold)]) };
        var unknownGold = Fixture(captured, "MORPHIC_GROVE", context: unknownGoldContext, configure: state => state with
        {
            GoldBudget = 0,
            Acts = state.Acts.Select(a => a.Act == 2 ? a with
            { RawEvents = [E("MORPHIC_GROVE"), E("BRAIN_LEECH")] } : a).ToImmutableArray()
        });
        var plausible = Pure(unknownGold, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
        Check(plausible.SelectedOccurrence == null && !plausible.Advanced && plausible.GoldBalance == null &&
            plausible.PendingCauses.Any(cause => cause.Code == "EventGoldAppearancePending") &&
            PredictorRouteCauseEvidence.OriginalCauses(plausible.PendingCauses).Any(cause => cause.Code == "EligibilityGoldUnknown"),
            "unknown Gold stops at the actual gold-dependent event occurrence and retains its original source uncertainty");

        var special = Fixture(captured, "RANWID_THE_ELDER", configure: state => state with
        {
            Acts = state.Acts.Select(a => a.Act == 2 ? a with
            { RawEvents = [E("RANWID_THE_ELDER"), E("THE_FUTURE_OF_POTIONS"), E("BRAIN_LEECH")] } : a).ToImmutableArray(),
            Relics = [], PotionSlots = state.PotionSlots.Select(_ => (PredictorPotion?)null).ToImmutableArray()
        });
        Check(Pure(special, [], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!.Event == E("BRAIN_LEECH"),
            "live default still evaluates potion and tradable relic source gates");
    }

    private static void LiveOrderedOccurrenceExclusions(PredictorRun captured)
    {
        var queue = new[] { E("SLIPPERY_BRIDGE"), E("TEA_MASTER"), E("TRASH_HEAP"), E("BRAIN_LEECH") }.ToImmutableArray();
        PredictorRun Ordered(int gold, int floor = 1, int cursor = 0) => Fixture(captured, "TEA_MASTER",
            configure: state => state with
            {
                GoldBudget = gold, Position = state.Position with { Floor = floor },
                Acts = state.Acts.Select(a => a.Act == 2 ? a with { RawEvents = queue, EventCursor = cursor } : a).ToImmutableArray()
            });
        PredictorEventNodeKnowledge Preview(PredictorRun run, params PredictorPlannedNode[] plans) =>
            Pure(run, [.. plans], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Event!;

        var enough = Ordered(150);
        var tea = Preview(enough);
        Check(tea.Event == E("TEA_MASTER") && tea.SelectedOccurrence is { Cursor: 1, IsUnconditional: true } &&
            tea.Occurrence!.Candidates.Select(c => c.QueueEvent).SequenceEqual([E("TEA_MASTER")]),
            "actual Tea Master >=150 threshold closes the ordered candidates before later HP/unconditional entries");
        var tooLittle = Ordered(149);
        var trash = Preview(tooLittle);
        Check(trash.Event == E("TRASH_HEAP") && trash.SelectedOccurrence is
            { Cursor: 2, HealthEligibilityAssumed: true, EligibilityAssumed: true, IsUnconditional: false } &&
            trash.Occurrence!.Candidates.Select(c => c.QueueEvent).SequenceEqual([E("TRASH_HEAP"), E("BRAIN_LEECH")]) &&
            !trash.HealthRequired,
            "arrival floor rejects Bridge, 149 rejects Tea, and unknown HP admits Trash without fabricating HP");
        var exclusion = new PredictorEventOccurrenceExclusion(E("TRASH_HEAP"), 2);
        var excluded = new PredictorPlannedNode(tooLittle.Working.Position.NodeId) { EventOccurrenceExclusions = [exclusion] };
        var brain = Preview(tooLittle, excluded);
        Check(brain.Event == E("BRAIN_LEECH") && brain.SelectedOccurrence is { Cursor: 3, IsUnconditional: true } &&
            brain.Occurrence!.Candidates[0].ExcludedByPlayer && !brain.Occurrence.Candidates[1].ExcludedByPlayer,
            "one node-local rejection selects the next candidate while retaining the rejected row for undo");
        Check(Preview(tooLittle, excluded with { EventOccurrenceExclusions = [] }).Event == E("TRASH_HEAP"),
            "withdrawing an exclusion restores the first candidate in queue order");
        var cannotSkipClose = excluded with
        { EventOccurrenceExclusions = [exclusion, new(E("BRAIN_LEECH"), 3)] };
        Check(Preview(tooLittle, cannotSkipClose).Event == E("BRAIN_LEECH"),
            "an unconditional close cannot be manually excluded to select later queue entries");
        Check(Preview(enough, excluded).Event == E("TEA_MASTER"),
            "arrival gold recomputation restores an earlier eligible candidate despite a later exclusion");
        Check(Preview(Ordered(150, floor: 5)).Event == E("TEA_MASTER") &&
            Preview(Ordered(150, floor: 6)).SelectedOccurrence?.QueueEvent == E("SLIPPERY_BRIDGE"),
            "Bridge reads actual arrival floor >6, including the floor-six/floor-seven boundary");
        Check(Preview(Ordered(149, cursor: 3)).SelectedOccurrence is { QueueEvent.Entry: "BRAIN_LEECH", Cursor: 3 },
            "initial selection starts at the captured current cursor, not queue zero or an invented previous event");

        var authoredTea = new PredictorPlannedNode(enough.Working.Position.NodeId)
        {
            Event = new(E("TEA_MASTER"))
            { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = tea.EntryOptions.Single(o => o.Key == "BONE_TEA").Value }] }
        };
        string savedIntent = JsonSerializer.Serialize(authoredTea);
        var changed = Pure(tooLittle, [authoredTea], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var changedEvent = changed.Nodes[0].Event!;
        Check(changed.Nodes.Length == 1 && !changedEvent.Advanced && changedEvent.BusinessChoiceRequired &&
            changedEvent.NeededInput == PredictorEventNeededInput.Option &&
            changedEvent.Event == E("TRASH_HEAP") && changedEvent.AuthoredEvent == E("TEA_MASTER") &&
            changedEvent.AuthoredActionsNotApplied && changedEvent.AppliedActions == 0 &&
            changedEvent.PendingCauses.Any(c => c.Code == "PlannedEventChanged") &&
            changedEvent.EntryOptions.Select(o => o.Key).SequenceEqual(trash.EntryOptions.Select(o => o.Key)) &&
            savedIntent == JsonSerializer.Serialize(authoredTea),
            "automatic identity change preserves the old Tea intent and stops at the new Trash business choice without applying that stale plan; " + ChainEvidence(changed) +
            "; authored=" + changedEvent.AuthoredEvent + "; retained=" + changedEvent.AuthoredActionsNotApplied + "; applied=" + changedEvent.AppliedActions +
            "; needed=" + changedEvent.NeededInput + "; business=" + changedEvent.BusinessChoiceRequired);
        var unchanged = Preview(enough, authoredTea);
        Check(unchanged.Event == E("TEA_MASTER") && unchanged.AppliedActions == 1 && !unchanged.AuthoredActionsNotApplied,
            "restored arrival gold rebinds the original authored Tea action instead of deleting it");

        // Consecutive event nodes exercise native selected-cursor +1 and
        // recompute eligibility using an edited preceding event purchase.
        var consecutive = Fixture(captured, "TEA_MASTER", configure: state => state with
        {
            GoldBudget = 150,
            Maps = state.Maps.Select(m => m with { Map = m.Map with
            { Points = m.Map.Points.Select(p => p.Position.Row == 2 ? p with { Type = MapPointType.Unknown } : p).ToArray() } }).ToImmutableArray(),
            Acts = state.Acts.Select(a => a.Act == 2 ? a with
            { RawEvents = [E("TEA_MASTER"), E("WHISPERING_HOLLOW"), E("BRAIN_LEECH")] } : a).ToImmutableArray()
        });
        var first = Preview(consecutive);
        var spent = new PredictorPlannedNode(consecutive.Working.Position.NodeId)
        { Event = new(E("TEA_MASTER"))
            { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = first.EntryOptions.Single(o => o.Key == "BONE_TEA").Value }] } };
        var next = Pure(consecutive, [spent], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(next.Nodes.Length == 2 && next.Nodes[1].Event?.SelectedOccurrence is { QueueEvent.Entry: "WHISPERING_HOLLOW", Cursor: 1 } &&
            next.Nodes[1].Event!.Occurrence!.Cursor == 1,
            "the next event starts after the selected slot and reads arrival gold after a 50-gold Tea purchase");
        var spentAll = spent with { Event = spent.Event! with
        { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = first.EntryOptions.Single(o => o.Key == "EMBER_TEA").Value }] } };
        var afterEdit = Pure(consecutive, [spentAll], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(afterEdit.Nodes.Length == 2 && afterEdit.Nodes[1].Event?.SelectedOccurrence is { QueueEvent.Entry: "BRAIN_LEECH", Cursor: 2 },
            "editing the preceding purchase to 150 gold automatically skips Whispering Hollow's >=44 gate");
    }

    private static void MorphicAndPrefix(PredictorRun captured)
    {
        var run = Fixture(captured, "MORPHIC_GROVE");
        var unselected = Pure(run, []).Nodes.Single().Event!;
        Check(!unselected.Advanced && unselected.NeededInput is PredictorEventNeededInput.Occurrence or PredictorEventNeededInput.Option,
            "event occurrence and choice have no default");
        var plan = Plan(run, "MORPHIC_GROVE");
        plan = Option(run, plan, "GROUP");
        var choice = View(run, plan);
        Check(choice.NeededInput == PredictorEventNeededInput.Cards && choice.CardsRequired == 2 &&
            choice.DeckCardCandidates.Count(c => c.IsKnown) > 2 && choice.GoldBalance == 0,
            "Morphic Group applies stolen gold and exposes two actual transformation targets");
        var targets = choice.DeckCardCandidates.Take(2).Select(c => Target(c.Value)).ToImmutableArray();
        plan = Add(plan, new(PredictorEventActionKind.Cards) { DeckCards = targets });
        var projected = Pure(run, [plan], 2);
        Check(projected.Nodes[0].Advanced && projected.Nodes[1].Room == PredictorRoomKind.Monster,
            "completed event joins the next modeled route node");
        var concrete = Fixture(captured, "MORPHIC_GROVE");
        var niche = concrete.Working.Streams.Single(s => s.Stream == PredictorStream.Niche);
        Enter(concrete, plan); Select(concrete, new ChooseEventOption("GROUP"));
        Select(concrete, new SelectEventCards(targets.Select(c => c.InstanceId).ToImmutableArray()));
        DeckEqual(projected.Nodes[0], concrete, "Morphic projection equals concrete source transformations");
        Check(niche == concrete.Working.Streams.Single(s => s.Stream == PredictorStream.Niche), "Morphic transforms preserve Niche");

        Enter(run, plan); Select(run, new ChooseEventOption("GROUP"));
        Check(View(run, plan).Advanced, "accepted event option is a preserved prefix");
        var changed = plan with { Event = plan.Event! with { Actions = plan.Event.Actions.SetItem(1,
            plan.Event.Actions[1] with { DeckCards = targets.SetItem(0, targets[0] with { Key = new("CARD", "UNAVAILABLE_MOD_CARD") }) }) } };
        Check(View(run, changed).PendingCauses.Any(c => c.Code == "PlannedEventDeckTargetPending"),
            "changed persistent target remains pending without rebinding");
        var changedChoice = plan with { Event = plan.Event! with { Actions = plan.Event.Actions.SetItem(0,
            plan.Event.Actions[0] with { ExpectedOption = new("LONER") }) } };
        Check(View(run, changedChoice).PendingCauses.Any(c => c.Code == "PlannedEventAcceptedPrefixChanged"),
            "a changed accepted branch cannot silently use the current child");

        var loner = Fixture(captured, "MORPHIC_GROVE");
        var lonerPlan = Option(loner, Plan(loner, "MORPHIC_GROVE"), "LONER");
        Check(View(loner, lonerPlan).HealthRequired, "health-dependent branch asks for a local fact");
        lonerPlan = Add(lonerPlan, new(PredictorEventActionKind.Health) { Health = new(40, 80) });
        var otherBranch = Pure(loner, [lonerPlan]).Nodes.Single();
        Check(otherBranch.Advanced && otherBranch.Event!.GoldBalance == 1000 && otherBranch.Cards.Length == loner.Working.Deck.Length,
            "alternate event branch projects independently from the untouched entry");
    }

    private static void ConveyorAndHealth(PredictorRun captured)
    {
        var run = Fixture(captured, "ENDLESS_CONVEYOR");
        var plan = Plan(run, "ENDLESS_CONVEYOR");
        var entry = View(run, plan);
        Check(entry.HealthRequired && entry.Options.IsEmpty && entry.NeededInput == PredictorEventNeededInput.Health,
            "Conveyor requires real or assumed current/max HP before rolling its full/nonfull pool");
        plan = Add(plan, new(PredictorEventActionKind.Health) { Health = new(60, 80) });
        Check(!View(run, plan).Options.IsEmpty, "declared HP exposes the native weighted dish");
        for (int i = 0; i < 3; i++)
        {
            var dish = View(run, plan).Options.Single(o => o.Key.StartsWith("GRAB_", StringComparison.Ordinal));
            plan = Option(run, plan, dish.Key);
            var afterGrab = View(run, plan);
            if (afterGrab.NeededInput == PredictorEventNeededInput.Cards)
                plan = Add(plan, new(PredictorEventActionKind.Cards) { DeckCards = [Target(afterGrab.DeckCardCandidates.First(c => c.IsKnown).Value)] });
            else if (afterGrab.NeededInput == PredictorEventNeededInput.Rewards)
                plan = Add(plan, new(PredictorEventActionKind.LeaveRewards));
            Check(View(run, plan).Options.Any(o => o.Key == "LEAVE"), "every grab resumes the parent Conveyor choice");
        }
        plan = Option(run, plan, "LEAVE");
        Check(View(run, plan).Advanced && View(run, plan).AppliedActions == plan.Event!.Actions.Length,
            "multiple dishes, child picks and explicit leave preserve ordered event continuation");

        var baths = Fixture(captured, "ABYSSAL_BATHS");
        var bathPlan = Option(baths, Plan(baths, "ABYSSAL_BATHS"), "IMMERSE");
        bathPlan = Add(bathPlan, new(PredictorEventActionKind.Health) { Health = new(70, 80) });
        bathPlan = Option(baths, bathPlan, "LINGER");
        bathPlan = Option(baths, bathPlan, "EXIT_BATHS");
        Check(View(baths, bathPlan).Advanced, "health already declared for a loop remains local to its parent event");
    }

    private static void GeneratedOffersAndObtains(PredictorRun captured)
    {
        var cheese = Fixture(captured, "ROOM_FULL_OF_CHEESE");
        var plan = Option(cheese, Plan(cheese, "ROOM_FULL_OF_CHEESE"), "GORGE");
        var offers = View(cheese, plan).OfferedCards;
        Check(offers.Length == 8 && offers.All(c => c.Id == 0), "generated event grid is a semantic snapshot");
        var select = new PredictorPlannedEventAction(PredictorEventActionKind.Cards) { ExpectedCards = offers, CardIndices = [1, 5] };
        var full = Add(plan, select);
        Check(View(cheese, full).Advanced && Pure(cheese, [full]).Nodes.Single().Cards.Length == cheese.Working.Deck.Length + 2,
            "generated grid selection binds ordered offered semantics");
        var wrong = Add(plan, select with { ExpectedCards = offers.SetItem(0, offers[0] with { Key = new("CARD", "UNAVAILABLE_MOD_CARD") }) });
        Check(View(cheese, wrong).PendingCauses.Any(c => c.Code == "PlannedEventOfferedCardsChanged"),
            "a changed generated grid cannot substitute another selection");

        var mushroom = Fixture(captured, "HUNGRY_FOR_MUSHROOMS");
        var mushroomPlan = Option(mushroom, Plan(mushroom, "HUNGRY_FOR_MUSHROOMS"), "FRAGRANT_MUSHROOM");
        Check(View(mushroom, mushroomPlan).RequiredChoices.Single().Phase == PredictorPhase.ObtainHealth,
            "event relic enters the shared actual obtain transaction");
        mushroomPlan = Add(mushroomPlan, new(PredictorEventActionKind.Obtain)
        { Obtain = new(PredictorObtainChildAction.Health, new("RELIC", "FRAGRANT_MUSHROOM")) { Health = new(70, 80) } });
        Check(View(mushroom, mushroomPlan).Advanced, "shared obtain child resumes and completes its event parent");
    }

    private static void GoldMerchantAndCrystal(PredictorRun captured)
    {
        var jungle = Fixture(captured, "JUNGLE_MAZE_ADVENTURE");
        var plan = Option(jungle, Plan(jungle, "JUNGLE_MAZE_ADVENTURE"), "JOIN_FORCES");
        var outcome = View(jungle, plan);
        Check(outcome.Advanced && outcome.GoldBalance > 1000 && !outcome.HealthRequired,
            "independent gold exchange does not demand unrelated HP");

        var merchant = Fixture(captured, "FAKE_MERCHANT");
        var merchantPlan = Option(merchant, Plan(merchant, "FAKE_MERCHANT"), "OPEN_INVENTORY");
        var buy = View(merchant, merchantPlan).Options.First(o => o.Key.StartsWith("BUY_", StringComparison.Ordinal) &&
            o.Value!.OfferedRelic?.Entry != "FAKE_LEES_WAFFLE");
        merchantPlan = Add(merchantPlan, new(PredictorEventActionKind.Option) { ExpectedOption = buy.Value });
        Check(View(merchant, merchantPlan).GoldBalance == 1000 - buy.Value!.GoldCost, "event merchant uses its actual stock and price");
        merchantPlan = Option(merchant, merchantPlan, "CLOSE_INVENTORY");
        merchantPlan = Option(merchant, merchantPlan, "LEAVE");
        Check(View(merchant, merchantPlan).Advanced, "event merchant purchase and explicit closure share the parent sequence");
        var wrongPrice = merchantPlan with { Event = merchantPlan.Event! with { Actions = merchantPlan.Event.Actions.SetItem(1,
            merchantPlan.Event.Actions[1] with { ExpectedOption = buy.Value! with { GoldCost = buy.Value.GoldCost + 1 } }) } };
        Check(View(merchant, wrongPrice).PendingCauses.Any(c => c.Code == "PlannedEventOptionChanged"),
            "changed event merchant offer stays pending");

        var crystal = Fixture(captured, "CRYSTAL_SPHERE");
        var crystalPlan = Option(crystal, Plan(crystal, "CRYSTAL_SPHERE"), "PAYMENT_PLAN");
        var board = View(crystal, crystalPlan).Crystal!;
        crystalPlan = Add(crystalPlan, new(PredictorEventActionKind.CrystalTool) { ExpectedCrystal = board, Tool = PredictorCrystalTool.Small });
        board = View(crystal, crystalPlan).Crystal!;
        var cell = board.Cells.First(c => c.Hidden);
        crystalPlan = Add(crystalPlan, new(PredictorEventActionKind.CrystalCell) { ExpectedCrystal = board, X = cell.X, Y = cell.Y });
        Check(View(crystal, crystalPlan).Crystal?.Remaining == 5 && Pure(crystal, [crystalPlan]).Nodes.Single().Cards.Any(c => c.Value.Key.Entry == "DEBT"),
            "Crystal reuses the actual local minigame and ordered reveal snapshots");
    }

    private static void NestedCombat(PredictorRun captured)
    {
        // Live drawing applies the native Punch Off floor >= 6 admission rule;
        // the generic floor-2 fixture would reject the occurrence before combat.
        var punch = Fixture(captured, "PUNCH_OFF", configure: state => state with
        {
            Position = state.Position with { Floor = 5 },
            Visits = state.Visits.Select(visit => visit with { TotalFloor = 5 }).ToImmutableArray()
        });
        Check(punch.Working.Position.Floor == 6, "Punch Off combat fixture satisfies native appearance admission");
        var plan = Option(punch, Plan(punch, "PUNCH_OFF"), "I_CAN_TAKE_THEM");
        plan = Option(punch, plan, "FIGHT");
        var battle = View(punch, plan);
        Check(battle.NeededInput == PredictorEventNeededInput.Combat && battle.Combat != null && !battle.Combat.Rewards.IsEmpty,
            "nested battle publishes its actual declarations and independent rewards");
        var stale = Add(plan, new(PredictorEventActionKind.Combat)
        { ExpectedEncounter = new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER"),
            Combat = new() { CardTriggers = [new(long.MaxValue, 1)] } });
        punch.SetPlannedNode(stale);
        foreach (var policy in new[] { PredictorRoutePreviewPolicy.AuthoredChoices, PredictorRoutePreviewPolicy.LiveMapDefaults })
        {
            var pending = Pure(punch, punch.PlannedNodes, policy: policy).Nodes.Single();
            Check(!pending.Advanced && pending.PendingCauses.Any(c => c.Code == "PlannedEventCombatFactsPending") &&
                punch.PlannedNodes.Single() == stale, "Event combat retains the same stale-instance allowance under " + policy +
                "; advanced=" + pending.Advanced + "; causes=" + string.Join(",", pending.PendingCauses.Select(c => c.Code)));
            var invalid = Add(plan, stale.Event!.Actions[^1] with
            { Combat = new() { CombatCardSourceOrder = [new(DeckCardId: punch.Working.Deck[0])] } });
            try
            {
                Pure(punch, [invalid], policy: policy);
                throw new InvalidOperationException("Event route swallowed an unclassified combat execution error under " + policy);
            }
            catch (ArgumentException ex) when (ex.Message == "PredictorCombatCardSourceOrderWithoutTemporary" &&
                ex is not PredictorCombatPlanBindingException) { }
        }
        plan = Add(plan, new(PredictorEventActionKind.Combat)
        { ExpectedEncounter = new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER"), Combat = new() });
        var rewards = View(punch, plan).Rewards;
        var card = rewards.First(r => r.Kind == PredictorRewardKind.Card && r.IsKnown);
        plan = Add(plan, new(PredictorEventActionKind.Reward) { RewardChoice = new(card.Value!, card.Occurrence, CardIndex: 1) });
        plan = Add(plan, new(PredictorEventActionKind.LeaveRewards));
        Check(View(punch, plan).Advanced, "nested combat reward pickup and closure resume the actual event parent");

        var dummy = Fixture(captured, "BATTLEWORN_DUMMY");
        var dummyPlan = Option(dummy, Plan(dummy, "BATTLEWORN_DUMMY"), "SETTING_2");
        Check(View(dummy, dummyPlan).Combat!.MissingFacts.EventTimeout, "Dummy timeout has no fabricated default");
        var fight = new PredictorPlannedEventAction(PredictorEventActionKind.Combat)
        { ExpectedEncounter = new("ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER"), Combat = new(), EventRanOutOfTime = false };
        var won = Pure(dummy, [Add(dummyPlan, fight)]).Nodes.Single();
        var timeout = Pure(dummy, [Add(dummyPlan, fight with { EventRanOutOfTime = true })]).Nodes.Single();
        Check(won.Advanced && timeout.Advanced && won.Cards.Sum(c => c.Value.UpgradeLevel) > timeout.Cards.Sum(c => c.Value.UpgradeLevel),
            "Dummy victory and timeout retain their distinct parent post-combat effects");
    }

    private static void InheritedDependencies(PredictorRun captured)
    {
        var run = Fixture(captured, "MORPHIC_GROVE", previousCombat: true);
        var before = Pure(run, [], 2);
        var eventNode = before.Nodes[1];
        Check(eventNode.PendingCauses.Any(c => c.Code == "EnemyCreationCount"), "unknown Fabricator Niche offset reaches the event boundary");
        var occurrence = eventNode.Event!.Occurrence;
        var candidate = occurrence?.Candidates.First(c => c.Event == E("MORPHIC_GROVE"));
        var plan = new PredictorPlannedNode(eventNode.Position.NodeId)
        {
            Event = new(E("MORPHIC_GROVE")) { Occurrence = candidate == null ? null : new(candidate.Event, candidate.QueueEvent,
                candidate.RawOrdinal, occurrence!.Cursor, candidate.Cursor),
                Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = new("GROUP") },
                    new(PredictorEventActionKind.Cards) { DeckCards = run.Working.Deck.Take(2).Select(id => Target(run.Working.Card(id))).ToImmutableArray() }] }
        };
        var closedCombat = new PredictorPlannedNode(run.Working.Position.NodeId) { LeaveRewards = true };
        var result = Pure(run, [closedCombat, plan], 3);
        Check(result.Nodes[1].Advanced && result.Nodes[1].Cards.Any(c => c.Value.FloorAdded == eventNode.Position.Floor && c.IsKnown) &&
            result.Nodes[1].Event!.GoldBalance == 0,
            "missing Niche does not mask known event-local Morphic transformations");
        var optional = Pure(run, [plan], 3).Nodes[1];
        Check(optional.Advanced && optional.Cards.Where(c => c.Value.FloorAdded == eventNode.Position.Floor)
            .All(c => !c.PendingCauses.Any(cause => cause.Code == "EnemyCreationCount")),
            "optional prior cards preserve known existing targets while unknown generated IDs remain explicitly pending");
    }

    private static void ExhaustedEntryRequirements(PredictorRun captured)
    {
        foreach (var (entry, expected) in new[] { ("THE_FUTURE_OF_POTIONS", "EventEntryTwoPotionsRequired"),
            ("STONE_OF_ALL_TIME", "EventEntryPotionRequired"), ("SLIPPERY_BRIDGE", "EventEntryRemovableCardRequired") })
        {
            var invalid = Fixture(captured, entry, configure: state => state with
            { PotionSlots = state.PotionSlots.Select(_ => (PredictorPotion?)null).ToImmutableArray(),
                Cards = state.Cards.Select(c => c with { Removable = false }).ToImmutableArray() });
            Check(View(invalid, Plan(invalid, entry)).PendingCauses.Any(c => c.Code == expected),
                "selected appearance preserves a known unavailable entry input: " + entry);
        }
        var run = Fixture(captured, "WELCOME_TO_WONGOS", previousCombat: true, visited: true,
            context: captured.Context with { AccountWongoPoints = null });
        var close = new PredictorPlannedNode(run.Working.Position.NodeId) { LeaveRewards = true };
        var before = Pure(run, [close], 2).Nodes[1];
        var occurrence = before.Event!.Occurrence!;
        Check(occurrence.Candidates.Single().IsCycleFallback, "exhausted cycle remains an explicit projection hypothesis");
        var candidate = occurrence.Candidates.Single();
        var plan = new PredictorPlannedNode(before.Position.NodeId)
        {
            Event = new(E("WELCOME_TO_WONGOS")) { Occurrence = new(candidate.Event, candidate.QueueEvent,
                candidate.RawOrdinal, occurrence.Cursor, candidate.Cursor) }
        };
        Check(Pure(run, [close, plan], 2).Nodes[1].PendingCauses.Any(c => c.Code == "EventAccountPointsRequired"),
            "exhausted occurrence cannot bypass the selected event's required account input");

        var repeat = Fixture(captured, "MORPHIC_GROVE", previousCombat: true, visited: true);
        var repeatClose = new PredictorPlannedNode(repeat.Working.Position.NodeId) { LeaveRewards = true };
        var repeatNode = Pure(repeat, [repeatClose], 2).Nodes[1];
        var repeated = repeatNode.Event!.Occurrence!;
        var repeatedCandidate = repeated.Candidates.Single();
        var repeatedPlan = new PredictorPlannedNode(repeatNode.Position.NodeId)
        {
            Event = new(E("MORPHIC_GROVE")) { Occurrence = new(repeatedCandidate.Event, repeatedCandidate.QueueEvent,
                repeatedCandidate.RawOrdinal, repeated.Cursor, repeatedCandidate.Cursor),
                Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = new("LONER") },
                    new(PredictorEventActionKind.Health) { Health = new(40, 80) }] }
        };
        Check(Pure(repeat, [repeatClose, repeatedPlan], 2).Nodes[1].Advanced,
            "accepted exhausted-cycle hypothesis replays at the same deferred occurrence boundary");
    }

    private static void FakeMerchantLiveDefault(PredictorRun captured)
    {
        var foul = new ModelKey("POTION", "FOUL_POTION");
        foreach (bool hasFoul in new[] { false, true })
        {
            var run = Fixture(captured, "FAKE_MERCHANT", configure: state => state with
            {
                PotionSlots = hasFoul ? [new(state.NextInstanceId, foul), null] : [null, null],
                NextInstanceId = state.NextInstanceId + (hasFoul ? 1 : 0)
            });
            var plan = Plan(run, "FAKE_MERCHANT");
            var defaultRoute = Pure(run, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var node = defaultRoute.Nodes[0];
            var view = node.Event!;
            Check(node.Advanced && view.Advanced && view.NeededInput == PredictorEventNeededInput.None &&
                view.AppliedActions == 0 && view.Combat == null &&
                !view.InputPolicy.BranchRequired && defaultRoute.Nodes.Length == 2 &&
                defaultRoute.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card).All(reward => reward.IsKnown),
                "Fake Merchant defaults to no shopping or combat and continues independent later rewards, including with a held Foul Potion");
            Check(view.GoldBalance == 1000 && view.Rewards.IsEmpty && node.Rewards.IsEmpty &&
                node.Cards.Select(card => card.Value).SequenceEqual(run.Working.Deck.Select(run.Working.Card)) &&
                node.Inventory!.Relics.IsEmpty && node.Inventory.PotionSlots.Select(slot => slot.Value).SequenceEqual(run.Working.PotionSlots),
                "the default merchant visit spends no gold, obtains no inventory and consumes no held Foul Potion");
            Check(view.EntryOptions.Single(option => option.Key == "OPEN_INVENTORY") is { Enabled: true, ResultPreview: not null } &&
                view.EntryOptions.Single(option => option.Key == "THROW_FOUL_POTION").Enabled == hasFoul,
                "the default visit retains explicit shopping and the actual availability of deliberate combat");
            var explicitLeave = Pure(run, [Option(run, plan, "LEAVE")], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
            foreach (string property in new[] { "rng.Rewards.calls", "rng.Niche.calls", "rng.Shops.calls" })
            {
                static PredictorKnowledgeFact After(PredictorRouteNodeKnowledge value, string key)
                {
                    var diagnostics = value.Diagnostics!;
                    return diagnostics.After.Select(diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == key);
                }
                var actual = After(node, property);
                var expected = After(explicitLeave, property);
                Check(actual.Status == PredictorKnowledgeStatus.Known && actual.Status == expected.Status &&
                    actual.DisplayValue == expected.DisplayValue,
                    "the default visit adds no public RNG consumption beyond the same event entry and explicit leave: " + property);
            }
            var emptyPlan = Pure(run, [plan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
            Check(emptyPlan is { Advanced: true, NeededInput: PredictorEventNeededInput.None, AppliedActions: 0 } && plan.Event!.Actions.IsEmpty &&
                View(run, plan).NeededInput == PredictorEventNeededInput.Option && !View(run, plan).Advanced,
                "an empty route plan uses only the live default; authored prediction still waits for an explicit merchant action");

            var openedPlan = Option(run, plan, "OPEN_INVENTORY");
            var opened = Pure(run, [openedPlan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
            Check(opened is { Advanced: false, NeededInput: PredictorEventNeededInput.Option, AppliedActions: 1 } &&
                opened.Options.Any(option => option.Key.StartsWith("BUY_", StringComparison.Ordinal)),
                "explicit OPEN_INVENTORY keeps the deliberate shopping sequence open instead of applying the no-action default");
            var buy = opened.Options.First(option => option.Enabled == true && option.Value?.OfferedRelic?.Entry != "FAKE_LEES_WAFFLE" &&
                option.Key.StartsWith("BUY_", StringComparison.Ordinal)).Value!;
            var purchased = Pure(run, [Add(openedPlan, new(PredictorEventActionKind.Option) { ExpectedOption = buy })],
                policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            Check(purchased.Event is { AppliedActions: 2 } && purchased.Event.GoldBalance == 1000 - buy.GoldCost &&
                purchased.Inventory!.Relics.Any(relic => relic.IsKnown && relic.Value.Key == buy.OfferedRelic),
                "explicit shopping still pays the actual price and obtains the selected stock");
            var changed = Pure(run, [Add(openedPlan, new(PredictorEventActionKind.Option)
                { ExpectedOption = buy with { GoldCost = buy.GoldCost + 1 } })],
                policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single().Event!;
            Check(!changed.Advanced && changed.AppliedActions == 1 && changed.GoldBalance == 1000 &&
                changed.PendingCauses.Any(cause => cause.Code == "PlannedEventOptionChanged"),
                "a stale deliberate purchase remains pending instead of becoming a no-shopping default");
            if (hasFoul)
            {
                var fight = Add(plan, new(PredictorEventActionKind.Option)
                    { ExpectedOption = view.EntryOptions.Single(option => option.Key == "THROW_FOUL_POTION").Value });
                var combat = Pure(run, [fight], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
                Check(combat.Event is { Advanced: true, NeededInput: PredictorEventNeededInput.None, AppliedActions: 1 } &&
                    combat.Inventory!.PotionSlots.All(slot => slot.IsKnown && slot.Value == null) &&
                    combat.Event.Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card),
                    "drawing explicitly throwing Foul Potion consumes its real bottle and continues to independent combat rewards with one business choice");
                var authoredCombat = Pure(run, [fight], policy: PredictorRoutePreviewPolicy.AuthoredChoices).Nodes.Single();
                Check(authoredCombat.Event is { NeededInput: PredictorEventNeededInput.Combat, AppliedActions: 1, Combat: not null } &&
                    authoredCombat.Inventory!.PotionSlots.All(slot => slot.Value == null),
                    "Authored throwing Foul Potion still consumes its real bottle and requires actual combat facts");
            }
        }
        var missing = PredictorRouteCauseEvidence.CreateExplicit("FakeMerchantDefaultPotionsUnobserved", "capture:fake-default-potions",
            PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
            "No actual route potion inventory was supplied.", ["potions.orderedSlots"]);
        var unknown = Fixture(captured, "FAKE_MERCHANT", context: captured.Context with { Live = new(1, true, [missing]) },
            configure: state => state with { PotionSlots = [null, null] });
        var unknownRoute = Pure(unknown, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(unknownRoute.Nodes[0].Event is { Advanced: true, NeededInput: PredictorEventNeededInput.None,
                AppliedActions: 0, Combat: null, GoldBalance: 1000, PotionInput: null } &&
            unknownRoute.Nodes[0].Inventory!.PotionPendingCauses.Any(cause => cause.Identity == missing.Identity) &&
            unknownRoute.Nodes.Length == 2 && unknownRoute.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                .All(reward => reward.IsKnown),
            "no-shopping default needs no invented local potion inventory and preserves its original observation gap without blocking later Rewards");
    }

    private static void FakeMerchantLocalFoulHypothesis(PredictorRun captured)
    {
        var foul = new ModelKey("POTION", "FOUL_POTION");
        var missing = PredictorRouteCauseEvidence.CreateExplicit("FakeMerchantRoutePotionsUnobserved", "capture:fake-potions",
            PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
            "The route does not project receipt of the earlier obtainable Foul Potion.", ["potions.orderedSlots"]);
        var local = Fixture(captured, "FAKE_MERCHANT", context: captured.Context with { Live = new(1, true, [missing]) },
            configure: state => state with { PotionSlots = [null, null], GoldBudget = 0 });
        var plan = Plan(local, "FAKE_MERCHANT");
        plan = plan with { Event = plan.Event! with { Potions = new() { OrderedSlots = [foul, null] } } };
        var continuation = Pure(local, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var arrived = continuation.Nodes[0].Event!;
        var throwing = arrived.Options.Single(option => option.Key == "THROW_FOUL_POTION");
        Check(arrived.PotionInputRequirement == PredictorEventPotionRequirement.OrderedSlots &&
            arrived.SelectedOccurrence is { IsUnconditional: true } && arrived.Advanced &&
            arrived.NeededInput == PredictorEventNeededInput.None && arrived.AppliedActions == 0 &&
            continuation.Nodes.Length == 2 && arrived.GoldBalance == 0 &&
            arrived.Combat == null && throwing is { IsKnown: true, Enabled: true,
                Value: { PotionId: null, LocalPotionOrdinal: 0 } } && throwing.Value.LocalPotionKey == foul,
            "a prior obtainable Foul hypothesis proves local merchant appearance and exposes its explicit fight option without automatic combat");
        Check(continuation.Nodes[0].Inventory!.PotionPendingCauses.Any(cause => cause.Identity == missing.Identity) &&
            continuation.Nodes[0].Inventory!.Relics.IsEmpty && plan.Event!.Actions.IsEmpty,
            "default local merchant continuation preserves unknown global inventory and manufactures no actions or relic receipts");
        var selected = Add(plan, new(PredictorEventActionKind.Option) { ExpectedOption = throwing.Value });
        var combat = Pure(local, [selected], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(combat.Event is { Advanced: true, NeededInput: PredictorEventNeededInput.None, AppliedActions: 1 } &&
            combat.Inventory!.PotionSlots.All(slot => slot.Value == null) &&
            combat.Inventory.PotionPendingCauses.Any(cause => cause.Identity == missing.Identity),
            "drawing local Foul consumption continues through merchant combat without modifying or proving the global inventory");
        var authoredCombat = Pure(local, [selected], policy: PredictorRoutePreviewPolicy.AuthoredChoices).Nodes.Single();
        Check(authoredCombat.Event is { NeededInput: PredictorEventNeededInput.Combat, AppliedActions: 1 } &&
            authoredCombat.Inventory!.PotionSlots.All(slot => slot.Value == null) &&
            authoredCombat.Inventory.PotionPendingCauses.Any(cause => cause.Identity == missing.Identity),
            "Authored local Foul consumption requires combat facts and preserves the unknown global inventory");

        var native = Fixture(captured, "FAKE_MERCHANT", configure: state => state with
            { PotionSlots = [new(state.NextInstanceId, foul), null], NextInstanceId = state.NextInstanceId + 1 });
        Enter(native, Plan(native, "FAKE_MERCHANT"));
        long actualId = native.Working.PotionSlots[0]!.Id;
        Check(native.EventOptions.Single(option => option.Key == "THROW_FOUL_POTION").PotionId == actualId,
            "without local input the native merchant retains its positive PotionId option binding");
        Select(native, new UseOutOfCombatPotion(0, actualId));
        Check(native.Phase == PredictorPhase.CombatFacts && native.Working.PotionSlots[0] == null,
            "the native positive PotionId use entry still consumes the real detached slot and starts combat");
    }

    private static void LocalPotionHypotheses(PredictorRun captured)
    {
        var missing = PredictorRouteCauseEvidence.CreateExplicit("LocalPotionSlotsMissing", "capture:potions",
            PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
            "No route potion inventory was supplied.", ["potions.orderedSlots"]);
        var context = captured.Context with { Live = new(1, true, [missing]) };
        var energy = new ModelKey("POTION", "ENERGY_POTION");
        var fruit = new ModelKey("POTION", "FRUIT_JUICE");
        var input = new PredictorEventPotionInput() { OrderedSlots = [energy, fruit, null] };
        Check(JsonSerializer.Deserialize<PredictorEventPotionInput>(JsonSerializer.Serialize(new PredictorEventPotionInput(0)))
            is { EmptySlots: 0, OrderedSlots.IsDefault: true },
            "A Conveyor count-only local input round-trips without inventing an empty identity list.");
        foreach (string entry in new[] { "THE_FUTURE_OF_POTIONS", "STONE_OF_ALL_TIME", "RANWID_THE_ELDER" })
        {
            PredictorState Inventory(PredictorState state, bool concrete)
            {
                if (entry == "RANWID_THE_ELDER") state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "VAJRA"));
                return concrete ? state with
                    { PotionSlots = [new(state.NextInstanceId, energy), new(state.NextInstanceId + 1, fruit), null], NextInstanceId = state.NextInstanceId + 2 }
                    : state with { PotionSlots = [null, null, null] };
            }
            var run = Fixture(captured, entry, context: context, configure: state => Inventory(state, false));
            var plan = Plan(run, entry);
            var blocked = View(run, plan);
            Check(blocked.PotionInputRequirement == PredictorEventPotionRequirement.OrderedSlots,
                "A potion-sensitive event exposes its local identity input before an entry prerequisite can block: " + entry);
            plan = plan with { Event = plan.Event! with { Potions = input } };
            var view = Pure(run, [plan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            var options = view.Event!.EntryOptions.IsEmpty ? view.Event.Options : view.Event.EntryOptions;
            Check(options.Any(option => option.Value?.LocalPotionKey != null) && options.Where(option =>
                option.Value?.LocalPotionKey != null).All(option => option.IsKnown && option.Value!.PotionId == null),
                "An ordered local hypothesis binds source potion selection without manufacturing persistent IDs: " + entry);
            Check(view.Inventory!.PotionSlots.IsEmpty && view.Inventory.PotionPendingCauses.Any(cause => cause.Identity == missing.Identity),
                "Local event identities cannot turn route potion inventory into a proven observation: " + entry);
            var facts = view.Diagnostics!.Snapshot.Facts;
            Check(facts.Any(fact => fact.Origin == PredictorKnowledgeOrigin.PlayerInput &&
                fact.PropertyKey.Contains("potions.local", StringComparison.Ordinal) && fact.Status == PredictorKnowledgeStatus.Known),
                "Event-local potion hypotheses retain PlayerInput provenance: " + entry);
            var concrete = Fixture(captured, entry, configure: state => Inventory(state, true));
            var source = View(concrete, Plan(concrete, entry));
            var localPayloads = options.Where(option => option.Value?.LocalPotionKey != null)
                .Select(option => (option.Value!.LocalPotionKey, option.Value.CardType)).ToArray();
            var sourcePayloads = source.Options.Where(option => option.Value?.PotionId != null)
                .Select(option => ((ModelKey?)concrete.Working.PotionSlots.Single(potion => potion?.Id == option.Value!.PotionId)!.Key,
                    option.Value!.CardType)).ToArray();
            Check(localPayloads.SequenceEqual(sourcePayloads),
                "Local ordered identities must preserve the concrete event-local RNG selection and rarity/type payload: " + entry);
            if (entry == "THE_FUTURE_OF_POTIONS")
            {
                var unavailableInput = input with { OrderedSlots = [new("POTION", "SAVED_POTION_OUTSIDE_CURRENT_CATALOG"), fruit] };
                var unavailablePlan = plan with { Event = plan.Event! with { Potions = unavailableInput } };
                Check(View(run, unavailablePlan).PendingCauses.Any(cause => cause.Code == "EventLocalPotionRarityUnavailable") &&
                    View(run, unavailablePlan).PotionInput == unavailableInput,
                    "A saved local bottle outside the current rarity catalog stays authored and reports its specific gap without substitution.");
                Enter(concrete, Plan(concrete, entry));
                var activeUnavailable = Plan(concrete, entry) with { Event = new(E(entry)) { Potions = unavailableInput } };
                Check(View(concrete, activeUnavailable).PendingCauses.Any(cause => cause.Code == "EventLocalPotionRarityUnavailable"),
                    "Active event replay must preserve a missing-rarity hypothesis without throwing during option setup.");
                var selected = options.First(option => option.Value?.LocalPotionKey != null).Value!;
                var chosen = plan with { Event = plan.Event! with
                    { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = selected }] } };
                var result = Pure(run, [chosen], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
                Check(result.Advanced && result.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                    result.Inventory!.PotionPendingCauses.Any(cause => cause.Identity == missing.Identity),
                    "Future's local discard computes its source card reward and leaves global inventory uncertainty intact.");
            }
        }
        var conveyor = Fixture(captured, "ENDLESS_CONVEYOR", context: context, configure: state => state with { PotionSlots = [null, null] });
        var conveyorPlan = Plan(conveyor, "ENDLESS_CONVEYOR") with
        { Event = Plan(conveyor, "ENDLESS_CONVEYOR").Event! with
            { Potions = new(0), Actions = [new(PredictorEventActionKind.Health) { Health = new(40, 80) }] } };
        var conveyorView = Pure(conveyor, [conveyorPlan], policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(conveyorView.Event!.PotionInputRequirement == PredictorEventPotionRequirement.EmptySlots &&
            conveyorView.Event.Options.Any(option => option.Value != null) &&
            !conveyorView.PendingCauses.Any(cause => cause.Code == "PlannedEventHealthDependent"),
            "Conveyor's empty-slot fact permits its weighted dish calculation without requiring bottle identities.");
        var fullConveyor = Fixture(captured, "ENDLESS_CONVEYOR", configure: state => state with
        { PotionSlots = [new(state.NextInstanceId, energy), new(state.NextInstanceId + 1, fruit)], NextInstanceId = state.NextInstanceId + 2 });
        var fullPlan = Add(Plan(fullConveyor, "ENDLESS_CONVEYOR"), new(PredictorEventActionKind.Health) { Health = new(40, 80) });
        Check(conveyorView.Event.Options.Select(option => option.Key).SequenceEqual(View(fullConveyor, fullPlan).Options.Select(option => option.Key)),
            "Conveyor's local zero-empty fact preserves the exact full-slot weighted-pool outcome.");
        // Conveyor has no damaging dish. Its local slot-count scope must still
        // expire before the next event's actual lethal/death-protection read.
        var lethalNext = Fixture(captured, "ENDLESS_CONVEYOR", context: context, configure: state =>
            PredictorSettlementEffects.AddRelic(state, new("RELIC", "JUZU_BRACELET")) with
        {
            PotionSlots = [null, null],
            Maps = state.Maps.Select(map => map with { Map = map.Map with { Points = map.Map.Points.Select(point =>
                point.Position.Row == 2 ? point with { Type = MapPointType.Unknown } : point).ToArray() } }).ToImmutableArray(),
            Acts = state.Acts.Select(act => act.Act == 2 ? act with
                { RawEvents = [E("ENDLESS_CONVEYOR"), E("JUNGLE_MAZE_ADVENTURE")] } : act).ToImmutableArray()
        });
        var firstPlan = Plan(lethalNext, "ENDLESS_CONVEYOR");
        firstPlan = firstPlan with { Event = firstPlan.Event! with
            { Potions = new(0), Actions = [new(PredictorEventActionKind.Health) { Health = new(1, 80) }] } };
        firstPlan = Option(lethalNext, firstPlan, "OBSERVE_CHEF");
        var next = Pure(lethalNext, [firstPlan], 2).Nodes[1];
        var occurrence = next.Event!.Occurrence!;
        var candidate = occurrence.Candidates.Single(candidate => candidate.Event == E("JUNGLE_MAZE_ADVENTURE"));
        var secondPlan = new PredictorPlannedNode(next.Position.NodeId)
        { Event = new(E("JUNGLE_MAZE_ADVENTURE")) { Occurrence = new(candidate.Event, candidate.QueueEvent,
            candidate.RawOrdinal, occurrence.Cursor, candidate.Cursor) } };
        var secondView = Pure(lethalNext, [firstPlan, secondPlan], 2).Nodes[1].Event!;
        secondPlan = secondPlan with { Event = secondPlan.Event! with { Actions =
            [new(PredictorEventActionKind.Option) { ExpectedOption = secondView.Options.Single(option => option.Key == "SOLO_QUEST").Value! },
             new(PredictorEventActionKind.Health) { Health = new(1, 80) }] } };
        var lethal = Pure(lethalNext, [firstPlan, secondPlan], 2).Nodes[1];
        Check(!lethal.Advanced && lethal.PendingCauses.Any(cause => cause.Code == "PlannedEventHealthDependent" &&
            cause.Parents.Any(parent => parent.Identity == missing.Identity)),
            "Conveyor's count-only input cannot prove Fairy absence for a later HP=1, damage=18 event.");
    }
}
