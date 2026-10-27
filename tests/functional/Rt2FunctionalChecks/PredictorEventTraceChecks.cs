using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorEventTraceChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("EVENT_TRACE " + message); }
    internal static void Run()
    {
        MorphicAssignmentAndLocalTransform();
        ConveyorHealthAndIndependentBranches();
        ConveyorLostHpAndLocalInputs();
        IndependentLocalSeedIgnoresPublicNiche();
        Console.WriteLine("PREDICTOR_EVENT_TRACE_PASS real selector/static inputs; Morphic invariant/targets/local RNG; Conveyor weighted health/potion guards; isolated hypothetical branches");
    }

    // Invoked by the existing native route-events runner. These cases need
    // native Quest/Curse definitions and actual tradable relic metadata; the
    // small managed RNG fixture above does not stand in for those catalogs.
    internal static void RunNative(PredictorRun captured)
    {
        UnselectedByrdonisAndRecomputation(captured);
        UnselectedTreasuryCorrelation(captured);
        TraderEntryAndJuzuRooms(captured);
        ConsecutiveEventChoices(captured);
        TreasuryThenMorphicReset(captured);
        FixedDamageDeathProtection(captured);
        Console.WriteLine("PREDICTOR_EVENT_STATE_JOIN_PASS native catalog; unselected Byrdonis/Treasury; independent later rewards; authored recomputation; common Trader entry pulls and Juzu room membership");
    }

    private static PredictorRun NativeRoute(PredictorRun captured, string entry, bool nextUnknown = false,
        bool restAfterCombat = false, Func<PredictorState, PredictorState>? configure = null)
    {
        const int act = 2;
        var types = restAfterCombat ? new[] { MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.RestSite, MapPointType.Boss } :
            new[] { MapPointType.Monster, MapPointType.Unknown, nextUnknown ? MapPointType.Unknown : MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], GoldBudget = 1000,
            Position = new(act, 1, "event-state-join:entry"),
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Monster, PredictorRoomKind.Monster, null, 1)],
            VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
            Acts = captured.Working.Acts.Select(a => a.Act == act ? a with
            {
                RawEvents = [new("EVENT", entry)], EventCursor = 0,
                NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK"), new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0
            } : a).ToImmutableArray()
        };
        state = configure?.Invoke(state) ?? state;
        var run = new PredictorRun(captured.Context, state);
        run.SetRoute(act, points.Select(point => point.Position).ToImmutableArray());
        return run;
    }

    private static PredictorRouteContinuation NativePreview(PredictorRun run, int count,
        params PredictorPlannedNode[] plans)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var phase = run.Phase; var request = run.Request;
        var result = run.PreviewRouteContinuation(plans.ToImmutableArray(), count,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            phase == run.Phase && request == run.Request, "Native joined event preview mutated caller state or request.");
        return result;
    }

    private static void KnownLaterCards(PredictorRouteContinuation view, string label)
    {
        Check(view.Nodes.Length >= 2 && view.Nodes[1].Room == PredictorRoomKind.Monster,
            label + " did not reach the subsequent ordinary combat.");
        var rewards = view.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card).ToArray();
        Check(rewards.Length > 0 && rewards.All(reward => reward.IsKnown && reward.Value != null && reward.DefaultSkipped),
            label + " contaminated independent Rewards or omitted the approved CardReward Skip.");
    }

    private static void UnselectedByrdonisAndRecomputation(PredictorRun captured)
    {
        var run = NativeRoute(captured, "BYRDONIS_NEST", restAfterCombat: true);
        int initialCount = run.Working.Deck.Length;
        int initialSkills = run.Working.Deck.Count(id => run.Working.Card(id).Type == EffectCardType.Skill);
        var unselected = NativePreview(run, 3);
        Check(unselected.Nodes.Length == 3 && unselected.Nodes[0].Advanced &&
            unselected.Nodes[0].Event!.NeededInput == PredictorEventNeededInput.Option,
            "Unselected Byrdonis was globally stopped or falsely reported as a chosen option.");
        KnownLaterCards(unselected, "Unselected Byrdonis");
        Check(Boundary(unselected.Nodes[0], true, "deck.All.count").Status == PredictorKnowledgeStatus.Partial &&
            Boundary(unselected.Nodes[0], true, "deck.Skills.count") is { Status: PredictorKnowledgeStatus.Known } skills &&
            skills.DisplayValue == initialSkills.ToString() && !unselected.Nodes[2].RestOfferPendingCauses.IsEmpty,
            "Optional Quest Egg changed initial Skill count or its uncertain membership was lost before Hatch.");
        var initial = unselected.Nodes[0];
        var takePreview = initial.Event!.EntryOptions.Single(option => option.Key == "TAKE").ResultPreview!;
        Check(takePreview.GeneratedCards.Any(card => card.IsKnown && card.Value.Key.Entry == "BYRDONIS_EGG"),
            "A fresh Egg model was blocked by unrelated instance identity uncertainty.");
        var eat = NativePreview(run, 2, Plan(initial, Option(initial.Event, "EAT")));
        var take = NativePreview(run, 2, Plan(initial, Option(initial.Event, "TAKE")));
        var eatAgain = NativePreview(run, 2, Plan(initial, Option(initial.Event, "EAT")));
        KnownLaterCards(eat, "Authored Byrdonis EAT"); KnownLaterCards(take, "Authored Byrdonis TAKE");
        Check(Boundary(eat.Nodes[0], true, "deck.All.count").DisplayValue == initialCount.ToString() &&
            Boundary(take.Nodes[0], true, "deck.All.count").DisplayValue == (initialCount + 1).ToString() &&
            eat.Nodes[0].Cards.All(card => card.Value.Key.Entry != "BYRDONIS_EGG") &&
            take.Nodes[0].Cards.Count(card => card.IsKnown && card.Value.Key.Entry == "BYRDONIS_EGG") == 1 &&
            Boundary(eatAgain.Nodes[0], true, "deck.All.count").DisplayValue == initialCount.ToString(),
            "EAT/TAKE replacement reused a prior joined state or left an abandoned Egg branch behind.");
        Check(eat.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card).Select(reward => reward.Value!.Cards)
            .Zip(take.Nodes[1].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card).Select(reward => reward.Value!.Cards))
            .All(pair => pair.First.SequenceEqual(pair.Second)), "Egg receipt changed independent later card generation.");
        var identityContext = captured.Context with
        {
            Live = new(1, true, [PredictorRouteCauseEvidence.CreateExplicit("CapturedNextInstanceId",
                "capture:event-next-id", PredictorCombatDomain.InstanceIds, PredictorPendingReasonKind.MissingObservation,
                "The next allocation identity was not observed; fixed card model metadata is available.", ["instanceIds.next"])])
        };
        var unknownIdentityRun = NativeRoute(new PredictorRun(identityContext, captured.Working), "BYRDONIS_NEST");
        var unknownIdentityEntry = NativePreview(unknownIdentityRun, 1).Nodes[0];
        var knownEgg = unknownIdentityEntry.Event!.EntryOptions.Single(option => option.Key == "TAKE").ResultPreview!
            .GeneratedCards.Single(card => card.Value.Key.Entry == "BYRDONIS_EGG");
        Check(knownEgg.IsKnown && knownEgg.Value.Id == 0,
            "Unknown fresh instance numbering hid a fixed Egg model despite the absence of BingBong/Hoarder skip listeners.");
    }

    private static void UnselectedTreasuryCorrelation(PredictorRun captured)
    {
        var run = NativeRoute(captured, "SUNKEN_TREASURY");
        int initialCount = run.Working.Deck.Length;
        var unselected = NativePreview(run, 2);
        KnownLaterCards(unselected, "Unselected Sunken Treasury");
        var first = unselected.Nodes[0];
        Check(first.Advanced && first.Event!.NeededInput == PredictorEventNeededInput.Option && first.Event.GoldBalance == null,
            "Treasury exposed one branch's Gold as a selected result or stopped independent continuation.");
        var gold = Boundary(first, true, "gold.balance"); var deck = Boundary(first, true, "deck.All.count");
        var snapshot = first.Diagnostics!.Snapshot;
        var goldChoices = snapshot.Explain(gold.Id).Roots.Where(root => root.Kind == PredictorKnowledgeRootKind.UserChoice)
            .Select(root => root.ChoiceId).ToArray();
        var deckChoices = snapshot.Explain(deck.Id).Roots.Where(root => root.Kind == PredictorKnowledgeRootKind.UserChoice)
            .Select(root => root.ChoiceId).ToArray();
        Check(gold.Status != PredictorKnowledgeStatus.Known && deck.Status == PredictorKnowledgeStatus.Partial &&
            goldChoices.Length == 1 && goldChoices.SequenceEqual(deckChoices),
            "Treasury Gold and optional Greed lost their shared choice identity or leaked a known witness value.");
        var small = NativePreview(run, 2, Plan(first, Option(first.Event!, "FIRST_CHEST")));
        var large = NativePreview(run, 2, Plan(first, Option(first.Event!, "SECOND_CHEST")));
        var smallGold = first.Event!.Options.Single(option => option.Key == "FIRST_CHEST").Value!.GoldGain!.Value;
        var largeGold = first.Event.Options.Single(option => option.Key == "SECOND_CHEST").Value!.GoldGain!.Value;
        Check(Boundary(small.Nodes[0], true, "gold.balance").DisplayValue == (1000 + smallGold).ToString() &&
            Boundary(large.Nodes[0], true, "gold.balance").DisplayValue == (1000 + largeGold).ToString() &&
            Boundary(small.Nodes[0], true, "deck.All.count").DisplayValue == initialCount.ToString() &&
            large.Nodes[0].Cards.Count(card => card.IsKnown && card.Value.Key.Entry == "GREED") == 1,
            "Treasury authored outcomes split Gold from their own Greed disposition.");
        var unknownGoldContext = captured.Context with
        {
            Live = new(1, true, [PredictorRouteCauseEvidence.CreateExplicit("CapturedTreasuryGold",
                "capture:treasury-gold", PredictorCombatDomain.Gold, PredictorPendingReasonKind.MissingObservation,
                "Incoming Gold was not observed; Treasury's source gains remain known.", ["gold.balance"])])
        };
        var unknownGold = NativePreview(NativeRoute(new PredictorRun(unknownGoldContext, captured.Working), "SUNKEN_TREASURY"), 2);
        KnownLaterCards(unknownGold, "Treasury with unobserved incoming Gold");
        var conditionalGold = unknownGold.Nodes[0].Diagnostics!.Snapshot.Facts.Where(fact =>
                fact.PropertyKey.StartsWith("event.option.", StringComparison.Ordinal) &&
                fact.PropertyKey.EndsWith(".conditional.gold.balance", StringComparison.Ordinal)).ToArray();
        Check(conditionalGold.Length == 2 && conditionalGold.All(fact =>
                fact.Status != PredictorKnowledgeStatus.Known && fact.DisplayValue == null),
            "A conditional Treasury row published private absolute Gold as known despite an unresolved incoming Gold observation.");
    }

    private static PredictorRun NativeEventPair(PredictorRun captured, string first, string second) =>
        NativeRoute(captured, first, nextUnknown: true, configure: state =>
            PredictorSettlementEffects.AddRelic(state, new("RELIC", "JUZU_BRACELET")) with
            {
                Acts = state.Acts.Select(act => act.Act == 2 ? act with
                { RawEvents = [new("EVENT", first), new("EVENT", second)] } : act).ToImmutableArray()
            });

    private static void ConsecutiveEventChoices(PredictorRun captured)
    {
        var view = NativePreview(NativeEventPair(captured, "BUGSLAYER", "SUNKEN_TREASURY"), 2);
        Check(view.Nodes.Length == 2 && view.Nodes.All(node => node.Advanced) &&
            view.Nodes[0].Event!.Event!.Value.Entry == "BUGSLAYER" &&
            view.Nodes[1].Event!.Event!.Value.Entry == "SUNKEN_TREASURY",
            "A second unrelated event failed to join after the first event's unresolved card-model choice.");
        var first = view.Nodes[0]; var second = view.Nodes[1];
        var firstCandidates = Boundary(first, true, "deck.All.candidates");
        var secondCandidates = Boundary(second, true, "deck.All.candidates");
        var originalChoices = first.Diagnostics!.Snapshot.Explain(firstCandidates.Id).Roots
            .Where(root => root.Kind == PredictorKnowledgeRootKind.UserChoice).Select(root => root.ChoiceId).ToArray();
        var retainedChoices = second.Diagnostics!.Snapshot.Explain(secondCandidates.Id).Roots
            .Where(root => root.Kind == PredictorKnowledgeRootKind.UserChoice).Select(root => root.ChoiceId).ToArray();
        Check(firstCandidates.Status != PredictorKnowledgeStatus.Known &&
            secondCandidates.Status != PredictorKnowledgeStatus.Known && originalChoices.Length == 1 &&
            originalChoices.All(retainedChoices.Contains) && retainedChoices.Distinct().Count() == 2 &&
            !second.Cards.Any(card => card.IsKnown && card.Value.Key.Entry is "EXTERMINATE" or "SQUASH"),
            "Synchronizing the private Bugslayer witness promoted a representative card or discarded its original choice.");
    }

    private static void TreasuryThenMorphicReset(PredictorRun captured)
    {
        var run = NativeEventPair(captured, "SUNKEN_TREASURY", "MORPHIC_GROVE");
        var initial = NativePreview(run, 2);
        Check(initial.Nodes.Length == 2 && initial.Nodes[1].Event!.Event!.Value.Entry == "MORPHIC_GROVE",
            "Partial Treasury Gold prevented the proved >=100 Morphic admission.");
        var morphic = initial.Nodes[1];
        var targets = PredictorCardChanges.TransformCandidates(run.Context, run.Working)
            .Take(2).Select(id => new PredictorPlannedCard(id, run.Working.Card(id).Key,
                run.Working.Card(id).FloorAdded)).ToImmutableArray();
        Check(targets.Length == 2, "Native Morphic fixture lacks two actual original transform targets.");
        var completed = NativePreview(run, 2, Plan(morphic, Option(morphic.Event!, "GROUP"),
            new(PredictorEventActionKind.Cards) { DeckCards = targets }));
        var node = completed.Nodes[1]; var goldBefore = Boundary(node, false, "gold.balance");
        var goldAfter = Boundary(node, true, "gold.balance");
        var deckAfter = Boundary(node, true, "deck.All.candidates");
        var treasuryChoices = node.Diagnostics!.Snapshot.Explain(goldBefore.Id).Roots
            .Where(root => root.Kind == PredictorKnowledgeRootKind.UserChoice).Select(root => root.ChoiceId).ToArray();
        var remainingDeckChoices = node.Diagnostics.Snapshot.Explain(deckAfter.Id).Roots
            .Where(root => root.Kind == PredictorKnowledgeRootKind.UserChoice).Select(root => root.ChoiceId).ToArray();
        Check(node.Advanced && goldBefore.Status == PredictorKnowledgeStatus.Partial &&
            goldAfter.Status == PredictorKnowledgeStatus.Known && goldAfter.DisplayValue == "0" &&
            treasuryChoices.Length == 1 && treasuryChoices.All(remainingDeckChoices.Contains) &&
            !node.Cards.Any(card => card.IsKnown && card.Value.Key.Entry == "GREED"),
            "Morphic zero assignment retained an earlier Gold choice delta or erased its independent Greed choice.");
        var actualTargets = MainFacts(node, ".transform.input.targets").Single();
        var pools = MainFacts(node, ".transform.input.orderedPools").Single();
        Check(actualTargets.Status == PredictorKnowledgeStatus.Known &&
            targets.All(target => actualTargets.DisplayValue!.Contains(target.Key.Serialized, StringComparison.Ordinal)) &&
            pools.Status == PredictorKnowledgeStatus.Known && pools.Origin == PredictorKnowledgeOrigin.Captured &&
            MainFacts(node, ".rng.eventLocal.calls").Any(fact => fact.DisplayValue == "2"),
            "Morphic reset bypassed actual native target definitions, ordered source pools or its two event-local draws.");
    }

    private static void FixedDamageDeathProtection(PredictorRun captured)
    {
        var run = NativeRoute(captured, "JUNGLE_MAZE_ADVENTURE", configure: state =>
            PredictorSettlementEffects.AddRelic(state, new("RELIC", "LIZARD_TAIL")));
        var node = NativePreview(run, 1).Nodes.Single();
        var inventory = Boundary(node, true, "relics.orderedInventory");
        var explanation = node.Diagnostics!.Snapshot.Explain(inventory.Id);
        var roots = explanation.Roots;
        Check(node.Advanced && node.Event!.NeededInput == PredictorEventNeededInput.Option &&
            inventory.Status != PredictorKnowledgeStatus.Known && inventory.DisplayValue == null &&
            roots.Any(root => root.Kind == PredictorKnowledgeRootKind.MissingObservation && root.NodeId == node.Position.NodeId) &&
            explanation.Facts.Any(fact => fact.PropertyKey == "health.event.outcome" && fact.Status != PredictorKnowledgeStatus.Known),
            "Joining fixed-damage Jungle options dropped the unknown health outcome that can consume Lizard Tail protection. " +
            $"advanced={node.Advanced}; needed={node.Event!.NeededInput}; inventory={inventory.Status}; " +
            string.Join(" | ", roots.Select(root => root.Kind + ":" + string.Join(",", root.RequiredInputs.Select(input => input.Key)))) +
            "; causes=" + string.Join(",", node.PendingCauses.Select(c => c.Code)));
    }

    private static PredictorState TraderInventory(PredictorState state, ImmutableArray<ModelKey> owned)
    {
        foreach (var relic in owned) state = PredictorSettlementEffects.AddRelic(state, relic);
        // Existing focused fixtures use a single actual ordered bucket to force
        // front pulls regardless of the source rarity roll/fallback sequence.
        var offers = new[] { "ANCHOR", "ART_OF_WAR", "BAG_OF_MARBLES", "BAG_OF_PREPARATION", "BLOOD_VIAL", "BRONZE_SCALES", "CENTENNIAL_PUZZLE", "ODDLY_SMOOTH_STONE", "ORICHALCUM", "THE_BOOT", "VAJRA" }
            .Select(entry => new ModelKey("RELIC", entry)).Where(relic => !owned.Contains(relic)).Take(4).ToImmutableArray();
        return state with
        {
            UnknownOdds = new(1, -1, 0, 0),
            PersonalBag = new(false, [], [new("Rare", offers)], []),
            SharedBag = new(false, [], [new("Rare", offers)], [])
        };
    }

    private static void TraderEntryAndJuzuRooms(PredictorRun captured)
    {
        var juzu = new ModelKey("RELIC", "JUZU_BRACELET");
        var neutral = new[] { "ANCHOR", "ART_OF_WAR", "BAG_OF_MARBLES", "BAG_OF_PREPARATION", "BLOOD_VIAL", "BRONZE_SCALES", "CENTENNIAL_PUZZLE", "ODDLY_SMOOTH_STONE", "ORICHALCUM", "THE_BOOT", "VAJRA" }
            .Select(entry => new ModelKey("RELIC", entry)).Where(captured.Context.Catalog!.TradableRelicTypes.Contains).ToArray();
        Check(captured.Context.Catalog.TradableRelicTypes.Contains(juzu) && neutral.Length >= 4,
            "Native Trader fixture lacks actual Juzu/tradable relic metadata.");
        ImmutableArray<ModelKey> owned = [];
        // Select inventory geometry with the real event handler. No fake shuffle
        // result or hand-bound trading target is supplied to the projection.
        for (int a = 0; a < neutral.Length - 3 && owned.IsEmpty; a++)
        for (int b = a + 1; b < neutral.Length - 2 && owned.IsEmpty; b++)
        for (int c = b + 1; c < neutral.Length - 1 && owned.IsEmpty; c++)
        for (int d = c + 1; d < neutral.Length && owned.IsEmpty; d++)
        {
            var candidate = new[] { juzu, neutral[a], neutral[b], neutral[c], neutral[d] }.ToImmutableArray();
            var probe = NativeRoute(captured, "RELIC_TRADER", nextUnknown: true,
                configure: state => TraderInventory(state, candidate));
            probe.BeginEvent(new(2, 2, "event-state-join:trader-probe"), new("EVENT", "RELIC_TRADER"));
            if (probe.EventOptions.Any(option => probe.Working.Relics.Single(relic => relic.Id == option.RelicId).Key == juzu)) owned = candidate;
        }
        Check(!owned.IsEmpty, "No bounded native Trader inventory exposed Juzu among its three actual trading targets.");
        var run = NativeRoute(captured, "RELIC_TRADER", nextUnknown: true, configure: state => TraderInventory(state, owned));
        int rewardsBefore = run.Working.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls;
        var expectedOffers = run.Working.PersonalBag.Buckets.Single().Entries.Take(3).Select(relic => relic.Entry).ToArray();
        var unselected = NativePreview(run, 2);
        var entry = unselected.Nodes[0];
        Check(!entry.Advanced && entry.Event is { BusinessChoiceRequired: true, NeededInput: PredictorEventNeededInput.Option, AppliedActions: 0 } &&
            entry.Event.Options.Length == 3 &&
            entry.Event.Options.Select(option => option.Value!.OfferedRelic!.Value.Entry)
                .SequenceEqual(expectedOffers) &&
            Boundary(entry, true, "rng.Rewards.calls") is { Status: PredictorKnowledgeStatus.Known } rewards &&
            rewards.DisplayValue == (rewardsBefore + 3).ToString(),
            "Trader retains its real business choice with the same common three Rewards draws and pre-offered identities; " +
            $"advanced={entry.Advanced}; business={entry.Event!.BusinessChoiceRequired}; needed={entry.Event.NeededInput}; " +
            "offers=" + string.Join(',', entry.Event.Options.Select(option => option.Value?.OfferedRelic?.Entry)) +
            "; rewards=" + Boundary(entry, true, "rng.Rewards.calls").DisplayValue);
        Check(Boundary(entry, true, "bags.personal").Status == PredictorKnowledgeStatus.Known &&
            Boundary(entry, true, "bags.shared").Status == PredictorKnowledgeStatus.Known,
            "Trader's common entry bag removals were contaminated by the later owned-relic choice.");
        Check(unselected.TerminalBoundary == null && unselected.Nodes.Length == 1 &&
            entry.Event.PendingCauses.Any(cause => cause.Code == "EventOptionChoice") &&
            Boundary(entry, true, "relics.orderedInventory").Status == PredictorKnowledgeStatus.Known,
            "Unselected Trader stops at its actual business choice before trying a next room or joining Juzu membership; " +
            $"nodes={unselected.Nodes.Length}; terminal={unselected.TerminalBoundary != null}; " +
            "pending=" + string.Join(',', entry.Event.PendingCauses.Select(cause => cause.Code)));
        var loseJuzu = entry.Event!.Options.Single(option => run.Working.Relics.Single(relic => relic.Id == option.Value!.RelicId).Key == juzu);
        var keepJuzu = entry.Event.Options.First(option => option.Key != loseJuzu.Key);
        var losing = NativePreview(run, 2, Plan(entry, Option(entry.Event, loseJuzu.Key)));
        var keeping = NativePreview(run, 2, Plan(entry, Option(entry.Event, keepJuzu.Key)));
        Check(losing.Nodes.Length == 2 && losing.Nodes[1].Room == PredictorRoomKind.Monster &&
            keeping.Nodes.Length == 2 && keeping.Nodes[1].Room != PredictorRoomKind.Monster,
            "Authored Juzu removal/retention did not change the actual subsequent unknown-room resolution.");
        foreach (var selected in new[] { losing, keeping })
            Check(Boundary(selected.Nodes[0], true, "rng.Rewards.calls").DisplayValue == (rewardsBefore + 3).ToString() &&
                Boundary(selected.Nodes[0], true, "bags.personal").DisplayValue == Boundary(entry, true, "bags.personal").DisplayValue,
                "Selecting a pre-offered Trader relic repeated its entry pulls or changed the common bag remainder.");
    }
    private static PredictorRun Route(string entry, bool unknownGold = false, int extraNiche = 0, bool unknownNiche = false,
        string seed = "SKDPGF6RC1PC")
    {
        var cards = Enumerable.Range(0, 3).Select(i => new PredictorCard(0, new("CARD", "EVENT_CONTROLLED_" + i),
            EffectCardType.Attack, 0, 1, true, 1)).ToImmutableArray();
        var pool = new ModelKey("CARD_POOL", "EVENT_CONTROLLED");
        // Conveyor option observation executes the actual Fried Eel branch.
        // Give that non-combat colorless draw its own valid controlled pool,
        // without adding those models to the initial deck/character pool.
        var colorless = cards.Select((card, i) => card with
            { Key = new("CARD", "CONVEYOR_COLORLESS_CONTROLLED_" + i) }).ToImmutableArray();
        var colorlessPool = new ModelKey("CARD_POOL", "CONVEYOR_COLORLESS_CONTROLLED");
        var context = new PredictorContext(seed, "0.111.0", new("CHARACTER", "IRONCLAD"), 0,
            "event-trace", "unlocks", [], [], [], 1)
        {
            Catalog = new(cards.Select((card, i) => new NeowEffectCardSnapshot("event-pool:" + i, card.Key, i,
                EffectCardRarity.Common, card.Type, false, false, false, true, true)).ToImmutableArray(),
                colorless.Select((card, i) => new NeowEffectCardSnapshot("conveyor-colorless:" + i, card.Key, i,
                    EffectCardRarity.Uncommon, card.Type, false, false, false, true, true)).ToImmutableArray())
            {
                CardDefinitions = cards.Select(card => new PredictorCardDefinition(card, "Common", pool, false, false, false)
                    { Levels = Enumerable.Range(0, 2).Select(level => new PredictorCardLevelMetadata(level, 1,
                        false, false, false, false, false, false)).ToImmutableArray() })
                    .Concat(colorless.Select(card => new PredictorCardDefinition(card, "Uncommon", colorlessPool, false, false, false)
                    { Levels = Enumerable.Range(0, 2).Select(level => new PredictorCardLevelMetadata(level, 1,
                        false, false, false, false, false, false)).ToImmutableArray() })).ToImmutableArray(),
                TransformationPools = [new(pool, cards.Select(card => card.Key).ToImmutableArray()),
                    new(colorlessPool, colorless.Select(card => card.Key).ToImmutableArray())]
            },
            PotionPool = Enumerable.Range(0, 2).SelectMany(i => new[]
            {
                new PredictorPotionOption(new("POTION", "ENERGY_POTION_" + i), PredictorPotionRarity.Common),
                new PredictorPotionOption(new("POTION", "BLOCK_POTION_" + i), PredictorPotionRarity.Uncommon),
                new PredictorPotionOption(new("POTION", "FRUIT_JUICE_" + i), PredictorPotionRarity.Rare)
            }).ToImmutableArray()
        };
        if (unknownGold) context = context with { Live = new(1, true,
            [PredictorRouteCauseEvidence.CreateExplicit("CapturedGold", "capture:event-gold", PredictorCombatDomain.Gold,
                PredictorPendingReasonKind.MissingObservation, "Gold was not observed.", ["gold.balance"])]) };
        if (unknownNiche) context = context with { Live = new(1, true,
            [PredictorRouteCauseEvidence.CreateExplicit("CapturedNiche", "capture:event-niche", PredictorCombatDomain.NicheRng,
                PredictorPendingReasonKind.MissingObservation, "Niche was not observed.", ["rng.Niche.state", "rng.Niche.calls"])]) };
        var points = new[]
        {
            new MapPredictionPoint(new(3, 0), MapPointType.Monster, true, [new(3, 1)]),
            new MapPredictionPoint(new(3, 1), MapPointType.Unknown, true, [new(3, 2)]),
            new MapPredictionPoint(new(3, 2), MapPointType.Boss, true, [])
        };
        var act = new ModelKey("ACT", "UNDERDOCKS");
        var state = PredictorSeedState.Create(context, cards, [], 1000, 2, new(-.05f, 0f)) with
        {
            OpeningCompleted = true, Position = new(2, 1, "event-trace:entry"),
            Maps = [new(2, new(context.Seed, act, 0, 7, points, 0), [points[0].Position])],
            Visits = [new(2, points[0].Position, MapPointType.Monster, PredictorRoomKind.Monster, null, 1)],
            UnknownOdds = new(0, -1, 0, 0),
            Acts = [new(2, act, new("EVENT", "NEOW"), [], [], [], [new("EVENT", entry)])]
        };
        if (extraNiche > 0)
        {
            var niche = state.Streams.Single(cursor => cursor.Stream == PredictorStream.Niche).Restore();
            for (int i = 0; i < extraNiche; i++) niche.NextInt(1);
            state = state.WithRng(PredictorStream.Niche, niche);
        }
        var run = new PredictorRun(context, state); run.SetRoute(2, points.Select(point => point.Position).ToImmutableArray()); return run;
    }
    private static PredictorRouteNodeKnowledge Preview(PredictorRun run, params PredictorPlannedNode[] plans)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request; var phase = run.Phase;
        var node = run.PreviewRouteContinuation(plans.ToImmutableArray(), maximumNodes: 1,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            request == run.Request && phase == run.Phase, "Event tracing/option forks changed the caller state, RNG or request.");
        return node;
    }
    private static PredictorPlannedNode Plan(PredictorRouteNodeKnowledge node, params PredictorPlannedEventAction[] actions) =>
        new(node.Position.NodeId) { Event = new(node.Event!.Event!.Value) { Actions = actions.ToImmutableArray() } };
    private static PredictorPlannedEventAction Option(PredictorEventNodeKnowledge view, string key) =>
        new(PredictorEventActionKind.Option) { ExpectedOption = view.Options.Single(option => option.Key == key).Value! };
    private static PredictorKnowledgeFact Boundary(PredictorRouteNodeKnowledge node, bool after, string key) =>
        (after ? node.Diagnostics!.After : node.Diagnostics!.Before).Select(node.Diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == key);
    private static IEnumerable<PredictorKnowledgeFact> MainFacts(PredictorRouteNodeKnowledge node, string suffix) =>
        node.Diagnostics!.Snapshot.Facts.Where(fact => fact.PropertyKey.StartsWith("event.compute.route.", StringComparison.Ordinal) &&
            fact.PropertyKey.EndsWith(suffix, StringComparison.Ordinal));

    private static void MorphicAssignmentAndLocalTransform()
    {
        var run = Route("MORPHIC_GROVE", unknownGold: true);
        var admission = Preview(run);
        Check(!admission.Advanced && admission.PendingCauses.Any(cause => cause.Code == "EventGoldAppearancePending" &&
            PredictorRouteCauseEvidence.OriginalCauses([cause]).Any(source => source.Code == "CapturedGold")),
            "An unknown future Gold admission must retain its real source before entering Morphic Grove.");
        // The assignment contract concerns an already observed current event,
        // whose identity is established even when its old balance is absent.
        // Admit that actual occurrence through the concrete replay API; the
        // drawing must not invent admission from its private Gold witness.
        run.AdvanceRoute();
        var occurrence = run.ObserveEventOccurrence()!;
        var selected = occurrence.Candidates.First(candidate => candidate.Event.Entry == "MORPHIC_GROVE");
        Check(run.Submit(run.Request!, new ChooseEventOccurrence(selected.Event, selected.QueueEvent,
            selected.RawOrdinal, occurrence.Cursor, selected.Cursor)) == PredictorInputResult.Accepted &&
            run.Phase == PredictorPhase.EventChoice, "The current-event fixture did not establish its real Morphic occurrence.");
        var initial = Preview(run);
        var plan = Plan(initial, Option(initial.Event!, "GROUP"));
        var picker = Preview(run, plan);
        var snapshot = picker.Diagnostics!.Snapshot;
        var before = MainFacts(picker, ".morphic.input.goldBefore").Single();
        var write = MainFacts(picker, ".gold.balance").First(fact => snapshot.Operations.Single(operation => operation.Id == fact.OperationId).Name == "Morphic GROUP: assign zero gold");
        Check(before.Status == PredictorKnowledgeStatus.Unknown && before.DisplayValue == null &&
            write.Status == PredictorKnowledgeStatus.Known && write.DisplayValue == "0" &&
            write.AllReadFactIds.Contains(before.Id) && write.UnresolvedReadFactIds.IsEmpty,
            "Morphic zero assignment leaked an unknown old balance or inherited an irrelevant old-balance dependency.");
        Check(Boundary(picker, true, "gold.balance").AllReadFactIds.Contains(write.Id),
            "Main-route Gold After did not link the actual assignment write.");
        var chosen = picker.Event!.DeckCardCandidates.Where(card => card.IsKnown).Take(2).Select(card =>
            new PredictorPlannedCard(card.Value.Id, card.Value.Key, card.Value.FloorAdded)).ToImmutableArray();
        var completed = Preview(run, plan with { Event = plan.Event! with
        { Actions = plan.Event.Actions.Add(new(PredictorEventActionKind.Cards) { DeckCards = chosen }) } });
        Check(completed.Event!.Advanced && MainFacts(completed, ".transform.input.targets").Any() &&
            MainFacts(completed, ".transform.input.orderedPools").Any() &&
            MainFacts(completed, ".rng.eventLocal.calls").Any(fact => fact.DisplayValue == "2") &&
            Boundary(completed, false, "rng.Niche.calls").DisplayValue == Boundary(completed, true, "rng.Niche.calls").DisplayValue,
            "Morphic target/pool/local-RNG calculation was absent or was recorded as a public Niche advance.");
        Check(admission.Diagnostics!.Snapshot.Facts.Any(fact => fact.PropertyKey.EndsWith(".occurrence.rawQueue", StringComparison.Ordinal)) &&
            admission.Diagnostics.Snapshot.Facts.Any(fact => fact.PropertyKey.EndsWith(".eligibility.immutableMode", StringComparison.Ordinal)) &&
            MainFacts(picker, ".input.action").Any(fact => fact.DisplayValue == "option:GROUP" && fact.Origin == PredictorKnowledgeOrigin.PlayerInput),
            "Event computation omitted selector/static-mode reads or authored choice provenance.");
    }
    private static void ConveyorHealthAndIndependentBranches()
    {
        var run = Route("ENDLESS_CONVEYOR");
        var initial = Preview(run);
        var node = Preview(run, Plan(initial, new PredictorPlannedEventAction(PredictorEventActionKind.Health) { Health = new(60, 80) }));
        Check(MainFacts(node, ".conveyor.weightedPool").Any(fact => fact.DisplayValue!.Contains("CLAM_ROLL", StringComparison.Ordinal)) &&
            MainFacts(node, ".conveyor.input.health").Any(fact => fact.DisplayValue == "60/80") &&
            MainFacts(node, ".rng.eventLocal.calls").All(fact => fact.DisplayValue == "0" || fact.DisplayValue == "1"),
            "Conveyor did not record its actual health/potion-weighted pool, or an option fork advanced the parent local RNG.");
        Check(node.Event!.Options.Where(option => option.ResultPreview != null).Select(option => option.ResultPreview!.TraceIdentity).Distinct().Count() ==
            node.Event.Options.Count(option => option.ResultPreview != null), "Independent option branches shared a trace identity.");
        var hypothetical = node.Diagnostics!.Snapshot.Facts.Where(fact => fact.PropertyKey.StartsWith("event.compute.option.", StringComparison.Ordinal) &&
            fact.PropertyKey.EndsWith(".input.action", StringComparison.Ordinal));
        Check(hypothetical.Any() && hypothetical.All(fact => fact.Origin == PredictorKnowledgeOrigin.Derived) &&
            Boundary(node, false, "gold.balance").DisplayValue == Boundary(node, true, "gold.balance").DisplayValue &&
            Boundary(node, false, "rng.Niche.calls").DisplayValue == Boundary(node, true, "rng.Niche.calls").DisplayValue,
            "Hypothetical options claimed player input or altered the parent Gold/public Niche frame.");
    }
    private static void ConveyorLostHpAndLocalInputs()
    {
        var run = Route("ENDLESS_CONVEYOR");
        var initial = Preview(run);
        var input = new PredictorConveyorInput(2, 20);
        var action = new PredictorPlannedEventAction(PredictorEventActionKind.ConveyorInputs) { Conveyor = input };
        var plan = Plan(initial, action);
        var reduced = Preview(run, plan);
        var full = Preview(run, Plan(initial, new PredictorPlannedEventAction(PredictorEventActionKind.Health) { Health = new(60, 80) }));
        Check(reduced.Event is { HealthRequired: false, ConveyorLostHp: 20 } && reduced.Event.ConveyorInput == input &&
            reduced.Event.Options.Select(option => option.Value).SequenceEqual(full.Event!.Options.Select(option => option.Value)) &&
            MainFacts(reduced, ".conveyor.input.lostHp").Any(fact => fact.DisplayValue == "20") &&
            !MainFacts(reduced, ".conveyor.input.health").Any() && !MainFacts(reduced, ".input.health").Any(),
            "Reduced Conveyor inputs did not reproduce the full-HP pool or invented a current/max HP observation.");
        var noMissingHp = Preview(run, Plan(initial, action with { Conveyor = new(0, 0) }));
        Check(MainFacts(noMissingHp, ".conveyor.weightedPool").All(fact =>
            !fact.DisplayValue!.Contains("CLAM_ROLL", StringComparison.Ordinal) &&
            !fact.DisplayValue.Contains("SUSPICIOUS_CONDIMENT", StringComparison.Ordinal)),
            "Conveyor ignored a zero lost-HP/empty-slot premise at its actual pool read.");

        var chef = Preview(run, plan with { Event = plan.Event! with
            { Actions = [action, Option(reduced.Event!, "OBSERVE_CHEF")] } });
        var fullChef = Preview(run, Plan(initial, new(PredictorEventActionKind.Health) { Health = new(60, 80) },
            Option(full.Event!, "OBSERVE_CHEF")));
        Check(chef.Advanced && chef.Event!.ConveyorInput == input && chef.Event.ConveyorLostHp == 20 &&
            chef.Cards.Select(card => card.Value).SequenceEqual(fullChef.Cards.Select(card => card.Value)) &&
            Boundary(chef, false, "rng.Niche.calls").DisplayValue == Boundary(chef, true, "rng.Niche.calls").DisplayValue,
            "Conveyor local confirmation changed Chef's real target, public stream, or lost the completed-node arrival receipt.");

        run.SetPlannedNode(plan);
        var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run),
            run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
        var restoredAction = restored.PlannedNodes.Single().Event!.Actions.Single();
        var replay = Preview(restored, restored.PlannedNodes.ToArray());
        Check(restoredAction.Action == PredictorEventActionKind.ConveyorInputs && restoredAction.Conveyor == input &&
            restoredAction.Health == null && replay.Event!.ConveyorInput == input &&
            replay.Event.Options.Select(option => option.Value).SequenceEqual(reduced.Event!.Options.Select(option => option.Value)),
            "Save/restore discarded the exact local Conveyor plan or replayed a fabricated full-HP fact.");

        var occurrence = initial.Event!.Occurrence!;
        var selected = initial.Event.SelectedOccurrence!;
        var authoredPlan = plan with { Event = plan.Event! with { Occurrence = new(selected.Event, selected.QueueEvent,
            selected.RawOrdinal, occurrence.Cursor, selected.Cursor) } };
        var authored = run.PreviewRouteContinuation([authoredPlan], 1,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.AuthoredChoices).Nodes.Single();
        Check(!authored.Advanced && authored.Event is { HealthRequired: true, ConveyorInput: null },
            "A drawing-only local Conveyor premise bypassed Authored's complete HP contract.");

        // Find first dishes by the real event-local pool/stream, rather than
        // supplying a synthetic dish or editing its private RNG cursor.
        var exercised = new HashSet<string>();
        for (int seed = 1; seed <= 64 && exercised.Count < 2; seed++)
        {
            var sample = Route("ENDLESS_CONVEYOR", seed: seed.ToString("D12"));
            var entry = Preview(sample);
            var sampleAction = action with { Conveyor = new(2, 6) };
            var samplePlan = Plan(entry, sampleAction);
            var offered = Preview(sample, samplePlan);
            string? branch = offered.Event!.Options.Select(option => option.Key)
                .FirstOrDefault(key => key is "GRAB_CAVIAR" or "GRAB_CLAM_ROLL");
            if (branch == null || !exercised.Add(branch)) continue;
            foreach (int lost in new[] { 6, 20, int.MaxValue })
            {
                var fact = sampleAction with { Conveyor = new(2, lost) };
                var actual = Preview(sample, Plan(entry, fact, Option(offered.Event, branch)));
                int healing = branch == "GRAB_CAVIAR" ? 4 : Math.Min(10, lost);
                int gain = branch == "GRAB_CAVIAR" ? 4 : 0;
                int remaining = branch == "GRAB_CAVIAR" ? lost : lost - healing;
                Check(actual.Event!.ConveyorLostHp == remaining &&
                    actual.Event.HealthConsequences.Contains(new(healing, gain)),
                    $"{branch} did not preserve its native clamped heal/max-HP deltas for lost HP {lost}; " +
                    $"actualLost={actual.Event.ConveyorLostHp}; applied={actual.Event.AppliedActions}; " +
                    string.Join(",", actual.PendingCauses.Select(cause => cause.Code)));
                if (remaining == 0)
                    Check(!MainFacts(actual, ".conveyor.weightedPool").Last().DisplayValue!.Contains("CLAM_ROLL", StringComparison.Ordinal),
                        "A fully healed local Conveyor outcome kept Clam in the next actual pool.");
            }
        }
        Check(exercised.Count == 2, "The bounded real Conveyor seeds did not exercise both Caviar and Clam.");
    }

    private static void IndependentLocalSeedIgnoresPublicNiche()
    {
        var one = Preview(Route("WHISPERING_HOLLOW"));
        var two = Preview(Route("WHISPERING_HOLLOW", extraNiche: 9));
        var missing = Preview(Route("WHISPERING_HOLLOW", unknownNiche: true));
        Check(one.Event!.Options.Single(option => option.Key == "GOLD").Value!.GoldCost ==
            two.Event!.Options.Single(option => option.Key == "GOLD").Value!.GoldCost &&
            MainFacts(one, ".rng.eventLocal.initialState").Single().DisplayValue == MainFacts(two, ".rng.eventLocal.initialState").Single().DisplayValue &&
            MainFacts(one, ".rng.eventLocal.calls").All(fact => fact.DisplayValue == "1"),
            "Event-local setup was coupled to the unrelated public Niche cursor or sibling option draws.");
        Check(Boundary(missing, false, "rng.Niche.state").Status == PredictorKnowledgeStatus.Unknown &&
            missing.Event!.Options.Single(option => option.Key == "GOLD").IsKnown &&
            MainFacts(missing, ".rng.eventLocal.initialState").Single().Status == PredictorKnowledgeStatus.Known,
            "An unobserved public Niche stream incorrectly blocked a seed-derived independent event-local draw.");
        Check(MainFacts(one, ".input.action").Any(fact => fact.DisplayValue!.StartsWith("occurrence:", StringComparison.Ordinal) &&
            fact.Origin == PredictorKnowledgeOrigin.ApprovedDefault), "Live default first occurrence was mislabeled as a player choice.");
    }
}
