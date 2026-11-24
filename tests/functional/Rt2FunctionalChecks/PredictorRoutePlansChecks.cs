using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorRoutePlansChecks
{
    internal static void Run(PredictorContext context, PredictorState state)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException)
            { checks++; return; }
            throw new InvalidOperationException("Malformed planned node was accepted");
        }
        PredictorRun Reload(string json) => PredictorRunStore.Deserialize(json,
            context.CatalogFingerprint, context.UnlockFingerprint);

        var run = new PredictorRun(context, state);
        var unknownCard = new PredictorPlannedCard(987654321, new ModelKey("CARD", "UNAVAILABLE_MOD_CARD"), 12);
        var potionAction = new PredictorPlannedPotionAction(PredictorRoutePotionStage.BeforeLeave,
            PredictorRoutePotionActionKind.Discard, 1, new(123456789, new("POTION", "UNAVAILABLE_MOD_POTION")), PredictorPhase.Rewards);
        var rest = new PredictorPlannedNode("future:rest:2:12")
        {
            RestActions = [new(PredictorRestAction.Heal) { MaximumHp = 80 },
                new(PredictorRestAction.Smith) { SmithCard = unknownCard },
                new(PredictorRestAction.Dig)],
            LeaveRest = true
        };
        var combat = new PredictorPlannedNode("future:combat:3:7")
        {
            Combat = new()
            {
                CardTriggers = [new(7654321, 2)],
                EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 4)]
            },
            RewardChoices = [new(new(0, PredictorRewardKind.Gold, PredictorRewardOrigin.Combat, default, [])
                { GoldAmount = 20 }, 0)],
            LeaveRewards = true, PotionActions = [potionAction]
        };
        var before = run.Working;
        long revision = run.Revision;
        run.SetPlannedNode(rest); run.SetPlannedNode(combat);
        Check(ReferenceEquals(run.Working, before) && run.Revision == revision && run.Request == null,
            "Stored plans do not execute rest, combat, reward pickup or travel");
        run.AutomaticRewardPickup = false;
        run.CombatDefaults = new() { CardRules = [new(new("CARD", "THE_HUNT"), 2)] };
        string serialized = PredictorRunStore.Serialize(run);
        var restored = Reload(serialized);
        Check(JsonNode.Parse(serialized)!["Schema"]!.GetValue<int>() == 7 && restored.PlannedNodes.Length == 2,
            "Schema 7 persists planned nodes");
        Check(restored.PlannedNodes[1].PotionActions is [var savedPotion] && savedPotion == potionAction,
            "Ordered potion intent preserves its stage, observed slot/key/identity and phase without requiring current catalog binding");
        Check(restored.PlannedNodes[0].RestActions.Select(a => a.Action).SequenceEqual(
            new[] { PredictorRestAction.Heal, PredictorRestAction.Smith, PredictorRestAction.Dig }) &&
            restored.PlannedNodes[0].LeaveRest && restored.PlannedNodes[0].RestActions[0].MaximumHp == 80,
            "Ordered Miniature Tent intent survives serialization");
        Check(restored.PlannedNodes[0].RestActions[1].SmithCard == unknownCard &&
            restored.PlannedNodes[1].Combat!.CardTriggers is [{ InstanceId: 7654321, EffectiveTriggers: 2 }] &&
            restored.PlannedNodes[1].RewardChoices[0].ExpectedReward.GoldAmount == 20,
            "Future nodes and presently unknown bindings remain authored intent");
        Check(!restored.AutomaticRewardPickup && restored.CombatDefaults.CardRules is [{ EffectiveTriggers: 2 }],
            "Plans preserve combat defaults and automatic pickup preference");

        restored.SetPlannedNode(restored.PlannedNodes[0] with { LeaveRest = false });
        Check(restored.PlannedNodes.Length == 2 && !restored.PlannedNodes[0].LeaveRest &&
            restored.PlannedNodes[1].NodeId == combat.NodeId, "Replacing one node preserves other plans and order");
        restored.EnterRewardNode(new(1, 3, "plan-rewind-fixture"), []);
        Check(restored.Submit(restored.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
            "Rewind fixture closes an explicit reward node");
        restored.Rewind(0);
        Check(restored.PlannedNodes.Length == 2, "Rewind preserves authored plans");
        restored.RemovePlannedNode(rest.NodeId);
        Check(restored.PlannedNodes is [{ NodeId: "future:combat:3:7" }], "Explicit removal clears only the selected plan");

        foreach (int schema in new[] { 1, 2 })
        {
            var legacy = JsonNode.Parse(serialized)!.AsObject();
            legacy["Schema"] = schema;
            legacy.Remove("PlannedNodes");
            Check(Reload(legacy.ToJsonString()).PlannedNodes.IsEmpty, $"Legacy schema {schema} starts with no plans");
        }
        var previous = JsonNode.Parse(serialized)!.AsObject();
        previous["Schema"] = 3;
        foreach (var item in previous["PlannedNodes"]!.AsArray())
        { item!.AsObject().Remove("Shop"); item.AsObject().Remove("Ancient"); }
        Check(Reload(previous.ToJsonString()).PlannedNodes.Length == 2,
            "Schema 3 rest/combat plans load without shop or Ancient fields");
        var schema6 = JsonNode.Parse(serialized)!.AsObject();
        schema6["Schema"] = 6;
        foreach (var item in schema6["PlannedNodes"]!.AsArray()) item!.AsObject().Remove("PotionActions");
        Check(Reload(schema6.ToJsonString()).PlannedNodes.All(p => p.PotionActions.IsEmpty),
            "Schema 6 plans load without new potion actions and preserve the empty default");

        var economy = new PredictorRun(context, state);
        var buy = new PredictorPlannedShopAction(PredictorShopActionKind.Buy)
        { ExpectedOffer = new(7, 0, PredictorShopEntryKind.Relic, new("RELIC", "MEMBERSHIP_CARD"), null, 150, 150, false, true) };
        economy.SetPlannedNode(new("future:shop") { Shop = new() { Actions = [buy, new(PredictorShopActionKind.Leave)] } });
        economy.SetPlannedNode(new("future:ancient") { Ancient = new(new("EVENT", "DARV"),
            new(0, new("RELIC", "GNARLED_HAMMER"), true, null, null, null, null, null))
            { Children = [new(PredictorObtainChildAction.ChooseDeckCards, new("RELIC", "GNARLED_HAMMER"))
                { DeckCards = [unknownCard] }] } });
        var restoredEconomy = Reload(PredictorRunStore.Serialize(economy));
        Check(restoredEconomy.PlannedNodes[0].Shop!.Actions[0].ExpectedOffer == buy.ExpectedOffer &&
            restoredEconomy.PlannedNodes[0].Shop!.Actions[1].Action == PredictorShopActionKind.Leave &&
            restoredEconomy.PlannedNodes[1].Ancient!.Children[0].DeckCards[0] == unknownCard,
            "Ordered shop and Ancient nested semantic plans persist without replacing unavailable targets");
        var schema4 = JsonNode.Parse(PredictorRunStore.Serialize(economy))!.AsObject();
        schema4["Schema"] = 4;
        foreach (var item in schema4["PlannedNodes"]!.AsArray()) item!.AsObject().Remove("Event");
        Check(Reload(schema4.ToJsonString()).PlannedNodes.All(p => p.Event == null),
            "Schema 4 shop and Ancient plans load with no event plan");
        var eventKey = new ModelKey("EVENT", "MORPHIC_GROVE");
        var eventPlan = new PredictorPlannedEvent(eventKey)
        {
            Occurrence = new(eventKey, eventKey, 3, 0, 2),
            Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = new("INDIVIDUAL") },
                new(PredictorEventActionKind.Cards) { DeckCards = [unknownCard] }]
        };
        var events = new PredictorRun(context, state);
        var excludedOccurrence = new PredictorEventOccurrenceExclusion(new("EVENT", "ENDLESS_CONVEYOR"), 1);
        events.SetPlannedNode(new("future:event") { Event = eventPlan, EventOccurrenceExclusions = [excludedOccurrence] });
        var restoredEventNode = Reload(PredictorRunStore.Serialize(events)).PlannedNodes.Single();
        var restoredEvent = restoredEventNode.Event!;
        Check(restoredEvent.ExpectedEvent == eventKey && restoredEvent.Occurrence == eventPlan.Occurrence &&
            restoredEvent.Actions[0].ExpectedOption == eventPlan.Actions[0].ExpectedOption &&
            restoredEvent.Actions[1].DeckCards.Single() == unknownCard,
            "Event appearance and ordered choices persist independently of current target availability");
        Check(restoredEventNode.EventOccurrenceExclusions.SequenceEqual([excludedOccurrence]),
            "Node-local exclusions persist independently of the retained event's operations");
        var withoutExclusions = JsonNode.Parse(PredictorRunStore.Serialize(events))!.AsObject();
        foreach (var item in withoutExclusions["PlannedNodes"]!.AsArray()) item!.AsObject().Remove("EventOccurrenceExclusions");
        Check(Reload(withoutExclusions.ToJsonString()).PlannedNodes.Single().EventOccurrenceExclusions.IsEmpty,
            "Older plans without occurrence exclusions preserve their original event choice and load with none");
        Reject(() => events.SetPlannedNode(new("future:event") { Event = eventPlan with
            { Occurrence = eventPlan.Occurrence! with { RawOrdinal = 0 } } }));
        Check(events.PlannedNodes.Single().Event == eventPlan, "Malformed event edit preserves the existing plan");
        var schema5 = JsonNode.Parse(PredictorRunStore.Serialize(events))!.AsObject();
        schema5["Schema"] = 5;
        foreach (var item in schema5["PlannedNodes"]!.AsArray()) item!.AsObject().Remove("Treasure");
        Check(Reload(schema5.ToJsonString()).PlannedNodes.Single().Event!.Occurrence == eventPlan.Occurrence,
            "Schema 5 event plans load without treasure intent");
        events.SetPlannedNode(new("future:treasure") { Treasure = new() { Actions =
            [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = new("RELIC", "OLD_COIN") },
                new(PredictorTreasureActionKind.Leave)] } });
        var restoredTreasure = Reload(PredictorRunStore.Serialize(events)).PlannedNodes.Last().Treasure!;
        Check(restoredTreasure.Actions.Select(a => a.Action).SequenceEqual(new[] { PredictorTreasureActionKind.Open,
            PredictorTreasureActionKind.TakeRelic, PredictorTreasureActionKind.Leave }) &&
            restoredTreasure.Actions[1].ExpectedRelic == new ModelKey("RELIC", "OLD_COIN"),
            "Treasure plans persist ordered semantic choices");
        var existingEconomy = economy.PlannedNodes;
        Reject(() => economy.SetPlannedNode(new("future:shop") { Shop = new()
            { Actions = [buy with { ExpectedOffer = buy.ExpectedOffer! with { Cost = -1 } }] } }));
        Reject(() => economy.SetPlannedNode(new("future:ancient") { Ancient = economy.PlannedNodes[1].Ancient! with
            { Children = [new(PredictorObtainChildAction.ChooseCard, new("RELIC", "KALEIDOSCOPE"))
                { CardIndices = [5] }] } }));
        Check(economy.PlannedNodes == existingEconomy, "Rejected nested plan edits preserve the previous authored plans");
        var omittedArrays = JsonNode.Parse(serialized)!.AsObject();
        var minimal = omittedArrays["PlannedNodes"]![0]!.AsObject();
        minimal.Remove("RestActions"); minimal.Remove("RewardChoices"); minimal.Remove("PotionActions");
        Check(Reload(omittedArrays.ToJsonString()).PlannedNodes[0].RestActions.IsEmpty,
            "Omitted optional arrays use the contract's empty default");

        var originalPlans = run.PlannedNodes;
        Reject(() => run.RestorePlannedNodes(default));
        Reject(() => run.RestorePlannedNodes([rest, rest]));
        Reject(() => run.SetPlannedNode(new(" ")));
        Reject(() => run.SetPlannedNode(rest with { RestActions = default }));
        Reject(() => run.SetPlannedNode(combat with { PotionActions = default }));
        Reject(() => run.SetPlannedNode(combat with { PotionActions = [potionAction with { Slot = -1 }] }));
        Reject(() => run.SetPlannedNode(combat with { PotionActions = [potionAction with
            { Stage = PredictorRoutePotionStage.BeforeChoices, ExpectedPhase = PredictorPhase.Complete }] }));
        Reject(() => run.SetPlannedNode(combat with { PotionActions = [potionAction with
            { ExpectedPotion = potionAction.ExpectedPotion with { Key = new("RELIC", "VAJRA") } }] }));
        Reject(() => run.SetPlannedNode(combat with { PotionActions = [potionAction,
            potionAction with { Stage = PredictorRoutePotionStage.BeforeChoices }] }));
        Reject(() => run.SetPlannedNode(rest with { RestActions = [new(PredictorRestAction.Heal) { SmithCard = unknownCard }] }));
        Reject(() => run.SetPlannedNode(rest with { RestActions = [new(PredictorRestAction.Smith)
            { SmithCard = unknownCard with { Key = new("RELIC", "VAJRA") } }] }));
        Reject(() => run.SetPlannedNode(combat with { Combat = new() { CardTriggers = [new(7, -1)] } }));
        Reject(() => run.SetPlannedNode(combat with { RewardChoices = [new(combat.RewardChoices[0].ExpectedReward, -1)] }));
        Reject(() => run.SetPlannedNode(combat with { RewardChoices = [new(combat.RewardChoices[0].ExpectedReward, 0, 0)] }));
        Check(run.PlannedNodes == originalPlans, "Rejected edits preserve all previous plans atomically");

        var returnedCard = state.Card(state.Deck[0]);
        var returnedPlan = new PredictorPlannedNode("future:returned-card")
        {
            RewardChoices = [new(new(0, PredictorRewardKind.SpecialCard, PredictorRewardOrigin.Combat, default,
                [returnedCard]), 0, CardIndex: 0)]
        };
        run.SetPlannedNode(returnedPlan);
        Check(Reload(PredictorRunStore.Serialize(run)).PlannedNodes.Last().RewardChoices[0].CardIndex == 0,
            "Returned special card supports a valid snapshot card index");
        Reject(() => run.SetPlannedNode(returnedPlan with
            { RewardChoices = [returnedPlan.RewardChoices[0] with { CardIndex = 1 }] }));

        string directory = Path.Combine(Path.GetTempPath(), "rt2-route-plans-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "route.json");
        try
        {
            PredictorRunStore.Save(path, run);
            string originalFile = File.ReadAllText(path);
            Reject(() => { run.RestorePlannedNodes([rest, rest]); PredictorRunStore.Save(path, run); });
            Check(File.ReadAllText(path) == originalFile, "Malformed plan edit never overwrites the old save file");
            var malformed = JsonNode.Parse(originalFile)!.AsObject();
            malformed["PlannedNodes"]![0]!["NodeId"] = "";
            Reject(() => Reload(malformed.ToJsonString()));
            Check(File.ReadAllText(path) == originalFile, "Malformed serialized plan leaves existing save untouched");
            Check(PredictorRunStore.ReadFrozenContext(path).Seed == context.Seed,
                "Frozen context reader accepts schema 7");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
        Console.WriteLine($"Predictor route plans checks PASS: {checks}; stored intent only, no runtime acceptance claimed.");
    }
}
