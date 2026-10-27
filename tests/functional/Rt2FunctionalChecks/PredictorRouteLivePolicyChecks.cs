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

    private static PredictorRun Route(PredictorRun captured, string relic, bool eventNode = false)
    {
        const int act = 2;
        var types = new[] { MapPointType.Ancient, eventNode ? MapPointType.Unknown : MapPointType.Treasure,
            MapPointType.Monster, MapPointType.Treasure, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var source = captured.Checkpoints[0].State;
        var bag = new PredictorRelicBag(false, [], [new("Rare", [R(relic), R("GORGET")])], []);
        var state = source with
        {
            OpeningCompleted = true, Position = new(act, 1, "live-policy:entry"), Relics = [], GoldBudget = 1000,
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
            VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0), SharedBag = bag,
            Acts = source.Acts.Select(a => a.Act == act ? a with
            {
                RawEvents = [new("EVENT", "HUNGRY_FOR_MUSHROOMS")], EventCursor = 0,
                NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0
            } : a).ToImmutableArray()
        };
        var run = new PredictorRun(captured.Context with { Live = null, Operation = null }, state);
        run.SetRoute(act, points.Select(point => point.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
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
