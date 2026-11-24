using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorRouteJourneyChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Route journey: " + message); }
    private static void Submit(PredictorRun run, PredictorInput input)
        => Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "input " + input);
    private static ModelKey K(string entry) => new("RELIC", entry);
    private static PredictorCard Display(PredictorCard card) => card with { Id = 0 };

    internal static void Run(PredictorRun captured)
    {
        // Fixed geometry isolates composition. Actual source catalogs, RNG,
        // event settlement, chest gold and merchant generation remain intact.
        var types = new[] { MapPointType.Ancient, MapPointType.Monster, MapPointType.Unknown,
            MapPointType.Treasure, MapPointType.Shop, MapPointType.RestSite, MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var bag = new PredictorRelicBag(true,
            new[] { "Common", "Uncommon", "Rare" }.Select(r => new PredictorBagEntry(K("OLD_COIN"), r)).ToImmutableArray(),
            new[] { "Common", "Uncommon", "Rare" }.Select(r => new PredictorBagBucket(r, [K("OLD_COIN")])).ToImmutableArray(), []);
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [], GoldBudget = 1000, UnknownOdds = new(0, -1, 0, 0),
            Position = new(1, 1, "journey:entry"), SharedBag = bag,
            Maps = [new(1, captured.RouteMap(1) with { Points = points }, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = captured.Working.Acts.SetItem(0, captured.Working.Acts[0] with
            { RawEvents = [new("EVENT", "MORPHIC_GROVE")], EventCursor = 0,
                NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 })
        };
        PredictorRun Create()
        {
            var run = new PredictorRun(captured.Context with { NumberOfRuns = 1 }, state) { AutomaticRewardPickup = false };
            run.SetRoute(1, points.Select(p => p.Position).ToImmutableArray());
            return run;
        }
        var source = Create();
        var concrete = Create();
        var plans = ImmutableArray.CreateBuilder<PredictorPlannedNode>();
        concrete.AdvanceRoute();
        plans.Add(new(concrete.Working.Position.NodeId) { LeaveRewards = true });
        Submit(concrete, concrete.DefaultCombatInput()!);
        Submit(concrete, new LeaveRewards());
        concrete.AdvanceRoute();

        var occurrence = concrete.ObserveEventOccurrence()!;
        var candidate = occurrence.Candidates.First(c => !c.IsCycleFallback);
        var select = new ChooseEventOccurrence(candidate.Event, candidate.QueueEvent, candidate.RawOrdinal, occurrence.Cursor, candidate.Cursor);
        Submit(concrete, select);
        var group = concrete.EventOptions.Single(o => o.Key == "GROUP");
        Submit(concrete, new ChooseEventOption("GROUP"));
        var targets = concrete.EventCardCandidates.Take(2).Select(concrete.Working.Card).ToArray();
        plans.Add(new(concrete.Working.Position.NodeId) { Event = new(candidate.Event)
        { Occurrence = select, Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = group },
            new(PredictorEventActionKind.Cards) { DeckCards = targets.Select(c => new PredictorPlannedCard(c.Id, c.Key, c.FloorAdded)).ToImmutableArray() }] } });
        Submit(concrete, new SelectEventCards(targets.Select(c => c.Id).ToImmutableArray()));
        Check(concrete.Phase == PredictorPhase.Complete && concrete.Working.GoldBudget == 0, "Morphic closes and clears gold");
        concrete.AdvanceRoute();

        plans.Add(new(concrete.Working.Position.NodeId) { Treasure = new() { Actions =
            [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = K("OLD_COIN") },
                new(PredictorTreasureActionKind.Leave)] } });
        Submit(concrete, new OpenTreasure());
        Submit(concrete, new TakeTreasureRelic(concrete.TreasureRelic!.Id));
        Submit(concrete, new LeaveTreasure());
        int afterChest = concrete.Working.GoldBudget;
        Check(afterChest > 300, "chest gold precedes Old Coin gain");
        concrete.AdvanceRoute();

        int slot = Enumerable.Range(0, concrete.ShopEntries.Length).First(i =>
            concrete.ShopEntries[i].Kind == PredictorShopEntryKind.CharacterCard && concrete.ShopOffers[i].Cost > 53 &&
            concrete.ShopEntries[i].Card is { } c && c.UpgradeLevel < c.MaxUpgradeLevel);
        var entry = concrete.ShopEntries[slot];
        var offer = new PredictorShopOfferSnapshot(slot, entry.Revision, entry.Kind, entry.Key, Display(entry.Card!),
            entry.BaseCost, concrete.ShopOffers[slot].Cost, entry.IsOnSale, entry.Stocked)
        { StockRevisionIsOccurrence = true };
        plans.Add(new(concrete.Working.Position.NodeId) { Shop = new() { Actions =
            [new(PredictorShopActionKind.Buy) { ExpectedOffer = offer }, new(PredictorShopActionKind.Leave)] } });
        Submit(concrete, new OpenShopInventory());
        Submit(concrete, new BuyShopEntry(entry.Id, concrete.ShopInventoryRevision));
        var bought = concrete.Working.Card(concrete.Working.Deck[^1]);
        Submit(concrete, new CloseShopInventory());
        Submit(concrete, new LeaveShop());
        int afterShop = concrete.Working.GoldBudget;
        concrete.AdvanceRoute();

        plans.Add(new(concrete.Working.Position.NodeId) { RestActions = [new(PredictorRestAction.Smith)
            { SmithCard = new(bought.Id, bought.Key, bought.FloorAdded) }], LeaveRest = true });
        Submit(concrete, new SelectRestAction(PredictorRestAction.Smith));
        Submit(concrete, new SelectSmithCard(bought.Id));
        Submit(concrete, new LeaveRestSite());
        concrete.AdvanceRoute();
        plans.Add(new(concrete.Working.Position.NodeId) { LeaveRewards = true });
        Submit(concrete, concrete.DefaultCombatInput()!);
        var finalRewards = concrete.Rewards;
        Submit(concrete, new LeaveRewards());

        foreach (var plan in plans) source.SetPlannedNode(plan);
        string saved = PredictorRunStore.Serialize(source);
        var forecast = source.PreviewRouteContinuation(source.PlannedNodes, 6, scope: PredictorRoutePreviewScope.AllModeledNodes);
        Check(forecast.Nodes.Length == 6 && forecast.Nodes.All(n => n.Advanced) && forecast.PendingCauses.IsEmpty,
            "all six distinct node transactions compose without missing inputs: " + string.Join(',', forecast.PendingCauses.Select(c => c.Code)));
        Check(forecast.Nodes[2].Treasure!.GoldBalance == afterChest && forecast.Nodes[3].Shop!.GoldBalance == afterShop,
            "Morphic zero, chest/Old Coin income and merchant expense cross node owners exactly");
        Check(forecast.Nodes[^1].Cards.Select(c => Display(c.Value)).SequenceEqual(concrete.Working.Deck.Select(concrete.Working.Card).Select(Display)),
            "event transformations, bought card and smith target reach the final combat");
        Check(forecast.Nodes[^1].Rewards.SelectMany(r => r.Value?.Cards ?? []).Select(Display).SequenceEqual(finalRewards.SelectMany(r => r.Cards).Select(Display)),
            "final reward cards match the same explicitly executed sequence");
        Check(PredictorRunStore.Serialize(source) == saved, "multi-node preview leaves input run/checkpoints/plans unchanged");

        // A prior change keeps future intent but forces its actual consumer to
        // reconsider affordability instead of reusing the previous prediction.
        var chest = source.PlannedNodes[2];
        source.SetPlannedNode(chest with { Treasure = new() { Actions =
            [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.Leave)] } });
        var changed = source.PreviewRouteContinuation(source.PlannedNodes, 6, scope: PredictorRoutePreviewScope.AllModeledNodes);
        Check(changed.Nodes.Length == 4 && !changed.Nodes[^1].Advanced &&
            changed.PendingCauses.Any(c => c.Code == "PlannedShopInsufficientGold") && source.PlannedNodes[4] == plans[4],
            "skipping Old Coin recomputes later affordability, retains later smith intent, and does not reuse stale success");
        source.SetPlannedNode(chest);
        var loaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(source), source.Context.CatalogFingerprint, source.Context.UnlockFingerprint);
        Check(loaded.PreviewRouteContinuation(loaded.PlannedNodes, 6, scope: PredictorRoutePreviewScope.AllModeledNodes).Nodes.All(n => n.Advanced),
            "restoring original plan and reloading reproduces the complete chain");
        Console.WriteLine("RUNTIME_ROUTE_JOURNEY_PASS combat-event-treasure-shop-rest-combat; source-equivalent final deck/rewards; changed upstream income invalidates purchase only; saved future plans; detached purity");
    }
}
