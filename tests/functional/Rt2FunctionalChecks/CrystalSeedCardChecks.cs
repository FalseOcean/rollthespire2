using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Tests.FastSearch;

internal static class CrystalSeedCardChecks
{
    private const RngSemanticProfileId Profile = RngSemanticProfileId.Modern113;

    internal static int Run()
    {
        CheckSeparateRewardCopies();
        CheckOffsetOnlyReroll();
        CheckGeneratedBoards();
        if (Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") == "1") CheckNativeKernel();
        Console.WriteLine("CRYSTAL_SEED_CARDS detached same-path acquisitions, repeated menus, ordered rerolls and cutoff PASS");
        return 0;
    }

    private static void CheckSeparateRewardCopies()
    {
        var (context, _) = CrystalSeedGoalChecks.CreateReplayFixture();
        var pool = context.Catalog!.CharacterCards;
        var rares = pool.Where(row => row.Rarity == EffectCardRarity.Rare).Select(row => row.CardKey).ToArray();
        var single = new PredictorCrystalBoard([Tile(PredictorCrystalItemKind.RareCard, 4, 4)], true);
        var multiple = single with { Items = [single.Items[0] with { Subscriptions = 2 }] };
        foreach (var second in new[] { rares[0], rares[1] })
        {
            var targets = CrystalSphereSeedCardTargets.Create(pool, [rares[0], second])!;
            var impossible = new CrystalSphereSeedSolver(single, new(0), 3, default, default, 24_000, targets, true).Solve();
            Check(impossible.Verdict == CrystalSphereSeedVerdict.NoMatch, "One card menu satisfied two picks");
            var possible = new CrystalSphereSeedSolver(multiple, new(0), 3, default, default, 24_000, targets).Solve();
            Check(possible.Witness?.CardTakes.Select(take => take.SubscriptionIndex).Order().SequenceEqual([0, 1]) == true,
                "Two subscriptions on one tile did not produce two distinct takes");
        }
    }

    private static void CheckOffsetOnlyReroll()
    {
        var (context, _) = CrystalSeedGoalChecks.CreateReplayFixture(12);
        var pool = context.Catalog!.CharacterCards;
        var rares = pool.Where(row => row.Rarity == EffectCardRarity.Rare).Select(row => row.CardKey).ToArray();
        var board = new PredictorCrystalBoard([Tile(PredictorCrystalItemKind.RareCard, 4, 4),
            Tile(PredictorCrystalItemKind.CommonCard, 2, 4)], true);
        var fixture = Enumerable.Range(0, 25).Select(seed => new Xoshiro256StarStar((ulong)seed))
            .Select(rng => (Rng: rng, Card: NativeMenu(rares, rng, 18).FirstOrDefault(candidate => new[] { 0, 6, 12 }
                .All(offset => !NativeMenu(rares, rng, offset).Contains(candidate)))))
            .First(fixture => fixture.Card.IsValid);
        var (rng, card) = fixture;
        var targets = CrystalSphereSeedCardTargets.Create(pool, [card])!;
        var result = new CrystalSphereSeedSolver(board, rng, 3, default, default, 24_000, targets, true).Solve();
        Check(result.Witness?.CardRerolls.Select(reroll => reroll.ItemIndex).SequenceEqual([1, 0]) == true &&
            result.Witness.CardTakes.Single() == new CrystalSphereSeedCardTake(0, 0, card),
            $"An unrequested Common reward did not advance the same event stream before the Rare reroll ({result.Verdict}/{result.Reason})");
        Check(new CrystalSphereSeedSolver(board, rng, 3, default, default, 0, targets, true).Solve().Verdict ==
            CrystalSphereSeedVerdict.Unknown, "A cutoff certified a Crystal identity negative");
        Check(new CrystalSphereSeedSolver(board, rng, 3, default, default, 24_000, targets).Solve().Verdict ==
            CrystalSphereSeedVerdict.NoMatch, "An absent ordinary offer was reported obtainable under the no-reroll premise");
    }

    private static void CheckGeneratedBoards()
    {
        var (sourceContext, initialState) = CrystalSeedGoalChecks.CreateReplayFixture(12);
        var allCards = sourceContext.Catalog!.CharacterCards;
        var ownPool = allCards.Where(card => card.PoolOrder % 12 < 6).ToImmutableArray();
        var otherPool = allCards.Where(card => card.PoolOrder % 12 is >= 6 and < 9).ToImmutableArray();
        var colorless = allCards.Where(card => card.PoolOrder % 12 >= 9).ToImmutableArray();
        var unlocked = otherPool.AddRange(ownPool);
        var context = sourceContext with { Catalog = new(ownPool, colorless)
            { AllCharacterCards = unlocked, CardDefinitions = sourceContext.Catalog.CardDefinitions } };
        var counters = new int[Enum.GetValues<CrystalSphereSeedVerdict>().Length];
        long searchTicks = 0;
        for (ulong root = 0; root < 32; root++)
        {
            int slot = (int)(root % 2);
            int divinations = root % 4 < 2 ? 3 : 6;
            bool modified = root % 2 == 1;
            var state = modified ? initialState with
            {
                Relics = [new(1, new("RELIC", "DINGY_RUG"), 1), new(2, new("RELIC", "PRISMATIC_GEM"), 1),
                    new(3, new("RELIC", "DRIFTWOOD"), 1)],
                NextInstanceId = 4
            } : initialState;
            var pool = modified ? unlocked.AddRange(colorless) : ownPool;
            var requested = new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare }
                .Select(rarity => pool.Last(card => card.Rarity == rarity).CardKey).ToImmutableArray();
            long started = Stopwatch.GetTimestamp();
            var result = CrystalSphereSeedSearch.EvaluateCards(root, slot, Profile, divinations, pool,
                requested, driftwood: modified);
            searchTicks += Stopwatch.GetTimestamp() - started;
            counters[(int)result.Verdict]++;
            if (result.Witness is not { } witness) continue;
            Check(witness.CardTakes.Select(take => take.Card).ToHashSet().SetEquals(requested), "Named witness lost a requested card");
            var afterMenu = PredictorStreamState.Capture(PredictorStream.Rewards, CrystalSeedGoalChecks.EventRng(root, slot));
            var source = PredictorRun.CreateCrystalBranch(context, state, afterMenu, divinations == 6, 51);
            VerifyReplay(source, witness);
        }
        Check(counters[(int)CrystalSphereSeedVerdict.Match] > 0, "Missing managed replay fixture witnesses");
        Console.WriteLine($"CRYSTAL_SEED_CARD_SAMPLE 32 roots, 3/6 clicks, paired uncommon/rare: match={counters[0]} noMatch={counters[1]} unknown={counters[2]} solveMs={searchTicks * 1000d / Stopwatch.Frequency:F1}");
    }

