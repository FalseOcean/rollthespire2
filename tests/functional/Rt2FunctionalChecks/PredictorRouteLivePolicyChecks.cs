using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

// Current live-map policy is intentionally narrower than the reusable exact
// obtain simulator. These checks exercise actual receipts through full routes.
internal static class PredictorRouteLivePolicyChecks
{
    private const string Cutoff = "RouteRelicDeckRandomnessCutoff";
    private static int _checks;
    private static ModelKey R(string key) => new("RELIC", key);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("LIVE_ROUTE_POLICY " + message); }
    private static void Send(PredictorRun run, PredictorInput input) =>
        Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "fixture input rejected: " + input);

    internal static int Run(PredictorRun captured)
    {
        _checks = 0;
        foreach (string relic in new[] { "WHETSTONE", "WAR_PAINT" }) AcquisitionBoundary(captured, relic);
        FragrantEvent(captured);
        UnaffectedRelics(captured);
        PickupOrder(captured);
        Console.WriteLine($"RUNTIME_ROUTE_LIVE_POLICY_PASS checks={_checks}; actual W/WP receipts; independent rewards/bags; fresh facts; exact/operation scope; Fragrant event; preserved pickup tiers; no blanket RandomDeck cutoff");
        return 0;
    }

    private static PredictorRun Route(PredictorRun captured, string relic, bool eventNode = false,
        ImmutableArray<PredictorRelic> held = default, ImmutableArray<PredictorCard> deck = default, bool hoarder = false,
        bool nicheUnknown = false, string eventKey = "HUNGRY_FOR_MUSHROOMS", bool shopAfterEvent = false)
    {
        const int act = 2;
        var types = new[] { MapPointType.Ancient, eventNode ? MapPointType.Unknown : MapPointType.Treasure,
            shopAfterEvent ? MapPointType.Shop : MapPointType.Monster, MapPointType.Treasure, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var source = captured.Checkpoints[0].State;
        var bag = new PredictorRelicBag(false, [], [new("Rare", [R(relic), R("GORGET")])], []);
        var state = source with
        {
            OpeningCompleted = true, Position = new(act, 1, "live-policy:entry"), Relics = held.IsDefault ? [] : held, GoldBudget = 1000,
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
            VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0), SharedBag = bag,
            Acts = source.Acts.Select(a => a.Act == act ? a with
            {
                RawEvents = [new("EVENT", eventKey)], EventCursor = 0,
                NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0
            } : a).ToImmutableArray()
        };
        if (shopAfterEvent) state = state with { PersonalBag = state.PersonalBag with
            { Buckets = state.PersonalBag.Buckets.Where(bucket => bucket.Rarity != "Shop").Append(new("Shop", [R("GNARLED_HAMMER")])).ToImmutableArray() } };
        if (!deck.IsDefault) state = state with { Cards = deck, Deck = deck.Select(card => card.Id).ToImmutableArray() };
        state = state with { NextInstanceId = Math.Max(state.NextInstanceId,
            state.Cards.Select(card => card.Id).Concat(state.Relics.Select(item => item.Id)).DefaultIfEmpty(0).Max() + 1) };
        var context = captured.Context with { Live = null, Operation = null };
        if (hoarder) context = context with { Modifiers = [new(new("MODIFIER", "HOARDER"))] };
        if (nicheUnknown) context = context with { Live = new(2, true,
            [PredictorRouteCauseEvidence.CreateExplicit("ObtainTestNiche", "capture:obtain", PredictorCombatDomain.NicheRng,
                PredictorPendingReasonKind.MissingObservation, "The incoming Niche cursor is unobserved.", ["rng.Niche.state"])]) };
        var run = new PredictorRun(context, state);
        run.SetRoute(act, points.Select(point => point.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    internal static void RunDependencyScopeBoundaries(PredictorRun captured)
    {
        PredictorRun Scoped(MapPointType[] rooms, ImmutableArray<PredictorRelic> held = default,
            string? quest = null, string[]? gifts = null, bool unknownDeck = false, bool cloneAttack = false)
        {
            const int act = 2;
            var points = rooms.Prepend(MapPointType.Ancient).Append(MapPointType.Boss)
                .Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
                    row == rooms.Length + 1 ? [] : [new(3, row + 1)])).ToArray();
            var source = captured.Checkpoints[0].State;
            var state = source with
            {
                OpeningCompleted = true, Position = new(act, 1, "scoped-state:entry"),
                GoldBudget = 5000, Relics = held.IsDefault ? [] : held, PotionSlots = [null, null, null],
                Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
                Visits = [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
                VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
                SharedBag = new(false, [], [new("Rare", (gifts ?? []).Select(R).ToImmutableArray())], []),
                Acts = source.Acts.Select(a => a.Act == act ? a with
                { RawEvents = [new("EVENT", "HUNGRY_FOR_MUSHROOMS")], EventCursor = 0 } : a).ToImmutableArray()
            };
            state = state with { NextInstanceId = Math.Max(state.NextInstanceId,
                state.Relics.Select(item => item.Id).DefaultIfEmpty(0).Max() + 1) };
            if (quest != null) state = PredictorObtainSources.AddCard(captured.Context, state, new ModelKey("CARD", quest));
            if (cloneAttack)
            {
                var original = state.Deck.Select(state.Card).First(card => card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack);
                state = state.WithCard(original with { Enchantment = new("ENCHANTMENT", "CLONE"), EnchantmentAmount = 1 });
            }
            var context = captured.Context with { Live = null, Operation = null, NumberOfRuns = 2 };
            if (unknownDeck) context = context with { Live = new(2, true,
                [PredictorRouteCauseEvidence.CreateExplicit("QuestStateMissing", "capture:quest", PredictorCombatDomain.Deck,
                    PredictorPendingReasonKind.MissingObservation, "The quest membership/counter was not captured.", ["deck.quest.state"])]) };
            var run = new PredictorRun(context, state);
            run.SetRoute(act, points.Select(point => point.Position).ToImmutableArray());
            run.AdvanceRoute();
            return run;
        }
        var dowsing = Scoped([MapPointType.RestSite, MapPointType.Unknown], quest: "DOWSING");
        var smith = Preview(dowsing, [], count: 2);
        Check(smith.Nodes.Length == 2 && smith.Nodes[0].RestDefaultApplied && smith.Nodes[0].Advanced &&
            !smith.PendingCauses.Any(c => c.Code == "UnknownRoomDowsingPending"),
            "Default Smith excludes the unupgradeable Dowsing payload and counter from its scope");
        var dowsingCard = smith.Nodes[0].Cards.Single(card => card.Value.Key.Entry == "DOWSING");
        Check(dowsingCard.IsKnown && dowsingCard.Value.MaxUpgradeLevel == 0,
            "Default Smith keeps an unupgradeable quest card's complete payload known");
        var authored = Preview(dowsing, [new(dowsing.Working.Position.NodeId)
            { RestActions = [new(PredictorRestAction.Smith)] }], PredictorRoutePreviewPolicy.AuthoredChoices, 2);
        Check(!authored.Nodes[0].Advanced, "Authored Smith still requires its real target");
        var missingQuest = Preview(Scoped([MapPointType.RestSite, MapPointType.Unknown], quest: "DOWSING", unknownDeck: true),
            [], count: 2);
        Check(missingQuest.Nodes.Length == 1 && missingQuest.PendingCauses.Any(c => c.Code == "UnknownRoomDowsingPending"),
            "A genuinely unobserved quest membership/counter still prevents a speculative question-mark entry");

        var tent = Scoped([MapPointType.RestSite, MapPointType.Unknown, MapPointType.Shop],
            [new(20000, R("MINIATURE_TENT"), 1), new(20001, R("GIRYA"), 1)]);
        var attack = tent.Working.Deck.Select(tent.Working.Card).First(card => card.UpgradeLevel < card.MaxUpgradeLevel);
        var restPlan = new PredictorPlannedNode(tent.Working.Position.NodeId)
        { RestActions = [new(PredictorRestAction.Smith) { SmithCard = new(attack.Id, attack.Key, attack.FloorAdded) },
            new(PredictorRestAction.Heal)] };
        var remaining = Preview(tent, [restPlan], count: 2);
        Check(remaining.Nodes.Length == 2 && remaining.Nodes[0].AppliedRestActions == 2 && remaining.Nodes[0].Advanced &&
            remaining.Nodes[0].PendingCauses.Any(c => c.Code == "RestChoice") &&
            !remaining.PendingCauses.Any(c => c.Code == "UnknownRoomRelicReadsPending"),
            "Tent's remaining Lift leaves Girya state unresolved without changing Juzu/Compass membership");

        foreach (string upgrade in new[] { "WHETSTONE", "WAR_PAINT" })
        {
            var tail = Scoped([MapPointType.Treasure, MapPointType.Treasure, MapPointType.Shop],
                [new(20000, R("LIZARD_TAIL"), 1)], gifts: ["SERE_TALON", upgrade]);
            var first = Treasure(tail, true);
            var afterHealth = Preview(tail, [first], count: 2);
            Check(afterHealth.Nodes.Length == 2 && afterHealth.Nodes[1].Treasure!.OfferedRelic == R(upgrade),
                "Tail's possible consumption does not change the next treasure's real bag selection");
            var second = new PredictorPlannedNode(afterHealth.Nodes[1].Position.NodeId)
            { Treasure = new() { Actions = [new(PredictorTreasureActionKind.Open),
                new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = R(upgrade) }] } };
            var received = Preview(tail, [first, second], count: 3);
            Check(received.Nodes.Length == 3 && received.Nodes[1].Treasure!.RelicTaken && received.Nodes[1].Advanced &&
                received.Nodes[2].Shop!.Offers.All(offer => offer.IsKnown && offer.Affordable != null),
                "Tail state cannot block an independent " + upgrade + " receipt or merchant stock/prices");
            Check(received.PendingCauses.Any(c => c.Code == "ObtainHealthOutcomePending") &&
                received.PendingCauses.Any(c => c.Code == Cutoff),
                "Independent delivery preserves both the real missing health/Tail state and upgrade/Niche uncertainty");
        }
        foreach (string gift in new[] { "WAR_PAINT", "WHETSTONE", "ELECTRIC_SHRYMP" })
        {
            var cloneRun = Scoped([MapPointType.Treasure, MapPointType.RestSite],
                [new(20000, R("PAELS_GROWTH"), 1)], gifts: [gift], cloneAttack: true);
            var original = cloneRun.Working.Deck.Select(cloneRun.Working.Card).Single(card => card.Enchantment?.Entry == "CLONE");
            var take = Treasure(cloneRun, true);
            var rest = Preview(cloneRun, [take], count: 2).Nodes[1];
            var clonePlan = new PredictorPlannedNode(rest.Position.NodeId)
            { RestActions = [new(PredictorRestAction.Clone)], LeaveRest = true };
            var result = Preview(cloneRun, [take, clonePlan], count: 2).Nodes[1];
            if (gift == "WAR_PAINT")
            {
                var copy = result.Cards.Where(card => !cloneRun.Working.Deck.Contains(card.Value.Id)).ToArray();
                Check(result.Advanced && result.AppliedRestActions == 1 && copy.Length == 1 && copy[0].IsKnown &&
                    (copy[0].Value with { Id = original.Id }) == original &&
                    result.Cards.Any(card => card.Value.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill && !card.IsKnown),
                    "Rest Clone copies the known Attack's full persistent payload without reading unrelated Skill upgrades");
            }
            else
            {
                Check(!result.Advanced && result.AppliedRestActions == 0 &&
                    result.PendingCauses.Any(cause => cause.Code == "RestDeckDependentAction") &&
                    result.Cards.Length == cloneRun.Working.Deck.Length,
                    gift == "WHETSTONE"
                        ? "Rest Clone still requires the original Attack's genuinely unknown full payload"
                        : "Rest Clone still requires unrelated Skills' enchantments to identify the complete original population");
                if (gift == "ELECTRIC_SHRYMP")
                    Check(result.Cards.Single(card => card.Value.Id == original.Id).IsKnown &&
                        result.Cards.Any(card => card.Value.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill && !card.IsKnown),
                        "Unknown sibling enchantments block Clone qualification even while its existing Attack original is fully known");
            }
        }
        Console.WriteLine("RUNTIME_DEPENDENCY_STATE_SCOPES_PASS default Smith quest; Authored target; Tent Lift; Tail W/WP receipts and merchant stock; Rest Clone scoped payload and population");
    }

    internal static void RunObtainOverblockBoundaries(PredictorRun captured)
    {
        PredictorRun Bones(string first, bool nicheUnknown = false, bool hooks = false) => Route(
            new PredictorRun(captured.Context with { OpeningEligibleRelics = [R(first), R("GOLDEN_PEARL"), R("NEOWS_BONES")] },
                captured.Checkpoints[0].State), "NEOWS_BONES", nicheUnknown: nicheUnknown,
            held: hooks ? [new(20000, R("LUCKY_FYSH"), 1), new(20001, R("BOOK_OF_FIVE_RINGS"), 1) { BookOfFiveRingsCardsAdded = 4 },
                new(20002, R("DARKSTONE_PERIAPT"), 1), new(20003, R("BING_BONG"), 1)] : [], hoarder: hooks);
        PredictorPlannedNode BonesPlan(PredictorRun run, string first, PredictorRoutePreviewPolicy policy)
        {
            var plan = Treasure(run, true);
            foreach (string key in new[] { first, "GOLDEN_PEARL" })
            {
                var node = Preview(run, [plan], policy, 1).Nodes[0];
                var reward = node.Treasure!.RequiredChoices.Single().Rewards.Single(item => item.IsKnown &&
                    item.Value is { Status: PredictorRewardStatus.Open } value && value.Key == R(key));
                plan = plan with { Treasure = plan.Treasure! with { Actions = plan.Treasure.Actions.Add(
                    new(PredictorTreasureActionKind.Obtain) { Obtain = new(PredictorObtainChildAction.Reward, R("NEOWS_BONES"))
                        { Reward = new(reward.Value!, reward.Occurrence) } }) } };
            }
            return plan;
        }
        var bones = Bones("NEW_LEAF");
        var bonesPlan = BonesPlan(bones, "NEW_LEAF", PredictorRoutePreviewPolicy.LiveMapDefaults);
        var result = Preview(bones, [bonesPlan], count: 2);
        var before = Preview(bones, [Treasure(bones, true)], count: 1).Nodes[0];
        Check(result.Nodes.Length == 2 && result.Nodes[0].Advanced &&
            result.Nodes[0].Treasure!.GoldBalance == before.Treasure!.GoldBalance + 150 &&
            result.Nodes[0].PendingCauses.Any(cause => cause.Code == "BonesCurseIdentityPending") &&
            !result.Nodes[0].Cards.Any(card => card.IsKnown && card.Value.Type.ToString() == "Curse" &&
                !bones.Working.Deck.Contains(card.Value.Id)) &&
            result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown),
            "New Leaf leaves the final Bones curse identity unknown while Golden Pearl and independent subsequent rewards complete");
        foreach (var policy in new[] { PredictorRoutePreviewPolicy.LiveMapDefaults, PredictorRoutePreviewPolicy.AuthoredChoices })
        {
            var known = Bones("ANCHOR");
            var knownPlan = BonesPlan(known, "ANCHOR", policy);
            var node = Preview(known, [knownPlan], policy, 1).Nodes[0];
            Check(node.Advanced && node.Cards.Count(card => card.IsKnown && card.Value.Type.ToString() == "Curse" &&
                    !known.Working.Deck.Contains(card.Value.Id)) == 1 &&
                !node.PendingCauses.Any(cause => cause.Code == "BonesCurseIdentityPending"),
                policy + " retains the concrete final curse when Niche is known; advanced=" + node.Advanced +
                ";newCurses=" + string.Join(',', node.Cards.Where(card => card.Value.Type.ToString() == "Curse" &&
                    !known.Working.Deck.Contains(card.Value.Id)).Select(card => card.Value.Key.Entry + ":" + card.IsKnown)) +
                ";causes=" + string.Join(',', node.PendingCauses.Select(cause => cause.Code)));
        }
        var hooked = Bones("ANCHOR", nicheUnknown: true, hooks: true);
        var hookedBefore = Preview(hooked, [Treasure(hooked, true)], count: 1).Nodes[0];
        var hookedNode = Preview(hooked, [BonesPlan(hooked, "ANCHOR", PredictorRoutePreviewPolicy.LiveMapDefaults)], count: 1).Nodes[0];
        Check(hookedNode.Advanced && hookedNode.Treasure!.GoldBalance == hookedBefore.Treasure!.GoldBalance + 150 + 60 &&
            hookedNode.Inventory!.Relics.Single(item => item.Value.Key == R("BOOK_OF_FIVE_RINGS")).Value.BookOfFiveRingsCardsAdded == 8,
            "unknown Bones curse settles four real Lucky/Book receipts from Bing Bong plus Hoarder without inventing IDs");
        Check(Fact(hookedNode, "deck.All.count").Status == PredictorKnowledgeStatus.Known &&
            Fact(hookedNode, "deck.All.count").DisplayValue == (hooked.Working.Deck.Length + 4).ToString() &&
            Fact(hookedNode, "deck.All.candidates").Status != PredictorKnowledgeStatus.Known &&
            !hookedNode.Cards.Any(item => item.IsKnown && item.Value.Type.ToString() == "Curse" &&
                !hooked.Working.Deck.Contains(item.Value.Id)) &&
            hookedNode.Diagnostics!.Results.Select(hookedNode.Diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == "bones.curse.maxHpDelta")
                is { Status: PredictorKnowledgeStatus.Known, DisplayValue: "24" },
            "known curse/copy quantity and Darkstone commands do not certify invented curse targets or identities");

        var hammer = Route(captured, "GORGET", eventNode: true, eventKey: "BUGSLAYER", shopAfterEvent: true);
        var baseline = Preview(hammer, [], count: 2);
        var shop = baseline.Nodes[1];
        var offer = shop.Shop!.Offers.Single(item => item.Value?.Key == R("GNARLED_HAMMER"));
        var buy = new PredictorPlannedNode(shop.Position.NodeId) { Shop = new()
            { Actions = [new(PredictorShopActionKind.Buy) { ExpectedOffer = offer.Value }] } };
        var purchased = Preview(hammer, [buy], count: 2).Nodes[1];
        var optional = purchased.OptionalObtainInputs.Single(item => item.Choice.Source == R("GNARLED_HAMMER"));
        var strike = optional.Choice.DeckCards.First(item => item.IsKnown &&
            PredictorCardChanges.Definition(captured.Context, item.Value.Key).IsStrike).Value;
        Check(purchased.Advanced && optional.Choice.DeckCards.Any(item => item.IsKnown) &&
            purchased.PendingCauses.Any(cause => cause.Code == "EventOptionChoice"),
            "Bugslayer's unresolved added attack does not hide fixed Hammer targets or erase the event choice");
        var selected = PredictorRun.ApplyOptionalObtainInput(buy, optional,
            new(PredictorObtainChildAction.ChooseDeckCards, R("GNARLED_HAMMER")) { DeckCards = [new(strike.Id, strike.Key, strike.FloorAdded)] });
        Check(Preview(hammer, [selected], count: 2).Nodes[1].Cards.Any(item => item.Value.Id == strike.Id &&
            item.Value.Enchantment?.Entry == "SHARP"), "the optional Hammer target replays its real Sharp enchantment");
        // An unresolved Smith target is a negative control for the individual
        // execution fields, rather than an unknown additional candidate.
        var projection = PredictorDeckKnowledge.Create(captured.Context, hammer.Working)
            .MarkExistingChangePending("prior-smith", "Upgrade target unknown", PredictorDeckUncertainFact.Upgrade,
                instanceIds: [strike.Id]);
        Check(projection.ReadTarget(strike.Id, PredictorDeckProjectionScope.EnchantableAttack,
            PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Enchantment) == null,
            "Hammer still rejects an individual target whose execution attributes are genuinely unresolved");
        Console.WriteLine("RUNTIME_OBTAIN_OVERBLOCK_BOUNDARIES_PASS Bones independent receipts/unknown curse; real hooks/copies; known Niche/authored curse; Hammer fixed target scope");
    }

    internal static void RunPandoraBoundaries(PredictorRun captured)
    {
        PredictorRelic Held(string key, int ordinal) => new(20000 + ordinal, R(key), 1);
        bool Basic(PredictorCard card) => PredictorCardChanges.Definition(captured.Context, card.Key) is { Rarity: "Basic" } definition &&
            (definition.IsStrike || definition.IsDefend);
        var initial = captured.Checkpoints[0].State;
        int count = initial.Deck.Select(initial.Card).Count(card => Basic(card) && card.Removable);
        Check(count > 0, "Pandora fixture has real removable basic targets");
        var plainRun = Route(captured, "PANDORAS_BOX");
        var plain = Preview(plainRun, [], count: 1).Nodes[0];
        Check(plain.Advanced && plain.Treasure is { RelicTaken: true } &&
            !plain.Cards.Any(card => Basic(card.Value) && card.Value.Removable) &&
            plain.PendingCauses.Any(cause => cause.Code == "PandoraTransformIdentityPending") &&
            Fact(plain, "deck.BasicStrike.count").DisplayValue == "0" && Fact(plain, "deck.BasicDefend.count").DisplayValue == "0" &&
            Fact(plain, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Unknown,
            "Pandora removes proven basics without publishing representative replacements or Niche progress");
        var luckyRun = Route(captured, "PANDORAS_BOX", held: [Held("LUCKY_FYSH", 0)]);
        var lucky = Preview(luckyRun, [], count: 1).Nodes[0];
        Check(lucky.Treasure!.GoldBalance == plain.Treasure!.GoldBalance + 15 * count &&
            lucky.Treasure.GoldPendingCauses.IsEmpty,
            "mandatory Pandora receipts settle Lucky Fysh gold once per replacement");
        var book = Preview(Route(captured, "PANDORAS_BOX", held: [Held("BOOK_OF_FIVE_RINGS", 0)]), [], count: 1).Nodes[0];
        Check(book.Inventory!.Relics.Single(item => item.Value.Key == R("BOOK_OF_FIVE_RINGS")) is { IsKnown: true } known &&
            known.Value.BookOfFiveRingsCardsAdded == count &&
            Fact(book, "relic.20000.cardsAdded").Status == PredictorKnowledgeStatus.Known &&
            Fact(book, "relic.20000.cardsAdded").DisplayValue == count.ToString(),
            "unknown replacement identities do not erase Pandora's known Book receipt count");
        var copies = Preview(Route(captured, "PANDORAS_BOX",
            held: [Held("LUCKY_FYSH", 0), Held("BOOK_OF_FIVE_RINGS", 1), Held("BING_BONG", 2)], hoarder: true), [], count: 1).Nodes[0];
        Check(copies.Treasure!.GoldBalance == plain.Treasure.GoldBalance + 15 * count * 4 &&
            copies.Inventory!.Relics.Single(item => item.Value.Key == R("BOOK_OF_FIVE_RINGS")).Value.BookOfFiveRingsCardsAdded == count * 4,
            "Bing Bong and Hoarder clones notify receipt hooks without recursive copying");
        var noBasics = initial.Deck.Select(initial.Card).Where(card => !Basic(card)).ToImmutableArray();
        var emptyPlain = Preview(Route(captured, "PANDORAS_BOX", deck: noBasics), [], count: 1).Nodes[0];
        var emptyLucky = Preview(Route(captured, "PANDORAS_BOX", held: [Held("LUCKY_FYSH", 0)], deck: noBasics), [], count: 1).Nodes[0];
        Check(emptyLucky.Treasure!.GoldBalance == emptyPlain.Treasure!.GoldBalance &&
            !emptyLucky.PendingCauses.Any(cause => cause.Code.StartsWith("Pandora", StringComparison.Ordinal)) &&
            Fact(emptyLucky, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Known,
            "Pandora with no basic targets adds no gold and invalidates no Niche stream");
        var exact = Preview(luckyRun, [Treasure(luckyRun, true)], PredictorRoutePreviewPolicy.AuthoredChoices, 1).Nodes[0];
        Check(exact.Cards.All(card => card.IsKnown) && exact.Treasure!.GoldBalance == lucky.Treasure.GoldBalance,
            "authored Pandora retains concrete transforms and agrees on mandatory receipt gold");

        foreach (string model in new[] { "THE_HUNT", "ROYALTIES" })
        {
            var catalog = captured.Context.Catalog!;
            var pool = catalog.TransformationPools.First(candidate => candidate.Cards.Contains(new("CARD", model)) &&
                catalog.CardDefinitions.Any(definition => definition.TransformationPool == candidate.Key && definition.Rarity == "Basic" &&
                    (definition.IsStrike || definition.IsDefend)));
            var basic = catalog.CardDefinitions.First(definition => definition.TransformationPool == pool.Key &&
                definition.Rarity == "Basic" && (definition.IsStrike || definition.IsDefend)).Prototype with { Id = 100, FloorAdded = 1 };
            var result = Preview(Route(captured, "PANDORAS_BOX", deck: [basic]), [], count: 2);
            var domain = model == "THE_HUNT" ? PredictorCombatDomain.ExtraCardRewards : PredictorCombatDomain.ExtraGoldRewards;
            Check(result.Nodes.Length == 2 && result.Nodes[1].PendingCauses.Any(cause =>
                    cause.Code == "PandoraTransformIdentityPending" && (cause.AffectedDomains & domain) != 0) &&
                Fact(result.Nodes[1], "rng.Rewards.calls").Status == PredictorKnowledgeStatus.Unknown,
                model + " in the actual basic transformation pool propagates extra rewards and later Rewards cursor uncertainty");
        }
        Console.WriteLine("RUNTIME_PANDORA_BOUNDARIES_PASS automatic removals; Lucky Fysh/Book receipts; copies; zero targets; actual transform pools; authored simulation");
    }

    private static PredictorRouteContinuation Preview(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.LiveMapDefaults, int count = 3)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints),
            draft = JsonSerializer.Serialize(run.CaptureNodeDraft());
        var result = run.PreviewRouteContinuation(plans, count, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            draft == JsonSerializer.Serialize(run.CaptureNodeDraft()), "preview never mutates source state or replay inputs");
        return result;
    }

    private static PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string property, bool before = false)
    {
        var frame = node.Diagnostics!;
        return (before ? frame.Before : frame.After).Select(frame.Snapshot.Fact).Single(fact => fact.PropertyKey == property);
    }
    private static PredictorPlannedNode Treasure(PredictorRun run, bool take) => new(run.Working.Position.NodeId)
    {
        Treasure = new() { Actions = take ?
            [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = run.TreasureRelic!.Key }] :
            [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.Leave)] }
    };

    private static void AcquisitionBoundary(PredictorRun captured, string relic)
    {
        var run = Route(captured, relic);
        Check(run.Working.Deck.Any(id => run.Working.Card(id).UpgradeLevel < run.Working.Card(id).MaxUpgradeLevel &&
            run.Working.Card(id).Type.ToString() == (relic == "WHETSTONE" ? "Attack" : "Skill")),
            relic + " fixture contains a fully known, nonempty eligible deck");
        var result = Preview(run, []);
        Check(result.Nodes.Length == 3 && result.Nodes[0].Treasure is { RelicTaken: true } && result.Nodes[0].Advanced &&
            result.Nodes[0].Inventory!.Relics.Any(item => item.IsKnown && item.Value.Key == R(relic)),
            relic + " is actually acquired and the route continues rather than default skipping its receipt");
        var cause = result.Nodes[0].PendingCauses.Single(c => c.Code == Cutoff);
        Check(cause.AffectedDomains == PredictorCombatDomain.NicheRng &&
            Fact(result.Nodes[0], "rng.Niche.calls").Status == PredictorKnowledgeStatus.Unknown &&
            Fact(result.Nodes[0], "rng.Niche.calls", before: true).Status == PredictorKnowledgeStatus.Known,
            relic + " invalidates only the downstream Niche stream after actual receipt, despite a complete deck");
        Check(result.Nodes[0].Cards.Where(card => card.Value.Type.ToString() == (relic == "WHETSTONE" ? "Skill" : "Attack"))
            .All(card => card.IsKnown), relic + " preserves known cards outside its upgrade population");
        Check(Fact(result.Nodes[1], "rng.Rewards.calls").Status == PredictorKnowledgeStatus.Known &&
            result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown) &&
            Fact(result.Nodes[2], "bags.shared").Status == PredictorKnowledgeStatus.Known &&
            result.Nodes[2].Treasure!.OfferedRelic == R("GORGET"),
            relic + " leaves independent future reward generation and shared bag identity known");

        var declined = Preview(run, [Treasure(run, false)]).Nodes[0];
        Check(declined.Treasure is { Opened: true, RelicTaken: false } &&
            !declined.PendingCauses.Any(c => c.Code == Cutoff) &&
            Fact(declined, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Known,
            relic + " merely offered and explicitly declined does not cut any stream");

        var authored = Preview(run, [Treasure(run, true)], PredictorRoutePreviewPolicy.AuthoredChoices, 1).Nodes[0];
        Check(authored.Advanced && authored.Treasure is { RelicTaken: true } && authored.Cards.All(card => card.IsKnown) &&
            Fact(authored, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Known &&
            !authored.PendingCauses.Any(c => c.Code == Cutoff),
            relic + " reusable authored route simulation retains exact random upgrades");

        Send(run, new OpenTreasure());
        Send(run, new TakeTreasureRelic(run.TreasureRelic!.Id));
        Send(run, new LeaveTreasure());
        Check(authored.Cards.Select(card => (card.Value.Id, card.Value.UpgradeLevel)).SequenceEqual(
            run.Working.Deck.Select(id => (id, run.Working.Card(id).UpgradeLevel))),
            relic + " authored route matches ordinary exact settlement on actual target instances");
        var fresh = new PredictorRun(run.Context, run.Working);
        fresh.RestoreRoutes(run.Routes);
        var restarted = Preview(fresh, [], count: 1).Nodes[0];
        Check(!restarted.PendingCauses.Any(c => c.Code == Cutoff) &&
            Fact(restarted, "rng.Niche.calls", before: true).Status == PredictorKnowledgeStatus.Known,
            relic + " fresh actual inventory/RNG facts replace the earlier projected cutoff");

        var operationSource = Route(captured, relic);
        long offerId = operationSource.Working.NextInstanceId;
        var operationState = operationSource.Working with { NextInstanceId = offerId + 1 };
        var operationContext = operationSource.Context with { Operation = new(1, 0, 1, []) };
        var offer = new PredictorAncientOffer(offerId, R(relic), true, null, null, null, null, null);
        var snapshot = new PredictorOperationSnapshot("live-policy-operation:" + relic, operationContext, operationState,
            new PredictorOperationAncientScene(new("EVENT", "NEOW"), [offer]), 70, 80);
        var operation = PredictorOperation.Preview(snapshot, new ChooseAncientOffer(offerId));
        Check(operation.Disposition == PredictorInputResult.Accepted && operation.RandomUpgrades.Length == 1 &&
            operation.State.Streams.Single(s => s.Stream == PredictorStream.Niche) == run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) &&
            operation.State.Deck.Select(id => (id, operation.State.Card(id).UpgradeLevel)).SequenceEqual(
                run.Working.Deck.Select(id => (id, run.Working.Card(id).UpgradeLevel))),
            relic + " current-node operation still reports the exact RNG state and upgraded cards");
    }

    private static void UnaffectedRelics(PredictorRun captured)
    {
        foreach (string relic in new[] { "NEOWS_TALISMAN", "ARCHAIC_TOOTH", "DISTINGUISHED_CAPE" })
        {
            var first = Preview(Route(captured, relic), [], count: 1).Nodes[0];
            Check(first.Treasure is { RelicTaken: true } && first.Advanced &&
                !first.PendingCauses.Any(cause => cause.Code == Cutoff) &&
                Fact(first, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Known,
                relic + " is acquired without inventing a deck-dependent random-consumption cutoff");
        }
    }

    private static void PickupOrder(PredictorRun captured)
    {
        var state = captured.Checkpoints[0].State with { Relics = [], GoldBudget = 100,
            PotionSlots = [null, null] };
        PredictorReward Reward(PredictorRewardKind kind, string key = "") => new(0, kind,
            PredictorRewardOrigin.Combat, key.Length == 0 ? default : new(kind == PredictorRewardKind.Potion ? "POTION" : "RELIC", key), []);
        var run = new PredictorRun(captured.Context, state);
        run.EnterRewardNode(new(2, 3, "live-policy:pickup-order"),
            [Reward(PredictorRewardKind.Relic, "WHETSTONE"),
                Reward(PredictorRewardKind.Gold) with { GoldAmount = 20 },
                Reward(PredictorRewardKind.Relic, "VAJRA"),
                Reward(PredictorRewardKind.Potion, "ENERGY_POTION"),
                Reward(PredictorRewardKind.Relic, "BOWLER_HAT")]);
        foreach (int expected in new[] { 4, 2, 1, 3, 0 })
        {
            var next = PredictorRewardPickupPolicy.Plan(run.Working, run.Rewards,
                liveMapDefaults: true, context: run.Context).NextInput;
            Check(next is TakeReward take && take.RewardId == run.Rewards[expected].Id,
                "live pickup order must be benefit relic, ordinary relic, gold, potion, exceptional relic");
            if (expected == 0)
                Check(PredictorRewardPickupPolicy.Plan(run).NextInput == null &&
                    PredictorRewardPickupPolicy.Plan(run).Pending.Any(pending => pending.Reason == PredictorRewardPickupPendingReason.RandomDeckChange),
                    "the reusable concrete pickup recommendation still leaves Whetstone manual");
            Send(run, next!);
        }
        Check(run.Working.GoldBudget == 125 && run.Rewards.All(reward => reward.Status == PredictorRewardStatus.Taken) &&
            run.Working.Relics.Last().Key == R("WHETSTONE") && run.Working.PotionSlots[0]?.Key.Entry == "ENERGY_POTION",
            "Bowler benefits ordinary gold, normal rewards are retained, and the special relic is acquired last");
    }

    private static void FragrantEvent(PredictorRun captured)
    {
        var run = Route(captured, "GORGET", eventNode: true);
        var initial = Preview(run, [], count: 1).Nodes[0].Event!;
        var option = initial.EntryOptions.Concat(initial.Options).First(item => item.Key == "FRAGRANT_MUSHROOM").Value!;
        var plan = new PredictorPlannedNode(run.Working.Position.NodeId)
        {
            Event = new(new("EVENT", "HUNGRY_FOR_MUSHROOMS"))
            {
                Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = option },
                    new(PredictorEventActionKind.Obtain)
                    { Obtain = new(PredictorObtainChildAction.Health, R("FRAGRANT_MUSHROOM")) { Health = new(70, 80) } }]
            }
        };
        var result = Preview(run, [plan], count: 2);
        Check(result.Nodes.Length == 2 && result.Nodes[0].Advanced && result.Nodes[0].Inventory!.Relics.Any(item =>
            item.IsKnown && item.Value.Key == R("FRAGRANT_MUSHROOM")) &&
            result.Nodes[0].PendingCauses.Any(c => c.Code == Cutoff) &&
            Fact(result.Nodes[0], "rng.Niche.calls").Status == PredictorKnowledgeStatus.Unknown &&
            result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown),
            "chosen Fragrant Mushroom obtains its actual relic and continues into independent combat rewards");
    }
}
