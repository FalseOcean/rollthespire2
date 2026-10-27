using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorRuntimeChecks
{
    private static int _checks;
    private static readonly PredictorContext Context = new("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"),
        10, "runtime-fixture-catalog", "runtime-fixture-unlocks", [], [], [], 1)
    {
        Catalog = new([], []) { CardDefinitions = new[] { Card(), Card("BASH"), Card("DEFEND_IRONCLAD", EffectCardType.Skill) }
            .Select(c => new PredictorCardDefinition(c, "Basic", new("CARD_POOL", "CONTROLLED"), false,
                c.Key.Entry == "STRIKE_IRONCLAD", c.Key.Entry == "DEFEND_IRONCLAD")
            { Levels = Enumerable.Range(0, 2).Select(level => new PredictorCardLevelMetadata(level,
                c.Key.Entry == "BASH" ? 2 : 1, false, false, false, false, false, false)).ToImmutableArray() }).ToImmutableArray() }
    };
    private static ModelKey Relic(string name) => new("RELIC", name);
    private static ModelKey Potion(string name) => new("POTION", name);
    private static PredictorCard Card(string name = "STRIKE_IRONCLAD", EffectCardType type = EffectCardType.Attack) =>
        new(0, new("CARD", name), type, 0, 1, true, 1);
    private static PredictorState State(params string[] relics) => PredictorSeedState.Create(Context,
        [Card(), Card(), Card(), Card("BASH"), Card("DEFEND_IRONCLAD", EffectCardType.Skill)], relics.Select(Relic), 99, 2, new(-.05f, .4f));
    private static PredictorReward Item(PredictorRewardKind kind, ModelKey key = default,
        PredictorRewardOrigin origin = PredictorRewardOrigin.Combat) => new(0, kind, origin, key, []);
    private static PredictorReward CardOffer() => Item(PredictorRewardKind.Card) with { Cards = [Card("BASH"), Card()] };
    private static PredictorRun Open(PredictorState state, params PredictorReward[] rewards)
    {
        var run = new PredictorRun(Context, state);
        run.EnterRewardNode(new(1, 3, "1:3"), rewards.ToImmutableArray());
        return run;
    }
    private static void Send(PredictorRun run, PredictorInput input, PredictorInputResult expected = PredictorInputResult.Accepted) =>
        Require(run.Submit(run.Request!, input) == expected, $"Input {input.GetType().Name}: {expected}");
    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { _checks++; return; }
        throw new InvalidOperationException(message);
    }

    internal static int Run()
    {
        RngContinuation();
        NativeBags();
        GoldAndInputIdentity();
        Potions();
        EntropicUse();
        RewardOrderAndNestedChoice();
        RewardPickupRecommendations();
        PredictorRewardPickupScopeChecks.Run(Context, State());
        PredictorRoutePlansChecks.Run(Context, State());
        CardLifecycle();
        CheckpointsAndCancellation();
        LocalCrystalBoundary();
        Console.WriteLine($"Predictor Runtime settlement checks PASS: {_checks}. Native authorities: RunRngSet/PlayerRngSet, GoldReward.Populate, RelicGrabBag, PotionFactory, StableShuffle. No full-node/route UI or complete-hook parity claimed.");
        return 0;
    }

    private static void RewardPickupRecommendations()
    {
        var run = Open(State(), Item(PredictorRewardKind.Gold) with { GoldAmount = 20 },
            Item(PredictorRewardKind.Relic, Relic("VAJRA")), CardOffer(),
            Item(PredictorRewardKind.Relic, Relic("BOWLER_HAT")),
            Item(PredictorRewardKind.Potion, Potion("ENERGY_POTION")));
        var before = run.Working;
        var rewardsBefore = run.Rewards;
        var plan = PredictorRewardPickupPolicy.Plan(run);
        Require(plan.NextInput is TakeReward first && first.RewardId == run.Rewards[3].Id,
            "Pickup policy prioritizes Bowler before existing gold and ordinary rewards");
        Require(plan.Pending is [{ Reason: PredictorRewardPickupPendingReason.PlayerChoice }],
            "Card rewards stay pending without stopping independent pickups");
        Require(ReferenceEquals(before, run.Working) && rewardsBefore.SequenceEqual(run.Rewards),
            "Pickup planning neither settles nor regenerates rewards");
        Send(run, plan.NextInput!);
        Require(PredictorRewardPickupPolicy.Plan(run).NextInput is TakeReward second && second.RewardId == run.Rewards[1].Id,
            "Ordinary relic pickups retain reward order");
        Send(run, PredictorRewardPickupPolicy.Plan(run).NextInput!);
        Require(PredictorRewardPickupPolicy.Plan(run).NextInput is TakeReward gold && gold.RewardId == run.Rewards[0].Id,
            "Gold follows the benefit and ordinary tiers");
        Send(run, PredictorRewardPickupPolicy.Plan(run).NextInput!);
        Require(PredictorRewardPickupPolicy.Plan(run).NextInput is TakeReward potion && potion.RewardId == run.Rewards[4].Id,
            "Potion pickup follows gold and does not replace a full slot");
        Send(run, PredictorRewardPickupPolicy.Plan(run).NextInput!);
        Require(run.Working.GoldBudget == before.GoldBudget + 25,
            "Automatic recommendation uses already generated gold and Bowler's pickup gain");
        Require(PredictorRewardPickupPolicy.Plan(run).NextInput == null && run.Phase == PredictorPhase.Rewards &&
            run.Rewards[2].Status == PredictorRewardStatus.Open,
            "Pending cards are not skipped and the reward node is not automatically left");

        var full = State() with { NextInstanceId = 102,
            PotionSlots = [new(100, Potion("ENERGY_POTION")), new(101, Potion("FIRE_POTION"))] };
        var potionRun = Open(full, Item(PredictorRewardKind.Potion, Potion("BLOCK_POTION")));
        var potionBefore = potionRun.Working;
        var fullPlan = PredictorRewardPickupPolicy.Plan(potionRun);
        Require(fullPlan.NextInput == null && fullPlan.Pending is [{ Reason: PredictorRewardPickupPendingReason.PotionSlotsFull }] &&
            potionRun.Phase == PredictorPhase.Rewards && potionRun.Rewards[0].Status == PredictorRewardStatus.Open &&
            ReferenceEquals(potionBefore, potionRun.Working),
            "Full potion inventory remains open without entering replacement or marking skipped");
        var belt = Open(full, Item(PredictorRewardKind.Potion, Potion("BLOCK_POTION")),
            Item(PredictorRewardKind.Relic, Relic("POTION_BELT")));
        Send(belt, PredictorRewardPickupPolicy.Plan(belt).NextInput!);
        Require(PredictorRewardPickupPolicy.Plan(belt).NextInput is TakeReward bottle && bottle.RewardId == belt.Rewards[0].Id,
            "Replanning after Potion Belt releases the preserved bottle");

        var sozu = Open(State("SOZU"), Item(PredictorRewardKind.Potion, Potion("BLOCK_POTION")),
            Item(PredictorRewardKind.Gold) with { GoldAmount = 20 });
        var sozuPlan = PredictorRewardPickupPolicy.Plan(sozu);
        Require(sozuPlan.NextInput is TakeReward sozuGold && sozuGold.RewardId == sozu.Rewards[1].Id &&
            sozuPlan.Pending is [{ Reason: PredictorRewardPickupPendingReason.PotionNotAllowed }] &&
            sozu.Rewards[0].Status == PredictorRewardStatus.Open,
            "Sozu never auto consumes an unprocurable potion reward");

        foreach (string name in new[] { "WHETSTONE", "WAR_PAINT", "EMPTY_CAGE", "TOY_BOX", "SILKEN_TRESS", "WING_CHARM" })
        {
            var held = Open(State(), Item(PredictorRewardKind.Relic, Relic(name)));
            Require(PredictorRewardPickupPolicy.Plan(held) is { NextInput: null, Pending.Length: 1 },
                $"Pickup policy leaves choice or order-sensitive {name} to the player");
        }
        var manual = Open(State(), Item(PredictorRewardKind.Relic, Relic("WHETSTONE")));
        Send(manual, new TakeReward(manual.Rewards[0].Id));
        Require(manual.Rewards[0].Status == PredictorRewardStatus.Taken,
            "Pending recommendation does not prohibit manually taking Whetstone");
        var wax = Open(State(), Item(PredictorRewardKind.Relic, Relic("BOWLER_HAT")) with { IsWaxRelic = true });
        Require(PredictorRewardPickupPolicy.Plan(wax).Pending is [{ Reason: PredictorRewardPickupPendingReason.WaxRelicOrder }],
            "Wax relic ordering overrides the benefit tier");
        var unknown = Item(PredictorRewardKind.Relic, Relic("UNRECOGNIZED_MOD_RELIC")) with { Id = 100 };
        var unknownPlan = PredictorRewardPickupPolicy.Plan(State(), [unknown,
            Item(PredictorRewardKind.Relic, Relic("VAJRA")) with { Id = 101 }]);
        Require(unknownPlan.NextInput is TakeReward known && known.RewardId == 101 &&
            unknownPlan.Pending is [{ RewardId: 100, Reason: PredictorRewardPickupPendingReason.UnclassifiedRelic }],
            "Unsupported pickup is localized while supported ordinary rewards remain available");
        Send(run, new EnterCardSelection(run.Rewards[2].Id));
        Require(PredictorRewardPickupPolicy.Plan(run).Pending is [{ Reason: PredictorRewardPickupPendingReason.ActiveSelection }],
            "Pickup policy does not interrupt an active card selection");
        Require(PredictorRewardPickupPolicy.Plan(State(), [], PredictorPhase.ObtainDeckCards).Pending is
            [{ Reason: PredictorRewardPickupPendingReason.ActivePhase }], "Nested obtain input remains a player boundary");
    }

    private static void RngContinuation()
    {
        foreach (string seed in new[] { Context.Seed, "000000000000", "000000000006", "SKDNXGNU2A16" })
        {
            var state = PredictorSeedState.Create(Context with { Seed = seed }, [], [], 0, 0, new(0, .4f));
            var run = new RunRngSet(seed);
            var player = new PlayerRngSet(run.Seed); // single-player slot zero
            var streams = new[] { run.UpFront, player.Rewards, player.Transformations, player.Shops,
                run.Niche, run.CombatPotionGeneration, run.UnknownMapPoint, run.TreasureRoomRelics };
            foreach (PredictorStream stream in Enum.GetValues<PredictorStream>())
            {
                var native = streams[(int)stream];
                CheckRng(state.Streams[(int)stream], native);
                for (int i = 0; i < 32; i++)
                {
                    int value;
                    (value, state) = state.Draw(stream, 7, 98);
                    Require(value == native.NextInt(7, 98), $"Native RNG draw {stream}");
                    CheckRng(state.Streams[(int)stream], native);
                }
                var restored = state.Streams[(int)stream].Restore();
                Require(restored.NextInt(1000) == native.NextInt(1000), "Boundary RNG restoration");
            }
        }
    }

    private static void LocalCrystalBoundary()
    {
        var state = State();
        var localContext = Context with { Crystal = new(2, 1, 123,
            ImmutableHashSet<ModelKey>.Empty) };
        void RefusesRoute(PredictorRun run, Action action)
        {
            var before = run.Working;
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message == "PredictorLocalCrystalOnly")
            {
                Require(ReferenceEquals(before, run.Working), "Local Crystal route refusal changed its boundary");
                return;
            }
            throw new InvalidOperationException("Local Crystal accepted a full-route operation");
        }
        var declaredLocal = new PredictorRun(localContext, state);
        RefusesRoute(declaredLocal, () => declaredLocal.AdvanceRoute());
        RefusesRoute(declaredLocal, () => declaredLocal.RouteMap(1));
        RefusesRoute(declaredLocal, () => declaredLocal.EnterRewardNode(new(1, 3, "local-crystal"), []));
        RefusesRoute(declaredLocal, () => PredictorRunStore.Serialize(declaredLocal));

        // A detached board can lack old context metadata. FromCrystal itself
        // still owns the local-mode boundary; do not infer full-route capability.
        var snapshot = new PredictorCrystalSnapshot(Context, state,
            state.Streams.Single(s => s.Stream == PredictorStream.Rewards), 0, PredictorCrystalTool.Big, true,
            Enumerable.Repeat(false, 100).ToImmutableArray(), Enumerable.Repeat(-1, 100).ToImmutableArray(),
            [new("GOLD_SMALL", 1, 1, 0, 0, 1, true)], [0]);
        var restoredLocal = PredictorRun.FromCrystal(snapshot);
        RefusesRoute(restoredLocal, () => restoredLocal.AdvanceRoute());
        RefusesRoute(restoredLocal, () => PredictorRunStore.Serialize(restoredLocal));
        var preview = PredictorRun.PreviewCrystalRewards(snapshot with { Context = localContext }, [0], CancellationToken.None);
        Require(preview.Context.Crystal is { PlayerCount: 2, PlayerSlot: 1 } &&
            preview.Rewards is [{ Kind: PredictorRewardKind.Gold, GoldAmount: 10 }],
            "Local multiplayer Crystal reward preview was replaced by full-route initialization");
        var beforeTake = preview.Working;
        var rewardsBeforeTake = preview.Rewards;
        Send(preview, new TakeReward(preview.Rewards[0].Id), PredictorInputResult.Invalid);
        Require(ReferenceEquals(beforeTake, preview.Working) && rewardsBeforeTake.SequenceEqual(preview.Rewards),
            "Local Crystal incorrectly settled a non-card reward or consumed working RNG");
    }

    private static void CheckRng(PredictorStreamState state, Rng native)
    {
        var expected = native.ToSerializable();
        Require((state.S0, state.S1, state.S2, state.S3, state.Calls) ==
            (expected.state0, expected.state1, expected.state2, expected.state3, expected.counter), "Native RNG continuation differs");
    }

    // Pure fake eligibility/rarity inputs drive the REAL vanilla bag algorithm.
    // This checks storage/refresh/pull semantics, not real catalog eligibility.
    private sealed class BagRelic : RelicModel
    {
        internal RelicRarity TestRarity;
        internal bool Allowed = true;
        public override RelicRarity Rarity => TestRarity;
        public override bool IsAllowed(IRunState runState) => Allowed;
        public override bool IsMock => true;
    }
    private static BagRelic BagModel(string name, RelicRarity rarity)
    {
        var model = (BagRelic)RuntimeHelpers.GetUninitializedObject(typeof(BagRelic));
        typeof(AbstractModel).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new ModelId("RELIC", name));
        model.TestRarity = rarity;
        model.Allowed = true;
        return model;
    }
    private static void NativeBags()
    {
        foreach (bool refresh in new[] { false, true })
        for (ulong seed = 0; seed < 12; seed++)
        {
            var models = new[] { BagModel("WHETSTONE", RelicRarity.Common), BagModel("WAR_PAINT", RelicRarity.Common),
                BagModel("POTION_BELT", RelicRarity.Common), BagModel("BOWLER_HAT", RelicRarity.Uncommon),
                BagModel("SOZU", RelicRarity.Rare), BagModel("EMPTY_CAGE", RelicRarity.Shop) };
            var nativeRng = new Rng(seed);
            var rng = new Xoshiro256StarStar(seed);
            var native = new RelicGrabBag(refresh);
            native.Populate(models, nativeRng);
            var bag = PredictorRelicBag.Populate(models.Select(m => new PredictorBagEntry(Relic(m.Id.Entry), m.Rarity.ToString())), !refresh, rng);
            CheckRng(PredictorStreamState.Capture(PredictorStream.UpFront, rng), nativeRng);
            CompareBag(bag, native);
            for (int i = 0; i < 18; i++)
            {
                if (i == 2) models[2].Allowed = false;
                if (i == 4) { native.MoveToFallback(models[3]); bag = bag.MoveToFallback(Relic(models[3].Id.Entry)); }
                if (i == 5) { native.Remove(models[3]); bag = bag.Remove(Relic(models[3].Id.Entry)); }
                string rarity = i % 4 == 0 ? "Shop" : "Common";
                bool back = i % 2 != 0;
                bool Filter(ModelKey k) => i != 1 || k.Entry == "SOZU";
                bool NativeFilter(RelicModel m) => Filter(Relic(m.Id.Entry));
                var expected = back ? native.PullFromBack(Enum.Parse<RelicRarity>(rarity), NativeFilter, null!) :
                    native.PullFromFront(Enum.Parse<RelicRarity>(rarity), NativeFilter, null!);
                ModelKey? actual;
                (actual, bag) = bag.Pull(rarity, back, k => models.Single(m => m.Id.Entry == k.Entry).Allowed, Filter);
                Require(actual?.Entry == expected?.Id.Entry, "Native bag pull/fallback/refresh");
                CompareBag(bag, native);
                CheckRng(PredictorStreamState.Capture(PredictorStream.UpFront, rng), nativeRng);
            }
        }
        var original = ImmutableArray.Create(new PredictorBagEntry(Relic("WHETSTONE"), "Common"));
        var noBucket = new PredictorRelicBag(true, original, [], []);
        var pulled = noBucket.Pull("Common", false, _ => true, _ => true);
        Require(pulled.Key == null && pulled.Bag.Buckets.Single().Entries.Length == 1, "Detached GetDeque on refresh");
        Require(pulled.Bag.Pull("Common", false, _ => true, _ => true).Key == Relic("WHETSTONE"), "Next pull sees refreshed bucket");
    }
    private static void CompareBag(PredictorRelicBag actual, RelicGrabBag native)
    {
        var expected = native.ToSerializable().RelicIdLists;
        Require(actual.Buckets.Length == expected.Count, "Bag rarity preservation");
        foreach (var bucket in actual.Buckets)
            Require(bucket.Entries.Select(k => k.Entry).SequenceEqual(expected[Enum.Parse<RelicRarity>(bucket.Rarity)].Select(k => k.Entry)), "Bag physical order");
    }

    private static void GoldAndInputIdentity()
    {
        foreach (var relics in new[] { Array.Empty<string>(), new[] { "BOWLER_HAT" }, new[] { "ECTOPLASM", "BOWLER_HAT" } })
        {
            var initial = State(relics);
            var gold = Item(PredictorRewardKind.Gold) with { PopulateGold = true, GoldMinimum = 7, GoldMaximum = 13 };
            var run = Open(initial, gold, gold with { GoldMinimum = 5, GoldMaximum = 5 }, gold with { PopulateGold = false, GoldAmount = 11 });
            var rng = new PlayerRngSet(new RunRngSet(Context.Seed).Seed).Rewards;
            var reward = new GoldReward(7, 13, null!);
            reward.SetRng(rng);
            reward.Populate();
            Require(run.Rewards[0].GoldAmount == reward.Amount, "Actual GoldReward.Populate result");
            var singleton = new GoldReward(5, null!);
            singleton.SetRng(rng);
            singleton.Populate();
            CheckRng(run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards), rng);
            Require(run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls == 2, "Prefilled vs populated fixed gold RNG");
            var oldRequest = run.Request!;
            var before = run.Working;
            Send(run, new TakeReward(-1), PredictorInputResult.Invalid);
            Require(ReferenceEquals(before, run.Working) && oldRequest == run.Request, "Invalid input mutated state/request");
            Send(run, new CorrectGoldBalance(123));
            Require(run.Working.GoldBudget == 123 && run.Working.Streams == before.Streams, "Budget correction triggered Gold hooks/RNG");
            Require(run.Submit(oldRequest, new CorrectGoldBalance(999)) == PredictorInputResult.Stale, "Duplicate submit accepted");
            int amount = run.Rewards[0].GoldAmount;
            Send(run, new TakeReward(run.Rewards[0].Id));
            int gain = relics.Contains("ECTOPLASM") ? 0 : relics.Contains("BOWLER_HAT") ? (int)(amount * 1.25m) : amount;
            Require(run.Working.GoldBudget == 123 + gain, "Real gold modifiers differ from budget correction");
            Send(run, new LeaveRewards());
            Require(run.Checkpoints[^1].CompletedNode!.Inputs[0].Action == "CorrectGoldBalance", "Invalid input entered accepted history");
            CheckRng(run.Checkpoints[^1].State.Streams.Single(s => s.Stream == PredictorStream.Rewards), rng);
            Require(initial.GoldBudget == 99, "Working aliased entry checkpoint");
            Require(run.Context.SealOfGoldFixedDeduction == null, "Invented SealOfGold amount");
        }
    }

    private static void Potions()
    {
        var s = State();
        s = s with { PotionSlots = [new(s.NextInstanceId, Potion("STRENGTH_POTION")), new(s.NextInstanceId + 1, Potion("DEXTERITY_POTION"))], NextInstanceId = s.NextInstanceId + 2 };
        var run = Open(s, Item(PredictorRewardKind.Potion, Potion("ENERGY_POTION")), Item(PredictorRewardKind.Relic, Relic("POTION_BELT")));
        long reward = run.Rewards[0].Id;
        Send(run, new TakeReward(reward), PredictorInputResult.TooFull);
        Require(run.Working.PotionSlots == s.PotionSlots && run.Rewards[0].Status == PredictorRewardStatus.Open, "TooFull consumed inventory/reward");
        Send(run, new LeaveRewards(), PredictorInputResult.Invalid);
        Send(run, new ReplaceRewardPotion(0, -1), PredictorInputResult.Invalid);
        Send(run, new CancelPotionReplacement());
        Send(run, new TakeReward(reward), PredictorInputResult.TooFull);
        Send(run, new ReplaceRewardPotion(1, s.PotionSlots[1]!.Id));
        Require(run.Working.PotionSlots[0] == s.PotionSlots[0] && run.Working.PotionSlots[1]!.Key == Potion("ENERGY_POTION"), "Replacement picked a different slot");
        Require(run.Working.PotionSlots[1]!.Id == run.Rewards[0].ContentInstanceId, "Potion identity changed on Procure");
        Send(run, new TakeReward(run.Rewards[1].Id));
        Require(run.Working.PotionSlots.Length == 4 && run.Working.PotionSlots.Skip(2).All(p => p == null), "PotionBelt lost inventory");
        Send(run, new DiscardPotion(0, s.PotionSlots[0]!.Id));
        Send(run, new DiscardPotion(0, s.PotionSlots[0]!.Id), PredictorInputResult.Invalid);
        Send(run, new LeaveRewards());
        var loaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), Context.CatalogFingerprint, Context.UnlockFingerprint);
        Require(loaded.Working.PotionSlots.SequenceEqual(run.Working.PotionSlots), "Potion capacities/instances did not persist");
        var sozu = Open(State("SOZU"), Item(PredictorRewardKind.Potion, Potion("ENERGY_POTION")));
        Send(sozu, new TakeReward(sozu.Rewards[0].Id), PredictorInputResult.NotAllowed);
        Require(sozu.Rewards[0].Status == PredictorRewardStatus.Taken && sozu.Working.PotionSlots.All(p => p == null), "NotAllowed is not Invalid/TooFull");
        Send(sozu, new LeaveRewards());
    }

    private sealed class PoolPotion : PotionModel
    {
        internal MegaCrit.Sts2.Core.Entities.Potions.PotionRarity TestRarity;
        public override MegaCrit.Sts2.Core.Entities.Potions.PotionRarity Rarity => TestRarity;
        public override MegaCrit.Sts2.Core.Entities.Potions.PotionUsage Usage => MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.AnyTime;
        public override MegaCrit.Sts2.Core.Entities.Cards.TargetType TargetType => MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyPlayer;
        public override bool IsMock => true;
    }
    private static void EntropicUse()
    {
        var pool = ImmutableArray.Create(new PredictorPotionOption(Potion("STRENGTH_POTION"), PredictorPotionRarity.Common),
            new(Potion("ENERGY_POTION"), PredictorPotionRarity.Common), new(Potion("DEXTERITY_POTION"), PredictorPotionRarity.Uncommon),
            new(Potion("ENTROPIC_BREW"), PredictorPotionRarity.Rare));
        var models = pool.Select(p =>
        {
            var model = (PoolPotion)RuntimeHelpers.GetUninitializedObject(typeof(PoolPotion));
            typeof(AbstractModel).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(model, new ModelId(p.Key.Category, p.Key.Entry));
            model.TestRarity = Enum.Parse<MegaCrit.Sts2.Core.Entities.Potions.PotionRarity>(p.Rarity.ToString());
            return model;
        }).ToArray();
        var nativeFactory = typeof(MegaCrit.Sts2.Core.Factories.PotionFactory).GetMethod("CreateRandomPotions", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool duplicateObserved = false;
        for (int skip = 0; skip < 24; skip++)
        foreach (bool sozu in new[] { false, true })
        {
            var state = State(sozu ? ["SOZU"] : []);
            var native = new RunRngSet(Context.Seed).CombatPotionGeneration;
            var rng = state.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration).Restore();
            for (int i = 0; i < skip; i++) { native.NextFloat(); rng.NextFloat(); }
            state = state.WithRng(PredictorStream.CombatPotionGeneration, rng);
            long id = state.NextInstanceId;
            state = state with { PotionSlots = [new(id, Potion("ENTROPIC_BREW")), null, null], NextInstanceId = id + 1 };
            var run = new PredictorRun(Context with { PotionPool = pool }, state);
            run.EnterRewardNode(new(1, 3, "1:3"), []);
            var expected = new List<string>();
            for (int i = 0; i < (sozu ? 1 : 3); i++)
            {
                var result = (IEnumerable<PotionModel>)nativeFactory.Invoke(null, [models, 1, native])!;
                expected.Add(result.Single().Id.Entry);
            }
            var rewardsBefore = run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards);
            Send(run, new UseOutOfCombatPotion(0, id));
            CheckRng(run.Working.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration), native);
            Require(run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards) == rewardsBefore, "Entropic borrowed Rewards RNG");
            if (sozu) Require(run.Working.PotionSlots.All(p => p == null), "Sozu produced inventory despite failed Procure");
            else Require(run.Working.PotionSlots.Select(p => p!.Key.Entry).SequenceEqual(expected), "Entropic native sequential potion generation");
            Send(run, new LeaveRewards());
            var recorded = run.Checkpoints[^1].CompletedNode!.Inputs[0];
            Require(recorded.GeneratedPotions.Select(p => p.Key.Entry).SequenceEqual(expected), "Generated-but-not-procured result lost");
            duplicateObserved |= expected.Distinct().Count() < expected.Count;
            var loaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), Context.CatalogFingerprint, Context.UnlockFingerprint);
            Require(loaded.Context.PotionPool.SequenceEqual(pool) && loaded.Working.PotionSlots.SequenceEqual(run.Working.PotionSlots), "Potion source/working state roundtrip");
        }
        Require(duplicateObserved, "No repeated identity case exercised");
    }

    private readonly record struct SortCard(PredictorCard Card) : IComparable<SortCard>
    {
        public int CompareTo(SortCard other)
        {
            int key = new ModelId(Card.Key.Category, Card.Key.Entry).CompareTo(new ModelId(other.Card.Key.Category, other.Card.Key.Entry));
            return key != 0 ? key : Card.UpgradeLevel.CompareTo(other.Card.UpgradeLevel);
        }
    }
    private static void RewardOrderAndNestedChoice()
    {
        for (int order = 0; order < 2; order++)
        {
            var run = Open(State(), Item(PredictorRewardKind.Relic, Relic("WHETSTONE")), CardOffer());
            if (order == 1) TakeCard(run, 1);
            var before = run.Working;
            var native = new Rng(new RunRngSet(Context.Seed).Niche.ToSerializable());
            var candidates = before.Deck.Select(before.Card).Where(c => c.Type == EffectCardType.Attack).Select(c => new SortCard(c)).ToList();
            candidates.StableShuffle(native);
            Send(run, new TakeReward(run.Rewards[0].Id));
            Require(run.Working.Cards.Where(c => c.UpgradeLevel == 1).Select(c => c.Id).Order().SequenceEqual(candidates.Take(2).Select(c => c.Card.Id).Order()), "Whetstone selected wrong master instances");
            CheckRng(run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche), native);
            if (order == 0) TakeCard(run, 1);
            Require(run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls == 3 + order, "Card/Whetstone order did not affect candidate RNG consumption");
            Send(run, new LeaveRewards());
        }
        var cage = Open(State(), Item(PredictorRewardKind.Relic, Relic("EMPTY_CAGE"), PredictorRewardOrigin.BonesInternal));
        Send(cage, new LeaveRewards(), PredictorInputResult.Invalid);
        long rewardId = cage.Rewards[0].Id;
        Send(cage, new TakeReward(rewardId));
        Require(cage.Working.Relics.Last().Id == cage.Rewards[0].ContentInstanceId, "Relic identity changed on Obtain");
        Require(cage.Phase == PredictorPhase.ObtainDeckCards && cage.Selection.ParentRewardId == rewardId && cage.Selection.RequiredCardCount == 2, "Nested parent/choice not projected");
        Require(cage.Working.Relics.Last().Key == Relic("EMPTY_CAGE") && cage.Rewards[0].Status == PredictorRewardStatus.Open, "Relic obtain must precede nested completion");
        Send(cage, new LeaveRewards(), PredictorInputResult.Invalid);
        long a = cage.Working.Deck[0], b = cage.Working.Deck[1];
        Send(cage, new SelectRemovalCards([a, a]), PredictorInputResult.Invalid);
        Send(cage, new SelectRemovalCards([a, b]));
        Require(cage.Working.Deck.Length == 3 && cage.Working.Card(a).Removed && cage.Working.Card(b).Removed, "Equal-name instance selection/removal lifetime");
        Send(cage, new LeaveRewards());
        var optional = Open(State(), Item(PredictorRewardKind.Relic, Relic("EMPTY_CAGE"), PredictorRewardOrigin.SmallCapsule));
        Send(optional, new LeaveRewards());
        Require(optional.Rewards[0].Status == PredictorRewardStatus.Skipped && optional.Working.Relics.IsEmpty, "SmallCapsule inherited forced obtain");
        var saved = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(optional), Context.CatalogFingerprint, Context.UnlockFingerprint);
        Require(saved.Checkpoints[^1].CompletedNode!.Rewards[0].Key == Relic("EMPTY_CAGE") &&
            saved.Checkpoints[^1].CompletedNode!.Rewards[0].Status == PredictorRewardStatus.Skipped, "Completed result identity/disposition lost on reload");
    }
    private static void TakeCard(PredictorRun run, int index)
    {
        var reward = run.Rewards[index];
        Send(run, new EnterCardSelection(reward.Id));
        Send(run, new TakeReward(reward.Id, reward.Cards[0].Id));
    }

    private static void CardLifecycle()
    {
        var run = Open(State(), CardOffer(), CardOffer());
        long id = run.Rewards[0].Id;
        Send(run, new TakeReward(id, run.Rewards[0].Cards[0].Id), PredictorInputResult.Invalid);
        Send(run, new EnterCardSelection(id));
        Send(run, new EnterCardSelection(run.Rewards[1].Id), PredictorInputResult.Invalid);
        Send(run, new LeaveRewards(), PredictorInputResult.Invalid);
        Send(run, new SkipCardReward());
        Require(run.Rewards[0].Status == PredictorRewardStatus.Open && !run.Rewards[0].RelicSubscriptionActive, "Skip incorrectly completed/resubscribed card reward");
        Send(run, new EnterCardSelection(id));
        Require(!run.Rewards[0].RelicSubscriptionActive, "Re-enter reinstated vanilla removed subscription");
        Send(run, new ExitCardSelection());
        TakeCard(run, 1);
        Send(run, new LeaveRewards());
        Require(run.Rewards[0].Status == PredictorRewardStatus.Skipped && run.Rewards[1].Status == PredictorRewardStatus.Taken, "Final card reward disposition");
        var manual = Open(State(), CardOffer() with { ManuallySetCards = true });
        Require(!manual.Rewards[0].RelicSubscriptionActive, "Fixed-card constructor invented RelicObtained subscription");
        Send(manual, new EnterCardSelection(manual.Rewards[0].Id));
        Send(manual, new ExitCardSelection());
        Send(manual, new LeaveRewards());
    }

    private static void CheckpointsAndCancellation()
    {
        var initial = State();
        var run = Open(initial, Item(PredictorRewardKind.Gold) with { GoldAmount = 10 });
        Send(run, new CorrectGoldBalance(1000));
        var old = run.Request!;
        Throws<InvalidOperationException>(() => PredictorRunStore.Serialize(run), "Direct test entry cannot silently lose active choices");
        string unfinished = PredictorRunStore.Serialize(new PredictorRun(Context, initial));
        var loaded = PredictorRunStore.Deserialize(unfinished, Context.CatalogFingerprint, Context.UnlockFingerprint);
        Require(loaded.Phase == PredictorPhase.Complete && loaded.Working.GoldBudget == 99 && loaded.Rewards.IsEmpty, "Disk serialized unfinished transaction");
        loaded.EnterRewardNode(new(1, 3, "1:3"), [Item(PredictorRewardKind.Gold) with { GoldAmount = 10 }]);
        Require(loaded.Submit(old, new CorrectGoldBalance(3)) == PredictorInputResult.Stale, "Reload accepted old session button");
        Send(run, new LeaveRewards());
        run.EnterRewardNode(new(1, 4, "1:4"), []);
        Send(run, new CorrectGoldBalance(1500));
        Send(run, new LeaveRewards());
        Require(run.Checkpoints.Length == 3, "Node did not commit exactly once");
        run.Rewind(0);
        Require(run.Working.GoldBudget == 99 && run.Checkpoints.Length == 1 && run.Revision == 1, "Rewind did not discard clicked node/downstream");
        run.EnterRewardNode(new(1, 3, "1:3"), []);
        Require(run.Submit(old, new CorrectGoldBalance(3)) == PredictorInputResult.Stale, "Rewind accepted old revision");
        var state = run.Working;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Throws<OperationCanceledException>(() => run.Submit(run.Request!, new CorrectGoldBalance(1), cancel.Token), "Cancel accepted input");
        Require(ReferenceEquals(state, run.Working), "Cancelled input mutated state");
        run.RestartCurrentNode();
        Throws<OperationCanceledException>(() => run.EnterRewardNode(new(1, 3, "1:3"), [], cancel.Token), "Cancelled node entry");
        Require(ReferenceEquals(initial, run.Working), "Cancelled/restarted node changed entry");
        Throws<NotImplementedException>(() => run.EnterRewardNode(new(1, 3, "1:3"),
            [Item(PredictorRewardKind.Gold) with { PopulateGold = true, GoldMinimum = 1, GoldMaximum = 7 },
                CardOffer() with { Cards = [Card() with { Enchantment = new ModelKey("ENCHANTMENT", "UNKNOWN_TEST_SOURCE") }] }]), "Unknown creation hook silently passed");
        Require(ReferenceEquals(initial, run.Working) && run.Phase == PredictorPhase.Complete, "Failed preparation published partial RNG");
        Throws<InvalidDataException>(() => PredictorRunStore.Deserialize(unfinished, "different-catalog", Context.UnlockFingerprint), "Catalog mismatch accepted");
        var futureSchema = System.Text.Json.Nodes.JsonNode.Parse(unfinished)!;
        futureSchema["Schema"] = 99;
        Throws<InvalidDataException>(() => PredictorRunStore.Deserialize(futureSchema.ToJsonString(), Context.CatalogFingerprint, Context.UnlockFingerprint), "Unknown schema accepted");
        var extraField = System.Text.Json.Nodes.JsonNode.Parse(unfinished)!;
        extraField["Extra"] = 1;
        Throws<JsonException>(() => PredictorRunStore.Deserialize(extraField.ToJsonString(), Context.CatalogFingerprint, Context.UnlockFingerprint), "Unknown field silently lost");
        Throws<InvalidDataException>(() => (initial with { NextInstanceId = 1 }).Validate(), "Corrupt allocator accepted");
        Throws<InvalidDataException>(() => (initial with { Deck = [initial.Deck[0], initial.Deck[0]] }).Validate(), "Duplicate master ID accepted");
        var bag = new PredictorRelicBag(true, [new(Relic("WHETSTONE"), "Common")], [new("Common", [])], [Relic("WAR_PAINT")]);
        var bagRun = new PredictorRun(Context, initial with { SharedBag = bag });
        var restoredBag = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(bagRun), Context.CatalogFingerprint, Context.UnlockFingerprint).Working.SharedBag;
        Require(restoredBag.RefreshAllowed && restoredBag.Original.SequenceEqual(bag.Original) &&
            restoredBag.MultiplayerFallback.SequenceEqual(bag.MultiplayerFallback), "RT2 checkpoint lost refresh/original/fallback like vanilla save projection");
        Require(restoredBag.Pull("Common", false, _ => true, _ => true).Key == Relic("WHETSTONE"), "Bag no longer refreshes after checkpoint reload");
        string path = Path.Combine(Path.GetTempPath(), "rt2-runtime-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            PredictorRunStore.Save(path, run);
            PredictorRunStore.Save(path, run);
            Require(PredictorRunStore.Load(path, Context.CatalogFingerprint, Context.UnlockFingerprint).Working.Deck.SequenceEqual(initial.Deck), "Atomic boundary file roundtrip");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