    private static void VerifyReplay(PredictorCrystalSnapshot source, CrystalSphereSeedWitness witness)
    {
        var run = PredictorCrystalExplorer.Replay(source, witness.Steps);
        var expected = witness.Rewards.Where(reward => reward.Kind.StartsWith("CARD_", StringComparison.Ordinal))
            .SelectMany(reward => Enumerable.Range(0, reward.Copies).Select(copy => (reward.ItemIndex, Copy: copy))).ToArray();
        var cardRewards = run.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
            .OrderBy(reward => reward.SubscriptionOrder).ToArray();
        Check(expected.Length == cardRewards.Length, "Card menu subscriptions differ from detached replay");
        var rewardIds = expected.Zip(cardRewards).ToDictionary(pair => pair.First, pair => pair.Second.Id);
        foreach (var reroll in witness.CardRerolls)
        {
            long id = rewardIds[(reroll.ItemIndex, reroll.SubscriptionIndex)];
            Check(run.Submit(run.Request!, new EnterCardSelection(id)) == PredictorInputResult.Accepted &&
                run.Submit(run.Request!, new RerollCardReward(id)) == PredictorInputResult.Accepted &&
                run.Submit(run.Request!, new ExitCardSelection()) == PredictorInputResult.Accepted,
                "The ordered reroll witness could not be applied to detached replay");
        }
        foreach (var take in witness.CardTakes)
        {
            long id = rewardIds[(take.ItemIndex, take.SubscriptionIndex)];
            var reward = run.Rewards.Single(reward => reward.Id == id);
            var card = reward.Cards.SingleOrDefault(candidate => candidate.Key == take.Card);
            Check(card != null && run.Submit(run.Request!, new EnterCardSelection(id)) == PredictorInputResult.Accepted &&
                run.Submit(run.Request!, new TakeReward(id, card.Id)) == PredictorInputResult.Accepted &&
                run.Working.Deck.Select(run.Working.Card).Any(candidate => candidate.Key == take.Card),
                "The card identity did not make it into the detached replay's deck");
        }
    }

