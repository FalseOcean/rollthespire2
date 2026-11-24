using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

// Exercises actual route shop consumers with the native harness's captured
// catalogs. No private recorder calls or mirrored ShopCost implementations.
internal static class PredictorShopReadRecordingChecks
{
    private const string ShopNode = "map:1:1:3:2";
    private const string CombatShopNode = "map:1:2:3:3";
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static PredictorRun Route(PredictorRun captured, PredictorCombatPendingCause? missing = null,
        bool combatBeforeShop = false)
    {
        var context = captured.Context with
        {
            NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns),
            Live = missing == null ? null : new(1, true, [missing])
        };
        var types = combatBeforeShop ? new[] { MapPointType.Ancient, MapPointType.Monster, MapPointType.Shop, MapPointType.Boss } :
            new[] { MapPointType.Ancient, MapPointType.Shop, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
        var map = captured.RouteMap(1) with { Points = points };
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], GoldBudget = 1000,
            CardShopRemovalsUsed = 0, PotionSlots = [null, null],
            Position = new(1, 1, "shop-read:entry"), Maps = [new(1, map, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)]
        };
        if (combatBeforeShop) state = state with
        {
            Acts = state.Acts.SetItem(0, state.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 })
        };
        var run = new PredictorRun(context, state);
        run.SetRoute(1, points.Select(point => point.Position).ToImmutableArray());
        return run;
    }

    private static PredictorRouteNodeKnowledge Preview(PredictorRun run, ImmutableArray<PredictorPlannedShopAction> actions)
    {
        string before = JsonSerializer.Serialize(new { run.Working, run.Checkpoints, run.Request, run.Phase });
        var result = run.PreviewRouteContinuation([new(ShopNode) { Shop = new() { Actions = actions } }], 1,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.AuthoredChoices);
        Check(before == JsonSerializer.Serialize(new { run.Working, run.Checkpoints, run.Request, run.Phase }),
            "Shop trace mutated the actual run.");
        Check(result.Nodes.Length == 1 && result.Nodes[0].Shop != null && result.Nodes[0].Diagnostics != null,
            "Actual shop route did not publish diagnostics.");
        return result.Nodes[0];
    }

    private static PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string suffix,
        PredictorShopActionKind action = PredictorShopActionKind.Buy,
        PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.PlayerInput)
    {
        var facts = node.Diagnostics!.Results.Select(node.Diagnostics.Snapshot.Fact).ToArray();
        // Each applied operation owns its own reads/return/writes. Automatic
        // closure adds an ApprovedDefault Leave beside the authored Buy or
        // removal; a suffix alone cannot identify the intended operation.
        const string selection = ".input.action";
        var input = facts.Single(fact => fact.PropertyKey.StartsWith("result.shop.action.", StringComparison.Ordinal) &&
            fact.PropertyKey.EndsWith(selection, StringComparison.Ordinal) && fact.DisplayValue == action.ToString() && fact.Origin == origin);
        string prefix = input.PropertyKey[..^selection.Length];
        return facts.Single(fact => fact.PropertyKey == prefix + suffix);
    }

    internal static void Run(PredictorRun captured)
    {
        var baseline = Preview(Route(captured), []);
        var card = baseline.Shop!.Offers.First(offer => offer.IsKnown &&
            offer.Kind is PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard &&
            offer.Affordable == true).Value!;
        var buy = new PredictorPlannedShopAction(PredictorShopActionKind.Buy) { ExpectedOffer = card };
        var purchased = Preview(Route(captured), [buy]);
        Check(purchased.Shop!.AppliedActions == 1 && Fact(purchased, ".projectionDisposition").DisplayValue == "Applied",
            "The actual buy action was not applied.");
        var chosen = Fact(purchased, ".input.action");
        Check(chosen.Origin == PredictorKnowledgeOrigin.PlayerInput && chosen.DisplayValue == "Buy",
            "An authored shop choice lost PlayerInput origin.");
        var defaultLeave = Fact(purchased, ".input.action", PredictorShopActionKind.Leave, PredictorKnowledgeOrigin.ApprovedDefault);
        Check(Fact(purchased, ".projectionDisposition", PredictorShopActionKind.Leave, PredictorKnowledgeOrigin.ApprovedDefault).DisplayValue == "Applied" &&
            defaultLeave.Id != chosen.Id,
            "Automatic Leave has its own applied operation and approved-default provenance beside the authored purchase.");
        Check(Fact(purchased, ".read.slot." + card.Slot + ".identity").DisplayValue == card.Key!.Value.Serialized &&
            Fact(purchased, ".read.slot." + card.Slot + ".cost").DisplayValue == card.Cost.ToString(CultureInfo.InvariantCulture),
            "Shop read recording disagrees with the exact selected slot identity or cost.");
        var goldRead = Fact(purchased, ".read.gold.balance");
        var goldWrite = Fact(purchased, ".write.gold.balance");
        Check(goldRead.DisplayValue == "1000" && goldWrite.DisplayValue == (1000 - card.Cost).ToString(CultureInfo.InvariantCulture) &&
            goldWrite.AllReadFactIds.Contains(goldRead.Id) && goldWrite.AllReadFactIds.Contains(chosen.Id),
            "A successful buy did not connect its actual gold read and authored action to the returned update.");
        Check(Fact(purchased, ".read.relics.shopDiscounts.membershipCard").DisplayValue == "False" &&
            Fact(purchased, ".read.relics.shopDiscounts.courier").DisplayValue == "False",
            "ShopCost's precise discount inputs were not recorded.");

        var removalOffer = baseline.Shop.Offers.Single(offer => offer.Kind == PredictorShopEntryKind.CardRemoval).Value!;
        var run = Route(captured);
        var target = run.Working.Deck.Select(run.Working.Card).First(card => card.Removable);
        var removed = Preview(run,
        [
            new(PredictorShopActionKind.BeginRemoval) { ExpectedOffer = removalOffer },
            new(PredictorShopActionKind.ChooseRemoval) { Card = new(target.Id, target.Key, target.FloorAdded) }
        ]);
        Check(removed.Shop!.AppliedActions == 2 &&
            Fact(removed, ".read.removal.target.identity", PredictorShopActionKind.ChooseRemoval).DisplayValue == target.Id + ":" + target.Key.Serialized + ":" + target.FloorAdded &&
            Fact(removed, ".read.removal.target.removable", PredictorShopActionKind.ChooseRemoval).DisplayValue == "True" &&
            Fact(removed, ".read.removal.quotedCost", PredictorShopActionKind.ChooseRemoval).DisplayValue == removalOffer.Cost.ToString(CultureInfo.InvariantCulture) &&
            Fact(removed, ".write.removal.selectedId", PredictorShopActionKind.ChooseRemoval).DisplayValue == target.Id.ToString(CultureInfo.InvariantCulture),
            "Actual removal did not record its specific target, qualification, and accepted quote.");

        var missingGold = PredictorRouteCauseEvidence.CreateExplicit("ShopCheckMissingGold", "capture:gold",
            PredictorCombatDomain.Gold, PredictorPendingReasonKind.MissingObservation,
            "Captured gold is not available.", ["gold.balance"]);
        var blockedGold = Preview(Route(captured, missingGold), [buy]);
        var maskedGold = Fact(blockedGold, ".read.gold.balance");
        var maskedAffordable = Fact(blockedGold, ".read.slot." + card.Slot + ".affordable");
        Check(blockedGold.Shop!.AppliedActions == 0 && maskedGold.DisplayValue == null && maskedAffordable.DisplayValue == null &&
            blockedGold.Diagnostics!.Snapshot.Explain(maskedAffordable.Id).Roots.Any(root => root.RootKey == missingGold.Identity),
            "Unknown gold leaked a witness affordability or lost the captured source root.");
        Check(Fact(blockedGold, ".read.slot." + card.Slot + ".identity").Status == PredictorKnowledgeStatus.Known &&
            Fact(blockedGold, ".read.slot." + card.Slot + ".cost").Status == PredictorKnowledgeStatus.Known,
            "Unknown gold hid independent selected-item identity or price.");

        var missingRng = PredictorRouteCauseEvidence.CreateExplicit("ShopCheckMissingRng", "capture:shopRng",
            PredictorCombatDomain.ShopsRng, PredictorPendingReasonKind.MissingObservation,
            "Captured shop cursor is not available.", ["rng.Shops.state"]);
        var blockedStock = Preview(Route(captured, missingRng), [buy]);
        foreach (string suffix in new[] { ".identity", ".bindingSnapshot", ".cost", ".baseCost" })
        {
            var fact = Fact(blockedStock, ".read.slot." + card.Slot + suffix);
            Check(fact.DisplayValue == null && blockedStock.Diagnostics!.Snapshot.Explain(fact.Id).Roots.Any(root =>
                root.RootKey == missingRng.Identity), "A masked shop read exposed source stock or lost its source cause.");
        }
        Check(!blockedStock.Diagnostics!.Results.Select(blockedStock.Diagnostics.Snapshot.Fact).Any(fact =>
            fact.PropertyKey.StartsWith("result.shop.action.", StringComparison.Ordinal) && fact.PropertyKey.Contains(".write.", StringComparison.Ordinal)),
            "A blocked stock choice invented an accepted state update.");
        UnselectedCardLeavesInitialRemovalKnown(captured);
        Console.WriteLine("PREDICTOR_SHOP_READ_RECORDING_PASS actual Buy/removal; exact slot/cost/gold reads; authored inputs; Gold/ShopRng masks; source roots; detached purity; partial reward population with proven initial target removal");
    }

    private static void UnselectedCardLeavesInitialRemovalKnown(PredictorRun captured)
    {
        var run = Route(captured, combatBeforeShop: true);
        var target = run.Working.Deck.Select(run.Working.Card).First(card => card.Removable);
        var initialIds = run.Working.Deck;
        PredictorRouteContinuation Forecast(ImmutableArray<PredictorPlannedShopAction> actions)
        {
            string before = JsonSerializer.Serialize(new { run.Working, run.Checkpoints, run.Request, run.Phase });
            // This property checks an unresolved authored card choice. Live
            // map policy settles the same ordinary reward with approved Skip.
            var result = run.PreviewRouteContinuation([new(CombatShopNode) { Shop = new() { Actions = actions } }], 2,
                scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.AuthoredChoices);
            Check(before == JsonSerializer.Serialize(new { run.Working, run.Checkpoints, run.Request, run.Phase }),
                "Partial-deck shop recording mutated the actual run.");
            Check(result.Nodes.Length == 2 && result.Nodes[1].Shop != null, "The real combat-to-shop route did not reach the merchant.");
            return result;
        }
        var baseline = Forecast([]);
        Check(baseline.Nodes[0].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
            baseline.Nodes[0].PendingCauses.Any(cause => cause.Code == "PendingCardChoice"),
            "The prior combat did not produce an actual unselected card-reward choice.");
        var removal = baseline.Nodes[1].Shop!.Offers.Single(offer => offer.Kind == PredictorShopEntryKind.CardRemoval).Value!;
        var forecast = Forecast([
            new(PredictorShopActionKind.BeginRemoval) { ExpectedOffer = removal },
            new(PredictorShopActionKind.ChooseRemoval) { Card = new(target.Id, target.Key, target.FloorAdded) }
        ]);
        var shop = forecast.Nodes[1];
        var snapshot = shop.Diagnostics!.Snapshot;
        var existence = Fact(shop, ".read.removal.candidateExistence", PredictorShopActionKind.BeginRemoval);
        var identity = Fact(shop, ".read.removal.target.identity", PredictorShopActionKind.ChooseRemoval);
        var removable = Fact(shop, ".read.removal.target.removable", PredictorShopActionKind.ChooseRemoval);
        foreach (var proof in new[] { existence, identity, removable })
            Check(proof.Status == PredictorKnowledgeStatus.Known && !string.IsNullOrWhiteSpace(proof.Derivation) &&
                proof.AllReadFactIds.Any(id => snapshot.Fact(id).Status == PredictorKnowledgeStatus.Partial) &&
                proof.UnresolvedReadFactIds.IsEmpty && snapshot.Explain(proof.Id).RequiredInputs.IsEmpty,
                "A proven shop-removal read over partial population facts lacks its specific proof or requests the unrelated reward choice.");
        Check(existence.DisplayValue == "True" && identity.DisplayValue == target.Id + ":" + target.Key.Serialized + ":" + target.FloorAdded &&
            removable.DisplayValue == "True" && shop.Shop!.AppliedActions == 2 &&
            Fact(shop, ".write.removal.selectedId", PredictorShopActionKind.ChooseRemoval).DisplayValue == target.Id.ToString(CultureInfo.InvariantCulture) &&
            !shop.Cards.Any(card => card.IsKnown && card.Value.Id == target.Id) &&
            initialIds.Where(id => id != target.Id).All(id => shop.Cards.Any(card => card.IsKnown && card.Value.Id == id)) &&
            forecast.PendingCauses.Any(cause => cause.Code == "PendingCardChoice"),
            "Removing the established initial card was blocked by an unrelated reward choice or altered another initial card.");

        // Compare against the actual concrete Skip branch through the same
        // encounter/shop consumers, retaining the exact original target ID.
        var concrete = Route(captured, combatBeforeShop: true);
        void Send(PredictorInput input) => Check(concrete.Submit(concrete.Request!, input) == PredictorInputResult.Accepted,
            "Concrete combat-to-shop control input was not accepted: " + input.GetType().Name);
        concrete.AdvanceRoute(); Send(concrete.DefaultCombatInput()!);
        while (PredictorRewardPickupPolicy.Plan(concrete).NextInput is { } pickup) Send(pickup);
        Send(new LeaveRewards()); concrete.AdvanceRoute(); Send(new OpenShopInventory());
        var entry = concrete.ShopEntries.Single(entry => entry.Kind == PredictorShopEntryKind.CardRemoval);
        Send(new BeginShopCardRemoval(entry.Id, concrete.ShopInventoryRevision)); Send(new ChooseShopRemovalCard(target.Id));
        Check(!concrete.Working.Deck.Contains(target.Id) &&
            initialIds.Where(id => id != target.Id).All(concrete.Working.Deck.Contains) &&
            Fact(shop, ".write.removal.removalsUsed", PredictorShopActionKind.ChooseRemoval).DisplayValue == concrete.Working.CardShopRemovalsUsed.ToString(CultureInfo.InvariantCulture),
            "The proven partial forecast disagrees with concrete removal of the same original card.");
    }
}
