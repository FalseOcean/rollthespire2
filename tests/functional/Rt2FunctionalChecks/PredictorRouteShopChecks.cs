using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorRouteShopChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("ROUTE_SHOP " + message); }

    internal static void Run(PredictorRun captured)
    {
        ChoiceAndChangedStock(captured);
        CompletedPlanRemainsEditable(captured);
        LegacyPlanBindings(captured);
        OrderedDiscountRestockAndRemoval(captured);
        UnknownGoldAndConcreteAlternatives(captured);
        NestedObtainAndActiveReplay(captured);
        ForcedParasol(captured);
        UnknownPotionReceiptAndMandatoryPrefix(captured);
        LiveMapDefaults(captured);
        Console.WriteLine("RUNTIME_ROUTE_SHOP_PASS player choice; automatic authored closure and editing; semantic stock binding; ordered discount/restock/removal; nullable affordability; concrete alternate gold states; nested obtain; active prefix; mandatory Parasol; pure replay");
    }

    private static PredictorRun Route(PredictorRun captured, bool combatFirst = false,
        string encounter = "FUZZY_WURM_CRAWLER_WEAK", int combatCount = 1, int rewardOffset = 0,
        string? shopRelic = null, params string[] relics)
    {
        var original = captured.RouteMap(1);
        var types = combatFirst
            ? combatCount == 2
                ? new[] { MapPointType.Ancient, MapPointType.Monster, MapPointType.Monster, MapPointType.Shop, MapPointType.Monster, MapPointType.Boss }
                : new[] { MapPointType.Ancient, MapPointType.Monster, MapPointType.Shop, MapPointType.Monster, MapPointType.Boss }
            : new[] { MapPointType.Ancient, MapPointType.Shop, MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
        var bag = PredictorRelicBag.Empty(false);
        if (shopRelic != null) bag = bag with { Buckets = [new("Shop", [new("RELIC", shopRelic)])] };
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], Position = new(1, 1, "route-shop:entry"),
            GoldBudget = 5000, PotionSlots = [null, null, null], PersonalBag = bag,
            Maps = [new(1, original with { Points = points }, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = captured.Working.Acts.SetItem(0, captured.Working.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", encounter), new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 })
        };
        if (rewardOffset > 0)
        {
            var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
            for (int i = 0; i < rewardOffset; i++) rng.NextFloat();
            state = state.WithRng(PredictorStream.Rewards, rng);
        }
        foreach (string relic in relics) state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", relic));
        var run = new PredictorRun(captured.Context with { NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns) }, state);
        run.SetRoute(1, points.Select(p => p.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static PredictorShopOfferSnapshot Offer(PredictorRun run, int slot)
    {
        var entry = run.ShopEntries[slot];
        return new(slot, entry.Revision, entry.Kind, entry.Key, entry.Card is { } card ? card with { Id = 0 } : null,
            entry.BaseCost, run.ShopOffers[slot].Cost, entry.IsOnSale, entry.Stocked)
        { StockRevisionIsOccurrence = true };
    }
    private static PredictorPlannedShopAction Buy(PredictorRun run, int slot)
        => new(PredictorShopActionKind.Buy) { ExpectedOffer = Offer(run, slot) };
    private static PredictorPlannedCard Target(PredictorRun run, long id)
        => new(id, run.Working.Card(id).Key, run.Working.Card(id).FloorAdded);
    private static PredictorPlannedNode Plan(string node, params PredictorPlannedShopAction[] actions)
        => new(node) { Shop = new() { Actions = actions.ToImmutableArray() } };

    private static PredictorRouteContinuation Pure(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans, int limit = 1,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices)
    {
        var before = JsonSerializer.Serialize(run.Working);
        var checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var draft = JsonSerializer.Serialize(run.CaptureNodeDraft());
        var request = run.Request;
        long revision = run.Revision;
        var result = run.PreviewRouteContinuation(plans, maximumNodes: limit, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(before == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            draft == JsonSerializer.Serialize(run.CaptureNodeDraft()) && request == run.Request && revision == run.Revision,
            "Preview must preserve state, checkpoint, active draft, request and revision");
        return result;
    }

    private static void LiveMapDefaults(PredictorRun captured)
    {
        var run = Route(captured);
        var result = Pure(run, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(result.Nodes.Length == 2 && result.Nodes[0].Advanced &&
            result.Nodes[0].Shop is { AppliedActions: 0, GoldBalance: 5000 } shop && shop.Offers.All(o => o.Value!.Stocked) &&
            result.Nodes[1].Room == PredictorRoomKind.Monster,
            "Default shop leave continues with all offers stocked and no purchase/removal");
        var parasol = Route(captured, relics: ["LORDS_PARASOL"]);
        var forced = Pure(parasol, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(!forced.Advanced && forced.Shop!.AutomaticPurchasesActive &&
            forced.Shop.Offers.Take(13).All(o => o.Value?.Stocked == false) &&
            forced.PendingCauses.Any(c => c.Code == "ShopRemovalChoice"),
            "No-buy defaults preserve native Parasol purchases and required removal target");
    }
    private static void Submit(PredictorRun run, PredictorInput input)
        => Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Concrete settlement: " + input.GetType().Name);
    private static void Purchase(PredictorRun run, int slot)
    {
        if (!run.ShopInventoryOpen) Submit(run, new OpenShopInventory());
        Submit(run, new BuyShopEntry(run.ShopEntries[slot].Id, run.ShopInventoryRevision));
    }

    private static void ChoiceAndChangedStock(PredictorRun captured)
    {
        var run = Route(captured);
        var unchanged = Pure(run, []).Nodes.Single();
        Check(!unchanged.Advanced && unchanged.Shop is { AppliedActions: 0 } shop && shop.Offers.Length == 14 &&
            shop.Offers.All(o => o.IsKnown && o.Value!.Card?.Id is null or 0) && shop.GoldBalance == 5000 &&
            unchanged.PendingCauses.Any(c => c.Code == "ShopChoice"), "Shop has no implicit purchase or leave default");
        var changed = Buy(run, 0) with { ExpectedOffer = Offer(run, 0) with { Cost = Offer(run, 0).Cost + 1 } };
        var mismatch = Pure(run, [Plan(run.Working.Position.NodeId, changed)]).Nodes.Single();
        Check(mismatch.Shop!.AppliedActions == 0 && mismatch.PendingCauses.Any(c => c.Code == "PlannedShopOfferChanged"),
            "Changed semantic offer cannot be rebound by its original slot or current entry ID");
        var wrongOccurrence = Buy(run, 0) with { ExpectedOffer = Offer(run, 0) with { StockRevision = Offer(run, 0).StockRevision + 1 } };
        var occurrenceMismatch = Pure(run, [Plan(run.Working.Position.NodeId, wrongOccurrence)]).Nodes.Single();
        Check(occurrenceMismatch.Shop!.AppliedActions == 0 &&
            occurrenceMismatch.PendingCauses.Any(c => c.Code == "PlannedShopOfferChanged"),
            "Matching model and price never rebind an authored offer to a different stock occurrence");
        var poor = Route(captured);
        Submit(poor, new CorrectGoldBalance(0));
        var unaffordable = Pure(poor, [Plan(poor.Working.Position.NodeId, Buy(poor, 0))]).Nodes.Single();
        Check(unaffordable.Shop!.Offers.All(o => o.IsKnown && o.Affordable == false) &&
            unaffordable.PendingCauses.Any(c => c.Code == "PlannedShopInsufficientGold") &&
            !unaffordable.PendingCauses.Any(c => c.Code == "PlannedShopAffordabilityPending"),
            "A known budget shortfall is not a missing affordability fact");
        Check(Pure(run, [Plan(run.Working.Position.NodeId, new PredictorPlannedShopAction(PredictorShopActionKind.Leave))]).Nodes.Single().Advanced,
            "Explicit leave advances without requiring a purchase");
        var denied = Route(captured, relics: ["SOZU"]);
        var potion = Buy(denied, 10);
        Submit(denied, new OpenShopInventory());
        Check(denied.Submit(denied.Request!, new BuyShopEntry(denied.ShopEntries[10].Id, denied.ShopInventoryRevision)) ==
            PredictorInputResult.NotAllowed, "Concrete Sozu purchase denial fixture");
        var deniedPreview = Pure(denied, [Plan(denied.Working.Position.NodeId, potion)]).Nodes.Single();
        Check(deniedPreview.Shop!.AppliedActions == 0 && deniedPreview.Shop.Offers[10].Value!.Stocked,
            "A recorded rejected purchase is not an accepted authored purchase prefix");
    }

    private static void CompletedPlanRemainsEditable(PredictorRun captured)
    {
        foreach (var policy in new[] { PredictorRoutePreviewPolicy.AuthoredChoices, PredictorRoutePreviewPolicy.LiveMapDefaults })
        {
            var run = Route(captured);
            string node = run.Working.Position.NodeId;
            var first = Buy(run, 0);
            var single = Pure(run, [Plan(node, first)], 2, policy);
            var shop = single.Nodes[0].Shop!;
            Check(single.Nodes.Length == 2 && single.Nodes[0].Advanced && shop.AppliedActions == 1 &&
                shop.Offers.Length == 14 && shop.Offers[0].Value is { Stocked: false } &&
                shop.Offers[1] is { Affordable: true, Value.Stocked: true } &&
                shop.GoldBalance == run.Working.GoldBudget - first.ExpectedOffer!.Cost &&
                single.Nodes[1].Room == PredictorRoomKind.Monster,
                "A single authored purchase continues while retaining final offers and affordability for more editing");
            Check(shop.Offers[1].Value == Offer(run, 1),
                "An unrelated purchase preserves the unchanged stocked card's occurrence and exact semantic snapshot");

            var second = new PredictorPlannedShopAction(PredictorShopActionKind.Buy) { ExpectedOffer = shop.Offers[1].Value };
            var appended = Pure(run, [Plan(node, first, second)], 2, policy).Nodes[0];
            var concrete = Route(captured);
            Purchase(concrete, 0); Purchase(concrete, 1);
            Check(appended.Advanced && appended.Shop!.AppliedActions == 2 &&
                appended.Shop.GoldBalance == concrete.Working.GoldBudget &&
                appended.Shop.Offers.Select(o => o.Value).SequenceEqual(Enumerable.Range(0, 14).Select(i => Offer(concrete, i))) &&
                appended.Cards.Select(c => c.Value.Key).SequenceEqual(concrete.Working.Deck.Select(id => concrete.Working.Card(id).Key)),
                "Appending a second purchase after automatic completion recomputes native payment, stock and card order");

            var reversed = Pure(run, [Plan(node, second, first)], 2, policy).Nodes[0];
            var reversedConcrete = Route(captured);
            Purchase(reversedConcrete, 1); Purchase(reversedConcrete, 0);
            Check(reversed.Advanced && reversed.Shop!.AppliedActions == 2 &&
                reversed.Shop.GoldBalance == reversedConcrete.Working.GoldBudget &&
                reversed.Cards.Select(c => c.Value.Key).SequenceEqual(reversedConcrete.Working.Deck.Select(id => reversedConcrete.Working.Card(id).Key)),
                "Reordering a completed plan replays the revised purchase order from node entry");

            var revoked = Pure(run, [Plan(node, second)], 2, policy).Nodes[0];
            var revokedConcrete = Route(captured);
            Purchase(revokedConcrete, 1);
            Check(revoked.Advanced && revoked.Shop!.AppliedActions == 1 && revoked.Shop.Offers[0].Value!.Stocked &&
                revoked.Shop.GoldBalance == revokedConcrete.Working.GoldBudget &&
                revoked.Cards.Select(c => c.Value.Key).SequenceEqual(revokedConcrete.Working.Deck.Select(id => revokedConcrete.Working.Card(id).Key)),
                "Revoking a purchase restores its stock and payment without retaining projected effects");
            var cleared = Pure(run, [Plan(node)], 2, policy).Nodes[0];
            Check(cleared.Advanced && cleared.Shop is { AppliedActions: 0, GoldBalance: 5000 } &&
                cleared.Shop.Offers.All(o => o.Value!.Stocked),
                "Clearing the shopping sequence continues with no purchases or removals");

            var removal = new PredictorPlannedShopAction(PredictorShopActionKind.BeginRemoval) { ExpectedOffer = Offer(run, 13) };
            var pending = Pure(run, [Plan(node, removal)], 2, policy).Nodes.Single();
            Check(!pending.Advanced && pending.Shop!.AppliedActions == 1 &&
                pending.PendingCauses.Any(c => c.Code == "ShopRemovalChoice"),
                "Automatic completion preserves the real unchosen removal target");
        }
    }

    private static void LegacyPlanBindings(PredictorRun captured)
    {
        PredictorPlannedShopAction LegacyBuy(PredictorRun run, int slot)
        {
            var entry = run.ShopEntries[slot];
            var offer = Offer(run, slot) with { StockRevisionIsOccurrence = false,
                StockRevision = entry.Stocked && entry.Card != null ? run.ShopInventoryRevision : entry.Revision };
            return new(PredictorShopActionKind.Buy) { ExpectedOffer = offer };
        }
        var baseline = Route(captured);
        var actual = Route(captured);
        var first = LegacyBuy(actual, 0); Purchase(actual, 0);
        var second = LegacyBuy(actual, 1); Purchase(actual, 1);
        var original = Plan(baseline.Working.Position.NodeId, first, second);
        var persisted = Route(captured);
        persisted.SetPlannedNode(original);
        string json = PredictorRunStore.Serialize(persisted);
        Check(!json.Contains(nameof(PredictorShopOfferSnapshot.StockRevisionIsOccurrence), StringComparison.Ordinal),
            "Legacy serialization omits the new revision encoding marker");
        var restored = PredictorRunStore.Deserialize(json, persisted.Context.CatalogFingerprint, persisted.Context.UnlockFingerprint);
        var saved = restored.PlannedNodes.Single();
        string restoredBefore = PredictorRunStore.Serialize(restored);
        var result = Pure(restored, restored.PlannedNodes).Nodes.Single();
        Check(result.Advanced && result.Shop!.AppliedActions == 2 && result.Shop.GoldBalance == actual.Working.GoldBudget &&
            result.Shop.ResolvedActions.Length == 2 && result.Shop.ResolvedActions.All(action => action.ExpectedOffer!.StockRevisionIsOccurrence) &&
            result.Shop.ResolvedActions[1].ExpectedOffer!.StockRevision == 0 &&
            saved.Shop!.Actions[1].ExpectedOffer!.StockRevision == 1 && restoredBefore == PredictorRunStore.Serialize(restored),
            "A serialized legacy multibuy plan replays exactly and exposes stable resolved bindings without rewriting saved intent");
        var active = Route(captured); Purchase(active, 0);
        var resumed = Pure(active, [saved]).Nodes.Single();
        Check(resumed.Advanced && resumed.Shop!.ResolvedActions.Length == 2 &&
            resumed.Shop.ResolvedActions[1].ExpectedOffer!.StockRevisionIsOccurrence &&
            resumed.Shop.GoldBalance == actual.Working.GoldBudget,
            "Legacy accepted purchase prefixes resolve stable bindings without purchasing twice");

        var resolved = result.Shop!.ResolvedActions;
        var legacyReordered = Pure(baseline, [Plan(baseline.Working.Position.NodeId, second, first)]).Nodes.Single();
        Check(!legacyReordered.Advanced && legacyReordered.Shop!.AppliedActions == 0 &&
            legacyReordered.PendingCauses.Any(cause => cause.Code == "PlannedShopOfferChanged"),
            "Legacy encoding retains its exact historical boundary guard until an explicit edit adopts resolved bindings");
        var reordered = Pure(baseline, [Plan(baseline.Working.Position.NodeId, resolved[1], resolved[0])]).Nodes.Single();
        var reverseActual = Route(captured); Purchase(reverseActual, 1); Purchase(reverseActual, 0);
        Check(reordered.Advanced && reordered.Shop!.AppliedActions == 2 &&
            reordered.Shop.GoldBalance == reverseActual.Working.GoldBudget &&
            reordered.Cards.Select(card => card.Value.Key).SequenceEqual(reverseActual.Working.Deck.Select(id => reverseActual.Working.Card(id).Key)),
            "Explicit legacy-plan reorder uses resolved occurrence bindings and preserves native purchase order");
        var revoked = Pure(baseline, [Plan(baseline.Working.Position.NodeId, resolved[1])]).Nodes.Single();
        var revokeActual = Route(captured); Purchase(revokeActual, 1);
        Check(revoked.Advanced && revoked.Shop!.AppliedActions == 1 && revoked.Shop.Offers[0].Value!.Stocked &&
            revoked.Shop.GoldBalance == revokeActual.Working.GoldBudget,
            "Explicit legacy-plan revocation uses stable remaining bindings rather than an obsolete inventory refresh revision");

        var courierBaseline = Route(captured, relics: ["THE_COURIER"]);
        var courierActual = Route(captured, relics: ["THE_COURIER"]);
        var courierFirst = LegacyBuy(courierActual, 0); Purchase(courierActual, 0);
        var courierOther = LegacyBuy(courierActual, 1); Purchase(courierActual, 1);
        var courierReplacement = LegacyBuy(courierActual, 0); Purchase(courierActual, 0);
        var courier = Pure(courierBaseline, [Plan(courierBaseline.Working.Position.NodeId,
            courierFirst, courierOther, courierReplacement)]).Nodes.Single();
        Check(courier.Advanced && courier.Shop!.AppliedActions == 3 && courier.Shop.GoldBalance == courierActual.Working.GoldBudget &&
            courier.Shop.ResolvedActions[2].ExpectedOffer is { StockRevision: 1, StockRevisionIsOccurrence: true } &&
            courierReplacement.ExpectedOffer!.StockRevision == 2 &&
            courier.Shop.Offers.Select(offer => offer.Value).SequenceEqual(Enumerable.Range(0, 14).Select(slot => Offer(courierActual, slot))),
            "Legacy Courier plans preserve a replacement card refreshed again by an unrelated purchase");
    }

    private static void OrderedDiscountRestockAndRemoval(PredictorRun captured)
    {
        var baseline = Route(captured, shopRelic: "MEMBERSHIP_CARD", relics: ["THE_COURIER"]);
        var actual = Route(captured, shopRelic: "MEMBERSHIP_CARD", relics: ["THE_COURIER"]);
        Check(actual.ShopEntries[9].Key?.Entry == "MEMBERSHIP_CARD", "Controlled actual Shop rarity bucket");
        int originalCardCost = actual.ShopOffers[0].Cost;
        var membership = Buy(actual, 9);
        Purchase(actual, 9);
        Check(actual.ShopOffers[0].Cost <= originalCardCost / 2 + 1 && actual.ShopEntries[9].Stocked &&
            actual.ShopEntries[9].Key?.Entry == "CIRCLET", "Membership changes later price; Courier restocks relic with ordinary rarity");
        var card = Buy(actual, 0);
        Purchase(actual, 0);
        Check(actual.ShopEntries[0].Stocked && !actual.ShopEntries[0].IsOnSale && actual.ShopEntries[0].Revision > 0,
            "Courier card restock is a fresh non-sale offer");
        var removal = new PredictorPlannedShopAction(PredictorShopActionKind.BeginRemoval) { ExpectedOffer = Offer(actual, 13) };
        long target = actual.Working.Deck.First(id => actual.Working.Card(id).Removable);
        var choose = new PredictorPlannedShopAction(PredictorShopActionKind.ChooseRemoval) { Card = Target(actual, target) };
        Submit(actual, new BeginShopCardRemoval(actual.ShopEntries[13].Id, actual.ShopInventoryRevision));
        Submit(actual, new ChooseShopRemovalCard(target));
        var expected = Enumerable.Range(0, 14).Select(i => Offer(actual, i)).ToArray();
        var plan = Plan(baseline.Working.Position.NodeId, membership, card, removal, choose);
        var projected = Pure(baseline, [plan], 2);
        var result = projected.Nodes[0];
        Check(result.Advanced && result.Shop is { AppliedActions: 4 } shop && shop.GoldBalance == actual.Working.GoldBudget &&
            shop.Offers.Select(o => o.Value).SequenceEqual(expected) && !shop.Offers[13].Value!.Stocked &&
            result.Cards.All(c => c.Value.Id != target) && projected.Nodes.Length == 2 && projected.Nodes[1].Room == PredictorRoomKind.Monster,
            "Completed ordered plan automatically continues with actual discounted payments, restock and one-use removal settlement");
        var legacy = Pure(baseline, [plan with { Shop = plan.Shop! with
            { Actions = plan.Shop!.Actions.Add(new(PredictorShopActionKind.Leave)) } }]).Nodes.Single();
        Check(legacy.Advanced && legacy.Shop!.AppliedActions == 5 && legacy.Shop.GoldBalance == result.Shop!.GoldBalance &&
            legacy.Shop.Offers.Select(o => o.Value).SequenceEqual(expected),
            "An older explicit shop leave remains readable with unchanged final inventory and payments");

        var active = Route(captured, shopRelic: "MEMBERSHIP_CARD", relics: ["THE_COURIER"]);
        Purchase(active, 9);
        var resumed = Pure(active, [plan]).Nodes.Single();
        Check(resumed.Advanced && resumed.Shop!.AppliedActions == 4 && resumed.Shop.GoldBalance == actual.Working.GoldBudget,
            "An active accepted purchase prefix resumes remaining actions without buying twice");
        var stale = Route(captured, relics: ["THE_COURIER"]);
        var first = Buy(stale, 0);
        Purchase(stale, 0);
        var staleResult = Pure(stale, [Plan(stale.Working.Position.NodeId, first, first)]).Nodes.Single();
        Check(staleResult.Shop!.AppliedActions == 1 && staleResult.PendingCauses.Any(c => c.Code == "PlannedShopOfferChanged"),
            "A second stale same-slot purchase cannot consume Courier replacement stock");
        var wrongReplacement = Buy(stale, 0) with
        { ExpectedOffer = Offer(stale, 0) with { StockRevision = first.ExpectedOffer!.StockRevision } };
        var replacementMismatch = Pure(stale, [Plan(stale.Working.Position.NodeId, first, wrongReplacement)]).Nodes.Single();
        Check(replacementMismatch.Shop!.AppliedActions == 1 &&
            replacementMismatch.PendingCauses.Any(c => c.Code == "PlannedShopOfferChanged"),
            "Even matching Courier replacement model/card/price cannot bind to the previous stock occurrence");

        var reversed = Route(captured, shopRelic: "MEMBERSHIP_CARD", relics: ["THE_COURIER"]);
        Purchase(reversed, 0); Purchase(reversed, 9);
        var normal = Route(captured, shopRelic: "MEMBERSHIP_CARD", relics: ["THE_COURIER"]);
        Purchase(normal, 9); Purchase(normal, 0);
        Check(reversed.Working.GoldBudget < normal.Working.GoldBudget,
            "Concrete purchase orders have different budgets when Membership arrives between payments");
    }

    private static PredictorCombatNodeOverrides UnresolvedGold => new()
    {
        EnemyCreations = [new(PredictorEnemyCreationSource.SurpriseGremlins, 0)],
        GremlinGold = new(false, true, null) { RecoverAllStolenGold = true }
    };

    private static void UnknownGoldAndConcreteAlternatives(PredictorRun captured)
    {
        var run = Route(captured, true, "GREMLIN_MERC_NORMAL", relics: ["WHITE_BEAST_STATUE", "BOWLER_HAT"]);
        var first = new PredictorPlannedNode(run.Working.Position.NodeId) { Combat = UnresolvedGold, LeaveRewards = true };
        var forecast = Pure(run, [first], 2);
        var shop = forecast.Nodes.Last().Shop!;
        Check(shop.Offers.Length == 14 && shop.Offers.All(o => o.IsKnown && o.Affordable == null) &&
            shop.GoldBalance == null && shop.GoldPendingCauses.Any(c => c.Code == "RecoveredGoldAmount"),
            "Unknown gold keeps all independent offer identities/prices and masks affordability alone");
        foreach (int amount in new[] { 0, 37 })
        {
            var actual = Route(captured, true, "GREMLIN_MERC_NORMAL", relics: ["WHITE_BEAST_STATUE", "BOWLER_HAT"]);
            var input = actual.ResolveCombatDefaults(UnresolvedGold with { GremlinGold = new(false, true, amount) })!.Input!;
            Submit(actual, input);
            while (PredictorRewardPickupPolicy.Plan(actual).NextInput is { } pickup) Submit(actual, pickup);
            Submit(actual, new LeaveRewards());
            actual.AdvanceRoute();
            Check(shop.Offers.Select(o => o.Value).SequenceEqual(Enumerable.Range(0, 14).Select(i => Offer(actual, i))),
                "Every reported known offer equals both concrete alternate recovered-gold states");
        }
        var nodeId = forecast.Nodes.Last().Position.NodeId;
        var purchase = new PredictorPlannedShopAction(PredictorShopActionKind.Buy) { ExpectedOffer = shop.Offers[0].Value };
        var pending = Pure(run, [first, Plan(nodeId, purchase)], 2).Nodes.Last();
        Check(pending.PendingCauses.Any(c => c.Code == "PlannedShopAffordabilityPending") &&
            pending.Shop!.Offers.All(o => o.IsKnown), "Authored buying waits for actual Gold without hiding known inventory");
        var leave = Pure(run, [first, Plan(nodeId, new PredictorPlannedShopAction(PredictorShopActionKind.Leave))], 2).Nodes.Last();
        Check(leave.Advanced, "Independent authored leave does not consume unknown Gold");
    }

    private static void NestedObtainAndActiveReplay(PredictorRun captured)
    {
        var baseline = Route(captured, shopRelic: "DOLLYS_MIRROR");
        var buy = Buy(baseline, 9);
        var suspended = Pure(baseline, [Plan(baseline.Working.Position.NodeId, buy)]).Nodes.Single();
        Check(suspended.Phase == PredictorPhase.ObtainDeckCards && suspended.Shop!.RequiredChoices.Length == 1 &&
            suspended.Shop.RequiredChoices[0].Source.Entry == "DOLLYS_MIRROR",
            $"Buying a relic pauses at the actual nested obtain target choice: phase={suspended.Phase}; " +
            $"offer={buy.ExpectedOffer!.Key}; applied={suspended.Shop?.AppliedActions}; " +
            $"removable={baseline.Working.Deck.Count(id => baseline.Working.Card(id).Removable)}; " +
            $"children={string.Join(',', suspended.Shop?.RequiredChoices.Select(c => c.Source.Entry) ?? [])}; " +
            $"pending={string.Join(',', suspended.PendingCauses.Select(c => c.Code))}");
        var liveSuspended = Pure(baseline, [Plan(baseline.Working.Position.NodeId, buy)], 2,
            PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(!liveSuspended.Advanced && liveSuspended.Shop!.RequiredChoices.Single().Source.Entry == "DOLLYS_MIRROR",
            "Automatic shop continuation never answers the required relic obtain target");
        var targets = baseline.Working.Deck.Where(id => !PredictorCardChanges.Definition(baseline.Context, baseline.Working.Card(id).Key).IsQuest)
            .Take(1).Select(id => Target(baseline, id)).ToImmutableArray();
        Check(targets.Length == 1, "Dolly's Mirror concrete fixture target");
        var child = new PredictorPlannedShopAction(PredictorShopActionKind.Obtain)
        { Obtain = new(PredictorObtainChildAction.ChooseDeckCards, new("RELIC", "DOLLYS_MIRROR")) { DeckCards = targets } };
        var plan = Plan(baseline.Working.Position.NodeId, buy, child);
        var result = Pure(baseline, [plan]).Nodes.Single();
        Check(result.Advanced && result.Shop!.AppliedActions == 2 && result.Cards.Length == baseline.Working.Deck.Length + 1,
            $"Explicit nested targets settle through the existing relic obtain path: advanced={result.Advanced}; phase={result.Phase}; " +
            $"applied={result.Shop?.AppliedActions}; deck={result.Cards.Length}/{baseline.Working.Deck.Length + 1}; " +
            $"pending={string.Join(',', result.PendingCauses.Select(c => c.Code))}");
        Purchase(baseline, 9);
        var active = Pure(baseline, [plan]).Nodes.Single();
        Check(active.Advanced && active.Shop!.AppliedActions == 2 && active.Shop.GoldBalance == result.Shop!.GoldBalance,
            "Active purchased relic resumes its nested child without charging or obtaining twice");
        Submit(baseline, new ChooseObtainDeckCards(targets.Select(c => c.InstanceId).ToImmutableArray()));
        Check(baseline.Phase == PredictorPhase.Shop && baseline.Working.Deck.Length == result.Cards.Length,
            "Concrete obtain target returns control to the merchant transaction");
        Submit(baseline, new CloseShopInventory());
        Submit(baseline, new LeaveShop());
    }

    private static void ForcedParasol(PredictorRun captured)
    {
        var run = Route(captured, relics: ["LORDS_PARASOL"]);
        run.AutomaticRewardPickup = false;
        Check(run.ShopParasolActive && run.ShopParasolCursor == 13 && run.ShopRemovalCandidates.Length > 0,
            "Actual entry applies Parasol's seven card purchases and mandatory removal pause");
        var pending = Pure(run, []).Nodes.Single();
        Check(pending.Shop!.AutomaticPurchasesActive && pending.Shop.Offers.Take(13).All(o => o.Value?.Stocked == false) &&
            pending.PendingCauses.Any(c => c.Code == "ShopRemovalChoice"), "Pickup preference cannot suppress mandatory Parasol prefix");
        var canceled = Pure(run, [Plan(run.Working.Position.NodeId, new PredictorPlannedShopAction(PredictorShopActionKind.CancelRemoval))]).Nodes.Single();
        Check(!canceled.Advanced && canceled.Shop!.AutomaticPurchasesActive, "Forced Parasol removal cannot be canceled by a plan");
        long id = run.ShopRemovalCandidates[0];
        var closed = Pure(run, [Plan(run.Working.Position.NodeId,
            new PredictorPlannedShopAction(PredictorShopActionKind.ChooseRemoval) { Card = Target(run, id) })]).Nodes.Single();
        Check(closed.Advanced && closed.Shop!.GoldBalance == run.Working.GoldBudget,
            "Completing Parasol's removal automatically continues after native free merchant settlement");

        var uncertain = Route(captured, true, "GREMLIN_MERC_NORMAL", relics: ["LORDS_PARASOL", "WHITE_BEAST_STATUE", "BOWLER_HAT"]);
        var projected = Pure(uncertain, [new(uncertain.Working.Position.NodeId) { Combat = UnresolvedGold, LeaveRewards = true }], 2).Nodes.Last();
        Check(projected.Shop!.AutomaticPurchasesActive && projected.Shop.Offers.Take(10).All(o => o.Value?.Stocked == false) &&
            projected.Shop.Offers.Skip(10).Take(3).Count(o => o.Value?.Stocked == false) == 2 &&
            projected.PendingCauses.Any(c => c.Code == "ShopRemovalChoice") &&
            !projected.PendingCauses.Any(c => c.Code == "ShopAutomaticPurchasePending"),
            "Free mandatory Parasol purchases remain independent of unresolved Gold");
    }

    private static void UnknownPotionReceiptAndMandatoryPrefix(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog!;
        var all = catalog.AllCharacterCards.Concat(catalog.CharacterCards).Concat(catalog.ColorlessCards).DistinctBy(c => c.CardKey).ToArray();
        var alchemize = all.Single(c => c.CardKey.Entry == "ALCHEMIZE");
        var pool = new List<NeowEffectCardSnapshot> { alchemize };
        foreach (var (type, count) in new[] { (EffectCardType.Attack, 2), (EffectCardType.Skill, 2), (EffectCardType.Power, 1) })
            pool.AddRange(all.Where(c => c.CardType == type && c.CardKey != alchemize.CardKey &&
                c.Rarity is not (EffectCardRarity.Basic or EffectCardRarity.Ancient) &&
                !PredictorCombatConsequences.IsTriggeredSource(catalog.CardDefinitions.Single(d => d.Prototype.Key == c.CardKey).Prototype) &&
                !catalog.CardDefinitions.Single(d => d.Prototype.Key == c.CardKey).IsQuest).Take(count));
        var controlled = new PredictorRun(captured.Context with
        { Catalog = catalog with { CharacterCards = pool.Select(c => c with { Rarity = EffectCardRarity.Common }).ToImmutableArray() } }, captured.Working);
        int offset = -1;
        for (int i = 0; i < 64; i++)
        {
            var candidate = Route(controlled, true, combatCount: 2, rewardOffset: i, relics: ["WHITE_BEAST_STATUE"]);
            if (Pure(candidate, []).Nodes.Single().Rewards.Any(r => r.IsKnown && r.Value!.Kind == PredictorRewardKind.Card &&
                r.Value.Cards.Any(c => c.Key.Entry == "ALCHEMIZE"))) { offset = i; break; }
        }
        Check(offset >= 0, "Controlled actual reward population exposes optional Alchemize within bounded source draws");
        var run = Route(controlled, true, combatCount: 2, rewardOffset: offset, relics: ["WHITE_BEAST_STATUE"]);
        var second = new PredictorPlannedNode("map:1:2:3:3") { LeaveRewards = true };
        var shop = Pure(run, [second], 3).Nodes.Last();
        Check(shop.Shop is { } known && known.Offers.All(o => o.IsKnown) && shop.PendingCauses.Any(c =>
            c.Code == "PendingCardChoice" && c.AffectedDomains.HasFlag(PredictorCombatDomain.PotionInventory)),
            "Optional earlier Alchemize changes potion receipt while merchant offers remain known");
        var buyPotion = new PredictorPlannedShopAction(PredictorShopActionKind.Buy) { ExpectedOffer = shop.Shop!.Offers[10].Value };
        var blocked = Pure(run, [second, Plan(shop.Position.NodeId, buyPotion)], 3).Nodes.Last();
        Check(blocked.PendingCauses.Any(c => c.Code == "PlannedShopPotionReceiptPending") && blocked.Shop!.Offers[10].IsKnown,
            "Known offered potion remains visible when actual receipt capacity is unresolved");
        var parasol = Route(controlled, true, combatCount: 2, rewardOffset: offset, relics: ["WHITE_BEAST_STATUE", "LORDS_PARASOL"]);
        var prefix = Pure(parasol, [second], 3).Nodes.Last();
        Check(prefix.Shop!.AutomaticPurchasesActive && prefix.Shop.Offers.Take(10).All(o => o.Value?.Stocked == false) &&
            prefix.Shop.Offers.Skip(10).Take(3).All(o => o.Value?.Stocked == true) &&
            prefix.PendingCauses.Any(c => c.Code == "ShopAutomaticPurchasePending"),
            "Parasol commits its independent mandatory card/relic prefix and pauses before the first uncertain potion receipt");
    }
}