    private static void CheckNativeKernel()
    {
        NativeWorkbenchCheckEnvironment.Initialize("CrystalSeedCardKernel");
        var allowsCard = typeof(CardModel).GetMethod("IsAllowedInRun")
            ?? throw new NotSupportedException("The Crystal card kernel check requires the 0.113 game assembly.");
        var isAllowedInRun = allowsCard.CreateDelegate<Func<CardModel, IRunState, bool>>();
        var manager = RunManager.Instance;
        const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", instance)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", instance)!;
        var priorState = stateProperty.GetValue(manager);
        var priorAscension = ascensionProperty.GetValue(manager);
        int comparisons = 0;
        TestMode.IsOn = true;
        try
        {
            foreach (int act in new[] { 1, 2 })
            foreach (string[] relics in new string[][]
            {
                [], ["DINGY_RUG"], ["PRISMATIC_GEM"], ["DINGY_RUG", "DRIFTWOOD", "PRISMATIC_GEM"],
                ["PRISMATIC_GEM", "DRIFTWOOD", "DINGY_RUG"]
            })
            {
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                var run = RunState.CreateForTest([player], seed: "000000000000", ascensionLevel: 10);
                run.CurrentActIndex = act;
                stateProperty.SetValue(manager, run);
                ascensionProperty.SetValue(manager, NativeGameFixture.StandardAscension(10));
                run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<CrystalSphere>().Id);
                foreach (var relic in relics)
                    player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC", relic)).ToMutable(), silent: true);

                var pools = (relics.Contains("PRISMATIC_GEM") ? player.UnlockState.CharacterCardPools : [])
                    .Append(player.Character.CardPool);
                if (relics.Contains("DINGY_RUG")) pools = pools.Append(ModelDb.CardPool<ColorlessCardPool>());
                var sourcePool = pools.Distinct().SelectMany(pool => pool.GetUnlockedCards(player.UnlockState,
                    CardMultiplayerConstraint.SingleplayerOnly)).Where(card => isAllowedInRun(card, run)).ToArray();

                foreach (var rarity in new[] { CardRarity.Uncommon, CardRarity.Rare })
                {
                    var cardPool = sourcePool.Where(card => card.Rarity == rarity).Select(Key).ToImmutableArray();
                    for (int offset = 0; offset < 12; offset++)
                    {
                        var rng = new Rng(489);
                        for (int n = 0; n < offset; n++) rng.NextFloat();
                        var model = new CrystalSphereSeedCardOffers(cardPool, [], new(489));
                        var reward = (CardReward)new CrystalSphereCardReward(rarity, player).ToReward(player, rng);
                        var rewards = new RewardsSet(player).WithCustomRewards([reward]);
                        rewards.GenerateWithoutOffering().GetAwaiter().GetResult();
                        Compare(reward.Cards, model.MenuAt(offset), rng, offset + 6);
                        Check(reward.CanReroll == relics.Contains("DRIFTWOOD"), "Native reward Driftwood premise disagreed");
                        if (reward.CanReroll)
                        {
                            reward.Reroll();
                            Check(!reward.CanReroll, "Native reward allowed a second Driftwood reroll");
                            Compare(reward.Cards, model.MenuAt(offset + 6), rng, offset + 12);
                        }
                        reward.OnSkipped();
                    }
                    if (relics.Length != 0) continue;
                    var smallPool = cardPool.Take(3).ToImmutableArray();
                    var smallRng = new Rng(489);
                    var small = new CardCreationOptions([player.Character.CardPool], CardCreationSource.Other,
                        CardRarityOddsType.Uniform, card => smallPool.Contains(Key(card)))
                        .WithRngOverride(smallRng).WithFlags(CardCreationFlags.IsCardReward);
                    var generated = CardFactory.CreateForReward(player, 3, small).Select(result => result.Card);
                    Compare(generated, new CrystalSphereSeedCardOffers(smallPool, [], new(489)).MenuAt(0), smallRng, 6);
                }
            }
        }
        finally
        {
            stateProperty.SetValue(manager, priorState);
            ascensionProperty.SetValue(manager, priorAscension);
            TestMode.IsOn = false;
        }
        Console.WriteLine($"CRYSTAL_SEED_CARD_NATIVE {comparisons} native CardFactory menus and exact RNG state PASS");

        void Compare(IEnumerable<CardModel> native, CrystalSphereSeedCardOffers.Menu expected, Rng rng, int calls)
        {
            var cursor = new Xoshiro256StarStar(489);
            cursor.Advance(calls);
            var actual = rng.ToSerializable();
            Check(native.Select(Key).SequenceEqual([expected.First, expected.Second, expected.Third]) &&
                actual.counter == calls && cursor.State == (actual.state0, actual.state1, actual.state2, actual.state3),
                "Crystal's native identity order or event stream differs from the fixed-rarity solver kernel");
            comparisons++;
        }

        static ModelKey Key(CardModel card) => new(card.Id.Category, card.Id.Entry);
    }

    private static PredictorCrystalBoardItem Tile(PredictorCrystalItemKind kind, int x, int y) =>
        new(kind, x, y, 2, 2, PredictorCrystalBoard.Rectangle(x, y, 2, 2), 1);

    private static IEnumerable<ModelKey> NativeMenu(IReadOnlyList<ModelKey> pool, Xoshiro256StarStar afterPlacement, int offset)
    {
        var rng = afterPlacement.Clone();
        rng.Advance(offset);
        var remaining = pool.ToList();
        for (int i = 0; i < 3; i++)
        {
            int index = rng.NextInt(remaining.Count);
            var card = remaining[index];
            remaining.RemoveAt(index);
            rng.NextFloat();
            yield return card;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
