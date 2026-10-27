using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

// Native captured catalogs, actual route entry and room commands. These checks
// compare computation evidence with the same concrete consumer, not a mirror
// of the rest/gold formula or private recorder invocation.
internal static class PredictorRestTreasureReadRecordingChecks
{
    private const string Node = "map:1:1:3:2";
    private static readonly ModelKey Gorget = new("RELIC", "GORGET");
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static string Scalar(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static PredictorRun Route(PredictorRun captured, MapPointType room,
        string[]? relics = null, bool night = false, PredictorCombatPendingCause? missing = null)
    {
        var context = captured.Context with
        {
            NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns),
            Modifiers = night ? [new(new("MODIFIER", "NIGHT_TERRORS"))] : [],
            Live = missing == null ? null : new(1, true, [missing])
        };
        var types = new[] { MapPointType.Ancient, room, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
        var map = captured.RouteMap(1) with { Points = points };
        long id = captured.Working.NextInstanceId;
        var state = captured.Working with
        {
            OpeningCompleted = true, GoldBudget = 100,
            Relics = (relics ?? []).Select(key => new PredictorRelic(id++, new("RELIC", key), 1)).ToImmutableArray(),
            PotionSlots = [null, null],
            SharedBag = new(false, [], [new("Common", [Gorget]), new("Uncommon", [Gorget]), new("Rare", [Gorget])], []),
            Position = new(1, 1, "rest-treasure-read:entry"), Maps = [new(1, map, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)]
        };
        state = state with { NextInstanceId = id };
        var run = new PredictorRun(context, state);
        run.SetRoute(1, points.Select(point => point.Position).ToImmutableArray());
        return run;
    }

    private static PredictorRouteNodeKnowledge Preview(PredictorRun run, PredictorPlannedNode plan)
    {
        string before = JsonSerializer.Serialize(new { run.Working, run.Checkpoints, run.Request, run.Phase });
        var result = run.PreviewRouteContinuation([plan], 1, scope: PredictorRoutePreviewScope.AllModeledNodes);
        Check(before == JsonSerializer.Serialize(new { run.Working, run.Checkpoints, run.Request, run.Phase }),
            "Rest/treasure computation tracing mutated the caller run.");
        Check(result.Nodes.Length == 1 && result.Nodes[0].Diagnostics != null,
            "The actual room route did not publish diagnostics.");
        return result.Nodes[0];
    }
    private static PredictorKnowledgeFact[] Computation(PredictorRouteNodeKnowledge node, string room) =>
        node.Diagnostics!.Results.Select(node.Diagnostics.Snapshot.Fact).Where(fact =>
            fact.PropertyKey.StartsWith(room + ".compute.", StringComparison.Ordinal)).ToArray();
    private static PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string room, string suffix) =>
        Computation(node, room).Single(fact => fact.PropertyKey.EndsWith(suffix, StringComparison.Ordinal));
    private static void Send(PredictorRun run, PredictorInput input) =>
        Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted,
            "Concrete room input was rejected: " + input.GetType().Name);
    private static void AfterUses(PredictorRouteNodeKnowledge node, string canonical, PredictorKnowledgeFact write)
    {
        var after = node.Diagnostics!.After.Select(node.Diagnostics.Snapshot.Fact).Single(f => f.PropertyKey == canonical);
        Check(after.AllReadFactIds.Contains(write.Id), "After frame lost its actual computation write for " + canonical);
    }

    internal static void Run(PredictorRun captured)
    {
        HealReadsOnlyItsActualBranch(captured);
        SmithRetainsTheActualTargetAndWrite(captured);
        TreasureGeneratedOpenedAndObtained(captured);
        TreasureSettlesTheActualSpoilsMarker(captured);
        SuppressedTreasureSkipsRandomReads(captured);
        UnknownGoldDrawWithEctoplasmHasSpecificInvariant(captured);
        Console.WriteLine("PREDICTOR_REST_TREASURE_READ_RECORDING_PASS actual route entry/commands; normal versus NightTerrors MaximumHP; fixed smith target; treasure rarity/bag/gold/obtain; suppressed draws; Ectoplasm invariant; canonical After links; detached purity");
    }

    private static void HealReadsOnlyItsActualBranch(PredictorRun captured)
    {
        var normal = Route(captured, MapPointType.RestSite, ["STONE_HUMIDIFIER", "REGAL_PILLOW"]);
        var plan = new PredictorPlannedNode(Node)
        { RestActions = [new(PredictorRestAction.Heal) { MaximumHp = 80 }], LeaveRest = true };
        var result = Preview(normal, plan);
        var concrete = Route(captured, MapPointType.RestSite, ["STONE_HUMIDIFIER", "REGAL_PILLOW"]);
        concrete.AdvanceRoute();
        var expected = concrete.RestHealOutcome(false);
        Send(concrete, new SelectRestAction(PredictorRestAction.Heal));
        Check(result.RestHeal == expected && result.AppliedRestActions == 1 &&
            !Computation(result, "rest").Any(f => f.PropertyKey.EndsWith(".maximumHp", StringComparison.Ordinal)),
            "Ordinary heal recorded an unconsumed MaximumHP input or disagreed with the actual nominal effect.");
        Check(Fact(result, "rest", ".write.heal.flatBonus").DisplayValue == Scalar(expected.FlatHealBonus) &&
            Fact(result, "rest", ".write.heal.maxHpDelta").DisplayValue == Scalar(expected.MaxHpDelta),
            "Actual normal heal outputs lost their concrete bonus or maximum-health delta.");
        var health = Fact(result, "rest", ".write.heal.maxHpDelta");
        Check(health.AllReadFactIds.Contains(Fact(result, "rest", ".read.heal.stoneHumidifier.count").Id),
            "The maximum-health delta did not cite its actual Stone Humidifier read.");

        var nightRun = Route(captured, MapPointType.RestSite, ["STONE_HUMIDIFIER"], night: true);
        var night = Preview(nightRun, plan);
        var input = Fact(night, "rest", ".input.maximumHp");
        var read = Fact(night, "rest", ".read.heal.maximumHp");
        var overrideAmount = Fact(night, "rest", ".write.heal.nominalOverride");
        var nightConcrete = Route(captured, MapPointType.RestSite, ["STONE_HUMIDIFIER"], night: true);
        nightConcrete.AdvanceRoute();
        var nightExpected = nightConcrete.RestHealOutcome(false, 80);
        Send(nightConcrete, new SelectRestAction(PredictorRestAction.Heal));
        Send(nightConcrete, new ProvideRestMaxHpFact(80));
        Check(night.RestHeal == nightExpected && input.Origin == PredictorKnowledgeOrigin.PlayerInput &&
            input.DisplayValue == "80" && read.AllReadFactIds.Contains(input.Id) &&
            overrideAmount.AllReadFactIds.Contains(read.Id) && overrideAmount.DisplayValue == "80",
            "NightTerrors lost the actual supplied MaximumHP→effect dependency.");
        var missing = Preview(nightRun, plan with { RestActions = [new(PredictorRestAction.Heal)] });
        Check(missing.RestHeal == null && missing.PendingCauses.Any(c => c.Code == "RestMaximumHp") &&
            !Computation(missing, "rest").Any(f => f.PropertyKey.Contains(".write.heal.", StringComparison.Ordinal)),
            "Missing NightTerrors MaximumHP produced an invented heal effect.");
    }

    private static void SmithRetainsTheActualTargetAndWrite(PredictorRun captured)
    {
        var run = Route(captured, MapPointType.RestSite);
        var target = run.Working.Deck.Select(run.Working.Card).First(card => card.UpgradeLevel < card.MaxUpgradeLevel);
        var node = Preview(run, new(Node) { RestActions = [new(PredictorRestAction.Smith)
        { SmithCard = new(target.Id, target.Key, target.FloorAdded) }], LeaveRest = true });
        var concrete = Route(captured, MapPointType.RestSite);
        concrete.AdvanceRoute(); Send(concrete, new SelectRestAction(PredictorRestAction.Smith));
        Send(concrete, new SelectSmithCard(target.Id));
        // The route binding and the actual SelectSmithCard consumer are two
        // distinct operations over the same target; retain both occurrences.
        var identity = Computation(node, "rest").Last(f => f.PropertyKey.EndsWith(
            ".read.target." + target.Id + ".identity", StringComparison.Ordinal));
        Check(identity.DisplayValue == target.Id + ":" + target.Key.Serialized + ":" + target.FloorAdded &&
            Fact(node, "rest", ".read.target." + target.Id + ".upgradeLevel").DisplayValue == Scalar(target.UpgradeLevel) &&
            node.Cards.Single(card => card.IsKnown && card.Value.Id == target.Id).Value.UpgradeLevel == concrete.Working.Card(target.Id).UpgradeLevel,
            "Smith evidence rebound the original target or disagreed with the actual upgrade.");
        AfterUses(node, "deck.Upgradable.count", Fact(node, "rest", ".write.deck.Upgradable.count"));
    }

    private static PredictorPlannedNode TreasurePlan(bool take = true) => new(Node)
    { Treasure = new() { Actions = take ? [new(PredictorTreasureActionKind.Open),
        new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = Gorget }, new(PredictorTreasureActionKind.Leave)] :
        [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.Leave)] } };

    private static void TreasureGeneratedOpenedAndObtained(PredictorRun captured)
    {
        var run = Route(captured, MapPointType.Treasure);
        var node = Preview(run, TreasurePlan());
        var concrete = Route(captured, MapPointType.Treasure);
        concrete.AdvanceRoute();
        Check(concrete.TreasureRelic?.Key == Gorget, "Controlled actual treasure bag did not offer Gorget.");
        Send(concrete, new OpenTreasure());
        int openedBalance = concrete.Working.GoldBudget;
        Send(concrete, new TakeTreasureRelic(concrete.TreasureRelic!.Id));
        Send(concrete, new LeaveTreasure());
        var relic = Fact(node, "treasure", ".write.relic.model");
        Check(relic.DisplayValue == Gorget.Serialized && node.Treasure is { Advanced: true } &&
            Fact(node, "treasure", ".write.gold.balance.after").DisplayValue == Scalar(openedBalance) &&
            Fact(node, "treasure", ".write.obtain.invoked").DisplayValue == "True",
            "Treasure computation evidence disagrees with its actual generated/opened/taken result.");
        Check(relic.AllReadFactIds.Contains(Fact(node, "treasure", ".read.bags.shared").Id) &&
            relic.AllReadFactIds.Contains(Fact(node, "treasure", ".read.rng.TreasureRoomRelics.state").Id),
            "Treasure pull omitted its actual shared-bag or rarity cursor dependency.");
        foreach (var stream in new[] { PredictorStream.TreasureRoomRelics, PredictorStream.Rewards })
        {
            var write = Fact(node, "treasure", ".write.rng." + stream + ".calls");
            Check(write.DisplayValue == Scalar(concrete.Working.Streams.Single(s => s.Stream == stream).Calls),
                "Treasure returned cursor differs from the concrete consumer: " + stream);
            AfterUses(node, "rng." + stream + ".calls", write);
        }
        AfterUses(node, "gold.balance", Fact(node, "treasure", ".write.gold.balance.afterSpoils"));
        Check(!Computation(node, "treasure").Any(f => f.PropertyKey.EndsWith("entryCoverage", StringComparison.Ordinal)),
            "An entry actually instrumented in this forecast was mislabeled as absent.");
    }

    private static void SuppressedTreasureSkipsRandomReads(PredictorRun captured)
    {
        var node = Preview(Route(captured, MapPointType.Treasure, ["SILVER_CRUCIBLE"]), TreasurePlan(take: false));
        var concrete = Route(captured, MapPointType.Treasure, ["SILVER_CRUCIBLE"]);
        concrete.AdvanceRoute(); Send(concrete, new OpenTreasure());
        Check(Fact(node, "treasure", ".write.generationAllowed").DisplayValue == "False" &&
            Fact(node, "treasure", ".write.relic.model").DisplayValue == "none" &&
            Fact(node, "treasure", ".write.normalGold").DisplayValue == "0" && concrete.Working.GoldBudget == 100 &&
            !Computation(node, "treasure").Any(f => f.PropertyKey.Contains(".read.rng.", StringComparison.Ordinal) ||
                f.PropertyKey.EndsWith(".read.bags.shared", StringComparison.Ordinal)),
            "Silver Crucible's skipped branch fabricated a random/bag read or payout.");
        AfterUses(node, "relics.silverCrucible.orderedTreasureRoomCounters",
            Fact(node, "treasure", ".write.silverCrucible.countersAfter"));
    }

    private static void TreasureSettlesTheActualSpoilsMarker(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog ?? throw new InvalidOperationException("Captured rest/treasure checks require the actual card catalog.");
        var prototype = catalog.CardDefinitions.Single(d => d.Prototype.Key.Entry == "SPOILS_MAP").Prototype;
        PredictorRun WithQuest()
        {
            var baseRun = Route(captured, MapPointType.Treasure);
            var card = prototype with { Id = baseRun.Working.NextInstanceId, FloorAdded = 1,
                SpoilsActIndex = 0, SpoilsCoord = new(3, 1) };
            var state = baseRun.Working with { Cards = baseRun.Working.Cards.Add(card),
                Deck = baseRun.Working.Deck.Add(card.Id), NextInstanceId = card.Id + 1 };
            var run = new PredictorRun(baseRun.Context, state);
            run.SetRoute(1, baseRun.Routes.Single().Points);
            return run;
        }
        var source = WithQuest();
        long id = source.Working.Deck[^1];
        var node = Preview(source, TreasurePlan(take: false));
        var concrete = WithQuest(); concrete.AdvanceRoute(); Send(concrete, new OpenTreasure());
        var amount = Fact(node, "treasure", ".write.spoilsGold");
        Check(amount.DisplayValue == "600" && amount.AllReadFactIds.Contains(
            Fact(node, "treasure", ".read.spoils.mapCount").Id) &&
            Fact(node, "treasure", ".read.spoils.questAtPoint").DisplayValue == "True" &&
            !concrete.Working.Deck.Contains(id) && !node.Cards.Any(card => card.IsKnown && card.Value.Id == id) &&
            Fact(node, "treasure", ".write.gold.balance.afterSpoils").DisplayValue == Scalar(concrete.Working.GoldBudget),
            "Actual SpoilsMap quest evidence lost its marker/count, payout, or consumed card.");
        AfterUses(node, "deck.All.count", Fact(node, "treasure", ".write.deck.All.count"));
        AfterUses(node, "gold.balance", Fact(node, "treasure", ".write.gold.balance.afterSpoils"));
    }

    private static void UnknownGoldDrawWithEctoplasmHasSpecificInvariant(PredictorRun captured)
    {
        var cause = PredictorRouteCauseEvidence.CreateExplicit("TreasureCheckMissingRewards", "capture:Rewards",
            PredictorCombatDomain.RewardsRng, PredictorPendingReasonKind.MissingObservation,
            "Rewards cursor was not observed.", ["rng.Rewards.state"]);
        var node = Preview(Route(captured, MapPointType.Treasure, ["ECTOPLASM"], missing: cause), TreasurePlan(take: false));
        var normal = Fact(node, "treasure", ".write.normalGold");
        var gold = Fact(node, "treasure", ".write.gold.balance.after");
        Check(normal.DisplayValue == null && node.Diagnostics!.Snapshot.Explain(normal.Id).Roots.Any(root => root.RootKey == cause.Identity),
            "Unknown Rewards cursor exposed nominal gold or lost its original source cause.");
        Check(gold.Status == PredictorKnowledgeStatus.Known && gold.DisplayValue == "100" &&
            gold.AllReadFactIds.Contains(normal.Id) && gold.UnresolvedReadFactIds.IsEmpty &&
            gold.Derivation?.Contains("Ectoplasm", StringComparison.Ordinal) == true &&
            node.Diagnostics!.Snapshot.Explain(gold.Id).RequiredInputs.IsEmpty,
            "Known Ectoplasm failed to prove unchanged balance independently of the unknown nominal draw.");
        Check(!Computation(node, "treasure").Any(f => f.PropertyKey.Contains("dragonFruit", StringComparison.Ordinal)),
            "A zero-gain TakeGold branch claimed a skipped Dragon Fruit listener read.");
    }
}
