using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

internal static partial class PredictorOperationEventChecks
{
    internal static void Run(PredictorRun captured)
    {
        var context = captured.Context with { Live = null, Crystal = null,
            Operation = new(1, 0, 1, []) };
        var state = captured.Working with { OpeningCompleted = true, CurrentRoomIsCombat = false,
            Position = new(1, 9, "operation:event:fixture") };
        var eligible = state.Deck.Where(id => state.Card(id).Removable).ToArray();
        Require(eligible.Length >= 2, "fixture needs removable deck instances");
        var eventRng = PredictorStreamState.Capture(PredictorStream.Niche, new Xoshiro256StarStar(719));
        // This RNG is a progressed native event witness. Its seed is unrelated
        // to Context.Seed, so accidentally rerunning entry cannot pass.
        var progressed = eventRng.Restore();
        progressed.NextInt(7);
        eventRng = PredictorStreamState.Capture(PredictorStream.Niche, progressed);
        var bridge = new PredictorOperationEventScene(new("EVENT", "SLIPPERY_BRIDGE"), eventRng,
            [new("OVERCOME", CardTargetId: eligible[0]), new("HOLD_ON", HealthDamage: 6)], new(60, 80))
        {
            BridgeCard = eligible[0], BridgeHoldOns = 3, BridgeSkipped = [eligible[1]]
        };
        var before = state.Streams;
        var run = new PredictorRun(context, state);
        bridge.Restore(run);
        Require(run.EventOptions[0].CardTargetId == eligible[0] && run.Working.Streams.SequenceEqual(before),
            "Bridge import changed actual proposal or run RNG");
        var candidates = state.Deck.Where(id => state.Card(id).Removable &&
            state.Card(id).Key != state.Card(eligible[0]).Key && id != eligible[0] && id != eligible[1]).ToArray();
        if (candidates.Length == 0) candidates = eligible;
        var expectedRng = eventRng.Restore();
        long expected = candidates[expectedRng.NextInt(candidates.Length)];
        Require(run.Submit(run.Request!, new ChooseEventOption("HOLD_ON")) == PredictorInputResult.Accepted &&
            run.EventOptions[0].CardTargetId == expected && run.EventOptions[1].HealthDamage == 7,
            "Bridge did not continue current skip/count/RNG boundary");
        Require(run.Working.Streams.SequenceEqual(before) && state.Deck.SequenceEqual(captured.Working.Deck),
            "Bridge prediction changed input state or unrelated RNG");
        var again = new PredictorRun(context, state);
        bridge.Restore(again);
        Require(again.Submit(again.Request!, new ChooseEventOption("HOLD_ON")) == PredictorInputResult.Accepted &&
            again.EventOptions.SequenceEqual(run.EventOptions), "Repeated Bridge prediction changed result");

        var tablet = new PredictorOperationEventScene(new("EVENT", "TABLET_OF_TRUTH"), eventRng,
            [new("DECIPHER", MaxHpCost: 12), new("GIVE_UP")], new(60, 80)) { TabletCount = 2 };
        var tabletRun = new PredictorRun(context, state);
        tablet.Restore(tabletRun);
        Require(tabletRun.Submit(tabletRun.Request!, new ChooseEventOption("DECIPHER")) == PredictorInputResult.Accepted &&
            tabletRun.EventOptions.Single(o => o.Key == "DECIPHER").MaxHpCost == 24,
            "Tablet import restarted decipher count");
        Require(tabletRun.Working.Streams.SequenceEqual(before), "Tablet local RNG leaked into run RNG");

        // Morphic Group has already stolen gold when its target picker opens.
        // Restoring that picker must preserve its current two-target decision.
        var pickerState = state with { GoldBudget = 37 };
        var picker = new PredictorOperationEventScene(new("EVENT", "MORPHIC_GROVE"), eventRng, [], new(60, 80))
        { Branch = "GROUP", AwaitingCards = true, CardCandidates = eligible.ToImmutableArray(), CardsRequired = 2 };
        var pickerRun = new PredictorRun(context, pickerState);
        picker.Restore(pickerRun);
        Require(pickerRun.Phase == PredictorPhase.EventCards && pickerRun.Working.GoldBudget == 37 &&
            pickerRun.EventCardsRequired == 2 && pickerRun.EventCardCandidates.SequenceEqual(eligible),
            "Event target picker replayed its paid parent or changed candidates");
        Require(pickerRun.Submit(pickerRun.Request!, new SelectEventCards([eligible[0], eligible[0]])) == PredictorInputResult.Invalid,
            "Event target picker accepted duplicate instances");

        long driftwoodId = state.NextInstanceId, potionId = driftwoodId + 1;
        var futureState = state with { Relics = [new(driftwoodId, new("RELIC", "DRIFTWOOD"), 1)],
            PotionSlots = [new(potionId, new("POTION", "FOUL_POTION")), null], NextInstanceId = potionId + 1 };
        var future = new PredictorOperationEventScene(new("EVENT", "THE_FUTURE_OF_POTIONS"), eventRng,
            [new("POTION:" + potionId, PotionId: potionId, CardType: EffectCardType.Skill)], new(60, 80))
        {
            FuturePotionTypes = ImmutableDictionary<long, EffectCardType>.Empty.Add(potionId, EffectCardType.Skill),
            FuturePotionRarities = ImmutableDictionary<long, EffectCardRarity>.Empty.Add(potionId, EffectCardRarity.Common)
        };
        var futureRun = new PredictorRun(context, futureState);
        future.Restore(futureRun);
        Require(futureRun.Submit(futureRun.Request!, new ChooseEventOption("POTION:" + potionId)) == PredictorInputResult.Accepted,
            "Future token potion could not use its captured actual rarity");
        var cardReward = futureRun.Rewards.Single(r => r.Kind == PredictorRewardKind.Card);
        Require(cardReward.Cards.All(c => c.UpgradeLevel > 0) && cardReward.RerollRecipe?.UpgradeAfterGeneration == true,
            "Future first generation lost source upgrade recipe");
        var selection = futureRun.Submit(futureRun.Request!, new EnterCardSelection(cardReward.Id));
        Require(selection == PredictorInputResult.Accepted, "Future picker entry: " + selection);
        var reroll = futureRun.Submit(futureRun.Request!, new RerollCardReward(cardReward.Id));
        Require(reroll == PredictorInputResult.Accepted, "Future Driftwood reroll disposition: " + reroll);
        Require(futureRun.Rewards.Single(r => r.Id == cardReward.Id).Cards.All(c => c.UpgradeLevel > 0),
            "Future Driftwood reroll lost event forced upgrade");
        var condition = new PredictorCombatFacts(false) { Potions = PredictorCombatPotionFacts.NoAdditional };
        var combatState = state with { Relics = [], PotionSlots = [null, null] };
        var dummyScene = new PredictorOperationEventScene(new("EVENT", "BATTLEWORN_DUMMY"), eventRng,
            [new("SETTING_1"), new("SETTING_2"), new("SETTING_3")], new(60, 80));
        var dummySnapshot = new PredictorOperationSnapshot("conditional-dummy", context, combatState, dummyScene, 60, 80);
        var waiting = PredictorOperation.Preview(dummySnapshot, new ChooseEventOption("SETTING_2"));
        Require(waiting.Waiting.Phase == PredictorPhase.CombatFacts, "Dummy invented battle conditions");
        var timedOut = PredictorOperation.Preview(dummySnapshot,
            [new ChooseEventOption("SETTING_2"), new ResolveCombat(condition, true)]);
        var won = PredictorOperation.Preview(dummySnapshot,
            [new ChooseEventOption("SETTING_2"), new ResolveCombat(condition, false)]);
        Require(timedOut.Disposition == PredictorInputResult.Accepted && won.Disposition == PredictorInputResult.Accepted &&
            timedOut.Waiting.Phase == PredictorPhase.Complete && !timedOut.Effects.Any(i => i.Action == "BattlewornDummyUpgrade") &&
            won.Effects.Any(i => i.Action == "BattlewornDummyUpgrade"), "Dummy explicit timeout/victory branches collapsed");
        var punchScene = new PredictorOperationEventScene(new("EVENT", "PUNCH_OFF"), eventRng,
            [new("NAB"), new("I_CAN_TAKE_THEM")], new(60, 80)) { LayoutEncounter = new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER") };
        var punchSnapshot = dummySnapshot with { Fingerprint = "conditional-punch", Scene = punchScene };
        var punch = PredictorOperation.Preview(punchSnapshot,
            [new ChooseEventOption("I_CAN_TAKE_THEM"), new ChooseEventOption("FIGHT"), new ResolveCombat(condition)]);
        Require(punch.Disposition == PredictorInputResult.Accepted && punch.Rewards.Any(r => r.Kind == PredictorRewardKind.Relic) &&
            punch.Rewards.Any(r => r.Kind == PredictorRewardKind.Potion), "Punch explicit battle condition omitted event extra rewards");
        long restRewardId = state.NextInstanceId;
        var resting = new PredictorOperationEventScene(new("EVENT", "DENSE_VEGETATION"), eventRng, [], new(60, 80))
        {
            Branch = "REST", MimicRestRewards = true,
            RewardScene = new([new(restRewardId, PredictorRewardKind.Potion, PredictorRewardOrigin.Event,
                new("POTION", "FIRE_POTION"), [], ContentInstanceId: restRewardId + 1)])
        };
        var rested = PredictorOperation.Preview(dummySnapshot with { Scene = resting,
            State = combatState with { NextInstanceId = restRewardId + 2 } }, new LeaveRewards());
        Require(rested.Disposition == PredictorInputResult.Accepted && rested.CurrentHp == 60 &&
            rested.Waiting.EventOptions.Select(o => o.Key).SequenceEqual(["FIGHT"]),
            "Captured Dense rest rewards repeated healing or finished before Fight");
        var repyState = PredictorObtainSources.AddCard(context, combatState, new ModelKey("CARD", "LANTERN_KEY"));
        repyState = PredictorObtainSources.AddCard(context, repyState, new ModelKey("CARD", "LANTERN_KEY"));
        var repy = new PredictorOperationEventScene(new("EVENT", "WAR_HISTORIAN_REPY"), eventRng,
            [new("INITIAL_UNLOCK_CAGE"), new("INITIAL_UNLOCK_CHEST")], new(60, 80));
        var multiplayerRepy = PredictorOperation.Preview(dummySnapshot with { Scene = repy, State = repyState,
            Context = context with { Operation = new(1, 0, 2, []) } }, new ChooseEventOption("INITIAL_UNLOCK_CAGE"));
        Require(multiplayerRepy.Disposition == PredictorInputResult.Accepted && multiplayerRepy.Waiting.Phase == PredictorPhase.Complete &&
            !multiplayerRepy.State.Deck.Any(id => multiplayerRepy.State.Card(id).Key.Entry == "LANTERN_KEY"),
            "Multiplayer Repy kept a key or invented second reward");
        var singlePlayerRepy = PredictorOperation.Preview(dummySnapshot with { Scene = repy, State = repyState },
            new ChooseEventOption("INITIAL_UNLOCK_CAGE"));
        Require(singlePlayerRepy.State.Deck.Count(id => singlePlayerRepy.State.Card(id).Key.Entry == "LANTERN_KEY") == 1 &&
            singlePlayerRepy.Waiting.EventOptions.Select(o => o.Key).SequenceEqual(["SECOND_UNLOCK_CHEST"]),
            "Singleplayer Repy lost the existing second-key path");
        var routeRepy = new PredictorRun(context with { Operation = null }, repyState);
        routeRepy.BeginEvent(state.Position, new("EVENT", "WAR_HISTORIAN_REPY"));
        Require(routeRepy.Submit(routeRepy.Request!, new ChooseEventOption("INITIAL_UNLOCK_CAGE")) == PredictorInputResult.Accepted &&
            routeRepy.EventOptions.Select(o => o.Key).SequenceEqual(["SECOND_UNLOCK_CHEST"]),
            "Route/SP Repy behavior changed without operation context");
        var trialConfirmation = new PredictorOperationEventScene(new("EVENT", "TRIAL"), eventRng,
            [new("ACCEPT"), new("DOUBLE_DOWN")], new(60, 80)) { TrialAbandonPending = true };
        var trialRun = new PredictorRun(context, combatState);
        trialConfirmation.Restore(trialRun);
        Require(trialRun.TrialAbandonConfirmationPending && trialRun.EventOptions.IsEmpty &&
            trialRun.Submit(trialRun.Request!, new ConfirmTrialAbandon(false)) == PredictorInputResult.Accepted &&
            trialRun.EventOptions.Select(o => o.Key).SequenceEqual(["ACCEPT", "DOUBLE_DOWN"]),
            "Restored Trial abandon confirmation did not preserve cancel return choices");
        string Dish(float roll, bool full)
        {
            (string Name, int Weight)[] choices = full
                ? [("CAVIAR", 6), ("SPICY_SNAPPY", 3), ("JELLY_LIVER", 3), ("SUSPICIOUS_CONDIMENT", 3), ("GOLDEN_FYSH", 1)]
                : [("CAVIAR", 6), ("SPICY_SNAPPY", 3), ("JELLY_LIVER", 3), ("SUSPICIOUS_CONDIMENT", 3), ("CLAM_ROLL", 6), ("GOLDEN_FYSH", 1)];
            float at = roll * choices.Sum(c => c.Weight);
            int cumulative = 0;
            foreach (var c in choices) { cumulative += c.Weight; if (at < cumulative) return c.Name; }
            throw new InvalidOperationException("Conveyor fixture roll invalid");
        }
        var healthRng = eventRng;
        string expectedDish = "";
        for (ulong seed = 1; seed < 128; seed++)
        {
            var candidate = new Xoshiro256StarStar(seed);
            float roll = candidate.NextFloat();
            if (Dish(roll, true) == Dish(roll, false)) continue;
            expectedDish = Dish(roll, true);
            healthRng = PredictorStreamState.Capture(PredictorStream.Niche, new Xoshiro256StarStar(seed));
            break;
        }
        Require(expectedDish.Length > 0, "Conveyor fixture did not distinguish full/nonfull pools");
        var conveyorHealth = new PredictorOperationEventScene(new("EVENT", "ENDLESS_CONVEYOR"), healthRng,
            [new("GRAB_FRIED_EEL"), new("OBSERVE_CHEF")], new(79, 80))
        { ConveyorDish = "FRIED_EEL", ConveyorLastDish = "FRIED_EEL", ConveyorGrabs = 1 };
        var healthState = combatState with { GoldBudget = 100,
            Relics = [new(combatState.NextInstanceId, new("RELIC", "BOOK_OF_FIVE_RINGS"), 1) { BookOfFiveRingsCardsAdded = 4 }],
            NextInstanceId = combatState.NextInstanceId + 1 };
        var healthResult = PredictorOperation.Preview(dummySnapshot with { State = healthState, Scene = conveyorHealth,
            CurrentHp = 79, MaximumHp = 80 }, new ChooseEventOption("GRAB_FRIED_EEL"));
        Require(healthResult.Disposition == PredictorInputResult.Accepted && healthResult.CurrentHp == 80 &&
            healthResult.Waiting.EventOptions[0].Key == "GRAB_" + expectedDish,
            "Conveyor next dish ignored automatic BookOfFiveRings healing before eligibility");
        CheckEventProjection(context, combatState, eventRng);
        Console.WriteLine("OPERATION_EVENT Bridge progressed RNG/skips/count + Tablet midpoint + paid Morphic picker + Future token/Driftwood upgrade PASS");
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
