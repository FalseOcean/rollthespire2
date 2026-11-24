using System.Collections.Immutable;
using System.Text.Json;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorRouteTreasureChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static ModelKey K(string entry) => new("RELIC", entry);
    private static PredictorPlannedTreasureAction Open() => new(PredictorTreasureActionKind.Open);
    private static PredictorPlannedTreasureAction Leave() => new(PredictorTreasureActionKind.Leave);
    private static PredictorPlannedTreasureAction Take(string relic) => new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = K(relic) };
    private static PredictorPlannedNode Plan(PredictorRun run, params PredictorPlannedTreasureAction[] actions)
        => new(run.Working.Position.NodeId) { Treasure = new() { Actions = actions.ToImmutableArray() } };
    private static void Select(PredictorRun run, PredictorInput input)
        => Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Treasure fixture input rejected: " + input);

    internal static void Run(PredictorRun captured)
    {
        OpeningAndSkipping(captured);
        AuthoredPickupAutoCompletes(captured);
        ObtainAndAcceptedPrefix(captured);
        SilverCrucibleAndSpoils(captured);
        InheritedDependencies(captured);
        LiveMapDefaults(captured);
        Console.WriteLine("RUNTIME_ROUTE_TREASURE_PASS native gold RNG; manual opening/skipping; automatic authored closure; source gold hooks; obtain child/return; accepted prefix; SilverCrucible; SpoilsMap; detached replay; independent prior facts");
    }

    // Controlled shared-bag contents select a particular obtain branch while
    // retaining the native rarity/gold streams and existing source settlement.
    private static PredictorRun Fixture(PredictorRun captured, string relic = "GORGET", PredictorState? supplied = null,
        bool previousCombat = false, string encounter = "FUZZY_WURM_CRAWLER_WEAK")
    {
        var types = previousCombat ? new[] { MapPointType.Ancient, MapPointType.Monster, MapPointType.Treasure, MapPointType.Boss } :
            new[] { MapPointType.Ancient, MapPointType.Treasure, MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var state = supplied ?? captured.Checkpoints[0].State;
        var bag = new PredictorRelicBag(true, new[] { "Common", "Uncommon", "Rare" }.Select(r => new PredictorBagEntry(K(relic), r)).ToImmutableArray(),
            new[] { "Common", "Uncommon", "Rare" }.Select(r => new PredictorBagBucket(r, [K(relic)])).ToImmutableArray(), []);
        state = state with
        {
            OpeningCompleted = true, Position = new(2, 1, "route-treasure:entry"),
            Maps = [new(2, captured.RouteMap(2) with { Points = points }, [points[0].Position])],
            Visits = [new(2, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
            PersonalBag = state.PersonalBag.Remove(K("GORGET")), SharedBag = bag,
            Acts = state.Acts.Select(a => a.Act == 2 ? a with
                { NormalEncounters = [new("ENCOUNTER", encounter)], NormalCursor = 0 } : a).ToImmutableArray()
        };
        var run = new PredictorRun(captured.Context, state);
        run.SetRoute(2, points.Select(p => p.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static PredictorRouteContinuation Pure(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans, int count = 1,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request;
        var result = run.PreviewRouteContinuation(plans, count, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) && request == run.Request,
            "Treasure preview leaves caller state, checkpoints and request unchanged");
        Check(result.Nodes.SelectMany(n => n.Treasure?.RequiredChoices ?? []).All(c => c.Cards.All(card => card.Id == 0) &&
            c.Bundles.All(b => b.All(card => card.Id == 0)) && c.Rewards.Where(r => r.Value != null).All(r => r.Value!.Id == 0)),
            "Treasure child observations do not export generated action IDs");
        return result;
    }

    private static void LiveMapDefaults(PredictorRun captured)
    {
        var run = Fixture(captured);
        var result = Pure(run, [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(result.Nodes.Length == 2 && result.Nodes[0].Advanced &&
            result.Nodes[0].Treasure is { Opened: true, RelicTaken: true, AppliedActions: 0 } &&
            result.Nodes[1].Room == PredictorRoomKind.Monster,
            "Live map defaults open and claim a fixed chest relic without authoring concrete actions");
        var explicitLeave = Pure(run, [Plan(run, Leave())], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        Check(!explicitLeave.Treasure!.Opened && !explicitLeave.Treasure.RelicTaken,
            "Explicit authored leave takes precedence over default treasure pickup");
        var child = Pure(Fixture(captured, "SCROLL_BOXES"), [], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(!child.Advanced && child.Treasure is { Opened: true, RelicTaken: true } &&
            child.Treasure.RequiredChoices is [{ Phase: PredictorPhase.ChooseBundle }],
            "Default relic claim keeps obtain bundle input pending without inventing a child choice");
    }

    private static void OpeningAndSkipping(PredictorRun captured)
    {
        var run = Fixture(captured);
        var unopened = Pure(run, []).Nodes.Single();
        Check(unopened.Treasure is { Opened: false, GenerationAllowed: true, OfferedRelic.Entry: "GORGET" } && !unopened.Advanced &&
            unopened.PendingCauses.Any(c => c.Code == "TreasureOpenOrLeaveChoice"), "Treasure has explicit open/leave choice");
        var before = run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards);
        var native = new Rng(new SerializableRng { counter = before.Calls, state0 = before.S0, state1 = before.S1, state2 = before.S2, state3 = before.S3 });
        int amount = native.NextInt(42, 53);
        if (run.Context.Ascension >= 3) amount = (int)(amount * .75);
        Check(unopened.Treasure!.NormalGold == amount && unopened.Treasure.SpoilsGold == 0, "Chest source gold agrees with native Rewards RNG");
        var skipClosed = Pure(run, [Plan(run, Leave())], 2);
        Check(skipClosed.Nodes[0].Advanced && !skipClosed.Nodes[0].Treasure!.Opened &&
            skipClosed.Nodes[0].Treasure!.GoldBalance == run.Working.GoldBudget && skipClosed.Nodes[1].Room == PredictorRoomKind.Monster,
            "Leaving unopened chest continues without gold");
        var skipRelic = Pure(run, [Plan(run, Open(), Leave())], 2);
        var concrete = Fixture(captured);
        Select(concrete, new OpenTreasure()); Select(concrete, new LeaveTreasure());
        Check(skipRelic.Nodes[0].Advanced && skipRelic.Nodes[0].Treasure is { Opened: true, RelicTaken: false } &&
            skipRelic.Nodes[0].Treasure!.GoldBalance == concrete.Working.GoldBudget &&
            concrete.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls == before.Calls + 1,
            "Opening pays mandatory gold once; skipping relic keeps it");
        var ecto = Fixture(captured, supplied: captured.Checkpoints[0].State with
            { Relics = captured.Checkpoints[0].State.Relics.Add(new(captured.Checkpoints[0].State.NextInstanceId, K("ECTOPLASM"), 0)),
                NextInstanceId = captured.Checkpoints[0].State.NextInstanceId + 1 });
        var deniedGold = Pure(ecto, [Plan(ecto, Open(), Leave())]).Nodes[0].Treasure!;
        Check(deniedGold.NormalGold > 0 && deniedGold.GoldBalance == ecto.Working.GoldBudget,
            "Source amount remains distinct from Ectoplasm's actual gold receipt");
    }

    private static void ObtainAndAcceptedPrefix(PredictorRun captured)
    {
        var run = Fixture(captured, "SCROLL_BOXES");
        var partialPlan = Plan(run, Open(), Take("SCROLL_BOXES"));
        var partial = Pure(run, [partialPlan]).Nodes[0];
        var child = partial.Treasure!.RequiredChoices.Single();
        Check(child.Phase == PredictorPhase.ChooseBundle && child.Bundles.Length == 2 && !partial.Advanced,
            "Treasure pickup exposes its shared obtain child");
        var livePartial = Pure(run, [partialPlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(!livePartial.Advanced && livePartial.Treasure!.RequiredChoices.Single().Phase == PredictorPhase.ChooseBundle,
            "Automatic authored closure never answers the required obtain bundle choice");
        var choose = new PredictorPlannedTreasureAction(PredictorTreasureActionKind.Obtain)
        { Obtain = new(PredictorObtainChildAction.ChooseBundle, K("SCROLL_BOXES")) { ExpectedBundles = child.Bundles, BundleIndex = 1 } };
        var full = Plan(run, Open(), Take("SCROLL_BOXES"), choose);
        var projected = Pure(run, [full], 2);
        Check(projected.Nodes[0].Advanced && projected.Nodes[0].Treasure!.RelicTaken && projected.Nodes[1].Room == PredictorRoomKind.Monster &&
            projected.Nodes[0].Treasure!.AppliedActions == 3,
            "Shared obtain child completes the chest without an authored leave");
        var legacy = Pure(run, [Plan(run, Open(), Take("SCROLL_BOXES"), choose, Leave())], 2);
        Check(legacy.Nodes[0].Advanced && legacy.Nodes[0].Treasure!.AppliedActions == 4 &&
            projected.Nodes[0].Cards.SequenceEqual(legacy.Nodes[0].Cards),
            "An older explicit leave remains readable with the same obtain settlement");
        Select(run, new OpenTreasure());
        Check(Pure(run, [full]).Nodes[0].Advanced, "Accepted opening is replayed once");
        Select(run, new TakeTreasureRelic(run.TreasureRelic!.Id));
        Check(Pure(run, [full]).Nodes[0].Advanced, "An active obtain child resumes after accepted relic pickup");
        Check(Pure(run, [Plan(run, Open(), Take("GORGET"), choose)]).Nodes[0].PendingCauses.Any(c =>
            c.Code is "PlannedTreasureChoiceChanged" or "PlannedTreasureAcceptedPrefixChanged"),
            "Changed relic intent remains pending instead of binding the active child");
        Select(run, new ChooseScrollBoxBundle(1)); Select(run, new LeaveTreasure());
        Check(projected.Nodes[0].Cards.Select(c => c.Value with { Id = 0 }).SequenceEqual(run.Working.Deck.Select(run.Working.Card).Select(c => c with { Id = 0 })),
            "Projected treasure child equals concrete ordered source settlement");
    }

    private static void AuthoredPickupAutoCompletes(PredictorRun captured)
    {
        foreach (var policy in new[] { PredictorRoutePreviewPolicy.AuthoredChoices, PredictorRoutePreviewPolicy.LiveMapDefaults })
        {
            var run = Fixture(captured);
            var plan = Plan(run, Open(), Take("GORGET"));
            var result = Pure(run, [plan], 2, policy);
            var chest = result.Nodes[0];
            Check(result.Nodes.Length == 2 && chest.Advanced &&
                chest.Treasure is { Opened: true, RelicTaken: true, AppliedActions: 2, RequiredChoices.IsEmpty: true } &&
                result.Nodes[1].Inventory!.Relics.Any(r => r.IsKnown && r.Value.Key == K("GORGET")),
                "Authored pickup automatically continues with its obtained relic in the next combat inventory");
            var concrete = Fixture(captured);
            Select(concrete, new OpenTreasure());
            Select(concrete, new TakeTreasureRelic(concrete.TreasureRelic!.Id));
            Select(concrete, new LeaveTreasure());
            Check(chest.Treasure!.GoldBalance == concrete.Working.GoldBudget,
                "Automatic closure preserves native opening gold settlement");

            var skipped = Pure(run, [Plan(run, Open(), Leave())], 2, policy);
            Check(skipped.Nodes.Length == 2 && skipped.Nodes[0].Advanced &&
                skipped.Nodes[0].Treasure is { Opened: true, RelicTaken: false } &&
                skipped.Nodes[1].Inventory!.Relics.All(r => r.Value.Key != K("GORGET")) &&
                skipped.Nodes[0].Treasure!.GoldBalance == chest.Treasure.GoldBalance,
                "Changing a completed pickup to skip preserves opening gold and revokes only the relic receipt");

            Select(run, new OpenTreasure());
            Select(run, new TakeTreasureRelic(run.TreasureRelic!.Id));
            var accepted = Pure(run, [plan], 2, policy);
            Check(accepted.Nodes.Length == 2 && accepted.Nodes[0].Advanced &&
                accepted.Nodes[0].Treasure!.AppliedActions == 2 &&
                accepted.Nodes[0].Treasure!.GoldBalance == concrete.Working.GoldBudget,
                "An accepted completed pickup prefix closes without opening or taking twice");
        }
    }

    private static void SilverCrucibleAndSpoils(PredictorRun captured)
    {
        var state = captured.Checkpoints[0].State;
        state = PredictorObtainSources.AddCard(captured.Context, state, new ModelKey("CARD", "SPOILS_MAP"));
        long first = state.Deck[^1];
        state = PredictorObtainSources.AddCard(captured.Context, state, new ModelKey("CARD", "SPOILS_MAP"));
        long second = state.Deck[^1];
        state = state.WithCard(state.Card(first) with { SpoilsActIndex = 1, SpoilsCoord = new(3, 1) });
        var spoils = Fixture(captured, supplied: state);
        var knowledge = Pure(spoils, [Plan(spoils, Open(), Leave())]).Nodes[0];
        Check(knowledge.Advanced && knowledge.Treasure!.SpoilsGold == 1200 && knowledge.Cards.All(c => c.Value.Id != first && c.Value.Id != second),
            "Opening quest-marked chest pays/removes all SpoilsMap copies before optional relic pickup");
        var silverState = state with { Relics = state.Relics.Add(new(state.NextInstanceId, K("SILVER_CRUCIBLE"), 0)), NextInstanceId = state.NextInstanceId + 1 };
        var silver = Fixture(captured, supplied: silverState);
        var suppressed = Pure(silver, [Plan(silver, Open(), Leave())]).Nodes[0];
        Check(suppressed.Treasure is { GenerationAllowed: false, OfferedRelic: null, NormalGold: 0, SpoilsGold: 0 } &&
            suppressed.Treasure.GoldBalance == silver.Working.GoldBudget && suppressed.Cards.Any(c => c.Value.Id == first),
            "First SilverCrucible chest suppresses normal relic/gold and Spoils completion together");
        int calls = silver.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls;
        Select(silver, new OpenTreasure());
        Check(silver.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls == calls,
            "Suppressed SilverCrucible chest consumes no normal-gold draw");
        var used = silverState with { Relics = silverState.Relics.Select(r => r.Key.Entry == "SILVER_CRUCIBLE" ? r with { TreasureRoomsEntered = 1 } : r).ToImmutableArray() };
        var next = Fixture(captured, supplied: used);
        Check(Pure(next, [Plan(next, Open(), Leave())]).Nodes[0].Treasure is { GenerationAllowed: true, SpoilsGold: 1200 },
            "Second SilverCrucible chest restores source rewards");
    }

    private static void InheritedDependencies(PredictorRun captured)
    {
        var state = captured.Checkpoints[0].State;
        state = state with { Relics = state.Relics.Add(new(state.NextInstanceId, K("BONE_TEA"), 0)), NextInstanceId = state.NextInstanceId + 1 };
        var run = Fixture(captured, supplied: state, previousCombat: true, encounter: "FABRICATOR_NORMAL");
        var prior = new PredictorPlannedNode(run.Working.Position.NodeId) { LeaveRewards = true };
        var result = Pure(run, [prior], 2);
        Check(result.Nodes.Length == 2 && result.Nodes[1].Room == PredictorRoomKind.Treasure &&
            result.Nodes[1].Treasure is { GenerationAllowed: true, OfferedRelic.Entry: "GORGET", NormalGold: > 0 } &&
            result.Nodes[1].PendingCauses.Any(c => c.Code == "EnemyCreationCount"),
            "Missing prior Niche fact with BoneTea inventory does not hide independent chest eligibility, relic or source gold");

        var gremlin = Fixture(captured, previousCombat: true, encounter: "GREMLIN_MERC_NORMAL");
        var gremlinPlan = new PredictorPlannedNode(gremlin.Working.Position.NodeId)
        { LeaveRewards = true, Combat = new() { GremlinGold = new(true, true, null) } };
        var missingBudget = Pure(gremlin, [gremlinPlan], 2);
        var chest = missingBudget.Nodes[1];
        var chestPlan = new PredictorPlannedNode(chest.Position.NodeId)
        { Treasure = new() { Actions = [Open(), Leave()] } };
        var continued = Pure(gremlin, [gremlinPlan, chestPlan], 2).Nodes[1];
        Check(continued.Advanced && continued.Treasure is { NormalGold: > 0, GoldBalance: null, OfferedRelic.Entry: "GORGET" } &&
            continued.Treasure.NormalGoldPendingCauses.IsEmpty && continued.Treasure.GoldPendingCauses.Any(c => c.Code == "StolenGoldLoss"),
            "Unknown prior gold balance does not block opening, known source payout, relic or explicit leave");
    }
}
